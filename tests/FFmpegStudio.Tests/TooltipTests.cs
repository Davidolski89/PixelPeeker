using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class TooltipTests
{
    [Fact] public void Every_action_has_a_complete_explanation_in_each_language_and_dialog_labels_resolve()
    {
        Assert.True(ButtonHelpCatalog.All.Count>=60);
        foreach(var (key,help) in ButtonHelpCatalog.All)
        {
            foreach(var language in L.SupportedLanguages)
            {
                var text=ButtonHelpCatalog.Text(key,language);
                Assert.True(text.Length>=(language is "zh" or "ja"?20:40),$"Missing explanation: {key}/{language}");
                Assert.DoesNotContain("{0}",text);
                foreach(var label in help.Labels)Assert.Equal(key,ButtonHelpCatalog.KeyForLabel(L.Translate(label,language)));
            }
        }
        Assert.Equal("Zoom",ButtonHelpCatalog.KeyForLabel("200 %"));
        Assert.NotEqual(ButtonHelpCatalog.Text("EditPreset"),ButtonHelpCatalog.Text("EditJob"));
        Assert.NotEqual(ButtonHelpCatalog.Text("CancelJob"),ButtonHelpCatalog.Text("CancelQualityAnalysis"));
    }
    [Theory]
    [InlineData(0,0)][InlineData(.5,500)][InlineData(2,2000)][InlineData(30,30000)]
    [InlineData(-1,0)][InlineData(100,30000)][InlineData(double.NaN,2000)]
    public void Tooltip_delay_is_bounded_and_converted_to_milliseconds(double seconds,int expected)
    {Assert.Equal(expected,TooltipTiming.Milliseconds(seconds));}
    [Fact] public async Task Tooltip_preferences_default_to_two_seconds_and_persist_when_disabled()
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-tooltips-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var log=new LoggingService(paths);var service=new SettingsService(paths,log);
            await service.LoadAsync();Assert.True(service.Current.TooltipsEnabled);Assert.Equal(2,service.Current.TooltipDelaySeconds);
            service.Current.TooltipsEnabled=false;service.Current.TooltipDelaySeconds=.5;await service.SaveAsync();
            var restored=new SettingsService(paths,log);await restored.LoadAsync();
            Assert.False(restored.Current.TooltipsEnabled);Assert.Equal(.5,restored.Current.TooltipDelaySeconds);
            restored.Current.TooltipDelaySeconds=double.PositiveInfinity;await restored.SaveAsync();await service.LoadAsync();
            Assert.Equal(2,service.Current.TooltipDelaySeconds);
        }
        finally
        {
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
               ||!Path.GetFileName(root).StartsWith("ffstudio-tooltips-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
