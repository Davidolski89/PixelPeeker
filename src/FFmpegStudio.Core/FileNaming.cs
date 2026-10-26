using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public sealed class FileNamingSettings
{
    public bool Enabled { get; set; }
    public bool IncludeDate { get; set; }
    public bool IncludeTime { get; set; }
    public bool IncludeResolution { get; set; } = true;
    public bool IncludeCodec { get; set; } = true;
    public bool IncludeEncoder { get; set; }
    public bool IncludeQuality { get; set; }
    public bool IncludeBitrate { get; set; }
    public bool IncludeSpeed { get; set; }
    public bool IncludePreset { get; set; }
    public bool IncludeFrameRate { get; set; }
}

public static partial class FileNaming
{
    public static string Pattern(FileNamingSettings settings)
    {
        var parts = new List<string> { "{name}" };
        void Add(bool include,string token) { if(include)parts.Add(token); }
        Add(settings.IncludeDate,"{date}");Add(settings.IncludeTime,"{time}");
        Add(settings.IncludeResolution,"{resolution}");Add(settings.IncludeCodec,"{codec}");
        Add(settings.IncludeEncoder,"{encoder}");Add(settings.IncludeQuality,"{quality}");
        Add(settings.IncludeBitrate,"{bitrate}");Add(settings.IncludeSpeed,"{speed}");
        Add(settings.IncludePreset,"{preset}");Add(settings.IncludeFrameRate,"{fps}");
        return string.Join("_",parts);
    }
    public static string Render(MediaInfo source,EncodingOptions options,string pattern,DateTimeOffset? timestamp = null)
    {
        var dimensions=ScalingService.Calculate(source.Video!,options.Filters);
        int width=options.Filters.PadEnabled?options.Filters.PadWidth:dimensions.Width;
        int height=options.Filters.PadEnabled?options.Filters.PadHeight:dimensions.Height;
        var time=timestamp??options.FileNameTimestamp??DateTimeOffset.Now;
        var video=options.Video;
        var fps=options.Filters.FrameRate>0?options.Filters.FrameRate:source.Video?.FrameRate;
        string Quality() => !video.CanEditQuality?"":
            (video.RateControl==RateControl.QP||VideoPresentation.UsesQpForQuality(video.Encoder)?"QP":video.IsNvenc?"CQ":"CRF")+Number(video.Quality);
        string Token(Match match) => match.Value switch
        {
            "{name}"=>Path.GetFileNameWithoutExtension(source.Path),
            "{width}"=>width.ToString(CultureInfo.InvariantCulture),"{height}"=>height.ToString(CultureInfo.InvariantCulture),
            "{resolution}"=>$"{width}x{height}","{codec}"=>ScalingService.Family(video.Encoder),
            "{encoder}"=>video.Encoder,"{preset}"=>options.PresetName,
            "{quality}"=>Quality(),"{bitrate}"=>video.CanEditBitrate?video.BitRateKbps.ToString(CultureInfo.InvariantCulture)+"kbps":"",
            "{speed}"=>string.IsNullOrWhiteSpace(video.Preset)?"":"preset-"+video.Preset,
            "{fps}"=>fps is >0?Number(fps.Value)+"fps":"",
            "{date}"=>time.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"{time}"=>time.ToString("HH-mm-ss",CultureInfo.InvariantCulture),
            _=>match.Value
        };
        // Split the template first: underscores or token-looking text in the source name stay untouched.
        var fragments=pattern.Split('_').Select(part=>(Template:part,Text:Placeholder().Replace(part,Token)));
        return string.Join("_",fragments.Where(p=>p.Template.Length==0||p.Text.Length>0).Select(p=>p.Text));
    }
    private static string Number(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    [GeneratedRegex(@"\{[a-z]+\}")] private static partial Regex Placeholder();
}
