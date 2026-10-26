using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public static class CutEditing
{
    public static void EnsureAudio(EncodingOptions options)
    {
        if (!CutTimeline.Active(options)) return;
        options.Video.CudaScale = false;
        foreach (var track in options.Audio.Where(track => track.Action == TrackAction.Kopieren))
        {
            track.Action = TrackAction.Encodieren;
            if (options.Container is "mp4" or "mov" && track.Encoder is not ("aac" or "libopus" or "ac3" or "eac3" or "alac" or "libmp3lame")) track.Encoder = "aac";
            if (options.Container == "webm" && track.Encoder is not ("libopus" or "libvorbis")) track.Encoder = "libopus";
        }
    }

    public static CutRegion? Add(EncodingOptions options, SourceFramePosition start, SourceFramePosition? end)
    {
        var merged = CutTimeline.Merge(options.Cuts.Append(new CutRegion(start, end)));
        options.Cuts.Clear();
        foreach (var cut in merged) options.Cuts.Add(cut);
        options.CutsEnabled = true;
        EnsureAudio(options);
        return options.Cuts.FirstOrDefault(cut => cut.Start.Seconds <= start.Seconds && (cut.End is null || cut.End.Seconds >= start.Seconds));
    }
}
