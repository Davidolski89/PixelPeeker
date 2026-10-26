using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Avalonia;

public partial class App : Application
{
    private ServiceProvider? provider;
    private Mutex? instance;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var prompts = new AvaloniaEditorPrompts();
            provider = new ServiceCollection()
                .AddPixelPeeker(paths)
                .AddSingleton<IUiMarshal, AvaloniaUiMarshal>()
                .AddSingleton<AvaloniaPlaybackSession>()
                .AddSingleton<IPlaybackSession>(sp => sp.GetRequiredService<AvaloniaPlaybackSession>())
                .AddSingleton<IEditorPrompts>(prompts)
                .AddSingleton<EditorViewModel>()
                .BuildServiceProvider();
            var vm = provider.GetRequiredService<EditorViewModel>();
            var window = new MainWindow { DataContext = vm };
            prompts.Owner = () => window;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                provider?.Dispose();
                instance?.Dispose();
            };
            var smoke = desktop.Args?.Contains("--smoke") == true;
            if (!TryAcquireInstance())
                vm.StatusText = L.T("PixelPeeker läuft bereits.");
            else
                _ = StartAsync(desktop, vm, window, smoke);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private bool TryAcquireInstance()
    {
        try
        {
            instance = new Mutex(true, "FFmpegStudio.Avalonia", out var created);
            return created;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, EditorViewModel vm, MainWindow window, bool smoke)
    {
        try
        {
            await vm.InitializeAsync();
            ApplyTheme(vm.Settings, window);
        }
        catch (Exception e)
        {
            provider?.GetService<LoggingService>()?.Write(L.T("Unerwarteter Oberflächenfehler."), e);
            vm.StatusText = e.Message;
        }
        if (!smoke) return;
        Console.WriteLine("PixelPeeker Avalonia smoke ready: " + window.Title + " — " + vm.StatusText);
        // Queue the exit until the desktop loop is running. Shutting down inline during startup aborts the dispatcher.
        Dispatcher.UIThread.Post(() => desktop.Shutdown(), DispatcherPriority.Background);
    }

    private void ApplyTheme(AppSettings settings, MainWindow window)
    {
        var theme = ThemeCatalog.Resolve(settings.ThemeId, settings.DarkMode);
        RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        void Set(string key, string hex)
        {
            if (Resources[key] is SolidColorBrush brush) brush.Color = Color.Parse(hex);
        }
        Set("StudioBg", theme.Bg);
        Set("StudioSurface", theme.Surface);
        Set("StudioRaised", theme.Raised);
        Set("StudioBorder", theme.Border);
        Set("StudioText", theme.Text);
        Set("StudioMuted", theme.Muted);
        Set("StudioAccent", theme.Accent);
        window.Background = new SolidColorBrush(Color.Parse(theme.Bg));
        window.Foreground = new SolidColorBrush(Color.Parse(theme.Text));
    }
}
