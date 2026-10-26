namespace FFmpegStudio.Core;

public static class SoftwareThreading
{
    public static bool SupportsCount(string encoder) => encoder is "libx264" or "libx264rgb" or "libx265" or "libopenh264" or "libaom-av1" or "librav1e" or "libvvenc";
    public static int ResolveCount(VideoOptions video, int logicalProcessors) => video.ThreadMode switch
    {
        ThreadControlMode.FesteAnzahl => video.ThreadCount,
        ThreadControlMode.CPUAnteil => Math.Max(1, (int)((long)Math.Max(1, logicalProcessors) * video.ThreadPercent / 100)),
        _ => 0
    };
    public static IEnumerable<string> Validate(VideoOptions video, Capabilities? capabilities = null)
    {
        if (!Enum.IsDefined(video.ThreadMode)) { yield return L.T("Ungültiger Thread-Modus."); yield break; }
        if (video.ThreadMode == ThreadControlMode.Automatisch) yield break;
        if (!SupportsCount(video.Encoder)) { yield return L.T("Dieser Encoder verwendet keine Threadanzahl-Steuerung. SVT-AV1 über die Parallelitätsstufe lp einstellen."); yield break; }
        if (video.ThreadMode == ThreadControlMode.FesteAnzahl && (video.ThreadCount < 1 || video.ThreadCount > Environment.ProcessorCount))
            yield return L.F($"Die Threadanzahl muss zwischen 1 und {Environment.ProcessorCount} liegen.");
        if (video.ThreadMode == ThreadControlMode.CPUAnteil && video.ThreadPercent is < 1 or > 100)
            yield return L.T("Der CPU-Kernanteil muss zwischen 1 und 100 Prozent liegen.");
        var parameterKey = video.Encoder switch { "libx265" => "x265-params", "libx264" or "libx264rgb" => "x264-params", "librav1e" => "rav1e-params", "libvvenc" => "vvenc-params", _ => "" };
        var nativeKey = video.Encoder switch { "libx265" => "pools", "libvvenc" => "threads", _ => "threads" };
        if (parameterKey.Length > 0 && SvtAv1Settings.Get(video.Extra.GetValueOrDefault(parameterKey, ""), nativeKey).Length > 0)
            yield return L.F($"Threadsteuerung und Expertenparameter {nativeKey} nicht gleichzeitig setzen. Einen der beiden Werte leeren.");
        if (video.Encoder == "libx265" && capabilities is not null && capabilities.Find(video.Encoder)?.Options?.Any(o => o.Name == "x265-params") != true)
            yield return L.T("Dieser FFmpeg-Build bietet keine x265-Worker-Pool-Steuerung an.");
    }
    public static string Describe(VideoOptions video)
    {
        if (video.IsSvtAv1) return L.T("SVT-AV1: lp 0/leer wählt die Parallelität anhand der CPU. 1–6 steuert Threads, gleichzeitig verarbeitete Bilder und Speicherbedarf; keine feste Threadanzahl.");
        if (!SupportsCount(video.Encoder)) return L.T("Die Threadsteuerung gilt für Softwareencoder.");
        var count = ResolveCount(video, Environment.ProcessorCount);
        var selection = count == 0 ? L.T("Der Encoder wählt die Threadanzahl automatisch.")
            : L.F($"{count} von {Environment.ProcessorCount} logischen CPU-Kernen als Thread-Budget pro Job. Ein Kernanteil ist kein CPU-Auslastungslimit.");
        return selection + " " + (video.Encoder == "libx265"
            ? L.T("x265 setzt damit pools (Worker-Threads); zusätzliche Frame- und Hilfsthreads sind möglich.")
            : L.T("Die Anzahl wird an den Videoencoder übergeben; Hilfsthreads und die tatsächlich erreichbare Parallelität hängen vom Codec und Bild ab."));
    }
}
