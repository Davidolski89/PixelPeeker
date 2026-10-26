using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public sealed class FFmpegCommandBuilder
{
    public static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    public IReadOnlyList<string> Build(MediaInfo media, EncodingOptions options, string output, Capabilities? capabilities = null)
    {
        var errors = Validate(media, options, capabilities);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (Path.GetFullPath(media.Path).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.T("Die Originaldatei darf nicht überschrieben werden."));
        var v = options.Video;
        bool cutting=CutTimeline.Active(options);
        var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-loglevel", "info", "-progress", "pipe:1", "-nostats" };
        void Add(string key, object value) { args.Add(key); args.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!); }
        if (v.CudaDecode) { Add("-hwaccel", "cuda"); Add("-hwaccel_device", v.GpuIndex); if (v.CudaScale) Add("-hwaccel_output_format", "cuda"); }
        if(cutting)args.Add("-copyts");
        if (!cutting && options.ClipStart > 0) Add("-ss", Number(options.ClipStart.Value));
        Add("-i", media.Path);
        var cutSubtitles=options.Subtitles.Where(s=>s.Action is TrackAction.Kopieren or TrackAction.Encodieren).ToList();
        if(cutting)foreach(var subtitle in cutSubtitles)Add("-i",CutTimeline.SubtitlePath(output,subtitle));
        if (!cutting && options.ClipDuration > 0) Add("-t", Number(options.ClipDuration.Value));
        var filter = FilterService.Build(media, options,includeSubtitles:!cutting);
        var burn = options.Subtitles.FirstOrDefault(s => s.Action == TrackAction.Einbrennen);
        if(cutting)
        {
            var video=CutTimeline.VideoFilter(media,options);
            if(burn is not null)video+=$",subtitles=filename={FilterService.EscapePath(CutTimeline.SubtitlePath(output,burn))}"+
                (CutTimeline.Fonts(media,output).Any()?$":fontsdir={FilterService.EscapePath(CutTimeline.FontDirectory(output))}":"");
            if(filter.Length>0)video+=","+filter;
            var graph=new List<string>{$"[0:{media.Video!.Index}]{video}[video]"};
            int ordinal=0;
            foreach(var track in options.Audio.Where(a=>a.Action!=TrackAction.Entfernen))graph.Add(CutTimeline.AudioGraph(media,options,track,ordinal++));
            Add("-filter_complex",string.Join(';',graph));Add("-map","[video]");
            Add("-fps_mode:v:0","vfr");Add("-enc_time_base:v:0","filter");
        }
        else if (burn is { IsBitmap: true })
        {
            Add("-filter_complex", $"[0:{media.Video!.Index}][0:{burn.InputIndex}]overlay=eof_action=pass:shortest=0[burn];[burn]{(filter.Length > 0 ? filter : "null")}[video]");
            Add("-map", "[video]");
        }
        else
        {
            Add("-map", $"0:{media.Video!.Index}");
            if (filter.Length > 0) Add("-vf", filter);
        }
        Add("-c:v:0", v.Encoder);
        if (v.IsNvenc) Add("-gpu", v.GpuIndex);
        switch (v.RateControl)
        {
            case RateControl.Qualität:
                if (v.IsNvenc) { Add("-rc", "vbr"); Add("-cq", Number(v.Quality)); Add("-b:v", "0"); }
                else if (v.Encoder is "libvvenc" or "librav1e") Add("-qp", Number(v.Quality));
                else { Add("-crf", Number(v.Quality)); if (v.Encoder == "libaom-av1") Add("-b:v", "0"); }
                break;
            case RateControl.QP: Add("-qp", Number(v.Quality)); if (v.IsNvenc) Add("-rc", "constqp"); break;
            default:
                Add("-b:v", $"{v.BitRateKbps}k");
                if (v.IsNvenc) Add("-rc", v.RateControl == RateControl.CBR ? "cbr" : "vbr");
                if (v.RateControl == RateControl.CBR) { Add("-minrate", $"{v.BitRateKbps}k"); Add("-maxrate", $"{v.BitRateKbps}k"); Add("-bufsize", $"{(v.BufferKbps > 0 ? v.BufferKbps : v.BitRateKbps * 2)}k"); }
                break;
        }
        if (v.MaxRateKbps > 0 && v.RateControl != RateControl.CBR) Add("-maxrate", $"{v.MaxRateKbps}k");
        if (v.BufferKbps > 0 && v.RateControl != RateControl.CBR) Add("-bufsize", $"{v.BufferKbps}k");
        if (!string.IsNullOrEmpty(v.Preset)) Add(v.Encoder == "libaom-av1" ? "-cpu-used" : v.Encoder == "librav1e" ? "-speed" : "-preset", v.Preset);
        if (!string.IsNullOrEmpty(v.Tune)) Add("-tune", v.Tune);
        if (!string.IsNullOrEmpty(v.Profile)) Add("-profile:v", v.Profile);
        if (!string.IsNullOrEmpty(v.Level)) Add("-level:v", v.Level);
        if (!string.IsNullOrEmpty(v.PixelFormat)) Add("-pix_fmt", v.PixelFormat);
        if (v.Gop > 0) Add("-g", v.Gop);
        if (v.BFrames >= 0) Add("-bf", v.BFrames);
        if (v.References > 0) Add("-refs", v.References);
        var extra = new Dictionary<string, string>(v.Extra);
        var threadCount = SoftwareThreading.ResolveCount(v, Environment.ProcessorCount);
        if (v.CanControlThreads && threadCount > 0)
        {
            if (v.Encoder == "libx265") extra["x265-params"] = SvtAv1Settings.Set(extra.GetValueOrDefault("x265-params", ""), "pools", threadCount.ToString(CultureInfo.InvariantCulture));
            else Add("-threads:v:0", threadCount);
            if (v.Encoder == "libaom-av1" && !extra.ContainsKey("row-mt") && (capabilities is null || capabilities.Find(v.Encoder)?.Options?.Any(o => o.Name == "row-mt") == true))
                Add("-row-mt", "1");
        }
        foreach (var kv in extra.Where(kv => kv.Value.Length > 0)) Add("-" + kv.Key, kv.Value);
        var sourceVideo = media.Video!;
        if (options.Filters.ToneMap || options.Filters.ColorSpace == "bt709")
        {
            Add("-color_primaries", "bt709"); Add("-color_trc", "bt709"); Add("-colorspace", "bt709");
        }
        else
        {
            if (sourceVideo.ColorPrimaries is { } cp && cp != "unknown") Add("-color_primaries", cp);
            if (sourceVideo.ColorTransfer is { } ct && ct != "unknown") Add("-color_trc", ct);
            if (sourceVideo.ColorSpace is { } cs && cs != "unknown") Add("-colorspace", cs);
        }
        if (sourceVideo.ColorRange is { } range && range != "unknown") Add("-color_range", options.Filters.ToneMap ? "tv" : range);
        int audioOut = 0;
        foreach (var a in options.Audio.Where(a => a.Action != TrackAction.Entfernen))
        {
            Add("-map", cutting?$"[cutAudio{audioOut}]":$"0:{a.InputIndex}");
            Add($"-c:a:{audioOut}", a.Action == TrackAction.Kopieren ? "copy" : a.Encoder);
            if (a.Action == TrackAction.Encodieren)
            {
                if (a.Encoder != "flac") Add($"-b:a:{audioOut}", $"{a.BitRateKbps}k");
                var compression = a.CompressionLevel ?? AudioEncoding.DefaultCompression(a.Encoder);
                if (compression.HasValue) Add($"-compression_level:a:{audioOut}", compression.Value);
                if (a.Encoder == "libopus")
                {
                    Add($"-application:a:{audioOut}", a.Application);
                    Add($"-vbr:a:{audioOut}", a.BitrateMode switch { "cbr" => "off", "constrained" => "constrained", _ => "on" });
                }
                if (a.Encoder == "aac") Add($"-aac_coder:a:{audioOut}", "twoloop");
                if (a.Channels > 0) Add($"-ac:a:{audioOut}", a.Channels);
                if (a.SampleRate > 0) Add($"-ar:a:{audioOut}", a.SampleRate);
                if (a.ChannelLayout.Length > 0) Add($"-channel_layout:a:{audioOut}", a.ChannelLayout);
            }
            Add($"-metadata:s:a:{audioOut}", $"language={a.Language}");
            Add($"-metadata:s:a:{audioOut}", $"title={a.Title}");
            Add($"-disposition:a:{audioOut}", a.IsDefault ? "default" : "0");
            audioOut++;
        }
        int subtitleOut = 0;
        foreach (var s in options.Subtitles.Where(s => s.Action is TrackAction.Kopieren or TrackAction.Encodieren))
        {
            Add("-map", cutting?$"{cutSubtitles.IndexOf(s)+1}:0":$"0:{s.InputIndex}"); Add($"-c:s:{subtitleOut}", cutting?CutTimeline.SubtitleCodec(s):s.Action == TrackAction.Kopieren ? "copy" : s.Encoder);
            Add($"-metadata:s:s:{subtitleOut}", $"language={s.Language}"); Add($"-metadata:s:s:{subtitleOut}", $"title={s.Title}");
            Add($"-disposition:s:{subtitleOut}", s.IsDefault && s.Forced ? "default+forced" : s.IsDefault ? "default" : s.Forced ? "forced" : "0");
            subtitleOut++;
        }
        // Matroska font attachments are required for faithful ASS rendering.
        if (options.Container == "mkv" && options.Subtitles.Any(s => s.Action == TrackAction.Kopieren && s.SourceCodec is "ass" or "ssa"))
        { Add("-map", "0:t?"); Add("-c:t", "copy"); }
        Add("-map_metadata", "0"); Add("-map_chapters", cutting || options.ClipDuration.HasValue ? "-1" : "0");
        Add("-metadata:s:v:0", "rotate=0");
        if (options.Container is "mp4" or "mov") Add("-movflags", "+faststart");
        if (options.Container == "mp4" && ScalingService.Family(v.Encoder) == "HEVC") Add("-tag:v", "hvc1");
        Add("-f", options.Container == "mkv" ? "matroska" : options.Container);
        args.Add(output);
        return args;
    }

    public List<string> Validate(MediaInfo media, EncodingOptions o, Capabilities? caps = null)
    {
        var errors = new List<string>();
        void Check(bool bad, string message) { if (bad) errors.Add(message); }
        var v = o.Video; var f = o.Filters;
        if (media.Video is null) { errors.Add(L.T("Keine unterstützte Videospur gefunden.")); return errors; }
        errors.AddRange(CutTimeline.Validate(media,o));
        if (!VideoEncoderPolicy.IsAllowed(v.Encoder)) { errors.Add(VideoEncoderPolicy.UnavailableMessage(v.Encoder)); return errors; }
        Check(o.Container is not ("mkv" or "mp4" or "webm" or "mov"), L.T("Nicht unterstützter Zielcontainer."));
        Check(o.ClipStart is {} start&&(!double.IsFinite(start)||start<0) || o.ClipDuration is {} length&&(!double.IsFinite(length)||length<=0), L.T("Ungültiger Testausschnitt."));
        Check(o.ClipStart >= media.Duration, L.T("Der Testausschnitt beginnt hinter dem Videoende."));
        Check(v.CanEditQuality && (v.Quality < 0 || !double.IsFinite(v.Quality)), L.T("Qualität muss eine nichtnegative Zahl sein."));
        if (v.CanEditQuality && double.IsFinite(v.Quality) && v.Quality >= 0)
        {
            var range = VideoQualityScale.For(v, caps?.Find(v.Encoder));
            Check(v.Quality < range.Minimum || v.Quality > range.Maximum, L.F($"Der Qualitätswert muss für {v.Encoder} zwischen {range.Minimum} und {range.Maximum} liegen."));
            Check(range.Step == 1 && v.Quality % 1 != 0, L.T("Dieser Qualitätsmodus benötigt einen ganzzahligen Wert."));
        }
        Check(v.RateControl is RateControl.Bitrate or RateControl.CBR && v.BitRateKbps <= 0, L.T("Die Videobitrate muss positiv sein."));
        Check(v.Gop < 0, L.T("GOP muss mindestens 0 sein (0 = Encoderstandard)."));
        errors.AddRange(SvtAv1Settings.Validate(v));
        errors.AddRange(SoftwareThreading.Validate(v, caps));
        Check(f.FrameRate < 0 || !double.IsFinite(f.FrameRate), L.T("Ungültige Bildrate."));
        Check(f.Rotation is not (0 or 90 or 180 or 270), L.T("Rotation: 0, 90, 180 oder 270 Grad wählen."));
        Check(f.CropLeft < 0 || f.CropRight < 0 || f.CropTop < 0 || f.CropBottom < 0, L.T("Crop-Werte dürfen nicht negativ sein."));
        Dimensions dimensions = default;
        (bool Resize,bool ResetSar) scaling=default;
        try { dimensions = ScalingService.Calculate(media.Video, f); scaling=ScalingService.RequiredOperations(media.Video,f,dimensions); } catch (Exception e) when (e is ArgumentException or OverflowException) { errors.Add(e.Message); }
        Check(f.PadEnabled && (f.PadWidth < dimensions.Width || f.PadHeight < dimensions.Height || f.PadWidth % 2 != 0 || f.PadHeight % 2 != 0), L.T("Padding benötigt gerade Abmessungen, die mindestens so groß wie das Zielbild sind."));
        Check(v.CudaScale && (!v.CudaDecode || !v.IsNvenc), L.T("CUDA-Skalierung benötigt CUDA-Decoding und einen NVENC-Encoder."));
        Check(v.CudaScale && (f.CropEnabled || f.PadEnabled || f.Deinterlace || f.Rotation != 0 || f.FlipHorizontal || f.FlipVertical || f.ToneMap || f.ColorSpace.Length > 0 || f.CustomChain.Length > 0 || o.Subtitles.Any(s => s.Action == TrackAction.Einbrennen)),
            L.T("CPU-Filter oder Untertitel-Burn-in sind mit diesem CUDA-Skalierungspfad nicht kombinierbar. CUDA-Skalierung ausschalten."));
        Check(v.CudaScale && f.Algorithm is not ("nearest" or "bilinear" or "bicubic" or "lanczos"), L.T("CUDA unterstützt hier nearest, bilinear, bicubic und lanczos."));
        Check(!v.CudaScale && f.Algorithm is not ("neighbor" or "nearest" or "bilinear" or "bicubic" or "lanczos" or "spline" or "area"), L.T("Unbekannter Skalierungsalgorithmus."));
        Check(f.ToneMap && !media.Video.IsHdr, L.T("Tonemapping ist nur für als HDR erkanntes Material freigegeben."));
        Check(media.Video.IsHdr && !f.ToneMap && (string.IsNullOrEmpty(v.PixelFormat) || !(v.PixelFormat.Contains("10") || v.PixelFormat.Contains("12") || v.PixelFormat.Contains("16"))),
            L.T("HDR benötigt eine ausdrücklich gewählte 10-/12-/16-Bit-Ausgabe oder aktiviertes Tonemapping."));
        Check(media.Video.IsHdr && f.ColorSpace.Length > 0 && !f.ToneMap, L.T("HDR nicht ohne Tonemapping nach SDR konvertieren."));
        Check(f.ColorSpace.Length > 0 && f.ColorSpace != "bt709", L.T("Die angebotene Farbraumkonvertierung unterstützt BT.709."));
        Check(f.ToneMapAlgorithm is not ("hable" or "mobius" or "reinhard" or "clip" or "linear" or "gamma"), L.T("Ungültiger Tonemapping-Algorithmus."));
        Check(o.Subtitles.Count(s => s.Action == TrackAction.Einbrennen) > 1, L.T("Nur eine Untertitelspur gleichzeitig einbrennen."));
        Check(v.CudaScale && Math.Abs(media.Video.Rotation % 360) > 0, L.T("Bei gedrehten Quellen den CPU-Skalierungspfad verwenden."));
        if (o.Container == "webm") Check(ScalingService.Family(v.Encoder) != "AV1", L.T("WebM benötigt in diesem Programm AV1."));
        foreach (var a in o.Audio.Where(a => a.Action != TrackAction.Entfernen))
        {
            Check(!media.Streams.Any(s => s.Index == a.InputIndex && s.Type == "audio"), L.F($"Audiospur #{a.InputIndex} existiert nicht."));
            Check(a.Action == TrackAction.Einbrennen, L.T("Audio kann nicht eingebrannt werden."));
            var codec = a.Action == TrackAction.Kopieren ? a.SourceCodec : a.Encoder;
            Check(o.Container == "webm" && codec is not ("opus" or "libopus" or "vorbis" or "libvorbis"), L.T("WebM unterstützt nur Opus/Vorbis-Audio."));
            Check(o.Container is "mp4" or "mov" && codec is not ("aac" or "ac3" or "eac3" or "mp3" or "libmp3lame" or "alac" or "opus" or "libopus"), L.F($"Audio {codec} ist für MP4/MOV hier nicht freigegeben; MKV wählen oder umcodieren."));
            if (a.Action == TrackAction.Encodieren)
            {
                Check(caps is not null && caps.Find(a.Encoder) is not { MediaType: "audio" }, L.F($"Audioencoder {a.Encoder} fehlt."));
                Check(a.Encoder != "flac" && a.BitRateKbps <= 0, L.T("Audiobitrate muss positiv sein."));
                Check(a.Encoder == "libmp3lame" && (a.Channels > 0 ? a.Channels : a.SourceChannels) > 2, L.T("MP3 unterstützt maximal zwei Kanäle. Stereo ausdrücklich auswählen."));
                Check(a.Encoder is "ac3" or "eac3" && !AudioEncoding.Bitrates(a.Encoder, a.Channels > 0 ? a.Channels : a.SourceChannels).Contains(a.BitRateKbps) && a.Encoder == "ac3", L.T("Für AC-3 einen unterstützten diskreten Bitratenwert wählen."));
                Check(a.CompressionLevel is < 0 || a.Encoder == "libopus" && a.CompressionLevel > 10 || a.Encoder == "libmp3lame" && a.CompressionLevel > 9 || a.Encoder == "flac" && a.CompressionLevel > 12, L.T("Ungültige Audio-Kompressionsstufe."));
            }
        }
        foreach (var s in o.Subtitles.Where(s => s.Action != TrackAction.Entfernen))
        {
            Check(!media.Streams.Any(x => x.Index == s.InputIndex && x.Type == "subtitle"), L.F($"Untertitelspur #{s.InputIndex} existiert nicht."));
            if (s.Action == TrackAction.Einbrennen) continue;
            var codec = s.Action == TrackAction.Kopieren ? s.SourceCodec : s.Encoder;
            Check(s.IsBitmap && s.Action == TrackAction.Encodieren, L.T("Bilduntertitel benötigen OCR für Textausgabe. Kopieren, entfernen oder einbrennen wählen."));
            Check(o.Container is "mp4" or "mov" && codec != "mov_text", L.T("MP4/MOV benötigt mov_text-Untertitel; Textspur konvertieren oder entfernen."));
            Check(o.Container == "webm" && codec != "webvtt", L.T("WebM benötigt WebVTT-Untertitel."));
            Check(o.Container == "mkv" && codec == "mov_text", L.T("mov_text für MKV nach SRT oder ASS konvertieren."));
            Check(s.Action == TrackAction.Encodieren && caps is not null && caps.Find(s.Encoder) is not { MediaType: "subtitle" }, L.F($"Untertitelencoder {s.Encoder} fehlt."));
            if(CutTimeline.Active(o)&&s.Action==TrackAction.Kopieren&&caps is not null)
                Check(caps.Find(CutTimeline.SubtitleCodec(s)) is not {MediaType:"subtitle"},L.F($"Untertitelencoder {CutTimeline.SubtitleCodec(s)} fehlt."));
        }
        if (caps is not null)
        {
            var encoder = caps.Find(v.Encoder);
            Check(encoder is null, L.F($"Encoder {v.Encoder} ist in diesem FFmpeg-Build nicht vorhanden."));
            if (encoder is not null)
            {
                Check(!string.IsNullOrEmpty(v.PixelFormat) && encoder.PixelFormats is { Count: > 0 } && !encoder.PixelFormats.Contains(v.PixelFormat), L.F($"{v.Encoder} unterstützt das Pixelformat {v.PixelFormat} nicht."));
                foreach (var key in v.Extra.Keys) Check(encoder.Options?.Any(x => x.Name == key) != true, L.F($"Expertenoption '{key}' wird von {v.Encoder} nicht angeboten."));
                foreach (var entry in new[] { ("preset", v.Preset), ("tune", v.Tune), ("profile", v.Profile) })
                {
                    var key = entry.Item1 == "preset" ? v.Encoder == "libaom-av1" ? "cpu-used" : v.Encoder == "librav1e" ? "speed" : "preset" : entry.Item1;
                    Check(!string.IsNullOrEmpty(entry.Item2) && encoder.Options?.Any(x => x.Name == key) != true && key != "profile", L.F($"{key} wird von {v.Encoder} nicht unterstützt; Feld leeren."));
                }
                string qualityOption = VideoQualityScale.Parameter(v);
                Check(v.RateControl is RateControl.Qualität or RateControl.QP && encoder.Options?.Any(x => x.Name == qualityOption) != true, L.F($"Qualitätssteuerung {qualityOption} wird von {v.Encoder} nicht angeboten. Bitrate wählen."));
            }
            foreach (var required in FilterService.Required(o,scaling.Resize,scaling.ResetSar))
                Check(!caps.Filters.Contains(required), L.F($"FFmpeg-Filter '{required}' fehlt."));
        }
        foreach (var key in v.Extra.Keys)
            Check(!Regex.IsMatch(key, @"^[a-zA-Z][a-zA-Z0-9_-]*$") || key is "i" or "y" or "n" or "f" or "map" or "progress", L.F($"Unzulässige Encoderoption: {key}"));
        return errors;
    }
    public static string Display(string executable, IEnumerable<string> args) => string.Join(" ", new[] { executable }.Concat(args).Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));
}

public static class FilterService
{
    public static string EscapePath(string path) => "'" + path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\\\\\''") + "'";
    public static string HdrToSdr(string algorithm = "hable") => $"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap={algorithm}:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";
    public static string Build(MediaInfo media, EncodingOptions o, bool includeCustom = true,bool includeSubtitles=true)
    {
        var f = o.Filters; var result = new List<string>();
        var burn = o.Subtitles.FirstOrDefault(s => s.Action == TrackAction.Einbrennen && !s.IsBitmap);
        if (includeSubtitles && burn is not null) result.Add($"subtitles=filename={EscapePath(media.Path)}:si={burn.SubtitleOrdinal}");
        if (f.Deinterlace) result.Add("bwdif=mode=send_frame:parity=auto:deint=interlaced");
        if (f.CropEnabled) result.Add($"crop=iw-{f.CropLeft + f.CropRight}:ih-{f.CropTop + f.CropBottom}:{f.CropLeft}:{f.CropTop}");
        if (f.Rotation == 90) result.Add("transpose=clock");
        if (f.Rotation == 270) result.Add("transpose=cclock");
        if (f.Rotation == 180) { result.Add("hflip"); result.Add("vflip"); }
        if (f.FlipHorizontal) result.Add("hflip");
        if (f.FlipVertical) result.Add("vflip");
        if (f.ToneMap) result.Add(HdrToSdr(f.ToneMapAlgorithm));
        else if (f.ColorSpace == "bt709") result.Add("colorspace=all=bt709");
        var d = ScalingService.Calculate(media.Video!, f);
        var scaling=ScalingService.RequiredOperations(media.Video!,f,d);
        if(scaling.Resize)
            result.Add(o.Video.CudaScale
                ? $"scale_cuda=w={d.Width}:h={d.Height}:interp_algo={(f.Algorithm == "neighbor" ? "nearest" : f.Algorithm)}"
                : $"scale={d.Width}:{d.Height}:flags={(f.Algorithm == "nearest" ? "neighbor" : f.Algorithm)}");
        if(scaling.ResetSar) result.Add("setsar=1");
        if (f.PadEnabled) result.Add($"pad={f.PadWidth}:{f.PadHeight}:(ow-iw)/2:(oh-ih)/2:black");
        if (f.FrameRate > 0) result.Add($"fps={FFmpegCommandBuilder.Number(f.FrameRate)}");
        if (includeCustom && f.CustomChain.Length > 0) result.Add(f.CustomChain);
        return string.Join(",", result);
    }
    public static IEnumerable<string> Required(EncodingOptions o,bool resize,bool resetSar)
    {
        if(CutTimeline.Active(o))
        {
            yield return "select";yield return "setpts";
            if(o.Audio.Any(a=>a.Action!=TrackAction.Entfernen))
            {yield return "atrim";yield return "asetpts";yield return "aresample";yield return "apad";yield return "concat";if(CutTimeline.Keep(o).Count>1)yield return "asplit";}
        }
        if(resize) yield return o.Video.CudaScale ? "scale_cuda" : "scale";
        if(resetSar) yield return "setsar";
        if (o.Filters.ToneMap) { yield return "zscale"; yield return "tonemap"; }
        if (o.Filters.Deinterlace) yield return "bwdif";
        if (o.Filters.CropEnabled) yield return "crop";
        if (o.Filters.PadEnabled) yield return "pad";
        if (o.Filters.ColorSpace.Length > 0) yield return "colorspace";
        if (o.Subtitles.Any(s => s.Action == TrackAction.Einbrennen)) yield return o.Subtitles.Any(s => s.Action == TrackAction.Einbrennen && s.IsBitmap) ? "overlay" : "subtitles";
    }
}
