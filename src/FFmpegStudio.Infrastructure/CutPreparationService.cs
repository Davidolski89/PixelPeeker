using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

/// <summary>Only files derived from this job's unique partial path are created or removed.</summary>
public sealed class CutPreparationService(ProcessRunner runner)
{
    public async Task PrepareAsync(Toolchain tools,MediaInfo media,EncodingOptions options,string output,CancellationToken ct)
    {
        if(!CutTimeline.Active(options))return;
        if(options.Subtitles.Any(s=>s.Action==TrackAction.Einbrennen))
        {
            var fonts=CutTimeline.Fonts(media,output).ToArray();
            if(fonts.Length>0)
            {
                Directory.CreateDirectory(CutTimeline.FontDirectory(output));
                var args=new List<string>{"-hide_banner","-nostdin","-n","-v","error"};
                foreach(var font in fonts)args.AddRange([$"-dump_attachment:t:{font.Ordinal}",font.Path]);
                args.AddRange(["-i",media.Path,"-map","0:v:0","-frames:v","0","-an","-sn","-f","null","-"]);
                (await runner.RunAsync(tools.FFmpegPath,args,ct)).EnsureSuccess(L.T("Untertitel für den Schnitt vorbereiten"));
            }
        }
        foreach(var track in options.Subtitles.Where(s=>s.Action!=TrackAction.Entfernen))
        {
            ct.ThrowIfCancellationRequested();
            var path=CutTimeline.SubtitlePath(output,track);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bool ass=track.SourceCodec is "ass" or "ssa";var raw=path+".source."+(ass?"ass":"srt");
            var result=await runner.RunAsync(tools.FFmpegPath,["-hide_banner","-nostdin","-n","-v","error","-copyts","-i",media.Path,"-map",$"0:{track.InputIndex}","-c:s",ass?"ass":"srt",raw],ct);
            result.EnsureSuccess(L.T("Untertitel für den Schnitt vorbereiten"));
            var text=SubtitleCuts.Retime(await File.ReadAllTextAsync(raw,ct),ass,options,media.StartTime);
            await using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await using(var writer=new StreamWriter(stream))await writer.WriteAsync(text.AsMemory(),ct);
        }
    }
    public static void Cleanup(MediaInfo media,EncodingOptions options,string output)
    {
        if(!CutTimeline.Active(options))return;
        if(options.Subtitles.Any(s=>s.Action==TrackAction.Einbrennen))
        {
            foreach(var font in CutTimeline.Fonts(media,output))try{File.Delete(font.Path);}catch(IOException){}catch(UnauthorizedAccessException){}
            try{Directory.Delete(CutTimeline.FontDirectory(output),false);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(var track in options.Subtitles.Where(s=>s.Action!=TrackAction.Entfernen))
        {
            var path=CutTimeline.SubtitlePath(output,track);
            foreach(var file in new[]{path,path+".source."+(track.SourceCodec is "ass" or "ssa"?"ass":"srt")})
                try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        try{Directory.Delete(output+".schnittdaten",false);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
