using System.Collections.Concurrent;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed record EncodeComparisonInput(MediaInfo Media,Toolchain Tools,EncodingJob? Job=null)
{
    public static EncodeComparisonInput FromJob(EncodingJob job)=>new(job.OutputInfo??throw new InvalidOperationException(L.T("Keine geprüfte Ausgabedatei vorhanden.")),
        job.Tools,new EncodingJob{Id=job.Id,Source=job.Source,Options=Json.Clone(job.Options),OutputInfo=job.OutputInfo,OutputPath=job.OutputPath,Tools=job.Tools,Status=job.Status});
}
public sealed record ComparisonPicture(string? Path,SourceFramePosition? Frame,double? SourceSeconds,string Message="");
public sealed record EncodeComparisonFrames(double SourceSeconds,SourceFramePosition Anchor,ComparisonPicture? Original,ComparisonPicture A,ComparisonPicture B);

public sealed class EncodeComparisonService(FrameNavigationService navigation,PreviewService preview)
{
    private readonly ConcurrentDictionary<string,double> origins=new();
    public async Task<EncodeComparisonFrames> LoadAsync(MediaInfo? original,EncodeComparisonInput a,EncodeComparisonInput? b,double requested,
        bool includeOriginal,double offsetA=0,double offsetB=0,FrameSeek direction=FrameSeek.Nearest,CancellationToken ct=default,bool matchReference=false)
    {
        if(!double.IsFinite(requested)||requested<0||!double.IsFinite(offsetA)||!double.IsFinite(offsetB))throw new ArgumentException(L.T("Gültige Zeitposition eingeben."));
        if(a.Job is {} aj&&b?.Job is {} bj&&!SameFile(aj.Source.Path,bj.Source.Path))
            throw new InvalidOperationException(L.T("Die Queue-Encodes müssen dieselbe Quelldatei verwenden."));
        var source=original??a.Job?.Source??b?.Job?.Source;
        if(source is not null&&new[]{a,b}.Any(input=>input?.Job is {} job&&!SameFile(source.Path,job.Source.Path)))
            throw new InvalidOperationException(L.T("Das Original passt nicht zur Quelle des Queue-Jobs."));
        // Without an original, B may extend beyond A or start after it. Use the file that covers the
        // requested source position, and keep true gaps on the common clock instead of snapping to A's EOF.
        static bool Covers(EncodeComparisonInput input,double time,double offset)=>time>=offset&&(input.Media.Duration is not {} duration||time<offset+duration);
        var anchorInput=source is null&&!Covers(a,requested,offsetA)&&b is not null&&Covers(b,requested,offsetB)?b:a;
        var anchorOffset=ReferenceEquals(anchorInput,a)?offsetA:offsetB;
        var hasAnchor=source is not null||Covers(anchorInput,requested,anchorOffset);
        var anchor=await navigation.ResolveAsync(anchorInput.Tools,source??anchorInput.Media,source is null?Math.Max(0,requested-anchorOffset):requested,direction,ct);
        // A clip may begin between source frames. Keep the initial comparison inside the retained interval.
        if(source is not null&&direction==FrameSeek.Nearest&&a.Job is {} clip&&
            ComparisonTimeline.OutputTime(clip.Options,requested) is not null&&ComparisonTimeline.OutputTime(clip.Options,anchor.Seconds) is null)
        {
            var next=await navigation.ResolveAsync(a.Tools,source,requested,FrameSeek.Next,ct);
            if(ComparisonTimeline.OutputTime(clip.Options,next.Seconds) is not null)anchor=next;
            else
            {
                var previous=await navigation.ResolveAsync(a.Tools,source,requested,FrameSeek.Previous,ct);
                if(ComparisonTimeline.OutputTime(clip.Options,previous.Seconds) is not null)anchor=previous;
            }
        }
        var seconds=source is not null?anchor.Seconds:hasAnchor?anchor.Seconds+anchorOffset:requested;
        var originalTask=includeOriginal&&source is not null?LoadOriginalAsync():Task.FromResult<ComparisonPicture?>(null);
        var at=LoadEncodeAsync(a,seconds,offsetA,ct);var bt=b is not null?LoadEncodeAsync(b,seconds,offsetB,ct):Task.FromResult(new ComparisonPicture(null,null,null));
        await Task.WhenAll(new Task[]{originalTask,at,bt});
        return new(seconds,anchor,await originalTask,await at,await bt);
        async Task<ComparisonPicture?> LoadOriginalAsync()
        {
            string filter="";int? width=null,height=null;
            if(matchReference&&a.Job is {} job)
            {
                filter=FilterService.Build(source!,FrameComparisonService.ReferenceOptions(job.Options),includeCustom:false);
                var dimensions=ScalingService.Calculate(a.Media.Video!,new FilterOptions());width=dimensions.Width;height=dimensions.Height;
            }
            return new(await preview.FrameAsync(a.Tools,source!,anchor.Seconds,filter,width,height,ct,anchor),anchor,anchor.Seconds);
        }
    }
    public static bool SameFile(string a,string b)=>Path.GetFullPath(a).Equals(Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    private async Task<ComparisonPicture> LoadEncodeAsync(EncodeComparisonInput input,double sourceSeconds,double offset,CancellationToken ct)
    {
        try
        {
            if(!File.Exists(input.Media.Path))throw new FileNotFoundException(L.T("Mediendatei nicht gefunden."),input.Media.Path);
            var job=input.Job;double target;
            double origin=0;
            if(job is not null)
            {
                if(job.Options.Filters.CustomChain.Length>0)return new(null,null,null,L.T("Freie Filterketten: Die automatische Zeitzuordnung ist nicht verfügbar."));
                var mapped=ComparisonTimeline.OutputTime(job.Options,sourceSeconds);
                if(mapped is null)return new(null,null,null,L.T("Diese Quellposition ist in diesem Encode nicht enthalten (Schnitt oder Testausschnitt)."));
                origin=await OriginAsync(input,ct);target=mapped.Value+origin;
            }
            else target=sourceSeconds-offset;
            if(target<-.000001||input.Media.Duration is {} duration&&target>=duration)
                return new(null,null,null,L.T("Diese Position liegt außerhalb der Datei. Zeitversatz oder Ausschnitt prüfen."));
            var frame=await navigation.ResolveAsync(input.Tools,input.Media,Math.Max(0,target),ct:ct);
            var actualSource=job is null?frame.Seconds+offset:CutTimeline.SourceTime(job.Options,Math.Max(0,frame.Seconds-origin));
            var path=await preview.FrameAsync(input.Tools,input.Media,frame.Seconds,ct:ct,exactFrame:frame);
            return new(path,frame,actualSource);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e){return new(null,null,null,e.Message);}
    }
    private async Task<double> OriginAsync(EncodeComparisonInput input,CancellationToken ct)
    {
        var job=input.Job!;var file=new FileInfo(input.Media.Path);var sourceFile=new FileInfo(job.Source.Path);
        var key=$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{sourceFile.Length}|{sourceFile.LastWriteTimeUtc.Ticks}|{System.Text.Json.JsonSerializer.Serialize(job.Options,Json.Options)}";
        if(origins.TryGetValue(key,out var cached))return cached;
        var start=CutTimeline.Keep(job.Options).First().Start;
        var firstSource=await navigation.ResolveAsync(input.Tools,job.Source,start,ct:ct);
        if(firstSource.Seconds<start-1e-7)firstSource=await navigation.ResolveAsync(input.Tools,job.Source,firstSource.Seconds,FrameSeek.Next,ct);
        var firstOutput=await navigation.ResolveAsync(input.Tools,input.Media,0,ct:ct);
        var origin=firstOutput.Seconds-(ComparisonTimeline.OutputTime(job.Options,firstSource.Seconds)??0);
        if(origins.Count>64)origins.Clear();origins[key]=origin;return origin;
    }
}
