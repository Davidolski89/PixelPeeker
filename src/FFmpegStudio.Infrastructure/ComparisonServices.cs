using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class FrameComparisonService(ProcessRunner runner, PreviewService preview)
{
    private readonly FrameNavigationService navigation=new(runner);
    public async Task<double> FindTimestampAsync(Toolchain tools, MediaInfo media, double requested, bool nearest, CancellationToken ct)
    {
        // read_intervals seeks to a keyframe; select the actual decoded presentation timestamp.
        var start = Math.Max(0, requested - 1);
        var end = Math.Min(media.Duration ?? requested + 2, requested + 2);
        var r = await runner.RunAsync(tools.FFprobePath, ["-v", "error", "-select_streams", media.Video!.Index.ToString(),
            "-read_intervals", $"{FFmpegCommandBuilder.Number(start)}%{FFmpegCommandBuilder.Number(end)}",
            "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", media.Path], ct);
        r.EnsureSuccess(L.T("Frame-Zeitstempel"));
        using var doc = JsonDocument.Parse(r.StandardOutput);
        var timestamps = doc.RootElement.GetProperty("frames").EnumerateArray()
            .Select(f => MediaProbeParser.Number(f, "best_effort_timestamp_time"))
            .Where(t => t.HasValue).Select(t => Math.Max(0, t!.Value - media.StartTime)).ToArray();
        if (timestamps.Length == 0) throw new InvalidDataException(L.T("Keine Frame-Zeitstempel im gewählten Bereich gefunden."));
        return nearest ? timestamps.MinBy(t => Math.Abs(t - requested)) : timestamps.Where(t => t + .00001 >= requested).DefaultIfEmpty(timestamps[^1]).First();
    }
    public async Task<FramePair> CompareAsync(EncodingJob job, double encodedSeconds, CancellationToken ct = default)
    {
        var output = job.OutputInfo ?? throw new InvalidOperationException(L.T("Zuerst einen Test- oder vollständigen Encode abschließen."));
        var outputFrame=await navigation.ResolveAsync(job.Tools,output,encodedSeconds,ct:ct);
        double encoded=outputFrame.Seconds;
        // Cut filters explicitly produce output timeline zero. A codec's negative preroll
        // (e.g. Opus) can move the container start without moving the video timestamps.
        double encodedTimeline=encoded+(CutTimeline.Active(job.Options)?output.StartTime:0);
        double sourceStart=CutTimeline.SourceTime(job.Options,encodedTimeline)-encoded;
        var sourceFrame=await navigation.ResolveAsync(job.Tools,job.Source,sourceStart+encoded,ct:ct);
        double original=sourceFrame.Seconds;
        var reference = ReferenceOptions(job.Options);
        var outputDimensions = ScalingService.Calculate(output.Video!, new FilterOptions());
        var sourceFilter = FilterService.Build(job.Source, reference, includeCustom: false);
        var frames = await Task.WhenAll(
            preview.FrameAsync(job.Tools, job.Source, original, sourceFilter, outputDimensions.Width, outputDimensions.Height, ct,sourceFrame),
            preview.FrameAsync(job.Tools, output, encoded, "", outputDimensions.Width, outputDimensions.Height, ct,outputFrame));
        var delta = original - sourceStart - encoded;
        return new(frames[0], frames[1], original, encoded, outputDimensions.Width, outputDimensions.Height,
            L.F($"Reale PTS: Quelle {original:0.000000} s · Ausgabe {encoded:0.000000} s · Abweichung {delta * 1000:0.###} ms. ") +
            L.T("Referenz mit identischer Geometrie; Ausgabe auf Vergleichsraster. ") +
            (job.Source.Video!.IsHdr || output.Video!.IsHdr ? L.T("HDR-Vorschau: Hable → BT.709.") : L.T("SDR-Vorschau.")) +
            (Math.Abs(delta) > .001 ? L.T(" Bei VFR/Framerateänderung wird der zeitlich nächste Quellframe verwendet.") : ""));
    }
    public static EncodingOptions ReferenceOptions(EncodingOptions options)
    {
        var reference = Json.Clone(options);
        reference.Video.CudaDecode = false; reference.Video.CudaScale = false;
        reference.Filters.FrameRate = 0; reference.Filters.CustomChain = "";
        foreach (var s in reference.Subtitles) s.Action = TrackAction.Entfernen;
        return reference;
    }
}

public sealed class QualityAnalysisService(AppPaths paths, ProcessRunner runner, FFmpegCapabilityService capabilities, QueueStore store)
{
    private readonly SemaphoreSlim gate = new(1);
    public Task<QualityResult> AnalyzeAsync(EncodingJob job, IProgress<QualityAnalysisProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(()=>AnalyzeCoreAsync(job,progress,ct),ct);
    private async Task<QualityResult> AnalyzeCoreAsync(EncodingJob job, IProgress<QualityAnalysisProgress>? progress, CancellationToken ct)
    {
        var clock=Stopwatch.StartNew();
        var duration=job.OutputInfo?.Duration;
        if(duration is not >0||!double.IsFinite(duration.Value)) duration=null;
        long lastReport=-250;
        void Report(string stage,double? seconds=null,ProgressInfo? process=null,bool force=true)
        {
            if(!force&&clock.ElapsedMilliseconds-lastReport<250) return;
            lastReport=clock.ElapsedMilliseconds;
            var percent=process?.Percent??(duration.HasValue&&seconds.HasValue?Math.Clamp(seconds.Value/duration.Value*100,0,100):null);
            progress?.Report(new(stage,percent,seconds,duration,process?.Speed is >0?process.Speed:null,clock.Elapsed,process?.Remaining));
        }
        var output = job.OutputInfo ?? throw new InvalidOperationException(L.T("Keine geprüfte Ausgabedatei vorhanden."));
        if (job.Options.Filters.CustomChain.Length > 0) throw new InvalidOperationException(L.T("Die Qualitätsanalyse ist für freie Filterketten gesperrt, da deren Zeit-/Geometrietransformation nicht verlässlich ableitbar ist."));
        if (job.Source.Video!.IsHdr && !job.Options.Filters.ToneMap)
            throw new InvalidOperationException(L.T("HDR-Metriken sind nicht automatisch mit SDR-VMAF-Modellen vergleichbar. Für diese Analyse einen expliziten HDR→SDR-Testencode erstellen."));
        Report(L.T("Warte auf einen freien Analyseplatz …"));
        await gate.WaitAsync(ct);
        try
        {
            Report(L.T("Analyse vorbereiten: FFmpeg-Filter prüfen …"));
            var caps = await capabilities.AnalyzeAsync(job.Tools, ct);
            var available = new[] { "libvmaf", "ssim", "psnr" }.Where(caps.Filters.Contains).ToArray();
            if (available.Length == 0) throw new InvalidOperationException(L.T("Dieser FFmpeg-Build enthält keine der Analysefilter VMAF, SSIM oder PSNR."));
            var directory = Path.Combine(paths.Analyses, job.Id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Directory.CreateDirectory(directory);
            var dimensions = ScalingService.Calculate(output.Video!, new FilterOptions());
            var referenceOptions = FrameComparisonService.ReferenceOptions(job.Options);
            string reference = FilterService.Build(job.Source, referenceOptions, includeCustom: false);
            bool cutting=CutTimeline.Active(job.Options);
            if(cutting)reference=CutTimeline.VideoFilter(job.Source,job.Options)+(reference.Length>0?","+reference:"");
            string referencePrefix = reference.Length>0 ? reference+"," : "";
            double? vmaf = null, ssim = null, psnr = null;
            var frames = new SortedDictionary<int, QualityFrame>();
            var fps = output.Video!.FrameRate;
            if (fps is not > 0) throw new InvalidOperationException(L.T("Die zeitliche Vergleichsbasis ist unbekannt. Analyse abgebrochen."));
            Report(L.T("Vorbereitung: Frame-Zeitstempel lesen …"),0);
            var actualTimestamps = await ReadOutputTimestampsAsync(job,seconds=>Report(L.T("Vorbereitung: Frame-Zeitstempel lesen …"),seconds,force:false),ct);
            for(var metricIndex=0;metricIndex<available.Length;metricIndex++)
            {
                var metric=available[metricIndex];
                string Label()=>L.F($"{(metric=="libvmaf"?"VMAF":metric.ToUpperInvariant())} berechnen · Durchlauf {metricIndex+1} von {available.Length}");
                Report(Label(),0);
                string metricFile = metric == "libvmaf" ? "vmaf.json" : metric + ".log";
                string compare = metric == "libvmaf" ? $"libvmaf=log_fmt=json:log_path={metricFile}:n_threads=4" : $"{metric}=stats_file={metricFile}";
                // Both inputs retain normalized PTS. Framesync matches by time, not frame ordinal.
                string graph = $"[0:{output.Video.Index}]scale={dimensions.Width}:{dimensions.Height}:flags=lanczos,setsar=1,format=yuv420p,settb=AVTB,setpts=PTS-STARTPTS[dist];" +
                    $"[1:{job.Source.Video!.Index}]{referencePrefix}scale={dimensions.Width}:{dimensions.Height}:flags=lanczos,setsar=1,format=yuv420p,settb=AVTB,setpts=PTS-STARTPTS[ref];[dist][ref]{compare}:shortest=1:repeatlast=0";
                var args = new List<string> { "-hide_banner", "-nostdin", "-nostats", "-stats_period", "0.25", "-progress", "pipe:1", "-i", job.OutputPath };
                if(cutting)args.Insert(0,"-copyts");
                if (!cutting && job.Options.ClipStart > 0) args.AddRange(["-ss", FFmpegCommandBuilder.Number(job.Options.ClipStart.Value)]);
                args.AddRange(["-i", job.Source.Path, "-an", "-sn", "-filter_complex", graph, "-f", "null", "-"]);
                var parser=new ProgressParser(duration);
                var result = await runner.RunAsync(job.Tools.FFmpegPath, args, ct,outputLine:line=>
                {
                    if(parser.Push(line) is { } p) Report(Label(),p.Seconds,p,force:line=="progress=end");
                },workingDirectory: directory);
                await File.WriteAllTextAsync(Path.Combine(directory, metric + "-process.log"), result.StandardError, ct);
                result.EnsureSuccess(L.F($"Qualitätsanalyse {metric}; Filter oder Modell prüfen"));
                Report(L.F($"{(metric=="libvmaf"?"VMAF":metric.ToUpperInvariant())}-Messwerte auswerten …"));
                if (metric == "libvmaf")
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, metricFile), ct));
                    vmaf = doc.RootElement.GetProperty("pooled_metrics").GetProperty("vmaf").GetProperty("mean").GetDouble();
                    foreach (var frame in doc.RootElement.GetProperty("frames").EnumerateArray())
                    {
                        int n = frame.GetProperty("frameNum").GetInt32();
                        frames[n] = new(TimeAt(n), frame.GetProperty("metrics").GetProperty("vmaf").GetDouble(), null, null);
                    }
                }
                else
                {
                    var aggregate = Regex.Match(result.StandardError, metric == "ssim" ? @"All:([0-9.]+)" : @"average:([0-9.inf]+)");
                    double? value = ParseMetric(aggregate.Groups[1].Value);
                    if (metric == "ssim") ssim = value; else psnr = value;
                    foreach (var line in await File.ReadAllLinesAsync(Path.Combine(directory, metricFile), ct))
                    {
                        var nMatch = Regex.Match(line, @"n:(\d+)");
                        if (!nMatch.Success) continue;
                        int n = int.Parse(nMatch.Groups[1].Value) - 1;
                        var m = Regex.Match(line, metric == "ssim" ? @"All:([0-9.]+)" : @"psnr_avg:([0-9.inf]+)");
                        var frame = frames.GetValueOrDefault(n, new(TimeAt(n), null, null, null));
                        frames[n] = metric == "ssim" ? frame with { Ssim = ParseMetric(m.Groups[1].Value) } : frame with { Psnr = ParseMetric(m.Groups[1].Value) };
                    }
                }
            }
            var conditions = L.F($"SDR BT.709/8-Bit-Metrikraster {dimensions}; PTS-normalisierte zeitliche Zuordnung; Lanczos; ") +
                L.F($"Referenzgeometrie gemäß Job (Crop/Rotation/Skalierung/Padding), ohne freien Filter und Burn-in. ") +
                (job.Options.Filters.ToneMap ? L.T("HDR-Quelle nach explizitem Tonemapping; keine native HDR-VMAF-Aussage. ") : "") +
                L.F($"Filter: {string.Join(", ", available)}. Modell: {(available.Contains("libvmaf") ? L.T("FFmpeg/libvmaf-Standardmodell") : L.T("nicht verfügbar"))}.");
            var analysis = new QualityResult(vmaf, ssim, psnr, conditions, frames.Values.ToList(), directory);
            Report(L.T("Analyseergebnisse speichern …"));
            await AtomicFile.WriteJsonAsync(Path.Combine(directory, "analysis.json"), analysis, ct);
            ct.ThrowIfCancellationRequested();
            await store.SaveAnalysisAsync(job.Id, analysis);
            job.Analysis = analysis;
            await store.SaveJobAsync(job);
            progress?.Report(new(L.T("Analyse abgeschlossen"),100,duration,duration,null,clock.Elapsed,TimeSpan.Zero));
            return analysis;
            double TimeAt(int n) => n < actualTimestamps.Count ? actualTimestamps[n] : n / fps.Value;
        }
        finally { gate.Release(); }
    }
    private async Task<List<double>> ReadOutputTimestampsAsync(EncodingJob job, Action<double> report, CancellationToken ct)
    {
        var result = new List<double>();
        var r = await runner.RunAsync(job.Tools.FFprobePath, ["-v", "error", "-select_streams", job.OutputInfo!.Video!.Index.ToString(),
            "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "csv=p=0", job.OutputPath], ct,
            line => { if (double.TryParse(line.Split(',')[0], CultureInfo.InvariantCulture, out var t)) {var seconds=Math.Max(0,t-job.OutputInfo.StartTime);result.Add(seconds);report(seconds);} });
        r.EnsureSuccess(L.T("Analyse-Zeitstempel")); return result;
    }
    private static double? ParseMetric(string text) => text == "inf" ? double.PositiveInfinity : double.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : null;
}

public sealed class SubtitleService(ProcessRunner runner)
{
    public async Task ExportAsync(Toolchain tools, MediaInfo media, SubtitleTrackOptions subtitle, string path, CancellationToken ct = default)
    {
        if (File.Exists(path) || Path.GetFullPath(path).Equals(media.Path, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("Die Exportdatei darf keine vorhandene Datei überschreiben."));
        string codec = Path.GetExtension(path).ToLowerInvariant() switch { ".srt" => "srt", ".ass" => "ass", ".vtt" => "webvtt", _ => "copy" };
        if (subtitle.IsBitmap && codec != "copy") throw new ArgumentException(L.T("Bilduntertitel können ohne OCR nicht in Text umgewandelt werden."));
        var result = await runner.RunAsync(tools.FFmpegPath, ["-hide_banner", "-nostdin", "-n", "-i", media.Path, "-map", $"0:{subtitle.InputIndex}", "-c:s", codec, path], ct);
        result.EnsureSuccess(L.T("Untertitelexport"));
    }
}
