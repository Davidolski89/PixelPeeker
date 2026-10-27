using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class EditorFeatureTests
{
    [Fact]
    public void Test_encode_plan_clips_the_preview_window()
    {
        var options = new EncodingOptions { Container = "mp4", FileNamePattern = "{name}" };
        var planned = TestEncodePlan.Create(options, 12.5, 2, 20, Path.Combine("cache-root"));
        Assert.Equal(12.5, planned.ClipStart);
        Assert.Equal(2, planned.ClipDuration);
        Assert.Equal("{name}_Test_{codec}", planned.FileNamePattern);
        Assert.Equal(Path.Combine("cache-root", "test-encodes"), planned.OutputDirectory);
        Assert.Equal("{name}", options.FileNamePattern);
        Assert.Equal(2, TestEncodePlan.Create(options, 18, 10, 20, "cache-root").ClipDuration);
        Assert.Throws<ArgumentException>(() => TestEncodePlan.Create(options, 0, 0, 10, "cache-root"));
    }

    [Fact]
    public void Expert_options_skip_values_the_simple_editor_already_sets()
    {
        var encoder = new EncoderInfo("libx264", "", "video", "", false, Options:
        [
            new("preset", "enum", "", ["fast"]),
            new("crf", "int", "", []),
            new("aq-mode", "int", "adaptive quant", ["0", "1"])
        ]);
        Assert.Equal("aq-mode", Assert.Single(EncoderOptionEditor.Editable(encoder)).Name);
    }

    [Fact]
    public void Settings_transfer_clamps_ranges_and_resolves_the_theme()
    {
        var target = new AppSettings();
        var edited = new AppSettings
        {
            ParallelJobs = 99,
            ParallelGpuJobs = 0,
            CacheLimitMiB = 1,
            UpdateIntervalHours = 0,
            Language = "en-US",
            ThemeId = ThemeCatalog.LightId,
            TooltipDelaySeconds = 80,
            ShowTestEncodeNotification = false,
            FileNaming = new FileNamingSettings { Enabled = true, IncludeCodec = false }
        };
        SettingsTransfer.Apply(target, edited);
        Assert.Equal(16, target.ParallelJobs);
        Assert.Equal(1, target.ParallelGpuJobs);
        Assert.Equal(32, target.CacheLimitMiB);
        Assert.Equal(1, target.UpdateIntervalHours);
        Assert.Equal("en", target.Language);
        Assert.Equal(ThemeCatalog.LightId, target.ThemeId);
        Assert.False(target.DarkMode);
        Assert.Equal(TooltipTiming.MaximumSeconds, target.TooltipDelaySeconds);
        Assert.False(target.ShowTestEncodeNotification);
        Assert.True(target.FileNaming.Enabled);
        Assert.False(target.FileNaming.IncludeCodec);
        edited.FileNaming.IncludeCodec = true;
        Assert.False(target.FileNaming.IncludeCodec);
    }

    [Fact]
    public void Notices_exports_and_reports_use_stable_text()
    {
        Assert.Contains("Queue", TestEncodeNotice.StatusMessage(new EncodingJob { Status = JobStatus.Wartend }));
        Assert.Contains("encodiert", TestEncodeNotice.StatusMessage(new EncodingJob { Status = JobStatus.Läuft }));
        var media = new MediaInfo { Path = Path.Combine("videos", "clip.mkv") };
        Assert.Equal("clip_eng.srt", SubtitleExport.SuggestedName(media, new SubtitleTrackOptions { Language = "eng" }));
        Assert.Equal("clip_eng.mks", SubtitleExport.SuggestedName(media, new SubtitleTrackOptions { Language = "eng", IsBitmap = true }));
        var csv = QualityReport.Csv(new QualityResult(90, 0.98, 40, "", [new QualityFrame(1.5, 90, null, 40)], ""));
        Assert.StartsWith("seconds;vmaf;ssim;psnr", csv);
        Assert.Contains("1.5;90;;40", csv);
        var described = QualityAnalysisRun.Describe(new QualityAnalysisProgress("messen", 10, 1, 4, 2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9)));
        Assert.Contains("10.0", described);
        Assert.Contains("00:00:03", described);
    }

    [Fact]
    public void Comparison_choice_names_a_crf_encode()
    {
        var output = new MediaInfo { Path = Path.Combine("out", "a.mkv"), Streams = [new() { Type = "video", Codec = "hevc", Width = 1280, Height = 720 }] };
        var job = new EncodingJob
        {
            Status = JobStatus.Abgeschlossen,
            OutputPath = output.Path,
            Source = new MediaInfo { Path = Path.Combine("in", "a.mkv") },
            OutputInfo = output,
            Options = new EncodingOptions { Video = new VideoOptions { Encoder = "libx265", Quality = 23, Preset = "medium", RateControl = RateControl.Qualität } }
        };
        var choice = new EncodeComparisonChoice(EncodeComparisonInput.FromJob(job));
        Assert.Contains("CRF23", choice.Label);
        Assert.Contains("libx265", choice.Label);
        Assert.Contains("a.mkv", choice.ToString());
    }

    [Fact]
    public async Task Editor_settings_command_clamps_and_switches_language()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable(AppIdentity.DataEnvironmentVariable);
        var language = L.Language;
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
            vm.EditSettingsRequested = draft =>
            {
                draft.ParallelJobs = 40;
                draft.Language = "en";
                draft.ShowTestEncodeNotification = false;
                draft.ThemeId = ThemeCatalog.LightId;
                return Task.FromResult<AppSettings?>(draft);
            };
            await vm.EditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(16, vm.Settings.ParallelJobs);
            Assert.Equal("en", vm.Settings.Language);
            Assert.Equal("en", L.Language);
            Assert.False(vm.Settings.ShowTestEncodeNotification);
            Assert.Equal(ThemeCatalog.LightId, vm.Settings.ThemeId);
            Assert.False(vm.Settings.DarkMode);
            Assert.Equal("Settings", vm.SettingsLabel);
        }
        finally
        {
            if (provider is not null) await provider.DisposeAsync();
            L.SetLanguage(language);
            Environment.SetEnvironmentVariable(AppIdentity.DataEnvironmentVariable, previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Comparison_samples_use_the_source_clock_and_known_coverage()
    {
        Assert.Equal(new[] { 0.1, 0.3, 0.5, 0.7, 0.9 }, ComparisonSamples.Fractions);
        Assert.Equal(10, ComparisonSamples.Position(100, 0.1, null, 0));
        var clipped = new EncodingOptions { ClipStart = 5 };
        Assert.Equal(15, ComparisonSamples.Position(100, 0.1, clipped, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ComparisonSamples.Position(0, 0.1, null, 0));
        var media = new MediaInfo { Path = Path.GetFullPath("clip.mkv"), Duration = 20, Streams = [new() { Type = "video", Width = 320, Height = 180 }] };
        var choice = new EncodeComparisonChoice(new EncodeComparisonInput(media, new Toolchain("test", "ffmpeg", "ffprobe", false)));
        var coverage = ComparisonSamples.Coverage(choice, 0);
        Assert.NotEmpty(coverage);
        Assert.Equal(ComparisonAvailability.Unknown, coverage[0].Availability);
    }

    [Fact]
    public void Quality_chart_sorts_the_weakest_frame_and_spaces_axes()
    {
        var frames = new[]
        {
            new QualityFrame(2, 90, 0.9, 40),
            new QualityFrame(1, 10, 0.2, 20)
        };
        Assert.Equal(1, QualityChart.Sorted(frames, "VMAF")[0].Seconds);
        Assert.Equal(2, QualityChart.Sorted(frames, "PSNR")[1].Seconds);
        var axis = QualityChart.Axis(0, 50, 2);
        Assert.Equal(3, axis.Count);
        Assert.Equal(0, axis[0]);
        Assert.Equal(25, axis[1]);
        Assert.Equal(50, axis[2]);
        Assert.Empty(QualityChart.Axis(double.NaN, 1));
    }

    [Fact]
    public void File_name_preview_uses_the_selected_clip()
    {
        var media = new MediaInfo
        {
            Path = Path.GetFullPath("holiday clip.mkv"),
            Streams = [new() { Type = "video", Codec = "h264", Width = 1280, Height = 720 }]
        };
        var options = EncodingOptions.ForMedia(media);
        options.Video.Encoder = "libx264";
        options.Container = "mkv";
        var naming = new FileNamingSettings { Enabled = true, IncludeResolution = true, IncludeCodec = false };
        var preview = FileNamePreview.Of(media, options, naming, new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("holiday clip_1280x720", preview);
        Assert.EndsWith(".mkv", preview);
        Assert.Equal(L.T("Eine Videodatei auswählen."), FileNamePreview.Of(null, options, naming));
    }

    [Fact]
    public void Session_power_prefers_systemctl_and_does_nothing_when_both_tools_are_missing()
    {
        var poweroff = SessionPower.Resolve(CompletionAction.Herunterfahren, name => name == "systemctl");
        Assert.Equal("systemctl", poweroff!.File);
        Assert.Equal("poweroff", Assert.Single(poweroff.Arguments));
        var suspend = SessionPower.Resolve(CompletionAction.Energiesparen, name => name == "loginctl");
        Assert.Equal("loginctl", suspend!.File);
        Assert.Equal("suspend", Assert.Single(suspend.Arguments));
        Assert.Null(SessionPower.Resolve(CompletionAction.Herunterfahren, _ => false));
        Assert.Null(SessionPower.Resolve(CompletionAction.Nichts, _ => true));
        var language = L.Language;
        try
        {
            L.SetLanguage("de");
            Assert.Contains("Countdown", SessionPower.Prompt(CompletionAction.Energiesparen));
            L.SetLanguage("en");
            Assert.Contains("countdown", SessionPower.Prompt(CompletionAction.Herunterfahren));
        }
        finally { L.SetLanguage(language); }
    }

    [Fact]
    public void Hardware_text_and_result_rows_describe_one_completed_source()
    {
        var text = HardwareReadout.Format(new SensorSnapshot(12.5, null, []));
        Assert.Contains("12.5", text);
        Assert.Contains("NVIDIA", text);
        var source = new MediaInfo { Path = Path.GetFullPath("clip.mkv"), Streams = [new() { Type = "video", Width = 640, Height = 360 }] };
        var done = new EncodingJob
        {
            Status = JobStatus.Abgeschlossen,
            Source = source,
            OutputBytes = 2 * 1048576,
            Speed = "1.5x",
            StartedAt = DateTimeOffset.Parse("2026-10-26T00:00:00Z"),
            EndedAt = DateTimeOffset.Parse("2026-10-26T00:00:10Z"),
            OutputInfo = new MediaInfo { BitRate = 800000, Streams = [new() { Type = "video", Width = 640, Height = 360 }] },
            Options = new EncodingOptions { Video = new VideoOptions { Encoder = "libx264", Preset = "fast" }, ClipStart = 1, ClipDuration = 4 },
            Analysis = new QualityResult(88, 0.9, 41, "", [], "")
        };
        var waiting = new EncodingJob { Status = JobStatus.Wartend, Source = source };
        Assert.Equal(source.Path, Assert.Single(EncodingResults.Sources([done, waiting])));
        var row = Assert.Single(EncodingResults.Rows([done, waiting], source.Path));
        Assert.Equal("libx264", row.Encoder);
        Assert.Equal("2.00", row.MiB);
        Assert.Equal("10.0", row.Seconds);
        Assert.Equal("800", row.Bitrate);
        Assert.Contains("88", row.Vmaf);
        Assert.DoesNotContain(TrackAction.Einbrennen, AudioTrackEditing.Actions);
        Assert.Contains("libopus", AudioTrackEditing.Encoders);
    }
}
