using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed class FolderWatchService(SettingsService settings, QueueStore store, LoggingService log) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, (long Size, long Write, int Stable)> candidates = new(StringComparer.OrdinalIgnoreCase);
    private Task? worker;
    private readonly object disposeGate = new();
    private Task? disposal;
    public Func<string, WatchRule, CancellationToken, Task>? OnReady { get; set; }
    public void Start() => worker ??= Task.Run(RunAsync);
    public static bool IsUnder(string child, string parent)
    {
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar);
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return c.Equals(p, StringComparison.OrdinalIgnoreCase) || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    public static void ValidateRule(WatchRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Folder) || string.IsNullOrWhiteSpace(rule.OutputFolder)) throw new ArgumentException(L.T("Eingangs- und Ausgabeordner sind erforderlich."));
        if (IsUnder(rule.Folder, rule.OutputFolder)) throw new ArgumentException(L.T("Der Ausgabeordner darf nicht der Eingangsordner oder dessen übergeordneter Ordner sein."));
        if (!Directory.Exists(rule.Folder)) throw new DirectoryNotFoundException(rule.Folder);
    }
    private async Task RunAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var rules = Json.Clone(settings.Current.WatchFolders);
                foreach (var rule in rules.Where(r => r.Enabled))
                {
                    try
                    {
                        ValidateRule(rule);
                        var extensions = rule.Extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var files = Directory.EnumerateFiles(rule.Folder, "*", new EnumerationOptions { RecurseSubdirectories = rule.Recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });
                        foreach (var path in files)
                        {
                            lifetime.Token.ThrowIfCancellationRequested();
                            if (rules.Any(r => !string.IsNullOrWhiteSpace(r.OutputFolder) && IsUnder(path, r.OutputFolder))) continue;
                            if (!extensions.Contains("*") && !extensions.Contains(Path.GetExtension(path))) continue;
                            try
                            {
                                var info = new FileInfo(path);
                                var fingerprint = $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                                if (await store.WasWatchedAsync(fingerprint)) continue;
                                var previous = candidates.GetValueOrDefault(path);
                                int stable = previous.Size == info.Length && previous.Write == info.LastWriteTimeUtc.Ticks ? previous.Stable + 1 : 0;
                                candidates[path] = (info.Length, info.LastWriteTimeUtc.Ticks, stable);
                                if (stable < 3 || info.Length == 0 || DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromSeconds(15)) continue;
                                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                                if (OnReady is not null)
                                {
                                    await OnReady(path, rule, lifetime.Token);
                                    await store.MarkWatchedAsync(fingerprint); candidates.Remove(path);
                                }
                            }
                            catch (IOException) { }
                            catch (Exception e) when (e is not OperationCanceledException) { log.Write($"Ordnerimport: {path}", e); }
                        }
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { log.Write(L.F($"Ordnerüberwachung: {rule.Folder}"), e); }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    public ValueTask DisposeAsync()
    {
        lock(disposeGate) return new(disposal??=DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        await lifetime.CancelAsync(); if (worker is not null) await worker; lifetime.Dispose();
    }
}

public sealed class HardwareMonitorService(ProcessRunner runner) : IHardwareMonitor
{
    private ulong lastIdle, lastKernel, lastUser;
    private ulong linuxIdle, linuxTotal;
    public async Task<SensorSnapshot> ReadAsync(CancellationToken ct = default)
    {
        double? cpu = null, ram = null;
        if (OperatingSystem.IsWindows())
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong delta = kernel + user - lastKernel - lastUser;
                if (lastKernel > 0 && delta > 0) cpu = Math.Clamp(100d * (delta - (idle - lastIdle)) / delta, 0, 100);
                (lastIdle, lastKernel, lastUser) = (idle, kernel, user);
            }
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref memory)) ram = memory.Load;
        }
        else ReadLinuxSensors(ref cpu, ref ram);
        var gpus = new List<GpuSensor>();
        var executable = FindNvidiaSmi();
        if (executable is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(3000);
            try
            {
                var result = await runner.RunAsync(executable, ["--query-gpu=index,name,driver_version,utilization.gpu,memory.used,memory.total,utilization.encoder,utilization.decoder,temperature.gpu,power.draw", "--format=csv,noheader,nounits"], timeout.Token);
                if (result.ExitCode == 0)
                    foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = line.Split(',', StringSplitOptions.TrimEntries);
                        if (p.Length < 10 || !int.TryParse(p[0], out var index)) continue;
                        double? N(int i) => double.TryParse(p[i], CultureInfo.InvariantCulture, out var n) ? n : null;
                        gpus.Add(new(index, p[1], p[2], N(3), N(4), N(5), N(6), N(7), N(8), N(9)));
                    }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return new(cpu, ram, gpus);
    }
    public static string? FindNvidiaSmi()
    {
        var choices = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            choices.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"));
            choices.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"));
            choices.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(Directory.Exists).Select(d => Path.Combine(d, "nvidia-smi.exe")));
        }
        else
        {
            choices.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(Directory.Exists).Select(d => Path.Combine(d, "nvidia-smi")));
        }
        return choices.FirstOrDefault(File.Exists);
    }
    private void ReadLinuxSensors(ref double? cpu, ref double? ram)
    {
        try
        {
            if (File.Exists("/proc/stat"))
            {
                var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
                var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts is { Length: >= 5 } && parts.Skip(1).All(p => ulong.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                {
                    var numbers = parts.Skip(1).Select(p => ulong.Parse(p, CultureInfo.InvariantCulture)).ToArray();
                    var idle = numbers[3] + (numbers.Length > 4 ? numbers[4] : 0);
                    ulong total = 0;
                    foreach (var n in numbers) total += n;
                    if (linuxTotal > 0 && total > linuxTotal)
                    {
                        var delta = total - linuxTotal;
                        var idleDelta = idle >= linuxIdle ? idle - linuxIdle : 0;
                        cpu = Math.Clamp(100d * (delta - Math.Min(idleDelta, delta)) / delta, 0, 100);
                    }
                    linuxIdle = idle;
                    linuxTotal = total;
                }
            }
            if (!File.Exists("/proc/meminfo")) return;
            ulong? memTotal = null, memAvailable = null;
            foreach (var entry in File.ReadLines("/proc/meminfo"))
            {
                if (entry.StartsWith("MemTotal:", StringComparison.Ordinal) && TryKb(entry, out var totalKb)) memTotal = totalKb;
                if (entry.StartsWith("MemAvailable:", StringComparison.Ordinal) && TryKb(entry, out var availableKb)) memAvailable = availableKb;
            }
            if (memTotal is > 0 && memAvailable is not null)
                ram = Math.Clamp(100d * (memTotal.Value - Math.Min(memAvailable.Value, memTotal.Value)) / memTotal.Value, 0, 100);
        }
        catch (IOException) { }
    }
    private static bool TryKb(string line, out ulong value)
    {
        value = 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
