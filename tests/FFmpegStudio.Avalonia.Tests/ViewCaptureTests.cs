using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FFmpegStudio.Avalonia;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ViewCaptureTests
{
    [AvaloniaFact]
    public async Task Capture_main_views_and_dialogs()
    {
        if (Environment.GetEnvironmentVariable("PIXELPEEKER_CAPTURE") != "1") return;
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-capture-" + Guid.NewGuid().ToString("N"));
        var output = Environment.GetEnvironmentVariable("PIXELPEEKER_CAPTURE_DIR") ?? "/tmp/pixelpeeker-views";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(output);
        var previous = Environment.GetEnvironmentVariable(AppIdentity.DataEnvironmentVariable);
        ServiceProvider? provider = null;
        try
        {
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, root);
            provider = new ServiceCollection()
                .AddPixelPeeker(new AppPaths(root))
                .AddSingleton<IUiMarshal, DirectUiMarshal>()
                .AddSingleton<IPlaybackSession, UnavailablePlaybackSession>()
                .AddSingleton<IEditorPrompts, RejectingPrompts>()
                .AddSingleton<EditorViewModel>()
                .BuildServiceProvider();
            var vm = provider.GetRequiredService<EditorViewModel>();
            await vm.InitializeAsync();
            var info = new MediaInfo
            {
                Path = Path.Combine(root, "clip.mkv"),
                Duration = 12.5,
                Size = 18_000_000,
                Container = "matroska",
                Streams =
                [
                    new MediaStream { Type = "video", Codec = "h264", Width = 1920, Height = 1080, AverageFrameRate = "24000/1001" },
                    new MediaStream { Index = 1, Type = "audio", Codec = "aac", Channels = 2, SampleRate = 48000, Tags = new Dictionary<string, string> { ["language"] = "deu", ["title"] = "Deutsch" } },
                    new MediaStream { Index = 2, Type = "subtitle", Codec = "subrip", Tags = new Dictionary<string, string> { ["language"] = "deu" } }
                ]
            };
            var options = EncodingOptions.ForMedia(info);
            options.Video.Encoder = "libx265";
            options.Video.Quality = 23;
            vm.Media.Add(new EditorMedia(info.Path, info, options));
            vm.SelectedMedia = vm.Media[0];
            vm.Jobs.Add(new EncodingJob
            {
                Source = info,
                Options = options,
                Status = JobStatus.Läuft,
                Progress = 42,
                Speed = "1.2x",
                Fps = 36,
                Remaining = "00:01:20",
                OutputPath = Path.Combine(root, "PixelPeeker", "clip_1080p_hevc.mkv")
            });
            vm.SelectedJob = vm.Jobs[0];
            vm.QueueSummary = QueueSummaryText.Format(vm.Jobs);
            var window = new MainWindow { DataContext = vm, Width = 1540, Height = 1020 };
            window.Show();
            Save(window, output, "01-preview");
            Select(window, "EncodeSettingsTab");
            Save(window, output, "02-encode-video");
            SelectInner(window, "AudioSettingsTab");
            Save(window, output, "03-encode-audio");
            SelectInner(window, "SubtitleSettingsTab");
            Save(window, output, "04-encode-subtitles");
            SelectInner(window, "OutputSettingsTab");
            Save(window, output, "05-encode-output");
            SelectNested(window, "PreviewTab", "FiltersSettingsTab");
            Save(window, output, "06-filters");
            Select(window, "MediaInfoTab");
            Save(window, output, "07-media-info");
            Select(window, "LargeCommandTab");
            Save(window, output, "08-command");
            Select(window, "LogTab");
            Save(window, output, "09-log");
            Select(window, "PreviewTab");
            Save(window, output, "10-queue-and-preview");

            StudioDialogs.OpenedPreview = dialog =>
            {
                var name = string.Concat((dialog.Title ?? "dialog").Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
                Save(dialog, output, "dialog-" + name);
                dialog.Close();
            };
            await StudioDialogs.EditSettingsAsync(window, Json.Clone(vm.Settings), info, options);
            await StudioDialogs.ManageFfmpegAsync(window, vm);
            await StudioDialogs.ShowTextAsync(window, L.T("FFmpeg-Protokoll"), "frame=120 fps=36 speed=1.2x");
            await StudioDialogs.ShowResultsAsync(window, vm.Jobs);
            await StudioDialogs.ShowAnalysisAsync(window, new QualityResult(92.5, 0.982, 41.2, "VMAF · Beispiel", [new QualityFrame(1.2, 90, 0.97, 40), new QualityFrame(2.4, 88, 0.96, 39)], ""));
            await StudioDialogs.ShowComparisonAsync(window, new EncodeComparisonSession(provider.GetRequiredService<EncodeComparisonService>(), provider.GetRequiredService<LoggingService>()), () => Task.FromResult<EncodeComparisonInput?>(null));
            await StudioDialogs.ConfirmCountdownAsync(window, CompletionAction.Benachrichtigung);
            window.Close();
        }
        finally
        {
            StudioDialogs.OpenedPreview = null;
            if (provider is not null) await provider.DisposeAsync();
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Select(MainWindow window, string name)
    {
        var tab = window.FindControl<TabItem>(name);
        var tabs = window.FindControl<TabControl>("MainPreviewTabs");
        if (tab is not null && tabs is not null) tabs.SelectedItem = tab;
    }

    private static void SelectNested(MainWindow window, string outer, string inner)
    {
        Select(window, outer);
        SelectChild(window, inner);
    }

    private static void SelectInner(MainWindow window, string name)
    {
        Select(window, "EncodeSettingsTab");
        SelectChild(window, name);
    }

    private static void SelectChild(MainWindow window, string name)
    {
        var tab = window.FindControl<TabItem>(name);
        if (tab is null) return;
        var parent = tab.GetVisualParent();
        while (parent is not null and not TabControl)
            parent = parent.GetVisualParent();
        if (parent is TabControl tabs) tabs.SelectedItem = tab;
    }

    private static void Save(TopLevel window, string directory, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var frame = window.CaptureRenderedFrame() ?? window.GetLastRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(directory, name + ".png"));
    }
}
