using Avalonia;
using System;

namespace FFmpegStudio.Avalonia;

// TODO: Playback. LibVLCSharp.WPF cannot be used here. A later phase needs a platform playback adapter.
// TODO: Full editor. Cuts, filters, encoder controls and queue commands still live in the WPF MainViewModel.
// TODO: Linux FFmpeg acquisition. gyan.dev ships Windows builds only; see IFmpegDistribution.
class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
