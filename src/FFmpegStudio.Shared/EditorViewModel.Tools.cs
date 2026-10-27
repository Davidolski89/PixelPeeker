using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public sealed partial class EditorViewModel
{
    private CancellationTokenSource? analysisCancellation;

    public Func<AppSettings, Task<AppSettings?>>? EditSettingsRequested { get; set; }
    public Func<EncoderInfo, IReadOnlyDictionary<string, string>, Task<IReadOnlyDictionary<string, string>?>>? EditExpertRequested { get; set; }
    public Func<EncodingJob, Task<bool>>? TestEncodeNoticeRequested { get; set; }
    public Func<EncodeComparisonSession, Task>? ComparisonRequested { get; set; }
    public Func<QualityResult, Task>? AnalysisRequested { get; set; }
    public event Action<AppTheme>? ThemeChanged;

    public void RefreshLanguage()
    {
        DistributionNote = distribution.SourceDescription;
        foreach (var name in new[]
        {
            nameof(PhaseNote), nameof(OpenFileLabel), nameof(LibraryHeading), nameof(MediaHeading), nameof(QueueHeading),
            nameof(CommandHeading), nameof(JobHeading), nameof(EncodeHeading), nameof(FilterHeading), nameof(CutHeading),
            nameof(SettingsLabel), nameof(CompareLabel), nameof(AnalyzeLabel), nameof(CancelAnalysisLabel), nameof(ExpertLabel), nameof(TestEncodeLabel),
            nameof(ExportSubtitleLabel), nameof(SystemFfmpegLabel), nameof(PreviousFrameLabel), nameof(NextFrameLabel),
            nameof(SubtitleHeading), nameof(CutSummary), nameof(SelectedJobDetails), nameof(SubtitleTracks),
            nameof(AudioHeading), nameof(HardwareHeading), nameof(ResultsLabel), nameof(FileNameHeading), nameof(RemoveLabel), nameof(PlayLabel),
            nameof(PauseLabel), nameof(StopLabel), nameof(PreviewLabel), nameof(CutStartLabel), nameof(CutEndLabel),
            nameof(ClearCutsLabel), nameof(EncoderLabel), nameof(ContainerLabel), nameof(QualityLabel), nameof(PresetLabel),
            nameof(WidthHeightLabel), nameof(DetectCropLabel), nameof(CropLabel), nameof(PresetsHeading), nameof(SavePresetLabel),
            nameof(DeletePresetLabel), nameof(ImportPresetLabel), nameof(ExportPresetLabel), nameof(AddQueueLabel),
            nameof(StartQueueLabel), nameof(StopQueueLabel), nameof(CancelJobLabel), nameof(InstallFfmpegLabel),
            nameof(CheckFfmpegLabel), nameof(HardwareText), nameof(OutputFileNamePreview), nameof(AudioTracks)
        })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private async Task EditSettingsAsync()
    {
        if (EditSettingsRequested is null) return;
        var edited = await EditSettingsRequested(Json.Clone(Settings));
        if (edited is null) return;
        await RunAsync(L.T("Einstellungen speichern …"), async _ =>
        {
            foreach (var rule in edited.WatchFolders.Where(rule => rule.Enabled)) FolderWatchService.ValidateRule(rule);
            SettingsTransfer.Apply(Settings, edited);
            L.SetLanguage(Settings.Language);
            RefreshLanguage();
            await settings.SaveAsync();
            ThemeChanged?.Invoke(ThemeCatalog.Resolve(Settings.ThemeId, Settings.DarkMode));
            StatusText = L.T("Einstellungen gespeichert.");
            OnPropertyChanged(nameof(Settings));
            RefreshSummary();
        });
    }

    [RelayCommand]
    private async Task ExpertOptionsAsync()
    {
        if (SelectedMedia?.Options?.Video is not { } video || Capabilities?.Find(video.Encoder) is not { } encoder)
        {
            StatusText = L.T("Datei auswählen.");
            return;
        }
        if (EditExpertRequested is null) return;
        var values = await EditExpertRequested(encoder, video.Extra);
        if (values is null) return;
        video.Extra = new Dictionary<string, string>(values);
        RefreshSummary();
        StatusText = L.T("Expertenoptionen übernommen.");
    }

    [RelayCommand]
    private Task TestEncodeAsync() => RunAsync(L.T("Testencode vorbereiten …"), async ct =>
    {
        if (SelectedMedia?.Info is not { } info || SelectedMedia.Options is null) throw new InvalidOperationException(L.T("Datei auswählen."));
        var options = TestEncodePlan.Create(SelectedMedia.Options, PreviewSeconds, TestSeconds, info.Duration, paths.Cache);
        var job = await queue.AddAsync(info, options, ct);
        queue.Start(job);
        StatusText = TestEncodeNotice.StatusMessage(job);
        if (Settings.ShowTestEncodeNotification && TestEncodeNoticeRequested is not null)
        {
            var suppress = await TestEncodeNoticeRequested(job);
            if (suppress)
            {
                Settings.ShowTestEncodeNotification = false;
                await settings.SaveAsync();
            }
            StatusText = TestEncodeNotice.StatusMessage(job);
        }
    });

    [RelayCommand]
    private Task ExportSubtitleAsync(SubtitleTrackOptions? track) => RunAsync(L.T("Untertitel exportieren …"), async ct =>
    {
        track ??= SelectedSubtitle;
        if (track is null || SelectedMedia?.Info is not { } info || versions.Active is not { } tools) return;
        var path = await prompts.PickSaveFileAsync(L.T("Untertitel exportieren"), SubtitleExport.SuggestedName(info, track));
        if (path is null) return;
        await subtitles.ExportAsync(tools, info, track, path, ct);
        StatusText = L.T("Untertitel exportiert.");
    });

    [RelayCommand]
    private async Task CompareEncodesAsync() => await OpenComparisonAsync(null);

    public async Task OpenComparisonAsync(IReadOnlyList<EncodingJob>? selected)
    {
        if (ComparisonRequested is null) return;
        var session = new EncodeComparisonSession(comparison, log);
        session.RefreshJobs(Jobs);
        if (selected is { Count: > 0 }) session.SelectJobs(selected);
        await ComparisonRequested(session);
    }

    public async Task<EncodeComparisonInput?> ProbeComparisonFileAsync()
    {
        var file = await prompts.PickOpenFileAsync(L.T("Encode-Vergleich"));
        if (file is null) return null;
        if (versions.Active is not { } tools) throw new InvalidOperationException(L.T("FFmpeg fehlt."));
        var info = await probe.ProbeAsync(tools, file);
        if (info.Video is null) throw new InvalidDataException(L.T("Keine Videospur vorhanden."));
        return new EncodeComparisonInput(info, tools);
    }

    [RelayCommand]
    private Task AnalyzeQualityAsync() => RunAsync(L.T("Qualitätsanalyse wird gestartet …"), async ct =>
    {
        if (SelectedJob is not { } job) throw new InvalidOperationException(L.T("Einen Job auswählen."));
        if (job.Status != JobStatus.Abgeschlossen || job.OutputInfo?.Video is null)
            throw new InvalidOperationException(L.T("Zuerst einen Test- oder vollständigen Encode abschließen."));
        analysisCancellation?.Cancel();
        analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outcome = await QualityAnalysisRun.ExecuteAsync(job, quality, _ =>
        {
            StatusText = job.AnalysisStatus;
            OnPropertyChanged(nameof(SelectedJobDetails));
        }, e => log.Write(L.F($"Qualitätsanalyse fehlgeschlagen: {job.Name}"), e), analysisCancellation.Token, marshal: ui.Invoke);
        OnPropertyChanged(nameof(SelectedJobDetails));
        StatusText = outcome switch
        {
            QualityAnalysisOutcome.Completed => L.F($"Qualitätsanalyse abgeschlossen · {job.Name}"),
            QualityAnalysisOutcome.Failed => L.F($"Qualitätsanalyse fehlgeschlagen · {job.Name}"),
            _ => job.AnalysisStatus
        };
        if (outcome == QualityAnalysisOutcome.Completed && job.Analysis is not null && AnalysisRequested is not null)
            await AnalysisRequested(job.Analysis);
    });

    [RelayCommand]
    private void CancelAnalysis() => analysisCancellation?.Cancel();
}
