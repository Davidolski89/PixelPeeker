using System.Globalization;
using System.Text.Json.Serialization;

namespace FFmpegStudio.Core;

/// <summary>Start is the first removed frame; End is the first retained frame, or null for EOF.</summary>
public sealed record CutRegion(SourceFramePosition Start, SourceFramePosition? End)
{
    [JsonIgnore] public string Label => $"{TimeSpan.FromSeconds(Math.Max(0,Start.Seconds)):hh\\:mm\\:ss\\.ffffff} → " +
        (End is {} end ? $"{TimeSpan.FromSeconds(Math.Max(0,end.Seconds)):hh\\:mm\\:ss\\.ffffff}" : L.T("Dateiende"));
}

public sealed record KeepSegment(double Start, double? End);

public static class CutTimeline
{
    public static bool Active(EncodingOptions o) => o.CutsEnabled && o.Cuts.Count > 0;
    public static IReadOnlyList<CutRegion> Merge(IEnumerable<CutRegion> cuts)
    {
        var result=new List<CutRegion>();
        foreach(var cut in cuts.OrderBy(c=>c.Start.Seconds))
        {
            if(result.Count==0 || result[^1].End is {} previous && cut.Start.Seconds>previous.Seconds+1e-7) result.Add(cut);
            else
            {
                var last=result[^1];
                result[^1]=last with {End=last.End is null||cut.End is null?null:last.End.Seconds>=cut.End.Seconds?last.End:cut.End};
            }
        }
        return result;
    }
    public static IReadOnlyList<KeepSegment> Keep(EncodingOptions o)
    {
        double clipStart=o.ClipStart??0; double? clipEnd=o.ClipDuration is {} length?clipStart+length:null;
        var result=new List<KeepSegment>();double cursor=clipStart;
        foreach(var cut in Active(o)?Merge(o.Cuts):[])
        {
            if(cut.End?.Seconds<=cursor)continue;
            if(clipEnd is {} limit && cut.Start.Seconds>=limit)break;
            var end=clipEnd.HasValue?Math.Min(cut.Start.Seconds,clipEnd.Value):cut.Start.Seconds;
            if(end>cursor+1e-7)result.Add(new(cursor,end));
            if(cut.End is null)return result;
            cursor=Math.Max(cursor,cut.End.Seconds);
        }
        if(!clipEnd.HasValue || cursor<clipEnd.Value-1e-7)result.Add(new(cursor,clipEnd));
        return result;
    }
    public static double? Duration(MediaInfo media,EncodingOptions o)
    {
        if(!Active(o))return o.ClipDuration??media.Duration;
        double total=0;
        foreach(var segment in Keep(o))
        {
            var end=segment.End??media.Duration;
            if(end is null)return null;
            total+=Math.Max(0,end.Value-segment.Start);
        }
        return total;
    }
    public static double SourceTime(EncodingOptions o,double outputTime)
    {
        foreach(var segment in Keep(o))
        {
            if(segment.End is null || outputTime<segment.End.Value-segment.Start-1e-7)return segment.Start+Math.Max(0,outputTime);
            outputTime-=segment.End.Value-segment.Start;
        }
        return Keep(o).LastOrDefault()?.End??o.ClipStart??0;
    }
    private static double RemovedBefore(IReadOnlyList<CutRegion> cuts,double time)=>
        cuts.Sum(c=>Math.Max(0,Math.Min(time,c.End?.Seconds??time)-c.Start.Seconds));

    /// <summary>Raw presentation timestamps select source frames before fps or geometry filters.</summary>
    public static string VideoFilter(MediaInfo media,EncodingOptions o)
    {
        var cuts=Merge(o.Cuts);var predicates=new List<string>();
        foreach(var cut in cuts)
            predicates.Add(cut.End is {} end?$"gte(pts,{cut.Start.Timestamp})*lt(pts,{end.Timestamp})":$"gte(pts,{cut.Start.Timestamp})");
        string select=$"not({string.Join('+',predicates)})";
        double clipStart=o.ClipStart??0;
        if(clipStart>0)select+=$"*gte(t,{N(media.StartTime+clipStart-1e-8)})";
        if(o.ClipDuration is {} duration)select+=$"*lt(t,{N(media.StartTime+clipStart+duration-1e-8)})";
        var offset=media.StartTime+clipStart-RemovedBefore(cuts,clipStart);
        string pts=$"PTS-({N(offset)})/TB";
        foreach(var cut in cuts.Where(c=>c.End is not null))
            pts+=$"-if(gte(PTS,{cut.End!.Timestamp}),{N(cut.End.Seconds-cut.Start.Seconds)}/TB,0)";
        return $"select='{select}',setpts='round({pts})'";
    }
    public static string AudioGraph(MediaInfo media,EncodingOptions o,AudioTrackOptions track,int ordinal)
    {
        var segments=Keep(o);var graph=new List<string>();
        if(segments.Count>1)graph.Add($"[0:{track.InputIndex}]asplit={segments.Count}"+string.Concat(Enumerable.Range(0,segments.Count).Select(i=>$"[ca{ordinal}_{i}]")));
        for(int i=0;i<segments.Count;i++)
        {
            var s=segments[i];var start=media.StartTime+s.Start;
            var input=segments.Count>1?$"ca{ordinal}_{i}":$"0:{track.InputIndex}";
            string filters=$"atrim=start={N(start)}"+(s.End is {} end?$":end={N(media.StartTime+end)}":"")+
                $",asetpts=PTS-({N(start)})/TB,aresample=async=1:first_pts=0";
            if(s.End is {} stop)filters+=$",apad=whole_dur={N(stop-s.Start)},atrim=duration={N(stop-s.Start)}";
            graph.Add($"[{input}]{filters}[cs{ordinal}_{i}]");
        }
        graph.Add(string.Concat(Enumerable.Range(0,segments.Count).Select(i=>$"[cs{ordinal}_{i}]"))+$"concat=n={segments.Count}:v=0:a=1[cutAudio{ordinal}]");
        return string.Join(';',graph);
    }
    public static string SubtitlePath(string output,SubtitleTrackOptions track)=>Path.Combine(output+".schnittdaten",$"subtitle-{track.InputIndex}."+(track.SourceCodec is "ass" or "ssa"?"ass":"vtt"));
    public static string FontDirectory(string output)=>Path.Combine(output+".schnittdaten","fonts");
    public static IEnumerable<(int Ordinal,string Path)> Fonts(MediaInfo media,string output)
    {
        int ordinal=0;
        foreach(var attachment in media.Streams.Where(s=>s.Type=="attachment"))
        {
            if(attachment.Codec is "ttf" or "otf")yield return (ordinal,Path.Combine(FontDirectory(output),$"font-{attachment.Index}.{attachment.Codec}"));
            ordinal++;
        }
    }
    public static string SubtitleCodec(SubtitleTrackOptions track)=>track.Action==TrackAction.Kopieren?track.SourceCodec switch{"subrip"=>"srt","ssa"=>"ass",_=>track.SourceCodec}:track.Encoder;
    public static IEnumerable<string> Validate(MediaInfo media,EncodingOptions o)
    {
        if(!Active(o))yield break;
        if(o.Cuts.Count>100)yield return L.T("Höchstens 100 Schnittbereiche pro Datei verwenden.");
        var tb=MediaStream.Rational(media.Video?.TimeBase);
        foreach(var c in o.Cuts)
        {
            bool Valid(SourceFramePosition p)=>double.IsFinite(p.Seconds)&&p.Seconds>=-1e-7&&tb is >0&&Math.Abs(p.Timestamp*tb.Value-media.StartTime-p.Seconds)<.002;
            if(!Valid(c.Start)||c.End is {} end&&(!Valid(end)||end.Seconds<=c.Start.Seconds+1e-7||end.Timestamp<=c.Start.Timestamp))
                yield return L.T("Ungültige Schnittmarken. Start und Ende müssen bestätigte Quellbilder in aufsteigender Reihenfolge sein.");
        }
        if(Keep(o).Count==0 || media.Duration is {} d&&Keep(o).All(s=>s.Start>=d)&&o.ClipDuration.HasValue)
            yield return L.T("Der Schnitt entfernt den gesamten gewählten Ausschnitt.");
        if(o.Video.CudaScale)yield return L.T("Für den Schnitt CUDA-Skalierung ausschalten. NVENC-Encoding bleibt möglich.");
        if(o.Filters.CustomChain.Length>0)yield return L.T("Freie Filterketten sind beim Schnitt gesperrt, damit die Zeitzuordnung eindeutig bleibt.");
        if(o.Audio.Any(a=>a.Action==TrackAction.Kopieren))yield return L.T("Für framegenauen Schnitt die erhaltenen Audiospuren encodieren statt kopieren.");
        if(o.Subtitles.Any(s=>s.IsBitmap&&s.Action!=TrackAction.Entfernen))yield return L.T("Bilduntertitel beim Schnitt entfernen. Das zeitliche Schneiden dieser Spuren wird noch nicht unterstützt.");
    }
    private static string N(double v)=>v.ToString("0.#########",CultureInfo.InvariantCulture);
}
