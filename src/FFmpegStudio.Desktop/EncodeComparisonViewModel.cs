using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Microsoft.Win32;

namespace FFmpegStudio.Desktop;

public sealed record EncodeChoice(EncodeComparisonInput Input)
{
    public override string ToString()=>Label;
    public string Path=>Input.Media.Path;
    public string Label=>Input.Job is {} job?
        (job.Options.Video.RateControl is RateControl.Qualität or RateControl.QP?
            (job.Options.Video.RateControl==RateControl.QP||job.Options.Video.Encoder is "libvvenc" or "librav1e"?"QP":job.Options.Video.IsNvenc?"CQ":"CRF")+job.Options.Video.Quality.ToString("0.##"):
            job.Options.Video.BitRateKbps+" kbit/s")+" · "+job.Options.Video.Encoder+" · "+job.Options.Video.Preset+" · "+System.IO.Path.GetFileName(Path):
        System.IO.Path.GetFileName(Path)+" · "+Input.Media.Video!.Codec+" · "+Input.Media.Video.Width+" × "+Input.Media.Video.Height;
}
public sealed record ComparisonZoomChoice(double Value)
{public string Label=>Value==0?L.T("Einpassen"):Value.ToString("0 %");public override string ToString()=>Label;}

public enum EncodeComparisonMode { EncodeAndReference, TwoEncodes, TwoEncodesAndReference }
public sealed record ComparisonModeChoice(EncodeComparisonMode Value)
{
    public string Label=>L.T(Value switch
    {
        EncodeComparisonMode.EncodeAndReference=>"Encode A + Referenz",
        EncodeComparisonMode.TwoEncodes=>"Encode A + Encode B",
        _=>"Encode A + Encode B + Referenz"
    });
}

public sealed partial class EncodeComparisonViewModel : ObservableObject
{
    private readonly EncodeComparisonService service;
    private readonly FFmpegVersionService versions;
    private readonly MediaProbeService probe;
    private readonly CancellationToken lifetime;
    private readonly LoggingService log;
    private CancellationTokenSource? cancellation;
    private readonly HashSet<Task> tasks=[];
    private Task current=Task.CompletedTask;
    private long generation;
    private bool changing,settingPosition,explicitOriginal;
    private int openingCount;
    private EncodeComparisonFrames? frames;
    public EncodeComparisonFrames? LoadedFrames=>frames;
    public ObservableCollection<EncodeChoice> Choices{get;}=[];
    public IReadOnlyList<ComparisonZoomChoice> ZoomChoices=>[new(0),new(1),new(2),new(4)];
    public IReadOnlyList<ComparisonModeChoice> ModeChoices=>Enum.GetValues<EncodeComparisonMode>().Select(m=>new ComparisonModeChoice(m)).ToArray();
    [ObservableProperty] private EncodeChoice? selectedA;
    [ObservableProperty] private EncodeChoice? selectedB;
    [ObservableProperty] private MediaInfo? original;
    [ObservableProperty] private EncodeComparisonMode mode=EncodeComparisonMode.EncodeAndReference;
    [ObservableProperty] private bool matchReference;
    [ObservableProperty] private double offsetA,offsetB,position,maximum=100;
    [ObservableProperty] private bool loading,sideBySide=true;
    [ObservableProperty] private bool openingFile;
    public bool Busy=>Loading||OpeningFile;
    [ObservableProperty] private double zoom;
    [ObservableProperty] private int focusIndex;
    [ObservableProperty] private BitmapImage? imageA,imageB,originalImage;
    [ObservableProperty] private string messageA="",messageB="",originalMessage="";
    [ObservableProperty] private string status=L.T("Einen oder zwei Encodes wählen oder Dateien öffnen.");
    public bool CanOffsetA=>SelectedA is {Input.Job:null};
    public bool CanOffsetB=>SelectedB is {Input.Job:null};
    public IReadOnlyList<ComparisonInterval> CoverageA=>Coverage(SelectedA,OffsetA);
    public IReadOnlyList<ComparisonInterval> CoverageB=>Coverage(SelectedB,OffsetB);
    private IReadOnlyList<ComparisonInterval> Coverage(EncodeChoice? choice,double offset)=>choice is null?
        [new(0,Maximum,ComparisonAvailability.Unknown)]:ComparisonTimeline.Coverage(Maximum,choice.Input.Job?.Options,choice.Input.Job?.Source.Duration,choice.Input.Media.Duration,offset);
    public bool CompareTwoEncodes=>Mode!=EncodeComparisonMode.EncodeAndReference;
    public bool OriginalVisible=>Mode!=EncodeComparisonMode.TwoEncodes;
    public bool CanMatchReference=>OriginalVisible&&Original is not null&&SelectedA?.Input.Job is not null;
    public string ToggleLabel=>CompareTwoEncodes?"A ↔ B":L.T("Referenz ↔ Encode");
    public string ReferenceLabel=>MatchReference&&CanMatchReference?L.T("Referenz mit Filtern von Encode A"):L.T("Original");
    public string ReferenceDescription=>MatchReference&&CanMatchReference?L.T("Referenz mit identischer Geometrie; Ausgabe auf Vergleichsraster. "):L.T("Original unverändert; Filter- und Größenunterschiede bleiben sichtbar. RGB/SDR-Vorschaubilder; bei anderer Bildrate wird der zeitlich nächste Frame gezeigt.");
    public bool SingleVisible=>!SideBySide;
    public int PanelCount=>(OriginalVisible?1:0)+(CompareTwoEncodes?2:1);
    public string OriginalLabel=>Original is null?L.T("Kein Original gewählt"):System.IO.Path.GetFileName(Original.Path);
    public BitmapImage? FocusImage=>FocusIndex==2&&OriginalVisible?OriginalImage:FocusIndex==1?ImageB:ImageA;
    public string FocusLabel=>FocusIndex==2&&OriginalVisible?ReferenceLabel:FocusIndex==1?"B · "+SelectedB?.Label:"A · "+SelectedA?.Label;
    public string FocusMessage=>FocusIndex==2&&OriginalVisible?OriginalMessage:FocusIndex==1?MessageB:MessageA;
    public event Action? Requested;
    public EncodeComparisonViewModel(EncodeComparisonService service,FFmpegVersionService versions,MediaProbeService probe,LoggingService log,CancellationToken lifetime)
    {this.service=service;this.versions=versions;this.probe=probe;this.log=log;this.lifetime=lifetime;}
    public void RefreshJobs(IEnumerable<EncodingJob> jobs)
    {
        var completed=jobs.Where(j=>j.Status==JobStatus.Abgeschlossen&&j.OutputInfo?.Video is not null).ToArray();
        foreach(var choice in Choices.Where(c=>c.Input.Job is {} j&&!completed.Any(x=>x.Id==j.Id)).ToArray())Choices.Remove(choice);
        foreach(var job in completed)if(!Choices.Any(c=>c.Input.Job?.Id==job.Id))Choices.Add(new(EncodeComparisonInput.FromJob(job)));
    }
    public void SelectJobs(IEnumerable<EncodingJob> selected)
    {
        var jobs=selected.Where(j=>j.Status==JobStatus.Abgeschlossen&&j.OutputInfo?.Video is not null).Take(2).ToArray();
        if(jobs.Length==0){Status=L.T("Einen oder zwei Encodes wählen oder Dateien öffnen.");Requested?.Invoke();return;}
        if(jobs.Length==2&&!EncodeComparisonService.SameFile(jobs[0].Source.Path,jobs[1].Source.Path)){Status=L.T("Die Queue-Encodes müssen dieselbe Quelldatei verwenden.");Requested?.Invoke();return;}
        var keepPosition=SameSource(SelectedA,jobs[0]);
        changing=true;
        try
        {
            RefreshJobs(jobs.Concat(Choices.Where(c=>c.Input.Job is not null).Select(c=>c.Input.Job!)));
            Mode=jobs.Length==1?EncodeComparisonMode.EncodeAndReference:Mode==EncodeComparisonMode.TwoEncodes?EncodeComparisonMode.TwoEncodes:EncodeComparisonMode.TwoEncodesAndReference;
            MatchReference=!CompareTwoEncodes;
            SelectedA=Choices.First(c=>c.Input.Job?.Id==jobs[0].Id);SelectedB=jobs.Length==2?Choices.First(c=>c.Input.Job?.Id==jobs[1].Id):null;
            explicitOriginal=false;Original=jobs[0].Source;OffsetA=OffsetB=0;
            if(!keepPosition)Position=CutTimeline.Keep(jobs[0].Options).FirstOrDefault()?.Start??0;
        }
        finally{changing=false;}
        ClearSamples();UpdateProperties();Requested?.Invoke();Request();
    }
    private static bool SameSource(EncodeChoice? previous,EncodingJob? next)=>previous?.Input.Job is {} old&&next is not null&&EncodeComparisonService.SameFile(old.Source.Path,next.Source.Path);
    partial void OnSelectedAChanged(EncodeChoice? oldValue,EncodeChoice? newValue)=>SelectionChanged(true,oldValue);
    partial void OnSelectedBChanged(EncodeChoice? value)=>SelectionChanged(false);
    private void SelectionChanged(bool a,EncodeChoice? previous=null)
    {
        if(changing)return;
        changing=true;
        try
        {
            if(a)OffsetA=0;else OffsetB=0;
            var source=SelectedA?.Input.Job?.Source??(CompareTwoEncodes?SelectedB?.Input.Job?.Source:null);
            if(source is not null){Original=source;explicitOriginal=false;}
            else if(!explicitOriginal)Original=null;
            if(a&&!SameSource(previous,SelectedA?.Input.Job))Position=SelectedA?.Input.Job is {} job?CutTimeline.Keep(job.Options).FirstOrDefault()?.Start??0:0;
        }
        finally{changing=false;}
        ClearSamples();UpdateProperties();Request();
    }
    partial void OnModeChanged(EncodeComparisonMode value)
    {
        if(changing)return;
        changing=true;
        try
        {
            var source=SelectedA?.Input.Job?.Source??(CompareTwoEncodes?SelectedB?.Input.Job?.Source:null);
            if(source is not null){Original=source;explicitOriginal=false;}else if(!explicitOriginal)Original=null;
        }
        finally{changing=false;}
        ClearSamples();UpdateProperties();Request();
    }
    partial void OnMatchReferenceChanged(bool value)
    {
        if(changing)return;
        if(!CanMatchReference)
        {
            changing=true;try{MatchReference=false;}finally{changing=false;}
            return;
        }
        ClearSamples();UpdateProperties();Request();
    }
    partial void OnOriginalChanged(MediaInfo? value){if(!changing){ClearSamples();UpdateProperties();Request();}}
    partial void OnOffsetAChanged(double value){if(!changing){ClearSamples();UpdateProperties();Request();}}
    partial void OnOffsetBChanged(double value){if(!changing){ClearSamples();UpdateProperties();Request();}}
    partial void OnMaximumChanged(double value){OnPropertyChanged(nameof(CoverageA));OnPropertyChanged(nameof(CoverageB));}
    partial void OnPositionChanged(double value){if(!settingPosition&&!changing)Request(debounce:true);}
    partial void OnSideBySideChanged(bool value)=>OnPropertyChanged(nameof(SingleVisible));
    partial void OnLoadingChanged(bool value)=>OnPropertyChanged(nameof(Busy));
    partial void OnOpeningFileChanged(bool value){OnPropertyChanged(nameof(Busy));RefreshCommands();}
    partial void OnFocusIndexChanged(int value)=>RefreshFocus();
    private void RefreshFocus(){OnPropertyChanged(nameof(FocusImage));OnPropertyChanged(nameof(FocusLabel));OnPropertyChanged(nameof(FocusMessage));}
    private void UpdateProperties()
    {
        if(!CanMatchReference&&MatchReference)
        {
            var wasChanging=changing;changing=true;try{MatchReference=false;}finally{changing=wasChanging;}
        }
        OnPropertyChanged(nameof(CompareTwoEncodes));
        if(!OriginalVisible&&FocusIndex==2||!CompareTwoEncodes&&FocusIndex==1)FocusIndex=0;
        Maximum=Math.Max(.1,Original?.Duration??SelectedA?.Input.Job?.Source.Duration??(CompareTwoEncodes?SelectedB?.Input.Job?.Source.Duration:null)??
            Math.Max((SelectedA?.Input.Media.Duration??100)+OffsetA,CompareTwoEncodes?(SelectedB?.Input.Media.Duration??0)+OffsetB:0));
        OnPropertyChanged(nameof(CoverageA));OnPropertyChanged(nameof(CoverageB));
        OnPropertyChanged(nameof(CanOffsetA));OnPropertyChanged(nameof(CanOffsetB));OnPropertyChanged(nameof(OriginalVisible));OnPropertyChanged(nameof(PanelCount));OnPropertyChanged(nameof(OriginalLabel));OnPropertyChanged(nameof(CanMatchReference));OnPropertyChanged(nameof(ReferenceLabel));OnPropertyChanged(nameof(ReferenceDescription));OnPropertyChanged(nameof(ToggleLabel));RefreshFocus();RefreshCommands();
    }
    private bool CanLoad()=>!lifetime.IsCancellationRequested&&SelectedA is not null&&(CompareTwoEncodes?SelectedB is not null:Original is not null);
    private bool CanStep()=>CanLoad()&&!Busy&&frames is not null;
    private void RefreshCommands(){LoadFramesCommand.NotifyCanExecuteChanged();PreviousCommand.NotifyCanExecuteChanged();NextCommand.NotifyCanExecuteChanged();ToggleCommand.NotifyCanExecuteChanged();GenerateSamplesCommand.NotifyCanExecuteChanged();}
    [RelayCommand(CanExecute=nameof(CanLoad))] private Task LoadFramesAsync()=>Request();
    [RelayCommand(CanExecute=nameof(CanStep))] private Task PreviousAsync()=>Request(FrameSeek.Previous);
    [RelayCommand(CanExecute=nameof(CanStep))] private Task NextAsync()=>Request(FrameSeek.Next);
    [RelayCommand(CanExecute=nameof(CanStep))] private void Toggle(){EndReferenceHold();FocusIndex=CompareTwoEncodes?(FocusIndex==1?0:1):(FocusIndex==2?0:2);SideBySide=false;}
    public Task WaitAsync()=>current;
    private Task Track(Task task)
    {
        tasks.Add(task);_=Forget();return current=task;
        async Task Forget(){try{await task;}finally{tasks.Remove(task);}}
    }
    private Task Request(FrameSeek direction=FrameSeek.Nearest,bool debounce=false)
    {
        EndReferenceHold();DeselectSample();var id=++generation;cancellation?.Cancel();cancellation=null;frames=null;
        ImageA=ImageB=OriginalImage=null;MessageA=MessageB=OriginalMessage="";RefreshFocus();
        if(!CanLoad())
        {
            Loading=false;Status=SelectedA is not null&&OriginalVisible&&Original is null?
                L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."):
                CompareTwoEncodes?L.T("Einen oder zwei Encodes wählen oder Dateien öffnen."):L.T("Original und Encode A wählen.");
            RefreshCommands();return current=Task.CompletedTask;
        }
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime);cancel.CancelAfter(TimeSpan.FromSeconds(60));cancellation=cancel;
        var a=SelectedA!.Input;var b=CompareTwoEncodes?SelectedB!.Input:null;var original=Original;var requested=Position;var oa=OffsetA;var ob=OffsetB;var include=OriginalVisible;var match=MatchReference&&CanMatchReference;
        Loading=true;Status=L.T("Korrespondierende Frames werden dekodiert …");RefreshCommands();return Track(Load());
        async Task Load()
        {
            try
            {
                if(debounce)await Task.Delay(180,cancel.Token);
                var result=await service.LoadAsync(original,a,b,requested,include,oa,ob,direction,cancel.Token,match);
                cancel.Token.ThrowIfCancellationRequested();
                var snapshot=DecodeFrames(result);
                if(id!=generation)return;
                DisplayFrames(snapshot);
            }
            catch(OperationCanceledException)when(id!=generation||lifetime.IsCancellationRequested){}
            catch(Exception e){if(id==generation){Status=e is OperationCanceledException?L.T("Zeitlimit beim Laden der Vorschau überschritten."):e.Message;log.Write("Encode-Vergleich",e);}}
            finally{if(id==generation){Loading=false;cancellation=null;RefreshCommands();}cancel.Dispose();}
        }
    }
    private static string Describe(ComparisonPicture picture,BitmapImage? image,double source)=>picture.Frame is {} f?
        $"{image?.PixelWidth} × {image?.PixelHeight} · "+L.F($"Dateizeit {f.Seconds:0.000000} s · Quelle {picture.SourceSeconds:0.000000} s · Δ {(picture.SourceSeconds-source)*1000:0.###} ms"):
        picture.Message;
    [RelayCommand] private Task OpenAAsync()=>ChooseAsync(0);
    [RelayCommand] private Task OpenBAsync()=>ChooseAsync(1);
    [RelayCommand] private Task OpenOriginalAsync()=>ChooseAsync(2);
    private async Task ChooseAsync(int slot)
    {
        var dialog=new OpenFileDialog{Title=L.T("Videodatei für den Vergleich auswählen"),Filter=L.T("Videodateien|*.mkv;*.mp4;*.mov;*.webm;*.avi;*.ts;*.m2ts;*.m4v|Alle Dateien|*.*")};
        if(dialog.ShowDialog()==true)await LoadFileAsync(slot,dialog.FileName);
    }
    public Task LoadFileAsync(int slot,string path)=>Track(LoadFileCoreAsync(slot,path));
    private async Task LoadFileCoreAsync(int slot,string path)
    {
        openingCount++;OpeningFile=true;Status=L.T("Vergleichsdatei wird gelesen …");
        try
        {
            var tools=versions.Active??throw new InvalidOperationException(L.T("FFmpeg fehlt für den Ordnerimport."));
            var media=await probe.ProbeAsync(tools,path,lifetime);
            if(media.Video is null)throw new InvalidDataException(L.T("Keine Videospur gefunden."));
            if(slot==2){explicitOriginal=true;Original=media;}
            else
            {
                var choice=Choices.FirstOrDefault(c=>c.Input.Job is null&&EncodeComparisonService.SameFile(c.Path,path));
                if(choice is null){choice=new(new(media,tools));Choices.Add(choice);}
                if(slot==0)SelectedA=choice;else SelectedB=choice;
            }
        }
        catch(OperationCanceledException){}
        catch(Exception e){Status=e.Message;log.Write("Vergleichsdatei öffnen",e);}
        finally{openingCount--;OpeningFile=openingCount>0;}
    }
    public void RefreshLanguage(string previous)
    {
        Status=L.Retranslate(Status,previous);OnPropertyChanged(nameof(ZoomChoices));OnPropertyChanged(nameof(ModeChoices));UpdateProperties();
        if(frames is {} f){MessageA=Describe(f.A,ImageA,f.SourceSeconds);MessageB=Describe(f.B,ImageB,f.SourceSeconds);OriginalMessage=f.Original is {} o?Describe(o,OriginalImage,f.SourceSeconds):OriginalVisible?L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."):"";RefreshFocus();}
    }
    public async Task ShutdownAsync(){generation++;cancellation?.Cancel();await Task.WhenAll(tasks.ToArray());}
}
