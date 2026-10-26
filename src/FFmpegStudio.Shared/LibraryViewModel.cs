using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

/// <summary>
/// Library slice kept for the first shared surface. The Avalonia shell now binds to EditorViewModel.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly SettingsService settings;
    private readonly QueueStore store;
    private readonly FFmpegVersionService versions;
    private readonly MediaProbeService probe;
    private readonly FFmpegCommandBuilder builder;
    private readonly IFmpegDistribution distribution;
    private readonly LoggingService log;
    private readonly IUiMarshal ui;

    public LibraryViewModel(SettingsService settings, QueueStore store, FFmpegVersionService versions, MediaProbeService probe,
        FFmpegCommandBuilder builder, IFmpegDistribution distribution, LoggingService log, IUiMarshal ui)
    {
        this.settings = settings;
        this.store = store;
        this.versions = versions;
        this.probe = probe;
        this.builder = builder;
        this.distribution = distribution;
        this.log = log;
        this.ui = ui;
    }

    public ObservableCollection<LibraryMedia> Media { get; } = [];
    public ObservableCollection<EncodingJob> Jobs { get; } = [];
    public AppSettings Settings => settings.Current;
    public string PhaseNote => L.T("Avalonia-Vorschau. Wiedergabe, der vollständige Editor und die Beschaffung von FFmpeg für Linux folgen in späteren Phasen.");
    public string OpenFileLabel => L.T("Datei öffnen");
    public string LibraryHeading => L.T("Medienbibliothek");
    public string MediaHeading => L.T("Medieninfo");
    public string QueueHeading => L.T("Queue");
    public string CommandHeading => L.T("Befehl");
    public string JobHeading => L.T("Ausgewählter Job");

    [ObservableProperty] private LibraryMedia? selectedMedia;
    [ObservableProperty] private EncodingJob? selectedJob;
    [ObservableProperty] private string mediaDetails = L.T("Noch keine Datei ausgewählt.");
    [ObservableProperty] private string commandPreview = "";
    [ObservableProperty] private string jobDetails = L.T("Einen Job auswählen, um Ergebnis, Protokoll und Messwerte zu sehen.");
    [ObservableProperty] private string statusText = L.T("PixelPeeker wird vorbereitet …");
    [ObservableProperty] private string queueSummary = L.T("Keine Jobs");
    [ObservableProperty] private string installedVersion = L.T("Nicht installiert");
    [ObservableProperty] private string distributionNote = "";

    public async Task InitializeAsync()
    {
        await settings.LoadAsync();
        L.SetLanguage(settings.Current.Language);
        NotifyLabels();
        DistributionNote = distribution.SourceDescription;
        string? startupError = null;
        try
        {
            await versions.LoadAsync();
            InstalledVersion = versions.Active?.Version ?? L.T("Nicht installiert");
        }
        catch (Exception e)
        {
            log.Write(L.T("FFmpeg-Startprüfung fehlgeschlagen."), e);
            InstalledVersion = L.T("Nicht installiert");
            startupError = e.Message;
        }
        await store.InitializeAsync();
        var jobs = await store.LoadJobsAsync();
        ui.Invoke(() =>
        {
            Jobs.Clear();
            foreach (var job in jobs) Jobs.Add(job);
            QueueSummary = QueueSummaryText.Format(Jobs);
            if (SelectedJob is null) SelectedJob = Jobs.FirstOrDefault();
        });
        StatusText = startupError ?? (versions.Active is not null
            ? L.T("Bereit · Dateien oder Ordner in die Bibliothek ziehen.")
            : distribution.SupportsManagedInstall
                ? L.T("FFmpeg fehlt. Unter „FFmpeg verwalten“ installieren oder lokale Programme auswählen.")
                : distribution.SourceDescription);
    }

    public async Task ImportPathsAsync(IEnumerable<string> input)
    {
        StatusText = L.T("Dateien werden analysiert …");
        var recursive = settings.Current.RecursiveImport;
        var files = await Task.Run(() => input.SelectMany(path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            : [path]).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var added = 0;
        var failed = 0;
        foreach (var file in files)
        {
            if (Media.Any(item => item.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            if (!File.Exists(file)) { failed++; log.Write(L.F($"Import fehlgeschlagen: {file}")); continue; }
            try
            {
                LibraryMedia entry;
                if (versions.Active is not { } tools)
                    entry = LibraryMedia.Unprobed(file);
                else
                {
                    var info = await probe.ProbeAsync(tools, file);
                    if (info.Video is null) throw new InvalidDataException(L.T("Keine Videospur vorhanden."));
                    entry = LibraryMedia.FromProbe(info, EncodingOptions.ForMedia(info));
                }
                ui.Invoke(() => { Media.Add(entry); SelectedMedia = entry; });
                added++;
            }
            catch (Exception e)
            {
                failed++;
                log.Write(L.F($"Import fehlgeschlagen: {file}"), e);
            }
        }
        StatusText = L.F($"{added} Dateien importiert · {failed} nicht lesbar oder ohne Video.");
    }

    partial void OnSelectedMediaChanged(LibraryMedia? value)
    {
        if (value is null)
        {
            MediaDetails = L.T("Noch keine Datei ausgewählt.");
            CommandPreview = "";
            return;
        }
        MediaDetails = value.Details;
        CommandPreview = value.Info is null || value.Options is null
            ? L.T("FFmpeg/ffprobe fehlt, daher wurden keine Spuren, Codecs oder Laufzeiten gelesen.")
            : BuildCommand(value.Info, value.Options);
    }

    partial void OnSelectedJobChanged(EncodingJob? value)
    {
        JobDetails = value is null
            ? L.T("Einen Job auswählen, um Ergebnis, Protokoll und Messwerte zu sehen.")
            : string.Join(Environment.NewLine, new[] { value.Name, value.DisplayStatus, value.OutputPath, value.Error }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string BuildCommand(MediaInfo info, EncodingOptions options)
    {
        try
        {
            var errors = builder.Validate(info, options);
            if (errors.Count > 0) return string.Join(Environment.NewLine, errors);
            var output = QueueService.SuggestedOutputPath(info, options, settings.Current.FileNaming);
            var temporary = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".unvollstaendig-VORSCHAU" + Path.GetExtension(output));
            var executable = versions.Active?.FFmpegPath ?? (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            return FFmpegCommandBuilder.Display(executable, builder.Build(info, options, temporary));
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private void NotifyLabels()
    {
        OnPropertyChanged(nameof(PhaseNote));
        OnPropertyChanged(nameof(OpenFileLabel));
        OnPropertyChanged(nameof(LibraryHeading));
        OnPropertyChanged(nameof(MediaHeading));
        OnPropertyChanged(nameof(QueueHeading));
        OnPropertyChanged(nameof(CommandHeading));
        OnPropertyChanged(nameof(JobHeading));
        MediaDetails = SelectedMedia?.Details ?? L.T("Noch keine Datei ausgewählt.");
        JobDetails = SelectedJob is null
            ? L.T("Einen Job auswählen, um Ergebnis, Protokoll und Messwerte zu sehen.")
            : JobDetails;
        QueueSummary = QueueSummaryText.Format(Jobs);
    }
}
