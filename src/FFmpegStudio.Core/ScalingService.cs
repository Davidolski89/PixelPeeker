using System.Globalization;

namespace FFmpegStudio.Core;

public readonly record struct Dimensions(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

public static class ScalingService
{
    public static Dimensions Calculate(MediaStream source, FilterOptions f)
    {
        double w = source.Width, h = source.Height;
        var sar = MediaStream.Rational(source.SampleAspectRatio) ?? 1;
        if (sar <= 0) sar = 1;
        // FFmpeg autorotation occurs before user filters; SAR is inverted for a quarter turn.
        if (Math.Abs(source.Rotation % 180) is > 45 and < 135) { (w, h) = (h, w); sar = 1 / sar; }
        if (f.CropEnabled) { w -= f.CropLeft + f.CropRight; h -= f.CropTop + f.CropBottom; }
        if (w < 2 || h < 2) throw new ArgumentException(L.T("Der Crop lässt kein gültiges Bild übrig."));
        w *= sar;
        if (Math.Abs(f.Rotation % 180) == 90) (w, h) = (h, w);
        var bounds = f.ScaleMode switch
        {
            ScaleMode.UHD8K => (7680d, 4320d), ScaleMode.UHD4K => (3840d, 2160d),
            ScaleMode.QHD1440 => (2560d, 1440d), ScaleMode.FullHD1080 => (1920d, 1080d),
            ScaleMode.HD720 => (1280d, 720d), ScaleMode.SD480 => (854d, 480d),
            ScaleMode.Benutzerdefiniert => ((double)f.Width, (double)f.Height),
            ScaleMode.Prozent => (w * f.Percent / 100, h * f.Percent / 100),
            _ => (w, h)
        };
        if (bounds.Item1 <= 0 || bounds.Item2 <= 0) throw new ArgumentException(L.T("Die Zielauflösung muss positiv sein."));
        var factor = Math.Min(bounds.Item1 / w, bounds.Item2 / h);
        if (!f.AllowUpscale) factor = Math.Min(factor, 1);
        return new(Even(w * factor), Even(h * factor));
    }
    public static (bool Resize, bool ResetSar) RequiredOperations(MediaStream source, FilterOptions f, Dimensions target)
    {
        // Compare with the frame entering scale, after FFmpeg autorotation and the preceding geometry filters.
        long width=source.Width, height=source.Height;
        if(Math.Abs(source.Rotation % 180) is >45 and <135) (width,height)=(height,width);
        if(f.CropEnabled) { width-=(long)f.CropLeft+f.CropRight; height-=(long)f.CropTop+f.CropBottom; }
        if(Math.Abs(f.Rotation % 180)==90) (width,height)=(height,width);
        // Keep the established normalization for uncommon, non-right-angle display matrices.
        var resize=target.Width!=width||target.Height!=height||source.Rotation%90!=0;
        var sar=MediaStream.Rational(source.SampleAspectRatio);
        return (resize,resize||sar is >0 and not 1);
    }
    private static int Even(double n) => Math.Max(2, checked((int)Math.Floor((n + 0.001) / 2)) * 2);
    public static string Family(string? encoder)
    {
        if(string.IsNullOrEmpty(encoder)) return "";
        if (encoder.Contains("264", StringComparison.Ordinal)) return "H.264";
        if (encoder.Contains("265", StringComparison.Ordinal) || encoder.StartsWith("hevc", StringComparison.Ordinal)) return "HEVC";
        if (encoder.Contains("266", StringComparison.Ordinal) || encoder.Contains("vvenc", StringComparison.Ordinal) || encoder.StartsWith("vvc", StringComparison.Ordinal)) return "VVC";
        if (encoder.Contains("av1", StringComparison.Ordinal) || encoder.Contains("aom", StringComparison.Ordinal) || encoder.Contains("rav1e", StringComparison.Ordinal)) return "AV1";
        return "";
    }
    public static string Describe(MediaInfo source, EncodingOptions o)
    {
        if (source.Video is not { } video) return L.T("Kein Video");
        try
        {
            var d = Calculate(video, o.Filters);
            if (o.Filters.PadEnabled) d = new(o.Filters.PadWidth, o.Filters.PadHeight);
            return $"{video.Width} × {video.Height} {video.Codec.ToUpperInvariant()} → {d} {Family(o.Video.Encoder)} · {o.Video.Encoder} · {o.Container.ToUpperInvariant()}";
        }
        catch (ArgumentException e) { return e.Message; }
        catch (OverflowException) { return L.T("Die Zielauflösung ist zu groß. Kleinere Werte wählen."); }
    }
}

public static class AudioEncoding
{
    public static int? DefaultCompression(string encoder) => encoder switch { "libopus" => 10, "libmp3lame" => 0, "flac" => 12, _ => null };
    public static int[] Bitrates(string encoder, int channels)
    {
        bool surround = channels > 2;
        return encoder switch
        {
            "flac" => [],
            "libopus" => surround ? [192, 256, 320, 384, 448, 512] : [64, 96, 128, 160, 192, 256, 320],
            "aac" => surround ? [256, 320, 384, 448, 512, 640] : [96, 128, 160, 192, 256, 320],
            "libmp3lame" => [96, 128, 160, 192, 224, 256, 320],
            "ac3" => surround ? [384, 448, 512, 576, 640] : [128, 192, 224, 256, 320, 384],
            "eac3" => surround ? [384, 448, 512, 640, 768, 1024] : [128, 192, 256, 320, 384, 448],
            _ => surround ? [256, 384, 512] : [96, 128, 192, 256]
        };
    }
    public static string QualityDescription(string encoder, int? value) => encoder switch
    {
        "libopus" => L.F($"Komplexität {value ?? 10} (10 = höchste), unabhängig von Bitrate"),
        "libmp3lame" => L.F($"Algorithmusqualität {value ?? 0} (0 = höchste)"),
        "flac" => L.F($"Kompressionsstufe {value ?? 12}; immer verlustfrei"),
        "aac" => L.T("Stabiler nativer AAC-Twoloop-Coder"),
        _ => L.T("Stabile Encoder-Standards; kein erfundenes Qualitäts-Preset")
    };
}

public static class SizeEstimator
{
    public static long? Estimate(MediaInfo media, EncodingOptions options)
    {
        double? duration = CutTimeline.Duration(media,options);
        if (duration is null || options.Video.RateControl is RateControl.Qualität or RateControl.QP) return null;
        long bps = options.Video.BitRateKbps * 1000L;
        foreach (var a in options.Audio.Where(a => a.Action != TrackAction.Entfernen))
        {
            long? rate = a.Action == TrackAction.Kopieren ? media.Streams.FirstOrDefault(s => s.Index == a.InputIndex)?.BitRate
                : a.Encoder == "flac" ? null : a.BitRateKbps * 1000L;
            if (rate is null) return null;
            bps += rate.Value;
        }
        return (long)(duration.Value * bps / 8 * 1.02);
    }
    public static long? Extrapolate(long testBytes, double testSeconds, double? fullSeconds) =>
        testSeconds > 0 && fullSeconds > 0 ? (long)(testBytes * fullSeconds.Value / testSeconds) : null;
}
