namespace FFmpegStudio.Core;

public enum FrameSeek { Nearest, Previous, Next }
public sealed record SourceFramePosition(double Seconds,long Timestamp,double DecodeFromSeconds=0);

public static class FrameNavigation
{
    public static SourceFramePosition Select(IReadOnlyList<SourceFramePosition> frames,double seconds,FrameSeek direction)
    {
        if(frames.Count==0)throw new InvalidDataException(L.T("Keine Frame-Zeitstempel im gewählten Bereich gefunden."));
        return direction switch
        {
            FrameSeek.Previous=>frames.LastOrDefault(f=>f.Seconds<seconds-.0000001)??frames[0],
            FrameSeek.Next=>frames.FirstOrDefault(f=>f.Seconds>seconds+.0000001)??frames[^1],
            _=>frames.MinBy(f=>Math.Abs(f.Seconds-seconds))!
        };
    }
}
