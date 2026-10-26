namespace FFmpegStudio.Core;

/// <summary>Encoder parameters only: source tracks, filters, cuts and output paths never enter this memory.</summary>
public sealed class CodecMemory
{
    public string LastVideoEncoder { get; set; } = "";
    public Dictionary<string,VideoOptions> Video { get; set; } = [];
    public string LastAudioEncoder { get; set; } = "";
    public Dictionary<string,AudioCodecSettings> Audio { get; set; } = [];
    public string LastSubtitleEncoder { get; set; } = "";
    public void Normalize()
    {
        Video??=[];Audio??=[];
        Video=Video.Where(p=>p.Value is not null&&VideoEncoderPolicy.IsAllowed(p.Key)&&p.Value.Encoder==p.Key).ToDictionary(p=>p.Key,p=>p.Value);
        Audio=Audio.Where(p=>p.Value is not null&&!string.IsNullOrWhiteSpace(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
        if(!VideoEncoderPolicy.IsAllowed(LastVideoEncoder??""))LastVideoEncoder="";
        LastAudioEncoder??="";LastSubtitleEncoder??="";
    }
    public void Remember(VideoOptions video)
    {
        if(!VideoEncoderPolicy.IsAllowed(video.Encoder))return;
        Video[video.Encoder]=Json.Clone(video);LastVideoEncoder=video.Encoder;
    }
    public void SeedFromJobs(IEnumerable<EncodingJob> jobs)
    {
        bool video=Video.Count==0&&LastVideoEncoder.Length==0,audio=Audio.Count==0&&LastAudioEncoder.Length==0,subtitles=LastSubtitleEncoder.Length==0;
        foreach(var job in jobs.OrderBy(j=>j.Options.FileNameTimestamp??j.StartedAt??DateTimeOffset.MinValue).ThenBy(j=>j.Order))
        {
            if(video)Remember(job.Options.Video);
            if(audio)foreach(var track in job.Options.Audio.Where(a=>a.Action==TrackAction.Encodieren))Remember(track);
            if(subtitles)foreach(var track in job.Options.Subtitles.Where(s=>s.Action==TrackAction.Encodieren))LastSubtitleEncoder=track.Encoder;
        }
    }
    public VideoOptions ForVideo(string encoder)
    {
        if(Video.TryGetValue(encoder,out var saved))return Json.Clone(saved);
        var video=new VideoOptions{Encoder=encoder};
        video.Preset=encoder switch{"libsvtav1" or "libaom-av1" or "librav1e"=>"6",_=>video.IsNvenc?"p6":encoder is "libx264" or "libx265" or "libvvenc"?"medium":""};
        video.RateControl=encoder is "libx264" or "libx265" or "libsvtav1" or "libaom-av1" or "librav1e" or "libvvenc"||video.IsNvenc?RateControl.Qualität:RateControl.Bitrate;
        video.Quality=ScalingService.Family(encoder)=="AV1"?28:23;
        return video;
    }
    public static void CopyVideo(VideoOptions source,VideoOptions target)
    {
        target.Encoder=source.Encoder;target.RateControl=source.RateControl;target.Quality=source.Quality;
        target.BitRateKbps=source.BitRateKbps;target.MaxRateKbps=source.MaxRateKbps;target.BufferKbps=source.BufferKbps;
        target.Preset=source.Preset;target.Tune=source.Tune;target.Profile=source.Profile;target.Level=source.Level;target.PixelFormat=source.PixelFormat;
        target.Gop=source.Gop;target.BFrames=source.BFrames;target.References=source.References;
        target.ThreadMode=source.ThreadMode;target.ThreadCount=source.ThreadCount;target.ThreadPercent=source.ThreadPercent;
        target.GpuIndex=source.GpuIndex;target.CudaDecode=source.CudaDecode;target.CudaScale=source.CudaScale;
        target.Extra=new(source.Extra);
    }
    public void Remember(AudioTrackOptions audio)
    {
        if(string.IsNullOrWhiteSpace(audio.Encoder))return;
        Audio[audio.Encoder]=AudioCodecSettings.From(audio);LastAudioEncoder=audio.Encoder;
    }
    public bool Restore(AudioTrackOptions audio)
    {
        if(!Audio.TryGetValue(audio.Encoder,out var saved))return false;
        saved.Apply(audio);return true;
    }
    public void ApplyToNewFile(EncodingOptions options,Capabilities caps)
    {
        var encoder=VideoEncoderPolicy.IsAllowed(LastVideoEncoder)&&caps.Find(LastVideoEncoder) is {MediaType:"video"}?LastVideoEncoder:options.Video.Encoder;
        if(VideoEncoderPolicy.IsAllowed(encoder)&&caps.Find(encoder) is {MediaType:"video"})options.Video=ForVideo(encoder);
        foreach(var track in options.Audio)
            if(caps.Find(LastAudioEncoder) is {MediaType:"audio"}){track.Encoder=LastAudioEncoder;Restore(track);}
        foreach(var track in options.Subtitles)
            if(caps.Find(LastSubtitleEncoder) is {MediaType:"subtitle"})track.Encoder=LastSubtitleEncoder;
    }
}

public sealed record AudioCodecSettings(int BitRateKbps,int? CompressionLevel,string BitrateMode,string Application,int Channels,int SampleRate,string ChannelLayout)
{
    public static AudioCodecSettings From(AudioTrackOptions a)=>new(a.BitRateKbps,a.CompressionLevel,a.BitrateMode,a.Application,a.Channels,a.SampleRate,a.ChannelLayout);
    public void Apply(AudioTrackOptions a)
    {
        a.BitRateKbps=BitRateKbps;a.CompressionLevel=CompressionLevel;a.BitrateMode=BitrateMode;a.Application=Application;
        a.Channels=Channels;a.SampleRate=SampleRate;a.ChannelLayout=ChannelLayout;
    }
}
