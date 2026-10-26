using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class MediaProbeService(ProcessRunner runner)
{
    public async Task<MediaInfo> ProbeAsync(Toolchain tools, string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("Mediendatei nicht gefunden."), path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var result = await runner.RunAsync(tools.FFprobePath, ["-v", "error", "-show_format", "-show_streams", "-show_chapters", "-print_format", "json", "-i", Path.GetFullPath(path)], timeout.Token);
        result.EnsureSuccess(L.T("Medienanalyse"));
        return MediaProbeParser.Parse(result.StandardOutput, Path.GetFullPath(path));
    }
}

public sealed class FFmpegCapabilityService(ProcessRunner runner)
{
    private readonly ConcurrentDictionary<string, Capabilities> cache = new();
    public async Task<Capabilities> AnalyzeAsync(Toolchain tools, CancellationToken ct = default)
    {
        if (cache.TryGetValue(tools.FFmpegPath, out var cached)) return cached;
        string[] commands = ["-version", "-buildconf", "-encoders", "-decoders", "-filters", "-hwaccels", "-pix_fmts"];
        var results = await Task.WhenAll(commands.Select(c => runner.RunAsync(tools.FFmpegPath, ["-hide_banner", c], ct)));
        foreach (var result in results) result.EnsureSuccess(L.T("Fähigkeitsprüfung"));
        string Text(int i) => results[i].StandardOutput + results[i].StandardError;
        var caps = new Capabilities
        {
            Version = Text(0), BuildConfiguration = Text(1),
            Encoders = CapabilityParser.Encoders(Text(2)).Where(e => e.MediaType != "video" || VideoEncoderPolicy.IsAllowed(e.Name)).ToList(),
            Decoders = CapabilityParser.Encoders(Text(3)).Select(e => e.Name).ToHashSet(),
            Filters = CapabilityParser.Filters(Text(4)),
            HardwareAccelerators = Text(5).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(l => !l.Contains(' ') && !l.EndsWith(':')).ToHashSet(),
            PixelFormats = Regex.Matches(Text(6), @"(?m)^\s*[IOHPB.]{5}\s+(\S+)").Select(m => m.Groups[1].Value).ToHashSet()
        };
        using var throttle = new SemaphoreSlim(4);
        var detailed = await Task.WhenAll(caps.Encoders.Select(async encoder =>
        {
            if (encoder.Family.Length == 0 && encoder.MediaType == "video") return encoder;
            await throttle.WaitAsync(ct);
            try
            {
                var help = await runner.RunAsync(tools.FFmpegPath, ["-hide_banner", "-h", "encoder=" + encoder.Name], ct);
                return CapabilityParser.WithHelp(encoder, help.StandardOutput + help.StandardError);
            }
            finally { throttle.Release(); }
        }));
        caps.Encoders = detailed.ToList(); cache[tools.FFmpegPath] = caps;
        return caps;
    }
}

public sealed class HardwareDetectionService(ProcessRunner runner)
{
    private readonly ConcurrentDictionary<string, HardwareProbe> probes = new();
    public async Task<HardwareProbe> ProbeEncoderAsync(Toolchain tools, string encoder, int gpu, string? pixelFormat = null, CancellationToken ct = default)
    {
        var key = $"{tools.FFmpegPath}|{encoder}|{gpu}|{pixelFormat}";
        if (probes.TryGetValue(key, out var found)) return found;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var args = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=640x360:r=25", "-frames:v", "3", "-an", "-c:v", encoder };
        if (encoder.EndsWith("_nvenc")) args.AddRange(["-gpu", gpu.ToString(CultureInfo.InvariantCulture)]);
        if (!string.IsNullOrEmpty(pixelFormat)) args.AddRange(["-pix_fmt", pixelFormat]);
        args.AddRange(["-f", "null", "-"]);
        try
        {
            var result = await runner.RunAsync(tools.FFmpegPath, args, timeout.Token);
            var probe = new HardwareProbe(result.ExitCode == 0, result.ExitCode == 0 ? L.F($"Probe-Encode erfolgreich · GPU {gpu} · {encoder}") : result.StandardError);
            probes[key] = probe; return probe;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, L.T("Hardwareprüfung hat das Zeitlimit überschritten.")); }
    }
}

public sealed class PreviewService(AppPaths paths, ProcessRunner runner, SettingsService settings)
{
    private readonly SemaphoreSlim gate = new(2);
    public async Task<string> FrameAsync(Toolchain tools, MediaInfo media, double seconds, string filter = "", int? width = null, int? height = null, CancellationToken ct = default,SourceFramePosition? exactFrame=null)
    {
        seconds = Math.Max(0, seconds);
        var source = new FileInfo(media.Path);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{media.Path}|{source.Length}|{source.LastWriteTimeUtc.Ticks}|{seconds:R}|{filter}|{width}|{height}|{tools.Version}|{exactFrame?.Timestamp}")));
        var output = Path.Combine(paths.Cache, id + ".png");
        if (File.Exists(output)) return output;
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(output)) return output;
            var filters = new List<string>();
            if(exactFrame is not null)filters.Add($"select=eq(pts\\,{exactFrame.Timestamp.ToString(CultureInfo.InvariantCulture)})");
            if (filter.Length > 0) filters.Add(filter);
            if (media.Video?.IsHdr == true && !filter.Contains("tonemap")) filters.Add(FilterService.HdrToSdr());
            if (width.HasValue && height.HasValue) filters.Add($"scale={width}:{height}:flags=lanczos,setsar=1");
            else if (width.HasValue) filters.Add($"scale={width}:-2:flags=lanczos,setsar=1");
            filters.Add("format=rgb24");
            var temp = Path.Combine(paths.Cache, id + "." + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var args = new List<string> { "-hide_banner", "-nostdin", "-v", "error" };
                if(exactFrame is not null)args.Add("-copyts");
                var seek=exactFrame?.DecodeFromSeconds??seconds;
                if(exactFrame is null||seek>0)args.AddRange(["-ss",FFmpegCommandBuilder.Number(seek)]);
                args.AddRange(["-i", media.Path, "-map", $"0:{media.Video!.Index}", "-frames:v", "1", "-an", "-sn", "-vf", string.Join(",", filters), "-fps_mode","passthrough","-threads", "1", "-n", temp]);
                var result = await runner.RunAsync(tools.FFmpegPath, args, ct);
                result.EnsureSuccess(L.T("Frame-Vorschau"));
                if (!File.Exists(temp) || new FileInfo(temp).Length == 0) throw new InvalidDataException(L.T("Kein Frame an dieser Position gefunden."));
                try{File.Move(temp,output,false);}
                catch(IOException)when(File.Exists(output)){ /* Another preview completed the same cached frame. */ }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            TrimCache(output);
            return output;
        }
        finally { gate.Release(); }
    }
    private void TrimCache(string keep)
    {
        var files = new DirectoryInfo(paths.Cache).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length), limit = Math.Max(32, settings.Current.CacheLimitMiB) * 1048576L;
        foreach (var file in files)
        {
            if (total <= limit) break;
            if (file.FullName == keep) continue;
            try { var size = file.Length; file.Delete(); total -= size; } catch (IOException) { }
        }
    }
    public async Task<(int Left, int Right, int Top, int Bottom)> DetectCropAsync(Toolchain tools, MediaInfo source, CancellationToken ct = default)
    {
        var candidates = new List<(int W, int H, int X, int Y)>();
        foreach (var fraction in new[] { .1, .35, .65 })
        {
            var result = await runner.RunAsync(tools.FFmpegPath, ["-hide_banner", "-nostdin", "-ss", FFmpegCommandBuilder.Number((source.Duration ?? 10) * fraction),
                "-i", source.Path, "-t", "3", "-an", "-sn", "-vf", "cropdetect=limit=24:round=2:reset=0", "-f", "null", "-"], ct);
            result.EnsureSuccess(L.T("Balkenerkennung"));
            candidates.AddRange(Regex.Matches(result.StandardError, @"crop=(\d+):(\d+):(\d+):(\d+)").Select(m =>
                (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value))));
        }
        if (candidates.Count == 0) throw new InvalidOperationException(L.T("Kein verlässlicher Crop-Vorschlag gefunden."));
        var best = candidates.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        var video = source.Video!;
        int w = video.Width, h = video.Height;
        if (Math.Abs(video.Rotation % 180) > 45) (w, h) = (h, w);
        return (best.X, w - best.W - best.X, best.Y, h - best.H - best.Y);
    }
}
