using System.Globalization;
using System.Text;
using System.Text.Json;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public static class TestEncodePlan
{
    public static EncodingOptions Create(EncodingOptions source, double previewSeconds, double testSeconds, double? mediaDuration, string cacheDirectory)
    {
        if (testSeconds <= 0 || testSeconds > 120) throw new ArgumentException(L.T("Testlänge zwischen 0 und 120 Sekunden wählen."));
        var options = Json.Clone(source);
        options.ClipStart = previewSeconds;
        options.ClipDuration = Math.Min(testSeconds, (mediaDuration ?? double.MaxValue) - previewSeconds);
        options.OutputDirectory = Path.Combine(cacheDirectory, "test-encodes");
        options.FileNamePattern = "{name}_Test_{codec}";
        return options;
    }
}

public static class TestEncodeNotice
{
    public static string StatusMessage(EncodingJob job) => job.Status switch
    {
        JobStatus.Wartend => L.T("Der Testencode wartet in der Queue und startet automatisch, sobald ein Platz frei ist."),
        JobStatus.Läuft => L.T("Der Testencode wird gerade encodiert."),
        _ => L.T("Testencode") + " · " + L.EnumText(job.Status)
    };
}

public static class SettingsTransfer
{
    public static void Apply(AppSettings target, AppSettings edited)
    {
        var theme = ThemeCatalog.Resolve(edited.ThemeId, edited.DarkMode);
        target.ThemeId = theme.Id;
        target.DarkMode = theme.IsDark;
        target.RecursiveImport = edited.RecursiveImport;
        target.TooltipsEnabled = edited.TooltipsEnabled;
        target.TooltipDelaySeconds = TooltipTiming.Normalize(edited.TooltipDelaySeconds);
        target.ShowTestEncodeNotification = edited.ShowTestEncodeNotification;
        target.FileNaming = Json.Clone(edited.FileNaming);
        target.AutoInstall = edited.AutoInstall;
        target.AutoUpdate = edited.AutoUpdate;
        target.CheckUpdates = edited.CheckUpdates;
        target.UpdateIntervalHours = Math.Clamp(edited.UpdateIntervalHours, 1, 720);
        target.ParallelJobs = Math.Clamp(edited.ParallelJobs, 1, 16);
        target.ParallelGpuJobs = Math.Clamp(edited.ParallelGpuJobs, 1, 8);
        target.CacheLimitMiB = Math.Clamp(edited.CacheLimitMiB, 32, 16384);
        target.EfficiencyMode = edited.EfficiencyMode;
        target.DefaultOutputDirectory = edited.DefaultOutputDirectory;
        target.WatchFolders = edited.WatchFolders;
        target.CompletionAction = edited.CompletionAction;
        target.Language = L.Normalize(edited.Language);
    }
}

public static class EncoderOptionEditor
{
    public static readonly string[] Managed = ["preset", "tune", "profile", "level", "gpu", "cq", "qp", "crf", "rc", "cpu-used", "speed"];

    public static IReadOnlyList<EncoderOption> Editable(EncoderInfo encoder) =>
        (encoder.Options ?? []).Where(option => !Managed.Contains(option.Name)).ToList();
}

public static class SubtitleExport
{
    public static string SuggestedName(MediaInfo media, SubtitleTrackOptions track) =>
        Path.GetFileNameWithoutExtension(media.Path) + "_" + track.Language + (track.IsBitmap ? ".mks" : ".srt");
}

public enum QualityAnalysisOutcome { Completed, Canceled, Failed }

public static class QualityAnalysisRun
{
    public static async Task<QualityAnalysisOutcome> ExecuteAsync(EncodingJob job, QualityAnalysisService quality,
        Action<QualityAnalysisProgress>? progress, Action<Exception>? log, CancellationToken ct,
        Func<bool>? stillCurrent = null, Action? started = null, Action<Action>? marshal = null)
    {
        void Apply(Action action)
        {
            if (marshal is null) action();
            else marshal(action);
        }
        Apply(() =>
        {
            job.AnalysisProgress = null;
            job.AnalysisStatus = L.T("Qualitätsanalyse wird gestartet …");
            job.AnalysisDetails = L.T("Quelle und Ausgabedatei werden verglichen. Die Analyse besteht aus mehreren Durchläufen.");
            job.IsAnalyzing = true;
        });
        started?.Invoke();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await quality.AnalyzeAsync(job, new Progress<QualityAnalysisProgress>(update =>
            {
                if (stillCurrent is not null && !stillCurrent()) return;
                Apply(() =>
                {
                    job.AnalysisStatus = update.Stage;
                    job.AnalysisProgress = update.Percent;
                    job.AnalysisDetails = Describe(update);
                    progress?.Invoke(update);
                });
            }), ct);
            Apply(() =>
            {
                job.AnalysisProgress = 100;
                job.AnalysisStatus = L.T("Qualitätsanalyse abgeschlossen");
                job.AnalysisDetails = Summary(job.Analysis!, clock.Elapsed);
            });
            return QualityAnalysisOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            Apply(() =>
            {
                job.AnalysisProgress = null;
                job.AnalysisStatus = L.T("Qualitätsanalyse abgebrochen");
                job.AnalysisDetails = L.F($"Beendet nach {FormatTime(clock.Elapsed)}. Vorhandene Messwerte bleiben verfügbar.");
            });
            return QualityAnalysisOutcome.Canceled;
        }
        catch (Exception e)
        {
            Apply(() =>
            {
                job.AnalysisProgress = null;
                job.AnalysisStatus = L.T("Qualitätsanalyse fehlgeschlagen");
                job.AnalysisDetails = e.Message.Length > 4000 ? e.Message[..4000] : e.Message;
            });
            log?.Invoke(e);
            return QualityAnalysisOutcome.Failed;
        }
        finally
        {
            Apply(() => job.IsAnalyzing = false);
        }
    }

    public static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    public static string Describe(QualityAnalysisProgress progress)
    {
        var parts = new List<string>();
        if (progress.Percent.HasValue) parts.Add(L.F($"{progress.Percent:0.0} % im aktuellen Durchlauf"));
        if (progress.Seconds.HasValue) parts.Add(progress.Duration.HasValue ? $"{progress.Seconds:0.0} / {progress.Duration:0.0} s" : L.F($"{progress.Seconds:0.0} s verarbeitet"));
        if (progress.Speed.HasValue) parts.Add($"{progress.Speed:0.00}×");
        parts.Add(L.F($"Laufzeit {FormatTime(progress.Elapsed)}"));
        if (progress.Remaining.HasValue) parts.Add(L.F($"Rest im Durchlauf ca. {FormatTime(progress.Remaining.Value)}"));
        return string.Join(" · ", parts);
    }

    public static string Summary(QualityResult result, TimeSpan elapsed)
    {
        static string N(double? value) => value?.ToString("0.###") ?? L.T("nicht verfügbar");
        return L.F($"VMAF {N(result.Vmaf)} · SSIM {N(result.Ssim)} · PSNR {N(result.Psnr)} dB\nDauer {FormatTime(elapsed)} · Ergebnisse gespeichert.");
    }
}

public static class QualityReport
{
    public static string Csv(QualityResult result)
    {
        var csv = new StringBuilder("seconds;vmaf;ssim;psnr\n");
        static string N(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        foreach (var frame in result.Frames)
            csv.AppendLine($"{N(frame.Seconds)};{N(frame.Vmaf)};{N(frame.Ssim)};{N(frame.Psnr)}");
        return csv.ToString();
    }

    public static string JsonText(QualityResult result) => JsonSerializer.Serialize(result, Json.Options);
}
