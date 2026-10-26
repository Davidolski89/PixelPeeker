using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Shared;

public static class StudioServices
{
    /// <summary>Registers the portable services shared by the WPF and Avalonia shells.</summary>
    public static IServiceCollection AddPixelPeeker(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(45) });
        services.AddSingleton<LoggingService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<QueueStore>();
        services.AddSingleton<IProcessControl>(_ => ProcessControls.ForCurrentPlatform());
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<FFmpegVersionService>();
        services.AddSingleton<FFmpegDownloadService>();
        services.AddSingleton<LinuxFfmpegDistribution>();
        services.AddSingleton<IFmpegDistribution>(sp => OperatingSystem.IsWindows()
            ? sp.GetRequiredService<FFmpegDownloadService>()
            : sp.GetRequiredService<LinuxFfmpegDistribution>());
        services.AddSingleton<FFmpegCapabilityService>();
        services.AddSingleton<HardwareDetectionService>();
        services.AddSingleton<MediaProbeService>();
        services.AddSingleton<PreviewService>();
        services.AddSingleton<FrameComparisonService>();
        services.AddSingleton<QualityAnalysisService>();
        services.AddSingleton<FrameNavigationService>();
        services.AddSingleton<AccuratePreviewService>();
        services.AddSingleton<EncodeComparisonService>();
        services.AddSingleton<FFmpegCommandBuilder>();
        services.AddSingleton<QueueService>();
        services.AddSingleton<PresetService>();
        services.AddSingleton<SubtitleService>();
        services.AddSingleton<FolderWatchService>();
        services.AddSingleton<HardwareMonitorService>();
        services.AddSingleton<IHardwareMonitor>(sp => sp.GetRequiredService<HardwareMonitorService>());
        return services;
    }
}
