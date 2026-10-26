using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Desktop;
using FFmpegStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace FFmpegStudio.Desktop.Tests;

public sealed class EditorTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Native_editor_keeps_valid_options_when_encoders_and_media_change()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            // Off-screen windows need software rendering to avoid occlusion-related snapshot artifacts.
            RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/PixelPeeker;component/Themes/Studio.xaml") });
            app.Resources["SelectionValue"] = new SelectionValueConverter();
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "ffstudio-editor-" + Guid.NewGuid());
                MainViewModel? vm = null;
                try
                {
                    await using var services = App.CreateServices(new AppPaths(root));
                    vm = services.GetRequiredService<MainViewModel>();
                    vm.Settings.CheckUpdates = false;
                    await services.GetRequiredService<SettingsService>().SaveAsync();
                    var errors = new List<Exception>();
                    app.DispatcherUnhandledException += (_, e) => { errors.Add(e.Exception); e.Handled = true; };
                    var window = services.GetRequiredService<MainWindow>();
                    window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000;
                    window.Show();
                    Assert.Equal(AppIdentity.Name,window.Title);
                    Assert.Equal("PixelPeeker",typeof(App).Assembly.GetName().Name);
                    Assert.Contains(Descendants<TextBlock>(window),t=>t.Text==AppIdentity.Name);
                    Assert.NotNull(window.Icon);
                    await vm.InitializeAsync();
                    output.WriteLine("Start und Einstellungen geladen.");
                    vm.ApplyCapabilities(TestCapabilities());
                    var first = Item("first.mkv", "libx265", "slow", "yuv420p");
                    var second = Item("second.mkv", "libx264", "fast", "yuv420p");
                    vm.Media.Add(first); vm.Media.Add(second); vm.SelectedMedia = first;
                    await Flush();
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="languages-only")
                    {await CheckLanguages(vm,window,services);Assert.Empty(errors);return;}
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="playback-only")
                    {await CheckPlayback(vm,window,services);return;}
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="comparison-only")
                    {await CheckEncodeComparison(vm,window,services);Assert.Empty(errors);return;}
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="test-notification-only")
                    {await App.CheckPublishedTestEncodeNoticeAsync(window,services,Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS")??root,true);Assert.Empty(errors);return;}
                    await CheckSettingsWorkspace(vm,window);
                    await CheckAdvancedCodecLayout(vm,window,first);
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="layout-only"){Assert.Empty(errors);return;}
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="encoder-controls")
                    {await SelectWorkspace(window,"EncodeSettingsTab");await CheckVideoInputs(vm,window,first);await CheckSvtPresets(vm,window,first);await CheckSvtAdvanced(vm,window,first);await CheckThreadControls(vm,window,first,services);await CheckCodecMemory(vm,window,services);await CheckPresetEncoders(vm,window,services);Assert.Empty(errors);return;}
                    await CheckTooltips(vm,window,services);
                    output.WriteLine("Tooltips: vollständige Aktionshilfen in DE/EN/PL, deaktivierte Buttons, globale Wartezeit, Ausschalten, Dialoge und gespeicherte Einstellungen bestanden.");
                    await CheckFileNaming(vm,window,services);
                    output.WriteLine("Dateinamen: zehn Angaben, DE/EN/PL, Vorschau, Speichern/Abbrechen, Qualitäts-/Bitratenmodus und unveränderte alte Aufträge bestanden.");
                    await CheckFramePreview(vm,window,services);
                    output.WriteLine("Frame-Vorschau: echter VFR-Seek, Vor/Zurück, Pfeiltasten, Filtermodus, schnelle Zeitwechsel, Dateiauswahl, Ladeanzeige und DE/EN/PL bestanden.");
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="preview")return;
                    await CheckCodecMemory(vm,window,services);
                    output.WriteLine("Codec-Speicher: SVT/NVENC, alle Videooptionen, Audioqualität/-bitrate, Sprachwechsel, dauerhafte Speicherung und Vorbelegung neuer Dateien bestanden.");
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="codecs")return;
                    await CheckPlayback(vm,window,services);
                    output.WriteLine("Quellwiedergabe: echtes Video/Audio, schnelle Suche, Pause/Stopp zu exakten Frames, MKV/HEVC/AV1/VFR/TS, Schnittmarken und Quellenwechsel bestanden.");
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_FOCUS")=="playback")return;
                    await CheckEncodeComparison(vm,window,services);
                    output.WriteLine("Encode-Vergleich: echte QP40-/QP50-Jobs plus Original, gleiche Quellframes, DE/EN/PL, Pixelzoom/Panning, A/B ohne Laden, Großansicht, Dateien und fehlende Ausgabe bestanden.");
                    await SelectWorkspace(window,"EncodeSettingsTab");
                    var encoder = Descendants<ComboBox>(window).Single(c => c.SelectedValuePath == "Name");
                    Assert.DoesNotContain(vm.VideoEncoders, e => e.Name is "av1_vulkan" or "h264_vaapi" or "hevc_d3d12va" or "av1_mf" or "h264_v4l2m2m");
                    first.Options.Video.Encoder = "av1_vulkan"; await Flush();
                    Assert.Contains("nicht angeboten", vm.ValidationText);
                    Assert.Contains("av1_vulkan", vm.EncoderInfo);
                    encoder.SetCurrentValue(ComboBox.SelectedValueProperty, "libx265"); await Flush();
                    Assert.Equal("libx265", first.Options.Video.Encoder);
                    Assert.DoesNotContain("nicht angeboten", vm.ValidationText);
                    output.WriteLine("Entfernte Schnittstellen sind ausgeblendet; alte Encoderwerte werden verständlich abgewiesen und lassen sich ersetzen.");
                    foreach (var name in new[] { "hevc_nvenc", "libx264", "libsvtav1", "libx265", "libaom-av1", "librav1e", "libvvenc" })
                    {
                        var remembered=vm.Settings.Codecs.ForVideo(name);
                        encoder.SetCurrentValue(ComboBox.SelectedValueProperty, name);
                        await Flush();
                        Assert.Equal(name, first.Options.Video.Encoder);
                        Assert.NotNull(first.Options.Video.PixelFormat);
                        Assert.Equal(remembered.Preset, first.Options.Video.Preset);
                        Assert.Equal(remembered.Tune, first.Options.Video.Tune);
                        Assert.Equal(remembered.Profile, first.Options.Video.Profile);
                        Assert.Empty(errors);
                    }
                    first.Options.Video.Preset = "slow"; first.Options.Video.PixelFormat = "yuv420p";
                    await CheckVideoInputs(vm,window,first);
                    await CheckSvtPresets(vm,window,first);
                    await CheckSvtAdvanced(vm,window,first);
                    await CheckThreadControls(vm,window,first,services);
                    await CheckQualitySlider(vm,window,first);
                    Assert.Empty(errors);
                    output.WriteLine("Preset-Anmerkungen, originale FFmpeg-Werte und gegenseitig deaktivierte Qualität-/Bitrate-Felder bestanden.");
                    vm.SelectedMedia = second; await Flush();
                    Assert.Equal("fast", second.Options.Video.Preset);
                    Assert.Equal("libx264", encoder.SelectedValue);
                    vm.SelectedMedia = first; await Flush();
                    Assert.Equal("slow", first.Options.Video.Preset);
                    Assert.Equal("yuv420p", first.Options.Video.PixelFormat);
                    var lastAom=vm.Settings.Codecs.ForVideo("libaom-av1");
                    first.Options.Video.Encoder = "libaom-av1"; await Flush();
                    vm.SelectedMedia = second; await Flush();
                    vm.SelectedMedia = first; await Flush();
                    Assert.Equal("6", first.Options.Video.Preset);
                    vm.ApplyCapabilities(TestCapabilities()); await Flush();
                    Assert.Equal("libaom-av1", first.Options.Video.Encoder);
                    Assert.Equal("libaom-av1", encoder.SelectedValue);
                    Assert.Equal("6", first.Options.Video.Preset);
                    Assert.Equal(lastAom.PixelFormat, first.Options.Video.PixelFormat);

                    var presets = (ComboBox)window.FindName("PresetSelector");
                    output.WriteLine("Encoder-, Medienwechsel und Listenaktualisierung bestanden.");
                    foreach (var preset in vm.Presets.Where(p => vm.Capabilities!.Find(p.Options.Video.Encoder) is not null).ToArray())
                    {
                        presets.SetCurrentValue(ComboBox.SelectedItemProperty, preset); await Flush();
                        Assert.Equal(preset.Name, vm.SelectedMedia!.Options.PresetName);
                        Assert.Equal(preset.Options.Video.Encoder, vm.SelectedMedia.Options.Video.Encoder);
                    }
                    var tabs = Descendants<TabControl>(window).Single(c => c.Items.OfType<TabItem>().Any(t => Equals(t.Header, "Audio")));
                    output.WriteLine("Presetwechsel bestanden.");
                    tabs.SetCurrentValue(TabControl.SelectedIndexProperty, 1); await Flush();
                    var audio = first.Options.Audio[0]; audio.Action = TrackAction.Encodieren; audio.Channels = 2;
                    var audioEncoder = Descendants<ComboBox>(window).Single(c => ReferenceEquals(c.DataContext, audio) && ReferenceEquals(c.ItemsSource, vm.AudioEncoders));
                    foreach (var name in new[] { "libopus", "aac", "flac", "libmp3lame", "ac3", "eac3", "libopus" })
                    {
                        var previousEncoder = audio.Encoder;
                        var expectedCompression=vm.Settings.Codecs.Audio.TryGetValue(name,out var remembered)?remembered.CompressionLevel:AudioEncoding.DefaultCompression(name);
                        audioEncoder.SetCurrentValue(ComboBox.SelectedItemProperty, name); await Flush();
                        Assert.Equal(name, audio.Encoder);
                        if(previousEncoder!=name) Assert.Equal(expectedCompression, audio.CompressionLevel);
                        Assert.Equal(name != "flac", audio.CanSetBitrate);
                    }
                    audio.CompressionLevel = 5;
                    var bitrate = Descendants<ComboBox>(window).Single(c => ReferenceEquals(c.DataContext, audio) && c.IsEditable && c.ItemsSource is int[]);
                    bitrate.SetCurrentValue(ComboBox.TextProperty, "256"); await Flush();
                    Assert.Equal(256, audio.BitRateKbps); Assert.Equal(5, audio.CompressionLevel);
                    vm.ApplyCapabilities(TestCapabilities()); await Flush();
                    Assert.Equal("libopus", audio.Encoder); Assert.Equal(5, audio.CompressionLevel); Assert.Equal(256, audio.BitRateKbps);

                    tabs.SetCurrentValue(TabControl.SelectedIndexProperty, 2); await Flush();
                    output.WriteLine("Audioencoder, benutzerdefinierte Bitrate und Qualität bestanden.");
                    var subtitle = first.Options.Subtitles[0]; subtitle.Action = TrackAction.Encodieren;
                    var subtitleEncoder = Descendants<ComboBox>(window).Single(c => ReferenceEquals(c.DataContext, subtitle) && ReferenceEquals(c.ItemsSource, vm.SubtitleEncoders));
                    foreach (var name in new[] { "mov_text", "ass", "srt" })
                    {
                        subtitleEncoder.SetCurrentValue(ComboBox.SelectedItemProperty, name); await Flush();
                        Assert.Equal(name, subtitle.Encoder);
                    }
                    vm.SelectedMedia = null; await Flush(); vm.SelectedMedia = first; await Flush();
                    await SelectWorkspace(window,"PreviewTab");
                    ((TabControl)window.FindName("PreviewSettingsTabs")).SelectedItem=window.FindName("FiltersSettingsTab");await Flush();
                    first.Options.Filters.ScaleMode=ScaleMode.Prozent; first.Options.Filters.AllowUpscale=true;
                    first.Options.Filters.Percent=double.MaxValue; await Flush();
                    Assert.NotEmpty(vm.ValidationText); Assert.Contains("zu groß",vm.Conversion);
                    first.Options.Filters.Percent=50; first.Options.Filters.AllowUpscale=false; await Flush();
                    App.ApplyTheme(false); App.ApplyTheme(true);
                    output.WriteLine("Untertitel, leere Auswahl und Themewechsel bestanden.");
                    await SelectWorkspace(window,"EncodeSettingsTab");tabs.SetCurrentValue(TabControl.SelectedIndexProperty,0); await Flush();
                    await CheckCommandPreview(vm,window,first);
                    await CheckLanguages(vm,window,services);
                    await CheckThemes(vm,window,services);
                    Assert.Empty(errors);
                    output.WriteLine("Deutsch, Englisch und Polnisch: Dialog, Sofortwechsel, Abbrechen, gespeicherte Sprache und unveränderte Befehle bestanden.");
                    if (Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is { Length: > 0 } integrationRoot)
                    {
                        var toolVersions = new FFmpegVersionService(new AppPaths(integrationRoot), new ProcessRunner());
                        await toolVersions.LoadAsync();
                        var tools = toolVersions.Active ?? throw new InvalidOperationException("Test-FFmpeg fehlt.");
                        var actualCaps = await services.GetRequiredService<FFmpegCapabilityService>().AnalyzeAsync(tools);
                        Assert.DoesNotContain(actualCaps.Encoders, e => e.MediaType == "video" && !VideoEncoderPolicy.IsAllowed(e.Name));
                        foreach (var retained in new[] { "libx264", "libx265", "libvvenc", "libsvtav1", "av1_nvenc", "av1_qsv", "av1_amf" })
                            Assert.Contains(actualCaps.ModernVideo, e => e.Name == retained);
                        vm.ApplyCapabilities(actualCaps); tabs.SetCurrentValue(TabControl.SelectedIndexProperty, 0); await Flush();
                        foreach (var actualEncoder in actualCaps.ModernVideo)
                        {
                            encoder.SetCurrentValue(ComboBox.SelectedValueProperty, actualEncoder.Name); await Flush();
                            Assert.Equal(actualEncoder.Name, first.Options.Video.Encoder);
                            Assert.NotNull(first.Options.Video.PixelFormat);
                            Assert.NotNull(first.Options.Video.Preset);
                            Assert.NotNull(first.Options.Video.Tune);
                            Assert.NotNull(first.Options.Video.Profile);
                            if(actualEncoder.Name=="libsvtav1")
                            {
                                Assert.Equal(Enumerable.Range(0,14).Select(n=>n.ToString()),vm.PresetChoices.Where(p=>p.Length>0));
                                SelectSpeedPreset((PresetSlider)window.FindName("VideoPresetSelector"),"13");await Flush();
                                Assert.Equal("13",first.Options.Video.Preset);Assert.Contains("\"-preset\" \"13\"",vm.CommandText);
                            }
                        }
                        output.WriteLine($"Echte FFmpeg-Encoder im nativen Editor geprüft: {string.Join(", ", actualCaps.ModernVideo.Select(e => e.Name))}");
                        var savedJobs=await new QueueStore(new AppPaths(integrationRoot)).LoadJobsAsync();
                        var completed=Json.Clone(savedJobs.First(j=>j.Status==JobStatus.Abgeschlossen&&j.OutputInfo?.Video is not null&&j.OutputPath.Contains("_mixed")&&File.Exists(j.OutputPath)&&File.Exists(j.Source.Path)));
                        await CheckRecordedCommand(vm,window,services,tools,completed.Source,root);
                        await CheckRecordedCommand(vm,window,services,tools,completed.Source,root,"libsvtav1","13");
                        completed.Id=Guid.NewGuid();
                        vm.Jobs.Add(completed); vm.SelectedJob=completed; await Flush();
                        Assert.True(vm.AnalyzeQualityCommand.CanExecute(null));
                        var cancelled=vm.AnalyzeQualityCommand.ExecuteAsync(null);
                        Assert.True(completed.IsAnalyzing); Assert.True(completed.IsAnalysisIndeterminate);
                        Assert.False(vm.IsBusy); Assert.False(vm.AnalyzeQualityCommand.CanExecute(null));
                        Assert.True(vm.CancelQualityAnalysisCommand.CanExecute(null));
                        vm.CancelQualityAnalysisCommand.Execute(null); await cancelled;
                        Assert.False(completed.IsAnalyzing); Assert.Contains("abgebrochen",completed.AnalysisStatus);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.Equal(Visibility.Visible,((Border)window.FindName("AnalysisActivityPanel")).Visibility);
                        Assert.False(((ProgressBar)window.FindName("AnalysisProgressBar")).IsIndeterminate);
                        Assert.True(vm.AnalyzeQualityCommand.CanExecute(null));
                        await vm.AnalyzeQualityCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(20));
                        Assert.NotNull(completed.Analysis); Assert.Equal(100,completed.AnalysisProgress);
                        Assert.False(completed.IsAnalyzing); Assert.Contains("abgeschlossen",completed.AnalysisStatus);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.Equal(100,((ProgressBar)window.FindName("AnalysisProgressBar")).Value);
                        // LibVLC owns an overlay window too; only close result dialogs created by this check.
                        foreach(var owned in window.OwnedWindows.OfType<AnalysisWindow>().ToArray()) owned.Close();
                        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
                        {
                            Directory.CreateDirectory(artifactRoot); window.UpdateLayout();
                            var panel=(FrameworkElement)window.FindName("AnalysisActivityPanel");
                            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);
                            bitmap.Render(panel);
                            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
                            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var file=File.Create(Path.Combine(artifactRoot,"quality-analysis-panel.png")); png.Save(file);
                        }
                        var missing=Json.Clone(completed); missing.Id=Guid.NewGuid(); missing.OutputPath=Path.Combine(root,"fehlende-ausgabe.mkv");
                        vm.Jobs.Add(missing); vm.SelectedJob=missing; await vm.AnalyzeQualityCommand.ExecuteAsync(null);
                        Assert.False(missing.IsAnalyzing); Assert.Contains("fehlgeschlagen",missing.AnalysisStatus);
                        Assert.NotEmpty(missing.AnalysisDetails); Assert.True(vm.AnalyzeQualityCommand.CanExecute(null));
                        output.WriteLine("Qualitätsanalyse: sichtbare Aktivität, Abbruch, Neustart und gespeicherte Messwerte bestanden.");
                    }
                    Assert.Empty(errors);
                    var playbackFile=Path.Combine(root,"playback-mkv.mkv");
                    if(File.Exists(playbackFile))
                    {
                        var activeTools=services.GetRequiredService<FFmpegVersionService>().Active!;
                        var media=await services.GetRequiredService<MediaProbeService>().ProbeAsync(activeTools,playbackFile);
                        vm.SelectedMedia=new MediaItem{Info=media,Options=EncodingOptions.ForMedia(media)};await vm.WaitForPreviewAsync();
                        await vm.PlaySourceCommand.ExecuteAsync(null);Assert.True(vm.PlaybackActive);
                    }
                    var nativePlayer=vm.PlaybackPlayer;await vm.ShutdownAsync();
                    if(nativePlayer is not null)Assert.Equal(IntPtr.Zero,nativePlayer.NativeReference);
                    Assert.Empty(errors);vm = null;
                    output.WriteLine("Hintergrunddienste beendet.");
                }
                catch (Exception e) { failure = e; output.WriteLine(e.ToString()); }
                finally
                {
                    if (vm is not null) await vm.ShutdownAsync();
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                    app.Shutdown();
                }
            });
            app.Run();
            if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(300));
    }

    private static async Task CheckCodecMemory(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        await SelectWorkspace(window,"EncodeSettingsTab");
        var previous=vm.SelectedMedia;var oldMemory=Json.Clone(vm.Settings.Codecs);
        var item=Item("codec-memory.mkv","libx265","medium","yuv420p");
        var imported=new List<MediaItem>();vm.Media.Add(item);vm.SelectedMedia=item;
        static string Snapshot(VideoOptions video)=>System.Text.Json.JsonSerializer.Serialize(video,Json.Options);
        try
        {
            var video=item.Options.Video;
            video.Encoder="libsvtav1";video.Preset="4";video.Quality=31;video.Gop=180;video.PixelFormat="yuv420p10le";video.SvtAv1Parameters="tune=0:film-grain=8:enable-dlf=2:lp=3";
            await Flush();var svt=Snapshot(video);
            video.Encoder="av1_nvenc";video.Preset="p7";video.RateControl=RateControl.QP;video.Quality=200;video.GpuIndex=1;video.CudaDecode=true;video.CudaScale=true;video.BitRateKbps=8000;video.MaxRateKbps=9000;video.BufferKbps=18000;
            await Flush();var nvenc=Snapshot(video);
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);
                video.Encoder="libsvtav1";await Flush();Assert.Equal(svt,Snapshot(video));Assert.Equal(31,((QualitySlider)window.FindName("VideoQualityInput")).Value);
                video.Encoder="av1_nvenc";await Flush();Assert.Equal(nvenc,Snapshot(video));Assert.Equal(200,((QualitySlider)window.FindName("VideoQualityInput")).Value);
            }
            video.Encoder="libsvtav1";await Flush();
            var audio=item.Options.Audio[0];audio.Title="Original track";audio.Language="pol";audio.Action=TrackAction.Encodieren;
            audio.Encoder="aac";audio.BitRateKbps=256;audio.Channels=2;audio.SampleRate=44100;audio.ChannelLayout="stereo";
            audio.Encoder="flac";audio.CompressionLevel=8;await Flush();
            audio.Encoder="aac";await Flush();Assert.Equal(256,audio.BitRateKbps);Assert.Equal(44100,audio.SampleRate);Assert.Equal("Original track",audio.Title);Assert.Equal("pol",audio.Language);
            audio.Encoder="flac";await Flush();Assert.Equal(8,audio.CompressionLevel);
            audio.Encoder="aac";item.Options.Subtitles[0].Encoder="ass";
            // Check the automatic write, independently reloading settings rather than copying live state.
            await Task.Delay(650);var reloaded=new SettingsService(services.GetRequiredService<AppPaths>(),services.GetRequiredService<LoggingService>());await reloaded.LoadAsync();
            Assert.Equal("libsvtav1",reloaded.Current.Codecs.LastVideoEncoder);Assert.Equal(svt,Snapshot(reloaded.Current.Codecs.ForVideo("libsvtav1")));Assert.Equal("aac",reloaded.Current.Codecs.LastAudioEncoder);Assert.Equal(256,reloaded.Current.Codecs.Audio["aac"].BitRateKbps);Assert.Equal("ass",reloaded.Current.Codecs.LastSubtitleEncoder);
            vm.Settings.Codecs=Json.Clone(reloaded.Current.Codecs);
            var source=Path.Combine(services.GetRequiredService<AppPaths>().Root,"frame-preview.mp4");
            if(File.Exists(source))
            {
                var fresh=Path.Combine(Path.GetDirectoryName(source)!,"codec-import.mp4");File.Copy(source,fresh);
                await vm.ImportPathsAsync([fresh]);var added=vm.SelectedMedia!;imported.Add(added);
                Assert.NotSame(item,added);Assert.Equal(svt,Snapshot(added.Options.Video));Assert.Empty(added.Options.Cuts);Assert.Equal(ScaleMode.Original,added.Options.Filters.ScaleMode);
                var queued=Json.Clone(item.Options);added.Options.Video.Quality=35;Assert.Equal(31,item.Options.Video.Quality);Assert.Equal(31,queued.Video.Quality);
            }
            vm.SelectedMedia=item;await Flush();Assert.Equal(svt,Snapshot(item.Options.Video));
            var before=Snapshot(item.Options.Video);vm.ApplyCapabilities(TestCapabilities());await Flush();Assert.Equal(before,Snapshot(item.Options.Video));
            ((TabControl)window.FindName("SettingsTabs")).SelectedIndex=0;
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(window,Path.Combine(artifacts,"remembered-svt-settings.png"));
        }
        finally
        {
            vm.SelectedMedia=previous;vm.Media.Remove(item);foreach(var added in imported)vm.Media.Remove(added);
            vm.Settings.Codecs=oldMemory;App.ApplyLanguage("de");await Flush();
        }
    }

    private static async Task CheckEncodeComparison(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var root=services.GetRequiredService<AppPaths>().Root;var runner=services.GetRequiredService<ProcessRunner>();
        var versions=services.GetRequiredService<FFmpegVersionService>();
        if(versions.Active is null){var originalVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await originalVersions.LoadAsync();await versions.ActivateAsync(originalVersions.Active!);}
        var tools=versions.Active!;var probe=services.GetRequiredService<MediaProbeService>();var queue=services.GetRequiredService<QueueService>();
        var source=Path.Combine(root,"encode-comparison-original.mkv");
        (await runner.RunAsync(tools.FFmpegPath,["-v","error","-f","lavfi","-i","testsrc2=size=640x360:rate=20:duration=3","-c:v","ffv1",source])).EnsureSuccess("UI comparison source");
        var media=await probe.ProbeAsync(tools,source);var jobs=new List<EncodingJob>();var previous=vm.SelectedMedia;
        var tabs=(TabControl)window.FindName("MainPreviewTabs");var selectedTab=tabs.SelectedItem;
        var button=(Button)window.FindName("CompareEncodesButton");var grid=(DataGrid)window.FindName("QueueGrid");
        EncodeComparisonControl? control=null;
        void Offscreen(EncodeComparisonWindow viewer)
        {viewer.ShowActivated=false;viewer.ShowInTaskbar=false;viewer.WindowState=WindowState.Normal;viewer.Left=-10000;viewer.Top=-10000;viewer.Width=1500;viewer.Height=950;}
        var comparison=vm.CompareEncodes;EncodeComparisonWindow? large=null;
        async Task Loaded(){await comparison.WaitAsync().WaitAsync(TimeSpan.FromSeconds(20));await Flush();Assert.False(comparison.Loading);Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.ImageB);}
        try
        {
            grid.SelectedItems.Clear();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            large=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();
            Assert.True(large.IsVisible);Assert.Equal(WindowState.Maximized,large.WindowState);Assert.Same(selectedTab,tabs.SelectedItem);
            Assert.Null(window.FindName("EncodeComparisonTab"));Assert.Empty(Descendants<EncodeComparisonControl>(window));
            Offscreen(large);await Flush();
            Assert.Contains(Descendants<Button>(large),b=>ButtonHints.GetKey(b)=="ComparisonOpenA");
            Assert.Contains(Descendants<Button>(large),b=>ButtonHints.GetKey(b)=="ComparisonOpenB");
            large.Close();large=null;
            foreach(var qp in new[]{40,50})
            {
                var options=EncodingOptions.ForMedia(media);options.Video.Encoder="libx264";options.Video.Preset="ultrafast";options.Video.RateControl=RateControl.QP;options.Video.Quality=qp;
                options.Video.PixelFormat="yuv420p";options.AutoAnalyze=false;options.OutputDirectory=Path.Combine(root,"comparison-jobs");
                options.FileNamePattern="{name}_{quality}";var job=await queue.AddAsync(media,options);jobs.Add(job);queue.Start(job);
            }
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25)))while(jobs.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(30,timeout.Token);
            Assert.All(jobs,j=>Assert.Equal(JobStatus.Abgeschlossen,j.Status));
            Assert.DoesNotContain(Descendants<Button>(window),b=>ButtonHints.GetKey(b)=="CompareFrames");
            Assert.Equal("Peek",button.Content);
            var startQueue=Descendants<Button>(window).Single(b=>ButtonHints.GetKey(b)=="StartQueue");
            Assert.Same(startQueue.Style,button.Style);Assert.Same(startQueue.Background,button.Background);Assert.Same(startQueue.Foreground,button.Foreground);
            grid.SelectedItems.Clear();grid.SelectedItems.Add(jobs[0]);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            large=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();Assert.Equal(WindowState.Maximized,large.WindowState);
            Offscreen(large);control=(EncodeComparisonControl)large.Content;await comparison.WaitAsync();await Flush();
            Assert.False(comparison.CompareTwoEncodes);Assert.True(comparison.MatchReference);Assert.Equal(2,comparison.PanelCount);
            Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.OriginalImage);Assert.Null(comparison.ImageB);
            Assert.False(((ComboBox)control.FindName("EncodeBSelector")).IsVisible);
            comparison.SideBySide=false;comparison.FocusIndex=0;var heldTask=comparison.WaitAsync();
            var singleSlider=(Slider)control.FindName("ComparisonTimeline");
            var down=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(singleSlider),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent};singleSlider.RaiseEvent(down);
            Assert.True(down.Handled);Assert.Same(comparison.OriginalImage,comparison.FocusImage);Assert.Same(heldTask,comparison.WaitAsync());
            singleSlider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(singleSlider),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyUpEvent});
            Assert.Same(comparison.ImageA,comparison.FocusImage);Assert.Same(heldTask,comparison.WaitAsync());
            comparison.ToggleCommand.Execute(null);Assert.Same(comparison.OriginalImage,comparison.FocusImage);comparison.ToggleCommand.Execute(null);Assert.Same(comparison.ImageA,comparison.FocusImage);
            var edit=(TextBox)control.FindName("ComparisonPosition");edit.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(edit),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Assert.Same(comparison.ImageA,comparison.FocusImage);
            var modeToggle=(ComboBox)control.FindName("ComparisonModeSelector");modeToggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(modeToggle),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Assert.Same(comparison.ImageA,comparison.FocusImage);
            await CheckComparisonViewport(comparison,control,large);
            await comparison.GenerateSamplesCommand.ExecuteAsync(null);await Flush();Assert.Equal(5,comparison.Samples.Count);
            Assert.True(comparison.HasSamples);Assert.All(comparison.Samples,s=>{Assert.True(s.PixelsCached);Assert.NotNull(s.Thumbnail);Assert.True(s.Thumbnail!.IsFrozen);Assert.NotNull(s.ImageA);Assert.NotNull(s.Reference);});
            Assert.InRange(comparison.Samples.Sum(s=>s.PixelBytes),1,EncodeComparisonViewModel.SampleMemoryLimit);
            var cached=comparison.Samples[3];var sampleTask=comparison.WaitAsync();
            ((ListBox)control.FindName("ComparisonSamples")).SelectedItem=cached;await Flush();
            Assert.Same(cached.ImageA,comparison.ImageA);Assert.Same(cached.Reference,comparison.OriginalImage);Assert.Equal(cached.Frames.SourceSeconds,comparison.Position);Assert.Same(sampleTask,comparison.WaitAsync());
            foreach(var language in L.SupportedLanguages)
            {
                App.ApplyLanguage(language);await Flush();Assert.Equal(L.T("Vergleich"),large.Title);Assert.Equal("Peek",button.Content);
                if(language!="de")Assert.DoesNotContain("Referenz mit identischer",comparison.ReferenceDescription);
                Assert.Equal(L.T("Vergleichsmodus"),((TextBlock)control.FindName("ComparisonModeLabel")).Text);
                Assert.Equal(L.T("Im Encode vorhanden"),((TextBlock)control.FindName("CoverageLegend")).Text);
                Assert.Equal(new[]{L.T("Encode A + Referenz"),L.T("Encode A + Encode B"),L.T("Encode A + Encode B + Referenz")},comparison.ModeChoices.Select(c=>c.Label));
                Assert.Equal(comparison.Mode,((ComboBox)control.FindName("ComparisonModeSelector")).SelectedValue);
                Assert.Equal(L.T("Referenz an Encode A anpassen"),((CheckBox)control.FindName("MatchReferenceToggle")).Content);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(large,Path.Combine(artifacts,language+"-single-frame-comparison.png"));
            }
            App.ApplyLanguage("de");comparison.MatchReference=false;await comparison.WaitAsync();await Flush();
            var sampleLoading=comparison.GenerateSamplesCommand.ExecuteAsync(null);Assert.True(comparison.Loading);comparison.Position=.8;
            await comparison.WaitAsync();await sampleLoading;await Flush();Assert.False(comparison.Loading);Assert.Equal(.8,comparison.Position,5);Assert.NotNull(comparison.ImageA);
            var filteredOptions=Json.Clone(jobs[0].Options);filteredOptions.Filters.ScaleMode=ScaleMode.Prozent;filteredOptions.Filters.Percent=50;filteredOptions.FileNamePattern="filtered-reference";
            var filteredJob=await queue.AddAsync(media,filteredOptions);jobs.Add(filteredJob);queue.Start(filteredJob);
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))while(filteredJob.Status is JobStatus.Wartend or JobStatus.Läuft)await Task.Delay(30,timeout.Token);
            Assert.Equal(JobStatus.Abgeschlossen,filteredJob.Status);
            grid.SelectedItems.Clear();grid.SelectedItems.Add(filteredJob);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await comparison.WaitAsync();await Flush();
            Assert.Equal(320,comparison.ImageA!.PixelWidth);Assert.Equal(320,comparison.OriginalImage!.PixelWidth);Assert.True(comparison.MatchReference);
            comparison.MatchReference=false;await comparison.WaitAsync();Assert.Equal(640,comparison.OriginalImage!.PixelWidth);Assert.Equal(320,comparison.ImageA.PixelWidth);
            large.Close();large=null;
            grid.SelectedItems.Clear();foreach(var job in jobs){Assert.Contains(job,grid.Items.Cast<EncodingJob>());grid.SelectedItems.Add(job);}
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            large=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();
            Assert.Equal(WindowState.Maximized,large.WindowState);Assert.Same(comparison,large.DataContext);Assert.Same(selectedTab,tabs.SelectedItem);
            Offscreen(large);control=(EncodeComparisonControl)large.Content;await Loaded();
            Assert.InRange(((ComboBox)control.FindName("EncodeASelector")).ActualHeight,20,55);
            Assert.InRange(((ComboBox)control.FindName("EncodeBSelector")).ActualHeight,20,55);
            Assert.Contains("QP40",comparison.SelectedA!.Label);Assert.Contains("QP50",comparison.SelectedB!.Label);
            Assert.NotNull(comparison.OriginalImage);Assert.Equal(3,comparison.PanelCount);Assert.False(comparison.CanOffsetA);
            await CheckComparisonModes(comparison,control,large);
            await CheckComparisonCoverage(comparison,control,large,services,media,jobs);
            grid.SelectedItems.Clear();grid.SelectedItems.Add(jobs[0]);grid.SelectedItems.Add(jobs[1]);
            comparison.Position=1.2;Assert.True(comparison.Loading);Assert.False(comparison.NextCommand.CanExecute(null));await Loaded();
            Assert.Equal(1.2,comparison.Position,4);Assert.Contains("1,200000",comparison.MessageA);Assert.Contains("1,200000",comparison.MessageB);
            var slider=(Slider)control.FindName("ComparisonTimeline");
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(slider),0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});await Loaded();Assert.Equal(1.25,comparison.Position,4);
            foreach(var time in new[]{.1,1.8,.7,1.2})comparison.Position=time;await Loaded();Assert.Equal(1.2,comparison.Position,4);
            var before=comparison.WaitAsync();var a=comparison.ImageA;var b=comparison.ImageB;
            comparison.FocusIndex=0;comparison.ToggleCommand.Execute(null);Assert.Same(b,comparison.FocusImage);Assert.False(comparison.SideBySide);Assert.Same(before,comparison.WaitAsync());
            comparison.ToggleCommand.Execute(null);Assert.Same(a,comparison.FocusImage);comparison.FocusIndex=2;Assert.Same(comparison.OriginalImage,comparison.FocusImage);
            Assert.True(comparison.BeginReferenceHold());comparison.Mode=EncodeComparisonMode.TwoEncodes;await Loaded();Assert.Null(comparison.OriginalImage);Assert.Equal(2,comparison.PanelCount);Assert.Equal(0,comparison.FocusIndex);
            comparison.Mode=EncodeComparisonMode.TwoEncodesAndReference;comparison.SideBySide=true;await Loaded();
            var preservedImage=comparison.ImageA;var preservedTask=comparison.WaitAsync();var preservedPosition=comparison.Position;
            large.WindowState=WindowState.Minimized;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush();
            Assert.Same(large,window.OwnedWindows.OfType<EncodeComparisonWindow>().Single());Assert.Equal(WindowState.Maximized,large.WindowState);
            Assert.Same(preservedImage,comparison.ImageA);Assert.Same(preservedTask,comparison.WaitAsync());Assert.Equal(preservedPosition,comparison.Position);
            Offscreen(large);await Flush();
            foreach(var language in L.SupportedLanguages)
            {
                App.ApplyLanguage(language);await Flush();Assert.Equal(L.T("Vergleich"),large.Title);
                Assert.All(Descendants<Button>(large),button=>Assert.Equal(ButtonHelpCatalog.Text(ButtonHints.GetKey(button)!),button.ToolTip));
                Assert.DoesNotContain(Descendants<Button>(large),button=>ButtonHints.GetKey(button)=="ComparisonLarge");
                if(language!="de"){Assert.DoesNotContain("Dateizeit",comparison.MessageA);Assert.DoesNotContain("Vergleich geladen",comparison.Status);}
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                {SaveSnapshot(large,Path.Combine(artifacts,language+"-encode-comparison-large.png"));SaveSnapshot(window,Path.Combine(artifacts,language+"-comparison-main-window.png"));}
            }
            comparison.Zoom=2;await Flush();var largeControl=(EncodeComparisonControl)large.Content;
            var paneA=(ComparisonImagePane)largeControl.FindName("PaneA");var paneB=(ComparisonImagePane)largeControl.FindName("PaneB");var originalPane=(ComparisonImagePane)largeControl.FindName("OriginalPane");
            Assert.True(paneA.Scroll.ScrollableWidth>20);paneA.Scroll.ScrollToHorizontalOffset(80);await Flush();
            Assert.InRange(Math.Abs(paneA.Scroll.HorizontalOffset-paneB.Scroll.HorizontalOffset),0,1);Assert.InRange(Math.Abs(paneA.Scroll.HorizontalOffset-originalPane.Scroll.HorizontalOffset),0,1);
            Assert.Equal(1280/VisualTreeHelper.GetDpi(paneA).DpiScaleX,Descendants<Image>(paneA).Single().Width,4);
            var retainedImage=comparison.ImageA;var retainedPosition=comparison.Position;var retainedTask=comparison.WaitAsync();
            large.Close();large=null;button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            large=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();Assert.Equal(WindowState.Maximized,large.WindowState);
            Assert.Same(retainedImage,comparison.ImageA);Assert.Same(retainedTask,comparison.WaitAsync());Assert.Equal(retainedPosition,comparison.Position);Assert.Equal(2,comparison.Zoom);
            Offscreen(large);await Flush();
            var missing=jobs[1].OutputPath;var moved=missing+".temporarily-moved";File.Move(missing,moved);
            try{await comparison.LoadFramesCommand.ExecuteAsync(null);Assert.NotNull(comparison.ImageA);Assert.Null(comparison.ImageB);Assert.NotEmpty(comparison.MessageB);}
            finally{File.Move(moved,missing);}
            var opening=comparison.LoadFileAsync(0,jobs[0].OutputPath);Assert.True(comparison.OpeningFile);Assert.True(comparison.Busy);await opening;
            await comparison.LoadFileAsync(1,jobs[1].OutputPath);await Loaded();
            Assert.True(comparison.CanOffsetA);Assert.True(comparison.CanOffsetB);Assert.Null(comparison.Original);
            Assert.All(comparison.CoverageA,i=>Assert.Equal(ComparisonAvailability.Unknown,i.Availability));
            Assert.Equal(3,comparison.PanelCount);Assert.False(comparison.CanMatchReference);Assert.False(comparison.MatchReference);
            Assert.Equal(L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."),comparison.OriginalMessage);
            comparison.Mode=EncodeComparisonMode.EncodeAndReference;await comparison.WaitAsync();await Flush();
            Assert.False(comparison.LoadFramesCommand.CanExecute(null));Assert.Equal(L.T("Referenzdatei auswählen, um die Referenz anzuzeigen."),comparison.Status);
            comparison.Mode=EncodeComparisonMode.TwoEncodes;await Loaded();Assert.Equal(2,comparison.PanelCount);
            comparison.Mode=EncodeComparisonMode.TwoEncodesAndReference;await Loaded();
            comparison.OffsetA=comparison.OffsetB=.4;comparison.Position=1;await Loaded();Assert.Contains("0,600000",L.Retranslate(comparison.MessageA,L.Language));
            Assert.Equal(ComparisonAvailability.Unavailable,comparison.CoverageA[0].Availability);Assert.Equal(.4,comparison.CoverageA[0].End,5);
            Assert.Equal(ComparisonAvailability.Unavailable,comparison.CoverageB[0].Availability);Assert.Equal(.4,comparison.CoverageB[0].End,5);
            await comparison.LoadFileAsync(2,source);await Loaded();Assert.NotNull(comparison.OriginalImage);Assert.False(comparison.CanMatchReference);
        }
        finally
        {
            large?.Close();comparison.SelectedA=comparison.SelectedB=null;
            foreach(var job in jobs){await queue.RemoveAsync(job);vm.Jobs.Remove(job);}vm.SelectedMedia=previous;tabs.SelectedIndex=0;App.ApplyLanguage("de");await Flush();
        }
    }

    private static async Task CheckPlayback(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var previous=vm.SelectedMedia;var root=services.GetRequiredService<AppPaths>().Root;
        var runner=services.GetRequiredService<ProcessRunner>();var tools=services.GetRequiredService<FFmpegVersionService>().Active!;
        if(tools is null)
        {
            var sourceVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await sourceVersions.LoadAsync();tools=sourceVersions.Active!;
            await services.GetRequiredService<FFmpegVersionService>().ActivateAsync(tools);
        }
        var added=new List<MediaItem>();var slider=(Slider)window.FindName("PreviewTimeline");
        var view=(LibVLCSharp.WPF.VideoView)window.FindName("SourceVideoView");
        async Task Until(Func<bool> predicate){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));while(!predicate())await Task.Delay(30,timeout.Token);}
        async Task Play()
        {
            var task=vm.PlaySourceCommand.ExecuteAsync(null);
            Assert.True(vm.PlaybackActive);Assert.Null(vm.DisplayedPreviewFrame);Assert.False(vm.MarkCutStartCommand.CanExecute(null));
            await task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(vm.PlaybackActive,vm.PreviewStatus);Assert.Same(vm.PlaybackPlayer,view.MediaPlayer);
            Assert.NotEqual(IntPtr.Zero,vm.PlaybackPlayer!.Hwnd);Assert.Equal(Visibility.Visible,view.Visibility);
            await Until(()=>vm.PlaybackPlayer.IsPlaying&&vm.PlaybackPlayer.VoutCount>0&&vm.PlaybackPlayer.Time>100);
            Assert.False(vm.PreviewLoading);Assert.False(vm.IsBusy);
            Assert.Null(vm.DisplayedPreviewFrame);Assert.False(vm.NextFrameCommand.CanExecute(null));
        }
        async Task Exact()
        {
            await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(vm.PlaybackActive);Assert.False(vm.PlaybackTransition);Assert.False(vm.PreviewLoading);
            Assert.NotNull(vm.DisplayedPreviewFrame);Assert.True(vm.MarkCutStartCommand.CanExecute(null));
            Assert.Equal(Visibility.Collapsed,view.Visibility);
        }
        try
        {
            foreach(var kind in new[]{"mkv","hevc","av1","av1-hd","vfr","offset"})
            {
                var path=Path.Combine(root,"playback-"+kind+(kind=="offset"?".ts":kind=="vfr"?".mp4":".mkv"));
                var av1=kind.StartsWith("av1");var width=kind=="av1-hd"?1280:192;var height=kind=="av1-hd"?720:108;
                var args=new List<string>{"-v","error","-f","lavfi","-i",$"testsrc2=size={width}x{height}:rate=10:duration=8","-f","lavfi","-i","sine=frequency=440:sample_rate=48000:duration=8"};
                if(kind=="vfr")args.AddRange(["-vf","setpts=if(lt(N\\,40)\\,N\\,40+(N-40)*2)","-fps_mode","vfr"]);
                args.AddRange(["-c:v",av1?"libsvtav1":kind=="hevc"?"libx265":"libx264","-preset",av1?"13":"ultrafast","-g","10","-c:a","aac"]);
                if(av1)args.AddRange(["-svtav1-params","lp=2"]);
                if(kind=="offset")args.AddRange(["-output_ts_offset","5"]);
                args.Add(path);(await runner.RunAsync(tools.FFmpegPath,args)).EnsureSuccess("Playback fixture "+kind);
                var media=await services.GetRequiredService<MediaProbeService>().ProbeAsync(tools,path);
                var item=new MediaItem{Info=media,Options=EncodingOptions.ForMedia(media)};added.Add(item);vm.Media.Add(item);vm.SelectedMedia=item;
                vm.PreviewSeconds=0;await vm.WaitForPreviewAsync();await Play();
                vm.PlaybackVolume=32;vm.PlaybackMuted=true;await vm.WaitForPlaybackAsync();
                await Until(()=>vm.PlaybackPlayer!.Volume==0);Assert.True(vm.PlaybackPlayer!.AudioTrackCount>1);
                vm.PlaybackMuted=false;await vm.WaitForPlaybackAsync();await Until(()=>vm.PlaybackPlayer!.Volume==32);
                using var descriptor=vm.PlaybackPlayer!.Media;
                var snapshot=Path.Combine(root,kind+"-native.png");Assert.True(vm.PlaybackPlayer.TakeSnapshot(0,snapshot,0,0));
                try{await Until(()=>File.Exists(snapshot));}
                catch(Exception e){var stats=descriptor!.Statistics;throw new InvalidOperationException($"{kind}: snapshot failed; video decoded={stats.DecodedVideo}, shown={stats.DisplayedPictures}, time={vm.PlaybackPlayer.Time}, log={vm.LogText}",e);}
                var pixels=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(snapshot));Assert.Equal(width,pixels.PixelWidth);Assert.Equal(height,pixels.PixelHeight);
                foreach(var time in new[]{1d,5d,2d,4d})slider.SetCurrentValue(RangeBase.ValueProperty,time);
                await vm.WaitForPlaybackAsync();await Until(()=>vm.PlaybackPlayer.Time>=3000);
                Assert.True(vm.PlaybackActive);Assert.Null(vm.DisplayedPreviewFrame);Assert.False(vm.PreviewLoading);
                var actual=vm.PlaybackPlayer.Time/1000d;
                await vm.PauseSourceCommand.ExecuteAsync(null);await Exact();
                Assert.InRange(vm.PreviewSeconds,Math.Max(0,actual-.7),actual+.7);
                var resolved=await services.GetRequiredService<FrameNavigationService>().ResolveAsync(tools,media,vm.PreviewSeconds);
                Assert.Equal(resolved.Timestamp,vm.DisplayedPreviewFrame!.Timestamp);
                vm.MarkCutStartCommand.Execute(null);await vm.NextFrameCommand.ExecuteAsync(null);vm.MarkCutEndCommand.Execute(null);Assert.Single(item.Options.Cuts);
                var stepped=vm.DisplayedPreviewFrame;await vm.StopSourceCommand.ExecuteAsync(null);await Exact();Assert.Equal(stepped,vm.DisplayedPreviewFrame);
                if(kind=="mkv")
                {
                    item.Options.Filters.ScaleMode=ScaleMode.Prozent;item.Options.Filters.Percent=50;
                    await Play();await vm.ShowFilteredPreviewCommand.ExecuteAsync(null);await Exact();
                    Assert.Equal(width/2,((System.Windows.Media.Imaging.BitmapSource)vm.PreviewImage!).PixelWidth);
                    Assert.Contains(L.T("Filtervorschau"),vm.PreviewStatus);
                    await Play();await SelectWorkspace(window,"EncodeSettingsTab");await vm.WaitForPlaybackAsync();await Exact();
                    Assert.False(vm.PlaybackPlayer!.IsPlaying);
                    Assert.Same(window.FindName("EncodeSettingsTab"),((TabControl)window.FindName("MainPreviewTabs")).SelectedItem);
                    await Play();((Button)window.FindName("CompareEncodesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var viewer=window.OwnedWindows.OfType<EncodeComparisonWindow>().Single();Assert.Equal(WindowState.Maximized,viewer.WindowState);
                    await vm.WaitForPlaybackAsync();await Exact();Assert.False(vm.PlaybackPlayer!.IsPlaying);viewer.Close();
                    await Play();Assert.Same(window.FindName("PreviewTab"),((TabControl)window.FindName("MainPreviewTabs")).SelectedItem);
                    vm.SelectedCut=item.Options.Cuts[0];await vm.JumpCutStartCommand.ExecuteAsync(null);await Exact();
                    Assert.Equal(vm.SelectedCut.Start.Timestamp,vm.DisplayedPreviewFrame!.Timestamp);
                    foreach(var language in new[]{"de","en","pl"})
                    {
                        App.ApplyLanguage(language);await Flush();
                        Assert.Equal(L.T("Abspielen"),((Button)window.FindName("PlaySourceButton")).Content);
                    }
                    App.ApplyLanguage("de");
                }
                await Play();
                slider.SetCurrentValue(RangeBase.ValueProperty,2.12);await vm.StopSourceCommand.ExecuteAsync(null);await Exact();
                Assert.InRange(vm.PreviewSeconds,2.0,2.2);Assert.True(vm.PlaySourceCommand.CanExecute(null));
                Assert.False(vm.StopSourceCommand.CanExecute(null));
                // Repeated play, immediate pause and a file switch must not let stale callbacks replace the exact frame.
                var starting=vm.PlaySourceCommand.ExecuteAsync(null);await Task.Delay(20);var holding=vm.PauseSourceCommand.ExecuteAsync(null);
                await Task.WhenAll(starting,holding);await Exact();await Task.Delay(150);Assert.False(vm.PlaybackPlayer!.IsPlaying);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                {Directory.CreateDirectory(artifacts);File.Copy(snapshot,Path.Combine(artifacts,kind+"-playing.png"),true);SaveSnapshot(window,Path.Combine(artifacts,kind+"-paused.png"));}
                await Play();vm.SelectedMedia=previous;await vm.WaitForPlaybackAsync();await Task.Delay(150);
                Assert.False(vm.PlaybackActive);Assert.Null(vm.DisplayedPreviewFrame);Assert.Same(previous?.Thumbnail,vm.PreviewImage);
            }
            // EOF also returns to the last real frame, including the VFR frame beyond the format duration.
            vm.SelectedMedia=added.Single(i=>i.Info.Path.Contains("-vfr."));vm.PreviewSeconds=vm.PreviewMaximum-1;await vm.WaitForPreviewAsync();
            await Play();await Until(()=>!vm.PlaybackActive);await Exact();Assert.Equal(11.8,vm.PreviewSeconds,4);
        }
        finally{vm.SelectedMedia=previous;await vm.WaitForPlaybackAsync();foreach(var item in added)vm.Media.Remove(item);vm.PlaybackVolume=70;App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckFramePreview(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        await SelectWorkspace(window,"PreviewTab");
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var previous=vm.SelectedMedia;var runner=services.GetRequiredService<ProcessRunner>();
        var sourceVersions=new FFmpegVersionService(new AppPaths(dataRoot),runner);await sourceVersions.LoadAsync();
        var tools=sourceVersions.Active!;await services.GetRequiredService<FFmpegVersionService>().ActivateAsync(tools);
        var path=Path.Combine(services.GetRequiredService<AppPaths>().Root,"frame-preview.mp4");
        (await runner.RunAsync(tools.FFmpegPath,["-v","error","-f","lavfi","-i","testsrc2=size=96x64:rate=10:duration=2","-vf","setpts=if(lt(N\\,10)\\,N\\,10+(N-10)*2)","-fps_mode","vfr","-c:v","libx264","-g","100","-bf","3",path])).EnsureSuccess("Frame preview fixture");
        var media=await services.GetRequiredService<MediaProbeService>().ProbeAsync(tools,path);
        var item=new MediaItem{Info=media,Options=EncodingOptions.ForMedia(media)};vm.Media.Add(item);
        var slider=(Slider)window.FindName("PreviewTimeline");
        async Task Loaded(){await vm.WaitForPreviewAsync().WaitAsync(TimeSpan.FromSeconds(15));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert.False(vm.PreviewLoading);Assert.NotNull(vm.DisplayedPreviewFrame);Assert.DoesNotContain("fehlgeschlagen",vm.PreviewStatus);}
        try
        {
            vm.SelectedMedia=item;await Loaded();Assert.NotNull(vm.PreviewImage);
            slider.SetCurrentValue(RangeBase.ValueProperty,.9);Assert.True(vm.PreviewLoading);Assert.False(vm.IsBusy);
            Assert.False(vm.NextFrameCommand.CanExecute(null));
            var activity=(FrameworkElement)window.FindName("PreviewActivity");Assert.Equal(Visibility.Visible,activity.Visibility);
            Assert.True(activity.IsDescendantOf((FrameworkElement)window.FindName("PreviewImageArea")));
            Assert.Single(Descendants<LoadingSpinner>(activity));
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} loadingArtifacts)SaveSnapshot(window,Path.Combine(loadingArtifacts,"preview-loading-overlay.png"));
            await Loaded();Assert.Equal(.9,vm.DisplayedPreviewFrame!.Seconds,5);
            var buffered=services.GetRequiredService<AccuratePreviewService>();var decoderStarts=buffered.Statistics.DecoderStarts;
            var pngCount=Directory.GetFiles(services.GetRequiredService<AppPaths>().Cache,"*.png").Length;
            await vm.NextFrameCommand.ExecuteAsync(null);await Loaded();Assert.Equal(1,vm.PreviewSeconds,5);
            await vm.NextFrameCommand.ExecuteAsync(null);await Loaded();Assert.Equal(1.2,vm.PreviewSeconds,5);
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(slider),0,Key.Left){RoutedEvent=Keyboard.PreviewKeyDownEvent});await Loaded();Assert.Equal(1,vm.PreviewSeconds,5);
            Assert.Equal(decoderStarts,buffered.Statistics.DecoderStarts);
            Assert.Equal(pngCount,Directory.GetFiles(services.GetRequiredService<AppPaths>().Cache,"*.png").Length);
            Assert.True(buffered.Statistics.CacheHits>0);
            Assert.InRange(buffered.Statistics.BufferedBytes,1,AccuratePreviewService.MemoryLimit);
            foreach(var time in new[]{.1,1.6,.7,1.2})slider.SetCurrentValue(RangeBase.ValueProperty,time);
            await Loaded();Assert.Equal(1.2,vm.PreviewSeconds,5);
            item.Options.Filters.ScaleMode=ScaleMode.Prozent;item.Options.Filters.Percent=50;
            await vm.ShowFilteredPreviewCommand.ExecuteAsync(null);await Loaded();
            Assert.Equal(48,((System.Windows.Media.Imaging.BitmapSource)vm.PreviewImage!).PixelWidth);
            slider.SetCurrentValue(RangeBase.ValueProperty,1.6);await Loaded();Assert.Equal(1.6,vm.PreviewSeconds,5);
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);await Flush();Assert.Contains(L.T("Filtervorschau"),vm.PreviewStatus);
                Assert.Equal(vm.PreviewStatus,((FrameworkElement)window.FindName("PreviewImageArea")).ToolTip);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(window,Path.Combine(artifacts,language+"-frame-preview.png"));
            }
            await vm.ShowPreviewCommand.ExecuteAsync(null);await Loaded();Assert.Equal(96,((System.Windows.Media.Imaging.BitmapSource)vm.PreviewImage!).PixelWidth);
            item.Options.Filters.CustomChain="hflip";
            await vm.ShowFilteredPreviewCommand.ExecuteAsync(null);await Loaded();Assert.Equal(48,((System.Windows.Media.Imaging.BitmapSource)vm.PreviewImage!).PixelWidth);
            item.Options.Filters.CustomChain="";await vm.ShowPreviewCommand.ExecuteAsync(null);await Loaded();
            slider.SetCurrentValue(RangeBase.ValueProperty,2.8);await Loaded();Assert.Equal(2.8,vm.PreviewSeconds,5);Assert.True(vm.PreviewMaximum>=2.8);
            await vm.NextFrameCommand.ExecuteAsync(null);await Loaded();Assert.Equal(2.8,vm.PreviewSeconds,5);
            slider.SetCurrentValue(RangeBase.ValueProperty,0d);await Loaded();await vm.PreviousFrameCommand.ExecuteAsync(null);await Loaded();Assert.Equal(0,vm.PreviewSeconds,5);
            var settingsTabs=(TabControl)window.FindName("PreviewSettingsTabs");settingsTabs.SelectedItem=window.FindName("CutSettingsTab");
            vm.MarkCutStartCommand.Execute(null);Assert.NotNull(vm.PendingCutStart);
            slider.SetCurrentValue(RangeBase.ValueProperty,.9);Assert.False(vm.MarkCutEndCommand.CanExecute(null));await Loaded();
            vm.MarkCutEndCommand.Execute(null);await Flush();Assert.Single(item.Options.Cuts);Assert.Equal(.9,item.Options.Cuts[0].End!.Seconds,5);
            Assert.Null(vm.PendingCutStart);Assert.True(vm.MarkCutStartCommand.CanExecute(null));
            slider.SetCurrentValue(RangeBase.ValueProperty,1.2);await Loaded();vm.MarkCutStartCommand.Execute(null);
            slider.SetCurrentValue(RangeBase.ValueProperty,1.6);await Loaded();vm.MarkCutEndCommand.Execute(null);await Flush();
            Assert.Equal(2,item.Options.Cuts.Count);Assert.Equal(2,Descendants<ListBox>(window).Single(b=>b.Name=="CutList").Items.Count);
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);await Flush();
                Assert.Equal(L.T("Schnitt"),((TabItem)window.FindName("CutSettingsTab")).Header);
                Assert.Contains("2",vm.CutSummary);
                if(language!="de")Assert.DoesNotContain("Schnittbereiche",vm.CutSummary);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(window,Path.Combine(artifacts,language+"-cut-editor.png"));
            }
            vm.SelectedCut=item.Options.Cuts[1];await vm.JumpCutEndCommand.ExecuteAsync(null);await Loaded();Assert.Equal(1.6,vm.PreviewSeconds,5);
            vm.RemoveCutCommand.Execute(null);Assert.Single(item.Options.Cuts);
            vm.ClearCutsCommand.Execute(null);Assert.Empty(item.Options.Cuts);
            slider.SetCurrentValue(RangeBase.ValueProperty,1.2);await Loaded();vm.MarkCutStartCommand.Execute(null);vm.CutToEndCommand.Execute(null);Assert.Null(item.Options.Cuts[0].End);
            var snapshot=Json.Clone(item.Options);vm.ClearCutsCommand.Execute(null);Assert.Single(snapshot.Cuts);
            settingsTabs.SelectedIndex=0;
            slider.SetCurrentValue(RangeBase.ValueProperty,1.2);vm.SelectedMedia=previous;await Task.Delay(300);
            Assert.Null(vm.DisplayedPreviewFrame);Assert.Same(previous?.Thumbnail,vm.PreviewImage);Assert.False(vm.PreviewLoading);
            vm.SelectedMedia=null;Assert.Null(vm.PreviewImage);Assert.False(vm.NextFrameCommand.CanExecute(null));
            Assert.Equal(0,buffered.Statistics.BufferedBytes);
        }
        finally{vm.SelectedMedia=previous;vm.Media.Remove(item);App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckTooltips(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        static void Check(DependencyObject root,bool enabled=true,int delay=2000)
        {
            // The system ScrollViewer template contains an empty infrastructure button.
            var buttons=Descendants<Button>(root).Where(b=>b.TemplatedParent is not ScrollViewer).ToArray();Assert.NotEmpty(buttons);
            foreach(var button in buttons)
            {
                var key=ButtonHints.GetKey(button);
                Assert.True(key is not null&&ButtonHelpCatalog.All.ContainsKey(key),$"Missing button help: '{button.Content}' ({button.GetType().Name}/{button.Name}, template: {button.TemplatedParent?.GetType().Name})");
                Assert.Equal(ButtonHelpCatalog.Text(key!),button.ToolTip);
                Assert.Equal(enabled,ToolTipService.GetIsEnabled(button));
                Assert.Equal(delay,ToolTipService.GetInitialShowDelay(button));
                Assert.Equal(0,ToolTipService.GetBetweenShowDelay(button));
                Assert.True(ToolTipService.GetShowOnDisabled(button));
            }
        }
        void Hide(Window dialog)
        {dialog.Owner=window;dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-10000;dialog.Top=-10000;}
        var tabs=(TabControl)window.FindName("SettingsTabs");var center=(TabControl)window.FindName("MainPreviewTabs");
        var analyze=Descendants<Button>(window).Single(b=>ButtonHints.GetKey(b)=="AnalyzeQuality");
        Assert.False(analyze.IsEnabled);Assert.True(ToolTipService.GetIsEnabled(analyze));
        foreach(var language in new[]{"de","en","pl"})
        {
            App.ApplyLanguage(language);await Flush();
            await SelectWorkspace(window,"EncodeSettingsTab");
            for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Check(window);}
            await SelectWorkspace(window,"PreviewTab");
            var previewTabs=(TabControl)window.FindName("PreviewSettingsTabs");
            for(int i=0;i<previewTabs.Items.Count;i++){previewTabs.SelectedIndex=i;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Check(window);}
            for(int i=0;i<center.Items.Count;i++){center.SelectedIndex=i;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Check(window);}
            var dialog=new SettingsWindow(vm.Settings);Hide(dialog);dialog.Show();await Flush();Check(dialog);
            var options=Descendants<TabControl>(dialog).Single();options.SelectedIndex=2;await Flush();Check(dialog);
            dialog.Close();
        }
        App.ApplyLanguage("de");tabs.SelectedIndex=0;await SelectWorkspace(window,"EncodeSettingsTab");
        foreach(var dialog in new Window[]{new ManagementWindow(vm),new EncoderOptionsWindow(TestCapabilities().Find("libx265")!,[]),new AnalysisWindow(new QualityResult(null,null,null,"",[],""))})
        {Hide(dialog);dialog.Show();await Flush();Check(dialog);dialog.Close();}
        var host=(FrameworkElement)window.FindName("VideoQualityInput");
        Assert.Equal(2000,ToolTipService.GetInitialShowDelay(host));
        var temporary=Json.Clone(vm.Settings);temporary.TooltipDelaySeconds=.5;ButtonHints.Apply(temporary);await Flush();Check(window,true,500);
        Assert.Equal(500,ToolTipService.GetInitialShowDelay(host));
        var popup=new ToolTip{Content=analyze.ToolTip,PlacementTarget=analyze,IsOpen=true};await Flush();
        Assert.True(popup.IsOpen);
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
            SaveSnapshot(popup,Path.Combine(artifacts,"quality-analysis-tooltip.png"));
        temporary.TooltipsEnabled=false;ButtonHints.Apply(temporary);await Flush();
        Assert.False(popup.IsOpen);Check(window,false,500);Assert.False(ToolTipService.GetIsEnabled(host));
        var disabledDialog=new SettingsWindow(vm.Settings);Hide(disabledDialog);disabledDialog.Show();await Flush();Check(disabledDialog,false,500);disabledDialog.Close();
        ButtonHints.Apply(vm.Settings);await Flush();Check(window);
        async Task Change(bool enabled,double seconds,bool save)
        {
            Exception? failure=null;
            _=Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
            {
                var dialog=Application.Current.Windows.OfType<SettingsWindow>().Single();
                try
                {
                    dialog.ShowInTaskbar=false;dialog.ShowActivated=false;dialog.Left=-10000;dialog.Top=-10000;
                    var toggle=Descendants<CheckBox>(dialog).Single(c=>c.Name=="TooltipsEnabledInput");
                    var input=Descendants<TextBox>(dialog).Single(c=>c.Name=="TooltipDelayInput");
                    var previous=vm.Settings.TooltipsEnabled;var previousDelay=vm.Settings.TooltipDelaySeconds;
                    toggle.SetCurrentValue(ToggleButton.IsCheckedProperty,true);
                    input.SetCurrentValue(TextBox.TextProperty,seconds.ToString(input.Language.GetSpecificCulture()));
                    toggle.SetCurrentValue(ToggleButton.IsCheckedProperty,enabled);await Flush();
                    Assert.Equal(enabled,input.IsEnabled);Assert.Equal(seconds,dialog.Result.TooltipDelaySeconds);
                    Assert.Equal(previous,vm.Settings.TooltipsEnabled);Assert.Equal(previousDelay,vm.Settings.TooltipDelaySeconds);
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts&&save&&enabled)
                        SaveSnapshot(dialog,Path.Combine(artifacts,"tooltip-settings.png"));
                    Descendants<Button>(dialog).Single(b=>ButtonHints.GetKey(b)==(save?"DialogSave":"DialogCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception e){failure=e;dialog.Close();}
            });
            await vm.EditSettingsCommand.ExecuteAsync(null);if(failure is not null)throw failure;await Flush();
        }
        await Change(false,.5,false);Check(window);Assert.True(vm.Settings.TooltipsEnabled);Assert.Equal(2,vm.Settings.TooltipDelaySeconds);
        await Change(false,.5,true);Check(window,false,500);
        var restored=new SettingsService(services.GetRequiredService<AppPaths>(),services.GetRequiredService<LoggingService>());
        await restored.LoadAsync();Assert.False(restored.Current.TooltipsEnabled);Assert.Equal(.5,restored.Current.TooltipDelaySeconds);
        await Change(true,2,true);Check(window);
    }

    private static async Task CheckFileNaming(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        await SelectWorkspace(window,"EncodeSettingsTab");
        var original=Json.Clone(vm.Settings.FileNaming);var item=vm.SelectedMedia!;
        var originalPattern=item.Options.FileNamePattern;var quality=item.Options.Video.Quality;
        var bitrate=item.Options.Video.BitRateKbps;var mode=item.Options.Video.RateControl;
        var frozen=new EncodingJob{Source=item.Info,Options=Json.Clone(item.Options),OutputPath=Path.GetFullPath("already-queued.mkv")};vm.Jobs.Add(frozen);
        async Task Edit(string language,bool save,bool reset=false)
        {
            App.ApplyLanguage(language);await Flush();Exception? failure=null;
            var previous=Json.Clone(vm.Settings.FileNaming);
            _=Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
            {
                var dialog=Application.Current.Windows.OfType<SettingsWindow>().Single();
                try
                {
                    dialog.ShowInTaskbar=false;dialog.ShowActivated=false;dialog.Left=-10000;dialog.Top=-10000;
                    var tabs=Descendants<TabControl>(dialog).Single();
                    var tab=tabs.Items.OfType<TabItem>().Single(t=>t.Name=="FileNamingSettingsTab");
                    Assert.Equal(L.T("Dateinamen"),tab.Header);tabs.SelectedItem=tab;await Flush();
                    var view=Descendants<FileNamingControl>(dialog).Single();
                    var master=Descendants<CheckBox>(view).Single(c=>c.Name=="UseGlobalFileNames");
                    master.SetCurrentValue(ToggleButton.IsCheckedProperty,true);await Flush();
                    var choices=Descendants<CheckBox>(view).Where(c=>c.Name.StartsWith("Naming")).ToArray();Assert.Equal(10,choices.Length);
                    foreach(var choice in choices)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(choice.ToolTip as string));Assert.True(ToolTipService.GetIsEnabled(choice));Assert.Equal(2000,ToolTipService.GetInitialShowDelay(choice));
                        bool selected=reset?(bool)typeof(FileNamingSettings).GetProperty("Include"+choice.Name[6..])!.GetValue(original)!:choice.Name!="NamingTime";
                        choice.SetCurrentValue(ToggleButton.IsCheckedProperty,selected);
                    }
                    if(reset)master.SetCurrentValue(ToggleButton.IsCheckedProperty,original.Enabled);
                    await Flush();
                    var pattern=Descendants<TextBox>(view).Single(t=>t.Name=="NamingPatternPreview");
                    var example=Descendants<TextBox>(view).Single(t=>t.Name=="NamingExamplePreview");
                    Assert.True(example.IsReadOnly);Assert.True(pattern.IsReadOnly);
                    Assert.Equal(dialog.Result.FileNaming.Enabled?FileNaming.Pattern(dialog.Result.FileNaming):originalPattern,pattern.Text);
                    Assert.Equal(Path.GetFileName(QueueService.SuggestedOutputPath(item.Info,item.Options,dialog.Result.FileNaming)),example.Text);
                    Assert.Contains(Descendants<TextBlock>(dialog),t=>t.Text==L.T("Vorschau mit der aktuell ausgewählten Datei"));
                    Assert.Equal(System.Text.Json.JsonSerializer.Serialize(previous),System.Text.Json.JsonSerializer.Serialize(vm.Settings.FileNaming));
                    if(!reset&&Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                        SaveSnapshot(dialog,Path.Combine(artifacts,language+"-file-naming.png"));
                    Descendants<Button>(dialog).Single(b=>ButtonHints.GetKey(b)==(save?"DialogSave":"DialogCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception e){failure=e;dialog.Close();}
            });
            await vm.EditSettingsCommand.ExecuteAsync(null);if(failure is not null)throw failure;await Flush();
        }
        try
        {
            ((TabControl)window.FindName("SettingsTabs")).SelectedItem=window.FindName("OutputSettingsTab");await Flush();
            await Edit("de",false);Assert.False(vm.Settings.FileNaming.Enabled);Assert.True(vm.CanEditFileNamePattern);
            foreach(var language in new[]{"de","en","pl"})
            {
                await Edit(language,true);Assert.True(vm.Settings.FileNaming.Enabled);Assert.False(vm.CanEditFileNamePattern);
                Assert.False(((TextBox)window.FindName("IndividualFileNamePatternInput")).IsEnabled);
                Assert.Equal(Path.GetFileName(QueueService.SuggestedOutputPath(item.Info,item.Options,vm.Settings.FileNaming)),vm.OutputFileNamePreview);
                Assert.Contains(Path.GetFileNameWithoutExtension(vm.OutputFileNamePreview)+".unvollstaendig-VORSCHAU"+Path.GetExtension(vm.OutputFileNamePreview),vm.CommandText);Assert.Equal(originalPattern,item.Options.FileNamePattern);
                Assert.Equal(Path.GetFullPath("already-queued.mkv"),frozen.OutputPath);Assert.Equal(originalPattern,frozen.Options.FileNamePattern);
                var restored=new SettingsService(services.GetRequiredService<AppPaths>(),services.GetRequiredService<LoggingService>());await restored.LoadAsync();
                Assert.True(restored.Current.FileNaming.IncludeQuality);Assert.True(restored.Current.FileNaming.IncludeBitrate);Assert.True(restored.Current.FileNaming.IncludeDate);Assert.True(restored.Current.FileNaming.Enabled);
            }
            item.Options.Video.Quality=25.5;await Flush();Assert.Contains("CRF25.5",vm.OutputFileNamePreview);Assert.DoesNotContain("kbps",vm.OutputFileNamePreview);
            item.Options.Video.BitRateKbps=4500;item.Options.Video.RateControl=RateControl.Bitrate;await Flush();
            Assert.Contains("4500kbps",vm.OutputFileNamePreview);Assert.DoesNotContain("CRF",vm.OutputFileNamePreview);
        }
        finally
        {
            item.Options.Video.Quality=quality;item.Options.Video.BitRateKbps=bitrate;item.Options.Video.RateControl=mode;
            await Edit("de",true,true);vm.Jobs.Remove(frozen);App.ApplyLanguage("de");await Flush();
        }
        Assert.True(vm.CanEditFileNamePattern);Assert.Equal(originalPattern,item.Options.FileNamePattern);
    }

    private static async Task CheckCommandPreview(MainViewModel vm, MainWindow window, MediaItem item)
    {
        var original=Json.Clone(item.Options); var oldJob=vm.SelectedJob;
        var tabs=(TabControl)window.FindName("SettingsTabs");
        var center=(TabControl)window.FindName("MainPreviewTabs");
        var view=(CommandPreviewControl)window.FindName("LargeCommandPreview");
        var source=(ComboBox)view.FindName("CommandSourceSelector");
        var text=(TextBox)view.FindName("CommandPreviewText");
        var job=new EncodingJob { Source=item.Info,Options=Json.Clone(item.Options),Tools=new("test","ffmpeg.exe","ffprobe.exe",false),
            OutputPath=Path.GetFullPath("job-output.mkv"),PartialPath=Path.GetFullPath("job.unvollstaendig-test.mkv") };
        try
        {
            item.Options.Video.Encoder="libx265"; item.Options.Video.Quality=23; item.Options.Video.RateControl=RateControl.Qualität;
            await Flush();
            await SelectWorkspace(window,"LargeCommandTab");
            Assert.Equal(4,tabs.Items.Count); Assert.True(text.IsReadOnly);
            Assert.Contains("\"-crf\" \"23\"",text.Text);Assert.Contains(item.Info.Path,text.Text);
            Assert.True(vm.CopyCommandCommand.CanExecute(null));
            item.Options.Video.Quality=24; await Flush();
            Assert.Contains("\"-crf\" \"24\"",text.Text);
            item.Options.Audio[0].Action=TrackAction.Encodieren; item.Options.Audio[0].Encoder="libopus";
            item.Options.Subtitles[0].Action=TrackAction.Entfernen;
            item.Options.Filters.ScaleMode=ScaleMode.Prozent;item.Options.Filters.Percent=50;await Flush();
            Assert.Contains("\"-c:a:0\" \"libopus\"",text.Text);
            Assert.DoesNotContain("\"-c:s:0\"",text.Text);Assert.Contains("scale=960:540",text.Text);
            item.Options.Filters.ScaleMode=ScaleMode.Original;await Flush();
            Assert.DoesNotContain("\"-vf\"",text.Text);
            Assert.DoesNotContain("scale=",text.Text);Assert.DoesNotContain("setsar=",text.Text);
            item.Options.Filters.ScaleMode=ScaleMode.Prozent;await Flush();
            Assert.Contains("scale=960:540",text.Text);
            item.Options.Video.Quality=1000;await Flush();
            Assert.False(vm.CopyCommandCommand.CanExecute(null)); Assert.DoesNotContain("\"-i\"",text.Text);
            item.Options.Video.Quality=24;await Flush();
            vm.SelectedJob=job;
            source.SetCurrentValue(ComboBox.SelectedIndexProperty,1);await Flush();
            Assert.Contains(job.PartialPath,text.Text);Assert.Contains("Vorschau",vm.CommandPreviewDescription);
            job.ExecutedCommand=FFmpegCommandBuilder.Display(job.Tools.FFmpegPath,new FFmpegCommandBuilder().Build(job.Source,job.Options,job.PartialPath));
            job.Status=JobStatus.Abgeschlossen; vm.SelectedJob=null;vm.SelectedJob=job;await Flush();
            var recorded=job.ExecutedCommand;
            Assert.Equal(recorded,text.Text);Assert.Contains("Tatsächlich",vm.CommandPreviewDescription);
            text.Select(5,10);var selectedText=text.SelectedText;
            item.Options.Video.Quality=25;await Flush(); Assert.Equal(recorded,text.Text);Assert.Equal(selectedText,text.SelectedText);
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);await Flush();
                Assert.Equal(1,vm.CommandViewIndex);Assert.Equal(recorded,text.Text);
                Assert.Equal(L.T("Befehl"),((TabItem)window.FindName("LargeCommandTab")).Header);
                Assert.Equal(L.T("Ausgewählter Queue-Job"),((ComboBoxItem)source.Items[1]).Content);
                source.SetCurrentValue(ComboBox.SelectedIndexProperty,0);await Flush();
                Assert.Same(window.FindName("LargeCommandTab"),center.SelectedItem);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} largeRoot)
                    SaveSnapshot(window,Path.Combine(largeRoot,language+"-command-large.png"));
                source.SetCurrentValue(ComboBox.SelectedIndexProperty,1);await Flush();
            }
            vm.SelectedJob=null;await Flush(); Assert.False(vm.CopyCommandCommand.CanExecute(null));
            source.SetCurrentValue(ComboBox.SelectedIndexProperty,0);vm.SelectedMedia=null;await Flush();
            Assert.Empty(text.Text);Assert.False(vm.CopyCommandCommand.CanExecute(null));
        }
        finally
        {
            vm.SelectedMedia=null;item.Options=original;vm.SelectedMedia=item;vm.SelectedJob=oldJob;vm.CommandViewIndex=0;
            App.ApplyLanguage("de");tabs.SetCurrentValue(TabControl.SelectedIndexProperty,0);center.SetCurrentValue(TabControl.SelectedIndexProperty,0);await Flush();
        }
    }

    private static async Task CheckRecordedCommand(MainViewModel vm, MainWindow window, ServiceProvider services, Toolchain tools, MediaInfo source, string root, string encoder="libx264",string preset="ultrafast")
    {
        await services.GetRequiredService<FFmpegVersionService>().ActivateAsync(tools);
        var options=EncodingOptions.ForMedia(source);options.Video.Encoder=encoder;options.Video.Preset=preset;
        if(encoder=="libsvtav1")
        {options.Video.Gop=48;options.Video.SvtTune="0";options.Video.SvtFilmGrain="0";options.Video.SvtDeblocking="2";options.Video.SvtParallelism="1";}
        else { options.Video.ThreadMode=ThreadControlMode.FesteAnzahl;options.Video.ThreadCount=1; }
        options.ClipDuration=.2;options.Audio.Clear();options.Subtitles.Clear();
        options.OutputDirectory=Path.Combine(root,"command-encode");options.FileNamePattern="Aufruf 'Ä' & prüfen";
        var queue=services.GetRequiredService<QueueService>();
        var job=await queue.AddAsync(source,options);
        EncodingJob? parallel=null;
        if(encoder=="libx264")parallel=await queue.AddAsync(source,options);
        vm.SelectedJob=job;vm.CommandViewIndex=1;await Flush();
        Assert.Contains("Vorschau",vm.CommandPreviewDescription);
        var preview=vm.DisplayedCommandText;
        if(parallel is not null)
        {
            var grid=(DataGrid)window.FindName("QueueGrid");Assert.Equal(DataGridSelectionMode.Extended,grid.SelectionMode);
            vm.ParallelJobLimit=2;grid.SelectedItems.Clear();grid.SelectedItems.Add(job);grid.SelectedItems.Add(parallel);
            ((Button)window.FindName("StartSelectedJobsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            vm.SelectedJob=job;
        }
        else queue.Start(job);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(job.Status is JobStatus.Wartend or JobStatus.Läuft||parallel?.Status is JobStatus.Wartend or JobStatus.Läuft) await Task.Delay(100,timeout.Token);
        await Flush();Assert.Equal(JobStatus.Abgeschlossen,job.Status);
        if(parallel is not null)Assert.Equal(JobStatus.Abgeschlossen,parallel.Status);
        Assert.NotNull(job.Scheduling);Assert.True(job.Scheduling.Requested);Assert.True(job.Scheduling.IdlePriority);
        var expected=FFmpegCommandBuilder.Display(tools.FFmpegPath,new FFmpegCommandBuilder().Build(source,job.Options,job.PartialPath));
        Assert.Equal(expected,job.ExecutedCommand);Assert.Equal(expected,vm.DisplayedCommandText);Assert.NotEqual(preview,expected);
        Assert.Contains(expected,await File.ReadAllTextAsync(job.LogPath));
        var restored=(await services.GetRequiredService<QueueStore>().LoadJobsAsync()).Single(j=>j.Id==job.Id);
        Assert.Equal(expected,restored.ExecutedCommand);
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
        {
            await SelectWorkspace(window,"LargeCommandTab");
            SaveSnapshot(window,Path.Combine(artifactRoot,"recorded-queue-command.png"));
            await File.WriteAllTextAsync(Path.Combine(artifactRoot,"recorded-command.txt"),expected);
        }
        vm.CommandViewIndex=0;((TabControl)window.FindName("SettingsTabs")).SetCurrentValue(TabControl.SelectedIndexProperty,0);await Flush();
    }

    private static void SelectSpeedPreset(PresetSlider slider,string raw)
    {
        var index=slider.Choices.ToList().IndexOf(raw);
        Assert.True(index>=0,"Missing speed preset: "+raw);
        slider.SetCurrentValue(RangeBase.ValueProperty,(double)index);
    }

    private static async Task CheckPresetEncoders(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA") is not {Length:>0} dataRoot)return;
        var versions=new FFmpegVersionService(new AppPaths(dataRoot),services.GetRequiredService<ProcessRunner>());await versions.LoadAsync();
        var caps=await services.GetRequiredService<FFmpegCapabilityService>().AnalyzeAsync(versions.Active!);
        var previous=vm.SelectedMedia;var item=Item("speed-presets.mkv","libsvtav1","13","yuv420p");vm.Media.Add(item);
        var slider=(PresetSlider)window.FindName("VideoPresetSelector");var selector=(ComboBox)window.FindName("VideoEncoderSelector");
        try
        {
            vm.SelectedMedia=item;vm.ApplyCapabilities(caps);App.ApplyLanguage("en");await Flush();
            foreach(var encoder in caps.ModernVideo)
            {
                selector.SetCurrentValue(ComboBox.SelectedValueProperty,encoder.Name);await Flush();
                Assert.Contains(Descendants<TextBlock>(selector),t=>t.Text==VideoPresentation.EncoderLabel(encoder)&&t.TextWrapping==TextWrapping.NoWrap);
                Assert.Equal(item.Options.Video.Preset,slider.Preset);
                if(!vm.CanSetPreset){Assert.False(slider.IsEnabled);continue;}
                Assert.DoesNotContain("",slider.Choices);
                if(vm.VideoPresetDefault.Length>0)Assert.Contains(vm.VideoPresetDefault,slider.Choices);
                foreach(var raw in slider.Choices.Where(v=>vm.PresetChoices.Contains(v)||v==vm.VideoPresetDefault).ToArray())
                {
                    SelectSpeedPreset(slider,raw);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Equal(raw,item.Options.Video.Preset);
                    var args=new FFmpegCommandBuilder().Build(item.Info,item.Options,Path.GetFullPath("speed-result.mkv"),caps).ToList();
                    var key=encoder.Name=="libaom-av1"?"-cpu-used":encoder.Name=="librav1e"?"-speed":"-preset";
                    if(raw.Length==0)Assert.DoesNotContain(key,args);else Assert.Equal(raw,args[args.IndexOf(key)+1]);
                }
            }
            item.Options.Video.Encoder="libsvtav1";item.Options.Video.Preset="13";await Flush();
            foreach(var language in L.SupportedLanguages)
            {
                App.ApplyLanguage(language);vm.ApplyCapabilities(caps);await Flush();
                Assert.Equal("13",item.Options.Video.Preset);Assert.Equal("13",slider.Preset);
                Assert.Equal("13",((TextBlock)window.FindName("VideoPresetValue")).Text);
            }
            item.Options.Video.Preset="-2";await Flush();
            Assert.Equal("-2",slider.Preset);Assert.Equal("-2",slider.Choices[(int)slider.Value]);
            vm.ApplyCapabilities(caps);await Flush();Assert.Equal("-2",item.Options.Video.Preset);
            item.Options.Video.Preset="";await Flush();Assert.Equal("",item.Options.Video.Preset);
            Assert.Equal(vm.VideoPresetDefault,slider.Choices[(int)slider.Value]);
            Assert.Equal(VideoPresentation.PresetLabel(vm.VideoPresetDefault,"libsvtav1",vm.VideoPresetDefault),((TextBlock)window.FindName("VideoPresetValue")).Text);
            Slider.IncreaseSmall.Execute(null,slider);await Flush();
            var nextPreset=slider.Choices[(int)slider.Value];Assert.Equal(nextPreset,item.Options.Video.Preset);
            item.Options.Video.Encoder="libx265";item.Options.Video.Preset="slow";await Flush();
            item.Options.Video.Encoder="libsvtav1";await Flush();Assert.Equal(nextPreset,slider.Preset);
            item.Options.Video.Encoder="libx265";await Flush();Assert.Equal("slow",slider.Preset);
        }
        finally{vm.SelectedMedia=previous;vm.Media.Remove(item);vm.ApplyCapabilities(TestCapabilities());App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckSvtPresets(MainViewModel vm,MainWindow window,MediaItem item)
    {
        var original=Json.Clone(item.Options.Video);var preset=(PresetSlider)window.FindName("VideoPresetSelector");
        try
        {
            item.Options.Video.Encoder="libsvtav1";await Flush();
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);await Flush();
                Assert.True(preset.IsEnabled);Assert.True(vm.CanSetPreset);
                Assert.Equal(Enumerable.Range(0,14).Select(n=>n.ToString()),vm.PresetChoices.Where(p=>p.Length>0));
                Assert.DoesNotContain("",preset.Choices);Assert.Contains("-2",preset.Choices);
                foreach(var value in new[]{"0","6","13"})
                {
                    SelectSpeedPreset(preset,value);await Flush();
                    Assert.Equal(value,item.Options.Video.Preset);Assert.Equal(value,vm.VideoPresetText);
                    var args=new FFmpegCommandBuilder().Build(item.Info,item.Options,Path.GetFullPath("svt-preset.mkv"),vm.Capabilities).ToList();
                    Assert.Equal(value,args[args.IndexOf("-preset")+1]);
                }
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
                { preset.BringIntoView();await Flush();SaveSnapshot(window,Path.Combine(artifactRoot,language+"-svt-presets.png")); }
            }
            vm.ApplyCapabilities(TestCapabilities());await Flush();
            Assert.Equal("13",item.Options.Video.Preset);Assert.Contains("13",vm.VideoPresetChoices);
        }
        finally
        {vm.SelectedMedia=null;item.Options.Video=original;vm.SelectedMedia=item;App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckSvtAdvanced(MainViewModel vm,MainWindow window,MediaItem item)
    {
        var original=Json.Clone(item.Options.Video);var advanced=(FrameworkElement)window.FindName("SvtAdvancedSettings");
        var execution=(Expander)window.FindName("VideoExecutionSettings");var structure=(Expander)window.FindName("VideoStructureSettings");
        var gop=(TextBox)window.FindName("VideoGopInput");var tune=(ComboBox)window.FindName("SvtTuneSelector");
        var grain=(TextBox)window.FindName("SvtFilmGrainInput");var dlf=(ComboBox)window.FindName("SvtDeblockingSelector");
        var parameters=(TextBox)window.FindName("SvtParametersInput");
        try
        {
            item.Options.Video.Encoder="libsvtav1";execution.SetCurrentValue(Expander.IsExpandedProperty,true);structure.SetCurrentValue(Expander.IsExpandedProperty,true);await Flush();
            Assert.True(advanced.IsVisible);Assert.Empty(item.Options.Video.SvtAv1Parameters);
            gop.SetCurrentValue(TextBox.TextProperty,"240");tune.SetCurrentValue(ComboBox.SelectedValueProperty,"0");
            grain.SetCurrentValue(TextBox.TextProperty,"0");dlf.SetCurrentValue(ComboBox.SelectedValueProperty,"2");await Flush();
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);await Flush();
                Assert.Equal(240,item.Options.Video.Gop);Assert.Equal("0",tune.SelectedValue);Assert.Equal("2",dlf.SelectedValue);
                Assert.Equal("tune=0:film-grain=0:enable-dlf=2",parameters.Text);
                Assert.Empty(vm.ValidationText);Assert.Contains("\"-g\" \"240\"",vm.CommandText);
                Assert.Contains("\"-svtav1-params\" \"tune=0:film-grain=0:enable-dlf=2\"",vm.CommandText);
                Assert.Contains(Descendants<TextBlock>(tune),t=>t.Text==L.T("0 · Visuelle Qualität (VQ)"));
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
                {advanced.BringIntoView();await Flush();SaveSnapshot(window,Path.Combine(artifactRoot,language+"-svt-advanced.png"));}
            }
            parameters.SetCurrentValue(TextBox.TextProperty,"tune=2:film-grain=12:enable-dlf=1:enable-tf=0");await Flush();
            Assert.Equal("2",tune.SelectedValue);Assert.Equal("12",grain.Text);Assert.Equal("1",dlf.SelectedValue);
            tune.SetCurrentValue(ComboBox.SelectedValueProperty,"");await Flush();
            Assert.DoesNotContain("tune=",parameters.Text);Assert.Contains("enable-tf=0",parameters.Text);
            grain.SetCurrentValue(TextBox.TextProperty,"51");await Flush();Assert.NotEmpty(vm.ValidationText);Assert.Empty(vm.CommandText);
            grain.SetCurrentValue(TextBox.TextProperty,"");await Flush();Assert.Empty(vm.ValidationText);
            item.Options.Video.Extra=new(){["svtav1-params"]="tune=0:film-grain=0:enable-dlf=2"};await Flush();
            Assert.Equal("0",tune.SelectedValue);Assert.Equal("0",grain.Text);Assert.Equal("2",dlf.SelectedValue);
            var restored=Json.Clone(item.Options.Video);vm.SelectedMedia=null;item.Options.Video=restored;vm.SelectedMedia=item;await Flush();
            Assert.Equal("tune=0:film-grain=0:enable-dlf=2",parameters.Text);Assert.Equal("240",gop.Text);
            var x265Gop=vm.Settings.Codecs.ForVideo("libx265").Gop;
            item.Options.Video.Encoder="libx265";await Flush();
            Assert.False(advanced.IsVisible);Assert.DoesNotContain("-svtav1-params",vm.CommandText);Assert.Equal(x265Gop,item.Options.Video.Gop);
            item.Options.Video.Encoder="libsvtav1";await Flush();Assert.Equal(240,item.Options.Video.Gop);Assert.Equal("tune=0:film-grain=0:enable-dlf=2",parameters.Text);
        }
        finally
        {execution.SetCurrentValue(Expander.IsExpandedProperty,false);structure.SetCurrentValue(Expander.IsExpandedProperty,false);vm.SelectedMedia=null;item.Options.Video=original;vm.SelectedMedia=item;App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckThreadControls(MainViewModel vm,MainWindow window,MediaItem item,ServiceProvider services)
    {
        var original=Json.Clone(item.Options.Video);var panel=(FrameworkElement)window.FindName("SoftwareThreadSettings");
        var execution=(Expander)window.FindName("VideoExecutionSettings");
        var mode=(ComboBox)window.FindName("ThreadModeSelector");var count=(TextBox)window.FindName("ThreadCountInput");
        var percent=(TextBox)window.FindName("ThreadPercentInput");var lp=(ComboBox)window.FindName("SvtParallelismSelector");
        try
        {
            execution.SetCurrentValue(Expander.IsExpandedProperty,true);
            foreach(var language in new[]{"de","en","pl"})
            {
                App.ApplyLanguage(language);
                foreach(var encoder in new[]{"libx264","libx265"})
                {
                    item.Options.Video.Encoder=encoder;await Flush();Assert.True(panel.IsVisible);
                    mode.SetCurrentValue(ComboBox.SelectedItemProperty,ThreadControlMode.FesteAnzahl);
                    count.SetCurrentValue(TextBox.TextProperty,"1");await Flush();
                    Assert.True(count.IsEnabled);Assert.False(percent.IsEnabled);Assert.True(count.IsVisible);Assert.False(percent.IsVisible);Assert.Empty(vm.ValidationText);
                    Assert.Contains(encoder=="libx265"?"pools=1":"\"-threads:v:0\" \"1\"",vm.CommandText);
                    mode.SetCurrentValue(ComboBox.SelectedItemProperty,ThreadControlMode.CPUAnteil);
                    percent.SetCurrentValue(TextBox.TextProperty,"50");await Flush();
                    Assert.False(count.IsEnabled);Assert.True(percent.IsEnabled);Assert.False(count.IsVisible);Assert.True(percent.IsVisible);Assert.Equal(ThreadControlMode.CPUAnteil,item.Options.Video.ThreadMode);
                    Assert.Contains(Math.Max(1,Environment.ProcessorCount/2).ToString(),vm.ThreadControlDescription);
                    mode.SetCurrentValue(ComboBox.SelectedItemProperty,ThreadControlMode.Automatisch);await Flush();
                    Assert.False(count.IsVisible);Assert.False(percent.IsVisible);Assert.Equal(1,item.Options.Video.ThreadCount);Assert.Equal(50,item.Options.Video.ThreadPercent);
                    mode.SetCurrentValue(ComboBox.SelectedItemProperty,ThreadControlMode.CPUAnteil);await Flush();
                }
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                {panel.BringIntoView();await Flush();SaveSnapshot(window,Path.Combine(artifacts,language+"-threads.png"));}
                item.Options.Video.Encoder="libsvtav1";await Flush();Assert.False(panel.IsVisible);
                lp.SetCurrentValue(ComboBox.SelectedValueProperty,"1");await Flush();Assert.Contains("lp=1",vm.CommandText);
                lp.SetCurrentValue(ComboBox.SelectedValueProperty,"");await Flush();Assert.DoesNotContain("lp=",vm.CommandText);
            }
            item.Options.Video.Encoder="hevc_nvenc";await Flush();Assert.False(panel.IsVisible);Assert.DoesNotContain("-threads:v:0",vm.CommandText);
            var parallel=(ComboBox)window.FindName("ParallelJobsSelector");var gpu=(ComboBox)window.FindName("ParallelGpuJobsSelector");
            var efficiency=(CheckBox)window.FindName("EfficiencyModeCheckBox");
            parallel.SetCurrentValue(ComboBox.SelectedItemProperty,2);gpu.SetCurrentValue(ComboBox.SelectedItemProperty,2);
            efficiency.SetCurrentValue(ToggleButton.IsCheckedProperty,false);await Flush();
            Assert.Equal(2,vm.Settings.ParallelJobs);Assert.Equal(2,vm.Settings.ParallelGpuJobs);Assert.False(vm.Settings.EfficiencyMode);
            efficiency.SetCurrentValue(ToggleButton.IsCheckedProperty,true);await Flush();
            await services.GetRequiredService<SettingsService>().SaveAsync();
            var saved=await AtomicFile.ReadJsonAsync<AppSettings>(Path.Combine(services.GetRequiredService<AppPaths>().Root,"settings.json"));
            Assert.NotNull(saved);Assert.True(saved.EfficiencyMode);Assert.Equal(2,saved.ParallelJobs);
        }
        finally
        {execution.SetCurrentValue(Expander.IsExpandedProperty,false);vm.SelectedMedia=null;item.Options.Video=original;vm.SelectedMedia=item;vm.ParallelJobLimit=1;vm.ParallelGpuJobLimit=1;App.ApplyLanguage("de");await Flush();}
    }

    private static async Task CheckVideoInputs(MainViewModel vm,MainWindow window,MediaItem item)
    {
        var original=Json.Clone(item.Options.Video);
        var rate=(ComboBox)window.FindName("VideoRateControlSelector");
        var quality=(QualitySlider)window.FindName("VideoQualityInput");
        var bitrate=(TextBox)window.FindName("VideoBitrateInput");
        var preset=(PresetSlider)window.FindName("VideoPresetSelector");
        try
        {
            foreach(var language in new[]{"de","en","pl"})
            foreach(var encoder in new[]{"hevc_nvenc","libx265","libx264"})
            {
                App.ApplyLanguage(language);
                item.Options.Video.Encoder=encoder;await Flush();
                var video=item.Options.Video;
                video.Quality=27;video.BitRateKbps=5200;
                foreach(var mode in new[]{RateControl.Qualität,RateControl.QP,RateControl.Bitrate,RateControl.CBR,RateControl.Qualität})
                {
                    rate.SetCurrentValue(ComboBox.SelectedItemProperty,mode);await Flush();
                    Assert.Equal(mode,video.RateControl);
                    Assert.Equal(mode is RateControl.Qualität or RateControl.QP,quality.IsEnabled);
                    Assert.Equal(mode is RateControl.Bitrate or RateControl.CBR,bitrate.IsEnabled);
                    Assert.Equal(mode is RateControl.Qualität or RateControl.QP,quality.IsVisible);
                    Assert.Equal(mode is RateControl.Bitrate or RateControl.CBR,bitrate.IsVisible);
                    Assert.Equal(27,video.Quality);Assert.Equal(5200,video.BitRateKbps);
                    Assert.Contains(Descendants<TextBlock>(rate),t=>t.Text==VideoPresentation.RateControlLabel(mode,encoder));
                }
                foreach(var raw in encoder.EndsWith("_nvenc")?new[]{"p7","slow","medium","fast"}:new[]{"fast","medium","slow"})
                {
                    var label=VideoPresentation.PresetLabel(raw,encoder,vm.VideoPresetDefault);
                    Assert.Contains(label,vm.VideoPresetChoices);
                    SelectSpeedPreset(preset,raw);await Flush();
                    Assert.Equal(raw,video.Preset);Assert.Equal(label,((TextBlock)window.FindName("VideoPresetValue")).Text);
                    var args=new FFmpegCommandBuilder().Build(item.Info,item.Options,Path.GetFullPath("preset-result.mkv"),vm.Capabilities).ToList();
                    Assert.Equal(raw,args[args.IndexOf("-preset")+1]);
                    Assert.DoesNotContain(args,a=>a.Contains(" — "));
                }
                video.Preset="custom-preset";await Flush();
                Assert.Equal("custom-preset",video.Preset);Assert.Equal("custom-preset",preset.Preset);
                Assert.DoesNotContain("",preset.Choices);
                if(vm.VideoPresetDefault.Length>0)
                {
                    video.Preset="";await Flush();
                    Assert.Equal("",video.Preset);Assert.Equal(vm.VideoPresetDefault,preset.Choices[(int)preset.Value]);
                    var readout=(TextBlock)window.FindName("VideoPresetValue");
                    Assert.Equal(VideoPresentation.PresetLabel(vm.VideoPresetDefault,encoder,vm.VideoPresetDefault),readout.Text);
                    var label=(TextBlock)window.FindName("VideoPresetLabel");
                    var labelBounds=label.TransformToAncestor(window).TransformBounds(new Rect(label.RenderSize));
                    var valueBounds=readout.TransformToAncestor(window).TransformBounds(new Rect(readout.RenderSize));
                    Assert.True(valueBounds.Left>labelBounds.Right);Assert.True(Math.Abs(valueBounds.Top-labelBounds.Top)<5);
                    SelectSpeedPreset(preset,vm.VideoPresetDefault);Slider.IncreaseSmall.Execute(null,preset);await Flush();
                    Assert.Equal(preset.Choices[(int)preset.Value],video.Preset);
                    SelectSpeedPreset(preset,vm.VideoPresetDefault);await Flush();
                    Assert.Equal(vm.VideoPresetDefault,video.Preset);
                    if(encoder=="libx264"&&language=="en"&&Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} defaultsRoot)
                        SaveSnapshot(window,Path.Combine(defaultsRoot,"en-x264-default.png"));
                }
                SelectSpeedPreset(preset,encoder.EndsWith("_nvenc")?"p7":"slow");await Flush();
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
                {
                    SaveSnapshot(window,Path.Combine(artifactRoot,language+"-"+encoder+"-quality.png"));
                    rate.SetCurrentValue(ComboBox.SelectedItemProperty,RateControl.Bitrate);await Flush();
                    SaveSnapshot(window,Path.Combine(artifactRoot,language+"-"+encoder+"-bitrate.png"));
                    SelectSpeedPreset(preset,"slow");
                    preset.BringIntoView();await Flush();
                    SaveSnapshot(window,Path.Combine(artifactRoot,language+"-"+encoder+"-presets.png"));
                }
            }
        }
        finally
        {
            item.Options.Video=original;vm.SelectedMedia=null;vm.SelectedMedia=item;App.ApplyLanguage("de");await Flush();
        }
    }

    private static async Task CheckQualitySlider(MainViewModel vm, MainWindow window, MediaItem item)
    {
        var original = Json.Clone(item.Options.Video);
        var slider = (QualitySlider)window.FindName("VideoQualityInput");
        var readout = (TextBlock)window.FindName("VideoQualityValue");
        var minimum = (TextBlock)window.FindName("VideoQualityMinimum");
        var maximum = (TextBlock)window.FindName("VideoQualityMaximum");
        MediaItem? other = null;
        try
        {
            foreach (var (encoder, mode, upper, step) in new[]
            {
                ("libx265", RateControl.Qualität, 51d, .5), ("libsvtav1", RateControl.Qualität, 63d, 1d),
                ("av1_nvenc", RateControl.Qualität, 63d, .5), ("av1_nvenc", RateControl.QP, 255d, 1d),
                ("librav1e", RateControl.Qualität, 255d, 1d), ("libvvenc", RateControl.QP, 63d, 1d)
            })
            {
                item.Options.Video.Encoder = encoder; item.Options.Video.RateControl = mode; await Flush();
                var lower = encoder == "av1_nvenc" && mode == RateControl.QP ? 1d : 0d;
                Assert.Equal(lower, slider.Minimum); Assert.Equal(upper, slider.Maximum); Assert.Equal(step, slider.SmallChange);
                Assert.Equal(lower.ToString("0"), minimum.Text); Assert.Equal(upper.ToString("0"), maximum.Text);
                foreach (var value in new[] { lower, upper, step == .5 ? 23.5 : 23d })
                {
                    slider.SetCurrentValue(RangeBase.ValueProperty, value); await Flush();
                    Assert.Equal(value, item.Options.Video.Quality);
                    Assert.Equal(value.ToString("0.##",System.Globalization.CultureInfo.CurrentCulture), readout.Text);
                    var args = new FFmpegCommandBuilder().Build(item.Info,item.Options,Path.GetFullPath("slider-result.mkv")).ToList();
                    Assert.Equal(FFmpegCommandBuilder.Number(value), args[args.IndexOf("-"+VideoQualityScale.Parameter(item.Options.Video))+1]);
                }
                Slider.IncreaseSmall.Execute(null, slider); await Flush();
                Assert.Equal((step == .5 ? 23.5 : 23d) + step, item.Options.Video.Quality);
            }
            // Switching from a 255-point scale to CRF must not push Slider's coerced value into saved options.
            other = Item("qp-preset.mkv", "av1_nvenc", "p6", "yuv420p");
            other.Options.Video.RateControl = RateControl.QP; other.Options.Video.Quality = 200;
            vm.Media.Add(other); vm.SelectedMedia = other; await Flush();
            Assert.Equal(255, slider.Maximum); Assert.Equal(200, slider.Value);
            item.Options.Video.Encoder = "libx265"; item.Options.Video.RateControl = RateControl.Qualität; item.Options.Video.Quality = 21.5;
            vm.SelectedMedia = item; await Flush();
            Assert.Equal(51, slider.Maximum); Assert.Equal(21.5, item.Options.Video.Quality);
            vm.SelectedMedia = other; await Flush();
            Assert.Equal(200, other.Options.Video.Quality); Assert.Equal(200, slider.Value);
            other.Options.Video.Quality = 300; await Flush();
            Assert.Equal(300, other.Options.Video.Quality); Assert.Equal("300", readout.Text);
            Assert.Contains("zwischen 1 und 255", vm.ValidationText);
            // Simulate clicking the existing thumb without depending on the desktop cursor position.
            slider.IsMoveToPointEnabled = false;
            try { slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left)
                { RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent }); }
            finally { slider.IsMoveToPointEnabled = true; }
            await Flush();
            Assert.Equal(255, other.Options.Video.Quality);
            other.Options.Video.Quality = 300; await Flush();
            Slider.DecreaseSmall.Execute(null, slider); await Flush();
            Assert.InRange(other.Options.Video.Quality, 1, 255);
            Assert.DoesNotContain("zwischen 1 und 255", vm.ValidationText);
            other.Options.Video.Quality = 0; await Flush();
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Home)
                { RoutedEvent=Keyboard.PreviewKeyDownEvent }); await Flush();
            Assert.Equal(1, other.Options.Video.Quality);
        }
        finally
        {
            vm.SelectedMedia = null; item.Options.Video = original; vm.SelectedMedia = item;
            if (other is not null) vm.Media.Remove(other);
            await Flush();
        }
    }

    private static async Task CheckThemes(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        var originalTheme=vm.Settings.ThemeId;var originalDark=vm.Settings.DarkMode;
        var originalApplied=App.CurrentTheme.Id;var originalLanguage=L.Language;
        var command=vm.CommandText;var primary=Descendants<Button>(window).Single(b=>ReferenceEquals(b.Command,vm.StartQueueCommand));
        static Color ColorOf(object brush)=>((SolidColorBrush)brush).Color;
        static Color Hex(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
        async Task Open(string? saveTheme)
        {
            Exception? modalFailure=null;
            _=Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
            {
                var dialog=Application.Current.Windows.OfType<SettingsWindow>().Single();
                try
                {
                    dialog.ShowInTaskbar=false;dialog.ShowActivated=false;dialog.Left=-10000;dialog.Top=-10000;
                    var tabs=Descendants<TabControl>(dialog).Single();
                    var existingLabel=Descendants<TextBlock>(dialog).First(t=>t.Text==L.T("Sprache"));
                    tabs.SetCurrentValue(TabControl.SelectedItemProperty,tabs.Items.OfType<TabItem>().Single(t=>t.Name=="AppearanceSettingsTab"));await Flush();
                    var selector=Descendants<ListBox>(dialog).Single(c=>c.Name=="ThemeSelector");
                    Assert.Equal(ThemeCatalog.All.Select(t=>t.Id),selector.Items.Cast<AppTheme>().Select(t=>t.Id));
                    if(saveTheme is null)
                    {
                        foreach(var language in new[]{"de","en","pl"})
                        {
                            App.ApplyLanguage(language);await Flush();
                            foreach(var theme in ThemeCatalog.All)
                            {
                                selector.SetCurrentValue(ListBox.SelectedValueProperty,theme.Id);await Flush();
                                Assert.Equal(theme.Id,dialog.Result.ThemeId);Assert.Equal(theme.Id,App.CurrentTheme.Id);
                                Assert.Equal(originalTheme,vm.Settings.ThemeId);Assert.Equal(originalDark,vm.Settings.DarkMode);
                                Assert.Equal(Hex(theme.Bg),ColorOf(window.Background));Assert.Equal(Hex(theme.Surface),ColorOf(((DataGrid)window.FindName("QueueGrid")).Background));
                                Assert.Equal(Hex(theme.Accent),ColorOf(primary.Background));Assert.Equal(Hex(theme.AccentText),ColorOf(primary.Foreground));
                                Assert.All(Descendants<TextBlock>(primary),text=>Assert.Equal(Hex(theme.AccentText),ColorOf(text.Foreground)));
                                Assert.Equal(Hex(theme.Muted),ColorOf(existingLabel.Foreground));
                                Assert.Equal(Hex(theme.Raised),ColorOf(Application.Current.Resources[SystemColors.WindowBrushKey]));
                                Assert.Equal(Hex(theme.Selection),ColorOf(Application.Current.Resources[SystemColors.HighlightBrushKey]));
                                Assert.Contains(Descendants<TextBlock>(selector),t=>t.Text==L.T(theme.Name));
                                Assert.Equal(command,vm.CommandText);
                                if(language=="de"&&Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                                {SaveSnapshot(window,Path.Combine(artifacts,"theme-"+theme.Id+".png"));SaveSnapshot(dialog,Path.Combine(artifacts,"picker-"+theme.Id+".png"));}
                            }
                        }
                        Descendants<Button>(dialog).Single(b=>Equals(b.Content,L.T("Abbrechen"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else
                    {
                        selector.SetCurrentValue(ListBox.SelectedValueProperty,saveTheme);await Flush();
                        Descendants<Button>(dialog).Single(b=>Equals(b.Content,L.T("Speichern"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }
                catch(Exception e){modalFailure=e;dialog.Close();}
            });
            await vm.EditSettingsCommand.ExecuteAsync(null);if(modalFailure is not null)throw modalFailure;await Flush();
        }
        try
        {
            await Open(null);Assert.Equal(originalApplied,App.CurrentTheme.Id);Assert.Equal(originalTheme,vm.Settings.ThemeId);
            foreach(var themeId in new[]{"glacier","sakura"})
            {
                await Open(themeId);Assert.Equal(themeId,vm.Settings.ThemeId);Assert.Equal(themeId,App.CurrentTheme.Id);
                var paths=services.GetRequiredService<AppPaths>();var restored=new SettingsService(paths,services.GetRequiredService<LoggingService>());
                await restored.LoadAsync();Assert.Equal(themeId,restored.Current.ThemeId);
                Assert.Equal(ThemeCatalog.Resolve(themeId).IsDark,restored.Current.DarkMode);
            }
        }
        finally
        {
            vm.Settings.ThemeId=originalTheme;vm.Settings.DarkMode=originalDark;App.ApplyTheme(originalApplied);App.ApplyLanguage(originalLanguage);
            await services.GetRequiredService<SettingsService>().SaveAsync();await Flush();
        }
    }

    private static async Task CheckLanguages(MainViewModel vm,MainWindow window,ServiceProvider services)
    {
        await SelectWorkspace(window,"EncodeSettingsTab");
        var builder=services.GetRequiredService<FFmpegCommandBuilder>();
        var selected=vm.SelectedMedia!;
        var target=Path.Combine(services.GetRequiredService<AppPaths>().Root,"localized-output.mkv");
        var command=builder.Build(selected.Info,selected.Options,target);
        var rateValue=selected.Options.Video.RateControl;
        var sourcePreset=selected.Options.PresetName;
        var testJob=new EncodingJob{Source=selected.Info,Options=Json.Clone(selected.Options),Status=JobStatus.Wartend};
        vm.Jobs.Add(testJob); vm.SelectedJob=testJob;
        async Task Change(string code,bool save)
        {
            Exception? modalFailure=null;
            var previous=vm.Settings.Language;
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
            {
                var dialog=Application.Current.Windows.OfType<SettingsWindow>().Single();
                try
                {
                    dialog.ShowInTaskbar=false;dialog.ShowActivated=false;dialog.Left=-10000;dialog.Top=-10000;
                    var choice=Descendants<ComboBox>(dialog).Single(c=>c.Name=="LanguageSelector");
                    Assert.Equal(L.SupportedLanguages,choice.Items.Cast<LanguageOption>().Select(l=>l.Code));
                    choice.SetCurrentValue(ComboBox.SelectedValueProperty,code); await Flush();
                    Assert.Equal(code,dialog.Result.Language);
                    Assert.Contains(Descendants<TextBlock>(choice),t=>t.Text==LanguageOption.All.Single(l=>l.Code==code).Name);
                    Assert.Equal(previous,vm.Settings.Language);
                    Assert.Equal(previous,L.Language);
                    var artifactRoot=Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS");
                    if(artifactRoot is {Length:>0})SaveSnapshot(dialog,Path.Combine(artifactRoot,previous+"-settings.png"));
                    if(save)Descendants<Button>(dialog).Single(b=>Equals(b.Content,L.T("Speichern"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.Close();
                }
                catch(Exception e){modalFailure=e;dialog.Close();}
            });
            await vm.EditSettingsCommand.ExecuteAsync(null);
            if(modalFailure is not null)throw modalFailure;
            await Flush();
        }
        foreach(var code in new[]{"en","pl","fr","es","tr","zh","ru","ja","de"})
        {
            await Change(code,true);
            Assert.Equal(code,L.Language);Assert.Equal(code,vm.Settings.Language);
            Assert.Contains(code=="zh"?"Microsoft YaHei":code=="ja"?"Yu Gothic":"Segoe UI",window.FontFamily.Source);
            var settingsButton=Descendants<Button>(window).Single(b=>ButtonHints.GetKey(b)=="EditSettings");
            Assert.Equal(ButtonHelpCatalog.Text("EditSettings",code),settingsButton.ToolTip);
            Assert.Contains(Descendants<Button>(window),b=>Equals(b.Content,L.T("Einstellungen")));
            Assert.Contains(Descendants<TextBlock>(window),t=>t.Text==L.T("Qualitätssteuerung"));
            Assert.Equal(L.T("Einstellungen gespeichert."),vm.StatusText);
            Assert.Equal(L.EnumText(JobStatus.Wartend),testJob.DisplayStatus);
            Assert.Equal(L.T("Datei / Konvertierung"),((DataGrid)window.FindName("QueueGrid")).Columns[0].Header);
            var rate=Descendants<ComboBox>(window).Single(c=>ReferenceEquals(c.ItemsSource,MainViewModel.RateControls));
            Assert.Equal(rateValue,rate.SelectedItem);
            Assert.Contains(Descendants<TextBlock>(rate),t=>t.Text==VideoPresentation.RateControlLabel(rateValue,selected.Options.Video.Encoder));
            Assert.Equal(sourcePreset,selected.Options.PresetName);
            Assert.Equal(command,builder.Build(selected.Info,selected.Options,target));
            var paths=services.GetRequiredService<AppPaths>();
            var restored=new SettingsService(paths,services.GetRequiredService<LoggingService>());
            await restored.LoadAsync();Assert.Equal(code,restored.Current.Language);
            testJob.OutputInfo=selected.Info;
            var resultsWindow=new ResultsWindow([testJob]){Owner=window,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000};
            try
            {
                resultsWindow.Show();await Flush();
                var results=Descendants<DataGrid>(resultsWindow).Single();
                Assert.Equal("Encoder",((System.Windows.Data.Binding)((DataGridTextColumn)results.Columns[0]).Binding).Path.Path);
                Assert.Equal("Preset",((System.Windows.Data.Binding)((DataGridTextColumn)results.Columns[1]).Binding).Path.Path);
                Assert.Equal("Raster",((System.Windows.Data.Binding)((DataGridTextColumn)results.Columns[2]).Binding).Path.Path);
                Assert.Equal("Bitrate",((System.Windows.Data.Binding)((DataGridTextColumn)results.Columns[7]).Binding).Path.Path);
                Assert.Equal(selected.Options.Video.Encoder,((TextBlock)results.Columns[0].GetCellContent(results.Items[0])).Text);
                Assert.Equal(selected.Options.Video.Preset,((TextBlock)results.Columns[1].GetCellContent(results.Items[0])).Text);
                Assert.Equal(L.T("Vollständig"),((TextBlock)results.Columns[3].GetCellContent(results.Items[0])).Text);
            }
            finally{resultsWindow.Close();}
            if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifactRoot)
                SaveSnapshot(window,Path.Combine(artifactRoot,code+"-desktop.png"));
        }
        await Change("pl",false);
        Assert.Equal("de",vm.Settings.Language);Assert.Equal("de",L.Language);
        vm.Jobs.Remove(testJob);vm.SelectedJob=null;
    }
    private static async Task CheckComparisonCoverage(EncodeComparisonViewModel comparison,EncodeComparisonControl control,Window window,ServiceProvider services,MediaInfo media,List<EncodingJob> jobs)
    {
        var queue=services.GetRequiredService<QueueService>();var navigation=services.GetRequiredService<FrameNavigationService>();
        var selected=new[]{comparison.SelectedA!.Input.Job!,comparison.SelectedB!.Input.Job!};var cuts=new List<EncodingJob>();
        foreach(var index in new[]{0,1})
        {
            var options=Json.Clone(selected[index].Options);options.FileNamePattern="coverage-"+index;
            if(index==1){options.ClipStart=.25;options.ClipDuration=2.5;}
            options.Cuts.Add(new(await navigation.ResolveAsync(selected[index].Tools,media,index==0?.5:1.5),await navigation.ResolveAsync(selected[index].Tools,media,index==0?1:2)));
            var job=await queue.AddAsync(media,options);jobs.Add(job);cuts.Add(job);queue.Start(job);
        }
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25)))while(cuts.Any(j=>j.Status is JobStatus.Wartend or JobStatus.Läuft))await Task.Delay(30,timeout.Token);
        Assert.All(cuts,j=>Assert.Equal(JobStatus.Abgeschlossen,j.Status));
        comparison.SelectJobs(cuts);await comparison.WaitAsync();comparison.SideBySide=true;comparison.Zoom=0;await Flush();
        var a=(ComparisonAvailabilityStrip)control.FindName("AvailabilityA");var b=(ComparisonAvailabilityStrip)control.FindName("AvailabilityB");
        Assert.True(a.IsVisible&&b.IsVisible);Assert.Equal(3,a.Intervals!.Count);Assert.Equal(5,b.Intervals!.Count);
        Assert.Equal(.5,a.Intervals[1].Start,5);Assert.Equal(1,a.Intervals[1].End,5);Assert.Equal(ComparisonAvailability.Unavailable,a.Intervals[1].Availability);
        Assert.Equal(ComparisonAvailability.Unavailable,b.Intervals[0].Availability);Assert.Equal(1.5,b.Intervals[2].Start,5);
        Assert.Equal(a.TranslatePoint(new Point(),control).X,b.TranslatePoint(new Point(),control).X,4);Assert.Equal(a.ActualWidth,b.ActualWidth,4);
        Assert.Equal(comparison.Maximum/2,a.TimeAt(a.ActualWidth/2),6);
        var slider=(Slider)control.FindName("ComparisonTimeline");var track=(Track)slider.Template.FindName("PART_Track",slider);
        Assert.InRange(Math.Abs(a.TranslatePoint(new Point(),control).X-track.TranslatePoint(new Point(track.Thumb.ActualWidth/2,0),control).X),0,.01);
        slider.Value=.75;await comparison.WaitAsync();await Flush();
        Assert.Null(comparison.ImageA);Assert.NotNull(comparison.ImageB);Assert.NotNull(comparison.OriginalImage);Assert.Equal(comparison.Position,a.Position);
        var pane=(ComparisonImagePane)control.FindName("PaneA");Assert.Equal(comparison.MessageA,pane.EmptyMessage);
        Assert.Contains(Descendants<TextBlock>(pane),t=>t.IsVisible&&t.Text==comparison.MessageA);
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(window,Path.Combine(artifacts,"coverage-missing-a.png"));
        var selectorA=(ComboBox)control.FindName("EncodeASelector");var selectorB=(ComboBox)control.FindName("EncodeBSelector");
        EncodeChoice Choice(EncodingJob job)=>comparison.Choices.Single(c=>c.Input.Job?.Id==job.Id);
        var frameBefore=comparison.LoadedFrames!.Anchor;
        selectorA.SelectedItem=Choice(selected[0]);Assert.Equal(.75,comparison.Position,6);await comparison.WaitAsync();
        Assert.Equal(frameBefore,comparison.LoadedFrames!.Anchor);Assert.NotNull(comparison.ImageA);
        selectorA.SelectedItem=Choice(cuts[1]);Assert.Equal(.75,comparison.Position,6);await comparison.WaitAsync();Assert.Equal(.75,comparison.Position,6);
        // Rapid changes must cancel earlier loads without resetting the shared source position.
        selectorA.SelectedItem=Choice(selected[1]);selectorA.SelectedItem=Choice(cuts[0]);await comparison.WaitAsync();
        Assert.Equal(.75,comparison.Position,6);Assert.Equal(frameBefore,comparison.LoadedFrames!.Anchor);Assert.Null(comparison.ImageA);
        slider.Value=1.75;await comparison.WaitAsync();await Flush();Assert.NotNull(comparison.ImageA);Assert.Null(comparison.ImageB);
        comparison.SideBySide=false;comparison.FocusIndex=1;await Flush();
        Assert.Contains(Descendants<TextBlock>((ComparisonImagePane)control.FindName("FocusPane")),t=>t.IsVisible&&t.Text==comparison.MessageB);
        selectorB.SelectedItem=Choice(selected[1]);Assert.Equal(1.75,comparison.Position,6);await comparison.WaitAsync();Assert.NotNull(comparison.ImageB);
        selectorB.SelectedItem=Choice(cuts[1]);await comparison.WaitAsync();Assert.Equal(1.75,comparison.Position,6);Assert.Null(comparison.ImageB);
        slider.Value=1;await comparison.WaitAsync();Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.ImageB);
        comparison.Mode=EncodeComparisonMode.EncodeAndReference;await comparison.WaitAsync();await Flush();Assert.False(b.IsVisible);Assert.True(a.IsVisible);
        comparison.Mode=EncodeComparisonMode.TwoEncodesAndReference;comparison.SideBySide=true;
        comparison.SelectJobs(selected);Assert.Equal(1,comparison.Position,6);await comparison.WaitAsync();await Flush();
        Assert.Equal(1,comparison.Position,6);
        Assert.Equal(ComparisonAvailability.Available,Assert.Single(a.Intervals!).Availability);Assert.Equal(ComparisonAvailability.Available,Assert.Single(b.Intervals!).Availability);
        slider.Value=2.9;await comparison.WaitAsync();comparison.SelectJobs(cuts);Assert.Equal(2.9,comparison.Position,6);await comparison.WaitAsync();
        Assert.Equal(2.9,comparison.Position,6);Assert.Null(comparison.ImageB);
        comparison.Mode=EncodeComparisonMode.EncodeAndReference;await comparison.WaitAsync();
        selectorA.SelectedItem=Choice(cuts[1]);await comparison.WaitAsync();Assert.Equal(2.9,comparison.Position,6);Assert.Null(comparison.ImageA);Assert.NotNull(comparison.OriginalImage);
        // A genuinely different source still starts at its own clip's first retained frame.
        var otherPath=Path.Combine(Path.GetDirectoryName(media.Path)!,"other-comparison-source.mkv");File.Copy(media.Path,otherPath);
        var otherMedia=await services.GetRequiredService<MediaProbeService>().ProbeAsync(selected[0].Tools,otherPath);
        var otherOptions=Json.Clone(cuts[1].Options);otherOptions.FileNamePattern="other-source";
        var other=await queue.AddAsync(otherMedia,otherOptions);jobs.Add(other);queue.Start(other);
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25)))while(other.Status is JobStatus.Wartend or JobStatus.Läuft)await Task.Delay(30,timeout.Token);
        Assert.Equal(JobStatus.Abgeschlossen,other.Status);
        selectorA.SelectedItem=Choice(other);Assert.Equal(.25,comparison.Position,6);await comparison.WaitAsync();Assert.Equal(.25,comparison.Position,6);Assert.NotNull(comparison.ImageA);
        comparison.SelectJobs(selected);Assert.Equal(0,comparison.Position,6);await comparison.WaitAsync();await Flush();
    }

    private static async Task CheckComparisonModes(EncodeComparisonViewModel comparison,EncodeComparisonControl control,Window window)
    {
        var selector=(ComboBox)control.FindName("ComparisonModeSelector");var match=(CheckBox)control.FindName("MatchReferenceToggle");
        Assert.Null(control.FindName("CompareTwoEncodesToggle"));Assert.Equal(3,selector.Items.Count);
        var a=comparison.SelectedA;var b=comparison.SelectedB;var position=comparison.Position;
        match.IsChecked=true;await comparison.WaitAsync();await Flush();Assert.True(comparison.MatchReference);
        comparison.FocusIndex=2;Assert.True(comparison.BeginReferenceHold());
        selector.SelectedValue=EncodeComparisonMode.TwoEncodes;await comparison.WaitAsync();await Flush();
        Assert.Equal(2,comparison.PanelCount);Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.ImageB);Assert.Null(comparison.OriginalImage);
        Assert.False(match.IsVisible);Assert.False(match.IsEnabled);Assert.False(comparison.MatchReference);Assert.Equal(0,comparison.FocusIndex);
        var loaded=comparison.WaitAsync();var image=comparison.ImageA;comparison.MatchReference=true;
        Assert.False(comparison.MatchReference);Assert.Same(loaded,comparison.WaitAsync());Assert.Same(image,comparison.ImageA);
        if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)SaveSnapshot(window,Path.Combine(artifacts,"mode-encodes-only.png"));
        selector.SelectedValue=EncodeComparisonMode.EncodeAndReference;await comparison.WaitAsync();await Flush();
        Assert.Equal(2,comparison.PanelCount);Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.OriginalImage);Assert.Null(comparison.ImageB);
        Assert.True(match.IsVisible&&match.IsEnabled);Assert.False(((ComboBox)control.FindName("EncodeBSelector")).IsVisible);
        Assert.Same(a,comparison.SelectedA);Assert.Same(b,comparison.SelectedB);Assert.Equal(position,comparison.Position,6);
        match.IsChecked=true;await comparison.WaitAsync();Assert.True(comparison.MatchReference);
        selector.SelectedValue=EncodeComparisonMode.TwoEncodesAndReference;await comparison.WaitAsync();await Flush();
        Assert.Equal(3,comparison.PanelCount);Assert.NotNull(comparison.ImageA);Assert.NotNull(comparison.ImageB);Assert.NotNull(comparison.OriginalImage);
        Assert.True(match.IsVisible&&match.IsEnabled);Assert.True(comparison.MatchReference);
        Assert.Same(a,comparison.SelectedA);Assert.Same(b,comparison.SelectedB);Assert.Equal(position,comparison.Position,6);
        loaded=comparison.WaitAsync();selector.SelectedValue=EncodeComparisonMode.TwoEncodesAndReference;Assert.Same(loaded,comparison.WaitAsync());
    }

    private static async Task CheckComparisonViewport(EncodeComparisonViewModel comparison,EncodeComparisonControl control,Window window)
    {
        var width=window.Width;var height=window.Height;var language=L.Language;
        var focus=(ComboBox)control.FindName("ComparisonFocus");var pane=(ComparisonImagePane)control.FindName("FocusPane");
        var picture=(Image)pane.Scroll.Content;
        Rect Bounds()=>new(pane.TranslatePoint(new Point(),control),pane.RenderSize);
        try
        {
            foreach(var locale in new[]{"en"}.Concat(L.SupportedLanguages.Where(l=>l!="en")))
            foreach(var size in locale=="en"?new[]{(1500d,950d),(900d,600d)}:new[]{(900d,600d)})
            {
                App.ApplyLanguage(locale);window.Width=size.Item1;window.Height=size.Item2;comparison.SideBySide=false;
                foreach(var zoom in new[]{0d,4d})
                {
                    comparison.Zoom=zoom;focus.SelectedIndex=0;await Flush();
                    if(zoom>0){pane.Scroll.ScrollToHorizontalOffset(100);pane.Scroll.ScrollToVerticalOffset(80);await Flush();}
                    var bounds=Bounds();var rendered=picture.RenderSize;var x=pane.Scroll.HorizontalOffset;var y=pane.Scroll.VerticalOffset;var loaded=comparison.WaitAsync();
                    void Unchanged()
                    {
                        Assert.Equal(bounds,Bounds());Assert.Equal(rendered,picture.RenderSize);
                        Assert.Equal(x,pane.Scroll.HorizontalOffset,3);Assert.Equal(y,pane.Scroll.VerticalOffset,3);Assert.Same(loaded,comparison.WaitAsync());
                    }
                    focus.SelectedIndex=2;await Flush();Assert.Same(comparison.OriginalImage,pane.Source);Unchanged();
                    focus.SelectedIndex=0;await Flush();Assert.Same(comparison.ImageA,pane.Source);Unchanged();
                    Assert.True(comparison.BeginReferenceHold());await Flush();Unchanged();comparison.EndReferenceHold();await Flush();Unchanged();
                }
                comparison.Zoom=0;comparison.SideBySide=true;await Flush();
                var reference=(ComparisonImagePane)control.FindName("OriginalPane");var encode=(ComparisonImagePane)control.FindName("PaneA");
                Assert.Equal(reference.ActualHeight,encode.ActualHeight,3);
                Assert.Equal(reference.TranslatePoint(new Point(),control).Y,encode.TranslatePoint(new Point(),control).Y,3);
                if(locale=="en"&&size.Item1==900&&Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                {
                    SaveSnapshot(window,Path.Combine(artifacts,"stable-reference-side-by-side.png"));
                    comparison.SideBySide=false;focus.SelectedIndex=2;await Flush();SaveSnapshot(window,Path.Combine(artifacts,"stable-reference-focus.png"));
                }
            }
        }
        finally
        {
            comparison.EndReferenceHold();comparison.Zoom=0;comparison.SideBySide=false;focus.SelectedIndex=0;
            window.Width=width;window.Height=height;App.ApplyLanguage(language);await Flush();
        }
    }

    private static async Task SelectWorkspace(MainWindow window,string name)
    {
        ((TabControl)window.FindName("MainPreviewTabs")).SelectedItem=window.FindName(name);
        await Flush();
    }

    private static async Task CheckAdvancedCodecLayout(MainViewModel vm,MainWindow window,MediaItem item)
    {
        var original=Json.Clone(item.Options.Video);var width=window.Width;var height=window.Height;
        var scroll=(ScrollViewer)window.FindName("VideoSettingsScrollViewer");
        var groups=new[]{"VideoFormatSettings","VideoStructureSettings","VideoExecutionSettings"}.Select(n=>(Expander)window.FindName(n)).ToArray();
        FrameworkElement Field(string name)=>(FrameworkElement)window.FindName(name);
        async Task Layout()
        {window.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();}
        void Aligned(string left,string right)
        {
            var a=Field(left);var b=Field(right);Assert.True(a.IsVisible&&b.IsVisible);
            var offset=b.TranslatePoint(new Point(),a);
            Assert.True(offset.X>=a.ActualWidth+8,$"Overlapping fields: {left}, {right}");
            Assert.InRange(Math.Abs(offset.Y),0,.5);
        }
        try
        {
            foreach(var language in L.SupportedLanguages)
            foreach(var size in new[]{(1540d,1020d),(1120d,760d)})
            foreach(var encoder in new[]{"libx264","libx265","libsvtav1","hevc_nvenc"})
            {
                App.ApplyLanguage(language);window.Width=size.Item1;window.Height=size.Item2;
                item.Options.Video.Encoder=encoder;
                item.Options.Video.ThreadMode=item.Options.Video.CanControlThreads?ThreadControlMode.CPUAnteil:ThreadControlMode.Automatisch;await Flush();
                var saved=System.Text.Json.JsonSerializer.Serialize(item.Options,Json.Options);
                foreach(var group in encoder=="libx264"?groups:groups.Skip(2))
                {
                    group.SetCurrentValue(Expander.IsExpandedProperty,true);await Layout();
                    // Keep the header visible even when its complete contents are taller than the viewport.
                    scroll.ScrollToVerticalOffset(group.TranslatePoint(new Point(),Field("VideoSettingsColumns")).Y);await Layout();
                    var content=(FrameworkElement)group.Content;
                    var fields=Descendants<Control>(content).Where(c=>c.IsVisible&&c is TextBox or ComboBox or Button).ToArray();
                    Assert.NotEmpty(fields);
                    var bounds=fields.Select(c=>(Control:c,Bounds:c.TransformToAncestor(content).TransformBounds(new Rect(c.RenderSize)))).ToArray();
                    foreach(var field in bounds)
                    {
                        Assert.True(field.Bounds.Left>=-.5&&field.Bounds.Right<=content.ActualWidth+.5,$"Field clipped horizontally: {language}, {size}, {encoder}, {field.Control.Name}");
                        Assert.True(field.Bounds.Width>=30&&field.Bounds.Height>=24);
                    }
                    for(var a=0;a<bounds.Length;a++)for(var b=a+1;b<bounds.Length;b++)
                    {
                        if(bounds[a].Control.IsAncestorOf(bounds[b].Control)||bounds[b].Control.IsAncestorOf(bounds[a].Control))continue;
                        var intersection=Rect.Intersect(bounds[a].Bounds,bounds[b].Bounds);
                        Assert.True(intersection.IsEmpty||intersection.Width<1||intersection.Height<1,$"Overlapping controls: {language}, {encoder}, {bounds[a].Control.Name}, {bounds[b].Control.Name}");
                    }
                    if(group==groups[0])
                    {
                        Aligned("VideoPixelFormatSelector","VideoProfileSelector");
                        Assert.Equal(vm.CanSetTune,Field("VideoTuneSelector").IsVisible);
                    }
                    if(group==groups[1])
                    {
                        Aligned("VideoGopInput","VideoMaxRateInput");Aligned("VideoBFramesInput","VideoReferencesInput");Aligned("VideoBFramesInput","VideoBufferInput");
                        foreach(var name in new[]{"VideoMaxRateInput","VideoBufferInput","VideoBFramesInput","VideoReferencesInput"})
                            Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(Field(name))));
                    }
                    if(group==groups[2])
                    {
                        Assert.Equal(encoder=="hevc_nvenc",Field("NvencSettings").IsVisible);
                        Assert.Equal(encoder=="libsvtav1",Field("SvtAdvancedSettings").IsVisible);
                        var panel=Field(encoder=="libsvtav1"?"SvtAdvancedSettings":encoder=="hevc_nvenc"?"NvencSettings":"SoftwareThreadSettings");
                        Assert.True(panel.ActualWidth>content.ActualWidth-1);
                        Assert.True(Field("EncoderUtilityActions").TranslatePoint(new Point(),panel).Y>=panel.ActualHeight);
                        if(encoder=="libsvtav1")
                        {
                            Aligned("SvtParallelismSelector","SvtTuneSelector");Aligned("SvtFilmGrainInput","SvtDeblockingSelector");
                            Assert.True(Field("SvtParametersInput").ActualWidth>panel.ActualWidth-1);
                        }
                        else if(encoder!="hevc_nvenc")Aligned("ThreadModeSelector","ThreadPercentInput");
                        else Assert.InRange(Field("VideoGpuInput").TranslatePoint(new Point(),content).X,0,1);
                    }
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                        SaveSnapshot(window,Path.Combine(artifacts,$"{language}-{encoder}-{group.Name}-{size.Item1}.png"));
                    var last=bounds.MaxBy(f=>f.Bounds.Bottom).Control;last.BringIntoView();await Layout();
                    var lastBounds=last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
                    Assert.True(lastBounds.Top>=-.5&&lastBounds.Bottom<=scroll.ActualHeight+.5,$"Last field cannot be reached: {language}, {size}, {encoder}, {group.Name}");
                    if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} bottomArtifacts)
                        SaveSnapshot(window,Path.Combine(bottomArtifacts,$"{language}-{encoder}-{group.Name}-{size.Item1}-bottom.png"));
                    group.SetCurrentValue(Expander.IsExpandedProperty,false);
                }
                Assert.Equal(saved,System.Text.Json.JsonSerializer.Serialize(item.Options,Json.Options));
            }
        }
        finally
        {
            foreach(var group in groups)group.SetCurrentValue(Expander.IsExpandedProperty,false);
            vm.SelectedMedia=null;item.Options.Video=original;vm.SelectedMedia=item;
            window.Width=width;window.Height=height;App.ApplyLanguage("de");scroll.ScrollToTop();await Flush();
        }
    }

    private static async Task CheckSettingsWorkspace(MainViewModel vm,MainWindow window)
    {
        var tabs=(TabControl)window.FindName("MainPreviewTabs");
        Assert.Equal(new[]{"PreviewTab","EncodeSettingsTab","MediaInfoTab","LargeCommandTab","LogTab"},tabs.Items.Cast<TabItem>().Select(t=>t.Name));
        var settings=(TabControl)window.FindName("SettingsTabs");
        Assert.Equal(new[]{"VideoSettingsTab","AudioSettingsTab","SubtitleSettingsTab","OutputSettingsTab"},settings.Items.Cast<TabItem>().Select(t=>t.Name));
        var item=vm.SelectedMedia!;
        var command=vm.CommandText;var width=window.Width;var height=window.Height;
        foreach(var language in L.SupportedLanguages)
        {
            App.ApplyLanguage(language);
            var saved=System.Text.Json.JsonSerializer.Serialize(item.Options,Json.Options);
            foreach(var size in new[]{(1920d,1020d),(1540d,1020d),(1120d,760d)})
            {
                window.Width=size.Item1;window.Height=size.Item2;
                await SelectWorkspace(window,"EncodeSettingsTab");settings.SelectedItem=window.FindName("VideoSettingsTab");await Flush();
                Assert.Equal(L.T("Encodierung"),((TabItem)window.FindName("EncodeSettingsTab")).Header);
                Assert.True(settings.ActualHeight>140,$"Settings viewport is too short: {settings.ActualHeight}");
                var basic=(FrameworkElement)window.FindName("VideoBasicSettings");var advanced=(FrameworkElement)window.FindName("VideoAdvancedSettings");
                Assert.True(basic.IsVisible&&advanced.IsVisible);Assert.True(basic.ActualWidth>600&&advanced.ActualWidth>600);
                var scroll=(ScrollViewer)window.FindName("VideoSettingsScrollViewer");scroll.ScrollToTop();await Flush();
                var encoder=(FrameworkElement)window.FindName("VideoEncoderSelector");var mode=(FrameworkElement)window.FindName("VideoRateControlSelector");
                var quality=(FrameworkElement)window.FindName("VideoQualityInput");var speed=(FrameworkElement)window.FindName("VideoPresetSelector");
                Assert.True(mode.TranslatePoint(new Point(),encoder).X>encoder.ActualWidth);
                Assert.InRange(Math.Abs(mode.TranslatePoint(new Point(),encoder).Y),0,.5);
                Assert.True(speed.TranslatePoint(new Point(),quality).X>quality.ActualWidth);
                Assert.InRange(Math.Abs(speed.TranslatePoint(new Point(),quality).Y),0,.5);
                var sliderBottom=speed.TranslatePoint(new Point(0,speed.ActualHeight),scroll).Y;
                if(sliderBottom>scroll.ActualHeight&&Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} clippedArtifacts)
                    SaveSnapshot(window,Path.Combine(clippedArtifacts,"clipped-primary-controls.png"));
                Assert.True(sliderBottom<=scroll.ActualHeight,$"Primary sliders are clipped: {language}, {size}, slider bottom {sliderBottom}, viewport {scroll.ActualHeight}");
                Assert.True(advanced.TranslatePoint(new Point(),basic).Y>=basic.ActualHeight);
                foreach(var group in new[]{"VideoFormatSettings","VideoStructureSettings","VideoExecutionSettings"})Assert.False(((Expander)window.FindName(group)).IsExpanded);
                Assert.True(((FrameworkElement)window.FindName("MediaList")).IsVisible&&((FrameworkElement)window.FindName("QueueGrid")).IsVisible);
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} artifacts)
                    SaveSnapshot(window,Path.Combine(artifacts,language+"-encode-settings-"+size.Item1+".png"));
                settings.SelectedItem=window.FindName("OutputSettingsTab");await Flush();
                Assert.Equal(vm.OutputFileNamePreview,((TextBox)window.FindName("VideoOutputNamePreview")).Text);
                await SelectWorkspace(window,"PreviewTab");
                var image=(FrameworkElement)window.FindName("PreviewImageArea");
                Assert.True(image.IsVisible&&image.ActualHeight>0);
                Assert.Equal(16d/9,image.ActualWidth/image.ActualHeight,5);
                var previewControls=(FrameworkElement)window.FindName("PreviewControls");
                Assert.True(image.TranslatePoint(new Point(0,image.ActualHeight),previewControls).Y<=.5);
                Assert.True(((FrameworkElement)window.FindName("PreviewSettingsTabs")).IsVisible);
                var jump=Descendants<Button>(window).Single(b=>ButtonHints.GetKey(b)=="JumpCutStart");
                var cutList=Descendants<ListBox>(window).Single(b=>b.Name=="CutList");
                Assert.True(jump.TranslatePoint(new Point(0,jump.ActualHeight),cutList).Y<=0);
                var previewSettings=(FrameworkElement)window.FindName("PreviewSettingsTabs");
                Assert.True(jump.TranslatePoint(new Point(0,jump.ActualHeight),previewSettings).Y<=previewSettings.ActualHeight-12,$"Cut navigation is clipped: {language}, {size}");
                if(size.Item1>=1540)
                {
                    var position=(FrameworkElement)window.FindName("PreviewPositionInput");
                    var testEncode=(FrameworkElement)window.FindName("TestEncodeButton");
                    Assert.InRange(Math.Abs(testEncode.TranslatePoint(new Point(),position).Y),0,1);
                }
                if(Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_ARTIFACTS") is {Length:>0} previewArtifacts)
                    SaveSnapshot(window,Path.Combine(previewArtifacts,language+"-preview-"+size.Item1+".png"));
                Assert.False(basic.IsVisible);
                Assert.Equal(command,vm.CommandText);Assert.Same(item,vm.SelectedMedia);
                Assert.Equal(saved,System.Text.Json.JsonSerializer.Serialize(item.Options,Json.Options));
            }
        }
        window.Width=width;window.Height=height;App.ApplyLanguage("de");
        settings.SelectedItem=window.FindName("VideoSettingsTab");await SelectWorkspace(window,"EncodeSettingsTab");
    }

    private static void SaveSnapshot(FrameworkElement window,string path)
    {
        window.UpdateLayout();Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        image.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var file=File.Create(path);encoder.Save(file);
    }
    private static async Task Flush()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(220); // Include the production debounced validation timer.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static MediaItem Item(string path, string encoder, string preset, string pixelFormat)
    {
        var info = new MediaInfo { Path = Path.GetFullPath(path), Duration = 10, Streams =
            [new() { Index = 0, Type = "video", Codec = "h264", Width = 1920, Height = 1080 },
             new() { Index = 2, Type = "audio", Codec = "aac", Channels = 2 },
             new() { Index = 3, Type = "subtitle", Codec = "subrip" }] };
        var options = EncodingOptions.ForMedia(info);
        options.Video = new() { Encoder = encoder, Preset = preset, PixelFormat = pixelFormat };
        return new() { Info = info, Options = options };
    }
    private static Capabilities TestCapabilities() => new()
    {
        Filters = ["scale", "setsar","select","setpts","atrim","asetpts","aresample","apad","concat","asplit"],
        Encoders = new[] { "libx264", "libx265", "hevc_nvenc", "av1_nvenc", "libsvtav1", "libaom-av1", "librav1e", "libvvenc",
                "av1_vulkan", "h264_vaapi", "hevc_d3d12va", "av1_mf", "h264_v4l2m2m" }
            .Select(name => new EncoderInfo(name, name, "video", ScalingService.Family(name), name.EndsWith("nvenc"),
                PixelFormats: ["yuv420p", "yuv420p10le"], Options:
                [new(name == "libaom-av1" ? "cpu-used" : name == "librav1e" ? "speed" : "preset", "string", "", ["medium", "fast", "slow", "p1", "p2", "p3", "p4", "p5", "p6", "p7", "6"]),
                 new("crf", name is "libsvtav1" or "libaom-av1" ? "int" : "float", "", []), new("cq", "float", "", []), new("qp", "int", "", []),
                 ..(name=="libx265"?new[]{new EncoderOption("x265-params","dictionary","",[])}:Array.Empty<EncoderOption>())]))
            .Select(info=>info.Name=="libsvtav1"?CapabilityParser.WithHelp(info,
                "Supported pixel formats: yuv420p yuv420p10le\n  -preset <int> E..V....... Encoding preset (from -2 to 13) (default -2)\n  -crf <int> E..V....... CRF (from 0 to 63)\n  -qp <int> E..V....... QP (from 0 to 63)\n  -svtav1-params <dictionary> E..V....... SVT parameters\n"):info)
            .Concat(new[] { "libopus", "aac", "flac", "libmp3lame", "ac3", "eac3" }.Select(name => new EncoderInfo(name,name,"audio","",false)))
            .Concat(new[] { "srt", "ass", "mov_text" }.Select(name => new EncoderInfo(name,name,"subtitle","",false))).ToList()
    };
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

