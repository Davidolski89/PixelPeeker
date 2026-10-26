using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Xunit;

namespace FFmpegStudio.Tests;

[Collection("Localization")]
public sealed class SharedLibraryTests
{
    [Fact]
    public void Media_details_include_the_probed_file_and_not_a_guess()
    {
        var previous = L.Language;
        try
        {
            L.SetLanguage("de");
            var info = new MediaInfo
            {
                Path = Path.GetFullPath("library-source.mkv"),
                Size = 4096,
                Container = "matroska",
                Duration = 1.5,
                Streams = [new() { Index = 0, Type = "video", Codec = "h264", Width = 320, Height = 240 }]
            };
            var text = MediaDetailsText.Format(info);
            Assert.Contains(info.Path, text);
            Assert.Contains("matroska", text);
            Assert.Contains("h264", text);
            var unprobed = MediaDetailsText.FormatUnprobed(info.Path);
            Assert.Contains(info.Path, unprobed);
            Assert.DoesNotContain("h264", unprobed);
            Assert.DoesNotContain("Dauer:", unprobed);
        }
        finally { L.SetLanguage(previous); }
    }

    [Fact]
    public async Task Library_lists_a_file_without_inventing_streams_and_reopens_the_queue()
    {
        var previous = L.Language;
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = Create(root);
            await vm.InitializeAsync();
            Assert.Equal("de", L.Language);
            Assert.Contains("gyan.dev", vm.DistributionNote);
            if (!OperatingSystem.IsWindows()) Assert.Contains("Linux", vm.DistributionNote);
            var file = Path.Combine(root, "clip.mkv");
            await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
            await vm.ImportPathsAsync([file]);
            var item = Assert.Single(vm.Media);
            Assert.Null(item.Info);
            Assert.Equal(file, item.Path);
            Assert.Contains(file, vm.MediaDetails);
            Assert.DoesNotContain("h264", vm.MediaDetails);
            Assert.DoesNotContain("Dauer:", vm.MediaDetails);
            Assert.Contains("4 Bytes", vm.MediaDetails);

            var stored = new EncodingJob
            {
                OutputPath = Path.Combine(root, "out.mkv"),
                Source = new MediaInfo { Path = file, Streams = [new() { Index = 0, Type = "video", Codec = "h264", Width = 16, Height = 16 }] },
                Status = JobStatus.Wartend
            };
            await new QueueStore(new AppPaths(root)).SaveJobAsync(stored);
            var reopened = Create(root);
            await reopened.InitializeAsync();
            var job = Assert.Single(reopened.Jobs);
            Assert.Equal(stored.Id, job.Id);
            Assert.Equal(JobStatus.Wartend, job.Status);
            Assert.Contains("1 Jobs", reopened.QueueSummary);
        }
        finally
        {
            L.SetLanguage(previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("ffstudio-library-", StringComparison.Ordinal))
                Directory.Delete(root, true);
        }
    }

    private static LibraryViewModel Create(string root)
    {
        var paths = new AppPaths(root);
        var log = new LoggingService(paths);
        var settings = new SettingsService(paths, log);
        var store = new QueueStore(paths);
        var runner = new ProcessRunner();
        var versions = new FFmpegVersionService(paths, runner);
        return new LibraryViewModel(settings, store, versions, new MediaProbeService(runner), new FFmpegCommandBuilder(),
            new FFmpegDownloadService(paths, versions, new HttpClient()), log, new DirectUiMarshal());
    }
}
