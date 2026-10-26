using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FFmpegStudio.Avalonia;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Xunit;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task Main_window_shows_an_imported_file_and_the_phase_note()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-avalonia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable(AppIdentity.DataEnvironmentVariable);
        var language = L.Language;
        try
        {
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, root);
            var paths = new AppPaths(root);
            var log = new LoggingService(paths);
            var vm = new LibraryViewModel(new SettingsService(paths, log), new QueueStore(paths),
                new FFmpegVersionService(paths, new ProcessRunner()), new MediaProbeService(new ProcessRunner()),
                new FFmpegCommandBuilder(), new FFmpegDownloadService(paths, new FFmpegVersionService(paths, new ProcessRunner()), new HttpClient()),
                log, new DirectUiMarshal());
            await vm.InitializeAsync();
            var window = new MainWindow { DataContext = vm };
            window.Show();
            var note = window.FindControl<TextBlock>("PhaseNote");
            var open = window.FindControl<Button>("OpenFileButton");
            Assert.NotNull(note);
            Assert.NotNull(open);
            Assert.Equal(AppIdentity.Name, window.Title);
            Assert.False(string.IsNullOrWhiteSpace(note!.Text));
            Assert.Contains("FFmpeg", note.Text);
            var file = Path.Combine(root, "clip.mkv");
            await File.WriteAllBytesAsync(file, [9, 8, 7, 6]);
            await vm.ImportPathsAsync([file]);
            var details = window.FindControl<TextBlock>("MediaDetailsText");
            Assert.NotNull(details);
            Assert.Contains("clip.mkv", details!.Text);
            Assert.DoesNotContain("h264", details.Text);
            Assert.Null(Assert.Single(vm.Media).Info);
            var status = window.FindControl<TextBlock>("StatusText");
            Assert.Contains("1", status?.Text);
        }
        finally
        {
            L.SetLanguage(language);
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("ffstudio-avalonia-", StringComparison.Ordinal))
                Directory.Delete(root, true);
        }
    }
}
