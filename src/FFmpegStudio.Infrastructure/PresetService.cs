using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class PresetService(AppPaths paths)
{
    public async Task<List<Preset>> LoadAsync()
    {
        var presets = BuiltIns().ToList();
        foreach (var file in Directory.GetFiles(paths.Presets, "*.json"))
        {
            var p = await ImportAsync(file);
            presets.RemoveAll(x => x.Name == p.Name); presets.Add(p);
        }
        return presets;
    }
    public static async Task<Preset> ImportAsync(string path)
    {
        var p = await AtomicFile.ReadJsonAsync<Preset>(path) ?? throw new InvalidDataException(L.T("Leeres Preset."));
        if (p.SchemaVersion != 1 || string.IsNullOrWhiteSpace(p.Name) || p.Options is null) throw new InvalidDataException(L.T("Ungültiges oder inkompatibles Preset."));
        p.Options.Cuts.Clear();
        return p;
    }
    private static Preset WithoutCuts(Preset preset){var copy=Json.Clone(preset);copy.Options.Cuts.Clear();return copy;}
    public Task SaveAsync(Preset preset) => AtomicFile.WriteJsonAsync(PathFor(preset.Name), WithoutCuts(preset));
    public static Task ExportAsync(Preset preset, string path) => AtomicFile.WriteJsonAsync(path, WithoutCuts(preset));
    public void Delete(Preset preset)
    {
        var path = PathFor(preset.Name);
        if (!File.Exists(path)) throw new InvalidOperationException(L.T("Mitgelieferte Presets können überschrieben, aber nicht entfernt werden."));
        File.Delete(path);
    }
    private string PathFor(string name) => Path.Combine(paths.Presets, QueueService.SanitizeFileName(name) + ".json");
    public static EncodingOptions Apply(Preset preset, MediaInfo media, EncodingOptions? current = null)
    {
        var options = Json.Clone(preset.Options);
        var mapping = EncodingOptions.ForMedia(media);
        // Match saved per-stream choices by stream ordinal; indices always come from the new source.
        for (int i = 0; i < mapping.Audio.Count; i++)
        {
            if (i < options.Audio.Count)
            {
                var saved = Json.Clone(options.Audio[i]); var source = mapping.Audio[i];
                saved.InputIndex = source.InputIndex; saved.SourceCodec = source.SourceCodec; saved.SourceSummary = source.SourceSummary; saved.SourceChannels = source.SourceChannels;
                mapping.Audio[i] = saved;
            }
        }
        for (int i = 0; i < mapping.Subtitles.Count; i++)
        {
            if (i < options.Subtitles.Count)
            {
                var saved = Json.Clone(options.Subtitles[i]); var source = mapping.Subtitles[i];
                saved.InputIndex = source.InputIndex; saved.SubtitleOrdinal = source.SubtitleOrdinal; saved.SourceCodec = source.SourceCodec; saved.SourceSummary = source.SourceSummary; saved.IsBitmap = source.IsBitmap;
                mapping.Subtitles[i] = saved;
            }
        }
        options.Audio = mapping.Audio; options.Subtitles = mapping.Subtitles;
        options.Cuts=Json.Clone(current?.Cuts??[]);options.CutsEnabled=current?.CutsEnabled??true;
        options.OutputDirectory = string.IsNullOrEmpty(current?.OutputDirectory) ? mapping.OutputDirectory : current.OutputDirectory;
        options.PresetName = preset.Name;
        var rule = preset.AudioRule;
        foreach (var track in options.Audio)
        {
            if (rule.KeepLanguages.Length > 0 && !rule.KeepLanguages.Contains(track.Language, StringComparer.OrdinalIgnoreCase)) track.Action = TrackAction.Entfernen;
            else if (rule.CopyCodecs.Contains(track.SourceCodec)) track.Action = TrackAction.Kopieren;
            else if (rule.TranscodeOthers) { track.Action = TrackAction.Encodieren; track.Encoder = rule.Encoder; track.BitRateKbps = rule.BitRateKbps; }
        }
        if (rule.PreferGerman)
        {
            var preferred = options.Audio.FirstOrDefault(a => a.Language is "deu" or "ger" && a.Action != TrackAction.Entfernen);
            if (preferred is not null) foreach (var a in options.Audio) a.IsDefault = ReferenceEquals(a, preferred);
        }
        if (rule.LanguageOrder.Length > 0)
        {
            var ordered = options.Audio.OrderBy(a => { var i = Array.IndexOf(rule.LanguageOrder, a.Language); return i < 0 ? int.MaxValue : i; }).ToList();
            options.Audio.Clear(); foreach (var track in ordered) options.Audio.Add(track);
        }
        if(CutTimeline.Active(options))foreach(var track in options.Audio.Where(a=>a.Action==TrackAction.Kopieren))track.Action=TrackAction.Encodieren;
        return options;
    }
    public static IEnumerable<Preset> BuiltIns()
    {
        foreach (var (name, description, encoder, quality, speed, scale) in new[]
        {
            ("Höchste Qualität", "HEVC, CRF 18, langsame Kompression; hohe Qualität bei langer Laufzeit.", "libx265", 18, "slow", ScaleMode.Original),
            ("Gute Qualität bei kleiner Datei", "HEVC, CRF 23; ausgewogene Archivierung. Größe ist vom Inhalt abhängig.", "libx265", 23, "medium", ScaleMode.Original),
            ("Sehr kleine Datei", "AV1, CRF 35; stärkere Kompression mit sichtbaren Verlusten möglich.", "libsvtav1", 35, "6", ScaleMode.Original),
            ("Schnellstes Encoding", "x264 ultrafast, CRF 23; hohe Geschwindigkeit und größere Dateien.", "libx264", 23, "ultrafast", ScaleMode.Original),
            ("NVIDIA NVENC", "HEVC NVENC, P6, CQ 23; benötigt erfolgreich geprüfte NVIDIA-Hardware.", "hevc_nvenc", 23, "p6", ScaleMode.Original),
            ("AV1 Archivierung", "SVT-AV1, CRF 24, Preset 4; moderne Wiedergabegeräte erforderlich.", "libsvtav1", 24, "4", ScaleMode.Original),
            ("HEVC Archivierung", "x265, CRF 20, slow; MKV mit Originalton.", "libx265", 20, "slow", ScaleMode.Original),
            ("Streaming", "H.264, CRF 22; MP4/AAC für breite Wiedergabekompatibilität.", "libx264", 22, "medium", ScaleMode.FullHD1080),
            ("4K → 1080p", "Maximal 1920 × 1080, Lanczos, HEVC; Seitenverhältnis bleibt erhalten.", "libx265", 23, "medium", ScaleMode.FullHD1080),
            ("1080p → 720p", "Maximal 1280 × 720, Lanczos, HEVC; kein automatisches Upscaling.", "libx265", 23, "medium", ScaleMode.HD720)
        })
        {
            var p = new Preset { Name = name, Description = description };
            p.Options.Video.Encoder = encoder; p.Options.Video.Quality = quality; p.Options.Video.Preset = speed; p.Options.Filters.ScaleMode = scale;
            if (name == "Streaming") { p.Options.Container = "mp4"; p.AudioRule = new() { TranscodeOthers = true, Encoder = "aac", BitRateKbps = 192 }; }
            yield return p;
        }
    }
}
