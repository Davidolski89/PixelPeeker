using FFmpegStudio.Core;
using Xunit;

namespace FFmpegStudio.Tests;

[Collection("Localization")]
public sealed class VideoPresentationTests
{
    [Fact]
    public void Compact_encoder_labels_are_unique_and_identify_the_implementation_and_type()
    {
        var previous=L.Language;
        var encoders=new[]{"libx264","libx264rgb","libopenh264","libx265","libvvenc","libaom-av1","librav1e","libsvtav1",
            "h264_nvenc","hevc_nvenc","av1_nvenc","h264_qsv","hevc_qsv","av1_qsv","h264_amf","hevc_amf","av1_amf"};
        try
        {
            foreach(var language in L.SupportedLanguages)
            {
                L.SetLanguage(language);
                var labels=encoders.Select(name=>
                {
                    var hardware=!name.StartsWith("lib",StringComparison.Ordinal);
                    var label=VideoPresentation.EncoderLabel(new(name,"Full description","video",ScalingService.Family(name),hardware));
                    Assert.Contains(ScalingService.Family(name),label);
                    Assert.EndsWith(L.T(hardware?"Hardware":"Software"),label);
                    Assert.DoesNotContain('\n',label);Assert.DoesNotContain("Full description",label);
                    return label;
                }).ToArray();
                Assert.Equal(encoders.Length,labels.Distinct().Count());
            }
        }
        finally{L.SetLanguage(previous);}
    }

    [Theory]
    [InlineData("libsvtav1","13,6,0","-2")]
    [InlineData("hevc_nvenc","slow,medium,fast,p1,p4,p7","p7")]
    [InlineData("libx265","slow,fast,medium","custom-preset")]
    [InlineData("hevc_amf","quality,balanced,speed,high_quality","")]
    public void Preset_steps_keep_raw_supported_tokens_and_saved_values_without_a_blank_step(string encoder,string supported,string saved)
    {
        var choices=supported.Split(',');var values=VideoPresentation.PresetValues(encoder,choices,saved);
        Assert.DoesNotContain("",values);
        if(saved.Length>0)Assert.Contains(saved,values);
        Assert.Equal(choices.Append(saved).Where(s=>s.Length>0).Distinct().Order(),values.Order());
        Assert.Equal(values.Count,values.Distinct().Count());
        if(encoder=="libsvtav1")Assert.Equal(new[]{"0","6","13","-2"},values);
    }

    [Theory]
    [InlineData("libx264","preset","Set encoding preset (default \"medium\")","medium")]
    [InlineData("libx265","preset","set the x265 preset","medium")]
    [InlineData("hevc_nvenc","preset","Encoding preset (from 0 to 9) (default p4)","p4")]
    [InlineData("libsvtav1","preset","Encoding preset (from -2 to 13) (default -2)","-2")]
    [InlineData("libaom-av1","cpu-used","Quality/Speed (from 0 to 8) (default 1)","1")]
    [InlineData("librav1e","speed","speed preset (from -1 to 10) (default -1)","-1")]
    [InlineData("h264_qsv","preset","(from 0 to 7) (default 0)","0")]
    [InlineData("hevc_amf","preset","Quality preset (from -1 to 15) (default -1)","-1")]
    public void Encoder_defaults_are_named_and_selectable_without_changing_preset_tokens(string encoder,string option,string description,string expected)
    {
        var info=new EncoderInfo(encoder,"","video","",false,Options:[new(option,"int",description,[])]);
        var defaultPreset=VideoPresentation.PresetDefault(info);
        Assert.Equal(expected,defaultPreset);
        var values=VideoPresentation.PresetValues(encoder,["custom"],"",defaultPreset);
        Assert.DoesNotContain("",values);Assert.Single(values,s=>s==expected);
        var previous=L.Language;
        try
        {
            foreach(var language in L.SupportedLanguages)
            {
                L.SetLanguage(language);
                var label=VideoPresentation.PresetLabel(expected,encoder,defaultPreset);
                Assert.EndsWith($" ({L.T("Standard")})",label);
                Assert.Equal(expected,VideoPresentation.PresetValue(label));
                Assert.DoesNotContain($" ({L.T("Standard")})",VideoPresentation.PresetLabel("custom",encoder,defaultPreset));
            }
            L.SetLanguage("en");
            if(encoder=="libx264")Assert.Equal("medium — balanced (Default)",VideoPresentation.PresetLabel(expected,encoder,defaultPreset));
        }
        finally{L.SetLanguage(previous);}
    }
}
