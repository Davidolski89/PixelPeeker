using System.Security.Cryptography;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class FrameNavigationTests
{
    private static readonly SourceFramePosition[] Frames=[new(0,0),new(.04,4),new(.1,10),new(.3,30)];
    [Theory]
    [InlineData(.04,FrameSeek.Previous,0)][InlineData(.04,FrameSeek.Next,.1)]
    [InlineData(.08,FrameSeek.Nearest,.1)][InlineData(.11,FrameSeek.Next,.3)]
    [InlineData(0,FrameSeek.Previous,0)][InlineData(.3,FrameSeek.Next,.3)]
    public void Frame_steps_follow_actual_timestamps_and_stop_at_the_ends(double requested,FrameSeek direction,double expected)
    {Assert.Equal(expected,FrameNavigation.Select(Frames,requested,direction).Seconds);}
    [Theory][InlineData("cfr")][InlineData("vfr")][InlineData("offset")][InlineData("gop")]
    public async Task Actual_frame_navigation_and_extracted_images_match_sequential_decoding(string mode)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var runner=new ProcessRunner();var versions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await versions.LoadAsync();
        var tools=versions.Active??throw new InvalidOperationException("Test FFmpeg missing");
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-frame-nav-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();
            var input=Path.Combine(root,"source."+(mode=="offset"?"ts":"mp4"));
            var longSource=mode is "offset" or "gop";
            var arguments=new List<string>{"-v","error","-f","lavfi","-i","testsrc2=size=96x64:rate=10:duration="+(longSource?"8":"2")};
            if(mode=="vfr")arguments.AddRange(["-vf","setpts=if(lt(N\\,10)\\,N\\,10+(N-10)*2)","-fps_mode","vfr"]);
            arguments.AddRange(["-c:v","libx264","-preset","fast","-g",longSource?"20":"100","-bf","3","-crf","18"]);
            if(mode=="offset")arguments.AddRange(["-output_ts_offset","5"]);
            arguments.Add(input);(await runner.RunAsync(tools.FFmpegPath,arguments)).EnsureSuccess("Generate navigation source");
            var media=await new MediaProbeService(runner).ProbeAsync(tools,input);
            if(mode=="offset")Assert.True(media.StartTime>4);
            Assert.False(string.IsNullOrEmpty(media.Video!.TimeBase));
            var navigation=new FrameNavigationService(runner);var preview=new PreviewService(paths,runner,settings);
            var results=new List<object>();
            foreach(var n in longSource?new[]{0,1,19,20,49,79}:new[]{0,1,9,10,11,19})
            {
                var expectedSeconds=mode=="vfr"&&n>=10?1+(n-10)/5d:n/10d;
                var position=await navigation.ResolveAsync(tools,media,expectedSeconds);
                Assert.Equal(expectedSeconds,position.Seconds,5);
                var images=await Task.WhenAll(Enumerable.Range(0,n==0?2:1).Select(_=>preview.FrameAsync(tools,media,position.Seconds,exactFrame:position)));
                Assert.All(images,image=>Assert.Equal(images[0],image));var actual=images[0];
                var expected=Path.Combine(root,"expected-"+n+".png");
                (await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",input,"-vf",$"select=eq(n\\,{n}),format=rgb24","-fps_mode","passthrough","-frames:v","1","-threads","1",expected])).EnsureSuccess("Reference frame");
                var hash=SHA256.HashData(await File.ReadAllBytesAsync(actual));
                Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(expected)),hash);
                if(n<(longSource?79:19))
                {
                    var next=await navigation.ResolveAsync(tools,media,position.Seconds,FrameSeek.Next);
                    var expectedNext=mode=="vfr"&&n+1>=10?1+(n+1-10)/5d:(n+1)/10d;
                    Assert.Equal(expectedNext,next.Seconds,5);
                    Assert.Equal(position,await navigation.ResolveAsync(tools,media,next.Seconds,FrameSeek.Previous));
                }
                results.Add(new{Frame=n,position.Seconds,position.Timestamp,Hash=Convert.ToHexString(hash)});
            }
            var last=await navigation.ResolveAsync(tools,media,media.Duration!.Value);
            Assert.Equal(last,await navigation.ResolveAsync(tools,media,last.Seconds,FrameSeek.Next));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>navigation.ResolveAsync(tools,media,0,ct:cancelled.Token));
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"frame-navigation-"+mode+".json"),new{mode,media.StartTime,media.Video.TimeBase,Frames=results});
        }
        finally
        {
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-frame-nav-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected temporary path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
