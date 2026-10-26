using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Win32;

namespace FFmpegStudio.Desktop;

public sealed partial class MediaItem : ObservableObject
{
    public MediaInfo Info { get; init; } = new();
    [ObservableProperty] private EncodingOptions options = new();
    [ObservableProperty] private ImageSource? thumbnail;
    public string Name => Info.Name;
    public string Summary => Info.Summary;
    public void RefreshLanguage()
    {
        foreach(var track in Options.Audio) { track.SourceSummary = Info.Streams.FirstOrDefault(s=>s.Index==track.InputIndex)?.Summary ?? track.SourceSummary; track.RefreshLanguage(); }
        foreach(var track in Options.Subtitles) { track.SourceSummary = Info.Streams.FirstOrDefault(s=>s.Index==track.InputIndex)?.Summary ?? track.SourceSummary; track.RefreshLanguage(); }
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Options));
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppPaths paths;
    private readonly SettingsService settings;
    private readonly FFmpegVersionService versions;
    private readonly FFmpegDownloadService downloader;
    private readonly FFmpegCapabilityService capabilityService;
    private readonly HardwareDetectionService hardware;
    private readonly MediaProbeService probe;
    private readonly PreviewService preview;
    private readonly QueueService queue;
    private readonly QueueStore store;
    private readonly PresetService presets;
    private readonly FFmpegCommandBuilder builder;
    private readonly QualityAnalysisService quality;
    private readonly SubtitleService subtitles;
    private readonly FolderWatchService watcher;
    private readonly HardwareMonitorService monitor;
    private readonly LoggingService log;
    private readonly Dispatcher dispatcher = System.Windows.Application.Current.Dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private readonly List<INotifyPropertyChanged> observed = [];
    private readonly DispatcherTimer refreshTimer;
    private Task? monitorTask, updateTask;
    private Task? shutdown;
    private Task queueSettingsSave = Task.CompletedTask;
    private sealed record AnalysisOperation(CancellationTokenSource Cancellation,TaskCompletionSource Completion);
    private readonly Dictionary<Guid,AnalysisOperation> analysisOperations=[];
    private readonly HashSet<string> importing = new(StringComparer.OrdinalIgnoreCase);
    private RemoteBuild? remote;
    private bool adjustingEncoder;
    private bool initialized;
    private bool applyingPreset;
    private SensorSnapshot? lastSensors;
    public ObservableCollection<MediaItem> Media { get; } = [];
    public ObservableCollection<EncodingJob> Jobs { get; } = [];
    public EncodeComparisonViewModel CompareEncodes{get;}
    public ObservableCollection<Preset> Presets { get; } = [];
    public ObservableCollection<EncoderInfo> VideoEncoders { get; } = [];
    public ObservableCollection<string> AudioEncoders { get; } = [];
    public ObservableCollection<string> SubtitleEncoders { get; } = [];
    public ObservableCollection<string> PresetChoices { get; } = [];
    public ObservableCollection<string> VideoPresetChoices { get; } = [];
    public string VideoPresetText
    {
        get=>SelectedMedia is {} item?VideoPresentation.PresetLabel(string.IsNullOrEmpty(item.Options.Video.Preset)?VideoPresetDefault:item.Options.Video.Preset,item.Options.Video.Encoder,VideoPresetDefault):"";
        set
        {
            if(adjustingEncoder||SelectedMedia is not {} item)return;
            item.Options.Video.Preset=VideoPresentation.PresetValue(value);
        }
    }
    public string VideoPresetDefault=>VideoPresentation.PresetDefault(SelectedMedia is {} item?Capabilities?.Find(item.Options.Video.Encoder):null);
    public IReadOnlyList<string> VideoPresetValues=>VideoPresentation.PresetValues(SelectedMedia?.Options.Video.Encoder,PresetChoices,SelectedMedia?.Options.Video.Preset,VideoPresetDefault);
    public string VideoPresetMinimum=>VideoPresetValues.FirstOrDefault()??"";
    public string VideoPresetMaximum=>VideoPresetValues.LastOrDefault()??"";
    public string VideoPresetDescription=>VideoPresentation.PresetDescription(SelectedMedia?.Options.Video.Encoder);
    public string VideoModeDescription=>SelectedMedia is {} item?VideoPresentation.ModeDescription(item.Options.Video):"";
    public string VideoQualityLabel=>SelectedMedia is {} item?VideoPresentation.QualityLabel(item.Options.Video):L.T("Qualitätswert");
    public QualityRange VideoQualityRange=>SelectedMedia is {} item?VideoQualityScale.For(item.Options.Video,Capabilities?.Find(item.Options.Video.Encoder)):new(0,51,.5);
    public string VideoQualityHint=>SelectedMedia is {} item?VideoPresentation.QualityHint(item.Options.Video):"";
    public string ThreadControlDescription=>SelectedMedia is {} item?SoftwareThreading.Describe(item.Options.Video):"";
    public static ThreadControlMode[] ThreadModes { get; } = Enum.GetValues<ThreadControlMode>();
    public static int[] ParallelJobChoices { get; } = Enumerable.Range(1,16).ToArray();
    public static int[] ParallelGpuJobChoices { get; } = Enumerable.Range(1,8).ToArray();
    public int ParallelJobLimit
    {
        get=>Settings.ParallelJobs;
        set { if(value<1||value>16||value==Settings.ParallelJobs)return;Settings.ParallelJobs=value;OnPropertyChanged();queueSettingsSave=SaveQueueSettingsAsync(); }
    }
    public int ParallelGpuJobLimit
    {
        get=>Settings.ParallelGpuJobs;
        set { if(value<1||value>8||value==Settings.ParallelGpuJobs)return;Settings.ParallelGpuJobs=value;OnPropertyChanged();queueSettingsSave=SaveQueueSettingsAsync(); }
    }
    public bool EncodeEfficiencyMode
    {
        get=>Settings.EfficiencyMode;
        set { if(value==Settings.EfficiencyMode)return;Settings.EfficiencyMode=value;OnPropertyChanged();queueSettingsSave=SaveQueueSettingsAsync(); }
    }
    private async Task SaveQueueSettingsAsync()
    {
        try { await settings.SaveAsync(); }
        catch(Exception e) { log.Write(L.T("Queue-Einstellungen konnten nicht gespeichert werden."),e);StatusText=e.Message; }
    }
    private void RefreshExecutionSettings()
    { OnPropertyChanged(nameof(ParallelJobLimit));OnPropertyChanged(nameof(ParallelGpuJobLimit));OnPropertyChanged(nameof(EncodeEfficiencyMode)); }
    public ObservableCollection<string> TuneChoices { get; } = [];
    public sealed record ParameterChoice(string Value, string Label);
    public static ParameterChoice[] SvtTuneChoices { get; } =
    [new("", "Encoderstandard"), new("0", "0 · Visuelle Qualität (VQ)"), new("1", "1 · PSNR"), new("2", "2 · SSIM"), new("4", "4 · MS-SSIM"), new("5", "5 · VMAF")];
    public static ParameterChoice[] SvtDeblockingChoices { get; } =
    [new("", "Encoderstandard"), new("0", "0 · Aus"), new("1", "1 · Ein"), new("2", "2 · Genauer, langsamer")];
    public static ParameterChoice[] SvtParallelismChoices { get; } =
    [new("", "Encoderstandard"), new("0", "0 · Automatisch nach CPU"), ..Enumerable.Range(1,6).Select(n=>new ParameterChoice(n.ToString(),n.ToString()))];
    public ObservableCollection<string> ProfileChoices { get; } = [];
    public ObservableCollection<string> PixelFormats { get; } = [];
    public static TrackAction[] AudioActions { get; } = [TrackAction.Kopieren, TrackAction.Encodieren, TrackAction.Entfernen];
    public static TrackAction[] SubtitleActions { get; } = Enum.GetValues<TrackAction>();
    public static RateControl[] RateControls { get; } = Enum.GetValues<RateControl>();
    public static ScaleMode[] ScaleModes { get; } = Enum.GetValues<ScaleMode>();
    public static string[] Containers { get; } = ["mkv", "mp4", "webm", "mov"];
    public static string[] Algorithms { get; } = ["lanczos","bicubic","bilinear","spline","area","neighbor"];
    public static string[] AudioModes { get; } = ["vbr", "cbr", "constrained"];
    public AppSettings Settings => settings.Current;
    public bool CanEditFileNamePattern=>!Settings.FileNaming.Enabled;
    public Capabilities? Capabilities { get; private set; }
    [ObservableProperty] private MediaItem? selectedMedia;
    [ObservableProperty] private EncodingJob? selectedJob;
    [ObservableProperty] private Preset? selectedPreset;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = L.T("PixelPeeker wird vorbereitet …");
    [ObservableProperty] private string installedVersion = L.T("Nicht installiert");
    [ObservableProperty] private string availableVersion = L.T("Noch nicht geprüft");
    [ObservableProperty] private double? downloadPercent;
    [ObservableProperty] private string conversion = L.T("Dateien importieren, um die Konvertierung einzurichten.");
    [ObservableProperty] private string validationText = "";
    [ObservableProperty] private string commandText = "";
    [ObservableProperty] private string outputFileNamePreview = "";
    private int commandViewIndex;
    public int CommandViewIndex
    {
        get=>commandViewIndex;
        set
        {
            if(value is not (0 or 1)||!SetProperty(ref commandViewIndex,value))return;
            OnPropertyChanged(nameof(IsJobCommandPreview)); RefreshCommandPreview();
        }
    }
    [ObservableProperty] private string displayedCommandText = "";
    [ObservableProperty] private string commandPreviewDescription = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CopyCommandCommand))] private bool canCopyCommand;
    public bool IsJobCommandPreview => CommandViewIndex == 1;
    [ObservableProperty] private string mediaDetails = L.T("Noch keine Datei ausgewählt.");
    [ObservableProperty] private string estimateText = "";
    [ObservableProperty] private string hardwareText = L.T("Hardwarewerte werden gelesen …");
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private ImageSource? previewImage;
    [ObservableProperty] private double previewSeconds;
    [ObservableProperty] private double testSeconds = 10;
    [ObservableProperty] private double previewMaximum = 100;
    [ObservableProperty] private string queueSummary = L.T("Keine Jobs");
    [ObservableProperty] private double queueProgress;
    [ObservableProperty] private string encoderInfo = L.T("Noch kein Encoder erkannt.");
    [ObservableProperty] private string gpuProbeText = "";
    [ObservableProperty] private bool canSetPreset;
    [ObservableProperty] private bool canSetTune;
    public string SelectedJobDetails => SelectedJob is null ? L.T("Einen Job auswählen, um Ergebnis, Protokoll und Messwerte zu sehen.")
        : L.F($"{SelectedJob.Conversion}\nPreset: {L.T(SelectedJob.Options.PresetName)}\nFFmpeg: {SelectedJob.Tools.Version}\nAusgabe: {SelectedJob.OutputPath}\n{L.EnumText(SelectedJob.Status)}\n{SelectedJob.Statistics}\n{SelectedJob.Error}\n") +
          (SelectedJob.Scheduling is {} scheduling?scheduling.Description+"\n":"") +
          (SelectedJob.Analysis is { } a ? $"VMAF {a.Vmaf:0.###} · SSIM {a.Ssim:0.######} · PSNR {a.Psnr:0.###} dB\n{a.Conditions}" : L.T("Noch keine Qualitätsanalyse."));

    public MainViewModel(AppPaths paths, SettingsService settings, FFmpegVersionService versions, FFmpegDownloadService downloader,
        FFmpegCapabilityService capabilityService, HardwareDetectionService hardware, MediaProbeService probe, PreviewService preview, FrameNavigationService frameNavigation, AccuratePreviewService accuratePreview,
        QueueService queue, QueueStore store, PresetService presets, FFmpegCommandBuilder builder,
        QualityAnalysisService quality, SubtitleService subtitles, FolderWatchService watcher, HardwareMonitorService monitor, LoggingService log,SourcePlaybackService playback,EncodeComparisonService encodeComparison)
    {
        this.paths=paths; this.settings=settings; this.versions=versions; this.downloader=downloader; this.capabilityService=capabilityService;
        this.hardware=hardware; this.probe=probe; this.preview=preview;this.frameNavigation=frameNavigation;this.accuratePreview=accuratePreview; this.queue=queue; this.store=store; this.presets=presets;
        this.builder=builder; this.quality=quality; this.subtitles=subtitles; this.watcher=watcher; this.monitor=monitor; this.log=log;
        this.playback=playback;playback.Changed+=PlaybackChanged;
        CompareEncodes=new(encodeComparison,versions,probe,log,lifetime.Token);
        Jobs.CollectionChanged+=(_,_)=>CompareEncodes.RefreshJobs(Jobs);
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        refreshTimer.Tick += (_,_) => { refreshTimer.Stop(); RefreshSummary(); };
        log.Written += line => Ui(() => { LogText += line + "\n"; if (LogText.Length > 100_000) LogText = LogText[^80_000..]; });
        queue.JobAdded += job => Ui(() => { Jobs.Add(job); SelectedJob = job; UpdateQueueSummary(); });
        queue.JobChanged += _ => Ui(() => { UpdateQueueSummary(); OnPropertyChanged(nameof(SelectedJobDetails)); RefreshAnalysisCommands(); RefreshCommandPreview();CompareEncodes.RefreshJobs(Jobs); });
        queue.JobCompleted += job => Ui(() =>
        {
            OnPropertyChanged(nameof(SelectedJobDetails));
            if (job.Options.ClipDuration.HasValue && job.Source.Duration.HasValue)
            {
                var full=Json.Clone(job.Options);full.ClipStart=null;full.ClipDuration=null;
                EstimateText = L.F($"Test-Hochrechnung: ca. {SizeEstimator.Extrapolate(job.OutputBytes, CutTimeline.Duration(job.Source,job.Options)??0, CutTimeline.Duration(job.Source,full)) / 1048576d:0.0} MiB. Nur der getestete Ausschnitt wurde gemessen.");
            }
            if (job.Options.AutoAnalyze) _ = AnalyzeJobSafelyAsync(job);
        });
        queue.BatchCompleted += () => Ui(() => _ = CompleteBatchAsync());
        watcher.OnReady = async (path, rule, ct) =>
        {
            var active = versions.Active ?? throw new InvalidOperationException(L.T("FFmpeg fehlt für den Ordnerimport."));
            var media = await probe.ProbeAsync(active, path, ct);
            if (media.Video is null) throw new InvalidDataException(L.T("Keine Videospur gefunden."));
            var preset = (await presets.LoadAsync()).FirstOrDefault(p=>p.Name==rule.PresetName) ?? throw new InvalidOperationException(L.T("Überwachungspreset nicht gefunden."));
            var options = PresetService.Apply(preset, media); options.OutputDirectory = rule.OutputFolder;
            var job = await queue.AddAsync(media, options, ct);
            if (rule.AutoStart) queue.Start(job);
        };
    }
    private void Ui(Action action) { if (dispatcher.CheckAccess()) action(); else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(action); }
    public async Task InitializeAsync()
    {
        await settings.LoadAsync(); App.ApplyLanguage(Settings.Language); App.ApplyTheme(Settings.ThemeId); OnPropertyChanged(nameof(Settings));
        ButtonHints.Apply(Settings);
        RefreshExecutionSettings();
        await queue.InitializeAsync(); foreach (var job in queue.Jobs) Jobs.Add(job); UpdateQueueSummary();
        Settings.Codecs.SeedFromJobs(queue.Jobs);if(queue.Jobs.Count>0)ScheduleCodecSave();
        await ReloadPresetsAsync();
        try { await versions.LoadAsync(lifetime.Token); await RefreshCapabilitiesAsync(lifetime.Token); }
        catch (Exception e) { log.Write(L.T("FFmpeg-Startprüfung fehlgeschlagen."),e); StatusText=e.Message; }
        initialized = true; watcher.Start();
        monitorTask = MonitorLoopAsync(); updateTask = UpdateLoopAsync();
        if (versions.Active is null)
        {
            StatusText = L.T("FFmpeg fehlt. Unter „FFmpeg verwalten“ installieren oder lokale Programme auswählen.");
            if (Settings.AutoInstall) await InstallAsync();
        }
        else StatusText = L.T("Bereit · Dateien oder Ordner in die Bibliothek ziehen.");
    }
    private async Task RefreshCapabilitiesAsync(CancellationToken ct)
    {
        InstalledVersion = versions.Active?.Version ?? L.T("Nicht installiert");
        if (versions.Active is not { } active) return;
        StatusText = L.T("Encoder, Filter und Pixelformate werden aus FFmpeg gelesen …");
        var caps = await capabilityService.AnalyzeAsync(active,ct);
        ApplyCapabilities(caps);
    }
    internal void ApplyCapabilities(Capabilities caps)
    {
        Capabilities = caps;
        VideoEncoders.Clear(); foreach (var e in caps.ModernVideo.OrderBy(e=>e.Family).ThenBy(e=>e.Hardware).ThenBy(e=>e.Name)) VideoEncoders.Add(e);
        AudioEncoders.Clear(); foreach (var e in caps.Audio.OrderBy(e=>new[]{"libopus","aac","flac","libmp3lame","ac3","eac3"}.Contains(e.Name)?0:1).ThenBy(e=>e.Name)) AudioEncoders.Add(e.Name);
        SubtitleEncoders.Clear(); foreach (var e in caps.Encoders.Where(e=>e.MediaType=="subtitle")) SubtitleEncoders.Add(e.Name);
        RefreshEncoderChoices(false); RefreshSummary();
    }
    private async Task RunUiAsync(string description, Func<CancellationToken,Task> action)
    {
        if (IsBusy) { StatusText=L.T("Die aktuelle Operation läuft noch. Bei Bedarf abbrechen."); return; }
        IsBusy=true; StatusText=description; operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { await action(operation.Token); }
        catch (OperationCanceledException) { StatusText=L.T("Operation abgebrochen."); }
        catch (Exception e) { StatusText=e.Message; log.Write(description,e); MessageBox.Show(e.Message,"PixelPeeker",MessageBoxButton.OK,MessageBoxImage.Warning); }
        finally { operation.Dispose(); operation=null; IsBusy=false; }
    }
    [RelayCommand] private void CancelOperation() => operation?.Cancel();
    [RelayCommand] private async Task ImportFilesAsync()
    {
        var dialog=new OpenFileDialog { Title=L.T("Videos importieren"),Multiselect=true,Filter=L.T("Mediendateien|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m2ts;*.mts;*.ts;*.m4v;*.wmv;*.mpg;*.mpeg|Alle Dateien|*.*") };
        if(dialog.ShowDialog()==true) await ImportPathsAsync(dialog.FileNames);
    }
    [RelayCommand] private async Task ImportFolderAsync()
    {
        var dialog=new OpenFolderDialog { Title=L.T("Medienordner importieren"),Multiselect=true };
        if(dialog.ShowDialog()==true) await ImportPathsAsync(dialog.FolderNames);
    }
    public Task ImportPathsAsync(IEnumerable<string> input) => RunUiAsync(L.T("Dateien werden analysiert …"),async ct=>
    {
        var tools=versions.Active??throw new InvalidOperationException(L.T("Bitte zuerst FFmpeg installieren."));
        var files=await Task.Run(()=>input.SelectMany(p=>Directory.Exists(p)
            ? Directory.EnumerateFiles(p,"*",new EnumerationOptions { RecurseSubdirectories=Settings.RecursiveImport,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint })
            : new[]{p}).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),ct);
        int failed=0,added=0;
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if(Media.Any(m=>m.Info.Path.Equals(file,StringComparison.OrdinalIgnoreCase))||!importing.Add(file)) continue;
            try
            {
                StatusText=L.F($"Analysiere {Path.GetFileName(file)} …");
                var info=await probe.ProbeAsync(tools,file,ct);
                if(info.Video is null) throw new InvalidDataException(L.T("Keine Videospur vorhanden."));
                var options=EncodingOptions.ForMedia(info);
                if(Capabilities is {} available)Settings.Codecs.ApplyToNewFile(options,available);
                if(Capabilities?.Find(options.Video.Encoder) is null)
                {
                    var fallback=Capabilities?.ModernVideo.FirstOrDefault(e=>!e.Hardware)??throw new InvalidOperationException(L.T("Kein moderner Softwareencoder verfügbar."));
                    options.Video.Encoder=fallback.Name; RestoreEncoderOptions(options.Video);
                }
                if(info.Video.IsHdr) options.Video.PixelFormat="yuv420p10le";
                if(Settings.DefaultOutputDirectory.Length>0) options.OutputDirectory=Settings.DefaultOutputDirectory;
                var item=new MediaItem {Info=info,Options=options}; Media.Add(item); SelectedMedia=item; added++;
                try { item.Thumbnail=LoadImage(await preview.FrameAsync(tools,info,Math.Min(3,(info.Duration??10)*.1),width:320,ct:ct)); if(ReferenceEquals(SelectedMedia,item)&&DisplayedPreviewFrame is null)PreviewImage=item.Thumbnail; }
                catch(Exception e) when(e is not OperationCanceledException) { log.Write(L.F($"Vorschau: {file}"),e); }
            }
            catch(Exception e) when(e is not OperationCanceledException) { failed++; log.Write(L.F($"Import fehlgeschlagen: {file}"),e); }
            finally { importing.Remove(file); }
        }
        StatusText=L.F($"{added} Dateien importiert · {failed} nicht lesbar oder ohne Video.");
    });
    [RelayCommand] private void RemoveMedia() { if(SelectedMedia is { } media) Media.Remove(media); SelectedMedia=Media.FirstOrDefault(); }
    partial void OnSelectedMediaChanged(MediaItem? value)
    {
        ResetPlayback();ResetPreview();filteredPreview=false;
        AttachCuts();
        foreach(var item in observed)
        {item.PropertyChanged-=OptionsChanged;if(item is INotifyPropertyChanging changing)changing.PropertyChanging-=OptionsChanging;}
        observed.Clear();switchingCodecs.Clear();
        if(value is not null)
        {
            Observe(value.Options); Observe(value.Options.Video); Observe(value.Options.Filters);
            foreach(var a in value.Options.Audio) Observe(a); foreach(var s in value.Options.Subtitles) Observe(s);
            settingPreviewPosition=true;
            try{PreviewMaximum=Math.Max(.1,value.Info.Duration??100); PreviewSeconds=Math.Min(3,PreviewMaximum*.1);}
            finally{settingPreviewPosition=false;}
            PreviewImage=value.Thumbnail; MediaDetails=MediaDetailsText.Format(value.Info);
            var matching=Presets.FirstOrDefault(p=>p.Name==value.Options.PresetName);
            if(!ReferenceEquals(SelectedPreset,matching)) { selectedPreset=matching; OnPropertyChanged(nameof(SelectedPreset)); }
        }
        else{PreviewImage=null;MediaDetails=L.T("Noch keine Datei ausgewählt.");}
        RefreshEncoderChoices(false); RefreshSummary();
        RememberSelectedVideo();
        if(value is not null)_=RequestPreviewAsync(PreviewSeconds,debounce:true);
    }
    private void Observe(INotifyPropertyChanged value)
    {
        observed.Add(value);value.PropertyChanged+=OptionsChanged;
        if(value is VideoOptions or AudioTrackOptions && value is INotifyPropertyChanging changing)changing.PropertyChanging+=OptionsChanging;
    }
    private void OptionsChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(adjustingEncoder) return;
        if(sender is not null&&switchingCodecs.Contains(sender)&&e.PropertyName!=nameof(VideoOptions.Encoder))return;
        if(e.PropertyName==nameof(VideoOptions.Encoder)&&sender is not null)switchingCodecs.Remove(sender);
        if(sender is EncodingOptions&&e.PropertyName==nameof(EncodingOptions.CutsEnabled)){EnsureCutAudio();RefreshCuts();}
        if(!applyingPreset && SelectedMedia is { } selected && selected.Options.PresetName!="Benutzerdefiniert")
        {
            selected.Options.PresetName="Benutzerdefiniert";
            if(SelectedPreset is not null) SelectedPreset=null;
        }
        if(sender is VideoOptions && e.PropertyName==nameof(VideoOptions.Encoder)&&!adjustingEncoder) RefreshEncoderChoices(true);
        if(sender is AudioTrackOptions audio&&e.PropertyName==nameof(AudioTrackOptions.Encoder))RestoreAudioCodec(audio);
        if(sender is VideoOptions)RefreshVideoLabels();
        RememberCodecChange(sender,e);
        refreshTimer.Stop(); refreshTimer.Start();
    }
    partial void OnSelectedJobChanged(EncodingJob? value)
    {
        OnPropertyChanged(nameof(SelectedJobDetails)); RefreshAnalysisCommands(); RefreshCommandPreview();
    }
    partial void OnSelectedPresetChanged(Preset? value)
    {
        if(initialized&&SelectedMedia is { } item&&value is not null)
        {
            applyingPreset=true;
            try { item.Options=PresetService.Apply(value,item.Info,item.Options); OnSelectedMediaChanged(item); }
            finally { applyingPreset=false; }
        }
    }
    public void PrepareSmokeDisplay()
    {
        Jobs.Clear(); SelectedJob=null; UpdateQueueSummary();
    }
    private void RestoreEncoderOptions(VideoOptions v)=>CodecMemory.CopyVideo(Settings.Codecs.ForVideo(v.Encoder),v);
    private void RefreshEncoderChoices(bool defaults)
    {
        if(SelectedMedia is not { } item||Capabilities is null)
        {
            CanSetPreset=false;CanSetTune=false;RefreshVideoLabels();return;
        }
        adjustingEncoder=true;
        try
        {
            var v=item.Options.Video;
            if(defaults)
            {RestoreEncoderOptions(v);if(CutTimeline.Active(item.Options))v.CudaScale=false;}
            // Editable ComboBoxes can write empty text while their available items change.
            // Keep the requested settings and rebind after all dependent lists are ready.
            var preset=v.Preset??""; var tune=v.Tune??""; var profile=v.Profile??""; var pixelFormat=v.PixelFormat??"";
            var allowed=VideoEncoderPolicy.IsAllowed(v.Encoder);
            var e=allowed?Capabilities.Find(v.Encoder):null;
            EncoderInfo=!allowed?VideoEncoderPolicy.UnavailableMessage(v.Encoder):e is null?L.T("Encoder im Build nicht vorhanden."):$"{e.Family} · {e.Name}\n{e.Description}\n{(e.Hardware?L.T("Hardware-Unterstützung wird vor dem Start tatsächlich geprüft."):L.T("Softwareencoder"))}";
            Fill(PixelFormats,e?.PixelFormats);
            Fill(PresetChoices,e?.Options?.FirstOrDefault(o=>o.Name==(v.Encoder=="libaom-av1"?"cpu-used":v.Encoder=="librav1e"?"speed":"preset"))?.Choices);
            Fill(TuneChoices,e?.Options?.FirstOrDefault(o=>o.Name=="tune")?.Choices);
            Fill(ProfileChoices,e?.Options?.FirstOrDefault(o=>o.Name=="profile")?.Choices);
            CanSetPreset=e?.Options?.Any(o=>o.Name is "preset" or "speed" or "cpu-used")==true;
            CanSetTune=e?.Options?.Any(o=>o.Name=="tune")==true;
            if(v.Encoder is "libx264" or "libx264rgb" or "libx265") Fill(PresetChoices,["ultrafast","superfast","veryfast","faster","fast","medium","slow","slower","veryslow"]);
            v.Preset=preset; v.Tune=tune; v.Profile=profile;
            v.PixelFormat=pixelFormat;
            Fill(VideoPresetChoices,VideoPresetValues.Select(s=>VideoPresentation.PresetLabel(s,v.Encoder,VideoPresetDefault)),false);
            RefreshVideoLabels();
            OnPropertyChanged(nameof(SelectedMedia));
            GpuProbeText="";
        }
        finally { adjustingEncoder=false; }
    }
    private void RefreshVideoLabels()
    {
        OnPropertyChanged(nameof(VideoPresetText));OnPropertyChanged(nameof(VideoPresetDescription));
        OnPropertyChanged(nameof(VideoPresetDefault));
        OnPropertyChanged(nameof(VideoPresetValues));OnPropertyChanged(nameof(VideoPresetMinimum));OnPropertyChanged(nameof(VideoPresetMaximum));
        OnPropertyChanged(nameof(VideoModeDescription));OnPropertyChanged(nameof(VideoQualityLabel));
        OnPropertyChanged(nameof(VideoQualityRange));OnPropertyChanged(nameof(VideoQualityHint));
        OnPropertyChanged(nameof(ThreadControlDescription));
    }
    private static void Fill(ObservableCollection<string> target,IEnumerable<string>? items,bool includeEmpty=true)
    {
        var values=(includeEmpty?new[]{""}:Array.Empty<string>()).Concat(items??[]).Distinct().ToArray();
        for(var i=0;i<values.Length;i++)
        {
            if(i<target.Count&&target[i]==values[i]) continue;
            var existing=target.IndexOf(values[i]);
            if(existing>=0) target.Move(existing,i); else target.Insert(i,values[i]);
        }
        while(target.Count>values.Length) target.RemoveAt(target.Count-1);
    }
    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(CutSummary));
        if(SelectedMedia is not { } item) { CommandText=""; OutputFileNamePreview=""; ValidationText=""; RefreshCommandPreview(); return; }
        Conversion=ScalingService.Describe(item.Info,item.Options);
        var errors=builder.Validate(item.Info,item.Options,Capabilities);
        ValidationText=string.Join("\n",errors);
        var estimated=SizeEstimator.Estimate(item.Info,item.Options);
        EstimateText=estimated.HasValue?L.F($"Bitraten-Schätzung: ca. {estimated/1048576d:0.0} MiB (inkl. 2 % Containerzuschlag)."):L.T("CRF/CQ: Dateigröße unbekannt. Ein Testencode ermöglicht eine grobe Hochrechnung.");
        try
        {
            var output=QueueService.SuggestedOutputPath(item.Info,item.Options,Settings.FileNaming);
            OutputFileNamePreview=Path.GetFileName(output);
            var temporary=Path.Combine(Path.GetDirectoryName(output)!,Path.GetFileNameWithoutExtension(output)+".unvollstaendig-VORSCHAU"+Path.GetExtension(output));
            CommandText=FFmpegCommandBuilder.Display(versions.Active?.FFmpegPath??"ffmpeg.exe",builder.Build(item.Info,item.Options,temporary,Capabilities));
        }
        catch(Exception e) { CommandText="";OutputFileNamePreview=""; if(ValidationText.Length==0) ValidationText=e.Message; }
        RefreshCommandPreview();
    }
    private void RefreshCommandPreview()
    {
        CanCopyCommand=false;
        if(!IsJobCommandPreview)
        {
            if(SelectedMedia is null) { DisplayedCommandText=""; CommandPreviewDescription=L.T("Eine Datei aus der Medienbibliothek auswählen, um den Aufruf zu sehen."); }
            else if(CommandText.Length==0)
            {
                CommandPreviewDescription=L.T("Die aktuellen Einstellungen sind ungültig. Bitte die angezeigten Fehler korrigieren.");
                DisplayedCommandText=ValidationText;
            }
            else
            {
                CommandPreviewDescription=L.T("Vorschau der aktuellen Einstellungen. Dateikollisionen und der temporäre Dateiname werden beim Jobstart berücksichtigt.");
                DisplayedCommandText=CommandText; CanCopyCommand=true;
            }
            return;
        }
        if(SelectedJob is not {} job)
        { DisplayedCommandText=""; CommandPreviewDescription=L.T("Einen Queue-Job auswählen, um seinen Aufruf zu sehen."); return; }
        if(job.ExecutedCommand.Length>0)
        {
            CommandPreviewDescription=L.F($"Tatsächlich gestarteter Aufruf für {job.Name}. Die temporäre Ausgabedatei wird nach erfolgreichem Encoding umbenannt.");
            DisplayedCommandText=job.ExecutedCommand; CanCopyCommand=true;
        }
        else if(job.Status is JobStatus.Wartend or JobStatus.Zurückgestellt)
        {
            CommandPreviewDescription=L.F($"Vorschau für Queue-Job {job.Name}. Der temporäre Dateiname kann sich beim Start ändern.");
            try
            {
                DisplayedCommandText=FFmpegCommandBuilder.Display(job.Tools.FFmpegPath,builder.Build(job.Source,job.Options,job.PartialPath));
                CanCopyCommand=true;
            }
            catch(Exception e) { DisplayedCommandText=e.Message; }
        }
        else if(job.Status==JobStatus.Läuft)
        { DisplayedCommandText=""; CommandPreviewDescription=L.T("Der Job wird vorbereitet. Der gestartete Aufruf erscheint hier, sobald FFmpeg läuft."); }
        else
        { DisplayedCommandText=""; CommandPreviewDescription=L.T("Kein gespeicherter Aufruf vorhanden. Bei älteren Jobs steht er im Jobprotokoll."); }
    }
    [RelayCommand] private Task AddQueueAsync() => RunUiAsync(L.T("Job wird angelegt …"),async ct=>
    {
        var item=SelectedMedia??throw new InvalidOperationException(L.T("Bitte eine Datei auswählen."));
        await queue.AddAsync(item.Info,item.Options,ct); StatusText=L.T("Job gespeichert. Mit „Queue starten“ beginnt das Encoding.");
    });
    public Task AddManyAsync(IEnumerable<MediaItem> items) => RunUiAsync(L.T("Jobs werden angelegt …"),async ct=> { foreach(var item in items) await queue.AddAsync(item.Info,item.Options,ct); StatusText=L.T("Jobs gespeichert."); });
    public void ApplyToMany(IEnumerable<MediaItem> items)
    {
        if(SelectedMedia is not { } source) return;
        var p=new Preset { Name=source.Options.PresetName, Options=Json.Clone(source.Options), AudioRule=new(){LanguageOrder=[]} };
        foreach(var item in items.Where(x=>x!=source)) item.Options=PresetService.Apply(p,item.Info,item.Options);
        StatusText=L.T("Einstellungen auf die markierten Dateien übertragen.");
    }
    [RelayCommand] private void StartQueue() { queue.StartAll(); StatusText=L.T("Queue gestartet."); }
    [RelayCommand] private void StopQueue() { queue.StopAll(); StatusText=L.T("Laufende Jobs werden abgebrochen."); }
    [RelayCommand] private void StartJob() { if(SelectedJob is { } job) queue.Start(job); }
    public void StartSelectedJobs(IEnumerable<EncodingJob> selected)
    { var count=queue.StartMany(selected);StatusText=L.F($"{count} ausgewählte Jobs zum Start vorgemerkt. Gleichzeitig laufen höchstens {Settings.ParallelJobs} Jobs."); }
    [RelayCommand] private void CancelJob() { if(SelectedJob is { } job) queue.Cancel(job); }
    [RelayCommand] private Task DeferJobAsync() => RunUiAsync(L.T("Job zurückstellen …"),async _=> { if(SelectedJob is { } job) await queue.DeferAsync(job); });
    [RelayCommand] private Task RemoveJobAsync() => RunUiAsync(L.T("Queue-Eintrag entfernen …"),async _=>
    {
        if(SelectedJob is not { } job) return; await queue.RemoveAsync(job); Jobs.Remove(job); SelectedJob=Jobs.FirstOrDefault(); UpdateQueueSummary();
    });
    [RelayCommand] private Task EditJobAsync() => RunUiAsync(L.T("Job bearbeiten …"),async _=>
    {
        var job=SelectedJob??throw new InvalidOperationException(L.T("Job auswählen."));
        if(job.Status is not(JobStatus.Wartend or JobStatus.Zurückgestellt)) throw new InvalidOperationException(L.T("Nur wartende oder zurückgestellte Jobs bearbeiten."));
        var edited=JsonEditor.Edit(L.T("Job-Einstellungen"),job.Options);
        if(edited is null) return;
        await queue.ReplaceOptionsAsync(job,edited); OnPropertyChanged(nameof(SelectedJobDetails));
    });
    public async Task MoveJobAsync(EncodingJob source,int index)
    {
        await queue.MoveAsync(source,index); Jobs.Clear(); foreach(var job in queue.Jobs) Jobs.Add(job); SelectedJob=source;
    }
    private void UpdateQueueSummary()
    {
        QueueSummary=QueueSummaryText.Format(Jobs);
        QueueProgress=Jobs.Count==0?0:Jobs.Sum(j=>j.Status==JobStatus.Abgeschlossen?100:j.Progress??0)/Jobs.Count;
    }
    [RelayCommand] private Task DetectCropAsync() => RunUiAsync(L.T("Schwarze Balken werden an mehreren Positionen geprüft …"),async ct=>
    {
        var item=SelectedMedia??throw new InvalidOperationException(L.T("Datei auswählen.")); var tools=versions.Active??throw new InvalidOperationException(L.T("FFmpeg fehlt."));
        var crop=await preview.DetectCropAsync(tools,item.Info,ct);
        if(MessageBox.Show(L.F($"Crop-Vorschlag übernehmen?\nLinks {crop.Left}, rechts {crop.Right}, oben {crop.Top}, unten {crop.Bottom} Pixel.\nDie Vorschau anschließend kontrollieren."),L.T("Schwarze Balken"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
        { var f=item.Options.Filters; f.CropLeft=crop.Left; f.CropRight=crop.Right; f.CropTop=crop.Top; f.CropBottom=crop.Bottom; f.CropEnabled=true; }
        StatusText=L.T("Balkenerkennung abgeschlossen.");
    });
    [RelayCommand] private Task TestEncodeAsync() => RunUiAsync(L.T("Testencode vorbereiten …"),async ct=>
    {
        var item=SelectedMedia??throw new InvalidOperationException(L.T("Datei auswählen."));
        if(TestSeconds<=0||TestSeconds>120) throw new ArgumentException(L.T("Testlänge zwischen 0 und 120 Sekunden wählen."));
        var o=Json.Clone(item.Options); o.ClipStart=PreviewSeconds; o.ClipDuration=Math.Min(TestSeconds,(item.Info.Duration??double.MaxValue)-PreviewSeconds);
        o.OutputDirectory=Path.Combine(paths.Cache,"test-encodes"); o.FileNamePattern="{name}_Test_{codec}";
        var job=await queue.AddAsync(item.Info,o,ct);queue.Start(job);StatusText=TestEncodeNotificationWindow.StatusMessage(job);
        if(Settings.ShowTestEncodeNotification)
        {
            var notice=new TestEncodeNotificationWindow(job){Owner=System.Windows.Application.Current.MainWindow};
            notice.ShowDialog();
            if(notice.DontShowAgain){Settings.ShowTestEncodeNotification=false;await settings.SaveAsync();}
            StatusText=TestEncodeNotificationWindow.StatusMessage(job);
        }
    });
    private bool CanAnalyzeQuality() => SelectedJob is { Status:JobStatus.Abgeschlossen,OutputInfo.Video:not null,IsAnalyzing:false };
    private bool CanCancelQualityAnalysis() => SelectedJob is { IsAnalyzing:true };
    private void RefreshAnalysisCommands()
    {
        AnalyzeQualityCommand.NotifyCanExecuteChanged(); CancelQualityAnalysisCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute=nameof(CanAnalyzeQuality))]
    private Task AnalyzeQualityAsync() => SelectedJob is { } job?AnalyzeJobSafelyAsync(job,true):Task.CompletedTask;
    [RelayCommand(CanExecute=nameof(CanCancelQualityAnalysis))]
    private void CancelQualityAnalysis()
    {
        if(SelectedJob is { } job&&analysisOperations.TryGetValue(job.Id,out var active))
        {
            job.AnalysisStatus=L.T("Qualitätsanalyse wird abgebrochen …"); active.Cancellation.Cancel();
        }
    }
    private async Task AnalyzeJobSafelyAsync(EncodingJob job,bool showResult=false)
    {
        if(lifetime.IsCancellationRequested||analysisOperations.ContainsKey(job.Id)) return;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var active=new AnalysisOperation(cancellation,new(TaskCreationOptions.RunContinuationsAsynchronously));
        analysisOperations.Add(job.Id,active);
        job.AnalysisProgress=null; job.AnalysisStatus=L.T("Qualitätsanalyse wird gestartet …");
        job.AnalysisDetails=L.T("Quelle und Ausgabedatei werden verglichen. Die Analyse besteht aus mehreren Durchläufen.");
        job.IsAnalyzing=true; RefreshAnalysisCommands();
        var clock=Stopwatch.StartNew();
        try
        {
            await quality.AnalyzeAsync(job,new Progress<QualityAnalysisProgress>(p=>
            {
                if(cancellation.IsCancellationRequested||!analysisOperations.TryGetValue(job.Id,out var current)||!ReferenceEquals(current,active)) return;
                job.AnalysisStatus=p.Stage; job.AnalysisProgress=p.Percent;
                job.AnalysisDetails=DescribeAnalysisProgress(p);
                if(!IsBusy) StatusText=L.F($"Qualitätsanalyse · {job.Name} · {p.Stage}");
            }),cancellation.Token);
            job.AnalysisProgress=100; job.AnalysisStatus=L.T("Qualitätsanalyse abgeschlossen");
            static string N(double? value)=>value?.ToString("0.###")??L.T("nicht verfügbar");
            var result=job.Analysis!;
            job.AnalysisDetails=L.F($"VMAF {N(result.Vmaf)} · SSIM {N(result.Ssim)} · PSNR {N(result.Psnr)} dB\nDauer {FormatAnalysisTime(clock.Elapsed)} · Ergebnisse gespeichert.");
            OnPropertyChanged(nameof(SelectedJobDetails));
            if(!IsBusy) StatusText=L.F($"Qualitätsanalyse abgeschlossen · {job.Name}");
            if(showResult&&!lifetime.IsCancellationRequested)
                new AnalysisWindow(result) {Owner=System.Windows.Application.Current.MainWindow}.Show();
        }
        catch(OperationCanceledException)
        {
            job.AnalysisProgress=null; job.AnalysisStatus=L.T("Qualitätsanalyse abgebrochen");
            job.AnalysisDetails=L.F($"Beendet nach {FormatAnalysisTime(clock.Elapsed)}. Vorhandene Messwerte bleiben verfügbar.");
        }
        catch(Exception e)
        {
            job.AnalysisProgress=null; job.AnalysisStatus=L.T("Qualitätsanalyse fehlgeschlagen");
            job.AnalysisDetails=e.Message.Length>4000?e.Message[..4000]:e.Message;
            log.Write(L.F($"Qualitätsanalyse fehlgeschlagen: {job.Name}"),e);
            if(!IsBusy) StatusText=L.F($"Qualitätsanalyse fehlgeschlagen · {job.Name}");
        }
        finally
        {
            job.IsAnalyzing=false; analysisOperations.Remove(job.Id); RefreshAnalysisCommands();
            active.Completion.TrySetResult();
        }
    }
    private static string FormatAnalysisTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private static string DescribeAnalysisProgress(QualityAnalysisProgress p)
    {
        var parts=new List<string>();
        if(p.Percent.HasValue) parts.Add(L.F($"{p.Percent:0.0} % im aktuellen Durchlauf"));
        if(p.Seconds.HasValue) parts.Add(p.Duration.HasValue?$"{p.Seconds:0.0} / {p.Duration:0.0} s":L.F($"{p.Seconds:0.0} s verarbeitet"));
        if(p.Speed.HasValue) parts.Add($"{p.Speed:0.00}×");
        parts.Add(L.F($"Laufzeit {FormatAnalysisTime(p.Elapsed)}"));
        if(p.Remaining.HasValue) parts.Add(L.F($"Rest im Durchlauf ca. {FormatAnalysisTime(p.Remaining.Value)}"));
        return string.Join(" · ",parts);
    }
    [RelayCommand] private void ShowAnalysis()
    {
        if(SelectedJob?.Analysis is not { } result) { StatusText=L.T("Noch keine gespeicherte Analyse vorhanden."); return; }
        new AnalysisWindow(result) { Owner=System.Windows.Application.Current.MainWindow }.Show();
    }
    [RelayCommand] private void CompareResults() => new ResultsWindow(Jobs.Where(j=>j.Status==JobStatus.Abgeschlossen).ToList()) { Owner=System.Windows.Application.Current.MainWindow }.Show();
    [RelayCommand] private Task ProbeHardwareAsync() => RunUiAsync(L.T("Encoder wird mit einem kurzen Encode geprüft …"),async ct=>
    {
        var v=SelectedMedia?.Options.Video??throw new InvalidOperationException(L.T("Datei auswählen."));
        var result=await hardware.ProbeEncoderAsync(versions.Active!,v.Encoder,v.GpuIndex,v.PixelFormat,ct);
        GpuProbeText=result.Details; StatusText=result.Usable?L.T("Encoder-Probe erfolgreich."):L.T("Encoder ist nicht nutzbar. libx264, libx265 oder libsvtav1 wählen.");
    });
    [RelayCommand] private void ExpertOptions()
    {
        if(SelectedMedia?.Options.Video is not { } v||Capabilities?.Find(v.Encoder) is not { } e) return;
        var dialog=new EncoderOptionsWindow(e,v.Extra) { Owner=System.Windows.Application.Current.MainWindow };
        if(dialog.ShowDialog()==true) { v.Extra=dialog.Values; RefreshSummary(); }
    }
    [RelayCommand(CanExecute=nameof(CanCopyCommand))] private void CopyCommand() { if(CanCopyCommand) Clipboard.SetText(DisplayedCommandText); }
    [RelayCommand] private void SelectOutputFolder()
    {
        if(SelectedMedia is not { } item) return;
        var dialog=new OpenFolderDialog { Title=L.T("Ausgabeordner"),InitialDirectory=Directory.Exists(item.Options.OutputDirectory)?item.Options.OutputDirectory:"" };
        if(dialog.ShowDialog()==true) item.Options.OutputDirectory=dialog.FolderName;
    }
    [RelayCommand] private Task ExportSubtitleAsync(SubtitleTrackOptions? track) => RunUiAsync(L.T("Untertitel exportieren …"),async ct=>
    {
        if(track is null||SelectedMedia is not { } item) return;
        var dialog=new SaveFileDialog { Title=L.T("Untertitel exportieren"),Filter=track.IsBitmap?L.T("Untertitel in Matroska|*.mks"):"SRT|*.srt|ASS|*.ass|WebVTT|*.vtt",FileName=Path.GetFileNameWithoutExtension(item.Info.Path)+"_"+track.Language };
        if(dialog.ShowDialog()==true) await subtitles.ExportAsync(versions.Active!,item.Info,track,dialog.FileName,ct);
    });
    [RelayCommand] private async Task SavePresetAsync()
    {
        if(SelectedMedia is not { } item) return;
        var name=TextDialog.Ask(L.T("Preset speichern"),L.T("Name des Presets"),item.Options.PresetName);
        if(string.IsNullOrWhiteSpace(name)) return;
        await RunUiAsync(L.T("Preset speichern …"),async _=>
        {
            var p=new Preset { Name=name.Trim(),Description=L.T("Benutzerdefiniertes Preset"),Options=Json.Clone(item.Options),AudioRule=new(){LanguageOrder=[]} };
            p.Options.PresetName=p.Name; await presets.SaveAsync(p); await ReloadPresetsAsync(); SelectedPreset=Presets.First(x=>x.Name==p.Name); StatusText=L.T("Preset gespeichert.");
        });
    }
    [RelayCommand] private Task EditPresetAsync() => RunUiAsync(L.T("Preset bearbeiten …"),async _=>
    {
        if(SelectedPreset is null) return; var p=JsonEditor.Edit(L.T("Preset und Audio-Standardregeln"),SelectedPreset);
        if(p is null) return; if(string.IsNullOrWhiteSpace(p.Name)||p.SchemaVersion!=1) throw new ArgumentException(L.T("Name und SchemaVersion 1 erforderlich."));
        await presets.SaveAsync(p); await ReloadPresetsAsync(); SelectedPreset=Presets.First(x=>x.Name==p.Name);
    });
    [RelayCommand] private Task DuplicatePresetAsync() => RunUiAsync(L.T("Preset duplizieren …"),async _=>
    {
        if(SelectedPreset is null) return; var p=Json.Clone(SelectedPreset);
        var name=TextDialog.Ask(L.T("Preset duplizieren"),L.T("Neuer Name"),p.Name+L.T(" – Kopie")); if(string.IsNullOrWhiteSpace(name)) return;
        p.Name=name; await presets.SaveAsync(p); await ReloadPresetsAsync(); SelectedPreset=Presets.First(x=>x.Name==p.Name);
    });
    [RelayCommand] private Task DeletePresetAsync() => RunUiAsync(L.T("Preset löschen …"),async _=>
    {
        if(SelectedPreset is not { } p) return;
        if(MessageBox.Show(L.F($"Preset „{p.Name}“ löschen?"),L.T("Preset"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
        presets.Delete(p); await ReloadPresetsAsync();
    });
    [RelayCommand] private Task ImportPresetAsync() => RunUiAsync(L.T("Preset importieren …"),async _=>
    {
        var dialog=new OpenFileDialog{Filter="FFmpeg-Studio-Preset|*.json"};
        if(dialog.ShowDialog()!=true) return; var p=await PresetService.ImportAsync(dialog.FileName);
        await presets.SaveAsync(p); await ReloadPresetsAsync(); SelectedPreset=Presets.First(x=>x.Name==p.Name);
    });
    [RelayCommand] private Task ExportPresetAsync() => RunUiAsync(L.T("Preset exportieren …"),async _=>
    {
        if(SelectedPreset is not { } p) return; var dialog=new SaveFileDialog{Filter="JSON-Preset|*.json",FileName=QueueService.SanitizeFileName(p.Name)+".json"};
        if(dialog.ShowDialog()==true) await PresetService.ExportAsync(p,dialog.FileName);
    });
    private async Task ReloadPresetsAsync() { var list=await presets.LoadAsync(); Presets.Clear(); foreach(var p in list) Presets.Add(p); }
    [RelayCommand] private void ManageFFmpeg() => new ManagementWindow(this) { Owner=System.Windows.Application.Current.MainWindow }.ShowDialog();
    [RelayCommand] private Task CheckUpdatesAsync() => RunUiAsync(L.T("FFmpeg-Version online prüfen …"),async ct=>
    {
        remote=await downloader.CheckAsync(ct); AvailableVersion=remote.Version; Settings.LastUpdateCheck=DateTimeOffset.UtcNow; await settings.SaveAsync();
        StatusText=VersionMetadata.IsNewBuild(versions.Active?.Version,remote.Version)?L.T("Ein anderer aktueller Build ist verfügbar."):L.T("Die verwaltete Build-ID ist aktuell.");
    });
    [RelayCommand] private Task InstallAsync() => RunUiAsync(L.T("FFmpeg herunterladen und installieren …"),async ct=>
    {
        remote=await downloader.CheckAsync(ct); AvailableVersion=remote.Version;
        await downloader.InstallAsync(remote,new Progress<DownloadProgress>(p=> { DownloadPercent=p.Percent; StatusText=p.ToString(); }),ct);
        await RefreshCapabilitiesAsync(ct); StatusText=L.T("FFmpeg installiert und geprüft. Bereit zum Import."); DownloadPercent=null;
    });
    [RelayCommand] private Task SelectLocalAsync() => RunUiAsync(L.T("Lokale FFmpeg-Installation prüfen …"),async ct=>
    {
        var dialog=new OpenFileDialog{Title=L.T("ffmpeg.exe auswählen"),Filter=L.T("FFmpeg|ffmpeg.exe|Programme|*.exe")};
        if(dialog.ShowDialog()!=true) return; var ffprobe=Path.Combine(Path.GetDirectoryName(dialog.FileName)!,"ffprobe.exe");
        if(!File.Exists(ffprobe))
        {
            var probeDialog=new OpenFileDialog{Title=L.T("Zugehörige ffprobe.exe auswählen"),Filter="ffprobe|ffprobe.exe"};
            if(probeDialog.ShowDialog()!=true) return; ffprobe=probeDialog.FileName;
        }
        await versions.SelectLocalAsync(dialog.FileName,ffprobe,ct); await RefreshCapabilitiesAsync(ct); StatusText=L.T("Lokale Installation geprüft. Dateien bleiben unverändert.");
    });
    [RelayCommand] private Task RollbackAsync() => RunUiAsync(L.T("Vorherige FFmpeg-Version aktivieren …"),async ct=>
    { await versions.RollbackAsync(ct); await RefreshCapabilitiesAsync(ct); StatusText=L.T("Vorherige Version aktiviert."); });
    [RelayCommand] private async Task EditSettingsAsync()
    {
        var dialog=new SettingsWindow(Settings,SelectedMedia?.Info,SelectedMedia?.Options) {Owner=System.Windows.Application.Current.MainWindow};
        if(dialog.ShowDialog()!=true) return;
        await RunUiAsync(L.T("Einstellungen speichern …"),async _=>
        {
            foreach(var rule in dialog.Result.WatchFolders.Where(r=>r.Enabled)) FolderWatchService.ValidateRule(rule);
            var edited=dialog.Result; Settings.ThemeId=ThemeCatalog.Resolve(edited.ThemeId,edited.DarkMode).Id;
            Settings.DarkMode=ThemeCatalog.Resolve(Settings.ThemeId).IsDark; Settings.RecursiveImport=edited.RecursiveImport;
            Settings.TooltipsEnabled=edited.TooltipsEnabled;Settings.TooltipDelaySeconds=TooltipTiming.Normalize(edited.TooltipDelaySeconds);
            Settings.ShowTestEncodeNotification=edited.ShowTestEncodeNotification;
            Settings.FileNaming=Json.Clone(edited.FileNaming);OnPropertyChanged(nameof(CanEditFileNamePattern));
            ButtonHints.Apply(Settings);
            Settings.AutoInstall=edited.AutoInstall; Settings.AutoUpdate=edited.AutoUpdate; Settings.CheckUpdates=edited.CheckUpdates;
            Settings.UpdateIntervalHours=Math.Clamp(edited.UpdateIntervalHours,1,720); Settings.ParallelJobs=Math.Clamp(edited.ParallelJobs,1,16);
            Settings.ParallelGpuJobs=Math.Clamp(edited.ParallelGpuJobs,1,8); Settings.CacheLimitMiB=Math.Clamp(edited.CacheLimitMiB,32,16384);
            Settings.EfficiencyMode=edited.EfficiencyMode;RefreshExecutionSettings();
            Settings.DefaultOutputDirectory=edited.DefaultOutputDirectory; Settings.WatchFolders=edited.WatchFolders;
            Settings.CompletionAction=edited.CompletionAction;
            Settings.Language=L.Normalize(edited.Language);
            await settings.SaveAsync(); App.ApplyLanguage(Settings.Language); App.ApplyTheme(Settings.ThemeId); StatusText=L.T("Einstellungen gespeichert."); OnPropertyChanged(nameof(Settings));RefreshSummary();
        });
    }
    [RelayCommand] private void OpenOutput()
    {
        if(SelectedJob is not { } job) return; OpenPath(Path.GetDirectoryName(job.OutputPath)!);
    }
    [RelayCommand] private void OpenLogs() => OpenPath(paths.Logs);
    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute=false,ArgumentList={path} });
    [RelayCommand] private void ShowJobLog()
    {
        if(SelectedJob is not { } job||!File.Exists(job.LogPath)) {StatusText=L.T("Noch kein Jobprotokoll vorhanden.");return;}
        new TextWindow(L.T("FFmpeg-Protokoll"),File.ReadAllText(job.LogPath)) {Owner=System.Windows.Application.Current.MainWindow}.Show();
    }
    private static string DescribeHardware(SensorSnapshot s)
    {
        static string N(double? value,string suffix=" %")=>value.HasValue?$"{value:0.0}{suffix}":L.T("nicht verfügbar");
        return $"CPU {N(s.CpuPercent)}  ·  RAM {N(s.RamPercent)}"+(s.Gpus.Count==0?L.T("  ·  NVIDIA: nicht verfügbar"):"\n"+string.Join("\n",s.Gpus.Select(g=>L.F($"GPU {g.Index}: {g.Name} · Treiber {g.Driver} · GPU {N(g.Utilization)} · NVENC {N(g.EncoderPercent)} · NVDEC {N(g.DecoderPercent)} · VRAM {N(g.MemoryUsedMiB," MiB")}/{N(g.MemoryTotalMiB," MiB")} · {N(g.Temperature," °C")} · {N(g.Power," W")}"))));
    }
    private async Task MonitorLoopAsync()
    {
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                var s=await monitor.ReadAsync(lifetime.Token);
                lastSensors=s; HardwareText=DescribeHardware(s);
                await Task.Delay(3000,lifetime.Token);
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception e) { log.Write("Hardwaremonitor",e); HardwareText=L.T("Hardwaremonitor nicht verfügbar."); }
    }
    private async Task UpdateLoopAsync()
    {
        try
        {
            await Task.Delay(1500,lifetime.Token);
            while(!lifetime.IsCancellationRequested)
            {
                if(Settings.CheckUpdates&&(Settings.LastUpdateCheck is null||DateTimeOffset.UtcNow-Settings.LastUpdateCheck>=TimeSpan.FromHours(Settings.UpdateIntervalHours)))
                {
                    try
                    {
                        remote=await downloader.CheckAsync(lifetime.Token); AvailableVersion=remote.Version; Settings.LastUpdateCheck=DateTimeOffset.UtcNow; await settings.SaveAsync();
                        if(Settings.AutoUpdate&&versions.Active?.Managed==true&&VersionMetadata.IsNewBuild(versions.Active.Version,remote.Version)&&!IsBusy) await InstallAsync();
                    }
                    catch(Exception e) when(e is not OperationCanceledException) { log.Write(L.T("Automatische Updateprüfung nicht möglich."),e); AvailableVersion=L.T("Online-Prüfung fehlgeschlagen"); }
                }
                await Task.Delay(TimeSpan.FromMinutes(1),lifetime.Token);
            }
        }
        catch(OperationCanceledException) { }
    }
    private async Task CompleteBatchAsync()
    {
        var action=Settings.CompletionAction;
        if(queue.Jobs.Any(j=>j.Status!=JobStatus.Abgeschlossen)||queue.IsRunning) return;
        if(action==CompletionAction.Signalton) System.Media.SystemSounds.Asterisk.Play();
        if(action==CompletionAction.Benachrichtigung)
        {
            using var iconStream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/PixelPeeker;component/Assets/PixelPeeker.ico")).Stream;
            using var icon=new System.Drawing.Icon(iconStream);
            using var notification=new System.Windows.Forms.NotifyIcon { Icon=icon,Visible=true,BalloonTipTitle=AppIdentity.Name,BalloonTipText=L.T("Alle Jobs erfolgreich abgeschlossen.") };
            notification.ShowBalloonTip(5000); await Task.Delay(6000,lifetime.Token);
        }
        if(action==CompletionAction.AnwendungSchließen) System.Windows.Application.Current.MainWindow.Close();
        if(action is CompletionAction.Energiesparen or CompletionAction.Herunterfahren)
        {
            var confirm=new CountdownWindow(action) {Owner=System.Windows.Application.Current.MainWindow};
            if(confirm.ShowDialog()!=true||queue.Jobs.Any(j=>j.Status!=JobStatus.Abgeschlossen)||queue.IsRunning) return;
            Settings.CompletionAction=CompletionAction.Nichts;
            if(action==CompletionAction.Energiesparen) System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend,false,false);
            else Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shutdown.exe")) {UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/s","/t","0"}});
        }
    }
    public Task ShutdownAsync() => shutdown??=ShutdownCoreAsync();
    private async Task ShutdownCoreAsync()
    {
        RememberSelectedVideo();codecSaveTimer?.Stop();
        refreshTimer.Stop(); operation?.Cancel(); await lifetime.CancelAsync();
        await CompareEncodes.ShutdownAsync();
        await ShutdownPlaybackAsync();
        await Task.WhenAll(previewTasks.ToArray());
        await accuratePreview.DisposeAsync();
        await Task.WhenAll(analysisOperations.Values.Select(a=>a.Completion.Task).ToArray());
        await watcher.DisposeAsync(); await queue.DisposeAsync();
        if(monitorTask is not null) await monitorTask; if(updateTask is not null) await updateTask;
        await queueSettingsSave;
        await codecSettingsSave;
        await settings.SaveAsync();
    }
    public bool HasActiveJobs=>queue.IsRunning;
    internal void RefreshLanguage(string previous)
    {
        CompareEncodes.RefreshLanguage(previous);
        UpdatePreviewStatus();
        RefreshCuts();
        StatusText=L.Retranslate(StatusText,previous); InstalledVersion=L.Retranslate(InstalledVersion,previous);
        AvailableVersion=L.Retranslate(AvailableVersion,previous); HardwareText=L.Retranslate(HardwareText,previous);
        if(lastSensors is {} sensors)HardwareText=DescribeHardware(sensors);
        foreach(var item in Media) item.RefreshLanguage();
        foreach(var job in Jobs)
        {
            job.AnalysisStatus=L.Retranslate(job.AnalysisStatus,previous);
            job.RefreshLanguage();
        }
        UpdateQueueSummary(); RefreshEncoderChoices(false); RefreshSummary();
        if(SelectedMedia is {} media) MediaDetails=MediaDetailsText.Format(media.Info);
        else { Conversion=L.T("Dateien importieren, um die Konvertierung einzurichten."); MediaDetails=L.T("Noch keine Datei ausgewählt."); EncoderInfo=L.T("Noch kein Encoder erkannt."); }
        OnPropertyChanged(nameof(SelectedJobDetails));
    }
    public static BitmapImage LoadImage(string path)
    {
        var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.UriSource=new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze(); return image;
    }
}
