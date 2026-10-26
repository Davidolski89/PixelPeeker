using System.Globalization;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class CutTests
{
    private static SourceFramePosition P(double s)=>new(s,(long)Math.Round(s*1000));
    [Theory][InlineData("all")][InlineData("reverse")][InlineData("timestamp")][InlineData("bitmap")][InlineData("audio-copy")][InlineData("cuda")][InlineData("custom")][InlineData("clip")]
    public void Incompatible_or_invalid_cuts_are_rejected_before_encoding(string mode)
    {
        var media=new MediaInfo{Path=Path.GetFullPath("source.mkv"),Duration=4,Streams=[new(){Index=0,Type="video",TimeBase="1/1000",Width=128,Height=96},new(){Index=1,Type="audio",Codec="aac"},new(){Index=2,Type="subtitle",Codec="hdmv_pgs_subtitle"}]};
        var o=EncodingOptions.ForMedia(media);o.Audio[0].Action=TrackAction.Encodieren;o.Subtitles[0].Action=TrackAction.Entfernen;o.Cuts.Add(new(P(1),P(2)));
        Assert.Empty(new FFmpegCommandBuilder().Validate(media,o));
        switch(mode)
        {
            case "all":o.Cuts.Clear();o.Cuts.Add(new(P(0),null));break;
            case "reverse":o.Cuts[0]=new(P(2),P(1));break;
            case "timestamp":o.Cuts[0]=new(new(1,1200),P(2));break;
            case "bitmap":o.Subtitles[0].Action=TrackAction.Kopieren;break;
            case "audio-copy":o.Audio[0].Action=TrackAction.Kopieren;break;
            case "cuda":o.Video.Encoder="hevc_nvenc";o.Video.CudaDecode=true;o.Video.CudaScale=true;break;
            case "custom":o.Filters.CustomChain="hflip";break;
            case "clip":o.ClipStart=1.1;o.ClipDuration=.4;break;
        }
        Assert.NotEmpty(new FFmpegCommandBuilder().Validate(media,o));
    }
    [Fact] public void Ranges_merge_and_clip_mapping_skips_all_removed_time()
    {
        var o=new EncodingOptions{Cuts=[new(P(1),P(2)),new(P(1.5),P(3)),new(P(4),P(5))]};
        Assert.Equal(2,CutTimeline.Merge(o.Cuts).Count);
        Assert.Equal(3,CutTimeline.Keep(o).Count);
        Assert.Equal(3,CutTimeline.SourceTime(o,1),8);Assert.Equal(5,CutTimeline.SourceTime(o,2),8);
        o.ClipStart=1.5;o.ClipDuration=4;
        Assert.Equal(new[]{new KeepSegment(3,4),new KeepSegment(5,5.5)},CutTimeline.Keep(o));
        Assert.Equal(1.5,CutTimeline.Duration(new(){Duration=10},o));
        Assert.Equal(5.2,CutTimeline.SourceTime(o,1.2),8);
        o.CutsEnabled=false;Assert.Equal(4,CutTimeline.Duration(new(){Duration=10},o));
    }
    [Fact] public void Subtitles_split_across_cuts_preserve_ass_styles_and_shift_offsets()
    {
        var o=new EncodingOptions{Cuts=[new(P(1),P(2)),new(P(3),P(4))]};
        var vtt=SubtitleCuts.Retime("1\n00:00:05,500 --> 00:00:09,500\nHello\n\n2\n00:00:06,100 --> 00:00:06,900\nad",false,o,5);
        Assert.StartsWith("WEBVTT",vtt);Assert.DoesNotContain("\nad",vtt);
        Assert.Contains("00:00:00.500 --> 00:00:01.000",vtt);Assert.Contains("00:00:02.000 --> 00:00:02.500",vtt);
        var ass="[Script Info]\nTitle: Test\n[V4+ Styles]\nStyle: KeepMe\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:05.50,0:00:09.50,KeepMe,,0,0,0,,Hello, world";
        var result=SubtitleCuts.Retime(ass,true,o,5);
        Assert.Contains("Style: KeepMe",result);Assert.Equal(3,result.Split("Dialogue:").Length-1);
        Assert.Contains("0:00:02.00,0:00:02.50,KeepMe,,0,0,0,,Hello, world",result);
    }
    [Fact] public async Task Cut_markers_belong_to_files_and_never_travel_in_presets()
    {
        var media=new MediaInfo{Path=Path.GetFullPath("source.mkv"),Streams=[new(){Index=0,Type="video",TimeBase="1/1000",Width=96,Height=64}]};
        var current=EncodingOptions.ForMedia(media);current.Cuts.Add(new(P(1),P(2)));
        var preset=new Preset{Name="test",Options=Json.Clone(current)};
        Assert.Empty(PresetService.Apply(preset,media).Cuts);
        var applied=PresetService.Apply(preset,media,current);Assert.Single(applied.Cuts);Assert.NotSame(current.Cuts,applied.Cuts);
        var file=Path.Combine(Path.GetTempPath(),"ffstudio-preset-"+Guid.NewGuid()+".json");
        try{await PresetService.ExportAsync(preset,file);Assert.Empty((await AtomicFile.ReadJsonAsync<Preset>(file))!.Options.Cuts);Assert.Single(preset.Options.Cuts);}
        finally{File.Delete(file);}
    }
    [Fact] public async Task Cut_queue_freezes_ranges_preserves_names_and_compares_against_the_cut_reference()
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} data)return;
        var runner=new ProcessRunner();var sourceVersions=new FFmpegVersionService(new AppPaths(data),runner);await sourceVersions.LoadAsync();var tools=sourceVersions.Active!;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-cut-queue-"+Guid.NewGuid());
        try
        {
            var paths=new AppPaths(root);var log=new LoggingService(paths);var settings=new SettingsService(paths,log);await settings.LoadAsync();settings.Current.ParallelJobs=2;
            var versions=new FFmpegVersionService(paths,runner);await versions.ActivateAsync(tools);
            var store=new QueueStore(paths);var caps=new FFmpegCapabilityService(runner);var probe=new MediaProbeService(runner);
            var source=Path.Combine(root,"source.mkv");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-f","lavfi","-i","testsrc2=size=128x96:rate=10:duration=3","-f","lavfi","-i","sine=duration=3:sample_rate=48000","-c:v","libx264","-crf","0","-c:a","flac",source])).EnsureSuccess("Queue fixture");
            var media=await probe.ProbeAsync(tools,source);var navigation=new FrameNavigationService(runner);
            var options=EncodingOptions.ForMedia(media);options.OutputDirectory=Path.Combine(root,"output");options.Video.Encoder="libx264";options.Video.Preset="medium";options.Video.Quality=0;options.Video.RateControl=RateControl.QP;
            options.Audio[0].Action=TrackAction.Encodieren;
            options.Cuts.Add(new(await navigation.ResolveAsync(tools,media,.5),await navigation.ResolveAsync(tools,media,1)));
            await using var queue=new QueueService(paths,store,settings,versions,caps,new HardwareDetectionService(runner),new FFmpegCommandBuilder(),runner,probe,log);await queue.InitializeAsync();
            var first=await queue.AddAsync(media,options);options.Cuts.Clear();options.Cuts.Add(new(await navigation.ResolveAsync(tools,media,1),await navigation.ResolveAsync(tools,media,2)));
            var second=await queue.AddAsync(media,options);Assert.NotEqual(first.OutputPath,second.OutputPath);Assert.Equal(.5,first.Options.Cuts[0].Start.Seconds,6);options.Cuts.Clear();
            queue.StartMany([first,second]);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            while(new[]{first,second}.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(30,timeout.Token);
            foreach(var job in new[]{first,second}){Assert.True(job.Status==JobStatus.Abgeschlossen,job.Error);Assert.Contains("select=",job.ExecutedCommand);Assert.False(Directory.Exists(job.PartialPath+".schnittdaten"));}
            Assert.InRange(first.OutputInfo!.Duration!.Value,2.5,2.52);Assert.InRange(second.OutputInfo!.Duration!.Value,2,2.02);Assert.Contains(first.OutputInfo.Streams,s=>s.Type=="audio"&&s.Codec=="opus");
            var restored=await store.LoadJobsAsync();Assert.Equal(.5,restored.Single(j=>j.Id==first.Id).Options.Cuts[0].Start.Seconds,6);
            var preview=new PreviewService(paths,runner,settings);var pair=await new FrameComparisonService(runner,preview).CompareAsync(first,.6,timeout.Token);
            Assert.Equal(1.1,pair.SourceSeconds,5);
            // Some muxers expose negative audio preroll as the format start. Video PTS stay unchanged.
            var preroll=Json.Clone(first);preroll.OutputInfo=preroll.OutputInfo! with{StartTime=-.007};
            var prerollPair=await new FrameComparisonService(runner,preview).CompareAsync(preroll,.607,timeout.Token);
            Assert.Equal(.607,prerollPair.EncodedSeconds,5);Assert.Equal(1.1,prerollPair.SourceSeconds,5);
            var analysis=await new QualityAnalysisService(paths,runner,caps,store).AnalyzeAsync(first,ct:timeout.Token);
            Assert.True(analysis.Ssim>.99999,$"Cut reference mismatch: SSIM {analysis.Ssim}");
            Assert.Equal(25,analysis.Frames.Count);
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"cut-queue.json"),new{First=new{first.Status,first.OutputPath,first.OutputInfo.Duration},Second=new{second.Status,second.OutputPath,second.OutputInfo.Duration},pair.SourceSeconds,analysis.Ssim,AnalysisFrames=analysis.Frames.Count});
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-cut-queue-"))throw new InvalidOperationException("Unexpected fixture path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    [Theory][InlineData("cfr")][InlineData("vfr")][InlineData("offset")][InlineData("silent")][InlineData("tail")][InlineData("clip")][InlineData("empty-subtitles")]
    [InlineData("svt")][InlineData("nvenc")][InlineData("ass")][InlineData("ass-offset")][InlineData("burn")][InlineData("mp4")]
    [InlineData("svt-vfr")][InlineData("nvenc-vfr")][InlineData("audio-offset")][InlineData("start")]
    public async Task Real_cut_export_keeps_exact_frames_audio_samples_and_retimed_subtitles(string mode)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} data)return;
        var runner=new ProcessRunner();var versions=new FFmpegVersionService(new AppPaths(data),runner);await versions.LoadAsync();var tools=versions.Active!;
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-cuts-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"Quelle 'Ä' & Werbung.mkv");var sub=Path.Combine(root,"source.srt");
            bool audio=mode!="silent",variable=mode.Contains("vfr"),lossy=mode.StartsWith("svt")||mode.StartsWith("nvenc");double length=variable?6:4;
            await File.WriteAllTextAsync(sub,mode=="empty-subtitles"?"1\n00:00:00,600 --> 00:00:00,800\nOnly ad\n":"1\n00:00:00,100 --> 00:00:00,400\nBefore\n\n2\n00:00:00,600 --> 00:00:00,800\nOnly ad\n\n3\n00:00:00,400 --> 00:00:03,500\nCrosses both cuts\n");
            var input=new List<string>{"-v","error","-f","lavfi","-i","testsrc2=size=256x144:rate=10:duration=4"};
            if(mode=="audio-offset")input.AddRange(["-itsoffset","0.25"]);
            if(audio)input.AddRange(["-f","lavfi","-i",$"sine=frequency=731:sample_rate=48000:duration={length}","-f","lavfi","-i",$"sine=frequency=439:sample_rate=48000:duration={length}"]);
            input.AddRange(["-i",sub,"-map","0:v"]);
            if(audio)input.AddRange(["-map","1:a","-map","2:a"]);
            input.AddRange(["-map",audio?"3:s":"1:s"]);
            if(variable)input.AddRange(["-vf","setpts=if(lt(N\\,20)\\,N\\,20+(N-20)*2)","-fps_mode","vfr"]);
            input.AddRange(["-c:v","libx264","-preset","fast","-crf","18","-g","100","-bf","3","-c:a","flac","-c:s",mode.StartsWith("ass")?"ass":"srt"]);
            if(mode is "offset" or "ass-offset")input.AddRange(["-output_ts_offset","5"]);
            if(mode=="burn")
            {
                var font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf");Assert.True(File.Exists(font));
                input.AddRange(["-attach",font,"-metadata:s:t:0","mimetype=application/x-truetype-font"]);
            }
            input.Add(source);(await runner.RunAsync(tools.FFmpegPath,input)).EnsureSuccess("Generate cut fixture");
            var media=await new MediaProbeService(runner).ProbeAsync(tools,source);var nav=new FrameNavigationService(runner);
            var a=await nav.ResolveAsync(tools,media,mode=="start"?0:.5);var b=await nav.ResolveAsync(tools,media,.9);
            var c=await nav.ResolveAsync(tools,media,2);var d=await nav.ResolveAsync(tools,media,variable?2.8:2.4);
            var o=EncodingOptions.ForMedia(media);o.Video.Encoder="libx264";o.Video.Preset="medium";o.Video.RateControl=RateControl.QP;o.Video.Quality=0;o.Video.PixelFormat="yuv420p";
            if(lossy)
            {o.Video.Encoder=mode.StartsWith("svt")?"libsvtav1":"hevc_nvenc";o.Video.Preset=mode.StartsWith("svt")?"13":"p4";o.Video.RateControl=RateControl.Qualität;o.Video.Quality=25;}
            if(mode=="burn")o.Subtitles[0].Action=TrackAction.Einbrennen;
            foreach(var track in o.Audio){track.Action=TrackAction.Encodieren;track.Encoder="flac";}
            if(mode=="mp4")
            {
                o.Container="mp4";foreach(var track in o.Audio)track.Encoder="aac";
                o.Subtitles[0].Action=TrackAction.Encodieren;o.Subtitles[0].Encoder="mov_text";
            }
            o.Cuts.Add(new(a,b));o.Cuts.Add(new(c,d));
            if(mode=="tail")o.Cuts.Add(new(await nav.ResolveAsync(tools,media,3),null));
            if(mode=="clip"){o.ClipStart=.7;o.ClipDuration=2.5;}
            var output=Path.Combine(root,"Ausgabe 'żółć' & geschnitten."+o.Container);
            await new CutPreparationService(runner).PrepareAsync(tools,media,o,output,default);
            var args=new FFmpegCommandBuilder().Build(media,o,output);
            var result=await runner.RunAsync(tools.FFmpegPath,args);result.EnsureSuccess("Cut export: "+mode);
            var probe=await new MediaProbeService(runner).ProbeAsync(tools,output);
            Assert.Equal(audio?2:0,probe.Streams.Count(s=>s.Type=="audio"));Assert.Equal(mode=="burn"?0:1,probe.Streams.Count(s=>s.Type=="subtitle"));
            // Independent sequential decode: compare retained frame pixels in order, not the filter text.
            var rawFrames=await runner.RunAsync(tools.FFprobePath,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",source]);
            var times=rawFrames.StandardOutput.Split('\n').Select(l=>double.TryParse(l.Trim().Split(',')[0],CultureInfo.InvariantCulture,out var t)?(double?)(t-media.StartTime):null).Where(t=>t.HasValue).Select(t=>t!.Value).ToArray();
            var kept=times.Select((t,n)=>(t,n)).Where(x=>x.t+1e-7>=(o.ClipStart??0)&&(!o.ClipDuration.HasValue||x.t<(o.ClipStart??0)+o.ClipDuration.Value-1e-7)&&!o.Cuts.Any(cut=>x.t>=cut.Start.Seconds-1e-7&&(cut.End is null||x.t<cut.End.Seconds-1e-7))).ToArray();
            async Task<string[]> Hashes(string path)
            {
                var hash=await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",path,"-map","0:v:0","-f","framemd5","-"]);hash.EnsureSuccess("Decode frame hashes");
                return hash.StandardOutput.Split('\n').Where(l=>l.Length>0&&!l.StartsWith('#')).Select(l=>l.Split(',')[^1].Trim()).ToArray();
            }
            var original=await Hashes(source);var encoded=await Hashes(output);
            if(!lossy&&mode!="burn")Assert.Equal(kept.Select(x=>original[x.n]),encoded);
            if(mode=="burn"){Assert.Equal(original[0],encoded[0]);Assert.NotEqual(original[1],encoded[1]);}
            var outputFrames=await runner.RunAsync(tools.FFprobePath,["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",output]);
            var actual=outputFrames.StandardOutput.Split('\n').Select(l=>double.TryParse(l.Trim().Split(',')[0],CultureInfo.InvariantCulture,out var t)?(double?)t:null).Where(t=>t.HasValue).Select(t=>t!.Value).ToArray();
            Assert.Equal(kept.Length,actual.Length);
            for(int i=0;i<actual.Length;i++)Assert.True(Math.Abs(CutTimeline.SourceTime(o,actual[i])-kept[i].t)<.002,$"Frame {i}: {actual[i]} maps incorrectly");
            for(int track=0;track<(audio?2:0);track++)
            {
                var before=Path.Combine(root,$"before-{track}.pcm");var after=Path.Combine(root,$"after-{track}.pcm");
                foreach(var item in new[]{(source,before),(output,after)})
                    (await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",item.Item1,"-map",$"0:a:{track}","-c:a","pcm_s16le","-f","s16le",item.Item2])).EnsureSuccess("Decode samples");
                var samples=await File.ReadAllBytesAsync(before);var expected=new List<byte>();
                if(mode=="audio-offset"&&track==0)samples=new byte[24000].Concat(samples).ToArray();
                foreach(var segment in CutTimeline.Keep(o))
                {
                    int start=(int)Math.Round(segment.Start*48000)*2,end=segment.End is {} stop?Math.Min(samples.Length,(int)Math.Round(stop*48000)*2):samples.Length;
                    if(end>start)expected.AddRange(samples.AsSpan(start,end-start).ToArray());
                }
                var decoded=await File.ReadAllBytesAsync(after);
                if(mode=="mp4")Assert.InRange(Math.Abs(expected.Count-decoded.Length),0,2048);
                else Assert.Equal(expected.ToArray(),decoded);
            }
            string subtitles="";
            if(mode!="burn")
            {
                var subtitleResult=await runner.RunAsync(tools.FFmpegPath,["-v","error","-i",output,"-map","0:s:0","-c:s","srt","-f","srt","-"]);
                subtitleResult.EnsureSuccess("Read cut subtitles");subtitles=subtitleResult.StandardOutput;Assert.DoesNotContain("Only ad",subtitles);
                if(mode!="empty-subtitles")Assert.Contains("Crosses both cuts",subtitles);
            }
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                await AtomicFile.WriteJsonAsync(Path.Combine(artifacts,"cut-"+mode+".json"),new{mode,media.StartTime,SourceFrames=times.Length,OutputFrames=actual.Length,ExpectedFrames=kept.Length,probe.Duration,Command=FFmpegCommandBuilder.Display(tools.FFmpegPath,args),Subtitles=subtitles});
            CutPreparationService.Cleanup(media,o,output);Assert.False(Directory.Exists(output+".schnittdaten"));
        }
        finally
        {
            if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ffstudio-cuts-"))throw new InvalidOperationException("Unexpected fixture path");
            Directory.Delete(root,true);
        }
    }
}
