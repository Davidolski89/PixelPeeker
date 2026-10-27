using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public sealed partial class EditorViewModel
{
    public static TrackAction[] AudioActions { get; } = AudioTrackEditing.Actions.ToArray();
    public static TrackAction[] SubtitleActions { get; } = Enum.GetValues<TrackAction>();
    public static RateControl[] RateControls { get; } = Enum.GetValues<RateControl>();
    public static ScaleMode[] ScaleModes { get; } = Enum.GetValues<ScaleMode>();
    public static ThreadControlMode[] ThreadModes { get; } = Enum.GetValues<ThreadControlMode>();
    public static int[] ParallelJobChoices { get; } = Enumerable.Range(1, 16).ToArray();
    public static int[] ParallelGpuJobChoices { get; } = Enumerable.Range(1, 8).ToArray();
    public static string[] AudioEncoders { get; } = AudioTrackEditing.Encoders;
    public static string[] SubtitleEncoderChoices { get; } = ["srt", "ass", "webvtt", "mov_text"];
    public static string[] AudioModes { get; } = ["vbr", "cbr", "constrained"];
    public static string[] AudioApplications { get; } = ["audio", "voip", "lowdelay"];
    public static string[] OpusApplications { get; } = ["audio", "voip", "lowdelay"];

    private CancellationTokenSource? operation;
    public Func<Task>? ManageFfmpegRequested { get; set; }
    public Func<string, string, Task<string?>>? EditTextRequested { get; set; }
    public Func<string, string, Task>? TextWindowRequested { get; set; }
    public Action<string>? RevealPath { get; set; }

    public string EstimateText { get; private set; } = "";
    public string LogText { get; private set; } = "";
    [ObservableProperty] private bool previewLoading;

    public int ParallelJobLimit
    {
        get => Math.Clamp(Settings.ParallelJobs, 1, 16);
        set
        {
            var next = Math.Clamp(value, 1, 16);
            if (next == Settings.ParallelJobs) return;
            Settings.ParallelJobs = next;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public int ParallelGpuJobLimit
    {
        get => Math.Clamp(Settings.ParallelGpuJobs, 1, 8);
        set
        {
            var next = Math.Clamp(value, 1, 8);
            if (next == Settings.ParallelGpuJobs) return;
            Settings.ParallelGpuJobs = next;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool EncodeEfficiencyMode
    {
        get => Settings.EfficiencyMode;
        set
        {
            if (value == Settings.EfficiencyMode) return;
            Settings.EfficiencyMode = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool CanEditFileNamePattern => !Settings.FileNaming.Enabled;
    public bool CanSetTune => TuneChoices.Count > 0;
    public double QueueProgress => Jobs.Count == 0 ? 0 : Jobs.Sum(job => job.Status == JobStatus.Abgeschlossen ? 100 : job.Progress ?? 0) / Jobs.Count;
    public double PreviewMaximum => Math.Max(0.1, SelectedMedia?.Info?.Duration ?? 100);
    public IReadOnlyList<string> SubtitleEncoders =>
        Capabilities?.Encoders.Where(encoder => encoder.MediaType == "subtitle").Select(encoder => encoder.Name).Distinct().ToArray() is { Length: > 0 } found
            ? found
            : ["srt", "ass", "webvtt"];

    public QualityRange VideoQualityRange => SelectedMedia?.Options?.Video is { } video
        ? VideoQualityScale.For(video, Capabilities?.Find(video.Encoder))
        : new QualityRange(0, 51, 0.5);
    public double VideoQualityMinimum => VideoQualityRange.Minimum;
    public double VideoQualityMaximum => VideoQualityRange.Maximum;
    public double VideoQualityStep => VideoQualityRange.Step;
    public string VideoQualityCaption => SelectedMedia?.Options?.Video is { } video ? VideoPresentation.QualityLabel(video) : L.T("Qualitätswert");
    public string VideoQualityHint => SelectedMedia?.Options?.Video is { } video ? VideoPresentation.QualityHint(video) : "";
    public string VideoModeDescription => SelectedMedia?.Options?.Video is { } video ? VideoPresentation.ModeDescription(video) : "";
    public string VideoPresetDefault => VideoPresentation.PresetDefault(SelectedMedia?.Options?.Video.Encoder is { } encoder ? Capabilities?.Find(encoder) : null);
    public IReadOnlyList<string> VideoPresetValues => SelectedMedia?.Options?.Video is { } video
        ? VideoPresentation.PresetValues(video.Encoder, PresetChoices, video.Preset, VideoPresetDefault)
        : [];
    public string VideoPresetText => SelectedMedia?.Options?.Video is { } video ? VideoPresentation.PresetLabel(video.Preset, video.Encoder, VideoPresetDefault) : "";
    public string VideoPresetMinimum => VideoPresetValues.FirstOrDefault() ?? "";
    public string VideoPresetMaximum => VideoPresetValues.LastOrDefault() ?? "";
    public string VideoPresetDescription => VideoPresentation.PresetDescription(SelectedMedia?.Options?.Video.Encoder);
    public double VideoPresetMaximumIndex => Math.Max(0, VideoPresetValues.Count - 1);
    public int VideoPresetIndex
    {
        get
        {
            var values = VideoPresetValues;
            var index = values.ToList().IndexOf(SelectedMedia?.Options?.Video.Preset ?? "");
            return index < 0 ? 0 : index;
        }
        set
        {
            var values = VideoPresetValues;
            if (values.Count == 0 || SelectedMedia?.Options is not { } options) return;
            var index = Math.Clamp(value, 0, values.Count - 1);
            if (options.Video.Preset == values[index]) return;
            options.Video.Preset = values[index];
            OnPropertyChanged(nameof(VideoPreset));
            OnPropertyChanged(nameof(VideoPresetText));
        }
    }

    public string CutPositionStatus => PendingCutStart is { } start
        ? L.F($"Schnittstart: {start.Seconds:0.000000} s · erstes erhaltenes Bild suchen und Ende setzen.")
        : L.T("Start setzen, dann das erste Bild, das erhalten bleiben soll.");

    public void AppendLog(string line)
    {
        LogText += line + "\n";
        if (LogText.Length > 100_000) LogText = LogText[^80_000..];
        OnPropertyChanged(nameof(LogText));
    }

    public void NotifyPresentation()
    {
        foreach (var name in new[]
        {
            nameof(VideoQualityRange), nameof(VideoQualityMinimum), nameof(VideoQualityMaximum), nameof(VideoQualityStep),
            nameof(VideoQualityCaption), nameof(VideoQualityHint), nameof(VideoModeDescription), nameof(VideoPresetDefault),
            nameof(VideoPresetValues), nameof(VideoPresetText), nameof(VideoPresetMinimum), nameof(VideoPresetMaximum),
            nameof(VideoPresetDescription), nameof(VideoPresetMaximumIndex), nameof(VideoPresetIndex), nameof(PreviewMaximum),
            nameof(CanEditFileNamePattern), nameof(CanSetTune), nameof(QueueProgress), nameof(SubtitleEncoders),
            nameof(CutPositionStatus), nameof(EstimateText), nameof(ParallelJobLimit), nameof(ParallelGpuJobLimit),
            nameof(EncodeEfficiencyMode)
        })
            OnPropertyChanged(name);
    }

    public Task AddManyAsync(IEnumerable<EditorMedia> items) => RunAsync(L.T("Jobs werden angelegt …"), async ct =>
    {
        foreach (var item in items)
        {
            if (item.Info is null || item.Options is null) continue;
            await queue.AddAsync(item.Info, item.Options, ct);
        }
        StatusText = L.T("Jobs gespeichert.");
    });

    public void ApplyToMany(IEnumerable<EditorMedia> items)
    {
        if (SelectedMedia?.Info is null || SelectedMedia.Options is not { } options) return;
        var preset = new Preset { Name = options.PresetName, Options = Json.Clone(options) };
        foreach (var item in items)
        {
            if (ReferenceEquals(item, SelectedMedia) || item.Info is null || item.Options is null) continue;
            item.Options = PresetService.Apply(preset, item.Info, item.Options);
        }
        StatusText = L.T("Einstellungen auf die markierten Dateien übertragen.");
    }

    public void StartSelected(IEnumerable<EncodingJob> selected)
    {
        var count = queue.StartMany(selected);
        StatusText = L.F($"{count} ausgewählte Jobs zum Start vorgemerkt. Gleichzeitig laufen höchstens {Settings.ParallelJobs} Jobs.");
    }

    [RelayCommand]
    private Task ImportFolderAsync() => RunAsync(L.T("Ordner werden analysiert …"), async _ =>
    {
        var folder = await prompts.PickFolderAsync(L.T("Ordner importieren"));
        if (folder is not null) await ImportCoreAsync([folder], CancellationToken.None);
    });

    [RelayCommand]
    private Task ManageFfmpegAsync() => ManageFfmpegRequested?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private void CancelOperation() => operation?.Cancel();

    [RelayCommand]
    private Task SeekBackwardAsync() => SeekByAsync(-10);

    [RelayCommand]
    private Task SeekForwardAsync() => SeekByAsync(10);

    private async Task SeekByAsync(double delta)
    {
        var next = Math.Max(0, PreviewSeconds + delta);
        var duration = SelectedMedia?.Info?.Duration;
        if (duration is > 0) next = Math.Min(next, duration.Value);
        PreviewSeconds = next;
        if (PlaybackActive) await SeekPlaybackAsync(next);
    }

    [RelayCommand]
    private void CutToEnd()
    {
        if (SelectedMedia?.Options is null || PendingCutStart is not { } start) return;
        SelectedCut = CutEditing.Add(SelectedMedia.Options, start, null);
        PendingCutStart = null;
        RefreshCuts();
        RefreshSummary();
    }

    [RelayCommand]
    private void JumpCutStart()
    {
        if (SelectedCut is null) return;
        PreviewSeconds = SelectedCut.Start.Seconds;
    }

    [RelayCommand]
    private void JumpCutEnd()
    {
        if (SelectedCut?.End is not { } end) return;
        PreviewSeconds = end.Seconds;
    }

    [RelayCommand]
    private async Task SelectOutputFolderAsync()
    {
        if (SelectedMedia?.Options is not { } options) return;
        var folder = await prompts.PickFolderAsync(L.T("Ausgabeordner"));
        if (folder is null) return;
        options.OutputDirectory = folder;
        RefreshSummary();
    }

    [RelayCommand]
    private void OpenOutput()
    {
        if (SelectedJob is not { } job) return;
        var directory = Path.GetDirectoryName(job.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory)) RevealPath?.Invoke(directory);
    }

    [RelayCommand]
    private void OpenLogs() => RevealPath?.Invoke(paths.Logs);

    [RelayCommand]
    private Task ShowJobLogAsync()
    {
        if (SelectedJob is not { } job || !File.Exists(job.LogPath))
        {
            StatusText = L.T("Noch kein Jobprotokoll vorhanden.");
            return Task.CompletedTask;
        }
        return TextWindowRequested?.Invoke(L.T("FFmpeg-Protokoll"), File.ReadAllText(job.LogPath)) ?? Task.CompletedTask;
    }

    [RelayCommand]
    private Task ShowAnalysisAsync()
    {
        if (SelectedJob?.Analysis is not { } analysis || AnalysisRequested is null)
        {
            StatusText = L.T("Noch keine Qualitätsanalyse vorhanden.");
            return Task.CompletedTask;
        }
        return AnalysisRequested(analysis);
    }

    [RelayCommand]
    private Task CompareResultsAsync() => ShowResultsAsync();

    [RelayCommand]
    private Task DeferJobAsync() => RunAsync(L.T("Job zurückstellen …"), async _ =>
    {
        if (SelectedJob is { } job) await queue.DeferAsync(job);
    });

    [RelayCommand]
    private Task EditJobAsync() => RunAsync(L.T("Job bearbeiten …"), async _ =>
    {
        var job = SelectedJob ?? throw new InvalidOperationException(L.T("Job auswählen."));
        if (job.Status is not (JobStatus.Wartend or JobStatus.Zurückgestellt))
            throw new InvalidOperationException(L.T("Nur wartende oder zurückgestellte Jobs bearbeiten."));
        if (EditTextRequested is null) return;
        var edited = await EditTextRequested(L.T("Job-Einstellungen"), System.Text.Json.JsonSerializer.Serialize(job.Options, Json.Options));
        if (string.IsNullOrWhiteSpace(edited)) return;
        var options = System.Text.Json.JsonSerializer.Deserialize<EncodingOptions>(edited, Json.Options)
            ?? throw new InvalidDataException(L.T("Die Job-Einstellungen konnten nicht gelesen werden."));
        await queue.ReplaceOptionsAsync(job, options);
        OnPropertyChanged(nameof(SelectedJobDetails));
    });

    [RelayCommand]
    private Task EditPresetAsync() => RunAsync(L.T("Preset bearbeiten …"), async _ =>
    {
        if (SelectedPreset is null || EditTextRequested is null) return;
        var edited = await EditTextRequested(L.T("Preset und Audio-Standardregeln"), System.Text.Json.JsonSerializer.Serialize(SelectedPreset, Json.Options));
        if (string.IsNullOrWhiteSpace(edited)) return;
        var preset = System.Text.Json.JsonSerializer.Deserialize<Preset>(edited, Json.Options);
        if (preset is null || string.IsNullOrWhiteSpace(preset.Name) || preset.SchemaVersion != 1)
            throw new ArgumentException(L.T("Name und SchemaVersion 1 erforderlich."));
        await presets.SaveAsync(preset);
        await ReloadPresetsAsync();
        SelectedPreset = Presets.FirstOrDefault(item => item.Name == preset.Name);
    });

    [RelayCommand]
    private async Task DuplicatePresetAsync()
    {
        if (SelectedPreset is null) return;
        var copy = Json.Clone(SelectedPreset);
        var name = await prompts.AskAsync(L.T("Preset duplizieren"), L.T("Neuer Name"), copy.Name + L.T(" – Kopie"));
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(L.T("Preset duplizieren …"), async _ =>
        {
            copy.Name = name.Trim();
            await presets.SaveAsync(copy);
            await ReloadPresetsAsync();
            SelectedPreset = Presets.FirstOrDefault(item => item.Name == copy.Name);
        });
    }

    public static void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);
        }
        catch (Exception)
        {
            // The caller surfaces a status line when the folder cannot be opened.
        }
    }
}
