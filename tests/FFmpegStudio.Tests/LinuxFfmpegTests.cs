using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FFmpegStudio.Infrastructure;
using Xunit;

public sealed class LinuxFfmpegTests
{
    [Fact]
    public void Architecture_tokens_cover_x64_and_arm64_only()
    {
        Assert.Equal("linux64", LinuxFfmpegSources.ArchitectureToken(Architecture.X64));
        Assert.Equal("linuxarm64", LinuxFfmpegSources.ArchitectureToken(Architecture.Arm64));
        Assert.True(LinuxFfmpegSources.IsSupported(Architecture.X64));
        Assert.True(LinuxFfmpegSources.IsSupported(Architecture.Arm64));
        Assert.False(LinuxFfmpegSources.IsSupported(Architecture.X86));
        Assert.False(LinuxFfmpegSources.IsSupported(Architecture.Arm));
        Assert.Throws<PlatformNotSupportedException>(() => LinuxFfmpegSources.ArchitectureToken(Architecture.Arm));
    }

    [Fact]
    public void Static_gpl_asset_ignores_shared_lgpl_and_release_branches()
    {
        var names = new[]
        {
            "ffmpeg-N-128100-gceb466cbcd-linux64-gpl-shared.tar.xz",
            "ffmpeg-N-128100-gceb466cbcd-linux64-lgpl.tar.xz",
            "ffmpeg-n8.1.3-24-g92611900bb-linux64-gpl-8.1.tar.xz",
            "ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz",
            "ffmpeg-N-128100-gceb466cbcd-linuxarm64-gpl.tar.xz",
            "checksums.sha256"
        };
        Assert.Equal("ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz", LinuxFfmpegSources.SelectStaticGplAsset(names, "linux64"));
        Assert.Equal("ffmpeg-N-128100-gceb466cbcd-linuxarm64-gpl.tar.xz", LinuxFfmpegSources.SelectStaticGplAsset(names, "linuxarm64"));
        Assert.Equal("N-128100-gceb466cbcd", LinuxFfmpegSources.VersionFromAsset("ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz"));
    }

    [Fact]
    public void Newest_autobuild_with_checksums_is_selected()
    {
        var older = ("autobuild-2026-10-25-13-00", (IReadOnlyList<string>)["checksums.sha256", "ffmpeg-N-128094-g31855b2a70-linux64-gpl.tar.xz"]);
        var newer = ("autobuild-2026-10-26-14-25", (IReadOnlyList<string>)["checksums.sha256", "ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz"]);
        var rolling = ("latest", (IReadOnlyList<string>)["checksums.sha256", "ffmpeg-master-latest-linux64-gpl.tar.xz"]);
        var broken = ("autobuild-2026-10-27-00-00", (IReadOnlyList<string>)["ffmpeg-N-128101-gabcdef1234-linux64-gpl.tar.xz"]);
        Assert.Equal("autobuild-2026-10-26-14-25", LinuxFfmpegSources.SelectAutobuild([older, newer, rolling, broken], "linux64"));
    }

    [Fact]
    public void Checksum_line_matches_the_selected_asset()
    {
        var file = """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  ffmpeg-master-latest-linux64-gpl.tar.xz
            1d2a1e90f82b870c114bff8df950b2f46749205a07c6dccfea8ce550e8df4691  ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz
            """;
        Assert.Equal("1D2A1E90F82B870C114BFF8DF950B2F46749205A07C6DCCFEA8CE550E8DF4691",
            LinuxFfmpegSources.ParseChecksum(file, "ffmpeg-N-128100-gceb466cbcd-linux64-gpl.tar.xz"));
        Assert.Equal("7fa72b652e19bf84c9461e332ea1cdf3", LinuxFfmpegSources.ParseMd5("7fa72b652e19bf84c9461e332ea1cdf3  ffmpeg-release-amd64-static.tar.xz\n"));
        Assert.Equal(new Uri("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz"), LinuxFfmpegSources.JohnVanSickleArchive("linux64"));
        Assert.Equal(new Uri("https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz"), LinuxFfmpegSources.JohnVanSickleArchive("linuxarm64"));
    }

    [Fact]
    public void Build_numbers_decide_which_managed_linux_build_is_newer()
    {
        Assert.True(LinuxFfmpegSources.IsNewer(null, "N-128100-gceb466cbcd"));
        Assert.True(LinuxFfmpegSources.IsNewer("N-128094-g31855b2a70", "N-128100-gceb466cbcd"));
        Assert.False(LinuxFfmpegSources.IsNewer("N-128100-gceb466cbcd", "N-128094-g31855b2a70"));
        Assert.True(LinuxFfmpegSources.IsNewer("jvs-linux64-abc", "N-10-gabcdef1"));
        Assert.False(LinuxFfmpegSources.IsNewer("jvs-linux64-abc", "jvs-linux64-abc"));
    }

    [Fact]
    public async Task Live_static_build_downloads_verifies_and_runs()
    {
        if (Environment.GetEnvironmentVariable("PIXELPEEKER_LIVE_FFMPEG") != "1" || !OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-live-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var runner = new ProcessRunner();
            var versions = new FFmpegVersionService(paths, runner);
            var distribution = new LinuxFfmpegDistribution(paths, versions, new HttpClient { Timeout = TimeSpan.FromMinutes(20) });
            Assert.True(distribution.SupportsManagedInstall);
            var build = await distribution.CheckAsync();
            Assert.False(string.IsNullOrWhiteSpace(build.Version));
            Assert.False(string.IsNullOrWhiteSpace(build.Sha256));
            Assert.Contains("linux", build.ArchiveUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            var tools = await distribution.InstallAsync(build);
            Assert.True(tools.Managed);
            Assert.Equal(build.Version, tools.Version);
            Assert.True(File.Exists(tools.FFmpegPath));
            Assert.True(File.Exists(tools.FFprobePath));
            var result = await runner.RunAsync(tools.FFmpegPath, ["-version"]);
            result.EnsureSuccess("ffmpeg -version");
            Assert.StartsWith("ffmpeg version ", result.StandardOutput);
            var metadataPath = Directory.GetFiles(paths.Versions, "studio-install.json", SearchOption.AllDirectories).Single();
            var metadata = await File.ReadAllTextAsync(metadataPath);
            Assert.Contains(build.Version, metadata);
            Assert.Contains(build.ArchiveUri.ToString(), metadata);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Sha256_and_md5_verification_reject_a_changed_byte()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "archive.bin");
            var body = Encoding.ASCII.GetBytes("pixelpeeker-linux-ffmpeg");
            await File.WriteAllBytesAsync(path, body);
            var sha = Convert.ToHexString(SHA256.HashData(body));
            var md5 = Convert.ToHexString(MD5.HashData(body)).ToLowerInvariant();
            await FFmpegDownloadService.VerifyHashAsync(path, sha);
            await FFmpegDownloadService.VerifyMd5Async(path, md5);
            body[0] ^= 1;
            await File.WriteAllBytesAsync(path, body);
            await Assert.ThrowsAsync<InvalidDataException>(() => FFmpegDownloadService.VerifyHashAsync(path, sha));
            await Assert.ThrowsAsync<InvalidDataException>(() => FFmpegDownloadService.VerifyMd5Async(path, md5));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
