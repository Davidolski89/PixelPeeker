using System.IO;
using FFmpegStudio.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Desktop;

public partial class App
{
    private async Task<object> CheckPublishedPlaybackAsync(MainWindow window,string output)
    {
        var vm=window.ViewModel;var original=vm.DisplayedPreviewFrame!;
        vm.PreviewSeconds=0;await vm.WaitForPreviewAsync();await vm.PlaySourceCommand.ExecuteAsync(null);
        if(!vm.PlaybackActive)throw new InvalidDataException(vm.PreviewStatus);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var player=vm.PlaybackPlayer!;using var descriptor=player.Media!;
        while(descriptor.Statistics.DisplayedPictures<1||player.Time<50)await Task.Delay(20,timeout.Token);
        var markersDisabled=!vm.MarkCutStartCommand.CanExecute(null)&&!vm.NextFrameCommand.CanExecute(null)&&vm.DisplayedPreviewFrame is null;
        var picture=Path.Combine(output,"playing-native.png");player.TakeSnapshot(0,picture,0,0);
        while(!File.Exists(picture))await Task.Delay(20,timeout.Token);
        vm.PreviewSeconds=vm.PreviewMaximum*.35;
        await vm.WaitForPlaybackAsync();
        var sourceClock=player.Time/1000d;var stats=descriptor.Statistics;
        await vm.PauseSourceCommand.ExecuteAsync(null);await vm.WaitForPreviewAsync();
        var paused=vm.DisplayedPreviewFrame??throw new InvalidDataException("No exact frame after playback pause.");
        if(vm.PlaybackActive||vm.PreviewLoading||!vm.MarkCutStartCommand.CanExecute(null))throw new InvalidDataException("Pause did not restore frame navigation.");
        await vm.NextFrameCommand.ExecuteAsync(null);var next=vm.DisplayedPreviewFrame;
        await vm.StopSourceCommand.ExecuteAsync(null);
        vm.PreviewSeconds=original.Seconds;await vm.WaitForPreviewAsync();
        var service=provider!.GetRequiredService<SourcePlaybackService>();
        return new{Engine=service.EngineVersion,service.NativeDirectory,MarkersDisabledDuringPlayback=markersDisabled,
            stats.DecodedVideo,stats.DisplayedPictures,stats.PlayedAudioBuffers,SourceClock=sourceClock,PausedFrame=paused,NextFrame=next,
            ExactNavigationRestored=vm.MarkCutStartCommand.CanExecute(null)&&!vm.PlaybackActive&&!vm.PreviewLoading};
    }
}
