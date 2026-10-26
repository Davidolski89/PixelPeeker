using System.Diagnostics;
using FFmpegStudio.Core;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FFmpegStudio.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess(string operation)
    {
        if (ExitCode != 0) throw new InvalidOperationException(L.F($"{operation} (Exitcode {ExitCode}):{Environment.NewLine}{StandardError}"));
    }
}

public sealed class ProcessRunner
{
    private readonly IProcessControl control;
    public ProcessRunner() : this(ProcessControls.ForCurrentPlatform()) { }
    public ProcessRunner(IProcessControl control) => this.control = control;
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken ct = default,
        Action<string>? outputLine = null, Action<string>? errorLine = null, string? workingDirectory = null, Action? started = null,
        bool efficiencyMode = false, Action<ProcessSchedulingInfo>? schedulingApplied = null)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException(L.F($"Prozess konnte nicht gestartet werden: {executable}"));
        var scheduling = control.ApplyEfficiency(process, efficiencyMode);
        using var job = control.AttachLifetime(process);
        using var registration = ct.Register(() => Kill(process));
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        async Task ReadAsync(StreamReader reader, StringBuilder buffer, Action<string>? callback, int limit)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (buffer.Length + line.Length > limit) buffer.Remove(0, Math.Min(buffer.Length, Math.Max(limit / 4, line.Length)));
                buffer.AppendLine(line);
                callback?.Invoke(line);
            }
        }
        var readOut = ReadAsync(process.StandardOutput, stdout, outputLine, 32 * 1024 * 1024);
        var readError = ReadAsync(process.StandardError, stderr, errorLine, 512 * 1024);
        try
        {
            schedulingApplied?.Invoke(scheduling);
            started?.Invoke();
            // Drain both pipes concurrently; cancellation kills the whole process tree.
            await Task.WhenAll(process.WaitForExitAsync(), readOut, readError);
            ct.ThrowIfCancellationRequested();
            return new(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync();
        }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

// Closing this handle on app exit also kills child FFmpeg processes, including on crashes.
internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    private WindowsProcessJob(SafeFileHandle handle) => this.handle = handle;
    public static WindowsProcessJob? TryAttach(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        var info = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimit>()) || !AssignProcessToJobObject(handle, process.Handle))
        { handle.Dispose(); return null; }
        return new(handle);
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
    { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorking, MaxWorking; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
    { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref ExtendedLimit info, uint length);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
