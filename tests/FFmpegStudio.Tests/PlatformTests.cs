using System.Diagnostics;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

public sealed class PlatformTests
{
    [Fact]
    public void Process_control_is_selected_for_the_current_operating_system()
    {
        var control = ProcessControls.ForCurrentPlatform();
        var info = control.ApplyEfficiency(Process.GetCurrentProcess(), enabled: !OperatingSystem.IsWindows());
        Assert.Equal(Environment.ProcessId, info.ProcessId);
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsProcessControl>(control);
            Assert.False(info.Requested);
            return;
        }
        Assert.IsType<PortableProcessControl>(control);
        Assert.True(info.Requested);
        Assert.False(info.EcoQos);
        Assert.False(info.IdlePriority);
        Assert.Null(control.AttachLifetime(Process.GetCurrentProcess()));
    }

    [Fact]
    public async Task Managed_ffmpeg_install_rejects_non_windows_before_any_download()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffstudio-dist-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var service = new FFmpegDownloadService(paths, new FFmpegVersionService(paths, new ProcessRunner()), new HttpClient());
            if (OperatingSystem.IsWindows())
            {
                Assert.True(service.SupportsManagedInstall);
                Assert.Contains("gyan.dev", service.SourceDescription);
                return;
            }
            Assert.False(service.SupportsManagedInstall);
            Assert.Contains("gyan.dev", service.SourceDescription);
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.CheckAsync());
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.InstallAsync(new RemoteBuild("0", new string('a', 64), new Uri("https://example.invalid/ffmpeg.7z"))));
        }
        finally
        {
            if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("ffstudio-dist-", StringComparison.Ordinal))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Linux_cpu_and_memory_are_read_from_proc_when_those_files_exist()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/proc/stat") || !File.Exists("/proc/meminfo")) return;
        var monitor = new HardwareMonitorService(new ProcessRunner());
        var sample = await monitor.ReadAsync();
        if (File.ReadAllText("/proc/meminfo").Contains("MemAvailable:", StringComparison.Ordinal))
        {
            Assert.NotNull(sample.RamPercent);
            Assert.InRange(sample.RamPercent!.Value, 0, 100);
        }
        for (var i = 0; i < 5 && sample.CpuPercent is null; i++)
        {
            await Task.Delay(30);
            sample = await monitor.ReadAsync();
        }
        Assert.NotNull(sample.CpuPercent);
        Assert.InRange(sample.CpuPercent!.Value, 0, 100);
    }
}
