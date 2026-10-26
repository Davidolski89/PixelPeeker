using System.Diagnostics;
using System.Security.Cryptography;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class AccuratePreviewTests
{
    [Theory]
    [InlineData("cfr")][InlineData("vfr")][InlineData("offset")][InlineData("gop")][InlineData("av1")]
    public async Task Buffered_seeking_matches_independent_sequential_pixels_and_reuses_the_decoder(string mode)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var versions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await versions.LoadAsync();var tools=versions.Active!;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-buffered-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var navigation=new FrameNavigationService(runner);
        await using var service=new AccuratePreviewService(navigation);
        try
        {
            var input=Path.Combine(root,"Quelle 'Ä' & Frames."+(mode=="offset"?"ts":"mkv"));
            var args=new List<string>{"-v","error","-f","lavfi","-i","testsrc2=size=94x66:rate=10:duration=6"};
            if(mode=="vfr")args.AddRange(["-vf","setpts=if(lt(N\\,10)\\,N\\,10+(N-10)*2)","-fps_mode","vfr"]);
            args.AddRange(["-c:v",mode=="av1"?"libsvtav1":"libx264","-preset",mode=="av1"?"13":"fast","-g",mode=="gop"?"600":"20"]);
            if(mode=="av1")args.AddRange(["-crf","35","-svtav1-params","lp=2"]);else args.AddRange(["-bf","3","-crf","18"]);
            if(mode=="offset")args.AddRange(["-output_ts_offset","5"]);
            args.Add(input);(await runner.RunAsync(tools.FFmpegPath,args)).EnsureSuccess("Buffered fixture");
            var media=await new MediaProbeService(runner).ProbeAsync(tools,input);
            var reference=Path.Combine(root,"sequential.raw");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",input,"-an","-sn","-vf","format=bgr24","-fps_mode","passthrough","-f","rawvideo",reference])).EnsureSuccess("Independent sequential reference");
            var expected=await File.ReadAllBytesAsync(reference);Assert.Equal(94*66*3*60,expected.Length);
            var timings=new List<object>();
            double Time(int n)=>mode=="vfr"&&n>=10?1+(n-10)/5d:n/10d;
            async Task Check(int n)
            {
                var position=await navigation.ResolveAsync(tools,media,Time(n));var watch=Stopwatch.StartNew();
                var actual=await service.FrameAsync(tools,media,position).WaitAsync(TimeSpan.FromSeconds(15));watch.Stop();
                Assert.Equal(position.Timestamp,actual.Timestamp);Assert.Equal(94,actual.Width);Assert.Equal(66,actual.Height);
                var packed=new byte[94*66*3];for(int y=0;y<66;y++)actual.Pixels.AsSpan(y*actual.Stride,94*3).CopyTo(packed.AsSpan(y*94*3));
                Assert.Equal(SHA256.HashData(expected.AsSpan(n*packed.Length,packed.Length)),SHA256.HashData(packed));
                timings.Add(new{Frame=n,Milliseconds=watch.Elapsed.TotalMilliseconds,Statistics=service.Statistics});
            }
            await Check(10);var starts=service.Statistics.DecoderStarts;
            await Check(11);await Check(12);await Check(11);await Check(10);await Check(9);
            Assert.Equal(starts,service.Statistics.DecoderStarts);Assert.True(service.Statistics.CacheHits>0);
            await Check(45);Assert.True(service.Statistics.DecoderStarts>starts);await Check(46);await Check(59);await Check(0);
            var frame=await navigation.ResolveAsync(tools,media,1);
            var filtered=await service.FrameAsync(tools,media,frame,"hflip",width:46);
            Assert.Equal(46,filtered.Width);Assert.NotEqual(94,filtered.Width);
            var filteredReference=Path.Combine(root,"filtered.raw");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",input,"-vf","hflip,scale=46:-2:flags=lanczos,setsar=1,format=bgr24","-an","-sn","-fps_mode","passthrough","-f","rawvideo",filteredReference])).EnsureSuccess("Filtered reference");
            var referencePixels=await File.ReadAllBytesAsync(filteredReference);var packedFilter=new byte[filtered.Width*filtered.Height*3];
            for(int y=0;y<filtered.Height;y++)filtered.Pixels.AsSpan(y*filtered.Stride,filtered.Width*3).CopyTo(packedFilter.AsSpan(y*filtered.Width*3));
            Assert.Equal(SHA256.HashData(referencePixels.AsSpan(10*packedFilter.Length,packedFilter.Length)),SHA256.HashData(packedFilter));
            Assert.InRange(service.Statistics.BufferedBytes,1,AccuratePreviewService.MemoryLimit);
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.FrameAsync(tools,media,frame,ct:cancelled.Token));
            service.Reset();Assert.Equal(0,service.Statistics.BufferedBytes);
            // A cancelled/replaced request must not poison the next decode or keep the source locked.
            var race=service.FrameAsync(tools,media,frame);service.Reset();
            try{await race;}catch(OperationCanceledException){}
            await Check(12);await service.DisposeAsync();Assert.False(service.Statistics.DecoderRunning);
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"buffered-preview-"+mode+".json"),new{mode,Timings=timings});
        }
        finally
        {
            await service.DisposeAsync();
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-buffered-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
            Directory.Delete(root,true);
        }
    }

    [Fact]
    public async Task Realistic_long_gop_preview_records_cold_and_adjacent_frame_latency()
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-buffered-benchmark-"+Guid.NewGuid());var paths=new AppPaths(root);
        var runner=new ProcessRunner();var versions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await versions.LoadAsync();var tools=versions.Active!;
        var navigation=new FrameNavigationService(runner);await using var service=new AccuratePreviewService(navigation);
        try
        {
            var source=Path.Combine(root,"AV1-long-gop.mkv");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-f","lavfi","-i","testsrc2=size=1280x720:rate=30:duration=5",
                "-c:v","libsvtav1","-preset","12","-crf","35","-g","300","-svtav1-params","lp=4",source])).EnsureSuccess("Preview benchmark fixture");
            var media=await new MediaProbeService(runner).ProbeAsync(tools,source);var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();var legacy=new PreviewService(paths,runner,settings);
            var positions=new List<SourceFramePosition>();for(int n=60;n<72;n++)positions.Add(await navigation.ResolveAsync(tools,media,n/30d));
            var oldTimings=new List<double>();var newTimings=new List<double>();
            foreach(var frame in positions){var watch=Stopwatch.StartNew();await legacy.FrameAsync(tools,media,frame.Seconds,width:1280,exactFrame:frame);oldTimings.Add(watch.Elapsed.TotalMilliseconds);}
            foreach(var frame in positions){var watch=Stopwatch.StartNew();await service.FrameAsync(tools,media,frame,width:1280);newTimings.Add(watch.Elapsed.TotalMilliseconds);}
            await Task.Delay(200);var settled=service.Statistics.DecodedFrames;await Task.Delay(150);
            Assert.Equal(settled,service.Statistics.DecodedFrames);Assert.InRange(settled,12,30);
            Assert.Equal(1,service.Statistics.DecoderStarts);Assert.InRange(service.Statistics.BufferedBytes,1,AccuratePreviewService.MemoryLimit);
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"preview-benchmark.json"),new{Source="Synthetic AV1, 1280x720, 30fps, GOP300",LegacyMilliseconds=oldTimings,BufferedMilliseconds=newTimings,Statistics=service.Statistics});
        }
        finally
        {
            await service.DisposeAsync();
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-buffered-benchmark-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
            Directory.Delete(root,true);
        }
    }
}
