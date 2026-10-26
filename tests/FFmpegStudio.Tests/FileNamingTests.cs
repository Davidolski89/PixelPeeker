using System.Globalization;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class FileNamingTests
{
    private static readonly DateTimeOffset Time=new(2026,10,18,14,5,9,TimeSpan.FromHours(2));
    private static MediaInfo Source()=>new(){Path=Path.GetFullPath("Film__{codec} ä.mkv"),Streams=[new(){Type="video",Width=1920,Height=1080,SampleAspectRatio="1:1",AverageFrameRate="24000/1001"}]};
    [Theory]
    [InlineData("libsvtav1",RateControl.Qualität,"CRF24.5")]
    [InlineData("av1_nvenc",RateControl.Qualität,"CQ24.5")]
    [InlineData("av1_nvenc",RateControl.QP,"QP24.5")]
    [InlineData("librav1e",RateControl.Qualität,"QP24.5")]
    [InlineData("libsvtav1",RateControl.Bitrate,"4500kbps")]
    [InlineData("libsvtav1",RateControl.CBR,"4500kbps")]
    public void Names_use_only_the_active_rate_parameter_and_preserve_the_original_name(string encoder,RateControl mode,string value)
    {
        var source=Source();var options=EncodingOptions.ForMedia(source);
        options.Video.Encoder=encoder;options.Video.RateControl=mode;options.Video.Quality=24.5;options.Video.BitRateKbps=4500;
        var original=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("pl-PL");
            Assert.Equal("Film__{codec} ä_"+value,FileNaming.Render(source,options,"{name}_{quality}_{bitrate}"));
        }
        finally{CultureInfo.CurrentCulture=original;}
    }
    [Fact] public void All_selected_parts_use_output_geometry_and_stable_enqueue_time()
    {
        var source=Source();var options=EncodingOptions.ForMedia(source);
        options.Video.Encoder="libsvtav1";options.Video.Quality=24;options.Video.Preset="4";options.PresetName="Archiv";
        options.Filters.ScaleMode=ScaleMode.Prozent;options.Filters.Percent=50;
        options.Filters.PadEnabled=true;options.Filters.PadWidth=1000;options.Filters.PadHeight=600;options.Filters.FrameRate=30;
        options.FileNameTimestamp=Time;
        var naming=new FileNamingSettings{Enabled=true,IncludeDate=true,IncludeTime=true,IncludeEncoder=true,IncludeQuality=true,IncludeBitrate=true,IncludeSpeed=true,IncludePreset=true,IncludeFrameRate=true};
        Assert.Equal("Film__{codec} ä_2026-10-18_14-05-09_1000x600_AV1_libsvtav1_CRF24_preset-4_Archiv_30fps",FileNaming.Render(source,options,FileNaming.Pattern(naming)));
        var restored=Json.Clone(options);Assert.Equal(Time,restored.FileNameTimestamp);
        Assert.Equal(FileNaming.Render(source,options,FileNaming.Pattern(naming)),FileNaming.Render(source,restored,FileNaming.Pattern(naming)));
        Assert.Equal("2026-10-18_14-05-09_1000_600",FileNaming.Render(source,restored,"{date}_{time}_{width}_{height}"));
    }
    [Fact] public void Original_name_only_unknown_fps_and_manual_patterns_remain_supported()
    {
        var source=Source();var options=EncodingOptions.ForMedia(source);
        var naming=new FileNamingSettings{IncludeResolution=false,IncludeCodec=false};
        Assert.Equal("{name}",FileNaming.Pattern(naming));
        Assert.Equal("Film__{codec} ä",FileNaming.Render(source,options,FileNaming.Pattern(naming)));
        Assert.Equal("23.976fps",FileNaming.Render(source,options,"{fps}"));
        var unknown=source with{Streams=[new(){Type="video",Width=1920,Height=1080}]};
        Assert.Equal("Film__{codec} ä_AV1",FileNaming.Render(unknown,options,"{name}_{fps}_AV1"));
        options.FileNamePattern="{name}_{height}p_{codec}";
        var path=QueueService.SuggestedOutputPath(source,options,naming);
        Assert.Equal("Film__{codec} ä_1080p_HEVC.mkv",Path.GetFileName(path));
    }
    [Fact] public async Task Naming_settings_are_opt_in_and_persist_all_choices()
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-naming-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var service=new SettingsService(paths,new(paths));await service.LoadAsync();
            Assert.False(service.Current.FileNaming.Enabled);
            service.Current.FileNaming=new(){Enabled=true,IncludeQuality=true,IncludeBitrate=true,IncludeDate=true,IncludeTime=true,IncludeEncoder=true,IncludeSpeed=true,IncludePreset=true,IncludeFrameRate=true};
            await service.SaveAsync();var restored=new SettingsService(paths,new(paths));await restored.LoadAsync();
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(service.Current.FileNaming),System.Text.Json.JsonSerializer.Serialize(restored.Current.FileNaming));
            await File.WriteAllTextAsync(Path.Combine(root,"settings.json"),"{\"FileNaming\":null}");await restored.LoadAsync();
            Assert.NotNull(restored.Current.FileNaming);Assert.False(restored.Current.FileNaming.Enabled);
        }
        finally{Clean(root);}
    }
    [Fact] public async Task Two_real_svt_jobs_keep_different_settings_and_unique_outputs_after_naming_changes()
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var dataVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await dataVersions.LoadAsync();
        var tools=dataVersions.Active??throw new InvalidOperationException("Test FFmpeg missing");
        var source=(await new QueueStore(new AppPaths(dataRoot)).LoadJobsAsync()).First(j=>j.Status==JobStatus.Abgeschlossen&&File.Exists(j.Source.Path)).Source;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-naming-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var log=new LoggingService(paths);var settings=new SettingsService(paths,log);await settings.LoadAsync();
            settings.Current.ParallelJobs=2;settings.Current.FileNaming.Enabled=true;
            var versions=new FFmpegVersionService(paths,runner);await versions.ActivateAsync(tools);
            var store=new QueueStore(paths);var caps=new FFmpegCapabilityService(runner);var probe=new MediaProbeService(runner);
            await using var queue=new QueueService(paths,store,settings,versions,caps,new HardwareDetectionService(runner),new FFmpegCommandBuilder(),runner,probe,log);
            await queue.InitializeAsync();
            var options=EncodingOptions.ForMedia(source);options.OutputDirectory=Path.Combine(root,"output");options.FileNamePattern="custom";options.ClipDuration=1;
            options.Video.Encoder="libsvtav1";options.Video.Preset="12";options.Video.Quality=24;options.Video.SvtParallelism="1";
            options.Filters.ScaleMode=ScaleMode.Benutzerdefiniert;options.Filters.Width=128;options.Filters.Height=128;
            options.Audio.Clear();options.Subtitles.Clear();var other=Json.Clone(options);other.Video.Quality=32;
            var pair=await Task.WhenAll(queue.AddAsync(source,options),queue.AddAsync(source,other));
            Assert.NotEqual(pair[0].OutputPath,pair[1].OutputPath);
            Assert.Contains(pair,j=>Path.GetFileNameWithoutExtension(j.OutputPath).EndsWith("_2"));
            Assert.DoesNotContain(pair,j=>Path.GetFileName(j.OutputPath).Contains("custom"));
            Assert.Equal(24,pair[0].Options.Video.Quality);Assert.Equal(32,pair[1].Options.Video.Quality);
            var names=pair.Select(j=>j.OutputPath).ToArray();settings.Current.FileNaming.IncludeQuality=true;
            Assert.Equal(names,pair.Select(j=>j.OutputPath));Assert.Equal("custom",options.FileNamePattern);
            Assert.Equal(2,queue.StartMany(pair));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
            while(pair.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(50,timeout.Token);
            Assert.All(pair,j=>{Assert.Equal(JobStatus.Abgeschlossen,j.Status);Assert.True(File.Exists(j.OutputPath));Assert.NotNull(j.Options.FileNameTimestamp);});
            Assert.NotEqual(pair[0].Scheduling?.ProcessId,pair[1].Scheduling?.ProcessId);
            var restored=(await store.LoadJobsAsync()).OrderBy(j=>j.Options.Video.Quality).ToList();Assert.Equal(names,restored.Select(j=>j.OutputPath));
            Assert.Equal(new double[]{24,32},restored.Select(j=>j.Options.Video.Quality));
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"file-naming-queue.json"),restored);
        }
        finally{SqliteConnection.ClearAllPools();Clean(root);}
    }
    private static void Clean(string root)
    {
        if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-naming-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
        if(Directory.Exists(root))Directory.Delete(root,true);
    }
}
