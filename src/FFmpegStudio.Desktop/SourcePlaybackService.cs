using System.Globalization;
using System.IO;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using LibVLCSharp.Shared;

namespace FFmpegStudio.Desktop;

/// <summary>Native operations run off the WPF thread and are serialized. Callbacks only publish data.</summary>
public sealed class SourcePlaybackService(LoggingService log) : IPlaybackSession
{
    private readonly SemaphoreSlim gate=new(1);
    private LibVLC? engine;
    private LibVLCSharp.Shared.MediaPlayer? player;
    private Media? media;
    private long session;
    private Action? detach;
    private TaskCompletionSource? playing,paused;
    private bool disposed;
    public event Action<PlaybackSignal>? Changed;
    public string EngineVersion=>engine?.Version??"";
    public string EngineName=>EngineVersion.Length==0?"LibVLC":"LibVLC "+EngineVersion;
    public bool IsSurfaceReady=>player?.Hwnd!=IntPtr.Zero;
    public object? NativePlayer=>player;
    async Task IPlaybackSession.OpenAsync(long id,MediaInfo source,double seconds,int volume,bool muted,CancellationToken ct)
    { _=await OpenAsync(id,source,seconds,volume,muted,ct); }
    public string NativeDirectory{get;private set;}="";
    public bool HasInput=>media is not null;
    public async Task<LibVLCSharp.Shared.MediaPlayer> OpenAsync(long id,MediaInfo source,double seconds,int volume,bool muted,CancellationToken ct)
    {
        return await WorkAsync(()=>
        {
            if(disposed)throw new ObjectDisposedException(nameof(SourcePlaybackService));
            CloseInput();
            if(engine is null)
            {
                NativeDirectory=FindNativeDirectory();LibVLCSharp.Shared.Core.Initialize(NativeDirectory);
                engine=new LibVLC("--no-video-title-show","--no-osd","--no-snapshot-preview","--no-sub-autodetect-file","--quiet");
                engine.Log+=(_,e)=>{if(e.Level==LogLevel.Error)try{log.Write("VLC · "+e.Module+": "+e.Message);}catch(IOException){}};
                player=new(engine){EnableHardwareDecoding=true,EnableKeyInput=false,EnableMouseInput=false};
            }
            session=id;playing=new(TaskCreationOptions.RunContinuationsAsynchronously);paused=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var p=player!;
            var playingReady=playing;
            EventHandler<EventArgs> onPlaying=(_,_)=>{playingReady.TrySetResult();Changed?.Invoke(new(id,PlaybackSignalKind.Playing));};
            EventHandler<EventArgs> onPaused=(_,_)=>{if(session==id)paused?.TrySetResult();};
            EventHandler<MediaPlayerTimeChangedEventArgs> onTime=(_,e)=>Changed?.Invoke(new(id,PlaybackSignalKind.Time,Math.Max(0,e.Time/1000d)));
            EventHandler<EventArgs> onEnd=(_,_)=>Changed?.Invoke(new(id,PlaybackSignalKind.Ended));
            EventHandler<EventArgs> onError=(_,_)=>{playingReady.TrySetException(new InvalidOperationException(L.T("Die Eingabedatei konnte nicht wiedergegeben werden.")));Changed?.Invoke(new(id,PlaybackSignalKind.Failed));};
            EventHandler<MediaPlayerBufferingEventArgs> onBuffer=(_,e)=>Changed?.Invoke(new(id,PlaybackSignalKind.Buffering,e.Cache));
            p.Playing+=onPlaying;p.Paused+=onPaused;p.TimeChanged+=onTime;p.EndReached+=onEnd;p.EncounteredError+=onError;p.Buffering+=onBuffer;
            detach=()=>{p.Playing-=onPlaying;p.Paused-=onPaused;p.TimeChanged-=onTime;p.EndReached-=onEnd;p.EncounteredError-=onError;p.Buffering-=onBuffer;};
            media=new Media(engine,source.Path,FromType.FromPath);
            // D3D11 AV1 decoders can reject pictures below their minimum coded dimensions without a usable fallback.
            if(source.Video is {Codec:"av1"} video&&(video.Width<256||video.Height<128))
            {media.AddOption(":avcodec-hw=none");media.AddOption(":codec=dav1d,all");}
            media.AddOption(":input-fast-seek");media.AddOption(":file-caching=150");media.AddOption(":no-sub-autodetect-file");media.AddOption(":no-spu");
            if(seconds>0)media.AddOption(":start-time="+seconds.ToString("0.######",CultureInfo.InvariantCulture));
            p.Media=media;p.Volume=muted?0:Math.Clamp(volume,0,100);
            return p;
        },ct);
    }
    public async Task PlayAsync(long id,CancellationToken ct)
    {
        var ready=await WorkAsync(()=>
        {
            if(session!=id||player is null)throw new OperationCanceledException(ct);
            if(player.Hwnd==IntPtr.Zero)throw new InvalidOperationException(L.T("Die Wiedergabefläche ist noch nicht bereit."));
            if(!player.Play())throw new InvalidOperationException(L.T("Die Eingabedatei konnte nicht wiedergegeben werden."));
            return playing!.Task;
        },ct);
        await ready.WaitAsync(TimeSpan.FromSeconds(15),ct);
    }
    public Task SeekAsync(long id,double seconds,CancellationToken ct)=>WorkAsync(()=>
    {if(session==id&&player is not null)player.Time=(long)Math.Round(Math.Max(0,seconds)*1000);return true;},ct);
    public async Task<double> HoldAsync(long id,bool stop,CancellationToken ct)
    {
        var wait=await WorkAsync(()=>
        {
            if(session!=id||player is null)return Task.CompletedTask;
            if(stop)return Task.CompletedTask;
            if(player.IsPlaying){paused=new(TaskCreationOptions.RunContinuationsAsynchronously);player.SetPause(true);return paused.Task;}
            return Task.CompletedTask;
        },ct);
        await wait.WaitAsync(TimeSpan.FromSeconds(5),ct);
        return await WorkAsync(()=>
        {
            if(session!=id||player is null)return 0d;
            double seconds=Math.Max(0,player.Time/1000d);
            // Pausing an input that is still opening is not supported by VLC 3; stop it to prevent hidden audio later.
            if(stop||player.State!=VLCState.Paused)CloseInput();
            return seconds;
        },ct);
    }
    public Task StopAsync(long id)=>WorkAsync(()=>{if(session==id)CloseInput();return true;},CancellationToken.None);
    public Task SetAudioAsync(int volume,bool muted)=>WorkAsync(()=>{if(player is not null&&!disposed){player.Volume=muted?0:Math.Clamp(volume,0,100);}return true;},CancellationToken.None);
    private async Task<T> WorkAsync<T>(Func<T> action,CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try{return await Task.Run(()=>{ct.ThrowIfCancellationRequested();return action();},ct).ConfigureAwait(false);}
        finally{gate.Release();}
    }
    private void CloseInput()
    {
        detach?.Invoke();detach=null;playing?.TrySetCanceled();paused?.TrySetCanceled();
        player?.Stop();if(player is not null)player.Media=null;
        media?.Dispose();media=null;session=0;
    }
    public static string FindNativeDirectory()
    {
        var roots=new[]{AppContext.BaseDirectory}.Concat((AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries));
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path=Path.Combine(root,"libvlc","win-x64");
            if(File.Exists(Path.Combine(path,"libvlc.dll"))&&Directory.Exists(Path.Combine(path,"plugins")))return path;
        }
        throw new FileNotFoundException(L.T("Die mitgelieferte VLC-Wiedergabebibliothek wurde nicht gefunden."));
    }
    public async ValueTask DisposeAsync()
    {
        await WorkAsync(()=>{if(disposed)return true;CloseInput();player?.Dispose();player=null;engine?.Dispose();engine=null;disposed=true;return true;},CancellationToken.None);
    }
}
