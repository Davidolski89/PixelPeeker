using System.Text.Json;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class ThemeTests
{
    [Fact] public void Ten_distinct_themes_have_readable_text_selections_and_action_colors()
    {
        Assert.Equal(10,ThemeCatalog.All.Count);Assert.Equal(10,ThemeCatalog.All.Select(t=>t.Id).Distinct().Count());
        Assert.Equal(10,ThemeCatalog.All.Select(t=>(t.Bg,t.Accent)).Distinct().Count());
        Assert.Contains(ThemeCatalog.All,t=>t.IsDark);Assert.Contains(ThemeCatalog.All,t=>!t.IsDark);
        foreach(var theme in ThemeCatalog.All)
        {
            foreach(var background in new[]{theme.Bg,theme.Surface,theme.Raised})
            {
                Assert.True(Contrast(theme.Text,background)>=4.5,$"{theme.Id}: text on {background}");
                Assert.True(Contrast(theme.Muted,background)>=4.5,$"{theme.Id}: secondary text on {background}");
            }
            Assert.True(Contrast(theme.AccentText,theme.Accent)>=4.5,$"{theme.Id}: action label");
            Assert.True(Contrast(theme.SelectionText,theme.Selection)>=4.5,$"{theme.Id}: selected text");
            Assert.True(Contrast(theme.Accent,theme.Surface)>=4.5,$"{theme.Id}: accent text");
            foreach(var language in new[]{"en","pl"})
            {Assert.False(string.IsNullOrWhiteSpace(L.Translate(theme.Name,language)));Assert.NotEqual(theme.Description,L.Translate(theme.Description,language));}
        }
    }
    [Theory]
    [InlineData("{\"DarkMode\":true}",ThemeCatalog.DefaultId)]
    [InlineData("{\"DarkMode\":false}",ThemeCatalog.LightId)]
    [InlineData("{\"ThemeId\":\"unknown\",\"DarkMode\":false}",ThemeCatalog.LightId)]
    [InlineData("{\"ThemeId\":\"sakura\",\"DarkMode\":false}","sakura")]
    public async Task Saved_theme_and_legacy_light_or_dark_preferences_restore_safely(string json,string expectedId)
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-theme-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);await File.WriteAllTextAsync(Path.Combine(root,"settings.json"),json);
            var service=new SettingsService(paths,new LoggingService(paths));await service.LoadAsync();
            Assert.Equal(expectedId,service.Current.ThemeId);Assert.Equal(ThemeCatalog.Resolve(expectedId).IsDark,service.Current.DarkMode);
            service.Current.ThemeId="glacier";service.Current.DarkMode=false;await service.SaveAsync();
            var restored=new SettingsService(paths,new LoggingService(paths));await restored.LoadAsync();
            Assert.Equal("glacier",restored.Current.ThemeId);Assert.False(restored.Current.DarkMode);
            Assert.Equal("glacier",JsonSerializer.Deserialize<AppSettings>(json:await File.ReadAllTextAsync(Path.Combine(root,"settings.json")),Json.Options)!.ThemeId);
        }
        finally
        {
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
               ||!Path.GetFileName(root).StartsWith("ffstudio-theme-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static double Contrast(string foreground,string background)
    {
        static double Luminance(string hex)
        {
            Assert.Matches("^#[0-9A-F]{6}$",hex);
            var components=Enumerable.Range(0,3).Select(i=>Convert.ToInt32(hex.Substring(1+i*2,2),16)/255d)
                .Select(c=>c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4)).ToArray();
            return components[0]*.2126+components[1]*.7152+components[2]*.0722;
        }
        var a=Luminance(foreground);var b=Luminance(background);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
}
