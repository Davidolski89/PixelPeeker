using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;
using SharpCompress.Compressors.Xz;

namespace FFmpegStudio.Infrastructure;

/// <summary>Selects pinned Linux FFmpeg archives. The Windows gyan.dev downloader is unchanged.</summary>
public static class LinuxFfmpegSources
{
    public const string BtbnRepository = "https://github.com/BtbN/FFmpeg-Builds";
    public const string BtbnReleasesApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases?per_page=8";
    public const string BtbnSource = "https://github.com/BtbN/FFmpeg-Builds";
    public const string FfmpegSource = "https://ffmpeg.org/download.html#get-sources";
    public const string JohnVanSicklePage = "https://johnvansickle.com/ffmpeg/";
    public const string License = "GPL-3.0-or-later";

    public static bool IsSupported(Architecture architecture) =>
        architecture is Architecture.X64 or Architecture.Arm64;

    public static string ArchitectureToken(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "linux64",
        Architecture.Arm64 => "linuxarm64",
        _ => throw new PlatformNotSupportedException(L.T("Für diese Prozessorarchitektur gibt es keinen geprüften Linux-FFmpeg-Build. Unterstützt werden x64 und arm64."))
    };

    /// <summary>Static GPL asset from an autobuild. Shared and LGPL packages are not selected.</summary>
    public static string? SelectStaticGplAsset(IEnumerable<string> names, string architectureToken)
    {
        var pattern = new Regex($@"^ffmpeg-N-\d+-g[0-9a-f]+-{Regex.Escape(architectureToken)}-gpl\.tar\.xz$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return names.FirstOrDefault(name => pattern.IsMatch(name));
    }

    public static string? VersionFromAsset(string assetName)
    {
        var match = Regex.Match(assetName, @"^(ffmpeg-)?(N-\d+-g[0-9a-f]+)-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[2].Value : null;
    }

    public static string? SelectAutobuild(IEnumerable<(string Tag, IReadOnlyList<string> Assets)> releases, string architectureToken)
    {
        foreach (var release in releases.Where(item => item.Tag.StartsWith("autobuild-", StringComparison.Ordinal)).OrderByDescending(item => item.Tag, StringComparer.Ordinal))
        {
            if (release.Assets.Contains("checksums.sha256") && SelectStaticGplAsset(release.Assets, architectureToken) is not null)
                return release.Tag;
        }
        return null;
    }

    public static string ParseChecksum(string checksumFile, string assetName)
    {
        foreach (var raw in checksumFile.Split('\n'))
        {
            var match = Regex.Match(raw.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(\S+)$");
            if (match.Success && string.Equals(match.Groups[2].Value, assetName, StringComparison.Ordinal))
                return match.Groups[1].Value.ToUpperInvariant();
        }
        throw new InvalidDataException(L.F($"Keine SHA-256-Zeile für {assetName}."));
    }

    public static string ParseMd5(string md5File)
    {
        var match = Regex.Match(md5File.Trim(), @"^([a-fA-F0-9]{32})(?:\s|$)");
        if (!match.Success) throw new InvalidDataException(L.T("Ungültige MD5-Metadaten."));
        return match.Groups[1].Value.ToLowerInvariant();
    }

    public static Uri JohnVanSickleArchive(string architectureToken) => architectureToken switch
    {
        "linux64" => new Uri("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz"),
        "linuxarm64" => new Uri("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz"),
        _ => throw new PlatformNotSupportedException(L.T("John Van Sickle veröffentlicht für diese Architektur keinen statischen Build."))
    };

    public static bool IsNewer(string? installed, string available)
    {
        static int? Number(string? text)
        {
            var match = Regex.Match(text ?? "", @"^N-(\d+)-g[0-9a-f]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
        var next = Number(available);
        if (next is null) return !string.Equals(installed?.Trim(), available.Trim(), StringComparison.OrdinalIgnoreCase);
        var current = Number(installed);
        return current is null || next.Value > current.Value;
    }
}

public static class FfmpegLocator
{
    public static string? Find(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static (string Ffmpeg, string Ffprobe)? FindPair()
    {
        var ffmpeg = Find("ffmpeg");
        var ffprobe = Find("ffprobe");
        if (ffmpeg is null || ffprobe is null) return null;
        return (ffmpeg, ffprobe);
    }
}

public sealed class LinuxFfmpegDistribution(AppPaths paths, FFmpegVersionService versions, HttpClient http) : IFmpegDistribution
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Architecture architecture = RuntimeInformation.ProcessArchitecture;

    public bool SupportsManagedInstall => OperatingSystem.IsLinux() && LinuxFfmpegSources.IsSupported(architecture);
    public string SourceDescription => SupportsManagedInstall
        ? L.T("Verwaltete Installation: BtbN/FFmpeg-Builds, statische GPL-Builds für Linux x64 und arm64, SHA-256. Ersatzquelle: John Van Sickle, statische GPL-Builds, MD5.")
        : L.T("Verwaltete Linux-Installation gibt es für x64 und arm64. Auf diesem System ist sie nicht verfügbar.");

    public async Task<RemoteBuild> CheckAsync(CancellationToken ct = default)
    {
        EnsureSupported();
        try
        {
            return await CheckBtbnAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or JsonException)
        {
            return await CheckJohnAsync(ct);
        }
    }

    public async Task<Toolchain> InstallAsync(RemoteBuild build, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureSupported();
        await gate.WaitAsync(ct);
        var staging = Path.Combine(paths.Versions, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var archivePath = Path.Combine(staging, "download.txz");
            await DownloadAsync(build.ArchiveUri, archivePath, progress, ct);
            progress?.Report(new(L.T("Prüfsumme wird geprüft"), 0, null, 0));
            if (string.Equals(build.HashAlgorithm, "MD5", StringComparison.OrdinalIgnoreCase))
                await FFmpegDownloadService.VerifyMd5Async(archivePath, build.Sha256, ct);
            else
                await FFmpegDownloadService.VerifyHashAsync(archivePath, build.Sha256, ct);
            progress?.Report(new(L.T("Archiv wird sicher entpackt"), 0, null, 0));
            var extracted = Path.Combine(staging, "extracted");
            var archiveForExtract = await Task.Run(() => UnwrapXz(archivePath, staging), ct);
            await Task.Run(() => FFmpegDownloadService.ExtractSafe(archiveForExtract, extracted, ct), ct);
            var ffmpeg = FindTool(extracted, "ffmpeg");
            var ffprobe = FindTool(extracted, "ffprobe");
            MakeExecutable(ffmpeg);
            MakeExecutable(ffprobe);
            var verified = await versions.ValidateAsync(ffmpeg, ffprobe, ct);
            var commit = Regex.Match(build.Version, @"g([0-9a-fA-F]{7,40})$").Groups[1].Value;
            if (commit.Length > 0 && !verified.BuildInfo.Contains(commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T("Build-ID und installierte Programmversion stimmen nicht überein."));
            await AtomicFile.WriteJsonAsync(Path.Combine(extracted, "studio-install.json"), new
            {
                build.Version,
                build.Sha256,
                build.HashAlgorithm,
                Source = build.ArchiveUri.ToString(),
                License = LinuxFfmpegSources.License,
                Installed = DateTimeOffset.UtcNow
            }, ct);
            var final = Path.Combine(paths.Versions, Sanitize(build.Version) + "-" + Guid.NewGuid().ToString("N")[..8]);
            var relative = Path.GetRelativePath(extracted, Path.GetDirectoryName(ffmpeg)!);
            Directory.Move(extracted, final);
            var tools = verified with
            {
                Version = build.Version,
                FFmpegPath = Path.Combine(final, relative, "ffmpeg"),
                FFprobePath = Path.Combine(final, relative, "ffprobe"),
                Managed = true
            };
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

    private async Task<RemoteBuild> CheckBtbnAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LinuxFfmpegSources.BtbnReleasesApi);
        request.Headers.UserAgent.ParseAdd("PixelPeeker");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var releases = JsonSerializer.Deserialize<List<GithubRelease>>(await response.Content.ReadAsStringAsync(ct)) ?? [];
        var token = LinuxFfmpegSources.ArchitectureToken(architecture);
        var listed = releases.Select(item => (item.TagName, (IReadOnlyList<string>)item.Assets.Select(asset => asset.Name).ToArray())).ToArray();
        var tag = LinuxFfmpegSources.SelectAutobuild(listed, token) ?? throw new InvalidDataException(L.T("Kein passender BtbN-Autobuild gefunden."));
        var release = releases.First(item => item.TagName == tag);
        var assetName = LinuxFfmpegSources.SelectStaticGplAsset(release.Assets.Select(asset => asset.Name), token)!;
        var asset = release.Assets.Single(item => item.Name == assetName);
        var checksumAsset = release.Assets.Single(item => item.Name == "checksums.sha256");
        var checksums = await http.GetStringAsync(checksumAsset.BrowserDownloadUrl, ct);
        var hash = LinuxFfmpegSources.ParseChecksum(checksums, assetName);
        var version = LinuxFfmpegSources.VersionFromAsset(assetName) ?? throw new InvalidDataException(L.T("Unbekanntes BtbN-Versionsformat."));
        return new RemoteBuild(version, hash, new Uri(asset.BrowserDownloadUrl));
    }

    private async Task<RemoteBuild> CheckJohnAsync(CancellationToken ct)
    {
        var archive = LinuxFfmpegSources.JohnVanSickleArchive(LinuxFfmpegSources.ArchitectureToken(architecture));
        var md5 = LinuxFfmpegSources.ParseMd5(await http.GetStringAsync(archive + ".md5", ct));
        var token = LinuxFfmpegSources.ArchitectureToken(architecture);
        return new RemoteBuild("jvs-" + token + "-" + md5, md5, archive, "MD5");
    }

    private async Task DownloadAsync(Uri uri, string archivePath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total > 1024L * 1024 * 1024) throw new InvalidDataException(L.T("Das Build-Archiv überschreitet 1 GiB."));
        FFmpegDownloadService.EnsureFreeSpace(archivePath, Math.Max((total ?? 300_000_000) * 5, 1_500_000_000));
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        var bytes = new byte[131072];
        long received = 0;
        var watch = Stopwatch.StartNew();
        long lastReport = 0;
        while (true)
        {
            int read = await input.ReadAsync(bytes, ct);
            if (read == 0) break;
            received += read;
            if (received > 1024L * 1024 * 1024) throw new InvalidDataException(L.T("Download unerwartet groß."));
            await output.WriteAsync(bytes.AsMemory(0, read), ct);
            if (watch.ElapsedMilliseconds - lastReport >= 200)
            {
                progress?.Report(new("Download", received, total, received / Math.Max(.001, watch.Elapsed.TotalSeconds)));
                lastReport = watch.ElapsedMilliseconds;
            }
        }
        if (total.HasValue && received != total) throw new InvalidDataException(L.T("Unvollständiger Download."));
    }

    private void EnsureSupported()
    {
        if (!SupportsManagedInstall)
            throw new PlatformNotSupportedException(SourceDescription);
    }

    internal static string UnwrapXz(string archivePath, string staging)
    {
        Span<byte> magic = stackalloc byte[6];
        using (var probe = File.OpenRead(archivePath))
        {
            if (probe.Read(magic) < 6) return archivePath;
        }
        if (magic is not [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]) return archivePath;
        var tar = Path.Combine(staging, "download.tar");
        using var input = File.OpenRead(archivePath);
        using var xz = new XZStream(input);
        using var output = new FileStream(tar, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[131072];
        long total = 0;
        int read;
        while ((read = xz.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(L.T("Entpacktes Archiv überschreitet 4 GiB."));
            output.Write(buffer, 0, read);
        }
        return tar;
    }

    internal static string FindTool(string root, string name)
    {
        var matches = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 0) throw new InvalidDataException(L.F($"Das Archiv enthält kein {name}."));
        if (matches.Count == 1) return matches[0];
        var inBin = matches.Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase)).ToList();
        if (inBin.Count == 1) return inBin[0];
        throw new InvalidDataException(L.F($"Das Archiv enthält mehrere {name}-Programme."));
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static string Sanitize(string version)
    {
        var cleaned = Regex.Replace(version, @"[^A-Za-z0-9._-]+", "-");
        return cleaned.Length == 0 ? "ffmpeg" : cleaned;
    }
}

internal sealed class GithubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("assets")] public List<GithubAsset> Assets { get; set; } = [];
}

internal sealed class GithubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
}
