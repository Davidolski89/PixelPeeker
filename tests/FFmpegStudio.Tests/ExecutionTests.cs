using System.Diagnostics;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class ExecutionTests
{
    private static MediaInfo Media() => new() { Path=Path.GetFullPath("thread-source.mkv"),Duration=4,Streams=[new(){Index=0,Type="video",Codec="h264",Width=128,Height=128,SampleAspectRatio="1:1"}] };
    [Theory]
    [InlineData("libx264")][InlineData("libx264rgb")][InlineData("libopenh264")]
    [InlineData("libaom-av1")][InlineData("librav1e")][InlineData("libvvenc")][InlineData("libx265")]
    public void Software_thread_budgets_use_the_encoder_control_and_preserve_other_parameters(string encoder)
    {
        var source=Media();var options=EncodingOptions.ForMedia(source);var video=options.Video;
        video.Encoder=encoder;video.Preset=encoder is "libaom-av1" or "librav1e"?"6":encoder=="libopenh264"?"":"medium";
        var builder=new FFmpegCommandBuilder();var output=Path.GetFullPath("thread-result.mkv");
        Assert.DoesNotContain("-threads:v:0",builder.Build(source,options,output));
        video.ThreadMode=ThreadControlMode.FesteAnzahl;video.ThreadCount=1;
        if(encoder=="libx265")video.Extra["x265-params"]="aq-mode=2:frame-threads=1";
        var original=Json.Clone(video.Extra);
        var restored=Json.Clone(new Preset{Options=options}).Options;
        var args=builder.Build(source,restored,output).ToList();
        if(encoder=="libx265")
        {
            Assert.DoesNotContain("-threads:v:0",args);Assert.Equal(1,args.Count(a=>a=="-x265-params"));
            Assert.Equal("aq-mode=2:frame-threads=1:pools=1",args[args.IndexOf("-x265-params")+1]);
            Assert.Equal(original["x265-params"],restored.Video.Extra["x265-params"]);
        }
        else Assert.Equal("1",args[args.IndexOf("-threads:v:0")+1]);
        if(encoder=="libaom-av1")Assert.Equal("1",args[args.IndexOf("-row-mt")+1]);
        video.ThreadMode=ThreadControlMode.CPUAnteil;video.ThreadPercent=25;
        Assert.Equal(4,SoftwareThreading.ResolveCount(video,16));Assert.Equal(1,SoftwareThreading.ResolveCount(video,1));
        video.ThreadPercent=100;Assert.Equal(16,SoftwareThreading.ResolveCount(video,16));
        video.ThreadMode=ThreadControlMode.Automatisch;Assert.Equal(0,SoftwareThreading.ResolveCount(video,16));
    }
    [Fact] public void Invalid_or_conflicting_thread_settings_are_rejected()
    {
        var video=new VideoOptions{Encoder="libx265",ThreadMode=ThreadControlMode.FesteAnzahl,ThreadCount=0};
        Assert.NotEmpty(SoftwareThreading.Validate(video));video.ThreadCount=Environment.ProcessorCount+1;Assert.NotEmpty(SoftwareThreading.Validate(video));
        video.ThreadCount=1;video.Extra["x265-params"]="pools=2";Assert.NotEmpty(SoftwareThreading.Validate(video));
        video.Extra.Clear();Assert.Empty(SoftwareThreading.Validate(video));
        video.ThreadMode=ThreadControlMode.CPUAnteil;video.ThreadPercent=0;Assert.NotEmpty(SoftwareThreading.Validate(video));
        video.ThreadPercent=101;Assert.NotEmpty(SoftwareThreading.Validate(video));
        video.ThreadPercent=50;video.Encoder="hevc_nvenc";Assert.NotEmpty(SoftwareThreading.Validate(video));
        video.ThreadMode=ThreadControlMode.Automatisch;Assert.Empty(SoftwareThreading.Validate(video));
        video.Encoder="libsvtav1";video.SvtParallelism="6";Assert.Empty(SvtAv1Settings.Validate(video));
        Assert.Equal("lp=6",video.SvtAv1Parameters);video.SvtParallelism="7";Assert.NotEmpty(SvtAv1Settings.Validate(video));
        video.SvtParallelism="0";Assert.Equal("0",Json.Clone(video).SvtParallelism);
    }
    [Fact] public async Task Windows_efficiency_is_applied_and_verified_on_the_owned_child_process()
    {
        if(!OperatingSystem.IsWindows())return;
        ProcessSchedulingInfo? applied=null;
        var runner=new ProcessRunner();
        var result=await runner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),
            ["/d","/c","ping -n 2 127.0.0.1 > nul"],efficiencyMode:true,schedulingApplied:info=>
            {
                applied=info;using var child=Process.GetProcessById(info.ProcessId);
                Assert.Equal(ProcessPriorityClass.Idle,child.PriorityClass);
            });
        Assert.Equal(0,result.ExitCode);Assert.NotNull(applied);Assert.True(applied.Requested);Assert.True(applied.IdlePriority);
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))Assert.True(applied.EcoQos,$"EcoQoS rejected: {applied.ErrorCode}");
        var restored=Json.Clone(new EncodingJob{Scheduling=applied});Assert.Equal(applied,restored.Scheduling);
        Assert.True(Json.Clone(new AppSettings()).EfficiencyMode);
    }
    [Fact] public async Task Real_queue_starts_selected_jobs_together_respects_limits_and_keeps_unselected_jobs_waiting()
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var dataVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await dataVersions.LoadAsync();
        var tools=dataVersions.Active??throw new InvalidOperationException("Test toolchain missing");
        var source=(await new QueueStore(new AppPaths(dataRoot)).LoadJobsAsync())
            .First(j=>j.Status==JobStatus.Abgeschlossen&&File.Exists(j.Source.Path)).Source.Path;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-parallel-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var log=new LoggingService(paths);var settings=new SettingsService(paths,log);
            await settings.LoadAsync();settings.Current.CheckUpdates=false;settings.Current.ParallelJobs=2;
            var versions=new FFmpegVersionService(paths,runner);await versions.ActivateAsync(tools);
            var store=new QueueStore(paths);var caps=new FFmpegCapabilityService(runner);var probe=new MediaProbeService(runner);
            var media=await probe.ProbeAsync(tools,source);
            await using var queue=new QueueService(paths,store,settings,versions,caps,new HardwareDetectionService(runner),new FFmpegCommandBuilder(),runner,probe,log);
            await queue.InitializeAsync();
            var options=EncodingOptions.ForMedia(media);options.OutputDirectory=Path.Combine(root,"output");
            options.Video.Encoder="libx264";options.Video.Preset="veryslow";options.Video.ThreadMode=ThreadControlMode.FesteAnzahl;options.Video.ThreadCount=1;
            options.Audio.Clear();options.Subtitles.Clear();
            var first=await queue.AddAsync(media,options);var second=await queue.AddAsync(media,options);var unselected=await queue.AddAsync(media,options);
            Assert.Equal(2,queue.StartMany([first,second,first]));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while(first.Scheduling is null||second.Scheduling is null)await Task.Delay(20,timeout.Token);
            using(var a=Process.GetProcessById(first.Scheduling.ProcessId))
            using(var b=Process.GetProcessById(second.Scheduling.ProcessId))
            {Assert.False(a.HasExited);Assert.False(b.HasExited);Assert.NotEqual(a.Id,b.Id);}
            Assert.Equal(JobStatus.Wartend,unselected.Status);Assert.Null(unselected.Scheduling);
            settings.Current.ParallelJobs=1;
            while(first.Status==JobStatus.Läuft||second.Status==JobStatus.Läuft)await Task.Delay(50,timeout.Token);
            Assert.Equal(JobStatus.Abgeschlossen,first.Status);Assert.Equal(JobStatus.Abgeschlossen,second.Status);
            Assert.Equal(JobStatus.Wartend,unselected.Status);Assert.True(first.Scheduling.IdlePriority);Assert.True(second.Scheduling.IdlePriority);
            var fourth=await queue.AddAsync(media,options);queue.StartAll();
            var peak=0;
            while(unselected.Status is JobStatus.Wartend or JobStatus.Läuft||fourth.Status is JobStatus.Wartend or JobStatus.Läuft)
            {peak=Math.Max(peak,queue.Jobs.Count(j=>j.Status==JobStatus.Läuft));await Task.Delay(20,timeout.Token);}
            Assert.Equal(1,peak);Assert.Equal(JobStatus.Abgeschlossen,unselected.Status);Assert.Equal(JobStatus.Abgeschlossen,fourth.Status);
            var restored=await store.LoadJobsAsync();Assert.Equal(4,restored.Count);Assert.All(restored,j=>Assert.True(j.Scheduling?.Requested));
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"parallel-queue.json"),new{SimultaneousProcesses=new[]{first.Scheduling.ProcessId,second.Scheduling.ProcessId},SequentialPeak=peak,Jobs=restored});
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
                ||!Path.GetFileName(root).StartsWith("ffstudio-parallel-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary test path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
