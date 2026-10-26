using System.Security.Cryptography;
using System.Text.Json;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class EncodeComparisonTests
{
    [Fact] public void Coverage_uses_source_time_for_different_cuts_clips_and_disabled_cuts()
    {
        var a=new EncodingOptions{ClipStart=1,ClipDuration=8};
        a.Cuts.Add(new(new(2,2),new(4,4)));a.Cuts.Add(new(new(3,3),new(5,5)));a.Cuts.Add(new(new(7,7),null));
        var coverage=ComparisonTimeline.Coverage(12,a,10,4);
        Assert.Equal(new[]{new ComparisonInterval(0,1,ComparisonAvailability.Unavailable),new(1,2,ComparisonAvailability.Available),new(2,5,ComparisonAvailability.Unavailable),new(5,7,ComparisonAvailability.Available),new(7,12,ComparisonAvailability.Unavailable)},coverage);
        var b=new EncodingOptions();b.Cuts.Add(new(new(5,5),new(6,6)));
        var other=ComparisonTimeline.Coverage(12,b,10,9);
        foreach(var t in new[]{0d,1,2,3,4,5,6,7,8,9,10,11})
        {
            Assert.Equal(t<10&&ComparisonTimeline.OutputTime(a,t) is not null,coverage.Single(i=>t>=i.Start&&t<i.End).Availability==ComparisonAvailability.Available);
            Assert.Equal(t<10&&ComparisonTimeline.OutputTime(b,t) is not null,other.Single(i=>t>=i.Start&&t<i.End).Availability==ComparisonAvailability.Available);
        }
        a.CutsEnabled=false;
        Assert.Equal(new[]{new ComparisonInterval(0,1,ComparisonAvailability.Unavailable),new(1,9,ComparisonAvailability.Available),new(9,12,ComparisonAvailability.Unavailable)},ComparisonTimeline.Coverage(12,a,10,8));
        a.Filters.CustomChain="setpts=2*PTS";Assert.Equal(new ComparisonInterval(0,12,ComparisonAvailability.Unknown),Assert.Single(ComparisonTimeline.Coverage(12,a,10,16)));
    }
    [Fact] public void External_coverage_marks_unknown_cuts_and_respects_offsets_and_file_bounds()
    {
        Assert.Equal(new[]{new ComparisonInterval(0,2,ComparisonAvailability.Unavailable),new(2,6,ComparisonAvailability.Unknown),new(6,10,ComparisonAvailability.Unavailable)},ComparisonTimeline.Coverage(10,null,null,4,2));
        Assert.Equal(new[]{new ComparisonInterval(0,2,ComparisonAvailability.Unknown),new(2,10,ComparisonAvailability.Unavailable)},ComparisonTimeline.Coverage(10,null,null,4,-2));
        Assert.Equal(new ComparisonInterval(0,10,ComparisonAvailability.Unavailable),Assert.Single(ComparisonTimeline.Coverage(10,null,null,4,12)));
        Assert.Equal(new ComparisonInterval(0,10,ComparisonAvailability.Unknown),Assert.Single(ComparisonTimeline.Coverage(10,null,null,null)));
        Assert.Empty(ComparisonTimeline.Coverage(double.NaN,null,null,4));
    }
    [Fact] public void Source_to_output_mapping_excludes_cuts_and_clip_boundaries()
    {
        var o=new EncodingOptions{ClipStart=1,ClipDuration=7};
        o.Cuts.Add(new(new(2,2),new(4,4)));o.Cuts.Add(new(new(6,6),null));
        Assert.Null(ComparisonTimeline.OutputTime(o,.9));Assert.Equal(0d,ComparisonTimeline.OutputTime(o,1));
        Assert.Null(ComparisonTimeline.OutputTime(o,2));Assert.Null(ComparisonTimeline.OutputTime(o,3.9));
        Assert.Equal(1d,ComparisonTimeline.OutputTime(o,4));Assert.Equal(2d,ComparisonTimeline.OutputTime(o,5));Assert.Null(ComparisonTimeline.OutputTime(o,6));
        foreach(var t in new[]{1d,1.5,4,4.5,5.9})Assert.Equal(t,CutTimeline.SourceTime(o,ComparisonTimeline.OutputTime(o,t)!.Value),6);
        o.CutsEnabled=false;Assert.Equal(6d,ComparisonTimeline.OutputTime(o,7));Assert.Null(ComparisonTimeline.OutputTime(o,8));
        Assert.Null(ComparisonTimeline.OutputTime(o,double.NaN));
    }
    [Theory][InlineData("full")][InlineData("clip")][InlineData("cut")][InlineData("vfr-offset")]
    public async Task Two_real_qp_encodes_and_original_select_the_same_source_frame(string mode)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var sourceVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await sourceVersions.LoadAsync();var tools=sourceVersions.Active!;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-encode-compare-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();
            var probe=new MediaProbeService(runner);var navigation=new FrameNavigationService(runner);var preview=new PreviewService(paths,runner,settings);var service=new EncodeComparisonService(navigation,preview);
            var source=Path.Combine(root,mode=="vfr-offset"?"original.ts":"original.mkv");
            var args=new List<string>{"-v","error","-f","lavfi","-i",mode=="vfr-offset"?"testsrc2=size=160x90:rate=10:duration=2":"testsrc2=size=160x90:rate=60:duration=1.2","-f","lavfi","-i","sine=frequency=440:duration=3"};
            if(mode=="vfr-offset")args.AddRange(["-vf","setpts=if(lt(N\\,10)\\,N\\,10+(N-10)*2)","-fps_mode","vfr","-c:v","libx264","-qp","0","-output_ts_offset","5","-c:a","aac"]);
            else args.AddRange(["-c:v","ffv1","-c:a","flac","-t","1.2"]);
            args.Add(source);(await runner.RunAsync(tools.FFmpegPath,args)).EnsureSuccess("Comparison source");
            var media=await probe.ProbeAsync(tools,source);var inputs=new List<EncodeComparisonInput>();
            foreach(var qp in new[]{40,50})
            {
                var options=EncodingOptions.ForMedia(media);options.Video.Encoder="libx264";options.Video.Preset="ultrafast";options.Video.RateControl=RateControl.QP;options.Video.Quality=qp;options.Video.PixelFormat="yuv420p";
                foreach(var audio in options.Audio){audio.Action=TrackAction.Encodieren;audio.Encoder=qp==40?"aac":"libopus";audio.BitRateKbps=96;}
                if(mode=="clip"){options.ClipStart=.201;options.ClipDuration=.809;}
                if(mode=="cut")options.Cuts.Add(new(await navigation.ResolveAsync(tools,media,.2),await navigation.ResolveAsync(tools,media,.4)));
                var output=Path.Combine(root,"QP"+qp+".mkv");
                (await runner.RunAsync(tools.FFmpegPath,new FFmpegCommandBuilder().Build(media,options,output))).EnsureSuccess("Comparison encode");
                var info=await probe.ProbeAsync(tools,output);inputs.Add(EncodeComparisonInput.FromJob(new(){Source=media,Options=options,OutputPath=output,OutputInfo=info,Tools=tools,Status=JobStatus.Abgeschlossen}));
            }
            var requested=mode=="vfr-offset"?1.6:.6;
            var result=await service.LoadAsync(media,inputs[0],inputs[1],requested,true);
            Assert.NotNull(result.Original!.Path);
            var sequential=await runner.RunAsync(tools.FFprobePath,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",source]);
            sequential.EnsureSuccess("Sequential source timestamps");using var doc=JsonDocument.Parse(sequential.StandardOutput);
            var timestamps=doc.RootElement.GetProperty("frames").EnumerateArray().Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture)-media.StartTime).ToArray();
            Assert.Equal(timestamps.MinBy(t=>Math.Abs(t-requested)),result.SourceSeconds,5);
            foreach(var (input,picture) in inputs.Zip(new[]{result.A,result.B}))
            {
                Assert.NotNull(picture.Path);Assert.InRange(Math.Abs(picture.SourceSeconds!.Value-result.SourceSeconds),0,.009);
                var retained=timestamps.Where(t=>ComparisonTimeline.OutputTime(input.Job!.Options,t) is not null).ToArray();
                var index=Array.FindIndex(retained,t=>Math.Abs(t-result.SourceSeconds)<.001);Assert.True(index>=0);
                var expected=Path.Combine(root,Path.GetFileNameWithoutExtension(input.Media.Path)+"-sequential.png");
                (await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",input.Media.Path,"-vf",$"select=eq(n\\,{index}),format=rgb24","-fps_mode","passthrough","-frames:v","1","-threads","1",expected])).EnsureSuccess("Sequential comparison reference");
                Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(expected)),SHA256.HashData(await File.ReadAllBytesAsync(picture.Path!)));
            }
            Assert.NotEqual(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(result.A.Path!))),Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(result.B.Path!))));
            var single=await service.LoadAsync(media,inputs[0],null,requested,true,matchReference:true);
            Assert.NotNull(single.A.Path);Assert.NotNull(single.Original!.Path);Assert.Null(single.B.Path);Assert.Null(single.B.Frame);
            Assert.Equal(result.SourceSeconds,single.SourceSeconds,6);Assert.Equal(result.A.Frame,single.A.Frame);
            if(mode=="cut")
            {var excluded=await service.LoadAsync(media,inputs[0],inputs[1],.3,true);Assert.Null(excluded.A.Path);Assert.Null(excluded.B.Path);Assert.NotNull(excluded.Original!.Path);}
            if(mode=="clip")
            {
                var initial=await service.LoadAsync(media,inputs[0],null,.201,true,matchReference:true);
                Assert.NotNull(initial.A.Path);Assert.NotNull(initial.Original!.Path);Assert.InRange(initial.SourceSeconds,.201,.23);
                var end=await service.LoadAsync(media,inputs[0],null,1.0099,true,matchReference:true);
                Assert.NotNull(end.A.Path);Assert.True(end.SourceSeconds<1.01);
                var excluded=await service.LoadAsync(media,inputs[0],inputs[1],.1,false);Assert.Null(excluded.A.Path);
                var external=await service.LoadAsync(null,new(inputs[0].Media,tools),new(inputs[1].Media,tools),.4,false,0,.1);
                Assert.Null(external.Original);Assert.NotNull(external.A.Path);Assert.NotNull(external.B.Path);
                var beyondA=await service.LoadAsync(null,new(inputs[0].Media,tools),new(inputs[1].Media,tools),1.3,false,0,1);
                Assert.Null(beyondA.A.Path);Assert.NotNull(beyondA.B.Path);Assert.InRange(beyondA.SourceSeconds,1.28,1.32);
                var gap=await service.LoadAsync(null,new(inputs[0].Media,tools),new(inputs[1].Media,tools),.9,false,0,1);
                Assert.Null(gap.A.Path);Assert.Null(gap.B.Path);Assert.Equal(.9,gap.SourceSeconds,6);
            }
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"comparison-"+mode+".json"),new{mode,result.SourceSeconds,result.A,result.B,result.Original});
        }
        finally
        {
            var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("ffstudio-encode-compare-"))throw new InvalidOperationException("Unexpected test directory");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [Fact] public async Task Single_encode_reference_matches_crop_scale_and_padding_without_changing_the_raw_original()
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var versions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await versions.LoadAsync();var tools=versions.Active!;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-single-reference-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();
            var probe=new MediaProbeService(runner);var preview=new PreviewService(paths,runner,settings);
            var source=Path.Combine(root,"source.mkv");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-f","lavfi","-i","testsrc2=size=160x90:rate=20:duration=2","-c:v","ffv1",source])).EnsureSuccess("Single comparison source");
            var media=await probe.ProbeAsync(tools,source);var options=EncodingOptions.ForMedia(media);
            options.Video.Encoder="libx264";options.Video.Preset="ultrafast";options.Video.PixelFormat="yuv420p";
            options.Filters.CropEnabled=true;options.Filters.CropLeft=8;options.Filters.CropRight=4;options.Filters.CropTop=4;options.Filters.CropBottom=4;
            options.Filters.ScaleMode=ScaleMode.Prozent;options.Filters.Percent=50;options.Filters.PadEnabled=true;options.Filters.PadWidth=100;options.Filters.PadHeight=60;
            options.ClipStart=.2;options.ClipDuration=1;
            var output=Path.Combine(root,"filtered.mkv");(await runner.RunAsync(tools.FFmpegPath,new FFmpegCommandBuilder().Build(media,options,output))).EnsureSuccess("Filtered comparison encode");
            var info=await probe.ProbeAsync(tools,output);var input=EncodeComparisonInput.FromJob(new(){Source=media,OutputInfo=info,OutputPath=output,Options=options,Tools=tools,Status=JobStatus.Abgeschlossen});
            var service=new EncodeComparisonService(new(runner),preview);
            var matched=await service.LoadAsync(media,input,null,.6,true,matchReference:true);
            var raw=await service.LoadAsync(media,input,null,.6,true);
            Assert.NotNull(matched.Original!.Path);Assert.NotNull(matched.A.Path);Assert.Null(matched.B.Path);
            // PNG IHDR uses big-endian dimensions. The original and matched reference must remain separate cache entries.
            static (int Width,int Height) Size(byte[] png)=>(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)),System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4)));
            Assert.Equal((100,60),Size(await File.ReadAllBytesAsync(matched.Original.Path!)));
            Assert.Equal((100,60),Size(await File.ReadAllBytesAsync(matched.A.Path!)));
            Assert.Equal((160,90),Size(await File.ReadAllBytesAsync(raw.Original!.Path!)));
            Assert.NotEqual(raw.Original.Path,matched.Original.Path);Assert.Equal(matched.SourceSeconds,raw.SourceSeconds);
            Assert.InRange(Math.Abs(matched.A.SourceSeconds!.Value-matched.SourceSeconds),0,.001);
        }
        finally
        {
            var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("ffstudio-single-reference-"))throw new InvalidOperationException("Unexpected test directory");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
