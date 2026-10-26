using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFmpegStudio.Core;

/// <summary>Presentation text only. Persisted enum names and FFmpeg arguments stay unchanged.</summary>
public static class L
{
    private static readonly Dictionary<string, string[]> translations = Load();
    private static string language = "de";
    public static string Language => language;
    public static IReadOnlyList<string> SupportedLanguages { get; } = Array.AsReadOnly(new[]{"de","en","pl","fr","es","tr","zh","ru","ja"});
    public static IReadOnlyCollection<string> Sources => translations.Keys;
    public static string Normalize(string? code) => code?.Split('-', '_')[0].ToLowerInvariant() switch
    { "en" => "en", "pl" => "pl", "fr" => "fr", "es" => "es", "tr" => "tr", "zh" => "zh", "ru" => "ru", "ja" => "ja", _ => "de" };
    public static void SetLanguage(string? code)
    {
        language = Normalize(code);
        var culture = CultureInfo.GetCultureInfo(language switch { "en" => "en-US", "pl" => "pl-PL", "fr" => "fr-FR", "es" => "es-ES", "tr" => "tr-TR", "zh" => "zh-CN", "ru" => "ru-RU", "ja" => "ja-JP", _ => "de-DE" });
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Number entry continues to follow the Windows culture, independently of UI language.
    }
    public static string T(string source) => Translate(source, language);
    public static string Translate(string source, string code)
    {
        var index=Normalize(code) switch{"en"=>0,"pl"=>1,"fr"=>2,"es"=>3,"tr"=>4,"zh"=>5,"ru"=>6,"ja"=>7,_=>-1};
        return index>=0&&translations.TryGetValue(source,out var values)?values[index]:source;
    }
    public static string F(FormattableString value) => string.Format(CultureInfo.CurrentCulture, T(value.Format), value.GetArguments());
    public static string EnumText(Enum value) => T(value switch
    {
        ScaleMode.UHD8K => "8K UHD", ScaleMode.UHD4K => "4K UHD", ScaleMode.QHD1440 => "1440p QHD",
        ScaleMode.FullHD1080 => "1080p Full HD", ScaleMode.HD720 => "720p HD", ScaleMode.SD480 => "480p SD",
        CompletionAction.AnwendungSchließen => "Anwendung schließen",
        ThreadControlMode.FesteAnzahl => "Feste Threadanzahl", ThreadControlMode.CPUAnteil => "Anteil der CPU-Kerne", _ => value.ToString()
    });
    public static string ResourceKey(string source) => "Loc." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    public static string? SourceOf(string text) => translations.ContainsKey(text) ? text : translations.FirstOrDefault(p=>p.Value.Contains(text)).Key;
    public static string Retranslate(string text, string previousLanguage)
    {
        foreach (var source in Sources)
            if (Translate(source, previousLanguage) == text) return T(source);
        return text;
    }
    private static Dictionary<string,string[]> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("FFmpegStudio.Core.Localization.translations.json")
            ?? throw new InvalidOperationException("Missing embedded language catalog.");
        return JsonSerializer.Deserialize<Dictionary<string,string[]>>(stream)!;
    }
}

public sealed record UiLanguage(string Code, string Name)
{
    public override string ToString() => Name;
    public static IReadOnlyList<UiLanguage> All { get; } =
    [
        new("de", "Deutsch"), new("en", "English"), new("pl", "Polski"), new("fr", "Français"),
        new("es", "Español"), new("tr", "Türkçe"), new("zh", "简体中文"), new("ru", "Русский"), new("ja", "日本語")
    ];
}
