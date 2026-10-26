using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Desktop;

public partial class App
{
    private async Task<object> CheckPublishedEncodeComparisonAsync(MainWindow window,string output)
    {
        var vm=window.ViewModel;var media=vm.SelectedMedia!.Info;var queue=provider!.GetRequiredService<QueueService>();var jobs=new List<EncodingJob>();
        foreach(var qp in new[]{40,50})
        {
            var options=EncodingOptions.ForMedia(media);options.Video=new(){Encoder="libx264",Preset="ultrafast",RateControl=RateControl.QP,Quality=qp,PixelFormat="yuv420p"};
            options.OutputDirectory=Path.Combine(output,"comparison-encodes");options.FileNamePattern="Comparison_QP"+qp;options.AutoAnalyze=false;
            foreach(var track in options.Audio){track.Action=TrackAction.Encodieren;track.Encoder="aac";}
            var job=await queue.AddAsync(media,options);jobs.Add(job);queue.Start(job);
        }
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
        while(jobs.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(30,timeout.Token);
        if(jobs.Any(j=>j.Status!=JobStatus.Abgeschlossen))throw new InvalidDataException("Published comparison encode failed: "+string.Join("; ",jobs.Select(j=>j.Error)));
        var comparison=vm.CompareEncodes;
        var workspace=(TabControl)window.FindName("MainPreviewTabs");var selectedTab=workspace.SelectedItem;
        var grid=(DataGrid)window.FindName("QueueGrid");var button=(Button)window.FindName("CompareEncodesButton");
        var startQueue=(Button)window.FindName("StartQueueButton");
        bool peekStyle=Equals(button.Content,"Peek")&&ReferenceEquals(button.Style,startQueue.Style)&&ReferenceEquals(button.Background,startQueue.Background)&&ReferenceEquals(button.Foreground,startQueue.Foreground);
        if(!peekStyle)throw new InvalidDataException("Peek button did not match the Start queue accent style.");
        grid.SelectedItems.Clear();grid.SelectedItems.Add(jobs[0]);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var singleWindow=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();await comparison.WaitAsync();
        bool single=singleWindow.WindowState==WindowState.Maximized&&!comparison.CompareTwoEncodes&&comparison.MatchReference&&comparison.ImageA is not null&&comparison.OriginalImage is not null&&comparison.ImageB is null&&comparison.PanelCount==2;
        if(!single)throw new InvalidDataException("Unified frame comparison did not load source and single encode.");
        var viewerControl=(EncodeComparisonControl)singleWindow.Content;var focusPane=(ComparisonImagePane)viewerControl.FindName("FocusPane");
        comparison.SideBySide=false;comparison.FocusIndex=0;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);singleWindow.UpdateLayout();
        var encodeViewport=new Rect(focusPane.TranslatePoint(new Point(),viewerControl),focusPane.RenderSize);var encodeSize=((Image)focusPane.Scroll.Content).RenderSize;
        comparison.FocusIndex=2;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);singleWindow.UpdateLayout();
        bool stableViewport=encodeViewport==new Rect(focusPane.TranslatePoint(new Point(),viewerControl),focusPane.RenderSize)&&encodeSize==((Image)focusPane.Scroll.Content).RenderSize;
        if(!stableViewport)throw new InvalidDataException("Selecting the matched reference moved or resized the comparison image.");
        comparison.FocusIndex=0;comparison.SideBySide=true;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var singleTask=comparison.WaitAsync();var singleImage=comparison.ImageA;
        bool held=comparison.BeginReferenceHold()&&ReferenceEquals(comparison.FocusImage,comparison.OriginalImage)&&ReferenceEquals(singleTask,comparison.WaitAsync());comparison.EndReferenceHold();
        if(!held||!ReferenceEquals(singleImage,comparison.ImageA))throw new InvalidDataException("Unified reference hold changed the loaded frames.");
        int samples=0;
        if(L.Language=="de")
        {
            await comparison.GenerateSamplesCommand.ExecuteAsync(null);samples=comparison.Samples.Count;
            if(samples!=5)throw new InvalidDataException("Unified comparison did not generate five samples.");
            comparison.SelectedSample=comparison.Samples[3];
            if(!ReferenceEquals(comparison.ImageA,comparison.Samples[3].ImageA))throw new InvalidDataException("Cached sample selection failed.");
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);singleWindow.UpdateLayout();
        var singleBitmap=new RenderTargetBitmap((int)singleWindow.ActualWidth,(int)singleWindow.ActualHeight,96,96,PixelFormats.Pbgra32);singleBitmap.Render(singleWindow);
        var singlePng=new PngBitmapEncoder();singlePng.Frames.Add(BitmapFrame.Create(singleBitmap));using(var stream=File.Create(Path.Combine(output,"frame-comparison.png")))singlePng.Save(stream);
        grid.SelectedItems.Clear();foreach(var job in jobs)grid.SelectedItems.Add(job);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var large=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();
        bool unified=ReferenceEquals(singleWindow,large)&&comparison.CompareTwoEncodes;
        if(!unified)throw new InvalidDataException("Frame and encode comparison did not reuse the same viewer.");
        bool directLarge=large.IsVisible&&large.WindowState==WindowState.Maximized&&ReferenceEquals(workspace.SelectedItem,selectedTab)&&window.FindName("EncodeComparisonTab") is null;
        if(!directLarge)throw new InvalidDataException("Comparison button did not open a maximized standalone comparison.");
        var modeSelector=(ComboBox)((EncodeComparisonControl)large.Content).FindName("ComparisonModeSelector");
        var choiceA=comparison.SelectedA;var choiceB=comparison.SelectedB;
        modeSelector.SelectedValue=EncodeComparisonMode.TwoEncodes;await comparison.WaitAsync();
        bool modes=modeSelector.Items.Count==3&&comparison.PanelCount==2&&comparison.ImageA is not null&&comparison.ImageB is not null&&comparison.OriginalImage is null&&!comparison.CanMatchReference;
        var noReferenceTask=comparison.WaitAsync();comparison.MatchReference=true;
        modes&=!comparison.MatchReference&&ReferenceEquals(noReferenceTask,comparison.WaitAsync());
        modeSelector.SelectedValue=EncodeComparisonMode.EncodeAndReference;await comparison.WaitAsync();
        modes&=comparison.PanelCount==2&&comparison.ImageA is not null&&comparison.ImageB is null&&comparison.OriginalImage is not null&&comparison.CanMatchReference;
        modeSelector.SelectedValue=EncodeComparisonMode.TwoEncodesAndReference;await comparison.WaitAsync();
        modes&=comparison.PanelCount==3&&comparison.ImageA is not null&&comparison.ImageB is not null&&comparison.OriginalImage is not null&&ReferenceEquals(choiceA,comparison.SelectedA)&&ReferenceEquals(choiceB,comparison.SelectedB);
        if(!modes)throw new InvalidDataException("Comparison mode selection lost a file or displayed the wrong images.");
        comparison.Position=Math.Min(1,(media.Duration??2)*.4);
        await comparison.WaitAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if(comparison.ImageA is null||comparison.ImageB is null||comparison.OriginalImage is null)throw new InvalidDataException("Published comparison did not load all frames: "+comparison.Status+" / "+comparison.MessageA+" / "+comparison.MessageB);
        var original=comparison.OriginalImage;var a=comparison.ImageA;var b=comparison.ImageB;var frames=comparison.LoadedFrames!;
        void Save(BitmapSource bitmap,string name){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));png.Save(stream);}
        Save(original,"comparison-original.png");Save(a,"comparison-a.png");Save(b,"comparison-b.png");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);large.UpdateLayout();
        void Capture(string name){var bitmap=new RenderTargetBitmap((int)large.ActualWidth,(int)large.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(large);Save(bitmap,name);}
        Capture("comparison.png");var loaded=comparison.WaitAsync();var position=comparison.Position;
        large.WindowState=WindowState.Minimized;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool reused=ReferenceEquals(window.OwnedWindows.OfType<EncodeComparisonWindow>().Single(),large)&&large.WindowState==WindowState.Maximized&&ReferenceEquals(loaded,comparison.WaitAsync())&&comparison.Position==position;
        if(!reused)throw new InvalidDataException("Comparison window was duplicated or reset.");
        comparison.FocusIndex=0;comparison.ToggleCommand.Execute(null);
        bool instant=ReferenceEquals(comparison.FocusImage,b)&&ReferenceEquals(loaded,comparison.WaitAsync())&&a.IsFrozen&&b.IsFrozen;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);large.UpdateLayout();Capture("comparison-ab.png");
        var (coverage,positionPreserved)=await CheckPublishedCoverageAsync(comparison,large,jobs,output);
        large.Close();comparison.SideBySide=true;
        return new{A=comparison.SelectedA!.Label,B=comparison.SelectedB!.Label,comparison.Position,ComparisonModesVerified=modes,AvailabilityVerified=coverage,SelectionPositionPreserved=positionPreserved,PeekButton=button.Content,PeekMatchesStartQueue=peekStyle,StableReferenceViewport=stableViewport,ThreeFramesLoaded=true,InstantToggle=instant,DirectLargeView=directLarge,ReusedWindow=reused,UnifiedComparison=unified,SingleEncodeComparison=single,ReferenceHold=held,SampleCount=samples,
            OriginalFrame=frames.Original!.Frame,FrameA=frames.A.Frame,FrameB=frames.B.Frame,SourceA=frames.A.SourceSeconds,SourceB=frames.B.SourceSeconds,
            OriginalWidth=original.PixelWidth,AWidth=a.PixelWidth,BWidth=b.PixelWidth,Jobs=jobs.Select(j=>new{j.OutputPath,j.Options.Video.Quality,j.Options.Video.RateControl}).ToArray()};
    }
    private async Task<(bool Availability,bool PositionPreserved)> CheckPublishedCoverageAsync(EncodeComparisonViewModel comparison,EncodeComparisonWindow window,List<EncodingJob> originals,string output)
    {
        var queue=provider!.GetRequiredService<QueueService>();var navigation=provider!.GetRequiredService<FrameNavigationService>();
        var jobs=new List<EncodingJob>();var position=comparison.Position;
        for(int i=0;i<2;i++)
        {
            var original=originals[i];var options=Json.Clone(original.Options);options.FileNamePattern="Coverage_"+i;
            options.Cuts.Add(new(await navigation.ResolveAsync(original.Tools,original.Source,i==0?.5:1.5),await navigation.ResolveAsync(original.Tools,original.Source,i==0?1:2)));
            if(i==1){options.ClipStart=.25;options.ClipDuration=2.5;}
            var job=await queue.AddAsync(original.Source,options);jobs.Add(job);queue.Start(job);
        }
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)))while(jobs.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(30,timeout.Token);
        if(jobs.Any(j=>j.Status!=JobStatus.Abgeschlossen))throw new InvalidDataException("Coverage encode failed: "+string.Join("; ",jobs.Select(j=>j.Error)));
        comparison.SelectJobs(jobs);comparison.SideBySide=true;comparison.Zoom=0;
        comparison.Position=.75;await comparison.WaitAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        var control=(EncodeComparisonControl)window.Content;var a=(ComparisonAvailabilityStrip)control.FindName("AvailabilityA");var b=(ComparisonAvailabilityStrip)control.FindName("AvailabilityB");
        bool verified=a.IsVisible&&b.IsVisible&&a.Intervals!.Count==3&&b.Intervals!.Count==5&&comparison.ImageA is null&&comparison.ImageB is not null&&comparison.OriginalImage is not null&&
            a.Intervals[1].Availability==ComparisonAvailability.Unavailable&&b.Intervals[2].Availability==ComparisonAvailability.Unavailable&&a.Position==comparison.Position&&b.Position==comparison.Position&&
            ((ComparisonImagePane)control.FindName("PaneA")).EmptyMessage==comparison.MessageA;
        var screenshot=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);screenshot.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(screenshot));using(var stream=File.Create(Path.Combine(output,"comparison-availability.png")))png.Save(stream);
        var selectorA=(ComboBox)control.FindName("EncodeASelector");var selectorB=(ComboBox)control.FindName("EncodeBSelector");
        EncodeChoice Choice(EncodingJob job)=>comparison.Choices.Single(c=>c.Input.Job?.Id==job.Id);
        var sourceFrame=comparison.LoadedFrames!.Anchor;var sourcePosition=comparison.Position;
        selectorA.SelectedItem=Choice(originals[0]);bool preserved=comparison.Position==sourcePosition;
        await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition&&comparison.LoadedFrames!.Anchor==sourceFrame&&comparison.ImageA is not null;
        selectorA.SelectedItem=Choice(jobs[1]);preserved&=comparison.Position==sourcePosition;await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition;
        selectorA.SelectedItem=Choice(jobs[0]);await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition&&comparison.ImageA is null;
        comparison.Position=1.75;await comparison.WaitAsync();verified&=comparison.ImageA is not null&&comparison.ImageB is null;
        sourcePosition=comparison.Position;
        selectorB.SelectedItem=Choice(originals[1]);preserved&=comparison.Position==sourcePosition;await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition&&comparison.ImageB is not null;
        selectorB.SelectedItem=Choice(jobs[1]);await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition&&comparison.ImageB is null;
        comparison.Position=2;await comparison.WaitAsync();verified&=comparison.ImageA is not null&&comparison.ImageB is not null;
        await AtomicFile.WriteJsonAsync(Path.Combine(output,"coverage.json"),new{Verified=verified,SelectionPositionPreserved=preserved,A=a.Intervals,B=b.Intervals});
        if(!verified)throw new InvalidDataException("Published availability tracks did not match independently cut A/B encodes.");
        sourcePosition=comparison.Position;comparison.SelectJobs(originals);preserved&=comparison.Position==sourcePosition;await comparison.WaitAsync();preserved&=comparison.Position==sourcePosition;
        if(!preserved)throw new InvalidDataException("Changing an encode from the same input source reset the comparison position.");
        comparison.Position=position;await comparison.WaitAsync();return (verified,preserved);
    }

}
