using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FFmpegStudio.Avalonia;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task Main_window_shows_an_imported_file_and_the_editor()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-avalonia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable(AppIdentity.DataEnvironmentVariable);
        var language = L.Language;
        ServiceProvider? provider = null;
        try
        {
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, root);
            var paths = new AppPaths(root);
            provider = new ServiceCollection()
                .AddPixelPeeker(paths)
                .AddSingleton<IUiMarshal, DirectUiMarshal>()
                .AddSingleton<IPlaybackSession, UnavailablePlaybackSession>()
                .AddSingleton<IEditorPrompts, RejectingPrompts>()
                .AddSingleton<EditorViewModel>()
                .BuildServiceProvider();
            var vm = provider.GetRequiredService<EditorViewModel>();
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
            Assert.Contains("libvlc", note.Text);
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
            Assert.NotNull(window.FindControl<Button>("OpenFileButton"));
        }
        finally
        {
            if (provider is not null) await provider.DisposeAsync();
            L.SetLanguage(language);
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("ffstudio-avalonia-", StringComparison.Ordinal))
                Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task System_libvlc_opens_when_the_library_is_installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-vlc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var session = new AvaloniaPlaybackSession(new LoggingService(new AppPaths(root)));
            var media = new MediaInfo { Path = "/bin/true" };
            try
            {
                await session.OpenAsync(1, media, 0, 0, true, CancellationToken.None);
            }
            catch (InvalidOperationException e) when (e.Message.Contains("libvlc", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.PlayAsync(1, CancellationToken.None));
            Assert.Contains("LibVLC", session.EngineName);
            Assert.False(string.IsNullOrWhiteSpace(session.EngineName.Replace("LibVLC", "", StringComparison.Ordinal).Trim()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
