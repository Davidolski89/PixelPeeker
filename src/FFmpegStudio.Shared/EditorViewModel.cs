using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

/// <summary>
/// Shared editor used by the Avalonia shell. The WPF shell keeps its own view and calls the same
/// encoder, cut and command helpers so both surfaces share that logic.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly SettingsService settings;
    private readonly FFmpegVersionService versions;
    private readonly IFmpegDistribution distribution;
    private readonly FFmpegCapabilityService capabilityService;
    private readonly MediaProbeService probe;
    private readonly PreviewService preview;
    private readonly FrameNavigationService frames;
    private readonly QueueService queue;
    private readonly QueueStore store;
    private readonly PresetService presets;
    private readonly FFmpegCommandBuilder builder;
    private readonly LoggingService log;
    private readonly IPlaybackSession playback;
    private readonly IEditorPrompts prompts;
    private readonly IUiMarshal ui;
    private readonly AppPaths paths;
    private readonly QualityAnalysisService quality;
    private readonly SubtitleService subtitles;
    private readonly EncodeComparisonService comparison;
    private readonly FolderWatchService watcher;
    private readonly IHardwareMonitor hardware;
    private readonly CancellationTokenSource background = new();
    private readonly List<INotifyPropertyChanged> observed = [];
    private readonly HashSet<string> importing = new(StringComparer.OrdinalIgnoreCase);
    private INotifyCollectionChanged? observedCuts;
    private bool adjustingEncoder;
    private bool applyingPreset;
    private bool initialized;
    private int saveGeneration;
    private RemoteBuild? remote;

    public EditorViewModel(SettingsService settings, FFmpegVersionService versions, IFmpegDistribution distribution,
        FFmpegCapabilityService capabilityService, MediaProbeService probe, PreviewService preview, FrameNavigationService frames,
        QueueService queue, QueueStore store, PresetService presets, FFmpegCommandBuilder builder, LoggingService log,
        IPlaybackSession playback, IEditorPrompts prompts, IUiMarshal ui, AppPaths paths, QualityAnalysisService quality,
        SubtitleService subtitles, EncodeComparisonService comparison, FolderWatchService watcher, IHardwareMonitor hardware)
    {
        this.settings = settings;
        this.versions = versions;
        this.distribution = distribution;
        this.capabilityService = capabilityService;
        this.probe = probe;
        this.preview = preview;
        this.frames = frames;
        this.queue = queue;
        this.store = store;
        this.presets = presets;
        this.builder = builder;
        this.log = log;
        this.playback = playback;
        this.prompts = prompts;
        this.ui = ui;
        this.paths = paths;
        this.quality = quality;
        this.subtitles = subtitles;
        this.comparison = comparison;
        this.watcher = watcher;
        this.hardware = hardware;
        watcher.OnReady = async (path, rule, ct) =>
        {
            var active = versions.Active ?? throw new InvalidOperationException(L.T("FFmpeg fehlt für den Ordnerimport."));
            var media = await probe.ProbeAsync(active, path, ct);
            if (media.Video is null) throw new InvalidDataException(L.T("Keine Videospur gefunden."));
            var preset = (await presets.LoadAsync()).FirstOrDefault(item => item.Name == rule.PresetName) ?? throw new InvalidOperationException(L.T("Überwachungspreset nicht gefunden."));
            var options = PresetService.Apply(preset, media);
            options.OutputDirectory = rule.OutputFolder;
            var job = await queue.AddAsync(media, options, ct);
            if (rule.AutoStart) queue.Start(job);
        };
        Playback = playback;
        playback.Changed += signal => ui.Invoke(() =>
        {
            if (signal.Kind == PlaybackSignalKind.Time && PlaybackActive) PreviewSeconds = signal.Value;
            if (signal.Kind is PlaybackSignalKind.Ended or PlaybackSignalKind.Failed)
            {
                PlaybackActive = false;
                PlaybackBusy = false;
            }
        });
        queue.JobAdded += job => ui.Invoke(() => { Jobs.Add(job); SelectedJob = job; UpdateQueueSummary(); });
        queue.JobChanged += _ => ui.Invoke(() => { UpdateQueueSummary(); OnPropertyChanged(nameof(SelectedJobDetails)); RefreshSummary(); });
        queue.BatchCompleted += () => ui.Invoke(() => _ = CompleteBatchAsync());
    }

    public IPlaybackSession Playback { get; }
    public Func<Task>? PreparePlaybackSurface { get; set; }
    public ObservableCollection<EditorMedia> Media { get; } = [];
    public ObservableCollection<EncodingJob> Jobs { get; } = [];
    public ObservableCollection<Preset> Presets { get; } = [];
    public ObservableCollection<string> VideoEncoders { get; } = [];
    public ObservableCollection<string> PixelFormats { get; } = [];
    public ObservableCollection<string> PresetChoices { get; } = [];
    public ObservableCollection<string> TuneChoices { get; } = [];
    public ObservableCollection<string> ProfileChoices { get; } = [];
    public ObservableCollection<string> VideoPresetChoices { get; } = [];
    public static string[] Containers { get; } = ["mkv", "mp4", "webm", "mov"];
    public static string[] Algorithms { get; } = ["lanczos", "bicubic", "bilinear", "spline", "area", "neighbor"];
    public AppSettings Settings => settings.Current;
    public Capabilities? Capabilities { get; private set; }
    public string PhaseNote => L.T("Avalonia-Editor. Wiedergabe nutzt die Systembibliothek libvlc. Linux lädt geprüfte statische FFmpeg-Builds, sofern glibc 2.28 oder neuer verfügbar ist.");
    public string SettingsLabel => L.T("Einstellungen");
    public string CompareLabel => L.T("Encodes vergleichen");
    public string AnalyzeLabel => L.T("Qualitätsanalyse");
    public string CancelAnalysisLabel => L.T("Analyse abbrechen");
    public string ExpertLabel => L.T("Expertenoptionen");
    public string TestEncodeLabel => L.T("Testencode");
    public string ExportSubtitleLabel => L.T("Untertitel exportieren");
    public string SystemFfmpegLabel => L.T("System-FFmpeg");
    public string PreviousFrameLabel => L.T("Vorheriges Bild");
    public string NextFrameLabel => L.T("Nächstes Bild");
    public string SubtitleHeading => L.T("Untertitel");
    public string OpenFileLabel => L.T("Datei öffnen");
    public string LibraryHeading => L.T("Bibliothek");
    public string MediaHeading => L.T("Mediendaten");
    public string QueueHeading => L.T("Queue");
    public string CommandHeading => L.T("FFmpeg-Aufruf");
    public string JobHeading => L.T("Job");
    public string EncodeHeading => L.T("Encoder");
    public string FilterHeading => L.T("Filter");
    public string CutHeading => L.T("Schnitt");
    [ObservableProperty] private EditorMedia? selectedMedia;
    [ObservableProperty] private EncodingJob? selectedJob;
    [ObservableProperty] private Preset? selectedPreset;
    [ObservableProperty] private string statusText = L.T("PixelPeeker wird vorbereitet …");
    [ObservableProperty] private string installedVersion = L.T("Nicht installiert");
    [ObservableProperty] private string distributionNote = "";
    [ObservableProperty] private string mediaDetails = L.T("Noch keine Datei ausgewählt.");
    [ObservableProperty] private string queueSummary = L.T("Keine Jobs");
    [ObservableProperty] private string commandText = "";
    [ObservableProperty] private string validationText = "";
    [ObservableProperty] private string encoderInfo = "";
    [ObservableProperty] private string conversion = "";
    [ObservableProperty] private bool canSetPreset;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double? downloadPercent;
    [ObservableProperty] private double previewSeconds;
    [ObservableProperty] private string? previewPath;
    [ObservableProperty] private SourceFramePosition? displayedFrame;
    [ObservableProperty] private double testSeconds = 10;
    [ObservableProperty] private SubtitleTrackOptions? selectedSubtitle;
    [ObservableProperty] private SourceFramePosition? pendingCutStart;
    [ObservableProperty] private CutRegion? selectedCut;

    public string SelectedEncoder
    {
        get => SelectedMedia?.Options?.Video.Encoder ?? "";
        set
        {
            if (adjustingEncoder || SelectedMedia?.Options is not { } options || string.IsNullOrWhiteSpace(value) || value == options.Video.Encoder) return;
            settings.Current.Codecs.Remember(options.Video);
            options.Video.Encoder = value;
            RefreshEncoderChoices(true);
            RememberSelectedVideo();
            RefreshSummary();
        }
    }
    public string SelectedContainer
    {
        get => SelectedMedia?.Options?.Container ?? "";
        set
        {
            if (SelectedMedia?.Options is not { } options || value == options.Container || string.IsNullOrWhiteSpace(value)) return;
            options.Container = value;
            OnPropertyChanged();
            RefreshSummary();
        }
    }
    public double VideoQuality
    {
        get => SelectedMedia?.Options?.Video.Quality ?? 0;
        set
        {
            if (SelectedMedia?.Options is not { } options || Math.Abs(options.Video.Quality - value) < 0.0001) return;
            options.Video.Quality = value;
            OnPropertyChanged();
            RememberSelectedVideo();
            RefreshSummary();
        }
    }
    public string VideoPreset
    {
        get => SelectedMedia?.Options?.Video.Preset ?? "";
        set
        {
            if (adjustingEncoder || SelectedMedia?.Options is not { } options || value == (options.Video.Preset ?? "")) return;
            options.Video.Preset = value;
            OnPropertyChanged();
            RememberSelectedVideo();
            RefreshSummary();
        }
    }
    public int OutputWidth
    {
        get => SelectedMedia?.Options?.Filters.Width ?? 0;
        set => SetFilter(filters => filters.Width = value);
    }
    public int OutputHeight
    {
        get => SelectedMedia?.Options?.Filters.Height ?? 0;
        set => SetFilter(filters => filters.Height = value);
    }
    public bool CropEnabled
    {
        get => SelectedMedia?.Options?.Filters.CropEnabled ?? false;
        set => SetFilter(filters => filters.CropEnabled = value);
    }
    public IReadOnlyList<CutRegion> CutEntries => SelectedMedia?.Options?.Cuts.ToArray() ?? [];
    public string CutSummary => SelectedMedia?.Options is not { } options || SelectedMedia.Info is not { } info
        ? L.T("Eine Videodatei auswählen.")
        : !CutTimeline.Active(options)
            ? L.T("Kein aktiver Schnitt. Das gesamte Video bleibt erhalten.")
            : L.F($"{options.Cuts.Count} Schnittbereiche · verbleibende Dauer ca. {CutTimeline.Duration(info, options):0.###} s");
    public string SelectedJobDetails => SelectedJob is null
        ? L.T("Einen Job auswählen, um Ergebnis, Protokoll und Messwerte zu sehen.")
        : string.Join(Environment.NewLine, new[] { SelectedJob.Name, SelectedJob.DisplayStatus, SelectedJob.OutputPath, SelectedJob.Error, SelectedJob.AnalysisStatus, SelectedJob.AnalysisDetails }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public IReadOnlyList<SubtitleTrackOptions> SubtitleTracks => SelectedMedia?.Options?.Subtitles.ToArray() ?? [];
    public IReadOnlyList<AudioTrackOptions> AudioTracks => SelectedMedia?.Options?.Audio.ToArray() ?? [];
    private AudioTrackOptions? selectedAudio;
    public AudioTrackOptions? SelectedAudio
    {
        get => selectedAudio;
        set
        {
            if (ReferenceEquals(selectedAudio, value)) return;
            selectedAudio = value;
            OnPropertyChanged();
        }
    }
    public string OutputFileNamePreview => FileNamePreview.Of(SelectedMedia?.Info, SelectedMedia?.Options, Settings.FileNaming);
    public string HardwareText { get; private set; } = "";
    public string AudioHeading => L.T("Audio");
    public string HardwareHeading => L.T("Hardware");
    public string ResultsLabel => L.T("Ergebnisse");
    public string FileNameHeading => L.T("Dateiname");
    public string RemoveLabel => L.T("Entfernen");
    public string PlayLabel => L.T("Abspielen");
    public string PauseLabel => L.T("Pause");
    public string StopLabel => L.T("Stopp");
    public string PreviewLabel => L.T("Vorschau");
    public string CutStartLabel => L.T("Start");
    public string CutEndLabel => L.T("Ende");
    public string ClearCutsLabel => L.T("Leeren");
    public string EncoderLabel => L.T("Encoder");
    public string ContainerLabel => L.T("Container");
    public string QualityLabel => L.T("Qualität");
    public string PresetLabel => L.T("Preset");
    public string WidthHeightLabel => L.T("Breite / Höhe");
    public string DetectCropLabel => L.T("Schwarze Balken erkennen …");
    public string CropLabel => L.T("Crop aktivieren");
    public string PresetsHeading => L.T("Presets");
    public string SavePresetLabel => L.T("Speichern");
    public string DeletePresetLabel => L.T("Löschen");
    public string ImportPresetLabel => L.T("Import");
    public string ExportPresetLabel => L.T("Export");
    public string AddQueueLabel => L.T("Queue");
    public string StartQueueLabel => L.T("Start");
    public string StopQueueLabel => L.T("Stopp");
    public string CancelJobLabel => L.T("Abbrechen");
    public string InstallFfmpegLabel => L.T("FFmpeg installieren");
    public string CheckFfmpegLabel => L.T("Updates prüfen");
    public bool HasEditableMedia => SelectedMedia?.CanEdit == true;

    public async Task InitializeAsync()
    {
        await settings.LoadAsync();
        L.SetLanguage(settings.Current.Language);
        DistributionNote = distribution.SourceDescription;
        string? startupError = null;
        try
        {
            await versions.LoadAsync();
            InstalledVersion = versions.Active?.Version ?? L.T("Nicht installiert");
            if (versions.Active is not null) Capabilities = await capabilityService.AnalyzeAsync(versions.Active);
        }
        catch (Exception e)
        {
            log.Write(L.T("FFmpeg-Startprüfung fehlgeschlagen."), e);
            InstalledVersion = L.T("Nicht installiert");
            startupError = e.Message;
        }
        await store.InitializeAsync();
        await queue.InitializeAsync();
        ui.Invoke(() =>
        {
            Jobs.Clear();
            foreach (var job in queue.Jobs) Jobs.Add(job);
            UpdateQueueSummary();
        });
        await ReloadPresetsAsync();
        settings.Current.Codecs.Normalize();
        settings.Current.Codecs.SeedFromJobs(Jobs);
        initialized = true;
        RefreshLanguage();
        StatusText = startupError ?? (versions.Active is not null
            ? L.T("Bereit · Dateien oder Ordner in die Bibliothek ziehen.")
            : distribution.SupportsManagedInstall
                ? L.T("FFmpeg fehlt. „FFmpeg installieren“ lädt einen geprüften Build, oder ein vorhandenes ffmpeg im PATH kann verwendet werden.")
                : distribution.SourceDescription);
        if (versions.Active is null && settings.Current.AutoInstall && distribution.SupportsManagedInstall)
            await InstallFfmpegAsync();
        watcher.Start();
        _ = MonitorLoopAsync(background.Token);
    }

    public Action? CloseRequested { get; set; }
    public Func<CompletionAction, Task<bool>>? CompletionRequested { get; set; }
    public Func<IReadOnlyList<EncodingJob>, Task>? ResultsRequested { get; set; }

    public void StopBackground() => background.Cancel();

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var snapshot = await hardware.ReadAsync(ct);
                ui.Invoke(() => { HardwareText = HardwareReadout.Format(snapshot); OnPropertyChanged(nameof(HardwareText)); });
                await Task.Delay(3000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            log.Write(L.T("Hardwaremonitor nicht verfügbar."), e);
            ui.Invoke(() => { HardwareText = L.T("Hardwaremonitor nicht verfügbar."); OnPropertyChanged(nameof(HardwareText)); });
        }
    }

    private async Task CompleteBatchAsync()
    {
        var action = Settings.CompletionAction;
        if (action == CompletionAction.Nichts) return;
        if (Jobs.Any(job => job.Status != JobStatus.Abgeschlossen) || queue.IsRunning) return;
        if (CompletionRequested is not null && !await CompletionRequested(action))
        {
            StatusText = L.T("Abgebrochen");
            return;
        }
        if (action == CompletionAction.AnwendungSchließen)
        {
            CloseRequested?.Invoke();
            return;
        }
        if (action == CompletionAction.Signalton)
        {
            SessionAlerts.Beep();
            StatusText = SessionAlerts.Notify();
            return;
        }
        if (action == CompletionAction.Benachrichtigung)
        {
            StatusText = SessionAlerts.Notify();
            return;
        }
        if (action is CompletionAction.Energiesparen or CompletionAction.Herunterfahren)
        {
            if (CompletionRequested is null)
            {
                StatusText = L.T("Diese Energieaktion wird hier nicht unterstützt.");
                return;
            }
            Settings.CompletionAction = CompletionAction.Nichts;
            await settings.SaveAsync();
            StatusText = await SessionPower.ExecuteAsync(action);
            return;
        }
        StatusText = L.EnumText(action);
    }

    [RelayCommand]
    private Task ShowResultsAsync() => ResultsRequested?.Invoke(Jobs.ToArray()) ?? Task.CompletedTask;

    [RelayCommand]
    private Task ImportFilesAsync() => RunAsync(L.T("Dateien werden analysiert …"), async _ =>
    {
        var files = await prompts.PickOpenFilesAsync();
        if (files is { Count: > 0 }) await ImportCoreAsync(files, CancellationToken.None);
    });

    public Task ImportPathsAsync(IEnumerable<string> input) => RunAsync(L.T("Dateien werden analysiert …"), ct => ImportCoreAsync(input, ct));

    private async Task ImportCoreAsync(IEnumerable<string> input, CancellationToken ct)
    {
        var recursive = settings.Current.RecursiveImport;
        var files = await Task.Run(() => input.SelectMany(path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            : [path]).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ct);
        var added = 0;
        var failed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (Media.Any(item => item.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            if (!importing.Add(file)) continue;
            try
            {
                if (!File.Exists(file)) { failed++; log.Write(L.F($"Import fehlgeschlagen: {file}")); continue; }
                EditorMedia entry;
                if (versions.Active is not { } tools)
                    entry = new EditorMedia(file, null, null);
                else
                {
                    var info = await probe.ProbeAsync(tools, file, ct);
                    if (info.Video is null) throw new InvalidDataException(L.T("Keine Videospur vorhanden."));
                    var options = EncodingOptions.ForMedia(info);
                    if (settings.Current.Codecs.LastVideoEncoder.Length > 0 && VideoEncoderPolicy.IsAllowed(settings.Current.Codecs.LastVideoEncoder) && Capabilities?.Find(settings.Current.Codecs.LastVideoEncoder) is not null)
                    {
                        options.Video.Encoder = settings.Current.Codecs.LastVideoEncoder;
                        CodecMemory.CopyVideo(settings.Current.Codecs.ForVideo(options.Video.Encoder), options.Video);
                    }
                    if (info.Video.IsHdr) options.Video.PixelFormat = "yuv420p10le";
                    if (settings.Current.DefaultOutputDirectory.Length > 0) options.OutputDirectory = settings.Current.DefaultOutputDirectory;
                    entry = new EditorMedia(file, info, options);
                }
                ui.Invoke(() => { Media.Add(entry); SelectedMedia = entry; });
                if (entry.CanEdit && versions.Active is { } thumbTools && entry.Info is { } thumbInfo)
                {
                    try
                    {
                        var at = Math.Min(3, Math.Max(0, (thumbInfo.Duration ?? 10) * .1));
                        entry.ThumbnailPath = await preview.FrameAsync(thumbTools, thumbInfo, at, width: 320, ct: ct);
                        if (ReferenceEquals(SelectedMedia, entry)) PreviewPath ??= entry.ThumbnailPath;
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { log.Write(L.T("Vorschaubild"), e); }
                }
                added++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                log.Write(L.F($"Import fehlgeschlagen: {file}"), e);
            }
            finally { importing.Remove(file); }
        }
        StatusText = L.F($"{added} Dateien importiert · {failed} nicht lesbar oder ohne Video.");
    }

    [RelayCommand] private void RemoveMedia()
    {
        if (SelectedMedia is { } media) Media.Remove(media);
        SelectedMedia = Media.FirstOrDefault();
    }

    partial void OnSelectedMediaChanged(EditorMedia? value)
    {
        foreach (var item in observed) item.PropertyChanged -= OptionsChanged;
        observed.Clear();
        if (observedCuts is not null) observedCuts.CollectionChanged -= CutsChanged;
        observedCuts = value?.Options?.Cuts;
        if (observedCuts is not null) observedCuts.CollectionChanged += CutsChanged;
        PendingCutStart = null;
        SelectedCut = null;
        if (value?.Options is { } options)
        {
            Observe(options);
            Observe(options.Video);
            Observe(options.Filters);
            foreach (var audio in options.Audio) Observe(audio);
            SelectedAudio = options.Audio.FirstOrDefault();
            MediaDetails = value.Details;
            PreviewPath = value.ThumbnailPath;
            var matching = Presets.FirstOrDefault(preset => preset.Name == options.PresetName);
            if (!ReferenceEquals(SelectedPreset, matching)) { selectedPreset = matching; OnPropertyChanged(nameof(SelectedPreset)); }
            PreviewSeconds = Math.Min(3, Math.Max(0, (value.Info?.Duration ?? 10) * .1));
        }
        else
        {
            MediaDetails = value?.Details ?? L.T("Noch keine Datei ausgewählt.");
            PreviewPath = null;
            SelectedAudio = null;
        }
        OnPropertyChanged(nameof(HasEditableMedia));
        OnPropertyChanged(nameof(SubtitleTracks));
        OnPropertyChanged(nameof(AudioTracks));
        OnPropertyChanged(nameof(OutputFileNamePreview));
        SelectedSubtitle = value?.Options?.Subtitles.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedEncoder));
        OnPropertyChanged(nameof(SelectedContainer));
        OnPropertyChanged(nameof(VideoQuality));
        OnPropertyChanged(nameof(VideoPreset));
        OnPropertyChanged(nameof(OutputWidth));
        OnPropertyChanged(nameof(OutputHeight));
        OnPropertyChanged(nameof(CropEnabled));
        RefreshEncoderChoices(false);
        RefreshCuts();
        RefreshSummary();
        if (value?.CanEdit == true) RememberSelectedVideo();
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (!initialized || applyingPreset || SelectedMedia?.Info is not { } info || SelectedMedia.Options is null || value is null) return;
        applyingPreset = true;
        try
        {
            SelectedMedia.Options = PresetService.Apply(value, info, SelectedMedia.Options);
            OnSelectedMediaChanged(SelectedMedia);
            StatusText = L.F($"Preset „{value.Name}“ angewendet.");
        }
        finally { applyingPreset = false; }
    }

    partial void OnSelectedJobChanged(EncodingJob? value) => OnPropertyChanged(nameof(SelectedJobDetails));

    private void Observe(INotifyPropertyChanged value)
    {
        observed.Add(value);
        value.PropertyChanged += OptionsChanged;
    }

    private void OptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (adjustingEncoder || applyingPreset) return;
        if (SelectedMedia?.Options is { } options && options.PresetName != "Benutzerdefiniert")
        {
            options.PresetName = "Benutzerdefiniert";
            if (SelectedPreset is not null) SelectedPreset = null;
        }
        if (sender is VideoOptions && e.PropertyName == nameof(VideoOptions.Encoder)) RefreshEncoderChoices(true);
        if (sender is EncodingOptions && e.PropertyName == nameof(EncodingOptions.CutsEnabled) && SelectedMedia?.Options is { } current)
            CutEditing.EnsureAudio(current);
        RefreshSummary();
        RememberSelectedVideo();
    }

    private void CutsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCuts();
        RefreshSummary();
    }

    private void RefreshEncoderChoices(bool defaults)
    {
        VideoEncoders.Clear();
        if (Capabilities is not null)
            foreach (var encoder in Capabilities.ModernVideo) VideoEncoders.Add(encoder.Name);
        if (SelectedMedia?.Options is not { } options || Capabilities is null)
        {
            CanSetPreset = false;
            EncoderInfo = "";
            OnPropertyChanged(nameof(SelectedEncoder));
            return;
        }
        adjustingEncoder = true;
        try
        {
            var state = EncoderChoices.Apply(options.Video, Capabilities, defaults, CutTimeline.Active(options),
                video => CodecMemory.CopyVideo(settings.Current.Codecs.ForVideo(video.Encoder), video),
                PixelFormats, PresetChoices, TuneChoices, ProfileChoices, VideoPresetChoices,
                () => VideoPresentation.PresetValues(options.Video.Encoder, PresetChoices, options.Video.Preset, VideoPresentation.PresetDefault(Capabilities.Find(options.Video.Encoder)))
                    .Select(value => VideoPresentation.PresetLabel(value, options.Video.Encoder, VideoPresentation.PresetDefault(Capabilities.Find(options.Video.Encoder)))));
            EncoderInfo = state.EncoderInfo;
            CanSetPreset = state.CanSetPreset;
            OnPropertyChanged(nameof(SelectedEncoder));
            OnPropertyChanged(nameof(VideoPreset));
            OnPropertyChanged(nameof(VideoQuality));
        }
        finally { adjustingEncoder = false; }
    }

    private void RefreshSummary()
    {
        if (SelectedMedia?.Info is not { } info || SelectedMedia.Options is not { } options)
        {
            CommandText = "";
            ValidationText = "";
            Conversion = "";
            return;
        }
        var executable = versions.Active?.FFmpegPath ?? (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        var current = EncodeCommandPreview.ForMedia(info, options, settings.Current, Capabilities, builder, executable);
        Conversion = current.Conversion;
        ValidationText = current.Validation;
        CommandText = current.Command.Length == 0 ? current.Validation : current.Command;
        OnPropertyChanged(nameof(CutSummary));
        OnPropertyChanged(nameof(OutputFileNamePreview));
        OnPropertyChanged(nameof(AudioTracks));
    }

    private void RefreshCuts()
    {
        OnPropertyChanged(nameof(CutEntries));
        OnPropertyChanged(nameof(CutSummary));
    }

    private void SetFilter(Action<FilterOptions> change)
    {
        if (SelectedMedia?.Options?.Filters is not { } filters) return;
        change(filters);
        OnPropertyChanged(nameof(OutputWidth));
        OnPropertyChanged(nameof(OutputHeight));
        OnPropertyChanged(nameof(CropEnabled));
        RefreshSummary();
    }

    private void RememberSelectedVideo()
    {
        if (SelectedMedia?.Options is not { } options) return;
        settings.Current.Codecs.Remember(options.Video);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        var token = ++saveGeneration;
        _ = SaveLater();
        async Task SaveLater()
        {
            try
            {
                await Task.Delay(400);
                if (token == saveGeneration) await settings.SaveAsync();
            }
            catch (Exception e) { log.Write(L.T("Codec-Einstellungen konnten nicht gespeichert werden."), e); }
        }
    }

    [RelayCommand] private Task ShowPreviewAsync() => RunAsync(L.T("Vorschau wird geladen …"), ct => LoadExactFrameAsync(PreviewSeconds, FrameSeek.Nearest, ct));
    [RelayCommand] private Task PreviousFrameAsync() => RunAsync(L.T("Vorschau wird geladen …"), ct => LoadExactFrameAsync(DisplayedFrame?.Seconds ?? PreviewSeconds, FrameSeek.Previous, ct));
    [RelayCommand] private Task NextFrameAsync() => RunAsync(L.T("Vorschau wird geladen …"), ct => LoadExactFrameAsync(DisplayedFrame?.Seconds ?? PreviewSeconds, FrameSeek.Next, ct));

    internal async Task LoadExactFrameAsync(double seconds, FrameSeek direction, CancellationToken ct)
    {
        if (SelectedMedia?.Info is not { } info || versions.Active is not { } tools)
            throw new InvalidOperationException(L.T("Datei und FFmpeg werden für die Vorschau benötigt."));
        var frame = await frames.ResolveAsync(tools, info, seconds, direction, ct);
        var width = Math.Min(1280, info.Video?.Width ?? 1280);
        PreviewPath = await preview.FrameAsync(tools, info, frame.Seconds, width: width, exactFrame: frame, ct: ct);
        DisplayedFrame = frame;
        PreviewSeconds = frame.Seconds;
        StatusText = L.F($"Geladenes Quellbild: {frame.Seconds:0.000000} s");
    }

    [RelayCommand] private async Task MarkCutStartAsync()
    {
        if (SelectedMedia?.Info is null) return;
        PendingCutStart = await ResolveFrameAsync(PreviewSeconds);
        RefreshCuts();
    }

    [RelayCommand] private async Task MarkCutEndAsync()
    {
        if (SelectedMedia?.Options is null || PendingCutStart is not { } start) return;
        var end = await ResolveFrameAsync(PreviewSeconds);
        if (end.Seconds <= start.Seconds) { StatusText = L.T("Das Schnittende muss hinter dem Start liegen."); return; }
        SelectedCut = CutEditing.Add(SelectedMedia.Options, start, end);
        PendingCutStart = null;
        RefreshCuts();
        RefreshSummary();
    }

    [RelayCommand] private void RemoveCut()
    {
        if (SelectedMedia?.Options is null || SelectedCut is null) return;
        SelectedMedia.Options.Cuts.Remove(SelectedCut);
        SelectedCut = null;
    }

    [RelayCommand] private void ClearCuts()
    {
        SelectedMedia?.Options?.Cuts.Clear();
        PendingCutStart = null;
        SelectedCut = null;
    }

    private async Task<SourceFramePosition> ResolveFrameAsync(double seconds)
    {
        if (SelectedMedia?.Info is { } info && versions.Active is { } tools)
            return await frames.ResolveAsync(tools, info, seconds);
        return new SourceFramePosition(Math.Max(0, seconds), 0);
    }

    [RelayCommand] private Task DetectCropAsync() => RunAsync(L.T("Schwarze Balken werden geprüft …"), async ct =>
    {
        if (SelectedMedia?.Info is not { } info || SelectedMedia.Options is null || versions.Active is not { } tools)
            throw new InvalidOperationException(L.T("Datei und FFmpeg werden für die Balkenerkennung benötigt."));
        var crop = await preview.DetectCropAsync(tools, info, ct);
        var accept = await prompts.ConfirmAsync(L.T("Schwarze Balken"), L.F($"Crop-Vorschlag übernehmen?\nLinks {crop.Left}, rechts {crop.Right}, oben {crop.Top}, unten {crop.Bottom} Pixel."));
        if (accept)
        {
            var filters = SelectedMedia.Options.Filters;
            filters.CropLeft = crop.Left;
            filters.CropRight = crop.Right;
            filters.CropTop = crop.Top;
            filters.CropBottom = crop.Bottom;
            filters.CropEnabled = true;
            OnPropertyChanged(nameof(CropEnabled));
        }
        StatusText = L.T("Balkenerkennung abgeschlossen.");
    });

    [RelayCommand] private Task AddQueueAsync() => RunAsync(L.T("Job wird angelegt …"), async ct =>
    {
        var item = SelectedMedia ?? throw new InvalidOperationException(L.T("Bitte eine Datei auswählen."));
        if (item.Info is null || item.Options is null) throw new InvalidOperationException(L.T("Die Datei wurde noch nicht analysiert."));
        await queue.AddAsync(item.Info, item.Options, ct);
        StatusText = L.T("Job gespeichert. Mit „Queue starten“ beginnt das Encoding.");
    });

    [RelayCommand] private void StartQueue()
    {
        queue.StartAll();
        StatusText = L.T("Queue gestartet.");
    }

    [RelayCommand] private void StopQueue()
    {
        queue.StopAll();
        StatusText = L.T("Laufende Jobs werden abgebrochen.");
    }

    [RelayCommand] private void CancelJob()
    {
        if (SelectedJob is { } job) queue.Cancel(job);
    }

    [RelayCommand] private Task RemoveJobAsync() => RunAsync(L.T("Queue-Eintrag entfernen …"), async _ =>
    {
        if (SelectedJob is not { } job) return;
        await queue.RemoveAsync(job);
        Jobs.Remove(job);
        SelectedJob = Jobs.FirstOrDefault();
        UpdateQueueSummary();
    });

    [RelayCommand] private async Task SavePresetAsync()
    {
        if (SelectedMedia?.Options is not { } options) return;
        var name = await prompts.AskAsync(L.T("Preset speichern"), L.T("Name des Presets"), options.PresetName);
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(L.T("Preset speichern …"), async _ =>
        {
            var preset = new Preset { Name = name.Trim(), Description = L.T("Benutzerdefiniertes Preset"), Options = Json.Clone(options), AudioRule = new() { LanguageOrder = [] } };
            preset.Options.PresetName = preset.Name;
            await presets.SaveAsync(preset);
            await ReloadPresetsAsync();
            SelectedPreset = Presets.First(item => item.Name == preset.Name);
            StatusText = L.T("Preset gespeichert.");
        });
    }

    [RelayCommand] private async Task DeletePresetAsync()
    {
        if (SelectedPreset is not { } preset) return;
        if (!await prompts.ConfirmAsync(L.T("Preset"), L.F($"Preset „{preset.Name}“ löschen?"))) return;
        await RunAsync(L.T("Preset löschen …"), async _ =>
        {
            presets.Delete(preset);
            await ReloadPresetsAsync();
        });
    }

    [RelayCommand] private Task ImportPresetAsync() => RunAsync(L.T("Preset importieren …"), async _ =>
    {
        var file = await prompts.PickOpenFileAsync(L.T("Preset importieren"));
        if (file is null) return;
        var preset = await PresetService.ImportAsync(file);
        await presets.SaveAsync(preset);
        await ReloadPresetsAsync();
        SelectedPreset = Presets.First(item => item.Name == preset.Name);
    });

    [RelayCommand] private Task ExportPresetAsync() => RunAsync(L.T("Preset exportieren …"), async _ =>
    {
        if (SelectedPreset is not { } preset) return;
        var file = await prompts.PickSaveFileAsync(L.T("Preset exportieren"), QueueService.SanitizeFileName(preset.Name) + ".json");
        if (file is not null) await PresetService.ExportAsync(preset, file);
    });

    [RelayCommand] private Task CheckFfmpegAsync() => RunAsync(L.T("FFmpeg-Version online prüfen …"), async ct =>
    {
        remote = await distribution.CheckAsync(ct);
        var newer = OperatingSystem.IsWindows()
            ? VersionMetadata.IsNewBuild(versions.Active?.Version, remote.Version)
            : LinuxFfmpegSources.IsNewer(versions.Active?.Version, remote.Version);
        StatusText = newer ? L.F($"Ein anderer Build ist verfügbar: {remote.Version}") : L.T("Die verwaltete Build-ID ist aktuell.");
    });

    [RelayCommand] private Task InstallFfmpegAsync() => RunAsync(L.T("FFmpeg herunterladen und installieren …"), async ct =>
    {
        remote = await distribution.CheckAsync(ct);
        await distribution.InstallAsync(remote, new Progress<DownloadProgress>(progress =>
        {
            DownloadPercent = progress.Percent;
            StatusText = progress.ToString() ?? "";
        }), ct);
        await RefreshAfterInstallAsync(ct);
        DownloadPercent = null;
        StatusText = L.T("FFmpeg installiert und geprüft.");
    });

    [RelayCommand] private Task UseSystemFfmpegAsync() => RunAsync(L.T("Lokale FFmpeg-Installation prüfen …"), async ct =>
    {
        var pair = FfmpegLocator.FindPair();
        string ffmpeg;
        string ffprobe;
        if (pair is { } found)
        {
            ffmpeg = found.Ffmpeg;
            ffprobe = found.Ffprobe;
        }
        else
        {
            ffmpeg = await prompts.PickOpenFileAsync(L.T("ffmpeg auswählen")) ?? "";
            if (ffmpeg.Length == 0) return;
            ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe");
            if (!File.Exists(ffprobe))
                ffprobe = await prompts.PickOpenFileAsync(L.T("ffprobe auswählen")) ?? "";
            if (!File.Exists(ffprobe)) return;
        }
        await versions.SelectLocalAsync(ffmpeg, ffprobe, ct);
        await RefreshAfterInstallAsync(ct);
        StatusText = L.T("Lokale Installation geprüft. Dateien bleiben unverändert.");
    });

    private async Task ReloadPresetsAsync()
    {
        var list = await presets.LoadAsync();
        ui.Invoke(() =>
        {
            Presets.Clear();
            foreach (var preset in list) Presets.Add(preset);
        });
    }

    private async Task RefreshAfterInstallAsync(CancellationToken ct)
    {
        InstalledVersion = versions.Active?.Version ?? L.T("Nicht installiert");
        if (versions.Active is not null) Capabilities = await capabilityService.AnalyzeAsync(versions.Active, ct);
        RefreshEncoderChoices(false);
        RefreshSummary();
    }

    private void UpdateQueueSummary() => QueueSummary = QueueSummaryText.Format(Jobs);

    private async Task RunAsync(string description, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = description;
        try { await action(CancellationToken.None); }
        catch (Exception e)
        {
            StatusText = e.Message;
            log.Write(description, e);
        }
        finally { IsBusy = false; }
    }
}
