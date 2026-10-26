using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FFmpegStudio.Core;

public enum TrackAction { Kopieren, Encodieren, Entfernen, Einbrennen }
public enum JobStatus { Wartend, Läuft, Abgeschlossen, Fehlgeschlagen, Abgebrochen, Unterbrochen, Zurückgestellt }
public enum RateControl { Qualität, Bitrate, CBR, QP }
public enum ThreadControlMode { Automatisch, FesteAnzahl, CPUAnteil }
public enum ScaleMode { Original, UHD8K, UHD4K, QHD1440, FullHD1080, HD720, SD480, Prozent, Benutzerdefiniert }
public enum CompletionAction { Nichts, Benachrichtigung, Signalton, AnwendungSchließen, Energiesparen, Herunterfahren }

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

public sealed record MediaStream
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public string Codec { get; init; } = "unbekannt";
    public string? Profile { get; init; }
    public int? Level { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? SampleAspectRatio { get; init; }
    public string? DisplayAspectRatio { get; init; }
    public string? AverageFrameRate { get; init; }
    public string? RealFrameRate { get; init; }
    public string? TimeBase { get; init; }
    public double? FrameRate => Rational(AverageFrameRate);
    public string FrameRateMode => AverageFrameRate is not null && RealFrameRate is not null && AverageFrameRate != RealFrameRate
        ? L.T("VFR möglich (keine vollständige Frameanalyse)") : L.T("unbekannt (keine vollständige Frameanalyse)");
    public long? BitRate { get; init; }
    public string? PixelFormat { get; init; }
    public int? BitDepth { get; init; }
    public string? ColorSpace { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? ColorTransfer { get; init; }
    public string? ColorRange { get; init; }
    public double Rotation { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public bool Default { get; init; }
    public bool Forced { get; init; }
    public bool AttachedPicture { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public string SideDataJson { get; init; } = "[]";
    public string Language => Tags.GetValueOrDefault("language", "und");
    public string Title => Tags.GetValueOrDefault("title", "");
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";
    public bool IsBitmapSubtitle => Codec is "hdmv_pgs_subtitle" or "dvd_subtitle" or "dvb_subtitle" or "xsub";
    public string Summary => Type == "video" ? $"{Width} × {Height} · {Codec} · {(FrameRate?.ToString("0.###", CultureInfo.CurrentCulture) ?? "?")} FPS"
        : $"#{Index} · {Language} · {Codec} · {(Type == "audio" ? L.F($"{Channels?.ToString() ?? "?"} Kanäle · {SampleRate?.ToString() ?? "?"} Hz") : IsBitmapSubtitle ? L.T("Bilduntertitel") : L.T("Textuntertitel"))} · {Title}";
    public static double? Rational(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('/', ':');
        if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var n)) return null;
        if (parts.Length == 1) return double.IsFinite(n) ? n : null;
        return double.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && d != 0 && double.IsFinite(n / d) ? n / d : null;
    }
}

public sealed record MediaInfo
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public long Size { get; init; }
    public string Container { get; init; } = "unbekannt";
    public double? Duration { get; init; }
    public long? BitRate { get; init; }
    public double StartTime { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public string ChaptersJson { get; init; } = "[]";
    public List<MediaStream> Streams { get; init; } = [];
    public string RawJson { get; init; } = "{}";
    public MediaStream? Video => Streams.FirstOrDefault(s => s.Type == "video" && !s.AttachedPicture);
    public string Summary => $"{Video?.Summary ?? L.T("Kein Video")} · {(Duration.HasValue ? TimeSpan.FromSeconds(Duration.Value).ToString(@"hh\:mm\:ss") : L.T("Dauer unbekannt"))} · {Size / 1048576d:0.0} MiB";
}

public sealed partial class VideoOptions : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSvtAv1), nameof(CanControlThreads), nameof(CanSetThreadCount), nameof(CanSetThreadPercent))] private string encoder = "libx265";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEditQuality),nameof(CanEditBitrate))] private RateControl rateControl = RateControl.Qualität;
    [ObservableProperty] private double quality = 23;
    [ObservableProperty] private int bitRateKbps = 4000;
    [ObservableProperty] private int maxRateKbps;
    [ObservableProperty] private int bufferKbps;
    [ObservableProperty] private string preset = "medium";
    [ObservableProperty] private string tune = "";
    [ObservableProperty] private string profile = "";
    [ObservableProperty] private string level = "";
    [ObservableProperty] private string pixelFormat = "";
    [ObservableProperty] private int gop;
    [ObservableProperty] private int bFrames = -1;
    [ObservableProperty] private int references;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSetThreadCount), nameof(CanSetThreadPercent))] private ThreadControlMode threadMode;
    [ObservableProperty] private int threadCount = 4;
    [ObservableProperty] private int threadPercent = 50;
    [ObservableProperty] private int gpuIndex;
    [ObservableProperty] private bool cudaDecode;
    [ObservableProperty] private bool cudaScale;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SvtAv1Parameters), nameof(SvtTune), nameof(SvtFilmGrain), nameof(SvtDeblocking), nameof(SvtParallelism))]
    private Dictionary<string, string> extra = [];
    [JsonIgnore] public bool IsSvtAv1 => Encoder == "libsvtav1";
    // These editors share the expert dictionary, so presets and older jobs have one source of truth.
    [JsonIgnore] public string SvtAv1Parameters
    {
        get => Extra.GetValueOrDefault("svtav1-params", "");
        set
        {
            value = value?.Trim() ?? "";
            if (value == SvtAv1Parameters) return;
            if (value.Length == 0) Extra.Remove("svtav1-params"); else Extra["svtav1-params"] = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(SvtTune));
            OnPropertyChanged(nameof(SvtFilmGrain)); OnPropertyChanged(nameof(SvtDeblocking));
            OnPropertyChanged(nameof(SvtParallelism));
        }
    }
    [JsonIgnore] public string SvtTune { get => SvtAv1Settings.Get(SvtAv1Parameters, "tune"); set { if (value is not null) SvtAv1Parameters = SvtAv1Settings.Set(SvtAv1Parameters, "tune", value); } }
    [JsonIgnore] public string SvtFilmGrain { get => SvtAv1Settings.Get(SvtAv1Parameters, "film-grain"); set => SvtAv1Parameters = SvtAv1Settings.Set(SvtAv1Parameters, "film-grain", value); }
    [JsonIgnore] public string SvtDeblocking { get => SvtAv1Settings.Get(SvtAv1Parameters, "enable-dlf"); set { if (value is not null) SvtAv1Parameters = SvtAv1Settings.Set(SvtAv1Parameters, "enable-dlf", value); } }
    [JsonIgnore] public string SvtParallelism { get => SvtAv1Settings.Get(SvtAv1Parameters, "lp"); set { if (value is not null) SvtAv1Parameters = SvtAv1Settings.Set(SvtAv1Parameters, "lp", value); } }
    [JsonIgnore] public bool CanControlThreads => SoftwareThreading.SupportsCount(Encoder);
    [JsonIgnore] public bool CanSetThreadCount => CanControlThreads && ThreadMode == ThreadControlMode.FesteAnzahl;
    [JsonIgnore] public bool CanSetThreadPercent => CanControlThreads && ThreadMode == ThreadControlMode.CPUAnteil;
    public bool IsNvenc => Encoder?.EndsWith("_nvenc", StringComparison.Ordinal)==true;
    [JsonIgnore] public bool CanEditQuality => RateControl is RateControl.Qualität or RateControl.QP;
    [JsonIgnore] public bool CanEditBitrate => RateControl is RateControl.Bitrate or RateControl.CBR;
}

public sealed partial class FilterOptions : ObservableObject
{
    [ObservableProperty] private ScaleMode scaleMode;
    [ObservableProperty] private int width = 1920;
    [ObservableProperty] private int height = 1080;
    [ObservableProperty] private double percent = 50;
    [ObservableProperty] private bool allowUpscale;
    [ObservableProperty] private string algorithm = "lanczos";
    [ObservableProperty] private bool cropEnabled;
    [ObservableProperty] private int cropLeft;
    [ObservableProperty] private int cropRight;
    [ObservableProperty] private int cropTop;
    [ObservableProperty] private int cropBottom;
    [ObservableProperty] private bool padEnabled;
    [ObservableProperty] private int padWidth = 1920;
    [ObservableProperty] private int padHeight = 1080;
    [ObservableProperty] private bool deinterlace;
    [ObservableProperty] private double frameRate;
    [ObservableProperty] private int rotation;
    [ObservableProperty] private bool flipHorizontal;
    [ObservableProperty] private bool flipVertical;
    [ObservableProperty] private bool toneMap;
    [ObservableProperty] private string toneMapAlgorithm = "hable";
    [ObservableProperty] private string colorSpace = "";
    [ObservableProperty] private string customChain = "";
}

public sealed partial class AudioTrackOptions : ObservableObject
{
    public int InputIndex { get; set; }
    public string SourceCodec { get; set; } = "";
    public string SourceSummary { get; set; } = "";
    public int SourceChannels { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEncode), nameof(CanSetBitrate))] private TrackAction action = TrackAction.Kopieren;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BitrateChoices), nameof(QualityDescription), nameof(CanSetBitrate))] private string encoder = "libopus";
    [ObservableProperty] private int bitRateKbps = 192;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BitrateChoices))] private int channels;
    [ObservableProperty] private int sampleRate;
    [ObservableProperty] private string channelLayout = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(QualityDescription))] private int? compressionLevel;
    [ObservableProperty] private string bitrateMode = "vbr";
    [ObservableProperty] private string application = "audio";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string language = "und";
    [ObservableProperty] private bool isDefault;
    public bool CanEncode => Action == TrackAction.Encodieren;
    public bool CanSetBitrate => CanEncode && Encoder != "flac";
    public int[] BitrateChoices => AudioEncoding.Bitrates(Encoder, Channels > 0 ? Channels : SourceChannels);
    public string QualityDescription => AudioEncoding.QualityDescription(Encoder, CompressionLevel);
    public void RefreshLanguage() { OnPropertyChanged(nameof(SourceSummary)); OnPropertyChanged(nameof(QualityDescription)); }
    partial void OnEncoderChanged(string value)
    {
        CompressionLevel = AudioEncoding.DefaultCompression(value);
        if (!BitrateChoices.Contains(BitRateKbps)) BitRateKbps = BitrateChoices.Length == 0 ? 0 : BitrateChoices[BitrateChoices.Length / 2];
    }
}

public sealed partial class SubtitleTrackOptions : ObservableObject
{
    public void RefreshLanguage() => OnPropertyChanged(nameof(SourceSummary));
    public int InputIndex { get; set; }
    public int SubtitleOrdinal { get; set; }
    public string SourceCodec { get; set; } = "";
    public string SourceSummary { get; set; } = "";
    public bool IsBitmap { get; set; }
    [ObservableProperty] private TrackAction action = TrackAction.Kopieren;
    [ObservableProperty] private string encoder = "srt";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string language = "und";
    [ObservableProperty] private bool isDefault;
    [ObservableProperty] private bool forced;
}

public sealed partial class EncodingOptions : ObservableObject
{
    public VideoOptions Video { get; set; } = new();
    public FilterOptions Filters { get; set; } = new();
    public ObservableCollection<AudioTrackOptions> Audio { get; set; } = [];
    public ObservableCollection<SubtitleTrackOptions> Subtitles { get; set; } = [];
    public ObservableCollection<CutRegion> Cuts { get; set; } = [];
    [ObservableProperty] private bool cutsEnabled = true;
    [ObservableProperty] private string container = "mkv";
    [ObservableProperty] private string outputDirectory = "";
    [ObservableProperty] private string fileNamePattern = "{name}_{height}p_{codec}";
    [ObservableProperty] private string presetName = "Benutzerdefiniert";
    public DateTimeOffset? FileNameTimestamp { get; set; }
    [ObservableProperty] private bool autoAnalyze;
    public double? ClipStart { get; set; }
    public double? ClipDuration { get; set; }
    public static EncodingOptions ForMedia(MediaInfo media)
    {
        var o = new EncodingOptions { OutputDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(media.Path)!, AppIdentity.Name) };
        foreach (var s in media.Streams.Where(s => s.Type == "audio"))
            o.Audio.Add(new() { InputIndex = s.Index, SourceCodec = s.Codec, SourceSummary = s.Summary, SourceChannels = s.Channels ?? 2, Title = s.Title, Language = s.Language, IsDefault = s.Default });
        int ordinal = 0;
        foreach (var s in media.Streams.Where(s => s.Type == "subtitle"))
            o.Subtitles.Add(new() { InputIndex = s.Index, SubtitleOrdinal = ordinal++, SourceCodec = s.Codec, SourceSummary = s.Summary, IsBitmap = s.IsBitmapSubtitle, Title = s.Title, Language = s.Language, IsDefault = s.Default, Forced = s.Forced });
        return o;
    }
}

public sealed record Toolchain(string Version, string FFmpegPath, string FFprobePath, bool Managed, string BuildInfo = "");
public sealed record EncoderOption(string Name, string Type, string Description, List<string> Choices);
public sealed record EncoderInfo(string Name, string Description, string MediaType, string Family, bool Hardware, string Help = "",
    List<string>? PixelFormats = null, List<EncoderOption>? Options = null);
public sealed class Capabilities
{
    public string Version { get; set; } = "";
    public string BuildConfiguration { get; set; } = "";
    public List<EncoderInfo> Encoders { get; set; } = [];
    public HashSet<string> Decoders { get; set; } = [];
    public HashSet<string> Filters { get; set; } = [];
    public HashSet<string> HardwareAccelerators { get; set; } = [];
    public HashSet<string> PixelFormats { get; set; } = [];
    public List<EncoderInfo> ModernVideo => Encoders.Where(e => e.MediaType == "video" && VideoEncoderPolicy.IsAllowed(e.Name)).ToList();
    public List<EncoderInfo> Audio => Encoders.Where(e => e.MediaType == "audio").ToList();
    public EncoderInfo? Find(string name) => Encoders.FirstOrDefault(e => e.Name == name);
}

public sealed partial class EncodingJob : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MediaInfo Source { get; set; } = new();
    public EncodingOptions Options { get; set; } = new();
    public Toolchain Tools { get; set; } = new("", "", "", false);
    public string OutputPath { get; set; } = "";
    public string PartialPath { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string ExecutedCommand { get; set; } = "";
    public ProcessSchedulingInfo? Scheduling { get; set; }
    public int Order { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayStatus))] private JobStatus status;
    [ObservableProperty] private double? progress;
    [ObservableProperty] private string speed = "";
    [ObservableProperty] private double fps;
    [ObservableProperty] private string remaining = "";
    [ObservableProperty] private long outputBytes;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private DateTimeOffset? startedAt;
    [ObservableProperty] private DateTimeOffset? endedAt;
    [ObservableProperty] private string statistics = "";
    public MediaInfo? OutputInfo { get; set; }
    public QualityResult? Analysis { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayStatus),nameof(IsAnalysisIndeterminate))]
    [property: JsonIgnore] private bool isAnalyzing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnalysisIndeterminate))]
    [property: JsonIgnore] private double? analysisProgress;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAnalysisStatus))]
    [property: JsonIgnore] private string analysisStatus = "";
    [ObservableProperty] [property: JsonIgnore] private string analysisDetails = "";
    [JsonIgnore] public bool IsAnalysisIndeterminate => IsAnalyzing&&!AnalysisProgress.HasValue;
    [JsonIgnore] public bool HasAnalysisStatus => AnalysisStatus.Length>0;
    [JsonIgnore] public string DisplayStatus => IsAnalyzing?L.T("Analyse läuft"):L.EnumText(Status);
    public void RefreshLanguage() { OnPropertyChanged(nameof(DisplayStatus)); OnPropertyChanged(nameof(Conversion)); OnPropertyChanged(nameof(PresetName)); }
    public string Name => Source.Name;
    public string Conversion => ScalingService.Describe(Source, Options);
    public string PresetName => Options.PresetName;
}

public sealed record ProgressInfo(double? Percent, double Seconds, double Speed, double Fps, long Bytes, TimeSpan Elapsed, TimeSpan? Remaining);
public sealed record QualityFrame(double Seconds, double? Vmaf, double? Ssim, double? Psnr);
public sealed record QualityResult(double? Vmaf, double? Ssim, double? Psnr, string Conditions, List<QualityFrame> Frames, string LogPath);
public sealed record QualityAnalysisProgress(string Stage, double? Percent, double? Seconds, double? Duration,
    double? Speed, TimeSpan Elapsed, TimeSpan? Remaining);
public sealed record FramePair(string OriginalPath, string EncodedPath, double SourceSeconds, double EncodedSeconds, int Width, int Height, string Conditions);
public sealed record HardwareProbe(bool Usable, string Details);
public sealed record ProcessSchedulingInfo(int ProcessId, bool Requested, bool EcoQos, bool IdlePriority, int ErrorCode)
{
    [JsonIgnore] public string Description => !Requested ? L.T("Windows-Effizienzmodus: Aus") : EcoQos && IdlePriority
        ? L.T("Windows-Effizienzmodus: Aktiv (EcoQoS + niedrige Priorität)") : IdlePriority
        ? L.T("Windows-Effizienzmodus: Nur niedrige Priorität; EcoQoS nicht verfügbar")
        : L.T("Windows-Effizienzmodus: Nicht verfügbar");
}
public sealed record SensorSnapshot(double? CpuPercent, double? RamPercent, List<GpuSensor> Gpus);
public sealed record GpuSensor(int Index, string Name, string Driver, double? Utilization, double? MemoryUsedMiB, double? MemoryTotalMiB,
    double? EncoderPercent, double? DecoderPercent, double? Temperature, double? Power);

public sealed class AudioRule
{
    public string[] KeepLanguages { get; set; } = [];
    public string[] CopyCodecs { get; set; } = [];
    public string[] LanguageOrder { get; set; } = ["deu", "ger", "eng"];
    public bool PreferGerman { get; set; }
    public bool TranscodeOthers { get; set; }
    public string Encoder { get; set; } = "libopus";
    public int BitRateKbps { get; set; } = 192;
}
public sealed class Preset
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public EncodingOptions Options { get; set; } = new();
    public AudioRule AudioRule { get; set; } = new();
    public override string ToString() => Name;
}
public sealed class WatchRule
{
    public bool Enabled { get; set; }
    public string Folder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public bool Recursive { get; set; }
    public string Extensions { get; set; } = ".mp4;.mkv;.mov;.webm;.avi;.m2ts;.ts";
    public string PresetName { get; set; } = "Gute Qualität bei kleiner Datei";
    public bool AutoStart { get; set; }
}
public sealed class AppSettings
{
    public string Language { get; set; } = "de";
    public bool DarkMode { get; set; } = true;
    public string ThemeId { get; set; } = "";
    public bool TooltipsEnabled { get; set; } = true;
    public bool ShowTestEncodeNotification { get; set; } = true;
    public double TooltipDelaySeconds { get; set; } = TooltipTiming.DefaultSeconds;
    public FileNamingSettings FileNaming { get; set; } = new();
    public CodecMemory Codecs { get; set; } = new();
    public bool RecursiveImport { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public bool AutoInstall { get; set; }
    public bool AutoUpdate { get; set; }
    public int UpdateIntervalHours { get; set; } = 24;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public int ParallelJobs { get; set; } = 1;
    public int ParallelGpuJobs { get; set; } = 1;
    public bool EfficiencyMode { get; set; } = true;
    public int CacheLimitMiB { get; set; } = 512;
    public List<WatchRule> WatchFolders { get; set; } = [];
    public string DefaultOutputDirectory { get; set; } = "";
    public CompletionAction CompletionAction { get; set; }
}
