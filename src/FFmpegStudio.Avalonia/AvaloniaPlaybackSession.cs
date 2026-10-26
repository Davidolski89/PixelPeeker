using System.Globalization;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using LibVLCSharp.Shared;

namespace FFmpegStudio.Avalonia;

/// <summary>
/// LibVLC playback for the Avalonia shell. Linux loads the system libvlc and does not bundle VLC.
/// LibVLCSharp.Avalonia is not used: that package depends on Avalonia 11.
/// </summary>
public sealed class AvaloniaPlaybackSession(LoggingService log) : IPlaybackSession
{
    private readonly SemaphoreSlim gate = new(1);
    private LibVLC? engine;
    private MediaPlayer? player;
    private Media? media;
    private long session;
    private Action? detach;
    private TaskCompletionSource? playing, paused;
    private IntPtr surface;
    private bool disposed;

    public event Action<PlaybackSignal>? Changed;
    public string EngineName => engine is null ? "LibVLC" : "LibVLC " + engine.Version;
    public bool HasInput => media is not null;
    public bool IsSurfaceReady => surface != IntPtr.Zero;
    public object? NativePlayer => player;

    public void AttachSurface(IntPtr handle)
    {
        surface = handle;
        if (player is null || disposed) return;
        if (OperatingSystem.IsWindows()) player.Hwnd = handle;
        else if (handle == IntPtr.Zero) player.XWindow = 0;
        else player.XWindow = unchecked((uint)handle.ToInt64());
    }

    public Task OpenAsync(long id, MediaInfo source, double seconds, int volume, bool muted, CancellationToken ct) =>
        WorkAsync(() =>
        {
            if (disposed) throw new ObjectDisposedException(nameof(AvaloniaPlaybackSession));
            CloseInput();
            EnsureEngine();
            session = id;
            playing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = player!;
            var playingReady = playing;
            EventHandler<EventArgs> onPlaying = (_, _) => { playingReady.TrySetResult(); Changed?.Invoke(new(id, PlaybackSignalKind.Playing)); };
            EventHandler<EventArgs> onPaused = (_, _) => { if (session == id) paused?.TrySetResult(); };
            EventHandler<MediaPlayerTimeChangedEventArgs> onTime = (_, e) => Changed?.Invoke(new(id, PlaybackSignalKind.Time, Math.Max(0, e.Time / 1000d)));
            EventHandler<EventArgs> onEnd = (_, _) => Changed?.Invoke(new(id, PlaybackSignalKind.Ended));
            EventHandler<EventArgs> onError = (_, _) =>
            {
                playingReady.TrySetException(new InvalidOperationException(L.T("Die Eingabedatei konnte nicht wiedergegeben werden.")));
                Changed?.Invoke(new(id, PlaybackSignalKind.Failed));
            };
            EventHandler<MediaPlayerBufferingEventArgs> onBuffer = (_, e) => Changed?.Invoke(new(id, PlaybackSignalKind.Buffering, e.Cache));
            current.Playing += onPlaying;
            current.Paused += onPaused;
            current.TimeChanged += onTime;
            current.EndReached += onEnd;
            current.EncounteredError += onError;
            current.Buffering += onBuffer;
            detach = () =>
            {
                current.Playing -= onPlaying;
                current.Paused -= onPaused;
                current.TimeChanged -= onTime;
                current.EndReached -= onEnd;
                current.EncounteredError -= onError;
                current.Buffering -= onBuffer;
            };
            media = new Media(engine!, source.Path, FromType.FromPath);
            if (source.Video is { Codec: "av1" } video && (video.Width < 256 || video.Height < 128))
            {
                media.AddOption(":avcodec-hw=none");
                media.AddOption(":codec=dav1d,all");
            }
            media.AddOption(":input-fast-seek");
            media.AddOption(":file-caching=150");
            media.AddOption(":no-sub-autodetect-file");
            media.AddOption(":no-spu");
            if (seconds > 0) media.AddOption(":start-time=" + seconds.ToString("0.######", CultureInfo.InvariantCulture));
            current.Media = media;
            current.Volume = muted ? 0 : Math.Clamp(volume, 0, 100);
            AttachSurface(surface);
        }, ct);

    public async Task PlayAsync(long id, CancellationToken ct)
    {
        var ready = await WorkAsync(() =>
        {
            if (session != id || player is null) throw new OperationCanceledException(ct);
            if (!IsSurfaceReady) throw new InvalidOperationException(L.T("Die Wiedergabefläche ist noch nicht bereit."));
            if (!player.Play()) throw new InvalidOperationException(L.T("Die Eingabedatei konnte nicht wiedergegeben werden."));
            return playing!.Task;
        }, ct);
        await ready.WaitAsync(TimeSpan.FromSeconds(15), ct);
    }

    public Task SeekAsync(long id, double seconds, CancellationToken ct) => WorkAsync(() =>
    {
        if (session == id && player is not null) player.Time = (long)Math.Round(Math.Max(0, seconds) * 1000);
    }, ct);

    public async Task<double> HoldAsync(long id, bool stop, CancellationToken ct)
    {
        var wait = await WorkAsync(() =>
        {
            if (session != id || player is null || stop || !player.IsPlaying) return Task.CompletedTask;
            paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
            player.SetPause(true);
            return paused.Task;
        }, ct);
        await wait.WaitAsync(TimeSpan.FromSeconds(5), ct);
        return await WorkAsync(() =>
        {
            if (session != id || player is null) return 0d;
            var seconds = Math.Max(0, player.Time / 1000d);
            if (stop || player.State != VLCState.Paused) CloseInput();
            return seconds;
        }, ct);
    }

    public Task StopAsync(long id) => WorkAsync(() => { if (session == id) CloseInput(); }, CancellationToken.None);
    public Task SetAudioAsync(int volume, bool muted) => WorkAsync(() =>
    {
        if (player is not null && !disposed) player.Volume = muted ? 0 : Math.Clamp(volume, 0, 100);
    }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await WorkAsync(() =>
        {
            if (disposed) return;
            CloseInput();
            player?.Dispose();
            player = null;
            engine?.Dispose();
            engine = null;
            disposed = true;
        }, CancellationToken.None);
    }

    private void EnsureEngine()
    {
        if (engine is not null) return;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            engine = new LibVLC("--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--no-sub-autodetect-file", "--quiet");
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or LibVLCSharp.Shared.VLCException)
        {
            throw new InvalidOperationException(L.T("Die Systembibliothek libvlc wurde nicht gefunden. Unter Linux wird eine VLC-Installation benötigt. PixelPeeker bündelt sie nicht."), e);
        }
        engine.Log += (_, entry) =>
        {
            if (entry.Level != LogLevel.Error) return;
            try { log.Write("VLC · " + entry.Module + ": " + entry.Message); }
            catch (IOException) { }
        };
        player = new MediaPlayer(engine) { EnableHardwareDecoding = true, EnableKeyInput = false, EnableMouseInput = false };
    }

    private async Task WorkAsync(Action action, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { await Task.Run(action, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task<T> WorkAsync<T>(Func<T> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(action, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private void CloseInput()
    {
        detach?.Invoke();
        detach = null;
        playing?.TrySetCanceled();
        paused?.TrySetCanceled();
        player?.Stop();
        if (player is not null) player.Media = null;
        media?.Dispose();
        media = null;
        session = 0;
    }
}
