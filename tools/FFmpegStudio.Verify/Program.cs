using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

if (args.Length < 2)
{
    Console.WriteLine("FFmpegStudio.Verify install|inspect|integration <Datenordner>");
    return;
}
var paths = new AppPaths(args[1]);
var runner = new ProcessRunner();
var versions = new FFmpegVersionService(paths, runner);
await versions.LoadAsync();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
if (args[0] == "install")
{
    var downloader = new FFmpegDownloadService(paths, versions, http);
    var remote = await downloader.CheckAsync();
    Console.WriteLine($"Build: {remote.Version}; SHA256: {remote.Sha256}; URL: {remote.ArchiveUri}");
    var last = DateTime.MinValue;
    var installed = await downloader.InstallAsync(remote, new Progress<DownloadProgress>(p =>
    {
        if ((DateTime.UtcNow - last).TotalSeconds < 3 && p.Phase == "Download") return;
        last = DateTime.UtcNow; Console.WriteLine(p);
    }));
    Console.WriteLine($"Installiert und aktiviert: {installed.FFmpegPath}");
}
else if (args[0] == "inspect")
{
    var tools = versions.Active ?? throw new Exception("Keine Installation.");
    var caps = await new FFmpegCapabilityService(runner).AnalyzeAsync(tools);
    Console.WriteLine($"Version: {tools.Version}\nModerne Videoencoder: {string.Join(", ", caps.ModernVideo.Select(e => e.Name))}\nAudioencoder: {caps.Audio.Count}\nFilter: {caps.Filters.Count}");
    foreach (var name in new[] { "h264_nvenc", "hevc_nvenc", "av1_nvenc" })
    {
        var result = await new HardwareDetectionService(runner).ProbeEncoderAsync(tools, name, 0);
        Console.WriteLine($"{name}: {result.Usable} — {result.Details}");
    }
}
else if (args[0] == "integration")
{
    await IntegrationSuite.RunAsync(paths, runner, versions);
}
