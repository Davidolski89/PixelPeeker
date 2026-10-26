using System.Text;
using System.Text.Json;
using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public static class MediaDetailsText
{
    public static string Format(MediaInfo info)
    {
        static string N(object? x) => x?.ToString() ?? L.T("unbekannt");
        var text = new StringBuilder(L.F($"Datei: {info.Path}\nContainer: {info.Container}\nGröße: {info.Size:N0} Bytes\nDauer: {N(info.Duration)} s\nGesamtbitrate: {N(info.BitRate)} bit/s\nStartzeit: {info.StartTime}\n"));
        foreach (var stream in info.Streams)
        {
            text.AppendLine($"\n{stream.Type.ToUpperInvariant()} #{stream.Index} — {stream.Summary}");
            text.AppendLine(L.F($"Profil: {N(stream.Profile)} · Level: {N(stream.Level)} · Bitrate: {N(stream.BitRate)}"));
            if (stream.Type == "video") text.AppendLine(L.F($"Raster: {stream.Width} × {stream.Height} · SAR: {N(stream.SampleAspectRatio)} · DAR: {N(stream.DisplayAspectRatio)}\nBildrate: {N(stream.AverageFrameRate)} · {stream.FrameRateMode}\nPixelformat: {N(stream.PixelFormat)} · Tiefe: {N(stream.BitDepth)}\nFarbraum: {N(stream.ColorSpace)} · Primärfarben: {N(stream.ColorPrimaries)} · Transfer: {N(stream.ColorTransfer)} · Bereich: {N(stream.ColorRange)}\nHDR: {(stream.IsHdr ? L.T("Ja") : L.T("Nicht erkannt"))} · Rotation: {stream.Rotation}°\nSide Data: {stream.SideDataJson}"));
            text.AppendLine(L.F($"Sprache: {stream.Language} · Titel: {stream.Title} · Default: {stream.Default} · Forced: {stream.Forced}"));
            foreach (var tag in stream.Tags) text.AppendLine($"  {tag.Key}: {tag.Value}");
        }
        text.AppendLine(L.T("\nKapitel:\n") + info.ChaptersJson + L.T("\nContainer-Metadaten:\n") + JsonSerializer.Serialize(info.Tags, Json.Options));
        return text.ToString();
    }

    public static string FormatUnprobed(string path)
    {
        var info = new FileInfo(path);
        var size = info.Exists ? info.Length.ToString("N0") : L.T("unbekannt");
        return L.F($"Datei: {path}\nGröße: {size} Bytes\n") + L.T("FFmpeg/ffprobe fehlt, daher wurden keine Spuren, Codecs oder Laufzeiten gelesen.");
    }
}

public static class QueueSummaryText
{
    public static string Format(IEnumerable<EncodingJob> jobs)
    {
        var list = jobs as ICollection<EncodingJob> ?? jobs.ToList();
        return L.F($"{list.Count} Jobs · {list.Count(j => j.Status == JobStatus.Läuft)} aktiv · {list.Count(j => j.Status == JobStatus.Abgeschlossen)} fertig · {list.Count(j => j.Status == JobStatus.Fehlgeschlagen)} Fehler");
    }
}
