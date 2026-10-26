using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Desktop;

public sealed partial class MainViewModel
{
    private readonly SourcePlaybackService playback;
    private readonly HashSet<Task> playbackTasks=[];
    private CancellationTokenSource? playbackCancellation,seekCancellation;
    private long playbackGeneration;
    private bool playbackReady,playbackHasInput;
    private double? requestedPlaybackPosition;
    private DateTime ignorePlaybackTicksUntil;
    private Task currentPlayback=Task.CompletedTask;
    public event Action? PlaybackViewRequested;
    [ObservableProperty] private LibVLCSharp.Shared.MediaPlayer? playbackPlayer;
    [ObservableProperty] private bool playbackActive;
    [ObservableProperty] private bool playbackTransition;
    [ObservableProperty] private bool playbackBusy;
    [ObservableProperty] private int playbackVolume=70;
    [ObservableProperty] private bool playbackMuted;
    public Task WaitForPlaybackAsync()=>currentPlayback;
    partial void OnPlaybackActiveChanged(bool value)=>RefreshPlaybackCommands();
    partial void OnPlaybackTransitionChanged(bool value)=>RefreshPlaybackCommands();
    partial void OnPlaybackVolumeChanged(int value)=>TrackPlayback(playback.SetAudioAsync(value,PlaybackMuted));
    partial void OnPlaybackMutedChanged(bool value)=>TrackPlayback(playback.SetAudioAsync(PlaybackVolume,value));
    private void RefreshPlaybackCommands()
    {
        PlaySourceCommand.NotifyCanExecuteChanged();PauseSourceCommand.NotifyCanExecuteChanged();StopSourceCommand.NotifyCanExecuteChanged();
        SeekBackwardCommand.NotifyCanExecuteChanged();SeekForwardCommand.NotifyCanExecuteChanged();
        PreviousFrameCommand.NotifyCanExecuteChanged();NextFrameCommand.NotifyCanExecuteChanged();RefreshCuts();UpdatePreviewStatus();
    }
    private bool CanPlaySource()=>!lifetime.IsCancellationRequested&&!PlaybackActive&&!PlaybackTransition&&SelectedMedia is {} item&&File.Exists(item.Info.Path);
    private bool CanPauseSource()=>PlaybackActive&&!PlaybackTransition;
    private bool CanStopSource()=>!PlaybackTransition&&(PlaybackActive||playbackHasInput);
    private bool CanQuickSeek()=>PlaybackActive&&!PlaybackTransition;
    private Task TrackPlayback(Task task)
    {
        playbackTasks.Add(task);_=Forget();return currentPlayback=task;
        async Task Forget(){try{await task;}catch(Exception e){log.Write("Wiedergabe",e);}finally{playbackTasks.Remove(task);}}
    }
    [RelayCommand(CanExecute=nameof(CanPlaySource))]
    private Task PlaySourceAsync()=>TrackPlayback(StartPlaybackAsync());
    private async Task StartPlaybackAsync()
    {
        if(!CanPlaySource()||SelectedMedia is not {} item)return;
        var id=++playbackGeneration;playbackCancellation?.Cancel();
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);playbackCancellation=cancellation;
        ResetPreview();filteredPreview=false;playbackReady=false;requestedPlaybackPosition=null;
        PlaybackActive=true;PlaybackBusy=true;PlaybackViewRequested?.Invoke();
        try
        {
            var player=await playback.OpenAsync(id,item.Info,PreviewSeconds,PlaybackVolume,PlaybackMuted,cancellation.Token);
            if(id!=playbackGeneration)return;
            PlaybackPlayer=player;playbackHasInput=true;
            // VideoView must attach its HWND before LibVLC starts, otherwise VLC creates a separate window.
            for(var n=0;player.Hwnd==IntPtr.Zero&&n<100;n++)
            {await Dispatcher.Yield(DispatcherPriority.Loaded);await Task.Delay(20,cancellation.Token);}
            await playback.PlayAsync(id,cancellation.Token);
            if(id!=playbackGeneration||PlaybackTransition)return;
            playbackReady=true;PlaybackBusy=false;
            if(requestedPlaybackPosition is {} target)await playback.SeekAsync(id,target,cancellation.Token);
            RefreshPlaybackCommands();
        }
        catch(OperationCanceledException){}
        catch(Exception e)
        {
            if(id==playbackGeneration&&!PlaybackTransition)
            {
                await playback.StopAsync(id);playbackHasInput=false;PlaybackActive=false;PlaybackBusy=false;
                previewError=e.Message;UpdatePreviewStatus();log.Write("Wiedergabe",e);
            }
        }
        finally{if(ReferenceEquals(playbackCancellation,cancellation))playbackCancellation=null;cancellation.Dispose();}
    }
    [RelayCommand(CanExecute=nameof(CanPauseSource))] private Task PauseSourceAsync()=>TrackPlayback(HoldPlaybackAsync(false));
    [RelayCommand(CanExecute=nameof(CanStopSource))] private Task StopSourceAsync()=>TrackPlayback(HoldPlaybackAsync(true,PlaybackActive?null:PreviewSeconds));
    [RelayCommand(CanExecute=nameof(CanQuickSeek))] private void SeekBackward()=>PreviewSeconds=Math.Max(0,PreviewSeconds-10);
    [RelayCommand(CanExecute=nameof(CanQuickSeek))] private void SeekForward()=>PreviewSeconds=Math.Min(PreviewMaximum,PreviewSeconds+10);
    private async Task HoldPlaybackAsync(bool stop,double? exactTarget=null)
    {
        if(PlaybackTransition){if(exactTarget.HasValue)requestedPlaybackPosition=exactTarget;return;}
        var id=playbackGeneration;var item=SelectedMedia;
        PlaybackTransition=true;PlaybackBusy=true;playbackCancellation?.Cancel();seekCancellation?.Cancel();
        if(exactTarget.HasValue)requestedPlaybackPosition=exactTarget;
        var target=exactTarget??requestedPlaybackPosition??(!playbackReady?PreviewSeconds:null);
        try
        {
            double actual;
            try{actual=await playback.HoldAsync(id,stop,lifetime.Token);}
            catch(Exception e)when(!lifetime.IsCancellationRequested){log.Write("Wiedergabe anhalten",e);actual=PreviewSeconds;await playback.StopAsync(id);stop=true;}
            if(id!=playbackGeneration||!ReferenceEquals(item,SelectedMedia)||lifetime.IsCancellationRequested)return;
            // A slider change made during the native pause operation takes precedence over its older clock sample.
            target=requestedPlaybackPosition??target??actual;
            requestedPlaybackPosition=null;playbackReady=false;playbackHasInput=playback.HasInput;
            PlaybackActive=false;PlaybackTransition=false;PlaybackBusy=false;
            currentPreview=RequestPreviewAsync(Math.Clamp(target.Value,0,PreviewMaximum));await currentPreview;
        }
        catch(OperationCanceledException){}
        finally{if(id==playbackGeneration){PlaybackActive=false;PlaybackTransition=false;PlaybackBusy=false;RefreshPlaybackCommands();}}
    }
    private void RequestQuickSeek(double seconds)
    {
        requestedPlaybackPosition=Math.Clamp(seconds,0,PreviewMaximum);ignorePlaybackTicksUntil=DateTime.UtcNow.AddMilliseconds(750);
        seekCancellation?.Cancel();
        if(!playbackReady||PlaybackTransition)return;
        var id=playbackGeneration;var target=requestedPlaybackPosition.Value;
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);seekCancellation=cancellation;
        TrackPlayback(Seek());
        async Task Seek()
        {
            try
            {
                await Task.Delay(80,cancellation.Token);await playback.SeekAsync(id,target,cancellation.Token);
                await Task.Delay(750,cancellation.Token);
                if(id==playbackGeneration&&!PlaybackTransition)requestedPlaybackPosition=null;
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(id==playbackGeneration){previewError=e.Message;UpdatePreviewStatus();log.Write("Wiedergabe-Suche",e);}}
            finally{if(ReferenceEquals(seekCancellation,cancellation))seekCancellation=null;cancellation.Dispose();}
        }
    }
    private void PlaybackChanged(PlaybackSignal signal)=>Ui(()=>
    {
        if(signal.Session!=playbackGeneration||!PlaybackActive||PlaybackTransition||lifetime.IsCancellationRequested)return;
        switch(signal.Kind)
        {
            case PlaybackSignalKind.Time when playbackReady&&DateTime.UtcNow>=ignorePlaybackTicksUntil:
                requestedPlaybackPosition=null;settingPreviewPosition=true;
                try{PreviewSeconds=Math.Clamp(signal.Value,0,PreviewMaximum);}finally{settingPreviewPosition=false;}
                break;
            case PlaybackSignalKind.Buffering:PlaybackBusy=signal.Value<100;break;
            case PlaybackSignalKind.Ended:TrackPlayback(HoldPlaybackAsync(true,PreviewMaximum));break;
            case PlaybackSignalKind.Failed:
                TrackPlayback(Recover());break;
        }
        async Task Recover()
        {
            await HoldPlaybackAsync(true);
            if(signal.Session==playbackGeneration){previewError=L.T("Die Eingabedatei konnte nicht wiedergegeben werden.");UpdatePreviewStatus();}
        }
    });
    private void ResetPlayback()
    {
        var previous=playbackGeneration++;playbackCancellation?.Cancel();seekCancellation?.Cancel();
        playbackReady=false;playbackHasInput=false;requestedPlaybackPosition=null;
        PlaybackActive=false;PlaybackTransition=false;PlaybackBusy=false;TrackPlayback(playback.StopAsync(previous));RefreshPlaybackCommands();
    }
    private async Task ShutdownPlaybackAsync()
    {
        ResetPlayback();await Task.WhenAll(playbackTasks.ToArray());playback.Changed-=PlaybackChanged;
        // Detach the WPF host while the native player still exists.
        PlaybackPlayer=null;await Dispatcher.Yield(DispatcherPriority.DataBind);await playback.DisposeAsync();
    }
}
