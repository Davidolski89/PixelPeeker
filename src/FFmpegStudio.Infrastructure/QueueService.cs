using System.Collections.Concurrent;
using System.Text;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class QueueService(AppPaths paths, QueueStore store, SettingsService settings, FFmpegVersionService versions,
    FFmpegCapabilityService capabilities, HardwareDetectionService hardware, FFmpegCommandBuilder builder,
    ProcessRunner runner, MediaProbeService probe, LoggingService log) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly List<EncodingJob> jobs = [];
    private readonly Dictionary<Guid, (CancellationTokenSource Cancellation, Task Work)> running = [];
    private readonly HashSet<Guid> requested = [];
    private readonly CancellationTokenSource lifetime = new();
    private Task? scheduler;
    private Task? disposal;
    private bool runAll;
    private bool batchActive;
    public event Action<EncodingJob>? JobAdded;
    public event Action<EncodingJob>? JobChanged;
    public event Action<EncodingJob>? JobCompleted;
    public event Action? BatchCompleted;
    public IReadOnlyList<EncodingJob> Jobs { get { lock (gate) return jobs.OrderBy(j => j.Order).ToArray(); } }
    public bool IsRunning { get { lock (gate) return running.Count > 0; } }
    public async Task InitializeAsync()
    {
        await store.InitializeAsync();
        var restored = await store.LoadJobsAsync();
        lock (gate) jobs.AddRange(restored);
        foreach (var job in restored) await store.SaveJobAsync(job);
        scheduler = Task.Run(SchedulerAsync);
    }
    public async Task<EncodingJob> AddAsync(MediaInfo source, EncodingOptions options, CancellationToken ct = default)
    {
        var tools = versions.Active ?? throw new InvalidOperationException(L.T("Bitte zuerst FFmpeg installieren oder auswählen."));
        var snapshot = Json.Clone(options);
        if(settings.Current.FileNaming.Enabled)snapshot.FileNamePattern=FileNaming.Pattern(settings.Current.FileNaming);
        snapshot.FileNameTimestamp=DateTimeOffset.Now;
        var caps = await capabilities.AnalyzeAsync(tools, ct);
        var errors = builder.Validate(source, snapshot, caps);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        var suggested = SuggestedOutputPath(source, snapshot);
        var directory = Path.GetDirectoryName(suggested)!;
        var stem = Path.GetFileNameWithoutExtension(suggested);
        Directory.CreateDirectory(directory);
        string output;
        EncodingJob job;
        lock (gate)
        {
            output = Path.Combine(directory, stem + "." + snapshot.Container);
            int suffix = 2;
            while (File.Exists(output) || jobs.Any(j => j.OutputPath.Equals(output, StringComparison.OrdinalIgnoreCase)) ||
                   output.Equals(source.Path, StringComparison.OrdinalIgnoreCase))
                output = Path.Combine(directory, $"{stem}_{suffix++}.{snapshot.Container}");
            job = new() { Source = source, Options = snapshot, Tools = tools, OutputPath = output, Order = jobs.Count == 0 ? 0 : jobs.Max(j => j.Order) + 1 };
            job.PartialPath = PartialPath(job);
            job.LogPath = Path.Combine(paths.Logs, job.Id + ".log");
            jobs.Add(job);
        }
        try { await store.SaveJobAsync(job); }
        catch { lock (gate) jobs.Remove(job); throw; }
        JobAdded?.Invoke(job);
        return job;
    }
    public static string SuggestedOutputPath(MediaInfo source, EncodingOptions options, FileNamingSettings? naming = null)
    {
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory) ? Path.Combine(Path.GetDirectoryName(source.Path)!, AppIdentity.Name) : options.OutputDirectory);
        var pattern=naming is {Enabled:true}?FileNaming.Pattern(naming):options.FileNamePattern;
        var stem=FileNaming.Render(source,options,pattern);
        return Path.Combine(directory, SanitizeFileName(stem) + "." + options.Container);
    }
    public static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*")) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) name = "Encoding";
        if (name.Length > 160) name = name[..160];
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) name = "_" + name;
        return name;
    }
    private static string PartialPath(EncodingJob job) => Path.Combine(Path.GetDirectoryName(job.OutputPath)!,
        Path.GetFileNameWithoutExtension(job.OutputPath) + ".unvollstaendig-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(job.OutputPath));
    public void StartAll() { lock (gate) { runAll = true; batchActive = true; } }
    public void Start(EncodingJob job)
    {
        lock (gate)
        {
            if (!jobs.Contains(job)) return;
            if (job.Status is JobStatus.Läuft or JobStatus.Abgeschlossen) return;
            job.Status = JobStatus.Wartend; job.Error = ""; job.Progress = null;
            requested.Add(job.Id); batchActive = true;
        }
        JobChanged?.Invoke(job);
    }
    public int StartMany(IEnumerable<EncodingJob> selection)
    {
        var selected = selection.DistinctBy(j => j.Id).ToArray();
        var changed = new List<EncodingJob>();
        lock (gate)
        {
            foreach (var job in selected.Where(j => jobs.Contains(j) && j.Status is not (JobStatus.Läuft or JobStatus.Abgeschlossen)))
            {
                job.Status = JobStatus.Wartend; job.Error = ""; job.Progress = null;
                requested.Add(job.Id); changed.Add(job);
            }
            if (changed.Count > 0) batchActive = true;
        }
        foreach (var job in changed) JobChanged?.Invoke(job);
        return changed.Count;
    }
    public async Task DeferAsync(EncodingJob job)
    {
        lock (gate)
        {
            if (job.Status == JobStatus.Läuft) throw new InvalidOperationException(L.T("Ein laufender Job kann nicht zurückgestellt werden."));
            requested.Remove(job.Id);
            job.Status = job.Status == JobStatus.Zurückgestellt ? JobStatus.Wartend : JobStatus.Zurückgestellt;
        }
        await store.SaveJobAsync(job); JobChanged?.Invoke(job);
    }
    public void Cancel(EncodingJob job)
    {
        lock (gate)
        {
            requested.Remove(job.Id);
            if (running.TryGetValue(job.Id, out var item)) item.Cancellation.Cancel();
        }
    }
    public void StopAll()
    {
        lock (gate) { runAll = false; batchActive = false; requested.Clear(); foreach (var item in running.Values) item.Cancellation.Cancel(); }
    }
    public async Task RemoveAsync(EncodingJob job)
    {
        lock (gate)
        {
            if (job.Status == JobStatus.Läuft) throw new InvalidOperationException("Laufenden Job zuerst abbrechen.");
            jobs.Remove(job); requested.Remove(job.Id);
        }
        await store.DeleteJobAsync(job.Id);
    }
    public async Task MoveAsync(EncodingJob job, int targetIndex)
    {
        List<EncodingJob> ordered;
        lock (gate)
        {
            ordered = jobs.OrderBy(j => j.Order).ToList();
            ordered.Remove(job); ordered.Insert(Math.Clamp(targetIndex, 0, ordered.Count), job);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        }
        foreach (var item in ordered) await store.SaveJobAsync(item);
    }
    public async Task ReplaceOptionsAsync(EncodingJob job, EncodingOptions options)
    {
        var clone = Json.Clone(options);
        var caps = await capabilities.AnalyzeAsync(job.Tools);
        var errors = builder.Validate(job.Source, clone, caps);
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        lock (gate)
        {
            if (job.Status != JobStatus.Wartend && job.Status != JobStatus.Zurückgestellt) throw new InvalidOperationException(L.T("Nur wartende oder zurückgestellte Jobs bearbeiten."));
            // Changing the container requires a new queue entry with a new reserved output path.
            if (clone.Container != job.Options.Container) throw new ArgumentException(L.T("Für einen anderen Container einen neuen Job anlegen."));
            job.Options = clone;
        }
        await store.SaveJobAsync(job); JobChanged?.Invoke(job);
    }
    private async Task SchedulerAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                bool completed = false;
                lock (gate)
                {
                    foreach (var finished in running.Where(kv => kv.Value.Work.IsCompleted).ToList())
                    { finished.Value.Cancellation.Dispose(); running.Remove(finished.Key); }
                    if (runAll || requested.Count > 0)
                    {
                        foreach (var job in jobs.Where(j => j.Status == JobStatus.Wartend && (runAll || requested.Contains(j.Id))).OrderBy(j => j.Order))
                        {
                            if (running.Count >= Math.Clamp(settings.Current.ParallelJobs, 1, 16)) break;
                            if (job.Options.Video.IsNvenc && jobs.Count(j => j.Status == JobStatus.Läuft && j.Options.Video.IsNvenc) >= Math.Clamp(settings.Current.ParallelGpuJobs, 1, 8)) continue;
                            requested.Remove(job.Id); job.Status = JobStatus.Läuft;
                            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                            var work = Task.Run(() => RunOneAsync(job, cancellation.Token));
                            running.Add(job.Id, (cancellation, work));
                        }
                    }
                    if (batchActive && running.Count == 0 && !jobs.Any(j => j.Status == JobStatus.Wartend && (runAll || requested.Contains(j.Id))))
                    {
                        completed = jobs.Count > 0 && jobs.All(j => j.Status == JobStatus.Abgeschlossen);
                        batchActive = false; runAll = false;
                    }
                }
                if (completed) BatchCompleted?.Invoke();
                await Task.Delay(250, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { log.Write(L.T("Queue-Scheduler gestoppt."), e); }
    }
    private async Task RunOneAsync(EncodingJob job, CancellationToken ct)
    {
        job.StartedAt = DateTimeOffset.UtcNow; job.EndedAt = null; job.Error = ""; job.Progress = null;
        job.OutputInfo = null; job.Analysis = null;
        job.ExecutedCommand = "";
        job.Scheduling = null;
        job.PartialPath = PartialPath(job);
        JobChanged?.Invoke(job);
        try
        {
            await store.SaveJobAsync(job);
            var expected = SizeEstimator.Estimate(job.Source, job.Options);
            FFmpegDownloadService.EnsureFreeSpace(job.OutputPath, expected ?? 256 * 1048576L);
            if (File.Exists(job.OutputPath)) throw new IOException(L.T("Die Zieldatei existiert inzwischen bereits. Neuen Job mit einem freien Dateinamen anlegen."));
            var caps = await capabilities.AnalyzeAsync(job.Tools, ct);
            if (caps.Find(job.Options.Video.Encoder)?.Hardware == true)
            {
                var result = await hardware.ProbeEncoderAsync(job.Tools, job.Options.Video.Encoder, job.Options.Video.GpuIndex, job.Options.Video.PixelFormat, ct);
                if (!result.Usable) throw new InvalidOperationException(L.T("Hardwareencoder ist nicht nutzbar. Software-Alternative wählen.\n") + result.Details);
            }
            await new CutPreparationService(runner).PrepareAsync(job.Tools,job.Source,job.Options,job.PartialPath,ct);
            var args = builder.Build(job.Source, job.Options, job.PartialPath, caps);
            var duration = CutTimeline.Duration(job.Source,job.Options);
            var parser = new ProgressParser(duration);
            await using (var writer = new StreamWriter(job.LogPath, append: true, Encoding.UTF8) { AutoFlush = true })
            {
                await writer.WriteLineAsync($"{DateTimeOffset.UtcNow:O}\n{FFmpegCommandBuilder.Display(job.Tools.FFmpegPath, args)}");
                var process = runner.RunAsync(job.Tools.FFmpegPath, args, ct, line =>
                {
                    if (parser.Push(line) is not { } p) return;
                    job.Progress = p.Percent;
                    if(p.Speed>0) job.Speed = $"{p.Speed:0.00}×";
                    if(p.Fps>0) job.Fps = p.Fps;
                    if(p.Bytes>0) job.OutputBytes = p.Bytes;
                    job.Remaining = p.Remaining?.ToString(@"hh\:mm\:ss") ?? L.T("unbekannt");
                    JobChanged?.Invoke(job);
                }, line => { lock(writer) writer.WriteLine(line); }, started: () =>
                {
                    job.ExecutedCommand = FFmpegCommandBuilder.Display(job.Tools.FFmpegPath, args);
                    JobChanged?.Invoke(job);
                }, efficiencyMode: settings.Current.EfficiencyMode, schedulingApplied: scheduling =>
                {
                    job.Scheduling = scheduling;
                    lock(writer) writer.WriteLine(scheduling.Description + (scheduling.ErrorCode != 0 ? $" (Windows {scheduling.ErrorCode})" : ""));
                    JobChanged?.Invoke(job);
                });
                while (!process.IsCompleted)
                {
                    await Task.WhenAny(process, Task.Delay(2000, CancellationToken.None));
                    if (!process.IsCompleted) await store.SaveJobAsync(job);
                }
                (await process).EnsureSuccess("Encoding");
            }
            ct.ThrowIfCancellationRequested();
            var info = await probe.ProbeAsync(job.Tools, job.PartialPath, ct);
            if (info.Video is null || info.Size == 0) throw new InvalidDataException(L.T("Die Ausgabe enthält kein gültiges Video."));
            int audioCount = job.Options.Audio.Count(a => a.Action != TrackAction.Entfernen);
            int subtitleCount = job.Options.Subtitles.Count(s => s.Action is TrackAction.Kopieren or TrackAction.Encodieren);
            if (info.Streams.Count(s => s.Type == "audio") != audioCount || info.Streams.Count(s => s.Type == "subtitle") != subtitleCount)
                throw new InvalidDataException(L.T("Die Ausgabespuren stimmen nicht mit dem Job überein."));
            File.Move(job.PartialPath, job.OutputPath, overwrite: false);
            job.OutputInfo = info with { Path = job.OutputPath };
            job.OutputBytes = info.Size; job.EndedAt = DateTimeOffset.UtcNow;
            var elapsed = (job.EndedAt - job.StartedAt)!.Value.TotalSeconds;
            var saving = job.Source.Size - info.Size;
            job.Statistics = L.F($"Quelle: {job.Source.Size / 1048576d:0.00} MiB · Ausgabe: {info.Size / 1048576d:0.00} MiB · Differenz: {saving / 1048576d:0.00} MiB ({(job.Source.Size > 0 ? 100d * saving / job.Source.Size : 0):0.0} %)\n") +
                L.F($"{info.Video.Summary} · {info.BitRate / 1000d:0} kbit/s · Zeit: {elapsed:0.0} s · Ø {(elapsed > 0 ? duration / elapsed : null):0.00}×\n") +
                string.Join("\n", job.Options.Audio.Select(a => $"Audio #{a.InputIndex}: {a.Action} {a.Encoder}")) + "\n" +
                string.Join("\n", job.Options.Subtitles.Select(s => $"Untertitel #{s.InputIndex}: {s.Action}"));
            job.Status = JobStatus.Abgeschlossen; job.Progress = 100; job.Remaining = "";
        }
        catch (OperationCanceledException) { job.Status = JobStatus.Abgebrochen; job.Error = L.T("Abgebrochen. Eine unvollständige Datei wird nicht als Ergebnis verwendet."); }
        catch (Exception e) { job.Status = JobStatus.Fehlgeschlagen; job.Error = e.Message; log.Write($"Job {job.Id} fehlgeschlagen.", e); }
        finally
        {
            CutPreparationService.Cleanup(job.Source,job.Options,job.PartialPath);
            job.EndedAt ??= DateTimeOffset.UtcNow;
            try { await store.SaveJobAsync(job); } catch (Exception e) { log.Write(L.T("Jobstatus konnte nicht gespeichert werden."), e); }
            JobChanged?.Invoke(job);
        }
        if (job.Status == JobStatus.Abgeschlossen) JobCompleted?.Invoke(job);
    }
    public ValueTask DisposeAsync()
    {
        lock(gate) return new(disposal??=DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        StopAll(); await lifetime.CancelAsync();
        if (scheduler is not null) await scheduler;
        Task[] active; lock (gate) active = running.Values.Select(x => x.Work).ToArray();
        await Task.WhenAll(active);
        lifetime.Dispose();
    }
}
