using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

/// <summary>Encoder-aware UI labels; raw preset tokens and enum values remain unchanged.</summary>
public static class VideoPresentation
{
    public static string EncoderLabel(EncoderInfo encoder)
    {
        var implementation=encoder.Name switch
        {
            "libx264"=>"x264", "libx264rgb"=>"x264 RGB", "libopenh264"=>"OpenH264", "libx265"=>"x265",
            "libvvenc"=>"VVenC", "libaom-av1"=>"AOM", "librav1e"=>"rav1e", "libsvtav1"=>"SVT-AV1",
            _ when encoder.Name.EndsWith("_nvenc",StringComparison.Ordinal)=>"NVIDIA NVENC",
            _ when encoder.Name.EndsWith("_qsv",StringComparison.Ordinal)=>"Intel QSV",
            _ when encoder.Name.EndsWith("_amf",StringComparison.Ordinal)=>"AMD AMF",
            _=>encoder.Name
        };
        return $"{encoder.Family} · {implementation} · {L.T(encoder.Hardware?"Hardware":"Software")}";
    }
    public static string PresetDefault(EncoderInfo? encoder)
    {
        var option=encoder?.Options?.FirstOrDefault(o=>o.Name==(encoder.Name=="libaom-av1"?"cpu-used":encoder.Name=="librav1e"?"speed":"preset"));
        var match=Regex.Match(option?.Description??"", "\\(default (?:\"([^\"]+)\"|([^\\)]+))\\)");
        if(match.Success)return (match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value).Trim();
        // x265's help omits the library's default preset; x264 also uses medium.
        return encoder?.Name is "libx264" or "libx264rgb" or "libx265"?"medium":"";
    }
    public static IReadOnlyList<string> PresetValues(string? encoder,IEnumerable<string> choices,string? current,string? defaultPreset=null)
    {
        var values=choices.Where(s=>s.Length>0).Distinct().ToList();
        if(!string.IsNullOrEmpty(defaultPreset)&&!values.Contains(defaultPreset))values.Add(defaultPreset);
        string[] order=Nvenc(encoder)?["fast","p1","p2","p3","medium","p4","p5","p6","p7","slow"]:
            ["ultrafast","superfast","veryfast","faster","fast","medium","slow","slower","veryslow","placebo","speed","balanced","quality","high_quality"];
        if(values.All(s=>int.TryParse(s,out _)))values=values.OrderBy(s=>int.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToList();
        else values=values.OrderBy(s=>Array.IndexOf(order,s) is var i&&i>=0?i:int.MaxValue).ToList();
        // Imported/older presets must remain selected until the user chooses a replacement.
        if(!string.IsNullOrEmpty(current)&&!values.Contains(current))values.Add(current);
        return values;
    }
    public static bool UsesQpForQuality(string? encoder) => encoder is "libvvenc" or "librav1e";
    private static bool Nvenc(string? encoder) => encoder?.EndsWith("_nvenc",StringComparison.Ordinal)==true;
    public static string RateControlLabel(RateControl mode,string? encoder) => mode switch
    {
        RateControl.Qualität when UsesQpForQuality(encoder) => L.T("Quantisierung (QP; wie QP-Modus)"),
        RateControl.Qualität when Nvenc(encoder) => L.T("Qualitätsziel (CQ)"),
        RateControl.Qualität => L.T("Qualitätsziel (CRF)"),
        RateControl.QP => L.T("Fester Quantisierer (CQP/QP)"),
        RateControl.Bitrate => L.T("Zielbitrate (Durchschnitt)"),
        RateControl.CBR => L.T("Konstante Bitrate (CBR)"),
        _ => L.EnumText(mode)
    };
    public static string QualityLabel(VideoOptions video) => video.RateControl==RateControl.QP||UsesQpForQuality(video.Encoder)
        ? L.T("Quantisierer (QP)") : Nvenc(video.Encoder)?L.T("Qualitätsziel (CQ)"):L.T("Qualitätsziel (CRF)");
    public static string QualityHint(VideoOptions video) => video.IsNvenc && video.RateControl != RateControl.QP
        ? L.T("Kleinere positive Werte: höhere Qualität. Größere Werte: stärkere Kompression. CQ 0 = automatisch.")
        : video.Encoder == "libsvtav1"
        ? L.T("Kleinere positive Werte: höhere Qualität. Größere Werte: stärkere Kompression. 0 = Encoder-Standard.")
        : L.T("Kleinere Werte: höhere Qualität. Größere Werte: stärkere Kompression.");
    public static string ModeDescription(VideoOptions video) => video.RateControl switch
    {
        RateControl.Qualität or RateControl.QP when UsesQpForQuality(video.Encoder) => L.T("Dieser Encoder verwendet in beiden Modi denselben QP-Parameter. Die Bitrate darf schwanken."),
        RateControl.Qualität => L.T("Der Encoder passt die Quantisierung an das Qualitätsziel an. Bitrate und Dateigröße dürfen schwanken."),
        RateControl.QP => L.T("Fester Basiswert für die Quantisierung. Sichtbare Qualität und Bitrate können je nach Szene schwanken."),
        RateControl.Bitrate => L.T("Durchschnittliche Zielbitrate. Der Encoder verteilt die Daten auf einfache und aufwendige Szenen."),
        RateControl.CBR => L.T("Möglichst konstante Datenrate. Bei NVIDIA wird CBR aktiviert; Softwareencoder setzen dies unterschiedlich um."),
        _ => ""
    };
    public static string PresetLabel(string? value,string? encoder,string? defaultPreset=null)
    {
        if(string.IsNullOrEmpty(value))return "";
        string? detail=Nvenc(encoder)?value switch
        {
            "slow" => "P7 + 2 Durchläufe (volle Auflösung)",
            "medium" => "P4 + 1 Durchlauf",
            "fast" => "P1 + 1 Durchlauf",
            "p1" => "am schnellsten · geringste Effizienz",
            "p2" => "sehr schnell · geringe Effizienz",
            "p3" => "schnell · einfache Kompression",
            "p4" => "ausgewogen",
            "p5" => "langsam · gute Effizienz",
            "p6" => "langsamer · höhere Effizienz",
            "p7" => "am langsamsten · höchste Effizienz",
            _ => null
        }:value switch
        {
            "ultrafast" => "am schnellsten · geringste Effizienz",
            "superfast" => "sehr schnell · geringe Effizienz",
            "veryfast" => "sehr schnell",
            "faster" => "schneller",
            "fast" => "schnell · geringere Kompression",
            "medium" => "ausgewogen",
            "slow" => "langsam · bessere Kompression",
            "slower" => "langsamer · höhere Effizienz",
            "veryslow" => "sehr langsam · hohe Effizienz",
            _ => null
        };
        var label=detail is null?value:$"{value} — {L.T(detail)}";
        return value==defaultPreset?$"{label} ({L.T("Standard")})":label;
    }
    public static string PresetValue(string text)
    {
        var suffix=$" ({L.T("Standard")})";
        if(text.EndsWith(suffix,StringComparison.Ordinal))text=text[..^suffix.Length];
        var separator=text.IndexOf(" — ",StringComparison.Ordinal);
        return (separator<0?text:text[..separator]).Trim();
    }
    public static string PresetDescription(string? encoder) => Nvenc(encoder)
        ? L.T("P1–P7 wählen das Grundpreset. Multipass wird separat eingestellt; slow erzwingt P7 mit Multipass fullres, medium und fast einen Durchlauf.")
        : encoder == "libsvtav1"
        ? L.T("SVT-AV1: Kleinere Presetnummern bedeuten langsamere, effizientere Kompression; größere Nummern höhere Geschwindigkeit. Die verfügbaren Werte stammen aus der FFmpeg-Encoderhilfe.")
        : L.T("Langsamere Presets verwenden mehr Rechenzeit für effizientere Kompression. Die Voreinstellung des Encoders ist mit (Standard) markiert.");
}
