using Avalonia;
using Avalonia.Headless;
using FFmpegStudio.Avalonia;
using FFmpegStudio.Core;

[assembly: AvaloniaTestApplication(typeof(AvaloniaTestHost))]

public class AvaloniaTestHost
{
    static AvaloniaTestHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "pixelpeeker-avalonia-headless");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, root);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true });
}
