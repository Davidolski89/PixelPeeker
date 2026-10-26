using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using FFmpegStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Desktop;

public partial class App
{
    private async Task<object> CheckPublishedSeekingAsync(MainWindow window,bool checkIdle)
    {
        var vm=window.ViewModel;var original=vm.DisplayedPreviewFrame!;
        var service=provider!.GetRequiredService<AccuratePreviewService>();var paths=provider!.GetRequiredService<AppPaths>();
        vm.PreviewSeconds=Math.Min(1,vm.PreviewMaximum*.25);await vm.WaitForPreviewAsync();
        if(vm.DisplayedPreviewFrame is null)throw new InvalidDataException(vm.PreviewStatus);
        var starts=service.Statistics.DecoderStarts;var files=Directory.GetFiles(paths.Cache,"*.png").Length;
        var times=new List<double>();var positions=new List<double>();
        for(int n=0;n<6;n++)
        {
            var watch=Stopwatch.StartNew();
            if(n<4)await vm.NextFrameCommand.ExecuteAsync(null);else await vm.PreviousFrameCommand.ExecuteAsync(null);
            watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);
            if(vm.DisplayedPreviewFrame is not {} frame||vm.PreviewLoading||vm.PreviewImage is not BitmapSource{IsFrozen:true})throw new InvalidDataException(vm.PreviewStatus);
            positions.Add(frame.Seconds);
        }
        if(service.Statistics.DecoderStarts!=starts||service.Statistics.BufferedBytes>AccuratePreviewService.MemoryLimit||Directory.GetFiles(paths.Cache,"*.png").Length!=files)
            throw new InvalidDataException("Published seeking did not reuse the in-memory decoder.");
        bool? idleReleased=null;
        if(checkIdle)
        {
            await Task.Delay(TimeSpan.FromSeconds(21));
            idleReleased=!service.Statistics.DecoderRunning;
            if(!idleReleased.Value||service.Statistics.BufferedBytes==0)throw new InvalidDataException("Idle decoder was not released with its cache retained.");
            await vm.PreviousFrameCommand.ExecuteAsync(null);await vm.NextFrameCommand.ExecuteAsync(null);
            if(vm.DisplayedPreviewFrame is null||service.Statistics.DecoderStarts!=starts)throw new InvalidDataException("Cached navigation after idle closure failed.");
        }
        var statistics=service.Statistics;
        vm.PreviewSeconds=original.Seconds;await vm.WaitForPreviewAsync();
        return new{StepMilliseconds=times,Positions=positions,NoPngFiles=true,DecoderReused=true,IdleDecoderReleased=idleReleased,Statistics=statistics};
    }
}
