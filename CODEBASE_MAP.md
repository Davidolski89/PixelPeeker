# PixelPeeker codebase map

Navigation guide for small changes. Start with the feature table, then read only the relevant implementation and tests.
Paths are relative to this project root. Source projects/namespaces retain `FFmpegStudio`; the desktop executable is `PixelPeeker.exe`.
Verified against source on 2026-10-26 (0.10.39, Avalonia editor features and Linux libc gate). Update locations when structure changes, not after every UI tweak.

## 1. Architecture overview

- **Core** (`src/FFmpegStudio.Core`): observable media/options/job models, argument generation and validation, timing/geometry rules, presentation catalogs. Targets plain `net10.0`; uses CommunityToolkit.Mvvm.
- **Infrastructure** (`src/FFmpegStudio.Infrastructure`): FFmpeg processes, media probing, frame extraction, queue scheduling, installation, storage and automation. References Core and also targets plain `net10.0`. Windows-only behavior sits behind `IProcessControl`, `IHardwareMonitor` and `IFmpegDistribution` in `PlatformServices.cs`.
- **Shared** (`src/FFmpegStudio.Shared`): UI-agnostic composition (`StudioServices.AddPixelPeeker`), `LibraryViewModel`, and `EditorViewModel` (encoders, filters, cuts, presets, queue, command preview, settings transfer, test encodes, quality analysis, comparison session, subtitle export). Encoder list updates, cut merging, command preview, settings copy and analysis text are shared helpers the WPF view model calls. References Infrastructure. Both shells reference this project.
- **Desktop** (`src/FFmpegStudio.Desktop`): Windows x64 WPF application using MVVM, XAML plus code-built controls/dialogs. References Shared (and Infrastructure directly). `SourcePlaybackService` implements `IPlaybackSession` with bundled LibVLC. File dialogs and confirmations stay in this project. `App.CreateServices` adds WPF-only services on top of `AddPixelPeeker`.
- **Avalonia** (`src/FFmpegStudio.Avalonia`): cross-platform `net10.0` desktop shell bound to `EditorViewModel`. Playback uses LibVLCSharp against the system libvlc (`AvaloniaPlaybackSession`, `VlcVideoView`). Linux FFmpeg installation uses `LinuxFfmpegDistribution`, which refuses musl and glibc older than 2.28. Settings, expert options, comparison, analysis and the test-encode notice are code-built dialogs in `StudioDialogs.cs`. File dialogs stay in this project.
- **Entry/composition:** `src/FFmpegStudio.Desktop/App.xaml` loads shared resources; `App.xaml.cs` implements `App.OnStartup`, singleton DI registration in `CreateServices`, language/theme setup and startup error handling. The Avalonia entry is `src/FFmpegStudio.Avalonia/Program.cs` and `App.axaml.cs`.
- **UI flow:** `MainWindow` binds to `MainViewModel`; its partial files separate preview, playback, cuts and codec memory. `MediaItem`, also in `MainViewModel.cs`, owns a source's editable `EncodingOptions`.
- **Commands/updates:** CommunityToolkit `[RelayCommand]` and `[ObservableProperty]` generate members. Edit their declarations, not generated files. Option notifications refresh validation, choices and command previews.
- **Service flow:** view-model commands call concrete services; `QueueService` job events, `LoggingService.Written` and playback events feed the UI. `MainViewModel.Ui` marshals service notifications onto the WPF dispatcher.
- **Comparison:** `MainViewModel.CompareEncodes` owns a separate `EncodeComparisonViewModel`; a reusable maximized window displays it. Queue jobs carry cloned options and their selected toolchain.

## 2. Directory and file structure

| Location | Responsibility |
| --- | --- |
| `FFmpegStudio.sln` | Core, Infrastructure, Shared, Desktop, Avalonia, test projects and the Verify console tool. |
| `FFmpegStudio.Linux.slnf` | Portable subset that builds on Linux: Core, Infrastructure, Shared, Avalonia, their tests and Verify. Excludes the WPF projects. |
| `Directory.Build.props`, `global.json` | Shared version/compiler settings and SDK selection; package/project references live in each project's `.csproj`. |
| `src/FFmpegStudio.Core/Models.cs` | Central DTOs, enums, `VideoOptions`, `FilterOptions`, track options, `EncodingOptions`, `EncodingJob`, `AppSettings`. |
| `src/FFmpegStudio.Core/Localization` | Embedded UI translations and button-help catalogs. |
| `src/FFmpegStudio.Infrastructure` | Runtime services; several files contain multiple related service classes (see feature table). `PlatformServices.cs` selects Windows or portable process control. |
| `src/FFmpegStudio.Shared` | `EditorViewModel`, `EncoderChoices`, `CutEditing`, `EncodeCommandPreview`, `EditorFeatures` (settings, test encode, analysis text), `EncodeComparisonSession`, `IPlaybackSession`, `LibraryViewModel`, `StudioServices`. |
| `src/FFmpegStudio.Desktop` | Main window, view-model partials, dialogs, custom controls, playback and published-app smoke helpers. |
| `src/FFmpegStudio.Avalonia` | Avalonia editor window, system-libvlc playback, settings/comparison/analysis dialogs and startup. Binds to `EditorViewModel`. |
| `src/FFmpegStudio.Desktop/Themes/Studio.xaml` | Shared WPF styles/templates and dynamic brush resources. |
| `src/FFmpegStudio.Desktop/Assets` | App icon/logo and image-generation prompt; see [branding notes](docs/BRANDING.md). |
| `tests/FFmpegStudio.Tests`, `tests/FFmpegStudio.Desktop.Tests`, `tests/FFmpegStudio.Avalonia.Tests` | Core/service tests, native WPF interaction regression test, and a headless Avalonia window test. |
| `tools/FFmpegStudio.Verify` | Console entry point and real-FFmpeg integration suite. |
| `Build.ps1`, `Publish.ps1` | Test/build entry points and portable publishing; **both run tests by default**. Publish supports `-SkipTests` after a separate successful run against the same source. |
| `.github/workflows/ci.yml` | Pull-request and `main` CI. Windows builds `FFmpegStudio.sln` and runs its test projects. Ubuntu builds `FFmpegStudio.Linux.slnf`, runs those tests, and smoke-starts Avalonia. Does not publish. |
| `docs` | [Editing](docs/FRAME-ACCURATE-EDITING.md), [comparison](docs/ENCODE-COMPARISON.md), [languages](docs/LANGUAGES.md), [architecture](docs/ARCHITECTURE.md), [verification history](docs/STATUS.md). |

Skip `artifacts`, `.test-data`, `.vs`, `bin` and `obj` during routine code navigation; these contain outputs, fixtures or generated state.
Use targeted searches, e.g. `rg -n 'RefreshEncoderChoices' src/FFmpegStudio.Desktop/MainViewModel.cs`.
Older prose/screenshots can describe superseded UI; verify current names and locations in source.

## 3. Feature-to-code navigation map

Primary paths below are the first files to open; related class names refer to their owners elsewhere in this table.

| Feature or task | Primary source files/classes | Related components |
| --- | --- | --- |
| Main window, workspace tabs, library/queue layout | `src/FFmpegStudio.Desktop/MainWindow.xaml`; `MainWindow.xaml.cs` in the same directory | `MainViewModel`; code-behind handles multi-selection, drag/drop, keyboard input, comparison opening and `PreviewLayout_SizeChanged` (16:9 preview). |
| Library, file/folder import, thumbnails, media info | `src/FFmpegStudio.Desktop/MainViewModel.cs`: `MediaItem`, `ImportPathsAsync`; Avalonia import is `EditorViewModel.ImportPathsAsync` | `MediaProbeService`, `PreviewService`; `EncodingOptions.ForMedia`; drop handlers. Avalonia stores a 320px PNG path on `EditorMedia.ThumbnailPath` after a successful probe. |
| FFmpeg/ffprobe calls and capability discovery | `src/FFmpegStudio.Infrastructure/MediaServices.cs`: `MediaProbeService`, `FFmpegCapabilityService`, `HardwareDetectionService`; `src/FFmpegStudio.Infrastructure/ProcessRunner.cs` | `src/FFmpegStudio.Core/Parsers.cs`: `MediaProbeParser`, `CapabilityParser`, `ProgressParser`. |
| Encoder choices, rate control, quality/speed sliders | `src/FFmpegStudio.Desktop/MainWindow.xaml`: `VideoEncoderSelector`, `VideoQualityInput`, `VideoPresetSelector`; `src/FFmpegStudio.Desktop/MainViewModel.cs`: `RefreshEncoderChoices`, `RefreshVideoLabels` | `src/FFmpegStudio.Core/VideoPresentation.cs`, `VideoQualityScale.cs`, `VideoEncoderPolicy.cs`; `src/FFmpegStudio.Desktop/QualitySlider.cs`, `PresetSlider.cs`, `SelectionValueConverter.cs`. |
| Advanced encoder settings, SVT parameters, CPU threading | `src/FFmpegStudio.Core/SvtAv1Settings.cs`, `src/FFmpegStudio.Core/SoftwareThreading.cs`; `src/FFmpegStudio.Desktop/Dialogs.cs`: `EncoderOptionsWindow` | `MainWindow.xaml`: `VideoFormatSettings`, `VideoStructureSettings`, `VideoExecutionSettings` beneath the primary controls; `SoftwareThreadSettings`, `SvtAdvancedSettings` and `NvencSettings` are full-width sections with paired fields inside the execution group, followed by `EncoderUtilityActions`. `VideoOptions` in `Models.cs`, `FFmpegCommandBuilder`. |
| Audio and subtitle configuration | `src/FFmpegStudio.Desktop/MainWindow.xaml`: audio/subtitle tabs; `src/FFmpegStudio.Core/Models.cs`: `AudioTrackOptions`, `SubtitleTrackOptions` | `AudioEncoding` in `src/FFmpegStudio.Core/ScalingService.cs`; command builder stream mapping; `SubtitleService` in `src/FFmpegStudio.Infrastructure/ComparisonServices.cs` exports subtitles. |
| Filters, crop, scaling, color conversion | `src/FFmpegStudio.Core/FFmpegCommandBuilder.cs`: `FilterService`; `src/FFmpegStudio.Core/ScalingService.cs`; `src/FFmpegStudio.Desktop/MainWindow.xaml`: `FiltersSettingsTab` | `FilterOptions`; `PreviewService.DetectCropAsync` in `MediaServices.cs`; preview/filter and comparison reference paths. |
| FFmpeg command generation, validation, displayed command | `src/FFmpegStudio.Core/FFmpegCommandBuilder.cs`: `Build`, `Validate`, `Display`; `src/FFmpegStudio.Desktop/MainViewModel.cs`: `RefreshCommandPreview` | `src/FFmpegStudio.Desktop/CommandPreviewControl.xaml` and `.xaml.cs`; `QueueService` records `EncodingJob.ExecutedCommand` after process start. |
| Queue scheduling, parallel limits, job execution/cancellation | `src/FFmpegStudio.Infrastructure/QueueService.cs`: `AddAsync`, `SchedulerAsync`, `RunOneAsync`, `StartAll`, `Start`, `StopAll` | `EncodingJob`, `QueueStore`, `ProcessRunner`, `ProgressParser`; `MainViewModel` queue commands and `MainWindow.xaml` queue controls. |
| Windows efficiency mode | `src/FFmpegStudio.Infrastructure/WindowsEfficiencyMode.cs`; `src/FFmpegStudio.Infrastructure/PlatformServices.cs`: `IProcessControl`, `WindowsProcessControl`, `PortableProcessControl`; `ProcessRunner.cs` | `QueueService` supplies preference and records applied scheduling; `AppSettings.EfficiencyMode`, `MainViewModel.EncodeEfficiencyMode`. Non-Windows hosts record the request and do not call EcoQoS. |
| Output naming, collisions, test encodes and notice | `src/FFmpegStudio.Core/FileNaming.cs`; `src/FFmpegStudio.Infrastructure/QueueService.cs`: `SuggestedOutputPath`, `AddAsync`; `src/FFmpegStudio.Shared/EditorFeatures.cs`: `TestEncodePlan`, `TestEncodeNotice` | WPF `FileNamingControl` and `TestEncodeNotificationWindow` (status text calls `TestEncodeNotice`). Avalonia settings edit the same `FileNamingSettings` flags; `StudioDialogs.TestEncodeNoticeAsync` shows the notice. |
| Accurate still preview, frame steps, loading overlay | `src/FFmpegStudio.Desktop/MainViewModel.Preview.cs`: `RequestPreviewAsync`; `src/FFmpegStudio.Infrastructure/FrameNavigationService.cs`, `AccuratePreviewService.cs` | `src/FFmpegStudio.Core/FrameNavigation.cs`; `PreviewService` PNG/custom-filter fallback; `MainWindow.xaml`, `src/FFmpegStudio.Desktop/LoadingSpinner.xaml`. |
| Playback, fast seeking, pause to exact frame | `IPlaybackSession` in `src/FFmpegStudio.Shared/PlaybackSession.cs`; Windows `src/FFmpegStudio.Desktop/SourcePlaybackService.cs`; Avalonia `src/FFmpegStudio.Avalonia/AvaloniaPlaybackSession.cs` and `VlcVideoView.cs` | WPF keeps LibVLCSharp.WPF, the bundled Windows libvlc, and `AccuratePreviewService` for the paused frame. Avalonia uses LibVLCSharp 3.10.2 (not LibVLCSharp.Avalonia) and the system libvlc. After pause it resolves the source frame with `FrameNavigationService` and extracts that PTS through `PreviewService`. |
| Cut editor, excluded ranges, synchronized cut export | `src/FFmpegStudio.Desktop/MainViewModel.Cuts.cs`, `CutEditorControl.cs`, `CutTimelineStrip.cs`; `src/FFmpegStudio.Core/CutTimeline.cs` | `src/FFmpegStudio.Infrastructure/CutPreparationService.cs`; `src/FFmpegStudio.Core/SubtitleCuts.cs`; command builder and comparison time mapping. |
| Peek: source/encode or two-encode comparison | WPF `src/FFmpegStudio.Desktop/EncodeComparisonViewModel.cs` and `EncodeComparisonControl`; Avalonia `EncodeComparisonSession` plus `StudioDialogs.ShowComparisonAsync` | Both call `EncodeComparisonService`. The WPF window keeps the sample strip, availability strip and focus pane. |
| Peek zoom, stable image sizes, samples, cut availability | `src/FFmpegStudio.Desktop/ComparisonImagePane.cs`, `ComparisonAvailabilityStrip.cs`, `EncodeComparisonViewModel.Samples.cs` | `src/FFmpegStudio.Core/ComparisonTimeline.cs`, `CutTimeline`; selection/source-position preservation lives in `EncodeComparisonViewModel`. |
| Quality analysis, progress, measurements, CSV/results | `QualityAnalysisService` in `ComparisonServices.cs`; shared `QualityAnalysisRun` and `QualityReport`; WPF `AnalyzeJobSafelyAsync` and `AnalysisWindow` | Avalonia `EditorViewModel.AnalyzeQualityAsync` shows the same measurements, a polyline and CSV/JSON export. `ResultsWindow` stays in the WPF shell. |
| Saved application presets | `src/FFmpegStudio.Infrastructure/PresetService.cs`: `BuiltIns`, `Apply`, import/export; `src/FFmpegStudio.Desktop/MainViewModel.cs`: preset commands | `Preset` in `Models.cs`; `JsonEditor`/`TextDialog` in `Dialogs.cs`. Application presets differ from encoder speed presets. |
| Last-used settings per codec | `src/FFmpegStudio.Core/CodecMemory.cs`; `src/FFmpegStudio.Desktop/MainViewModel.Codecs.cs` | `MainViewModel.OptionsChanging`, `OptionsChanged`, `RestoreEncoderOptions`; `AppSettings.Codecs`, `SettingsService`. |
| Global preferences and settings dialog | `AppSettings`; WPF `SettingsWindow`; Avalonia `StudioDialogs.EditSettingsAsync`; shared `SettingsTransfer.Apply` | `SettingsService` in `Storage.cs`. Both shells copy language, theme, queue limits, file-name flags, watch rules and the test-encode notice through `SettingsTransfer`. |
| Themes and shared UI styles | `src/FFmpegStudio.Core/ThemeCatalog.cs`; `src/FFmpegStudio.Desktop/Themes/Studio.xaml`; WPF `App.ApplyTheme`; Avalonia `App.ApplyTheme` | WPF `ThemePickerControl` shows swatches. Avalonia settings list the same `ThemeCatalog` entries and preview the choice before save. |
| Localization and tooltips | `src/FFmpegStudio.Core/L.cs`, `Localization/translations.json`, `ButtonHelpCatalog.cs`, `Localization/button-help.json`; `src/FFmpegStudio.Desktop/Localization.cs`, `ButtonHints.cs` | `App.ApplyLanguage`; `MainViewModel.RefreshLanguage`; `Dialogs.cs` contains translated code-built UI. |
| FFmpeg installation, updates, local tools, rollback | `FFmpegManagement.cs`: `FFmpegVersionService`, `FFmpegDownloadService`; `LinuxFfmpeg.cs` and `LinuxLibc.cs` | Windows managed download stays on gyan.dev. Linux x64/arm64 uses a pinned BtbN autobuild static GPL `.tar.xz` plus `checksums.sha256`, with John Van Sickle static GPL builds and their `.md5` files as fallback, only when glibc is 2.28 or newer. musl and older glibc show package-manager guidance and keep the PATH/manual pair. `studio-install.json` records version, hash, source URL and license. |
| Database, settings files and application data paths | `src/FFmpegStudio.Infrastructure/Storage.cs`: `AppPaths`, `AtomicFile`, `SettingsService`, `QueueStore` | `src/FFmpegStudio.Core/AppIdentity.cs`; JSON conventions/models in `Models.cs`; presets use `PresetService`. |
| Logs, failure display, unexpected exceptions | `src/FFmpegStudio.Infrastructure/Storage.cs`: `LoggingService`; `src/FFmpegStudio.Desktop/MainViewModel.cs`: `RunUiAsync`; `src/FFmpegStudio.Desktop/App.xaml.cs`: dispatcher exception handler | `ProcessResult.EnsureSuccess`, `QueueService.RunOneAsync`, per-job `LogPath`/`Error`; preview and analysis have separate cancellation/error handling. |
| Folder watching, metrics, completion actions | `src/FFmpegStudio.Infrastructure/AutomationServices.cs`: `FolderWatchService`, `HardwareMonitorService` (`IHardwareMonitor`); `src/FFmpegStudio.Desktop/MainViewModel.cs`: `MonitorLoopAsync`, `CompleteBatchAsync` | `WatchRule`, `AppSettings`; `CountdownWindow` in `Dialogs.cs`; queue batch-completed event. Windows reads kernel32 and `nvidia-smi.exe`. Linux reads `/proc/stat`, `/proc/meminfo` and `nvidia-smi` when those sources exist. |

## 4. Common modification guide

| If you want to change… | Start by inspecting… |
| --- | --- |
| A button, spacing or tab placement | `src/FFmpegStudio.Desktop/MainWindow.xaml`; follow its binding to `MainViewModel` or event to `MainWindow.xaml.cs`. Shared colors/templates belong in `Themes/Studio.xaml` under Desktop. |
| A dialog or cut/file-naming control | `src/FFmpegStudio.Desktop/Dialogs.cs`, `CutEditorControl.cs` or `FileNamingControl.cs`; these build much of their UI in C#, not XAML. |
| An encoder parameter/default | `src/FFmpegStudio.Core/Models.cs`, then `FFmpegCommandBuilder.cs` (`Build` **and** `Validate`); check codec memory and capability-derived choices in the feature table. |
| Codec labels, quality bounds or speed choices | `src/FFmpegStudio.Core/VideoPresentation.cs`, `VideoQualityScale.cs`; Desktop `RefreshEncoderChoices`, `QualitySlider`/`PresetSlider`. Raw FFmpeg values must stay separate from labels. |
| A global setting | `AppSettings` in `src/FFmpegStudio.Core/Models.cs` → `SettingsWindow` in Desktop `Dialogs.cs` → `MainViewModel.EditSettingsAsync` → `SettingsService.LoadAsync` normalization, if needed. |
| Preview debounce, stale frames or frame stepping | `src/FFmpegStudio.Desktop/MainViewModel.Preview.cs`; then `FrameNavigationService` for timestamp choice or `AccuratePreviewService` for decoding/cache. |
| Playback seeking or pause/stop behavior | `src/FFmpegStudio.Desktop/MainViewModel.Playback.cs` → `SourcePlaybackService.cs`; preserve the handoff to accurate preview. |
| Actual FFmpeg arguments or filters | `src/FFmpegStudio.Core/FFmpegCommandBuilder.cs`; `FilterService` is in that same file. Check the displayed command and queued snapshot paths too. |
| Queue start/stop, retries or parallelism | `src/FFmpegStudio.Infrastructure/QueueService.cs`; inspect Desktop commands only if the wrong selection/action reaches the service. |
| A localized string or tooltip | `src/FFmpegStudio.Core/Localization/translations.json` or `button-help.json`; find its source text, `L.ResourceKey`/XAML key or `ButtonHints.Key` before editing. |
| Saved codec preferences or new-file defaults | `src/FFmpegStudio.Core/CodecMemory.cs`, `src/FFmpegStudio.Desktop/MainViewModel.Codecs.cs`; global JSON loading is in Infrastructure `Storage.cs`. |
| Peek selection, source time or frame size | `src/FFmpegStudio.Desktop/EncodeComparisonViewModel.cs`, then `EncodeComparisonControl.xaml` / `ComparisonImagePane.cs`; timing/coverage rules are in Core `ComparisonTimeline.cs` and `CutTimeline.cs`. |

## 5. Important dependencies and constraints

- **Supported platform/dependencies:** the full editor is still WPF/.NET 10 x64. Core, Infrastructure, Shared and Avalonia target plain `net10.0`. Packages: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Data.Sqlite, SharpCompress, Avalonia 12, and LibVLCSharp.WPF/VideoLAN.LibVLC.Windows (WPF only). Check project files for exact versions; [third-party notices](THIRD-PARTY-NOTICES.md) cover redistribution.
- **Two video paths:** LibVLC plays the original input with audio; FFmpeg/ffprobe provide exact stills, encoding and metrics. Do not treat playback seeking as frame confirmation or apply export cuts to source playback. See [editing](docs/FRAME-ACCURATE-EDITING.md).
- **Frame identity:** navigation uses source PTS/time bases, not `time × average FPS`. `MainViewModel.Preview.cs` gates frame steps/cut marks while loading or transitioning; cancelled/older generations must not replace the current image.
- **Cut boundaries:** `CutRegion.Start` is the first removed frame; `End` is the first retained frame (`null` means EOF). Keep video, audio, text subtitles and source/output mappings consistent across export and comparison.
- **Peek state:** preserve position when changing queue encodes from the same source; unknown external-file coverage is not proof of missing video. Label changes must not resize comparison image areas. See [comparison](docs/ENCODE-COMPARISON.md).
- **Options vs presentation:** translated labels must not replace persisted enum names, encoder tokens or preset values. Sliders and selection refreshes must not silently clamp/overwrite saved options. Preserve `adjustingEncoder`/`applyingPreset` guards and codec-memory transitions.
- **Parameter ownership:** SVT controls and expert options share `VideoOptions.Extra` through `SvtAv1Settings`; avoid duplicate/conflicting arguments. CPU budgets are encoder-specific (`SoftwareThreading`); SVT `lp` is a parallelism level, not a thread count.
- **Geometry:** `FilterService`/`ScalingService` decide whether scaling or SAR normalization is needed. Identity geometry should not add a scale filter; still preview, comparison reference and export reuse these rules with deliberate differences.
- **Stream identity:** retain source stream indices when editing tracks; output mapping uses contiguous output ordinals. `PresetService.Apply` remaps to new media and preserves that media's cuts; saved/exported presets omit cut ranges.
- **Settings acceptance:** `SettingsWindow` edits a clone; `EditSettingsAsync` copies accepted fields explicitly. A new field needs that save path as well as its model/control. Cancel must retain prior preferences and revert previewed appearance.
- **Queue snapshots:** `QueueService.AddAsync` clones options and reserves output names; jobs retain their FFmpeg/ffprobe paths. UI edits, global naming changes and FFmpeg updates must not silently rewrite queued jobs.
- **Output integrity:** arguments go through `ProcessStartInfo.ArgumentList`, not a shell. Encodes use `-n`, a unique partial file, successful exit and output/stream probing before a non-overwriting rename. Keep cancellation/process-tree cleanup and collision checks.
- **Persistence compatibility:** `AppIdentity` deliberately retains `%LOCALAPPDATA%/FFmpegStudio`, `FFMPEG_STUDIO_DATA` and the legacy mutex identity. `Storage.cs` owns JSON normalization/atomic writes and SQLite schema/recovery; running jobs restore as interrupted.
- **Localization:** German source strings are keys; translation arrays are ordered EN/PL/FR/ES/TR/ZH/RU/JA. XAML `Loc.*` keys derive from `L.ResourceKey`. Preserve placeholders, all nine languages and Windows numeric-entry culture. See [languages](docs/LANGUAGES.md).
- **Lifecycle/progress:** keep dispatcher marshalling, cancellation and awaited shutdown of preview/playback/comparison/queue services. Quality-analysis progress belongs to its current metric pass; failure/cancellation must not erase earlier complete measurements.
- **Installation:** `FFmpegDownloadService` pins archive/hash to one build, validates extraction paths and activates only verified binaries. Old versions remain usable by existing jobs; local installations are validated in place. `SupportsManagedInstall` is true only on Windows. Calling `CheckAsync` or `InstallAsync` elsewhere throws `PlatformNotSupportedException` before any download.

## 6. Testing navigation

Choose checks by changed behavior; commands below are references, not a requirement for documentation-only edits.
Core/service tests live in `tests/FFmpegStudio.Tests`; native UI coverage lives in `tests/FFmpegStudio.Desktop.Tests/EditorTests.cs`. The headless Avalonia check lives in `tests/FFmpegStudio.Avalonia.Tests`.

| Changed area | Tests to inspect/run |
| --- | --- |
| Arguments, validation, scaling, stream mapping, capability parsing, installer safety | `CoreTests.cs` in `tests/FFmpegStudio.Tests`. Includes queue recovery and preset remapping. |
| Encoder names/preset tokens, quality labels | `VideoPresentationTests.cs`, `LocalizationTests.cs`, relevant `CoreTests.cs`; native `CheckVideoInputs`, `CheckPresetEncoders`, `CheckSvtPresets`, `CheckQualitySlider`. |
| Threads, efficiency, parallel starts | `ExecutionTests.cs`; native `CheckThreadControls`. Real queue coverage requires FFmpeg fixtures. |
| Codec memory and persistence | `CodecMemoryTests.cs`; native `CheckCodecMemory`; `CoreTests.cs` recovery/serialization cases. |
| Output file names and collisions | `FileNamingTests.cs`; native `CheckFileNaming`. |
| Exact seeking and decoder caching | `FrameNavigationTests.cs`, `AccuratePreviewTests.cs`; native `CheckFramePreview`. |
| Cut semantics, audio/subtitle retiming, cut exports | `CutTests.cs`; native `CheckFramePreview`; comparison tests for timeline consequences. |
| Peek timing/coverage, real encoded frames | `EncodeComparisonTests.cs`; native `CheckEncodeComparison` for modes, layout, position retention and interaction. |
| Playback and pause/stop handoff | Native `CheckPlayback`; `src/FFmpegStudio.Desktop/App.PlaybackSmoke.cs` and `App.SeekingSmoke.cs` contain published-app checks. |
| UI layout, localization, themes and tooltips | `LocalizationTests.cs`, `ThemeTests.cs`, `TooltipTests.cs`; native `CheckSettingsWorkspace`, `CheckAdvancedCodecLayout`, `CheckLanguages`, `CheckTooltips`. |
| Test-encode notification | Native focus `test-notification-only`; implementation/check helper in `src/FFmpegStudio.Desktop/App.TestEncodeNoticeSmoke.cs`. |
| Branding/data-path compatibility | `BrandingTests.cs`; native startup assertions. |
| Quality metrics and end-to-end FFmpeg execution | `tools/FFmpegStudio.Verify/IntegrationSuite.cs`; `CoreTests.cs` analysis-state checks and native analysis coverage. |

Targeted example: `dotnet test tests/FFmpegStudio.Tests/FFmpegStudio.Tests.csproj -c Release --filter FullyQualifiedName~FileNamingTests`.
Native UI entry: `dotnet test tests/FFmpegStudio.Desktop.Tests/FFmpegStudio.Desktop.Tests.csproj -c Release` (Windows/STA dispatcher).
Linux subset: `dotnet test FFmpegStudio.Linux.slnf -c Release` (skips the WPF projects). Avalonia headless: `dotnet test tests/FFmpegStudio.Avalonia.Tests/FFmpegStudio.Avalonia.Tests.csproj -c Release`.

- `FFMPEG_STUDIO_TEST_FOCUS` selects branches of the single native test: `layout-only`, `encoder-controls`, `languages-only`, `comparison-only`, `playback-only`, `test-notification-only`.
- `preview`, `codecs`, `playback` are early-return checkpoints after preceding checks; they are not isolated test cases. Inspect the branch near the top of `EditorTests.cs` before choosing a focus.
- Set `FFMPEG_STUDIO_TEST_DATA` to a test data root with a valid managed FFmpeg installation (`versions.json`); many real-media tests **return without exercising FFmpeg when it is absent**. A passing count alone does not prove integration coverage.
- `FFMPEG_STUDIO_TEST_ARTIFACTS` selects saved screenshots/reports. Use isolated data; do not point mutable smoke runs at the user's normal settings/queue.
- `tools/FFmpegStudio.Verify/Program.cs` accepts `install`, `inspect` or `integration` plus a data folder. `install` downloads; `integration` generates media and executes real encodes/metrics. Hardware results depend on the machine/build.
- Published-app entry is `App.OnStartup` with `--smoke-test <output-folder> [source-file]`; optional `--seek`, `--playback`, `--cuts`, `--compare`, `--test-notice` exercise additional paths (`--idle-seek` extends `--seek`). Use an isolated `FFMPEG_STUDIO_DATA` root with toolchain configuration.
- `src/FFmpegStudio.Desktop/App.EncodeComparisonSmoke.cs` holds published comparison checks; other smoke helpers are listed above. Historical evidence is indexed in [STATUS](docs/STATUS.md); do not treat it as a substitute for checking changed behavior.
- For a broad implementation change, `dotnet test FFmpegStudio.sln -c Release` runs the test projects included in the solution, including the WPF project that builds only on Windows. `Publish.ps1` also runs tests by default before packaging; use `-SkipTests` only after a successful separate run against the same source. Avoid publishing when builds/tests are explicitly out of scope.
- CI is `.github/workflows/ci.yml`. It does not set `FFMPEG_STUDIO_TEST_DATA` or `PIXELPEEKER_LIVE_FFMPEG`, and it does not run `tools/FFmpegStudio.Verify`.

## 7. Avalonia / Linux migration

The WPF app remains the full Windows product. The Avalonia shell edits, queues, plays, compares, analyzes and installs FFmpeg when the host libc can run the static builds.

| Area | Where it lives now | Still Windows-only |
| --- | --- | --- |
| Models, parsing, commands, presets, scaling, cuts, localization | `FFmpegStudio.Core` | — |
| Probe, queue, SQLite settings, presets, preview extraction, comparison services | `FFmpegStudio.Infrastructure` | — |
| Efficiency mode and process-tree job objects | `IProcessControl`; Windows implementation in `WindowsEfficiencyMode` / `WindowsProcessJob` | EcoQoS and job objects |
| CPU, RAM, GPU readings | `IHardwareMonitor` | kernel32 sensors; Linux uses `/proc` and `nvidia-smi` when present |
| Managed FFmpeg download | `IFmpegDistribution` | gyan.dev on Windows. Linux: `LinuxFfmpegDistribution` plus `LinuxLibc` (glibc ≥ 2.28; no musl download) |
| Editor, settings, comparison, analysis, playback | `EditorViewModel` and `FFmpegStudio.Avalonia` | WPF still owns the sample strip, focus comparison pane, chart axes, tooltip catalog and completion countdown |
| Full editor, dialogs, themes in the WPF resource dictionary, LibVLC playback | `FFmpegStudio.Desktop` | WPF, `net10.0-windows`, LibVLCSharp.WPF |

Do not publish a Linux build from this branch. WPF remains the Windows shell; Avalonia is the cross-platform shell on the same services.
