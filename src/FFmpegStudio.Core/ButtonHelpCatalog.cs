using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public sealed record ButtonHelp(string De, string En, string Pl, string[] Labels,string Fr,string Es,string Tr,string Zh,string Ru,string Ja);

public static class ButtonHelpCatalog
{
    public static IReadOnlyDictionary<string, ButtonHelp> All { get; } = Load();
    public static string Text(string key, string? language = null) => All.TryGetValue(key, out var help)
        ? L.Normalize(language ?? L.Language) switch { "en" => help.En, "pl" => help.Pl, "fr"=>help.Fr,"es"=>help.Es,"tr"=>help.Tr,"zh"=>help.Zh,"ru"=>help.Ru,"ja"=>help.Ja, _ => help.De } : "";
    public static string? KeyForLabel(string label)
    {
        if (Regex.IsMatch(label, @"^\d+\s*%$")) return "Zoom";
        // Different source texts may have the same translation. Resolve only actual action labels.
        return All.FirstOrDefault(entry=>entry.Value.Labels.Any(source=>source==label||
            L.SupportedLanguages.Any(language=>L.Translate(source,language)==label))).Key;
    }
    private static Dictionary<string, ButtonHelp> Load()
    {
        using var stream = typeof(ButtonHelpCatalog).Assembly.GetManifestResourceStream("FFmpegStudio.Core.Localization.button-help.json")
            ?? throw new InvalidOperationException("Missing button help catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, ButtonHelp>>(stream, Json.Options)!;
    }
}

public static class TooltipTiming
{
    public const double DefaultSeconds = 2;
    public const double MaximumSeconds = 30;
    public static double Normalize(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, MaximumSeconds) : DefaultSeconds;
    public static int Milliseconds(double seconds) => (int)Math.Round(Normalize(seconds) * 1000);
}
