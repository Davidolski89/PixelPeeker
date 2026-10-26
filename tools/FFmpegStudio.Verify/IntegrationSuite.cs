using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

internal static class IntegrationSuite
{
    public static async Task RunAsync(AppPaths paths, ProcessRunner runner, FFmpegVersionService versions)
    {
        var tools=versions.Active??throw new InvalidOperationException("Zuerst install ausführen.");
        var run=Path.Combine(paths.Root,"integration-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(run);
        var log=new LoggingService(paths);var settings=new SettingsService(paths,log);await settings.LoadAsync();
        settings.Current.CheckUpdates=false;settings.Current.ParallelJobs=2;settings.Current.ParallelGpuJobs=1;await settings.SaveAsync();
        var store=new QueueStore(paths);await store.InitializeAsync();var capsService=new FFmpegCapabilityService(runner);
        var caps=await capsService.AnalyzeAsync(tools);var probe=new MediaProbeService(runner);var hardware=new HardwareDetectionService(runner);
        var preview=new PreviewService(paths,runner,settings);var compare=new FrameComparisonService(runner,preview);
        var quality=new QualityAnalysisService(paths,runner,capsService,store);var builder=new FFmpegCommandBuilder();
        await using var queue=new QueueService(paths,store,settings,versions,capsService,hardware,builder,runner,probe,log);
        await queue.InitializeAsync();
        var results=new List<object>();
        async Task Check(string name,Func<Task> test)
        {
            var clock=Stopwatch.StartNew();
            try{await test();results.Add(new{name,Passed=true,Seconds=clock.Elapsed.TotalSeconds});Console.WriteLine("PASS "+name);}
            catch(Exception e){results.Add(new{name,Passed=false,Seconds=clock.Elapsed.TotalSeconds,Error=e.ToString()});Console.WriteLine("FAIL "+name+"\n"+e.Message);}
        }
        var source=Path.Combine(run,"Quelle 'Ä' & zwei Spuren.mkv");
        var srt=Path.Combine(run,"de.srt");var english=Path.Combine(run,"en.srt");
        await File.WriteAllTextAsync(srt,"1\n00:00:00,100 --> 00:00:01,800\nDeutsch: ÄÖÜ – Spur 1\n\n2\n00:00:02,000 --> 00:00:03,800\nZweiter Untertitel\n");
        await File.WriteAllTextAsync(english,"1\n00:00:00,100 --> 00:00:03,800\nEnglish subtitle track\n");
        var generate=await runner.RunAsync(tools.FFmpegPath,["-hide_banner","-nostdin","-n","-f","lavfi","-i","testsrc2=size=960x404:rate=24:duration=4",
            "-f","lavfi","-i","sine=frequency=440:sample_rate=48000:duration=4","-f","lavfi","-i","sine=frequency=880:sample_rate=48000:duration=4",
            "-i",srt,"-i",english,"-map","0:v","-map","1:a","-map","2:a","-map","3:s","-map","4:s",
            "-c:v","libx264","-preset","ultrafast","-crf","18","-pix_fmt","yuv420p","-color_primaries","bt709","-color_trc","bt709","-colorspace","bt709","-color_range","tv",
            "-c:a:0","aac","-c:a:1","ac3","-b:a:1","192k","-c:s","srt","-metadata:s:a:0","language=deu","-metadata:s:a:1","language=eng",
            "-metadata:s:s:0","language=deu","-metadata:s:s:1","language=eng",source]);
        generate.EnsureSuccess("Testmedien erzeugen");
        var originalHash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var media=await probe.ProbeAsync(tools,source);
        await AtomicFile.WriteJsonAsync(Path.Combine(run,"source.json"),media);
        var template=EncodingOptions.ForMedia(media);template.OutputDirectory=Path.Combine(run,"output");
        template.Video.Encoder="libx264";template.Video.Preset="veryfast";template.Filters.ScaleMode=ScaleMode.Prozent;template.Filters.Percent=50;
        async Task<EncodingJob> Encode(string name,Action<EncodingOptions> configure)
        {
            var options=Json.Clone(template);options.FileNamePattern="{name}_"+name;configure(options);
            var job=await queue.AddAsync(media,options);queue.Start(job);
            var timeout=Stopwatch.StartNew();
            while(job.Status is JobStatus.Wartend or JobStatus.Läuft){if(timeout.Elapsed>TimeSpan.FromMinutes(5)){queue.Cancel(job);throw new TimeoutException(name);}await Task.Delay(100);}
            if(job.Status!=JobStatus.Abgeschlossen)throw new InvalidOperationException(job.Error);
            if(!File.Exists(job.OutputPath)||job.OutputInfo?.Video is null)throw new Exception("Fehlendes Ergebnis.");
            return job;
        }
        EncodingJob? mixed=null;
        await Check("Original: kein Skalierungsfilter, unverändertes Raster, Vergleich und Qualitätsanalyse",async()=>
        {
            var job=await Encode("original-no-scale",o=>o.Filters.ScaleMode=ScaleMode.Original);
            Require(!job.ExecutedCommand.Contains("\"-vf\"")&&!job.ExecutedCommand.Contains("setsar="),"Kein unnötiger Filteraufruf");
            Require(job.OutputInfo!.Video!.Width==media.Video!.Width&&job.OutputInfo.Video.Height==media.Video.Height,"Originalraster");
            var pair=await compare.CompareAsync(job,.5);
            Require(File.Exists(pair.OriginalPath)&&File.Exists(pair.EncodedPath),"Originalvergleich ohne Referenzfilter");
            var analysis=await quality.AnalyzeAsync(job);
            Require(analysis.Vmaf.HasValue&&analysis.Ssim.HasValue&&analysis.Psnr.HasValue,"Metriken ohne Referenzfilter");
        });
        await Check("ffprobe: zwei Audio- und zwei Untertitelspuren",()=>{Require(media.Streams.Count(s=>s.Type=="audio")==2&&media.Streams.Count(s=>s.Type=="subtitle")==2,"Spurenanzahl");return Task.CompletedTask;});
        await Check("MKV: Streamcopy + Opus + Untertitel entfernen + 50 Prozent",async()=>
        {
            mixed=await Encode("mixed",o=>{o.Audio[1].Action=TrackAction.Encodieren;o.Audio[1].Encoder="libopus";o.Audio[1].BitRateKbps=192;o.Subtitles[1].Action=TrackAction.Entfernen;});
            Require(mixed.OutputInfo!.Video!.Width==480&&mixed.OutputInfo.Video.Height==202,"Zielraster");
            Require(mixed.OutputInfo.Streams.Count(s=>s.Type=="subtitle")==1,"Untertitelmapping");
            Require(mixed.OutputInfo.Streams.Where(s=>s.Type=="audio").Select(s=>s.Codec).SequenceEqual(new[]{"aac","opus"}),"Audiomapping");
        });
        await Check("MP4: Textuntertitel konvertieren + AAC",async()=>
        {
            await Encode("mp4",o=>{o.Container="mp4";foreach(var a in o.Audio){a.Action=TrackAction.Encodieren;a.Encoder="aac";a.BitRateKbps=128;}foreach(var s in o.Subtitles){s.Action=TrackAction.Encodieren;s.Encoder="mov_text";}});
        });
        foreach(var codec in new[]{"flac","libmp3lame","ac3","eac3"})
        {
            var encoder=codec;
            await Check("Audio: "+encoder,async()=>await Encode(encoder,o=>
            {o.Audio[0].Action=TrackAction.Encodieren;o.Audio[0].Encoder=encoder;o.Audio[0].Channels=2;o.Audio[0].BitRateKbps=192;o.Audio[1].Action=TrackAction.Entfernen;}));
        }
        await Check("Textuntertitel einbrennen mit Apostroph/Unicode im Pfad",async()=>
        {
            var job=await Encode("burn",o=>{o.Subtitles[0].Action=TrackAction.Einbrennen;o.Subtitles[1].Action=TrackAction.Entfernen;});
            Require(job.OutputInfo!.Streams.All(s=>s.Type!="subtitle"),"Keine gemuxte Burn-in-Spur");
        });
        await Check("Testausschnitt + VFR-fähige PTS-Zuordnung",async()=>
        {
            var job=await Encode("clip",o=>{o.ClipStart=1;o.ClipDuration=2;});
            var pair=await compare.CompareAsync(job,.5);
            Require(File.Exists(pair.OriginalPath)&&File.Exists(pair.EncodedPath),"Framepaar");
            Require(Math.Abs(pair.SourceSeconds-1-pair.EncodedSeconds)<.05,"PTS-Abweichung");
            await AtomicFile.WriteJsonAsync(Path.Combine(run,"frame-pair.json"),pair);
        });
        await Check("PNG-Vorschau und Crop-Erkennung",async()=>
        {
            var image=await preview.FrameAsync(tools,media,1,width:320);
            Require(new FileInfo(image).Length>0,"PNG");
            var crop=await preview.DetectCropAsync(tools,media);
            Require(crop.Left>=0&&crop.Right>=0&&crop.Top>=0&&crop.Bottom>=0,"Crop");
        });
        await Check("VMAF / SSIM / PSNR nach echtem Encode",async()=>
        {
            if(mixed is null)throw new Exception("Mixed-Job fehlgeschlagen.");
            var updates=new List<QualityAnalysisProgress>();
            string lastStage="";
            var analysis=await quality.AnalyzeAsync(mixed,new InlineProgress<QualityAnalysisProgress>(p=>
            {
                updates.Add(p);
                if(p.Stage!=lastStage) {Console.WriteLine(p.Stage);lastStage=p.Stage;}
            }));
            Require(analysis.Ssim is >0 and <=1&&analysis.Psnr>0,"Metriken");
            if(caps.Filters.Contains("libvmaf"))Require(analysis.Vmaf is >=0 and <=100,"VMAF");
            Require(analysis.Frames.Count>0,"Verlauf");
            Require(updates.First().Stage.Contains("Analyseplatz")&&!updates.First().Percent.HasValue,"Sichtbare Wartephase ohne erfundene Prozentwerte");
            Require(updates.Any(p=>p.Stage.Contains("Zeitstempel")&&p.Seconds.HasValue),"Fortschritt der Vorbereitungsphase");
            Require(updates.Where(p=>p.Percent.HasValue).All(p=>p.Percent is >=0 and <=100),"Begrenzte Prozentwerte");
            foreach(var metric in new[]{"VMAF","SSIM","PSNR"}.Where(m=>caps.Filters.Contains(m=="VMAF"?"libvmaf":m.ToLowerInvariant())))
                Require(updates.Any(p=>p.Stage.StartsWith(metric+" berechnen")&&p.Seconds>0&&p.Percent.HasValue),"Echter FFmpeg-Fortschritt: "+metric);
            Require(updates.Last().Percent==100&&updates.Last().Stage=="Analyse abgeschlossen","Abschluss erst nach Speicherung");
            await AtomicFile.WriteJsonAsync(Path.Combine(run,"analysis-progress.json"),updates);
        });
        await Check("Qualitätsanalyse: wartende Analyse abbrechen",async()=>
        {
            if(mixed is null)throw new Exception("Mixed-Job fehlgeschlagen.");
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first=quality.AnalyzeAsync(mixed,new InlineProgress<QualityAnalysisProgress>(p=>{if(p.Stage.Contains("berechnen"))started.TrySetResult();}));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var cancellation=new CancellationTokenSource();
            var waiting=false;
            try
            {
                await quality.AnalyzeAsync(mixed,new InlineProgress<QualityAnalysisProgress>(p=>
                {
                    if(p.Stage.Contains("Analyseplatz")) {waiting=true;Require(!p.Percent.HasValue,"Warten ohne Prozent");cancellation.Cancel();}
                }),cancellation.Token);
                throw new Exception("Die wartende Analyse wurde nicht abgebrochen.");
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            var result=await first;
            Require(waiting&&ReferenceEquals(result,mixed.Analysis),"Laufende Analyse und Ergebnis bleiben erhalten");
        });
        foreach(var encoder in new[]{"libx265","libsvtav1","libaom-av1","librav1e","libvvenc"}.Where(e=>caps.Find(e)is not null))
        {
            await Check("Software-Videoencoder: "+encoder,async()=>
            {
                await Encode(encoder,o=>
                {
                    o.Video.Encoder=encoder;o.Video.Preset=encoder is "libsvtav1"?"10":encoder is "libaom-av1" or "librav1e"?"8":encoder=="libvvenc"?"faster":"ultrafast";
                    o.ClipDuration=.5;o.Filters.ScaleMode=ScaleMode.Benutzerdefiniert;o.Filters.Width=320;o.Filters.Height=180;
                    o.Audio.Clear();o.Subtitles.Clear();
                });
            });
        }
        await Check("Prozessabbruch ohne zurückbleibenden FFmpeg-Prozess",async()=>
        {
            using var ct=new CancellationTokenSource(600);
            var start=Stopwatch.StartNew();
            try{await runner.RunAsync(tools.FFmpegPath,["-hide_banner","-nostdin","-re","-f","lavfi","-i","testsrc2=size=64x64:rate=24","-f","null","-"],ct.Token);throw new Exception("Kein Abbruch.");}
            catch(OperationCanceledException){Require(start.Elapsed<TimeSpan.FromSeconds(10),"Abbruchdauer");}
        });
        await Check("Originaldatei unverändert",async()=>Require(originalHash==Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))),"Originalhash"));
        var gpuResults=new List<object>();
        foreach(var encoder in new[]{"h264_nvenc","hevc_nvenc","av1_nvenc"}.Where(e=>caps.Find(e)is not null))
        {
            var result=await hardware.ProbeEncoderAsync(tools,encoder,0);gpuResults.Add(new{encoder,result.Usable,result.Details});
            Console.WriteLine($"GPU {encoder}: {result.Usable} {result.Details}");
            if(result.Usable) await Check("NVENC vollständiger Job: "+encoder,async()=>await Encode(encoder,o=>
            {
                o.Video.Encoder=encoder;o.Video.Preset="p6";o.Filters.ScaleMode=ScaleMode.Original;
                o.Video.Extra["multipass"]="fullres";o.Video.Extra["spatial-aq"]="1";o.Video.Extra["temporal-aq"]="1";o.Video.Extra["rc-lookahead"]="16";
            }));
        }
        if(caps.Filters.Contains("scale_cuda"))
        {
            await Check("CUDA-Decoding + HEVC NVENC bei Original ohne Skalierungsfilter",async()=>
            {
                var job=await Encode("cuda-original-no-scale",o=>
                {o.Video.Encoder="hevc_nvenc";o.Video.Preset="p5";o.Video.CudaDecode=true;o.Video.CudaScale=true;o.Filters.ScaleMode=ScaleMode.Original;});
                Require(!job.ExecutedCommand.Contains("\"-vf\""),"Direkte CUDA-Frames ohne Identitätsskalierung");
                Require(job.OutputInfo!.Video!.Width==media.Video!.Width&&job.OutputInfo.Video.Height==media.Video.Height,"CUDA-Originalraster");
            });
            await Check("CUDA-Decoding + GPU-Skalierung + HEVC NVENC",async()=>await Encode("cuda",o=>
            {o.Video.Encoder="hevc_nvenc";o.Video.Preset="p5";o.Video.CudaDecode=true;o.Video.CudaScale=true;}));
        }
        var sensors=await new HardwareMonitorService(runner).ReadAsync();
        await AtomicFile.WriteJsonAsync(Path.Combine(run,"report.json"),new{Time=DateTimeOffset.Now,Tools=tools,Tests=results,Hardware=gpuResults,Sensors=sensors,Source=source});
        Console.WriteLine("REPORT "+Path.Combine(run,"report.json"));
        Console.WriteLine("SOURCE "+source);
        bool failed=results.Any(r=>!(bool)r.GetType().GetProperty("Passed")!.GetValue(r)!);
        if(failed)Environment.ExitCode=1;
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception("Prüfung fehlgeschlagen: "+message);}
    private sealed class InlineProgress<T>(Action<T> report):IProgress<T> { public void Report(T value)=>report(value); }
}
