using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public static class SvtAv1Settings
{
    // Respect FFmpeg dictionary quoting/escaping; unknown parameters must survive edits unchanged.
    private static IEnumerable<string> Tokens(string text)
    {
        int start = 0; bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && !quoted) { i++; continue; }
            if (text[i] == '\'') quoted = !quoted;
            if (text[i] == ':' && !quoted) { yield return text[start..i]; start = i + 1; }
        }
        if (start < text.Length) yield return text[start..];
        else if (text.Length > 0) yield return "";
    }
    private static string Key(string token) => token.Split('=', 2)[0].Trim();
    public static string Get(string text, string key)
    {
        var token = Tokens(text).LastOrDefault(t => Key(t) == key);
        return token is not null && token.Contains('=') ? token[(token.IndexOf('=') + 1)..].Trim().Trim('\'') : "";
    }
    public static string Set(string text, string key, string? value)
    {
        var tokens = Tokens(text).Where(t => Key(t) != key).ToList();
        if (!string.IsNullOrWhiteSpace(value)) tokens.Add(key + "=" + value.Trim());
        return string.Join(':', tokens);
    }
    public static IEnumerable<string> Validate(VideoOptions video)
    {
        var text = video.SvtAv1Parameters;
        if (string.IsNullOrWhiteSpace(text)) yield break;
        if (!video.IsSvtAv1) { yield return L.T("SVT-AV1-Parameter benötigen den Encoder libsvtav1."); yield break; }
        var seen = new HashSet<string>();
        foreach (var token in Tokens(text))
        {
            var key = Key(token);
            if (!Regex.IsMatch(key, @"^[a-zA-Z][a-zA-Z0-9_-]*$") || !token.Contains('=') || string.IsNullOrWhiteSpace(token[(token.IndexOf('=') + 1)..]))
            { yield return L.T("SVT-AV1-Parameter als name=wert:name=wert eingeben."); continue; }
            if (!seen.Add(key)) yield return L.F($"SVT-AV1-Parameter '{key}' ist doppelt vorhanden.");
            var max = key switch { "tune" => 5, "film-grain" => 50, "enable-dlf" => 2, "lp" => 6, _ => -1 };
            if (max >= 0 && (!int.TryParse(Get(token, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > max))
                yield return L.F($"SVT-AV1: {key} benötigt eine ganze Zahl zwischen 0 und {max}.");
            if (key == "keyint" && video.Gop > 0)
                yield return L.T("GOP und SVT-AV1-keyint nicht gleichzeitig setzen; einen der beiden Werte leeren.");
        }
    }
}
