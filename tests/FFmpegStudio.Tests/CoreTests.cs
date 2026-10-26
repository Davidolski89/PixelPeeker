using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class CoreTests
{
    private static MediaInfo Media(int w = 1920, int h = 1080, string sar = "1:1", double rotation = 0) => new()
    {
        Path = Path.GetFullPath("Quelle ä.mkv"), Size = 1_000_000, Duration = 100,
        Streams =
        [
            new() { Index = 0, Type = "video", Codec = "h264", Width = w, Height = h, SampleAspectRatio = sar, Rotation = rotation, AverageFrameRate = "24000/1001" },
            new() { Index = 2, Type = "audio", Codec = "eac3", Channels = 6, BitRate = 448_000, Tags = new() { ["language"] = "deu" } },
            new() { Index = 5, Type = "audio", Codec = "dts", Channels = 6 },
            new() { Index = 8, Type = "subtitle", Codec = "subrip" },
            new() { Index = 10, Type = "subtitle", Codec = "subrip" }
        ]
    };
    [Theory]
    [InlineData(2560,1080,ScaleMode.FullHD1080,1920,810)]
    [InlineData(3840,2160,ScaleMode.FullHD1080,1920,1080)]
    [InlineData(640,360,ScaleMode.FullHD1080,640,360)]
    [InlineData(1080,1920,ScaleMode.HD720,404,720)]
    public void Bounds_preserve_aspect_without_upscaling(int w,int h,ScaleMode mode,int expectedW,int expectedH)
    {
        Assert.Equal(new Dimensions(expectedW, expectedH), ScalingService.Calculate(Media(w,h).Video!, new() { ScaleMode = mode }));
    }
    [Fact] public void Sar_and_rotation_are_respected()
    {
        Assert.Equal(new Dimensions(768,576), ScalingService.Calculate(Media(720,576,"16:15").Video!,new()));
        Assert.Equal(new Dimensions(1080,1920), ScalingService.Calculate(Media(rotation:90).Video!,new()));
    }
    [Fact] public void Unchanged_geometry_has_no_scale_sar_or_vf_argument()
    {
        var media=Media(2560,1440);var options=EncodingOptions.ForMedia(media);var builder=new FFmpegCommandBuilder();
        options.Filters.ScaleMode=ScaleMode.Original;
        Assert.Empty(FilterService.Build(media,options));
        Assert.DoesNotContain("-vf",builder.Build(media,options,Path.GetFullPath("original.mkv")));
        options.Filters.ScaleMode=ScaleMode.Prozent;options.Filters.Percent=100;
        Assert.Empty(FilterService.Build(media,options));
        var small=Media(640,360);options.Filters.ScaleMode=ScaleMode.FullHD1080;
        Assert.Empty(FilterService.Build(small,options));
        options.Video.Encoder="hevc_nvenc";options.Video.CudaDecode=true;options.Video.CudaScale=true;
        options.Filters.ScaleMode=ScaleMode.Original;
        var cuda=builder.Build(media,options,Path.GetFullPath("cuda-original.mkv"));
        Assert.Contains("-hwaccel_output_format",cuda);Assert.DoesNotContain("-vf",cuda);
        options.Filters.ScaleMode=ScaleMode.Prozent;options.Filters.Percent=50;
        Assert.Equal("scale_cuda=w=1280:h=720:interp_algo=lanczos,setsar=1",FilterService.Build(media,options));
    }
    [Fact] public void Required_scaling_and_square_pixel_normalization_are_preserved()
    {
        var options=EncodingOptions.ForMedia(Media());options.Filters.ScaleMode=ScaleMode.Original;
        Assert.Equal("scale=768:576:flags=lanczos,setsar=1",FilterService.Build(Media(720,576,"16:15"),options));
        Assert.Equal("scale=640:360:flags=lanczos,setsar=1",FilterService.Build(Media(641,361),options));
        Assert.Equal("setsar=1",FilterService.Build(Media(720,480,"1000001:1000000"),options));
        options.Filters.ScaleMode=ScaleMode.FullHD1080;
        Assert.Equal("scale=1920:1080:flags=lanczos,setsar=1",FilterService.Build(Media(2560,1440),options));
    }
    [Fact] public void Crop_rotation_padding_and_other_filters_do_not_force_an_identity_scale()
    {
        var media=Media();var options=EncodingOptions.ForMedia(media);options.Filters.ScaleMode=ScaleMode.Original;
        Assert.Empty(FilterService.Build(Media(rotation:90),options));
        Assert.Contains("scale=",FilterService.Build(Media(rotation:30),options));
        options.Filters.Rotation=90;Assert.Equal("transpose=clock",FilterService.Build(media,options));
        options.Filters.Rotation=0;options.Filters.CropEnabled=true;options.Filters.CropLeft=100;options.Filters.CropRight=100;
        Assert.Equal("crop=iw-200:ih-0:100:0",FilterService.Build(media,options));
        options.Filters.CropEnabled=false;options.Filters.PadEnabled=true;
        Assert.Equal("pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black",FilterService.Build(Media(1280,720),options));
        options.Filters.PadEnabled=false;options.Filters.FlipHorizontal=true;options.Filters.FrameRate=25;
        Assert.Equal("hflip,fps=25",FilterService.Build(media,options));
    }
    [Fact] public void Capability_validation_only_requires_filters_present_in_the_generated_chain()
    {
        var media=Media();var options=EncodingOptions.ForMedia(media);options.Video.Preset="";
        var caps=new Capabilities { Encoders=[new("libx265","x265","video","HEVC",false,Options:[new("crf","float","",[])])] };
        var builder=new FFmpegCommandBuilder();
        options.Filters.ScaleMode=ScaleMode.Original;
        Assert.Empty(builder.Validate(media,options,caps));
        options.Filters.ScaleMode=ScaleMode.Prozent;options.Filters.Percent=50;
        Assert.Contains(builder.Validate(media,options,caps),e=>e.Contains("'scale'"));
        Assert.Contains(builder.Validate(media,options,caps),e=>e.Contains("'setsar'"));
        options.Filters.AllowUpscale=true;options.Filters.Percent=double.MaxValue;
        Assert.NotEmpty(builder.Validate(media,options,caps));
    }
    [Fact] public void Crop_never_removes_everything()
    {
        Assert.Throws<ArgumentException>(() => ScalingService.Calculate(Media().Video!,new() { CropEnabled = true, CropLeft = 1900, CropRight = 200 }));
    }
    [Fact] public void Oversized_resolution_is_a_validation_error_instead_of_a_summary_exception()
    {
        var media=Media(); var options=EncodingOptions.ForMedia(media);
        options.Filters.ScaleMode=ScaleMode.Prozent; options.Filters.AllowUpscale=true; options.Filters.Percent=double.MaxValue;
        Assert.NotEmpty(new FFmpegCommandBuilder().Validate(media,options));
        Assert.Contains("zu groß",ScalingService.Describe(media,options));
    }
    [Fact] public void Mixed_tracks_use_input_indices_and_contiguous_output_indices()
    {
        var media = Media(); var o = EncodingOptions.ForMedia(media);
        o.Audio[1].Action = TrackAction.Encodieren; o.Audio[1].Encoder = "libopus"; o.Audio[1].BitRateKbps = 256;
        o.Subtitles[0].Action = TrackAction.Entfernen;
        var args = new FFmpegCommandBuilder().Build(media,o,Path.GetFullPath("output.mkv"));
        Assert.Contains("0:2", args); Assert.Contains("0:5", args); Assert.DoesNotContain("0:8", args); Assert.Contains("0:10",args);
        Assert.Equal("copy", Value(args,"-c:a:0")); Assert.Equal("libopus",Value(args,"-c:a:1"));
        Assert.Equal("10",Value(args,"-compression_level:a:1")); Assert.Equal("256k",Value(args,"-b:a:1"));
        Assert.DoesNotContain("-b:a:0",args); Assert.Equal("copy",Value(args,"-c:s:0")); Assert.DoesNotContain("-c:s:1",args);
        Assert.Contains("-n",args); Assert.DoesNotContain("-y",args);
    }
    [Theory] [InlineData("libopus","10")] [InlineData("libmp3lame","0")] [InlineData("flac","12")]
    public void Audio_quality_is_independent_of_bitrate(string encoder,string compression)
    {
        var media = Media(); var o = EncodingOptions.ForMedia(media); var a = o.Audio[0];
        a.Action = TrackAction.Encodieren; a.Encoder = encoder; a.Channels = 2; a.BitRateKbps = 192;
        var args = new FFmpegCommandBuilder().Build(media,o,Path.GetFullPath("output.mkv"));
        Assert.Equal(compression,Value(args,"-compression_level:a:0"));
        if (encoder == "flac") Assert.DoesNotContain("-b:a:0",args);
        a.CompressionLevel = 5; a.BitRateKbps = 256; Assert.Equal(5,a.CompressionLevel);
    }
    [Fact] public void Bitrate_choices_change_with_codec_and_channels()
    {
        Assert.NotEqual(AudioEncoding.Bitrates("aac",2),AudioEncoding.Bitrates("libopus",6));
        Assert.Empty(AudioEncoding.Bitrates("flac",2));
    }
    [Fact] public void Bitmap_to_text_is_rejected()
    {
        var media = Media(); var o = EncodingOptions.ForMedia(media);
        o.Subtitles[0].IsBitmap = true; o.Subtitles[0].Action = TrackAction.Encodieren;
        Assert.Contains(new FFmpegCommandBuilder().Validate(media,o),x => x.Contains("OCR"));
    }
    [Fact] public void Container_mismatch_and_source_overwrite_are_rejected()
    {
        var media = Media(); var o = EncodingOptions.ForMedia(media); o.Container = "webm";
        Assert.NotEmpty(new FFmpegCommandBuilder().Validate(media,o));
        o.Container = "mkv";
        Assert.Throws<ArgumentException>(() => new FFmpegCommandBuilder().Build(media,o,media.Path));
    }
    [Fact] public void Missing_encoder_is_not_available()
    {
        Assert.Contains(new FFmpegCommandBuilder().Validate(Media(), EncodingOptions.ForMedia(Media()),new()), x=>x.Contains("nicht vorhanden"));
        Assert.Equal("VVC",ScalingService.Family("libvvenc"));
        Assert.False(new VideoOptions { Encoder = "libvvenc" }.IsNvenc);
    }
    [Fact] public void Removed_backends_are_hidden_and_cannot_run_through_saved_options()
    {
        string[] allowed = ["libx264", "libx264rgb", "libx265", "libvvenc", "libaom-av1", "librav1e", "libsvtav1",
            "h264_nvenc", "hevc_nvenc", "av1_nvenc", "h264_qsv", "hevc_qsv", "av1_qsv", "h264_amf", "hevc_amf", "av1_amf"];
        var removed = new[] { "h264", "hevc", "av1" }
            .SelectMany(codec => new[] { "vaapi", "vulkan", "d3d12va", "mf", "v4l2m2m", "videotoolbox", "mediacodec", "omx", "rkmpp", "future_backend" }
                .Select(backend => $"{codec}_{backend}")).ToArray();
        var text = string.Join('\n', allowed.Concat(removed).Select(name => $" V..... {name} Test encoder")) +
            "\n A..... libopus Opus\n S..... srt SubRip\n";
        var caps = new Capabilities { Encoders = CapabilityParser.Encoders(text), Filters = ["scale", "setsar"] };
        Assert.Equal(allowed.Order(), caps.ModernVideo.Select(e => e.Name).Order());
        Assert.Equal("libopus", Assert.Single(caps.Audio).Name);
        Assert.Equal("subtitle", caps.Find("srt")!.MediaType);
        var media = Media(); var builder = new FFmpegCommandBuilder();
        var options = EncodingOptions.ForMedia(media);
        options.Video.RateControl = RateControl.Bitrate; options.Video.Preset = "";
        foreach (var name in allowed)
        {
            options.Video.Encoder = name;
            Assert.Equal(name, Value(builder.Build(media, options, Path.GetFullPath("output.mkv"), caps), "-c:v:0"));
        }
        foreach (var name in removed)
        {
            options.Video.Encoder = name;
            var savedOptions = Json.Clone(options);
            foreach (var capabilities in new Capabilities?[] { null, caps })
            {
                var error = Assert.Single(builder.Validate(media, savedOptions, capabilities));
                Assert.Contains(name, error);
                Assert.Contains("nicht angeboten", error);
                Assert.Throws<ArgumentException>(() => builder.Build(media, savedOptions, Path.GetFullPath("output.mkv"), capabilities));
            }
        }
    }
    [Fact] public void Empty_optional_encoder_selections_do_not_crash_validation_or_command_preview()
    {
        var media=Media(); var options=EncodingOptions.ForMedia(media);
        options.Video.PixelFormat=null!; options.Video.Preset=null!; options.Video.Tune=null!;
        options.Video.Profile=null!; options.Video.Level=null!;
        var caps=new Capabilities { Filters=["scale","setsar"], Encoders=
            [new("libx265","x265","video","HEVC",false,PixelFormats:["yuv420p"],Options:[new("crf","int","",[])])] };
        var builder=new FFmpegCommandBuilder();
        Assert.Empty(builder.Validate(media,options,caps));
        var args=builder.Build(media,options,Path.GetFullPath("output.mkv"),caps);
        foreach(var option in new[]{"-pix_fmt","-preset","-tune","-profile:v","-level:v"}) Assert.DoesNotContain(option,args);
        options.Video.Encoder=null!;
        Assert.False(options.Video.IsNvenc);
        Assert.Contains(builder.Validate(media,options,caps),error=>error.Contains("Encoder"));
    }
    [Fact] public void Probe_reads_real_stream_metadata_and_unknown_values()
    {
        var m = MediaProbeParser.Parse("""
        {"streams":[{"index":3,"codec_type":"video","codec_name":"hevc","width":2560,"height":1080,"pix_fmt":"yuv420p10le","color_transfer":"smpte2084","avg_frame_rate":"24000/1001","side_data_list":[{"rotation":90}]}],"format":{"duration":"12.5","size":"12345"}}
        ""","x.mkv");
        Assert.Equal(3,m.Video!.Index); Assert.Equal(10,m.Video.BitDepth); Assert.True(m.Video.IsHdr); Assert.Equal(90,m.Video.Rotation);
        Assert.Null(m.Video.BitRate); Assert.Equal(12.5,m.Duration);
    }
    [Fact] public void Quality_scales_follow_codec_mode_and_actual_finite_option_limits()
    {
        foreach (var (encoder, mode, maximum, step) in new[]
        {
            ("libx264", RateControl.Qualität, 51d, .5), ("libx265", RateControl.Qualität, 51d, .5),
            ("hevc_nvenc", RateControl.Qualität, 51d, .5), ("av1_nvenc", RateControl.Qualität, 63d, .5),
            ("av1_nvenc", RateControl.QP, 255d, 1d), ("libsvtav1", RateControl.Qualität, 63d, 1d),
            ("libaom-av1", RateControl.Qualität, 63d, 1d), ("librav1e", RateControl.Qualität, 255d, 1d),
            ("libvvenc", RateControl.Qualität, 63d, 1d), ("libx265", RateControl.QP, 51d, 1d)
        })
            Assert.Equal(new QualityRange(encoder == "av1_nvenc" && mode == RateControl.QP ? 1 : 0, maximum, step), VideoQualityScale.For(new() { Encoder = encoder, RateControl = mode }));
        var video = new VideoOptions { Encoder = "av1_nvenc" };
        var info = CapabilityParser.WithHelp(new("av1_nvenc", "AV1", "video", "AV1", true),
            "  -cq <float> E..V....... Quality (from 0 to 63) (default 0)\n  -qp <int> E..V....... QP (from -1 to 255) (default -1)\n");
        Assert.Equal(new QualityRange(0, 63, .5), VideoQualityScale.For(video, info));
        video.RateControl = RateControl.QP;
        Assert.Equal(new QualityRange(1, 255, 1), VideoQualityScale.For(video, info));
        var bounded = info with { Options = [new("qp", "int", "QP (from 2 to 48)", [])] };
        Assert.Equal(new QualityRange(2, 48, 1), VideoQualityScale.For(video, bounded));
        var unbounded = info with { Options = [new("qp", "int", "QP (from -1 to INT_MAX)", [])] };
        Assert.Equal(new QualityRange(1, 255, 1), VideoQualityScale.For(video, unbounded));
    }
    [Fact] public void Saved_quality_is_validated_against_the_active_scale_without_being_overwritten()
    {
        var media = Media(); var options = EncodingOptions.ForMedia(media); var builder = new FFmpegCommandBuilder();
        options.Video.Encoder = "av1_nvenc"; options.Video.RateControl = RateControl.QP; options.Video.Quality = 255;
        Assert.Empty(builder.Validate(media, options));
        options.Video.Quality = 256;
        var restored = Json.Clone(options);
        Assert.Contains(builder.Validate(media, restored), error => error.Contains("zwischen 1 und 255"));
        Assert.Equal(256, restored.Video.Quality);
        restored.Video.RateControl = RateControl.Bitrate;
        Assert.Empty(builder.Validate(media, restored));
        restored.Video.RateControl = RateControl.QP; restored.Video.Quality = 23.5;
        Assert.Contains(builder.Validate(media, restored), error => error.Contains("ganzzahligen"));
        restored.Video.Encoder = "libx265"; restored.Video.RateControl = RateControl.Qualität;
        Assert.Equal("23.5", Value(builder.Build(media, restored, Path.GetFullPath("output.mkv")), "-crf"));
    }
    [Fact] public void Capability_help_and_options_are_parsed()
    {
        var info = CapabilityParser.Encoders(" V....D hevc_nvenc NVIDIA NVENC HEVC encoder (codec hevc)\n A..... libopus libopus Opus\n").First();
        Assert.True(info.Hardware); Assert.Equal("HEVC",info.Family);
        info = CapabilityParser.WithHelp(info,"Supported pixel formats: yuv420p p010le\n  -preset <int> E..V....... speed\n     p1 12 E..V....... fast\n  -cq <float> E..V....... quality\n");
        Assert.Contains("p010le",info.PixelFormats!); Assert.Contains("p1",info.Options![0].Choices);
    }
    [Fact] public void Numeric_speed_presets_are_discovered_from_help_without_replacing_named_presets()
    {
        foreach(var (encoder,option,lower,upper) in new[]{("libsvtav1","preset",-2,13),("libaom-av1","cpu-used",-1,8),("librav1e","speed",-1,10)})
        {
            var info=CapabilityParser.WithHelp(new(encoder,encoder,"video","AV1",false),
                $"  -{option} <int> E..V....... Encoding speed (from {lower} to {upper}) (default {lower})\n");
            Assert.Equal(Enumerable.Range(0,upper+1).Select(n=>n.ToString()),Assert.Single(info.Options!).Choices);
        }
        var named=CapabilityParser.WithHelp(new("hevc_nvenc","NVENC","video","HEVC",true),
            "  -preset <int> E..V....... Encoding preset (from 0 to 18)\n     p1 12 E..V....... fastest\n     p7 18 E..V....... slowest\n");
        Assert.Equal(new[]{"p1","p7"},Assert.Single(named.Options!).Choices);
        var unknown=CapabilityParser.WithHelp(named,"  -preset <int> E..V....... Preset (from -1 to INT_MAX)\n");
        Assert.Empty(Assert.Single(unknown.Options!).Choices);
    }
    [Fact] public void Both_current_and_legacy_filter_flag_formats_are_supported()
    {
        var filters=CapabilityParser.Filters(" .. scale V->V scale\n T.. setsar V->V SAR\n TS cropdetect V->V crop\n .S libvmaf VV->V analysis\n ... anull A->A pass");
        Assert.Contains("scale",filters);Assert.Contains("setsar",filters);Assert.Contains("libvmaf",filters);Assert.Equal(5,filters.Count);
    }
    [Fact] public void Progress_microseconds_and_unknown_duration_are_correct()
    {
        var parser = new ProgressParser(10); parser.Push("out_time_us=5000000"); parser.Push("speed=2.0x"); parser.Push("fps=50");
        var p = parser.Push("progress=continue")!; Assert.Equal(50,p.Percent); Assert.Equal(2.5,p.Remaining!.Value.TotalSeconds);
        Assert.Null(new ProgressParser(null).Push("progress=continue")!.Percent);
    }
    [Theory] [InlineData("../escape")] [InlineData("C:/escape")] [InlineData("/escape")] [InlineData("folder/../../escape")] [InlineData("file:stream")]
    public void Unsafe_archive_paths_are_rejected(string key) =>
        Assert.Throws<InvalidDataException>(() => FFmpegDownloadService.SafeEntryPath(Path.GetTempPath(),key));
    [Fact] public void Version_detection_uses_build_identity()
    {
        Assert.False(VersionMetadata.IsNewBuild("2026-10-15-git-afead44165","2026-10-15-git-afead44165\n"));
        Assert.True(VersionMetadata.IsNewBuild("2026-10-12-git-615b07170e","2026-10-15-git-afead44165"));
        Assert.False(VersionMetadata.IsNewBuild("2026-10-15-git-afead44165","2026-10-12-git-615b07170e"));
        Assert.Throws<InvalidDataException>(()=>VersionMetadata.ParseVersion("<html>404</html>"));
    }
    [Fact] public async Task Corrupt_download_is_rejected()
    {
        var path = Path.GetTempFileName();
        try { await File.WriteAllTextAsync(path,"corrupt"); await Assert.ThrowsAsync<InvalidDataException>(() => FFmpegDownloadService.VerifyHashAsync(path,new string('0',64))); }
        finally { File.Delete(path); }
    }
    [Fact] public void Size_estimate_does_not_invent_crf_sizes()
    {
        var m = Media(); var o = EncodingOptions.ForMedia(m); Assert.Null(SizeEstimator.Estimate(m,o));
        o.Video.RateControl = RateControl.Bitrate; o.Audio[1].Action = TrackAction.Entfernen;
        Assert.Equal((long)(100 * (4_000_000 + 448_000) / 8d * 1.02),SizeEstimator.Estimate(m,o));
    }
    [Fact] public void Running_analysis_state_is_observable_but_is_not_restored_as_a_running_task()
    {
        var job=new EncodingJob { Source=Media(),Status=JobStatus.Abgeschlossen };
        var changes=new List<string?>(); job.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName);
        job.IsAnalyzing=true; job.AnalysisStatus="VMAF berechnen";
        Assert.True(job.IsAnalysisIndeterminate); Assert.Equal("Analyse läuft",job.DisplayStatus);
        job.AnalysisProgress=25;
        Assert.False(job.IsAnalysisIndeterminate);
        Assert.Contains(nameof(EncodingJob.IsAnalysisIndeterminate),changes);
        var restored=Json.Clone(job);
        Assert.False(restored.IsAnalyzing); Assert.Null(restored.AnalysisProgress); Assert.False(restored.HasAnalysisStatus);
        Assert.Equal(JobStatus.Abgeschlossen,restored.Status);
    }
    [Fact] public async Task Process_start_is_reported_only_after_a_process_actually_starts()
    {
        var runner = new ProcessRunner(); var starts = 0;
        var result = await runner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),
            ["/d","/c","exit","0"], started: () => starts++);
        Assert.Equal(0,result.ExitCode); Assert.Equal(1,starts);
        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(
            Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe"),[], started: () => starts++));
        Assert.Equal(1,starts);
    }
    [Fact] public void Suggested_output_path_uses_real_naming_settings_without_creating_files()
    {
        var source = Media(); var options = EncodingOptions.ForMedia(source);
        options.OutputDirectory = Path.Combine(Path.GetTempPath(), "ffstudio-preview-"+Guid.NewGuid());
        options.FileNamePattern = "{name}_{width}x{height}_{encoder}";
        options.Filters.ScaleMode = ScaleMode.Prozent; options.Filters.Percent = 50;
        Assert.Equal(Path.Combine(options.OutputDirectory,"Quelle ä_960x540_libx265.mkv"),QueueService.SuggestedOutputPath(source,options));
        Assert.False(Directory.Exists(options.OutputDirectory));
    }
    [Fact] public async Task Queue_recovery_marks_running_job_interrupted()
    {
        var root = Path.Combine(Path.GetTempPath(),"ffstudio-test-"+Guid.NewGuid());
        try
        {
            var store = new QueueStore(new(root)); await store.InitializeAsync();
            var command=FFmpegCommandBuilder.Display("ffmpeg.exe",["-i",Media().Path,"-crf","23","output.mkv"]);
            await store.SaveJobAsync(new() { Status = JobStatus.Läuft, Source = Media(), ExecutedCommand=command });
            var jobs = await store.LoadJobsAsync(); Assert.Equal(JobStatus.Unterbrochen,Assert.Single(jobs).Status);
            Assert.Equal(command,jobs[0].ExecutedCommand);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }
    }
    [Fact] public void Preset_remaps_indices_to_new_media()
    {
        var preset = PresetService.BuiltIns().First(); preset.Options.Audio.Add(new() { InputIndex = 99,Action = TrackAction.Encodieren });
        var options = PresetService.Apply(preset,Media());
        Assert.Equal(2,options.Audio[0].InputIndex); Assert.Equal(TrackAction.Encodieren,options.Audio[0].Action);
        Assert.Equal(preset.Name,options.PresetName); Assert.Contains("→",ScalingService.Describe(Media(),options));
    }
    [Fact] public void Inactive_quality_and_bitrate_values_do_not_block_the_selected_mode()
    {
        var media=Media();var options=EncodingOptions.ForMedia(media);var builder=new FFmpegCommandBuilder();
        var output=Path.GetFullPath("inactive-fields.mkv");
        options.Video.Quality=double.NaN;
        foreach(var mode in new[]{RateControl.Bitrate,RateControl.CBR})
        {
            options.Video.RateControl=mode;
            Assert.Equal("4000k",Value(builder.Build(media,options,output),"-b:v"));
        }
        options.Video.RateControl=RateControl.Qualität;
        Assert.Throws<ArgumentException>(()=>builder.Build(media,options,output));
        options.Video.Quality=23;options.Video.BitRateKbps=-1;
        foreach(var mode in new[]{RateControl.Qualität,RateControl.QP})
        {
            options.Video.RateControl=mode;
            Assert.DoesNotContain("-b:v",builder.Build(media,options,output));
        }
        options.Video.RateControl=RateControl.Bitrate;
        Assert.Throws<ArgumentException>(()=>builder.Build(media,options,output));
    }
    [Fact] public void Svt_controls_share_expert_parameters_and_survive_preset_serialization()
    {
        var media=Media();var options=EncodingOptions.ForMedia(media);var video=options.Video;
        video.Encoder="libsvtav1";video.Preset="6";video.Gop=240;
        video.SvtTune="0";video.SvtFilmGrain="0";video.SvtDeblocking="2";
        Assert.Equal("tune=0:film-grain=0:enable-dlf=2",video.SvtAv1Parameters);
        video.SvtAv1Parameters += @":enable-tf=0:fgs-table='C:\grain\test:table.tbl'";
        video.SvtTune="1";
        Assert.Contains(@"fgs-table='C:\grain\test:table.tbl'",video.SvtAv1Parameters);
        Assert.Contains("enable-tf=0",video.SvtAv1Parameters);
        video.SvtTune="0";
        var restored=Json.Clone(new Preset{Options=options}).Options;
        Assert.Equal(240,restored.Video.Gop);Assert.Equal("0",restored.Video.SvtTune);
        Assert.Equal("0",restored.Video.SvtFilmGrain);Assert.Equal("2",restored.Video.SvtDeblocking);
        Assert.Equal(video.SvtAv1Parameters,restored.Video.Extra["svtav1-params"]);
        var args=new FFmpegCommandBuilder().Build(media,restored,Path.GetFullPath("svt-options.mkv"));
        Assert.Equal("240",Value(args,"-g"));Assert.Equal(video.SvtAv1Parameters,Value(args,"-svtav1-params"));
        Assert.Equal(1,args.Count(a=>a=="-svtav1-params"));Assert.DoesNotContain("-tune",args);
        restored.Video.Extra=new(){["svtav1-params"]="tune=2:film-grain=12:enable-dlf=1"};
        Assert.Equal("2",restored.Video.SvtTune);Assert.Equal("12",restored.Video.SvtFilmGrain);
        restored.Video.SvtTune="";Assert.DoesNotContain("tune=",restored.Video.SvtAv1Parameters);
        restored.Video.SvtFilmGrain="";restored.Video.SvtDeblocking="";restored.Video.Gop=0;
        args=new FFmpegCommandBuilder().Build(media,restored,Path.GetFullPath("svt-default.mkv"));
        Assert.DoesNotContain("-svtav1-params",args);Assert.DoesNotContain("-g",args);
    }
    [Theory]
    [InlineData("tune=6")][InlineData("tune=-1")][InlineData("film-grain=51")]
    [InlineData("film-grain=0.5")][InlineData("enable-dlf=3")][InlineData("enable-dlf=abc")]
    [InlineData("tune=0:tune=1")][InlineData("film-grain=")][InlineData("tune=0:broken")]
    public void Svt_invalid_parameters_are_rejected_before_start(string parameters)
    {
        var options=EncodingOptions.ForMedia(Media());options.Video.Encoder="libsvtav1";
        options.Video.Preset="6";options.Video.SvtAv1Parameters=parameters;
        Assert.Throws<ArgumentException>(()=>new FFmpegCommandBuilder().Build(Media(),options,Path.GetFullPath("invalid-svt.mkv")));
    }
    [Fact] public void Gop_and_svt_parameters_cannot_silently_override_each_other_or_leak_to_another_encoder()
    {
        var options=EncodingOptions.ForMedia(Media());var builder=new FFmpegCommandBuilder();
        options.Video.Gop=-1;Assert.NotEmpty(builder.Validate(Media(),options));
        options.Video.Gop=240;options.Video.Encoder="libsvtav1";options.Video.Preset="6";
        options.Video.SvtAv1Parameters="keyint=120";Assert.NotEmpty(builder.Validate(Media(),options));
        options.Video.SvtAv1Parameters="tune=0:film-grain=0:enable-dlf=2";
        Assert.Empty(builder.Validate(Media(),options));
        options.Video.Encoder="libx265";options.Video.Preset="medium";
        Assert.NotEmpty(builder.Validate(Media(),options));
    }
    private static string Value(IReadOnlyList<string> args,string key) => args[args.ToList().IndexOf(key)+1];
}
