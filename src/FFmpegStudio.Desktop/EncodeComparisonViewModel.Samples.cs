using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Desktop;

public sealed record ComparisonSample(EncodeComparisonFrames Frames,BitmapImage? ImageA,BitmapImage? ImageB,BitmapImage? Reference,
    BitmapImage? Thumbnail=null,bool PixelsCached=true)
{
    public string Label=>$"{Frames.SourceSeconds:0.000} s";
    public long PixelBytes=>new[]{ImageA,ImageB,Reference}.Where(i=>i is not null).Sum(i=>(long)i!.PixelWidth*i.PixelHeight*((i.Format.BitsPerPixel+7)/8));
}

public sealed partial class EncodeComparisonViewModel
{
    public const long SampleMemoryLimit=192L*1024*1024;
    public ObservableCollection<ComparisonSample> Samples{get;}=[];
    public bool HasSamples=>Samples.Count>0;
    [ObservableProperty] private ComparisonSample? selectedSample;
    private bool selectingSample;
    private (int Focus,bool Side)? referenceHold;

    public bool BeginReferenceHold()
    {
        if(referenceHold is not null)return true;
        if(Busy||!OriginalVisible||OriginalImage is null)return false;
        referenceHold=(FocusIndex,SideBySide);FocusIndex=2;SideBySide=false;return true;
    }
    public void EndReferenceHold()
    {
        if(referenceHold is not {} previous)return;
        referenceHold=null;FocusIndex=previous.Focus;SideBySide=previous.Side;
    }
    private static ComparisonSample DecodeFrames(EncodeComparisonFrames result)=>new(result,
        result.A.Path is {} a?MainViewModel.LoadImage(a):null,
        result.B.Path is {} b?MainViewModel.LoadImage(b):null,
        result.Original?.Path is {} original?MainViewModel.LoadImage(original):null);
    private void DisplayFrames(ComparisonSample sample)
    {
        var result=sample.Frames;frames=result;ImageA=sample.ImageA;ImageB=sample.ImageB;OriginalImage=sample.Reference;
        MessageA=Describe(result.A,ImageA,result.SourceSeconds);MessageB=Describe(result.B,ImageB,result.SourceSeconds);
        OriginalMessage=result.Original is {} o?Describe(o,OriginalImage,result.SourceSeconds):OriginalVisible?L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."):"";
        settingPosition=true;try{Maximum=Math.Max(Maximum,result.SourceSeconds);Position=result.SourceSeconds;}finally{settingPosition=false;}
        Status=OriginalVisible&&Original is null?L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."):L.T("Vergleich geladen. Gleiche Quellzeit; tatsächliche Frame-Zeiten stehen unter den Bildern.");RefreshFocus();
    }
    private void DeselectSample()
    {selectingSample=true;try{SelectedSample=null;}finally{selectingSample=false;}}
    private void ClearSamples()
    {EndReferenceHold();DeselectSample();Samples.Clear();OnPropertyChanged(nameof(HasSamples));}
    partial void OnSelectedSampleChanged(ComparisonSample? value)
    {
        if(selectingSample||value is null)return;
        EndReferenceHold();
        if(!value.PixelsCached){settingPosition=true;try{Position=value.Frames.SourceSeconds;}finally{settingPosition=false;}Request();return;}
        generation++;cancellation?.Cancel();cancellation=null;Loading=false;DisplayFrames(value);RefreshCommands();
    }
    private bool CanGenerateSamples()=>CanLoad()&&!Busy;
    [RelayCommand(CanExecute=nameof(CanGenerateSamples))]
    private Task GenerateSamplesAsync()=>Track(GenerateSamplesCoreAsync());
    private async Task GenerateSamplesCoreAsync()
    {
        if(!CanGenerateSamples())return;
        var a=SelectedA!.Input;var b=CompareTwoEncodes?SelectedB!.Input:null;var original=Original;
        var include=OriginalVisible;var match=MatchReference&&CanMatchReference;var oa=OffsetA;var ob=OffsetB;
        var duration=a.Job is {} job?(job.Source.Duration is {} end?
            CutTimeline.Keep(job.Options).Sum(s=>Math.Max(0,Math.Min(s.End??end,end)-s.Start)):CutTimeline.Duration(job.Source,job.Options)):a.Media.Duration;
        if(duration is not >0){Status=L.T("Dauer unbekannt.");return;}
        var id=++generation;cancellation?.Cancel();ClearSamples();
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime);cancel.CancelAfter(TimeSpan.FromMinutes(2));cancellation=cancel;
        Loading=true;RefreshCommands();long retainedBytes=0;
        try
        {
            foreach(var fraction in new[]{.1,.3,.5,.7,.9})
            {
                Status=L.T("Vergleichsbilder werden geladen …")+$" {Samples.Count+1}/5";
                var position=a.Job is {} selectedJob?CutTimeline.SourceTime(selectedJob.Options,duration.Value*fraction):duration.Value*fraction+oa;
                var result=await service.LoadAsync(original,a,b,position,include,oa,ob,ct:cancel.Token,matchReference:match);
                cancel.Token.ThrowIfCancellationRequested();if(id!=generation)return;
                var sample=DecodeFrames(result);var file=result.A.Path??result.Original?.Path??result.B.Path;
                BitmapImage? thumbnail=null;
                if(file is not null)
                {
                    using var stream=File.OpenRead(file);thumbnail=new();thumbnail.BeginInit();thumbnail.CacheOption=BitmapCacheOption.OnLoad;
                    thumbnail.DecodePixelWidth=160;thumbnail.StreamSource=stream;thumbnail.EndInit();thumbnail.Freeze();
                }
                sample=sample with{Thumbnail=thumbnail};
                if(retainedBytes+sample.PixelBytes<=SampleMemoryLimit)retainedBytes+=sample.PixelBytes;
                else sample=sample with{ImageA=null,ImageB=null,Reference=null,PixelsCached=false};
                Samples.Add(sample);OnPropertyChanged(nameof(HasSamples));
            }
            if(id==generation&&Samples.Count>0)
            {
                var first=Samples[0];
                DisplayFrames(first.PixelsCached?first:DecodeFrames(first.Frames));
                selectingSample=true;try{SelectedSample=first;}finally{selectingSample=false;}
            }
        }
        catch(OperationCanceledException)when(id!=generation||lifetime.IsCancellationRequested){}
        catch(Exception e){if(id==generation){Status=e is OperationCanceledException?L.T("Zeitlimit beim Laden der Vorschau überschritten."):e.Message;log.Write("Vergleich",e);}}
        finally{if(id==generation){Loading=false;cancellation=null;RefreshCommands();}cancel.Dispose();}
    }
}
