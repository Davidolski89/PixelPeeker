using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class CodecMemoryTests
{
    [Fact] public void Each_video_encoder_keeps_an_independent_complete_snapshot()
    {
        var memory=new CodecMemory();
        var svt=new VideoOptions{Encoder="libsvtav1",Quality=31,Preset="4",Gop=180,PixelFormat="yuv420p10le",Extra=new(){{"svtav1-params","tune=0:film-grain=8:enable-dlf=2:lp=3"}}};
        memory.Remember(svt);svt.Quality=60;svt.Extra["svtav1-params"]="tune=1";
        var nvenc=new VideoOptions{Encoder="av1_nvenc",RateControl=RateControl.QP,Quality=200,Preset="p7",GpuIndex=1,CudaDecode=true,CudaScale=true,BitRateKbps=8000,MaxRateKbps=9000,BufferKbps=18000,BFrames=3,References=4,ThreadCount=2,ThreadPercent=70,Tune="hq",Profile="main",Level="5.1"};
        memory.Remember(nvenc);
        var restored=memory.ForVideo("libsvtav1");Assert.Equal(31,restored.Quality);Assert.Equal("4",restored.Preset);Assert.Equal(180,restored.Gop);Assert.Equal("tune=0:film-grain=8:enable-dlf=2:lp=3",restored.SvtAv1Parameters);
        CodecMemory.CopyVideo(restored,nvenc);Assert.False(nvenc.CudaScale);Assert.False(nvenc.CudaDecode);Assert.Equal("libsvtav1",nvenc.Encoder);
        CodecMemory.CopyVideo(memory.ForVideo("av1_nvenc"),nvenc);Assert.Equal(200,nvenc.Quality);Assert.Equal(1,nvenc.GpuIndex);Assert.Equal(9000,nvenc.MaxRateKbps);Assert.Equal(3,nvenc.BFrames);Assert.Equal("main",nvenc.Profile);
        restored.Extra.Clear();Assert.NotEmpty(memory.ForVideo("libsvtav1").Extra);
        Assert.Equal("av1_nvenc",memory.LastVideoEncoder);
    }
    [Fact] public void Audio_memory_restores_parameters_without_copying_track_identity_or_actions()
    {
        var memory=new CodecMemory();var first=new AudioTrackOptions{InputIndex=7,SourceCodec="ac3",SourceChannels=6,Encoder="libopus",BitRateKbps=256,CompressionLevel=5,Channels=2,SampleRate=48000,ChannelLayout="stereo",BitrateMode="constrained",Application="voip",Language="deu",Title="German",IsDefault=true,Action=TrackAction.Encodieren};
        memory.Remember(first);first.BitRateKbps=64;
        var second=new AudioTrackOptions{InputIndex=1,SourceCodec="aac",SourceChannels=2,Encoder="libopus",Language="eng",Title="English",Action=TrackAction.Kopieren};
        Assert.True(memory.Restore(second));Assert.Equal(256,second.BitRateKbps);Assert.Equal(5,second.CompressionLevel);Assert.Equal("voip",second.Application);
        Assert.Equal(1,second.InputIndex);Assert.Equal("aac",second.SourceCodec);Assert.Equal("eng",second.Language);Assert.Equal("English",second.Title);Assert.False(second.IsDefault);Assert.Equal(TrackAction.Kopieren,second.Action);
        second.Encoder="flac";Assert.False(memory.Restore(second));second.CompressionLevel=8;memory.Remember(second);
        second.Encoder="libopus";memory.Restore(second);Assert.Equal(5,second.CompressionLevel);Assert.Equal(256,second.BitRateKbps);
    }
    [Fact] public void New_files_use_the_last_available_encoders_but_keep_their_own_tracks_filters_and_cuts()
    {
        var memory=new CodecMemory{LastSubtitleEncoder="ass"};memory.Remember(new VideoOptions{Encoder="libsvtav1",Quality=33,Preset="3"});memory.Remember(new AudioTrackOptions{Encoder="aac",BitRateKbps=256});
        var caps=new Capabilities{Encoders=[new("libsvtav1","","video","AV1",false),new("libx265","","video","HEVC",false),new("aac","","audio","",false),new("ass","","subtitle","",false)]};
        var source=new MediaInfo{Path=Path.GetFullPath("new.mkv"),Streams=[new(){Index=0,Type="video"},new(){Index=2,Type="audio",Codec="ac3",Tags=new(){{"language","pol"}}},new(){Index=4,Type="subtitle",Codec="subrip"}]};
        var options=EncodingOptions.ForMedia(source);memory.ApplyToNewFile(options,caps);
        Assert.Equal("libsvtav1",options.Video.Encoder);Assert.Equal(33,options.Video.Quality);Assert.Equal("aac",options.Audio[0].Encoder);Assert.Equal("ass",options.Subtitles[0].Encoder);
        Assert.Equal(2,options.Audio[0].InputIndex);Assert.Equal("pol",options.Audio[0].Language);Assert.Equal(TrackAction.Kopieren,options.Audio[0].Action);Assert.Empty(options.Cuts);Assert.Equal(ScaleMode.Original,options.Filters.ScaleMode);
        options.Video.Quality=40;Assert.Equal(33,memory.ForVideo("libsvtav1").Quality);
        memory.LastVideoEncoder="av1_nvenc";memory.ApplyToNewFile(options,caps);Assert.Equal("libsvtav1",options.Video.Encoder);
        options=EncodingOptions.ForMedia(source);memory.ApplyToNewFile(options,caps);Assert.Equal("libx265",options.Video.Encoder);
    }
    [Fact] public async Task Codec_preferences_survive_a_settings_reload_and_old_settings_still_load()
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-codecs-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();
            settings.Current.Codecs.Remember(new VideoOptions{Encoder="libsvtav1",Quality=29,Preset="5",Gop=96,Extra=new(){{"svtav1-params","tune=0:lp=3"}}});
            settings.Current.Codecs.Remember(new AudioTrackOptions{Encoder="aac",BitRateKbps=256});settings.Current.Codecs.LastSubtitleEncoder="mov_text";await settings.SaveAsync();
            var reload=new SettingsService(paths,new(paths));await reload.LoadAsync();Assert.Equal("libsvtav1",reload.Current.Codecs.LastVideoEncoder);Assert.Equal(29,reload.Current.Codecs.ForVideo("libsvtav1").Quality);Assert.Equal(256,reload.Current.Codecs.Audio["aac"].BitRateKbps);Assert.Equal("mov_text",reload.Current.Codecs.LastSubtitleEncoder);
            await File.WriteAllTextAsync(Path.Combine(root,"settings.json"),"{\"Codecs\":null}");await reload.LoadAsync();Assert.NotNull(reload.Current.Codecs);Assert.Empty(reload.Current.Codecs.Video);
        }
        finally{if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"ffstudio-codecs-"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
    }
    [Fact] public void Existing_jobs_seed_missing_memory_once_in_time_order_without_changing_jobs()
    {
        var older=new EncodingJob{Options=new(){FileNameTimestamp=DateTimeOffset.Parse("2026-10-18T10:00:00+02:00"),Video=new(){Encoder="libsvtav1",Quality=35,Preset="6"}}};
        var newer=Json.Clone(older);newer.Options.FileNameTimestamp=older.Options.FileNameTimestamp!.Value.AddHours(1);newer.Options.Video.Quality=27;newer.Options.Video.Preset="4";
        var memory=new CodecMemory();memory.SeedFromJobs([newer,older]);Assert.Equal(27,memory.ForVideo("libsvtav1").Quality);Assert.Equal(35,older.Options.Video.Quality);
        memory.Remember(new VideoOptions{Encoder="libx264",Quality=20});memory.SeedFromJobs([newer,older]);Assert.Equal("libx264",memory.LastVideoEncoder);
        memory.Remember(new VideoOptions{Encoder="av1_vulkan"});Assert.Equal("libx264",memory.LastVideoEncoder);Assert.DoesNotContain("av1_vulkan",memory.Video.Keys);
    }
}
