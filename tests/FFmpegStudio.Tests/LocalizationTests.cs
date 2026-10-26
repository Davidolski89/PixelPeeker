using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;

[Collection("Localization")]
public sealed class LocalizationTests
{
    [Fact]
    public void Annotated_presets_round_trip_and_quality_modes_match_the_encoder()
    {
        var previous=L.Language;
        try
        {
            foreach(var code in L.SupportedLanguages)
            {
                L.SetLanguage(code);
                foreach(var encoder in new[]{"hevc_nvenc","libx265","libvvenc","libsvtav1"})
                foreach(var raw in new[]{"","p1","p6","p7","slow","medium","fast","6","custom-preset"})
                    Assert.Equal(raw,VideoPresentation.PresetValue(VideoPresentation.PresetLabel(raw,encoder)));
                Assert.Contains("P7",VideoPresentation.PresetLabel("slow","hevc_nvenc"));
                Assert.DoesNotContain("P7",VideoPresentation.PresetLabel("slow","libx265"));
                Assert.Contains("CRF",VideoPresentation.RateControlLabel(RateControl.Qualität,"libx265"));
                Assert.Contains("CQ",VideoPresentation.RateControlLabel(RateControl.Qualität,"hevc_nvenc"));
                Assert.Contains("QP",VideoPresentation.RateControlLabel(RateControl.Qualität,"librav1e"));
                Assert.NotEqual(VideoPresentation.RateControlLabel(RateControl.QP,"hevc_nvenc"),VideoPresentation.RateControlLabel(RateControl.Qualität,"hevc_nvenc"));
            }
        }
        finally{L.SetLanguage(previous);}
    }

    [Fact]
    public void All_translations_preserve_format_arguments()
    {
        foreach(var source in L.Sources)
        foreach(var code in L.SupportedLanguages)
        {
            var text=L.Translate(source,code);
            Assert.False(string.IsNullOrWhiteSpace(text),source+" / "+code);
            System.Text.CompositeFormat.Parse(text);
            static string[] Arguments(string value)=>Regex.Matches(value,@"(?<!\{)\{(\d+)(?:[,}:])")
                .Select(m=>m.Groups[1].Value).OrderBy(s=>s).ToArray();
            Assert.Equal(Arguments(source),Arguments(text));
            Assert.Equal(Regex.Matches(source,@"\{[^{}]+\}").Select(m=>m.Value).Order(),Regex.Matches(text,@"\{[^{}]+\}").Select(m=>m.Value).Order());
        }
    }

    [Fact]
    public void Language_changes_preserve_commands_enum_storage_and_number_entry()
    {
        var previous=L.Language;
        var numberCulture=CultureInfo.CurrentCulture;
        var media=new MediaInfo{Path=Path.GetFullPath("źródło Ä.mkv"),Duration=10,
            Streams=[new(){Index=0,Type="video",Codec="h264",Width=1920,Height=1080},new(){Index=1,Type="audio",Codec="aac",Channels=2}]};
        var options=EncodingOptions.ForMedia(media);
        var builder=new FFmpegCommandBuilder();
        var target=Path.GetFullPath("wynik.mkv");
        var original=builder.Build(media,options,target);
        try
        {
            foreach(var code in L.SupportedLanguages)
            {
                L.SetLanguage(code);
                Assert.Equal(original,builder.Build(media,options,target));
                Assert.Equal(numberCulture,CultureInfo.CurrentCulture);
                Assert.Equal("und",options.Audio[0].Language);
                Assert.Equal("Benutzerdefiniert",options.PresetName);
                var saved=JsonSerializer.Serialize(options,Json.Options);
                Assert.Equal("Qualität",JsonDocument.Parse(saved).RootElement.GetProperty("Video").GetProperty("RateControl").GetString());
                Assert.Equal(options.Video.RateControl,Json.Clone(options).Video.RateControl);
                Assert.Equal(code switch{"en"=>"Copy","pl"=>"Kopiuj","fr"=>"Copier","es"=>"Copiar","tr"=>"Kopyala","zh"=>"复制","ru"=>"Копировать","ja"=>"コピー",_=>"Kopieren"},L.EnumText(TrackAction.Kopieren));
                options.Video.Quality=-1;
                Assert.Contains(L.T("Qualität muss eine nichtnegative Zahl sein."),builder.Validate(media,options));
                options.Video.Quality=23;
            }
        }
        finally{L.SetLanguage(previous);}
    }

    [Theory]
    [InlineData("de","de")][InlineData("en","en")][InlineData("pl","pl")]
    [InlineData("pl-PL","pl")][InlineData("fr","fr")][InlineData("fr-CA","fr")][InlineData("es-MX","es")]
    [InlineData("TR_tr","tr")][InlineData("zh-Hans","zh")][InlineData("ru-RU","ru")][InlineData("ja-JP","ja")]
    [InlineData("it","de")][InlineData(null,"de")]
    public async Task Language_setting_is_restored_and_invalid_values_fall_back(string? saved,string expected)
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-language-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);
            var service=new SettingsService(paths,new LoggingService(paths));
            service.Current.Language=saved!;
            await service.SaveAsync();
            var restored=new SettingsService(paths,new LoggingService(paths));
            await restored.LoadAsync();
            Assert.Equal(expected,restored.Current.Language);
            Assert.Equal("de",JsonSerializer.Deserialize<AppSettings>("{}",Json.Options)!.Language);
        }
        finally
        {
            if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"ffstudio-language-"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);
        }
    }

    [Fact]
    public void Every_supported_language_has_a_complete_embedded_catalog()
    {
        using var stream=typeof(L).Assembly.GetManifestResourceStream("FFmpegStudio.Core.Localization.translations.json")!;
        var catalog=JsonSerializer.Deserialize<Dictionary<string,string[]>>(stream)!;
        Assert.Equal(9,L.SupportedLanguages.Count);
        foreach(var (source,values) in catalog)
        {
            Assert.Equal(L.SupportedLanguages.Count-1,values.Length);
            foreach(var value in values)Assert.False(string.IsNullOrWhiteSpace(value),source);
        }
        foreach(var language in L.SupportedLanguages.Where(l=>l!="de"))
        foreach(var source in new[]{"Sprache","Einstellungen","Vorschau","Untertitel","Ein Bild weiter","Dateinamen"})
            Assert.NotEqual(source,L.Translate(source,language));
    }
}
