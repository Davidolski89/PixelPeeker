using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace FFmpegStudio.Infrastructure;

public sealed record DownloadProgress(string Phase, long Bytes, long? Total, double BytesPerSecond)
{
    public double? Percent => Total > 0 ? Bytes * 100d / Total.Value : null;
    public override string ToString() => $"{Phase} · {Bytes / 1048576d:0.0}{(Total.HasValue ? $" / {Total / 1048576d:0.0}" : "")} MiB · {BytesPerSecond / 1048576d:0.0} MiB/s";
}
public sealed record RemoteBuild(string Version, string Sha256, Uri ArchiveUri, string HashAlgorithm = "SHA256");
public sealed record VersionState(Toolchain? Active, Toolchain? Previous);

public sealed class FFmpegVersionService(AppPaths paths, ProcessRunner runner)
{
    private readonly SemaphoreSlim gate = new(1);
    public Toolchain? Active { get; private set; }
    public Toolchain? Previous { get; private set; }
    public event Action? Changed;
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var state = await AtomicFile.ReadJsonAsync<VersionState>(Path.Combine(paths.Root, "versions.json"), ct);
        Active = state?.Active; Previous = state?.Previous;
        if (Active is not null && (!File.Exists(Active.FFmpegPath) || !File.Exists(Active.FFprobePath))) Active = null;
        if (Active is not null) await ValidateAsync(Active.FFmpegPath, Active.FFprobePath, ct);
    }
    public async Task<Toolchain> ValidateAsync(string ffmpeg, string ffprobe, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var tasks = new[] { runner.RunAsync(ffmpeg, ["-version"], timeout.Token), runner.RunAsync(ffprobe, ["-version"], timeout.Token), runner.RunAsync(ffmpeg, ["-buildconf"], timeout.Token) };
        var results = await Task.WhenAll(tasks);
        foreach (var r in results) r.EnsureSuccess(L.T("FFmpeg-Installationsprüfung"));
        if (!results[0].StandardOutput.StartsWith("ffmpeg version ") || !results[1].StandardOutput.StartsWith("ffprobe version "))
            throw new InvalidDataException(L.T("Die ausgewählten Programme melden keine gültige FFmpeg-/ffprobe-Version."));
        var version = Regex.Match(results[0].StandardOutput, @"ffmpeg version (\S+)").Groups[1].Value;
        return new(version, Path.GetFullPath(ffmpeg), Path.GetFullPath(ffprobe), false, results[0].StandardOutput + results[2].StandardError + results[2].StandardOutput);
    }
    public async Task ActivateAsync(Toolchain tools, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await AtomicFile.WriteJsonAsync(Path.Combine(paths.Root, "versions.json"), new VersionState(tools, Active), ct);
            Previous = Active; Active = tools;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
    public async Task SelectLocalAsync(string ffmpeg, string ffprobe, CancellationToken ct = default) =>
        await ActivateAsync(await ValidateAsync(ffmpeg, ffprobe, ct), ct);
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        var previous = Previous ?? throw new InvalidOperationException(L.T("Keine vorherige Installation vorhanden."));
        await ValidateAsync(previous.FFmpegPath, previous.FFprobePath, ct);
        await ActivateAsync(previous, ct);
    }
}

public sealed class FFmpegDownloadService(AppPaths paths, FFmpegVersionService versions, HttpClient http) : IFmpegDistribution
{
    public const string BaseUrl = "https://www.gyan.dev/ffmpeg/builds/";
    private readonly SemaphoreSlim gate = new(1);
    public bool SupportsManagedInstall => OperatingSystem.IsWindows();
    public string SourceDescription => SupportsManagedInstall
        ? L.T("Verwaltete Installation: gyan.dev, nur Windows-Builds.")
        : L.T("Verwaltete Installation ist auf diesem System nicht verfügbar. gyan.dev liefert nur Windows-Builds. Linux-FFmpeg folgt später.");
    public async Task<RemoteBuild> CheckAsync(CancellationToken ct = default)
    {
        EnsureManagedInstallSupported();
        var version = VersionMetadata.ParseVersion(await http.GetStringAsync(BaseUrl + "ffmpeg-git-full.7z.ver", ct));
        // Pin both archive and checksum to one immutable build, avoiding an alias rollover race.
        var archive = new Uri(BaseUrl + $"packages/ffmpeg-{version}-full_build.7z");
        var hash = VersionMetadata.ParseSha256(await http.GetStringAsync(archive + ".sha256", ct));
        return new(version, hash, archive);
    }
    public async Task<Toolchain> InstallAsync(RemoteBuild build, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureManagedInstallSupported();
        await gate.WaitAsync(ct);
        var staging = Path.Combine(paths.Versions, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var archivePath = Path.Combine(staging, "download.7z");
            using (var response = await http.GetAsync(build.ArchiveUri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                if (total > 1024L * 1024 * 1024) throw new InvalidDataException(L.T("Das Build-Archiv überschreitet 1 GiB."));
                EnsureFreeSpace(staging, Math.Max((total ?? 300_000_000) * 5, 1_500_000_000));
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                var bytes = new byte[131072]; long received = 0; var watch = Stopwatch.StartNew(); long lastReport = 0;
                while (true)
                {
                    int read = await input.ReadAsync(bytes, ct); if (read == 0) break;
                    received += read;
                    if (received > 1024L * 1024 * 1024) throw new InvalidDataException(L.T("Download unerwartet groß."));
                    await output.WriteAsync(bytes.AsMemory(0, read), ct);
                    if (watch.ElapsedMilliseconds - lastReport >= 200)
                    { progress?.Report(new("Download", received, total, received / Math.Max(.001, watch.Elapsed.TotalSeconds))); lastReport = watch.ElapsedMilliseconds; }
                }
                if (total.HasValue && received != total) throw new InvalidDataException(L.T("Unvollständiger Download."));
                progress?.Report(new(L.T("SHA-256 wird geprüft"), received, total, 0));
            }
            await VerifyHashAsync(archivePath, build.Sha256, ct);
            progress?.Report(new(L.T("Archiv wird sicher entpackt"), 0, null, 0));
            var extracted = Path.Combine(staging, "extracted");
            await Task.Run(() => ExtractSafe(archivePath, extracted, ct), ct);
            var ffmpeg = Directory.GetFiles(extracted, "ffmpeg.exe", SearchOption.AllDirectories).Single();
            var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            var verified = await versions.ValidateAsync(ffmpeg, ffprobe, ct);
            var commit = build.Version.Split("-git-")[1];
            if (!verified.BuildInfo.Contains(commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T("Build-ID und installierte Programmversion stimmen nicht überein."));
            await AtomicFile.WriteJsonAsync(Path.Combine(extracted, "studio-install.json"), new { build.Version, build.Sha256, Source = build.ArchiveUri.ToString(), Installed = DateTimeOffset.UtcNow }, ct);
            var final = Path.Combine(paths.Versions, build.Version + "-" + Guid.NewGuid().ToString("N")[..8]);
            var relative = Path.GetRelativePath(extracted, Path.GetDirectoryName(ffmpeg)!);
            Directory.Move(extracted, final);
            var tools = verified with { Version = build.Version, FFmpegPath = Path.Combine(final, relative, "ffmpeg.exe"), FFprobePath = Path.Combine(final, relative, "ffprobe.exe"), Managed = true };
            await versions.ActivateAsync(tools, ct);
            progress?.Report(new(L.T("Installation abgeschlossen"), 1, 1, 0));
            return tools;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            gate.Release();
        }
    }
    private static void EnsureManagedInstallSupported()
    {
        // TODO: Linux FFmpeg acquisition. gyan.dev publishes Windows binaries only.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(L.T("Die verwaltete FFmpeg-Installation lädt derzeit nur Windows-Builds von gyan.dev."));
    }
    public static async Task VerifyHashAsync(string path, string expected, CancellationToken ct = default)
    {
        var normalized = VersionMetadata.ParseSha256(expected);
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(normalized), Convert.FromHexString(hash)))
            throw new InvalidDataException(L.T("SHA-256-Prüfung fehlgeschlagen. Das Archiv wird nicht installiert."));
    }
    public static async Task VerifyMd5Async(string path, string expected, CancellationToken ct = default)
    {
        var normalized = expected.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, "^[a-f0-9]{32}$")) throw new InvalidDataException(L.T("Ungültige MD5-Metadaten."));
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await MD5.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalized), Encoding.ASCII.GetBytes(hash)))
            throw new InvalidDataException(L.T("MD5-Prüfung fehlgeschlagen. Das Archiv wird nicht installiert."));
    }
    public static string SafeEntryPath(string root, string key)
    {
        var normalized = key.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':') ||
            normalized.Split('/').Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException(L.T("Unsicherer Archivpfad."));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(prefix, normalized));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("Archivpfad außerhalb des Installationsverzeichnisses."));
        return target;
    }
    public static void ExtractSafe(string archivePath, string root, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        long declared = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var key = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
            if (entry.IsDirectory && key is "" or ".") continue;
            _ = SafeEntryPath(root, entry.Key ?? "");
            if (entry.LinkTarget is not null || entry.IsEncrypted) throw new InvalidDataException(L.T("Links und verschlüsselte Archive sind nicht zulässig."));
            declared += entry.Size;
            if (declared > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(L.T("Entpacktes Archiv überschreitet 4 GiB."));
        }
        if (archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            long total = 0;
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                var target = SafeEntryPath(root, reader.Entry.Key ?? "");
                if (reader.Entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                WriteEntry(root, reader.Entry.Key ?? "", reader.OpenEntryStream(), ref total, ct);
            }
            return;
        }
        long unpacked = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var key = (entry.Key ?? "").Replace('\\', '/').TrimEnd('/');
            if (entry.IsDirectory && key is "" or ".") continue;
            if (entry.IsDirectory) { Directory.CreateDirectory(SafeEntryPath(root, entry.Key ?? "")); continue; }
            WriteEntry(root, entry.Key ?? "", entry.OpenEntryStream(), ref unpacked, ct);
        }
    }
    private static void WriteEntry(string root, string key, Stream input, ref long total, CancellationToken ct)
    {
        var target = SafeEntryPath(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using (input)
        using (var output = new FileStream(target, FileMode.CreateNew))
        {
            var buffer = new byte[131072];
            int n;
            while ((n = input.Read(buffer)) > 0)
            {
                ct.ThrowIfCancellationRequested(); total += n;
                if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(L.T("Archiv überschreitet das Entpacklimit."));
                output.Write(buffer, 0, n);
            }
        }
    }
    public static void EnsureFreeSpace(string target, long bytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!);
        if (drive.IsReady && drive.AvailableFreeSpace < bytes) throw new IOException(L.F($"Nicht genügend freier Speicherplatz. Mindestens {bytes / 1073741824d:0.0} GiB erforderlich."));
    }
}
