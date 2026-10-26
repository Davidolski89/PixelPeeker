using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public static class SubtitleCuts
{
    private static readonly Regex CueTime=new(@"(?<start>\d+:\d{2}:\d{2}[,.]\d+)\s*-->\s*(?<end>\d+:\d{2}:\d{2}[,.]\d+)");
    public static string Retime(string text,bool ass,EncodingOptions o,double sourceOffset)
    {
        var segments=CutTimeline.Keep(o);var output=new StringBuilder(ass?"":"WEBVTT\n\n");
        IEnumerable<(double Start,double End)> Intersections(double start,double end)
        {
            start-=sourceOffset;end-=sourceOffset;double shift=0;
            foreach(var segment in segments)
            {
                double a=Math.Max(start,segment.Start), b=Math.Min(end,segment.End??end);
                if(b>a+1e-7)yield return (shift+a-segment.Start,shift+b-segment.Start);
                if(segment.End is {} stop)shift+=stop-segment.Start;
            }
        }
        if(ass)
        {
            bool events=false;string[] fields=[];
            foreach(var line in text.Replace("\r","").Split('\n'))
            {
                if(line.StartsWith('['))events=line.Equals("[Events]",StringComparison.OrdinalIgnoreCase);
                if(events && line.StartsWith("Format:",StringComparison.OrdinalIgnoreCase))fields=line[7..].Split(',').Select(x=>x.Trim()).ToArray();
                if(events && line.StartsWith("Dialogue:",StringComparison.OrdinalIgnoreCase))
                {
                    int a=Array.FindIndex(fields,x=>x.Equals("Start",StringComparison.OrdinalIgnoreCase)),b=Array.FindIndex(fields,x=>x.Equals("End",StringComparison.OrdinalIgnoreCase));
                    var values=line[9..].TrimStart().Split(',',fields.Length);
                    if(a<0||b<0||values.Length!=fields.Length)throw new InvalidDataException("Invalid ASS event format.");
                    foreach(var cue in Intersections(Parse(values[a]),Parse(values[b])))
                    {values[a]=Stamp(cue.Start,true);values[b]=Stamp(cue.End,true);output.AppendLine("Dialogue: "+string.Join(',',values));}
                }
                else output.AppendLine(line);
            }
        }
        else
        {
            foreach(var block in Regex.Split(text.Trim(),@"\r?\n\s*\r?\n"))
            {
                var match=CueTime.Match(block);if(!match.Success)continue;
                var body=block[(match.Index+match.Length)..].Trim();
                foreach(var cue in Intersections(Parse(match.Groups["start"].Value),Parse(match.Groups["end"].Value)))
                    output.AppendLine(Stamp(cue.Start,false)+" --> "+Stamp(cue.End,false)).AppendLine(body).AppendLine();
            }
        }
        return output.ToString();
    }
    private static double Parse(string value)=>TimeSpan.Parse(value.Trim().Replace(',','.'),CultureInfo.InvariantCulture).TotalSeconds;
    private static string Stamp(double value,bool ass)
    {
        var t=TimeSpan.FromMilliseconds(Math.Max(0,Math.Round(value*(ass?100:1000))*(ass?10:1)));
        return ass?$"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds/10:00}":$"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}
