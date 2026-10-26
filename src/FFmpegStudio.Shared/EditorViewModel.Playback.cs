using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public sealed partial class EditorViewModel
{
    private readonly HashSet<Task> playbackTasks = [];
    private CancellationTokenSource? playbackCancellation;
    private long playbackGeneration;
    [ObservableProperty] private bool playbackActive;
    [ObservableProperty] private bool playbackBusy;
    [ObservableProperty] private int playbackVolume = 70;
    [ObservableProperty] private bool playbackMuted;
    [ObservableProperty] private string playbackStatus = "";

    partial void OnPlaybackVolumeChanged(int value) => Track(playback.SetAudioAsync(value, PlaybackMuted));
    partial void OnPlaybackMutedChanged(bool value) => Track(playback.SetAudioAsync(PlaybackVolume, value));

    [RelayCommand]
    private Task PlayAsync() => Track(StartPlaybackAsync());

    private async Task StartPlaybackAsync()
    {
        if (SelectedMedia?.Info is not { } info || !File.Exists(info.Path))
        {
            StatusText = L.T("Für die Wiedergabe eine vorhandene Datei auswählen.");
            return;
        }
        var id = ++playbackGeneration;
        playbackCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        playbackCancellation = cancellation;
        PlaybackActive = true;
        PlaybackBusy = true;
        PlaybackStatus = playback.EngineName;
        try
        {
            if (PreparePlaybackSurface is not null) await PreparePlaybackSurface();
            await playback.OpenAsync(id, info, PreviewSeconds, PlaybackVolume, PlaybackMuted, cancellation.Token);
            for (var attempt = 0; !playback.IsSurfaceReady && attempt < 50; attempt++)
                await Task.Delay(20, cancellation.Token);
            await playback.PlayAsync(id, cancellation.Token);
            PlaybackBusy = false;
            PlaybackStatus = playback.EngineName;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (id == playbackGeneration)
            {
                await playback.StopAsync(id);
                PlaybackActive = false;
                PlaybackBusy = false;
                PlaybackStatus = e.Message;
                StatusText = e.Message;
                log.Write(L.T("Wiedergabe"), e);
            }
        }
    }

    [RelayCommand]
    private Task PauseAsync() => Track(PauseCoreAsync());

    private async Task PauseCoreAsync()
    {
        if (!PlaybackActive) return;
        var seconds = await playback.HoldAsync(playbackGeneration, false, CancellationToken.None);
        PreviewSeconds = seconds;
        PlaybackActive = false;
        PlaybackBusy = false;
    }

    [RelayCommand]
    private Task StopPlaybackAsync() => Track(StopCoreAsync());

    private async Task StopCoreAsync()
    {
        var id = playbackGeneration;
        playbackCancellation?.Cancel();
        await playback.StopAsync(id);
        PlaybackActive = false;
        PlaybackBusy = false;
    }

    [RelayCommand]
    private Task SeekPlaybackAsync(double seconds) => Track(playback.SeekAsync(playbackGeneration, seconds, CancellationToken.None));

    private Task Track(Task task)
    {
        playbackTasks.Add(task);
        _ = Forget();
        return task;
        async Task Forget()
        {
            try { await task; }
            catch (Exception e) { log.Write(L.T("Wiedergabe"), e); }
            finally { playbackTasks.Remove(task); }
        }
    }
}
