using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

// Top-down BGR pixels. No encoded image or temporary file is needed by the desktop preview.
public sealed record PreviewPixels(long Timestamp,int Width,int Height,int Stride,byte[] Pixels);
public sealed record AccuratePreviewStatistics(long DecoderStarts,long DecodedFrames,long CacheHits,long BufferedBytes,bool DecoderRunning);

public sealed class AccuratePreviewService(FrameNavigationService navigation) : IDisposable,IAsyncDisposable
{
    public const long MemoryLimit=96*1024*1024;
    private readonly object sync=new();
    private readonly List<Session> sessions=[];
    private Session? current;
    private bool disposed;
    private long starts,decoded,hits;
    public AccuratePreviewStatistics Statistics
    {get{lock(sync)return new(Interlocked.Read(ref starts),Interlocked.Read(ref decoded),Interlocked.Read(ref hits),current?.BufferedBytes??0,current?.Running??false);}}

    public async Task<PreviewPixels> FrameAsync(Toolchain tools,MediaInfo media,SourceFramePosition frame,string filter="",int? width=null,int? height=null,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var file=new FileInfo(media.Path);
        var key=$"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{media.Video!.Index}|{tools.FFmpegPath}|{tools.Version}|{filter}|{width}|{height}";
        var first=await navigation.PrerollAsync(tools,media,frame,ct).ConfigureAwait(false);
        Session session;
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed,this);ct.ThrowIfCancellationRequested();
            if(current is null||!current.CanServe(key,frame))
            {
                current?.Stop();sessions.RemoveAll(s=>s.Completion.IsCompleted);
                current=new Session(key,tools,media,first,frame,filter,width,height,()=>Interlocked.Increment(ref decoded),()=>Interlocked.Increment(ref hits));
                sessions.Add(current);Interlocked.Increment(ref starts);
            }
            session=current;
        }
        return await session.GetAsync(frame,ct).ConfigureAwait(false);
    }

    // Selection changes and native playback release the accurate decoder immediately.
    public void Reset(){lock(sync){current?.Stop();current=null;}}
    public async ValueTask DisposeAsync()
    {
        Session[] pending;
        lock(sync){if(disposed)return;disposed=true;pending=sessions.ToArray();foreach(var s in pending)s.Stop();current=null;}
        await Task.WhenAll(pending.Select(s=>s.Completion)).ConfigureAwait(false);
    }
    public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed class Session
    {
        private static readonly Regex Timestamp=new(@"\bpts:\s*(-?\d+)",RegexOptions.Compiled|RegexOptions.CultureInvariant);
        private readonly object sync=new();
        private readonly Dictionary<long,PreviewPixels> cache=[];
        private readonly Queue<long> order=new();
        private readonly CancellationTokenSource lifetime=new();
        private readonly SemaphoreSlim demand=new(0,1);
        private TaskCompletionSource changed=NewSignal();
        private readonly Action decoded,hit;
        private SourceFramePosition requested;
        private readonly SourceFramePosition first;
        private long last=long.MinValue,bytes;
        private double lastSeconds;
        private bool ended,stopped,running;
        private Exception? error;
        public string Key{get;}
        public Task Completion{get;}
        public long BufferedBytes{get{lock(sync)return bytes;}}
        public bool Running{get{lock(sync)return running;}}
        private static TaskCompletionSource NewSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Pulse(){var old=changed;changed=NewSignal();old.TrySetResult();}
        public Session(string key,Toolchain tools,MediaInfo media,SourceFramePosition first,SourceFramePosition requested,string filter,int? width,int? height,Action decoded,Action hit)
        {
            Key=key;this.first=first;this.requested=requested;this.decoded=decoded;this.hit=hit;
            Completion=Task.Run(()=>PumpAsync(tools,media,filter,width,height));
        }
        public bool CanServe(string key,SourceFramePosition frame)
        {
            lock(sync)return !stopped&&key==Key&&(cache.ContainsKey(frame.Timestamp)||
                (!ended&&error is null&&frame.Timestamp>=first.Timestamp&&frame.Timestamp>last&&frame.Seconds<=Math.Max(requested.Seconds,lastSeconds)+2));
        }
        public async Task<PreviewPixels> GetAsync(SourceFramePosition frame,CancellationToken ct)
        {
            bool initial=true;
            while(true)
            {
                ct.ThrowIfCancellationRequested();Task wait;
                lock(sync)
                {
                    if(stopped)throw new OperationCanceledException();
                    requested=frame;
                    if(demand.CurrentCount==0)demand.Release();
                    if(cache.TryGetValue(frame.Timestamp,out var image)){if(initial)hit();return image;}
                    if(error is not null)throw new InvalidOperationException(L.T("Frame-Vorschau")+": "+error.Message,error);
                    if(ended||last>frame.Timestamp)throw new InvalidDataException(L.T("Kein Frame an dieser Position gefunden."));
                    wait=changed.Task;
                }
                initial=false;await wait.WaitAsync(ct).ConfigureAwait(false);
            }
        }
        public void Stop()
        {
            lock(sync){if(stopped)return;stopped=true;cache.Clear();order.Clear();bytes=0;Pulse();}
            lifetime.Cancel();
        }
        private bool NeedMore()
        {
            if(last<=requested.Timestamp)return true;
            var ahead=cache.Values.Count(f=>f.Timestamp>requested.Timestamp);
            return ahead<8&&lastSeconds<requested.Seconds+.75&&bytes+(cache.GetValueOrDefault(last)?.Pixels.LongLength??0)<=MemoryLimit;
        }
        private async Task PumpAsync(Toolchain tools,MediaInfo media,string filter,int? width,int? height)
        {
            Process? process=null;Task? stderr=null;WindowsProcessJob? job=null;
            var timestamps=Channel.CreateBounded<long>(new BoundedChannelOptions(64){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
            var messages=new StringBuilder();
            try
            {
                var filters=new List<string>{$"select=gte(pts\\,{first.Timestamp.ToString(CultureInfo.InvariantCulture)})","showinfo@studio=checksum=0"};
                if(filter.Length>0)filters.Add(filter);
                if(media.Video!.IsHdr&&!filter.Contains("tonemap",StringComparison.Ordinal))filters.Add(FilterService.HdrToSdr());
                if(width.HasValue)filters.Add($"scale={width}:{(height.HasValue?height.Value:-2)}:flags=lanczos,setsar=1");
                filters.Add("format=bgr24");
                var args=new List<string>{"-hide_banner","-nostdin","-nostats","-v","info","-copyts"};
                if(first.DecodeFromSeconds>0)args.AddRange(["-ss",FFmpegCommandBuilder.Number(first.DecodeFromSeconds)]);
                args.AddRange(["-i",media.Path,"-map",$"0:{media.Video.Index}","-an","-sn","-dn","-vf",string.Join(",",filters),
                    "-fps_mode","passthrough","-c:v","bmp","-threads","1","-f","image2pipe","-flush_packets","1","pipe:1"]);
                var start=new ProcessStartInfo(tools.FFmpegPath){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8};
                foreach(var arg in args)start.ArgumentList.Add(arg);
                lifetime.Token.ThrowIfCancellationRequested();process=new(){StartInfo=start};
                if(!process.Start())throw new InvalidOperationException(L.T("Frame-Vorschau"));
                job=WindowsProcessJob.TryAttach(process);
                using var kill=lifetime.Token.Register(()=>Kill(process));
                lock(sync)running=true;
                stderr=ReadErrorsAsync();
                var timebase=MediaStream.Rational(media.Video.TimeBase)??throw new InvalidDataException("Missing video time base.");
                while(true)
                {
                    bool more;lock(sync)more=NeedMore();
                    if(!more)
                    {
                        if(!await demand.WaitAsync(TimeSpan.FromSeconds(20),lifetime.Token).ConfigureAwait(false))
                        {
                            lock(sync){if(NeedMore())continue;ended=true;Pulse();}
                            break;
                        }
                        continue;
                    }
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    var image=await ReadBitmapAsync(process.StandardOutput.BaseStream,timeout.Token).ConfigureAwait(false);
                    if(image is null){await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);break;}
                    var pts=await timestamps.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    var pixels=image with{Timestamp=pts};
                    lock(sync)
                    {
                        if(stopped)break;
                        last=pts;lastSeconds=pts*timebase-media.StartTime;
                        if(!cache.ContainsKey(pts)){cache.Add(pts,pixels);order.Enqueue(pts);bytes+=pixels.Pixels.LongLength;}
                        while((cache.Count>24||bytes>MemoryLimit)&&cache.Count>1)
                        {
                            var oldest=order.Dequeue();
                            if(oldest==requested.Timestamp){order.Enqueue(oldest);continue;}
                            if(cache.Remove(oldest,out var removed))bytes-=removed.Pixels.LongLength;
                        }
                        decoded();Pulse();
                    }
                }
                if(process.HasExited&&process.ExitCode!=0){await stderr.ConfigureAwait(false);throw new InvalidDataException(messages.ToString());}
                async Task ReadErrorsAsync()
                {
                    try
                    {
                        while(await process.StandardError.ReadLineAsync().ConfigureAwait(false) is {} line)
                        {
                            if(line.Contains("showinfo@studio",StringComparison.Ordinal)&&Timestamp.Match(line) is {Success:true} match)
                                await timestamps.Writer.WriteAsync(long.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture),lifetime.Token).ConfigureAwait(false);
                            else{if(messages.Length>16384)messages.Remove(0,8192);messages.AppendLine(line);}
                        }
                    }
                    catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
                    finally{timestamps.Writer.TryComplete();}
                }
            }
            catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
            catch(Exception e){lock(sync)error=e;}
            finally
            {
                // Also unblock a bounded metadata writer when stdout is intentionally no longer drained.
                lifetime.Cancel();
                try
                {
                    if(process is not null)
                    {
                        Kill(process);
                        try{await process.WaitForExitAsync().ConfigureAwait(false);}catch(InvalidOperationException){}
                        if(stderr is not null)try{await stderr.ConfigureAwait(false);}catch(Exception e){lock(sync)error??=e;}
                    }
                }
                finally{process?.Dispose();job?.Dispose();lock(sync){running=false;ended=true;Pulse();}}
            }
        }
        private static void Kill(Process process)
        {try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        private static async Task<PreviewPixels?> ReadBitmapAsync(Stream input,CancellationToken ct)
        {
            var header=new byte[54];var count=await input.ReadAsync(header.AsMemory(0,1),ct).ConfigureAwait(false);if(count==0)return null;
            await input.ReadExactlyAsync(header.AsMemory(1),ct).ConfigureAwait(false);
            int I(int offset)=>BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset));
            var size=I(2);var offset=I(10);var width=I(18);var signedHeight=I(22);
            if(header[0]!='B'||header[1]!='M'||I(14)!=40||width<=0||signedHeight==0||signedHeight==int.MinValue||
                BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(28))!=24||I(30)!=0||offset!=54||size<54||size-54>MemoryLimit)
                throw new InvalidDataException("Invalid preview bitmap header.");
            var height=Math.Abs(signedHeight);var stride=checked((width*3+3)&~3);
            if(checked((long)stride*height)!=size-54)throw new InvalidDataException("Invalid preview bitmap size.");
            var pixels=new byte[size-54];await input.ReadExactlyAsync(pixels,ct).ConfigureAwait(false);
            if(signedHeight>0)
            {
                var row=new byte[stride];
                for(int y=0;y<height/2;y++){var a=pixels.AsMemory(y*stride,stride);var b=pixels.AsMemory((height-1-y)*stride,stride);a.CopyTo(row);b.CopyTo(a);row.CopyTo(b);}
            }
            return new(0,width,height,stride,pixels);
        }
    }
}
