using System.Diagnostics;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

/// <summary>
/// Applies optional encode-process scheduling and tracks the process tree.
/// Windows uses EcoQoS and a job object. Other systems keep the process unchanged.
/// </summary>
public interface IProcessControl
{
    ProcessSchedulingInfo ApplyEfficiency(Process process, bool enabled);
    IDisposable? AttachLifetime(Process process);
}

/// <summary>Reads CPU, memory and GPU sensors that the current machine actually exposes.</summary>
public interface IHardwareMonitor
{
    Task<SensorSnapshot> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Managed FFmpeg acquisition. Windows downloads gyan.dev builds. Linux downloads static GPL builds
/// from BtbN/FFmpeg-Builds, with John Van Sickle's static builds as a fallback.
/// </summary>
public interface IFmpegDistribution
{
    bool SupportsManagedInstall { get; }
    string SourceDescription { get; }
    Task<RemoteBuild> CheckAsync(CancellationToken ct = default);
    Task<Toolchain> InstallAsync(RemoteBuild build, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
}

public static class ProcessControls
{
    public static IProcessControl ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsProcessControl() : new PortableProcessControl();
}

public sealed class WindowsProcessControl : IProcessControl
{
    public ProcessSchedulingInfo ApplyEfficiency(Process process, bool enabled) => WindowsEfficiencyMode.Apply(process, enabled);
    public IDisposable? AttachLifetime(Process process) => WindowsProcessJob.TryAttach(process);
}

/// <summary>No EcoQoS and no Windows job object. Cancellation still kills the started process tree.</summary>
public sealed class PortableProcessControl : IProcessControl
{
    public ProcessSchedulingInfo ApplyEfficiency(Process process, bool enabled) => new(process.Id, enabled, false, false, 0);
    public IDisposable? AttachLifetime(Process process) => null;
}
