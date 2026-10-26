namespace FFmpegStudio.Core;

public enum ComparisonAvailability { Available, Unavailable, Unknown }
public sealed record ComparisonInterval(double Start,double End,ComparisonAvailability Availability);

public static class ComparisonTimeline
{
    /// <summary>Coverage on the common source clock. External files have known bounds, but no known cut map.</summary>
    public static IReadOnlyList<ComparisonInterval> Coverage(double duration,EncodingOptions? options,double? sourceDuration,double? fileDuration,double offset=0)
    {
        if(!double.IsFinite(duration)||duration<=0)return [];
        if(!double.IsFinite(offset)||options?.Filters.CustomChain.Length>0)
            return [new(0,duration,ComparisonAvailability.Unknown)];
        var intervals=new List<ComparisonInterval>();double cursor=0;
        var limit=options is null?duration:Math.Min(duration,sourceDuration is >0?sourceDuration.Value:duration);
        var keep=options is null?new[]{new KeepSegment(offset,fileDuration is >0?offset+fileDuration.Value:null)}:CutTimeline.Keep(options);
        foreach(var segment in keep)
        {
            var start=Math.Clamp(segment.Start,0,limit);var end=Math.Clamp(segment.End??limit,0,limit);
            if(end<=start)continue;
            if(start>cursor)intervals.Add(new(cursor,start,ComparisonAvailability.Unavailable));
            intervals.Add(new(start,end,options is null?ComparisonAvailability.Unknown:ComparisonAvailability.Available));cursor=end;
        }
        if(cursor<duration)intervals.Add(new(cursor,duration,ComparisonAvailability.Unavailable));
        return intervals;
    }

    /// <summary>Inverse of CutTimeline.SourceTime. Removed frames and times outside a test clip have no output position.</summary>
    public static double? OutputTime(EncodingOptions options,double sourceSeconds)
    {
        if(!double.IsFinite(sourceSeconds)||sourceSeconds<0)return null;
        double elapsed=0;
        foreach(var segment in CutTimeline.Keep(options))
        {
            if(sourceSeconds<segment.Start-1e-7)return null;
            if(segment.End is null||sourceSeconds<segment.End.Value-1e-7)return elapsed+Math.Max(0,sourceSeconds-segment.Start);
            elapsed+=segment.End.Value-segment.Start;
        }
        return null;
    }
}
