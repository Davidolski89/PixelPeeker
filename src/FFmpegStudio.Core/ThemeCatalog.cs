namespace FFmpegStudio.Core;

public sealed record AppTheme(string Id, string Name, string Description, bool IsDark,
    string Bg, string Surface, string Raised, string Border, string Text, string Muted,
    string Accent, string AccentText, string Warning, string Selection, string SelectionText);

public static class ThemeCatalog
{
    public const string DefaultId = "studio-mint";
    public const string LightId = "daylight";
    public static IReadOnlyList<AppTheme> All { get; } = Array.AsReadOnly<AppTheme>(
    [
        new(DefaultId, "Studio Mint", "Dunkles Schieferblau mit Mint", true,
            "#10151F", "#171E2B", "#202A3A", "#344155", "#F0F4FA", "#A5B4C9", "#70DBC1", "#0E2420", "#FFCB7B", "#2C4353", "#F0F4FA"),
        new("midnight", "Mitternacht", "Tiefes Nachtblau mit elektrischem Blau", true,
            "#090E1B", "#10192C", "#19243B", "#304362", "#EDF3FF", "#A5B8D6", "#84B7FF", "#101C32", "#FFD18A", "#273C61", "#EDF3FF"),
        new("northern-lights", "Nordlicht", "Kühles Petrol mit leuchtendem Cyan", true,
            "#09191D", "#10262C", "#19343C", "#315460", "#EDFBFC", "#A4C6CD", "#66E2EA", "#09282D", "#FFD28A", "#254953", "#EDFBFC"),
        new("amethyst", "Amethyst", "Dunkles Violett mit Lavendel", true,
            "#181222", "#241C32", "#30263F", "#514163", "#F6F0FC", "#C2B1D4", "#C6A4FF", "#261438", "#FFD18C", "#49365F", "#F6F0FC"),
        new("ember", "Glut", "Warmes Anthrazit mit Kupferorange", true,
            "#1B1310", "#2A1E19", "#382A23", "#5E473A", "#FFF3E9", "#D0B4A2", "#FFAD76", "#351B0D", "#FFDA86", "#523A2D", "#FFF3E9"),
        new("forest", "Wald", "Tiefes Waldgrün mit frischem Salbei", true,
            "#101A13", "#19291D", "#24382A", "#405D47", "#EFF8ED", "#AFC8B1", "#A6DF91", "#192C14", "#EED488", "#36513B", "#EFF8ED"),
        new("sakura", "Sakura", "Dunkle Pflaume mit Kirschblütenrosa", true,
            "#21131D", "#301E2B", "#402938", "#65465A", "#FFF0F7", "#D4B2C6", "#FFA6CF", "#3B1730", "#FFD38C", "#573A4C", "#FFF0F7"),
        new(LightId, "Tageslicht", "Helles Neutralgrau mit Türkis", false,
            "#EEF2F7", "#FFFFFF", "#E7EDF4", "#BCC9D7", "#172538", "#465A72", "#107566", "#FFFFFF", "#895000", "#CDE5DF", "#172538"),
        new("sandstone", "Sandstein", "Warme Cremetöne mit Terrakotta", false,
            "#F3EDE2", "#FFFCF5", "#EDE2D1", "#C7B59D", "#382B22", "#695442", "#9D4727", "#FFFFFF", "#805000", "#E6CFB4", "#382B22"),
        new("glacier", "Gletscher", "Klares Eisweiß mit Ozeanblau", false,
            "#ECF4FA", "#FAFDFF", "#E0EDF6", "#B0C7D9", "#17334A", "#405E76", "#126999", "#FFFFFF", "#885500", "#C5DEEF", "#17334A")
    ]);
    public static AppTheme Resolve(string? id, bool legacyDarkMode = true) =>
        All.FirstOrDefault(theme => theme.Id == id) ?? All.First(theme => theme.Id == (legacyDarkMode ? DefaultId : LightId));
}
