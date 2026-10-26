using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? provider;
    private Mutex? mutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeRoot=e.Args.Length>=2&&e.Args[0]=="--smoke-test"?Environment.GetEnvironmentVariable("FFMPEG_STUDIO_DATA"):null;
        var mutexName=string.IsNullOrWhiteSpace(smokeRoot)?AppIdentity.SingleInstanceMutex:
            "Local\\FFmpegStudio.Smoke."+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(smokeRoot).ToUpperInvariant())))[..16];
        mutex = new Mutex(true,mutexName,out var created);
        if (!created) { MessageBox.Show(L.T("PixelPeeker läuft bereits."), "PixelPeeker"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            provider?.GetService<LoggingService>()?.Write(L.T("Unerwarteter Oberflächenfehler."), args.Exception);
            if(e.Args.Length>=2&&e.Args[0]=="--smoke-test")
            {
                Directory.CreateDirectory(e.Args[1]);File.WriteAllText(Path.Combine(e.Args[1],"error.txt"),args.Exception.ToString());
                args.Handled=true;Shutdown(1);return;
            }
            MessageBox.Show(args.Exception.Message, "PixelPeeker", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        provider = CreateServices(new AppPaths());
        var settings = provider.GetRequiredService<SettingsService>();
        await settings.LoadAsync();
        ApplyLanguage(settings.Current.Language);
        ApplyTheme(settings.Current.ThemeId);
        ButtonHints.Apply(settings.Current);
        var window = provider.GetRequiredService<MainWindow>();
        MainWindow = window; window.Show();
        await window.ViewModel.InitializeAsync();
        if (e.Args.Length >= 2 && e.Args[0] == "--smoke-test")
        {
            var testSource=e.Args.Length>=3?e.Args[2]:Directory.EnumerateDirectories(provider.GetRequiredService<AppPaths>().Root,"integration-*")
                .OrderByDescending(p=>p).SelectMany(p=>Directory.EnumerateFiles(p,"Quelle*.mkv")).FirstOrDefault();
            if(testSource is not null) await window.ViewModel.ImportPathsAsync([testSource]);
            await window.ViewModel.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var importedCodecs=window.ViewModel.SelectedMedia is {} importedItem?new
            {
                Video=Json.Clone(importedItem.Options.Video),
                Audio=importedItem.Options.Audio.Select(a=>new{a.Encoder,Settings=AudioCodecSettings.From(a)}).ToArray(),
                Subtitles=importedItem.Options.Subtitles.Select(s=>s.Encoder).ToArray()
            }:null;
            window.ViewModel.PrepareSmokeDisplay();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Directory.CreateDirectory(e.Args[1]);
            void Capture(string name)
            {
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(e.Args[1], name)); encoder.Save(output);
            }
            Capture("desktop.png");
            object? seekingSmoke=null;
            if(e.Args.Contains("--seek"))seekingSmoke=await CheckPublishedSeekingAsync(window,e.Args.Contains("--idle-seek"));
            object? playbackSmoke=null;
            if(e.Args.Contains("--playback"))playbackSmoke=await CheckPublishedPlaybackAsync(window,e.Args[1]);
            var settingTabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs");
            var workspaceTabs=(TabControl)window.FindName("MainPreviewTabs");
            workspaceTabs.SelectedItem=window.FindName("EncodeSettingsTab");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); Capture("encode-settings.png");
            settingTabs.SelectedItem=window.FindName("OutputSettingsTab");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); Capture("output-settings.png");
            workspaceTabs.SelectedItem=window.FindName("LargeCommandTab");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); Capture("command.png");
            object? cutSmoke=null;
            if(e.Args.Contains("--cuts")&&window.ViewModel.SelectedMedia is {} item)
            {
                var vm=window.ViewModel;var original=vm.DisplayedPreviewFrame!;
                var duration=item.Info.Duration??throw new InvalidDataException("Smoke source has no duration.");
                vm.PreviewSeconds=duration*.25;await vm.WaitForPreviewAsync();vm.MarkCutStartCommand.Execute(null);
                vm.PreviewSeconds=duration*.5;await vm.WaitForPreviewAsync();
                if(!vm.MarkCutEndCommand.CanExecute(null))throw new InvalidOperationException("Smoke cut markers were not confirmed.");
                vm.MarkCutEndCommand.Execute(null);
                item.Options.Video.Encoder="libsvtav1";item.Options.Video.Preset="13";item.Options.Video.Quality=30;
                item.Options.OutputDirectory=Path.Combine(e.Args[1],"export");item.Options.FileNamePattern="{name}_CutSmoke";
                var queue=provider.GetRequiredService<QueueService>();var job=await queue.AddAsync(item.Info,item.Options);
                queue.Start(job);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while(job.Status is JobStatus.Wartend or JobStatus.Läuft)await Task.Delay(50,timeout.Token);
                if(job.Status!=JobStatus.Abgeschlossen)throw new InvalidDataException("Published cut export failed: "+job.Error);
                workspaceTabs.SelectedItem=window.FindName("PreviewTab");
                ((TabControl)window.FindName("PreviewSettingsTabs")).SelectedItem=window.FindName("CutSettingsTab");
                vm.PreviewSeconds=original.Seconds;await vm.WaitForPreviewAsync();
                await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();Capture("cuts.png");
                cutSmoke=new{job.Status,job.OutputPath,OutputDuration=job.OutputInfo!.Duration,ExpectedDuration=CutTimeline.Duration(job.Source,job.Options),Ranges=job.Options.Cuts,job.ExecutedCommand};
            }
            object? encodeComparisonSmoke=null;
            if(e.Args.Contains("--compare"))encodeComparisonSmoke=await CheckPublishedEncodeComparisonAsync(window,e.Args[1]);
            object? testEncodeNoticeSmoke=null;
            if(e.Args.Contains("--test-notice"))testEncodeNoticeSmoke=await CheckPublishedTestEncodeNoticeAsync(window,provider,e.Args[1]);
            await File.WriteAllTextAsync(Path.Combine(e.Args[1], "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                NativeWpf = true, Language = L.Language, window.ActualWidth, window.ActualHeight, Imported = window.ViewModel.Media.Count,
                ApplicationName=window.Title,ExecutableName=Path.GetFileName(Environment.ProcessPath),
                TooltipsEnabled = ToolTipService.GetIsEnabled(window.FindName("PlaySourceButton") as DependencyObject ?? window),
                TooltipDelayMilliseconds = ToolTipService.GetInitialShowDelay(window.FindName("PlaySourceButton") as DependencyObject ?? window),
                ButtonHelpCount = ButtonHelpCatalog.All.Count,
                GlobalFileNaming = window.ViewModel.Settings.FileNaming.Enabled,
                OutputFileName = window.ViewModel.OutputFileNamePreview,
                IndividualNamingEnabled = window.ViewModel.CanEditFileNamePattern,
                PreviewFrame = window.ViewModel.DisplayedPreviewFrame,PreviewLoading = window.ViewModel.PreviewLoading,
                PreviewStatus = window.ViewModel.PreviewStatus,
                Toolchain = window.ViewModel.InstalledVersion, Status = window.ViewModel.StatusText,
                VideoEncoders = window.ViewModel.VideoEncoders.Select(e => e.Name).ToArray(),
                SettingsTabCount = settingTabs.Items.Count, CommandPreview = window.ViewModel.DisplayedCommandText,
                WorkspaceTabs=workspaceTabs.Items.Cast<TabItem>().Select(t=>t.Name).ToArray(),
                PreviewSettingsTabCount=((TabControl)window.FindName("PreviewSettingsTabs")).Items.Count,
                CutSmoke=cutSmoke,
                SeekingSmoke=seekingSmoke,
                PlaybackSmoke=playbackSmoke,
                EncodeComparisonSmoke=encodeComparisonSmoke,
                TestEncodeNoticeSmoke=testEncodeNoticeSmoke,
                ImportedCodecSettings=importedCodecs,
                SvtAv1Presets = window.ViewModel.Capabilities?.Find("libsvtav1")?.Options?.FirstOrDefault(o=>o.Name=="preset")?.Choices
            }, Json.Options));
            window.Close();
        }
    }
    internal static ServiceProvider CreateServices(AppPaths paths)
    {
        var services = new ServiceCollection();
        services.AddPixelPeeker(paths);
        services.AddSingleton<SourcePlaybackService>();
        services.AddSingleton<IPlaybackSession>(sp => sp.GetRequiredService<SourcePlaybackService>());
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
    public static AppTheme CurrentTheme { get; private set; } = ThemeCatalog.Resolve(ThemeCatalog.DefaultId);
    public static void ApplyTheme(bool dark) => ApplyTheme(dark ? ThemeCatalog.DefaultId : ThemeCatalog.LightId);
    public static void ApplyTheme(string? id)
    {
        var theme = ThemeCatalog.Resolve(id); CurrentTheme = theme;
        void Set(object key, string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Current.Resources[key] = new SolidColorBrush(color);
        }
        foreach (var (key, value) in new[] { ("Bg",theme.Bg),("Surface",theme.Surface),("Raised",theme.Raised),
            ("Border",theme.Border),("Text",theme.Text),("Muted",theme.Muted),("Accent",theme.Accent),
            ("AccentText",theme.AccentText),("Warning",theme.Warning),("Selection",theme.Selection),("SelectionText",theme.SelectionText) }) Set(key,value);
        Set(SystemColors.WindowBrushKey,theme.Raised); Set(SystemColors.WindowTextBrushKey,theme.Text);
        Set(SystemColors.ControlBrushKey,theme.Raised); Set(SystemColors.ControlTextBrushKey,theme.Text);
        Set(SystemColors.HighlightBrushKey,theme.Selection); Set(SystemColors.HighlightTextBrushKey,theme.SelectionText);
        Set(SystemColors.InactiveSelectionHighlightBrushKey,theme.Selection); Set(SystemColors.InactiveSelectionHighlightTextBrushKey,theme.SelectionText);
    }
    public static void ApplyLanguage(string? code)
    {
        var previous = L.Language;
        L.SetLanguage(code);
        Current.Resources["UiFontFamily"]=new FontFamily(L.Language switch
        {
            "zh"=>"Microsoft YaHei UI, Microsoft YaHei, Global User Interface",
            "ja"=>"Yu Gothic UI, Yu Gothic, MS Gothic, Global User Interface",
            _=>"Segoe UI, Global User Interface"
        });
        foreach (var source in L.Sources) Current.Resources[L.ResourceKey(source)] = L.T(source);
        LanguageContext.Instance.Refresh();
        foreach (var window in Current.Windows.OfType<Window>())
        {
            window.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty,"UiFontFamily");
            window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
            if (window is MainWindow main) main.ViewModel.RefreshLanguage(previous);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        // Async background shutdown is awaited by MainWindow before reaching here.
        mutex?.Dispose();
        base.OnExit(e);
    }
}
