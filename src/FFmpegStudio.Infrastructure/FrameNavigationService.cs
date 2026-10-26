using System.Globalization;
using System.Text.Json;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class FrameNavigationService(ProcessRunner runner)
{
    private sealed record Window(string Key,double Start,double End,IReadOnlyList<SourceFramePosition> Frames);
    private readonly List<Window> cache=[];
    private readonly SemaphoreSlim gate=new(1);
    private static string Key(Toolchain tools,MediaInfo media)
    {var source=new FileInfo(media.Path);return $"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}|{media.Video!.Index}|{tools.FFprobePath}|{tools.Version}";}
    // A few preceding frames make reverse stepping immediate without estimating VFR frame durations.
    public async Task<SourceFramePosition> PrerollAsync(Toolchain tools,MediaInfo media,SourceFramePosition frame,CancellationToken ct=default)
    {
        var key=Key(tools,media);await gate.WaitAsync(ct);
        try
        {
            var window=cache.LastOrDefault(w=>w.Key==key&&w.Frames.Any(f=>f.Timestamp==frame.Timestamp));
            if(window is null)return frame;
            var index=window.Frames.ToList().FindIndex(f=>f.Timestamp==frame.Timestamp);
            return window.Frames[Math.Max(0,index-4)];
        }
        finally{gate.Release();}
    }
    public async Task<SourceFramePosition> ResolveAsync(Toolchain tools,MediaInfo media,double seconds,FrameSeek direction=FrameSeek.Nearest,CancellationToken ct=default)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentException(L.T("Gültige Zeitposition eingeben."));
        var key=Key(tools,media);
        await gate.WaitAsync(ct);
        try
        {
            bool Covers(Window w)=>w.Key==key&&w.Start<=seconds&&w.End>=seconds&&
                (direction==FrameSeek.Next?(w.Start==0||w.Frames.Any(f=>f.Seconds<=seconds))&&(w.Frames.Any(f=>f.Seconds>seconds+.0000001)||w.End>=media.Duration):
                 direction==FrameSeek.Previous?(w.Frames.Any(f=>f.Seconds<seconds-.0000001)||w.Start==0)&&(w.Frames.Any(f=>f.Seconds>=seconds)||w.End>=media.Duration):
                 (w.Frames.Any(f=>f.Seconds<=seconds)||w.Start==0)&&(w.Frames.Any(f=>f.Seconds>=seconds)||w.End>=media.Duration));
            SourceFramePosition Select(Window window)=>direction==FrameSeek.Nearest&&media.Duration is {} end&&seconds>=end?window.Frames[^1]:FrameNavigation.Select(window.Frames,seconds,direction);
            if(cache.LastOrDefault(Covers) is {} cached)return Select(cached);
            var duration=media.Duration is >0&&double.IsFinite(media.Duration.Value)?Math.Max(media.Duration.Value,seconds):seconds+30;
            for(double radius=2;;radius*=4)
            {
                ct.ThrowIfCancellationRequested();
                var start=Math.Max(0,seconds-radius);var end=Math.Min(duration,seconds+radius);
                var result=await runner.RunAsync(tools.FFprobePath,["-v","error","-select_streams",media.Video!.Index.ToString(CultureInfo.InvariantCulture),
                    "-read_intervals",$"{FFmpegCommandBuilder.Number(start==0?0:Math.Max(0,start+media.StartTime-1))}%{FFmpegCommandBuilder.Number(end+media.StartTime+2)}","-show_frames",
                    "-show_entries","frame=best_effort_timestamp,best_effort_timestamp_time,key_frame","-of","json",media.Path],ct);
                result.EnsureSuccess(L.T("Frame-Zeitstempel"));
                using var json=JsonDocument.Parse(result.StandardOutput);
                var frames=new List<SourceFramePosition>();
                double decodeFrom=0;
                foreach(var frame in json.RootElement.GetProperty("frames").EnumerateArray())
                    if(frame.TryGetProperty("best_effort_timestamp",out var pts)&&pts.TryGetInt64(out var stamp)&&MediaProbeParser.Number(frame,"best_effort_timestamp_time") is {} time&&double.IsFinite(time)&&time-media.StartTime>=-.0000001)
                    {
                        if(MediaProbeParser.Number(frame,"key_frame")==1)decodeFrom=Math.Max(0,time-media.StartTime-1);
                        frames.Add(new(Math.Max(0,time-media.StartTime),stamp,decodeFrom));
                    }
                var window=new Window(key,start,Math.Max(end,frames.Count==0?end:frames.Max(f=>f.Seconds)),frames.DistinctBy(f=>f.Timestamp).OrderBy(f=>f.Seconds).ToArray());
                if(window.Frames.Count>0&&(Covers(window)||(start==0&&end==duration)||radius>=duration))
                {
                    cache.Add(window);while(cache.Count>4)cache.RemoveAt(0);
                    return Select(window);
                }
                if(start==0&&end==duration)throw new InvalidDataException(L.T("Keine Frame-Zeitstempel im gewählten Bereich gefunden."));
            }
        }
        finally{gate.Release();}
    }
}
