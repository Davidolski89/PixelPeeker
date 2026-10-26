using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public static class MediaProbeParser
{
    public static MediaInfo Parse(string json, string path)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error)) throw new InvalidDataException(error.ToString());
        var streams = new List<MediaStream>();
        if (root.TryGetProperty("streams", out var items))
        foreach (var s in items.EnumerateArray())
        {
            var tags = Tags(s);
            var sideData = s.TryGetProperty("side_data_list", out var side) ? side.GetRawText() : "[]";
            double rotation = double.TryParse(tags.GetValueOrDefault("rotate"), CultureInfo.InvariantCulture, out var r) ? r : 0;
            if (side.ValueKind == JsonValueKind.Array)
                foreach (var sd in side.EnumerateArray()) if (Number(sd, "rotation") is { } degrees) rotation = degrees;
            s.TryGetProperty("disposition", out var disposition);
            var pixel = Text(s, "pix_fmt");
            var depth = (int?)Number(s, "bits_per_raw_sample");
            if (depth is null or 0 && pixel is not null)
            {
                var m = Regex.Match(pixel, @"(?:p|gbrp)(10|12|14|16)|p0(10|16)");
                depth = m.Success ? int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
            }
            streams.Add(new()
            {
                Index = (int)(Number(s, "index") ?? 0), Type = Text(s, "codec_type") ?? "",
                Codec = Text(s, "codec_name") ?? "unbekannt", Profile = Text(s, "profile"), Level = (int?)Number(s, "level"),
                Width = (int)(Number(s, "width") ?? 0), Height = (int)(Number(s, "height") ?? 0),
                SampleAspectRatio = Text(s, "sample_aspect_ratio"), DisplayAspectRatio = Text(s, "display_aspect_ratio"),
                AverageFrameRate = Text(s, "avg_frame_rate"), RealFrameRate = Text(s, "r_frame_rate"),
                TimeBase = Text(s,"time_base"),
                BitRate = (long?)Number(s, "bit_rate"), PixelFormat = pixel, BitDepth = depth,
                ColorSpace = Text(s, "color_space"), ColorPrimaries = Text(s, "color_primaries"), ColorTransfer = Text(s, "color_transfer"), ColorRange = Text(s, "color_range"),
                Rotation = rotation, SampleRate = (int?)Number(s, "sample_rate"), Channels = (int?)Number(s, "channels"), ChannelLayout = Text(s, "channel_layout"),
                Default = Number(disposition, "default") == 1, Forced = Number(disposition, "forced") == 1, AttachedPicture = Number(disposition, "attached_pic") == 1,
                Tags = tags, SideDataJson = sideData
            });
        }
        root.TryGetProperty("format", out var format);
        return new()
        {
            Path = path, Streams = streams, Container = Text(format, "format_name") ?? "unbekannt",
            Duration = Number(format, "duration"), StartTime = Number(format, "start_time") ?? 0,
            Size = (long)(Number(format, "size") ?? 0), BitRate = (long?)Number(format, "bit_rate"), Tags = Tags(format),
            ChaptersJson = root.TryGetProperty("chapters", out var chapters) ? chapters.GetRawText() : "[]", RawJson = json
        };
    }
    public static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
    public static double? Number(JsonElement item, string name) => double.TryParse(Text(item, name), CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    private static Dictionary<string, string> Tags(JsonElement item) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("tags", out var tags)
        ? tags.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase) : [];
}

public static class CapabilityParser
{
    public static List<EncoderInfo> Encoders(string text)
    {
        var list = new List<EncoderInfo>();
        foreach (Match m in Regex.Matches(text, @"(?m)^\s*([VAS])[A-Z.]{5}\s+(\S+)\s+(.+)$"))
        {
            var name = m.Groups[2].Value;
            if (name == "=") continue;
            list.Add(new(name, m.Groups[3].Value.Trim(), m.Groups[1].Value switch { "V" => "video", "A" => "audio", _ => "subtitle" },
                m.Groups[1].Value == "V" ? ScalingService.Family(name) : "", name.Contains("nvenc") || name.Contains("_qsv") || name.Contains("_amf") || name.Contains("_mf")));
        }
        return list;
    }
    public static EncoderInfo WithHelp(EncoderInfo info, string help)
    {
        var options = new List<EncoderOption>();
        EncoderOption? current = null;
        foreach (var line in help.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s+-(\S+)\s+<([^>]+)>\s+\S+\s*(.*)$");
            if (m.Success) { current = new(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Trim(), []); options.Add(current); }
            else if (current is not null)
            {
                var choice = Regex.Match(line, @"^\s{4,}(\S+)\s+(-?\d+)\s+\S+");
                if (choice.Success) current.Choices.Add(choice.Groups[1].Value);
            }
        }
        // Numeric speed controls (SVT-AV1, AOM, rav1e) have no named enum entries.
        // Negative values select automatic/internal modes; keep the normal nonnegative presets in the list.
        foreach (var option in options.Where(o => o.Type == "int" && o.Name is "preset" or "cpu-used" or "speed" && o.Choices.Count == 0))
        {
            var range = Regex.Match(option.Description, @"\(from (-?\d+) to (-?\d+)\)");
            if (!range.Success || !int.TryParse(range.Groups[1].Value, CultureInfo.InvariantCulture, out var lower)
                || !int.TryParse(range.Groups[2].Value, CultureInfo.InvariantCulture, out var upper)) continue;
            lower = Math.Max(0, lower);
            if (upper < lower || upper > 64) continue;
            for (var value = lower; value <= upper; value++) option.Choices.Add(value.ToString(CultureInfo.InvariantCulture));
        }
        var pix = Regex.Match(help, @"Supported pixel formats:\s*([^\r\n]+)");
        return info with { Help = help, Options = options, PixelFormats = pix.Success ? pix.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() : [] };
    }
    public static HashSet<string> Filters(string text) => Regex.Matches(text, @"(?m)^\s*[TSC.]{2,3}\s+(\w+)\s+[AVN|]+->[AVN|]+").Select(m => m.Groups[1].Value).ToHashSet();
}

public sealed class ProgressParser(double? duration)
{
    private readonly Dictionary<string, string> values = [];
    private readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
    public ProgressInfo? Push(string line)
    {
        var pos = line.IndexOf('=');
        if (pos < 1) return null;
        values[line[..pos]] = line[(pos + 1)..].Trim();
        if (line[..pos] != "progress") return null;
        double Get(string key) => double.TryParse(values.GetValueOrDefault(key)?.TrimEnd('x'), CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
        var seconds = Math.Max(0, Get("out_time_us") / 1_000_000);
        var speed = Get("speed");
        var percent = duration > 0 ? Math.Clamp(seconds / duration.Value * 100, 0, 100) : (double?)null;
        return new(percent, seconds, speed, Get("fps"), (long)Get("total_size"), elapsed.Elapsed,
            duration > seconds && speed > 0 ? TimeSpan.FromSeconds((duration.Value - seconds) / speed) : null);
    }
}

public static class VersionMetadata
{
    public static string ParseVersion(string text)
    {
        var version = text.Trim();
        if (!Regex.IsMatch(version, @"^\d{4}-\d{2}-\d{2}-git-[a-fA-F0-9]{7,40}$")) throw new InvalidDataException(L.T("Unbekanntes Gyan-Versionsformat."));
        return version;
    }
    public static string ParseSha256(string text)
    {
        var match = Regex.Match(text.Trim(), @"^([a-fA-F0-9]{64})(?:\s|$)");
        if (!match.Success) throw new InvalidDataException(L.T("Ungültige SHA-256-Metadaten."));
        return match.Groups[1].Value.ToUpperInvariant();
    }
    public static bool IsNewBuild(string? installed, string available) =>
        !string.Equals(installed?.Trim(), ParseVersion(available), StringComparison.OrdinalIgnoreCase)
        && (!DateOnly.TryParse(installed?.Length >= 10 ? installed[..10] : "", out var oldDate)
            || DateOnly.Parse(available[..10], CultureInfo.InvariantCulture) >= oldDate);
}
