# PixelPeeker

Native Windows 10/11 x64 video conversion built with WPF and .NET 10. The first usable release imports media, inspects it with ffprobe, detects encoders and filters in the installed FFmpeg build, configures each audio and subtitle stream, and runs real software or NVIDIA NVENC jobs. Its queue and settings persist in SQLite and JSON.

**Linux / Avalonia (in progress).** Phase 1 extracts the portable services into `net10.0` libraries (`FFmpegStudio.Core`, `FFmpegStudio.Infrastructure`, `FFmpegStudio.Shared`) and adds an Avalonia desktop project, `src/FFmpegStudio.Avalonia`. That shell builds on Linux and Windows and reuses `LibraryViewModel`: open a media file, show ffprobe details when FFmpeg is already available, and list the saved queue. Playback, the full editor and downloading FFmpeg on Linux are later phases. The WPF app remains the complete Windows product. On Linux, build the portable subset with `dotnet build FFmpegStudio.Linux.slnf` and run `dotnet run --project src/FFmpegStudio.Avalonia/FFmpegStudio.Avalonia.csproj`. The WPF project targets `net10.0-windows` and does not build on Linux.

Version 0.10.23 merges **Compare frames** and **Compare encodes** into one **Compare** button and maximized window. Select one completed job for source versus encode, or two jobs for A/B with an optional source reference. The unified viewer retains hold-Space reference preview, five sample frames, pixel zoom, synchronized panning and external files. Reference filters can match crop, scale and other spatial settings from encode A. Reopening preserves the comparison. All nine languages, remembered codec settings and accurate seeking remain available. See [CHANGELOG.md](CHANGELOG.md).

The interface, dialogs, selection labels, application messages and tooltips support German, English, Polish, French, Spanish, Turkish, Simplified Chinese, Russian and Japanese; German remains the default for existing installations. Windows number formats, saved enum values, preset identities and FFmpeg arguments remain independent of the interface language. FFmpeg's own help and process output keep their original language. Drag files or folders into the library; use **Add to queue** for the selected file, **Queue selected** for selected files, and **Start queue** to run saved jobs. At app startup PixelPeeker checks for FFmpeg. Without a managed or local FFmpeg installation, install the latest Gyan Git Master Full build in **Manage FFmpeg** or choose an existing ffmpeg.exe and matching ffprobe.exe. Each managed update is downloaded into a new version directory, checked against the SHA-256 supplied for that exact build, safely extracted, and run before activation. Previous versions remain available for rollback.

## Build

Install the .NET 10 SDK and Windows Desktop targeting pack, then run Build.ps1. This builds the Visual Studio solution and runs the unit tests. Run Publish.ps1 to build a self-contained x64 Windows release in artifacts/portable-win-x64-<version> and update artifacts/portable-win-x64 with the same release. Publishing may need to download the Windows runtime pack the first time.

Publishing runs the tests by default. Use `Publish.ps1 -SkipTests` only when the same source has already passed its relevant tests in a separate run.

Open FFmpegStudio.sln in Visual Studio. The Windows executable built from source is src/FFmpegStudio.Desktop/bin/Release/net10.0-windows/PixelPeeker.exe. The Avalonia shell is src/FFmpegStudio.Avalonia/bin/Release/net10.0/PixelPeeker.Avalonia.

## What works

- Import multiple files or folders, recursive import, Unicode paths, thumbnails, ffprobe JSON details, duplicate detection and corrupt-file reporting.
- Available AVC, HEVC, VVC and AV1 software encoders plus NVIDIA NVENC, Intel Quick Sync and AMD AMF from the installed FFmpeg build. The VVC implementation is identified as libvvenc; each GPU encoder is probed before a queue job begins.
- Per-job quality, bitrate, CQ/QP, rate mode, preset, tune, profile, level, VBV, GOP, B-frames, references, pixel format, GPU index, CUDA decode and CUDA scaling. Expert options come from the selected encoder's own ffmpeg help output and are checked before use.
- Aspect-preserving bounded scaling with even dimensions and optional upscaling; crop, pad, Lanczos, bicubic, bilinear, spline, area or nearest scaling; deinterlacing, frame rate, rotation, flips, HDR tonemapping, BT.709 conversion and custom video filters. Black-bar detection suggests a crop and never applies it by itself.
- Individual audio copy, conversion or removal, with encoder- and channel-dependent bitrate choices, lossless FLAC, and independently editable algorithmic quality. Subtitle copy, compatible format conversion, removal, text or bitmap burn-in, flags and separate subtitle export are supported; bitmap-to-text conversion is disabled because OCR is not included.
- Persistent and reorderable jobs, per-job option snapshots, process cancellation, progress, speed, FPS, remaining time, saved logs, safe non-overwriting output, output probing and size statistics.
- Cached previews, test encodes, a preloaded instant A/B viewer, synchronized side-by-side comparison, VMAF/SSIM/PSNR when present in the actual build, stored results and CSV export.
- JSON presets, multiple folder-watch rules, completion notifications and an NVIDIA monitor backed by actual Windows and NVIDIA sensor readings.

See docs/STATUS.md for verified support and the current limits of this build.

## Data and safety

### Compare two encodes

Click **Compare** beside the queue. One selected completed job opens source versus encode, with a reference matching its image filters and output geometry. Ctrl-select two jobs to open A/B with an optional untouched source. Use **Compare two encodes** to switch modes, **Match reference to encode A** to choose a filtered reference, and **File A / File B** for existing files. The source timeline maps cuts and test clips automatically. Hold **Space** to view the loaded reference and release it to restore the previous view. Generate five sample frames, select a thumbnail, inspect actual source/file timestamps, and use **100/200/400%**, synchronized panning and instant A/B switching. [The comparison guide](docs/ENCODE-COMPARISON.md) explains timing, reference filters and memory bounds.

### Frame preview and navigation

Use **Play / Pause / Stop**, the volume slider and **Mute** to view the original file with audio. During playback the timeline and **−10 s / +10 s** seek quickly using keyframes. Pause and Stop hold the current position and load an exact source frame before allowing cut markers and frame steps. Stop releases the playback decoder. While the timeline has focus, **Space** toggles play/pause and **Left / Right** seek ten seconds during playback or step one frame when paused. Playback includes sections marked for removal and applies no encoding filters. Selecting an original/filtered still or a cut boundary returns to exact frame mode. The portable EXE contains the playback engine and extracts it on first launch; no separate VLC installation is required.

When playback is paused, the preview loads automatically after an 80 ms pause while moving the timeline or editing its time field. New requests cancel obsolete waits; only the latest request for the selected file may update the picture. Loading activity and errors appear beside the timeline without blocking imports or the encoding queue.

Use **← / →** below the picture, or press **Left / Right** while the timeline has focus, to move one source frame. Navigation reads actual frame timestamps with ffprobe and selects the exact source timestamp when decoding the image with FFmpeg. It does not estimate steps from the average frame rate, so variable frame rates and nonzero start timestamps are supported. The position snaps to the confirmed loaded frame. Moving past either end keeps the boundary frame. Timestamp windows are cached; a persistent decoder preloads neighbouring images into RAM (24 frames / 96 MiB maximum), with direct pixel transfer and no PNG round trip. Large jumps or changed filters restart decoding near a preceding keyframe. The decoder closes after 20 idle seconds, on source changes, on playback, and on shutdown. Existing buffered frames remain usable after idle closure. Free filter chains retain isolated image extraction; the full resolution encode comparison uses its separate image cache.

**Original frame** and **Filtered preview** select the mode used for subsequent time changes. The filtered view applies the configured visual filters, while export frame-rate conversion is omitted so navigation stays on the source timeline. The preview is scaled to at most 1280 pixels wide; use test encodes and frame comparison to inspect encoder quality. An arbitrary custom filter may alter frame content, so displayed source time identifies the input frame before that filter.

[The editing guide](docs/FRAME-ACCURATE-EDITING.md) explains how to mark several sections for removal and export them with synchronized audio, subtitles and source/output time mapping.

### Output file names

Open **Einstellungen → Dateinamen** (Settings → File names), enable **Use these naming choices for new queue jobs**, and select the details to append. The original file name stays first; the output container supplies the extension. Both the settings dialog and **Encode settings → Output** show the resulting name. With global naming disabled, the individual pattern under Output continues to apply. Global naming applies to all new queue jobs, including watched-folder and test encodes, and overrides their individual patterns. Saving does not rename existing queue jobs or output files.

Quality uses the active CRF, CQ or QP parameter. Target bitrate appears only in bitrate/CBR modes and is the configured video target, not a measurement of the finished file. Date and time use local time when the job is added to the queue and remain fixed across later starts or restarts. Output resolution includes the configured geometry and padding; FPS uses the selected output rate or the average source rate reported by ffprobe. Missing optional values are omitted. The speed preset is distinct from the saved application preset name.

Individual patterns also support `{name}`, `{width}`, `{height}`, `{resolution}`, `{codec}`, `{encoder}`, `{quality}`, `{bitrate}`, `{speed}`, `{preset}`, `{date}`, `{time}` and `{fps}`. A global example is `Film_2026-10-18_1920x1080_AV1_CRF24_preset-4.mkv`. Names already present on disk or reserved by another queue job receive `_2`, `_3`, etc.; existing files are never overwritten.

### Button help

Hover over an action button to read what it does and any prerequisites. Help covers the library, previews, presets, video/subtitle controls, queue, quality analysis, command views, FFmpeg management and dialogs. Disabled actions also offer help. Text follows the interface language. In **Einstellungen → Allgemein** (Settings → General), **Tooltips anzeigen** enables or disables all tooltips, including existing field hints. Set the delay from 0 to 30 seconds; the default is 2 seconds. The delay applies to each new hint. **Save** applies and persists these preferences; **Cancel** keeps the previous settings.

### Color themes

Open **Einstellungen → Darstellung** (Settings → Appearance) and select a palette card. Studio Mint, Midnight, Northern Lights, Amethyst, Ember, Forest and Sakura are dark themes; Daylight, Sandstone and Glacier are light themes. The application and open dialogs preview the selection immediately. **Save** persists it for the next launch; **Cancel** or closing the dialog restores your previous theme. Changing colors does not affect media, encoder options or commands. Existing dark preferences become Studio Mint and existing light preferences become Daylight. Palette names and descriptions follow the selected interface language.

### GOP and SVT-AV1

GOP is the maximum keyframe interval in frames: for example, 240 frames at 24 fps is 10 seconds. Leave it at 0 for the encoder default. For libsvtav1, expand **SVT-AV1 · Advanced settings** to choose tune, film-grain (0–50), and enable-dlf (0–2), or enter the full colon-separated parameter list. Explicit zeros are preserved. A GOP of 240 with `tune=0:film-grain=0:enable-dlf=2` produces `-g 240 -svtav1-params tune=0:film-grain=0:enable-dlf=2`. The controls edit the same dictionary used by Expert options, so the command contains only one SVT parameter argument. Known ranges and duplicate parameters are validated; other options must be supported by your installed SVT library. Do not combine GOP with a separate `keyint` parameter. See the [official SVT-AV1 parameter reference](https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/master/Docs/Parameters.md) for individual option behavior and version requirements.

### Threads, efficiency and parallel jobs

For software encoders, expand **Video → CPU threads** and choose automatic threading, a fixed count, or a percentage of the machine's logical CPU cores. The percentage becomes a thread budget per job (rounded down, at least one), not a CPU utilization cap. x264/x264rgb, OpenH264, AOM-AV1, rav1e and VVenC use `-threads:v:0`; AOM also enables `row-mt` when available unless you have set it explicitly. x265 uses `x265-params pools=N` for worker threads, preserving other expert parameters; frame and helper threads can be additional. Conflicting expert thread settings are reported instead of silently overwritten. Refer to the [FFmpeg encoder options](https://ffmpeg.org/ffmpeg-codecs.html) and [x265 threading documentation](https://x265.readthedocs.io/en/master/threading.html).

SVT-AV1 uses **SVT-AV1 · Advanced settings → parallelism level (lp)**. In the tested SVT 4.2 library, empty/0 chooses a level based on the CPU; 1–6 controls the amount of pipeline parallelism and memory. These levels are not thread counts or CPU affinity limits. General fixed-count/core-share controls are therefore not offered for this encoder. The `lp` control stays synchronized with the full SVT parameter string and expert options.

**Efficiency mode**, enabled by default, applies Windows EcoQoS and idle process priority immediately after each encode process starts, before the normal start callback. PixelPeeker reads back the applied state and saves it in the job details and log. If Windows cannot apply everything, the actual available state is shown and encoding continues. The checkbox beside the queue and its Settings counterpart affect new starts, including test and hardware encodes. Efficiency mode can reduce throughput; see [Microsoft's process power-throttling API](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation).

Choose **Parallel jobs** (1–16) and **NVIDIA jobs** (1–8) beside the queue. Ctrl/Shift-select rows and choose **Start selected** to request only those jobs, or choose **Start queue** for all waiting jobs. Each encoder keeps its own saved CPU settings. Changes to the limits are saved immediately; lowering a limit allows running jobs to finish and restricts subsequent starts. Jobs not selected remain waiting unless the whole queue has also been started.

### Quality analysis

Select a successfully completed queue job and click **Qualität analysieren**. PixelPeeker reads frame timestamps, then compares the source and encoded output in a separate pass for each available metric: VMAF, SSIM and PSNR. Both videos must be decoded; analysis can take considerable time at high resolutions. It creates measurement files rather than a newly encoded video.

The job detail area shows the current phase, an activity indicator during preparation or waiting, and actual processed time/percent, speed and estimated time remaining when available. Percentages and remaining time refer to the named current pass, not all passes together. **Analyse abbrechen** cancels that job's running or waiting analysis. Other application operations and the encoding queue remain available. Automatic analyses use the same status display, and the queue marks the job as **Analyse läuft**.

After a manual analysis, the result window opens and measurements are saved for **Messwerte**. Errors and cancellation appear directly in the job area; previous complete measurements remain available. A new session does not restore old progress indicators as running tasks.

### Storage

User data is stored in %LOCALAPPDATA%\FFmpegStudio by default. Set FFMPEG_STUDIO_DATA to select another data folder. Managed FFmpeg versions, presets, logs, a PNG cache and studio.db are created there. Settings and presets are JSON; the queue, analyses and watched-file fingerprints are stored in SQLite.

FFmpeg receives each argument via ProcessStartInfo.ArgumentList without a command shell. It writes with -n to a unique incomplete file in the destination directory. A successful exit and a successful ffprobe check of the requested video, audio and subtitle streams are required before that file is renamed to the final output. Cancellation terminates the process tree. Source files are not used as an output target.

## FFmpeg source and licence

The managed Windows download source is [Gyan's FFmpeg Windows builds](https://www.gyan.dev/ffmpeg/builds/). The application uses the Git Master Full .7z archive and its matching .ver and .sha256 text files. These endpoints and the live metadata format were verified while developing this version. Gyan says its static builds are **GPLv3**. The build's ffmpeg version, build configuration, commit id, pinned archive URL and checksum are retained in the managed install directory. The application does not bundle FFmpeg binaries in its source or publish output; the user can install, update or select FFmpeg separately. A Gyan build and every library it contains carry their respective licence obligations. See [Gyan's build and library notes](https://www.gyan.dev/ffmpeg/builds/#about-these-builds) and [FFmpeg's legal FAQ](https://ffmpeg.org/legal.html) before redistributing a build.

### Linux

On Linux x64 and arm64 the Avalonia shell downloads a static GPL FFmpeg build. The Windows gyan.dev downloader is unchanged. The application still does not ship FFmpeg binaries; it only links to the publisher's files, checks the published checksum, and stores the source URL with the install.

Primary source: [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) GitHub releases. The app reads `https://api.github.com/repos/BtbN/FFmpeg-Builds/releases?per_page=8`, chooses the newest tag named `autobuild-*` that contains `checksums.sha256` and a static GPL asset, and downloads that immutable asset. The rolling `latest` tag is not used. Asset names, verified on 2026-10-26:

- x64: `ffmpeg-N-<build>-g<commit>-linux64-gpl.tar.xz`
- arm64: `ffmpeg-N-<build>-g<commit>-linuxarm64-gpl.tar.xz`

Shared (`*-gpl-shared.tar.xz`) and LGPL assets are not selected. The `gpl` variant is a static executable build (libx264, libx265 and the other GPL dependencies), licensed **GPL-3.0-or-later**. BtbN's Linux builds still need glibc 2.28 or newer and Linux 4.18 or newer; they are not musl/Alpine builds. Corresponding source is the FFmpeg commit embedded in the asset name, published by the [FFmpeg project](https://ffmpeg.org/download.html#get-sources), together with BtbN's build scripts in the same repository. The checksum file in that release is `checksums.sha256` (SHA-256, two spaces, then the file name).

Fallback, used when the BtbN release list or checksum file cannot be read: [John Van Sickle's static builds](https://johnvansickle.com/ffmpeg/). Those pages state that the builds are licensed under the **GNU GPL version 3**. URLs verified on 2026-10-26:

- x64: `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz` and the sibling `.md5`
- arm64: `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz` and the sibling `.md5`

John publishes MD5 sums, not SHA-256. The fallback checks that MD5 before extraction. These release builds are older than the BtbN autobuilds (the release files were last updated in August 2024). They target Linux kernels 3.2 and newer and are still glibc builds, not musl builds. Corresponding source is the FFmpeg release named by `ffmpeg -version`, from the [FFmpeg source archives](https://ffmpeg.org/download.html#get-sources).

Both publishers allow downloading the builds from those public URLs. PixelPeeker does not redistribute the binaries. A user who already has ffmpeg and ffprobe can choose the pair on `PATH` instead of a managed install. xz archives are decompressed and then extracted with the same path checks used for the Windows 7z archives. `studio-install.json` in the version folder records the version id, checksum, algorithm, source URL and `GPL-3.0-or-later`.

Linux playback does not bundle VLC. The Avalonia shell loads the system `libvlc` (for example the distro `libvlc5` and `vlc-plugin-base` packages). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The app references CommunityToolkit.Mvvm (MIT), Microsoft.Extensions.DependencyInjection (MIT), Microsoft.Data.Sqlite (MIT), SharpCompress (MIT) and SQLitePCLRaw (Apache 2.0; SQLite engine code is public domain). The portable publish includes the applicable runtime licenses and third-party notices in `licenses/`; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). xUnit (Apache 2.0) is a development and test dependency and is not part of the desktop app. Restored direct and transitive dependencies are listed by NuGet with dotnet list package --include-transitive.

## Tests

dotnet test FFmpegStudio.sln -c Release exercises parsing, scaling, stream mapping, bitrate and quality rules, capability detection, update metadata, download integrity, archive path safety, queue recovery and preset remapping. The integration runner creates media and exercises actual encoding, sensors, a GPU and the installed FFmpeg build; see tools/FFmpegStudio.Verify.

The solution also contains native WPF regression tests in tests/FFmpegStudio.Desktop.Tests. These run real controls on an STA dispatcher and exercise encoder changes, presets, switching media, audio and subtitle selectors, capability refresh, custom audio quality, invalid scaling, themes and shutdown. They use isolated temporary settings. Optionally set FFMPEG_STUDIO_TEST_DATA to an existing test-data folder containing a managed FFmpeg installation to exercise every encoder discovered in that actual build as well.
