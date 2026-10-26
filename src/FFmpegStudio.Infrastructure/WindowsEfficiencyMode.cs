using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public static class WindowsEfficiencyMode
{
    public static ProcessSchedulingInfo Apply(Process process, bool enabled)
    {
        var id = process.Id;
        if (!enabled || !OperatingSystem.IsWindows()) return new(id, enabled, false, false, 0);
        bool idle = false, eco = false; int error = 0;
        try { process.PriorityClass = ProcessPriorityClass.Idle; idle = process.PriorityClass == ProcessPriorityClass.Idle; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        { error = e is Win32Exception win32 ? win32.NativeErrorCode : -1; }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            try
            {
                var state = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 1 };
                if (SetProcessInformation(process.Handle, 4, ref state, (uint)Marshal.SizeOf<PowerThrottling>()))
                {
                    state = new PowerThrottling { Version = 1 };
                    if (GetProcessInformation(process.Handle, 4, ref state, (uint)Marshal.SizeOf<PowerThrottling>()))
                        eco = (state.ControlMask & state.StateMask & 1) != 0;
                    else error = Marshal.GetLastWin32Error();
                }
                else error = Marshal.GetLastWin32Error();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or EntryPointNotFoundException)
            { error = e is Win32Exception win32 ? win32.NativeErrorCode : -1; }
        }
        return new(id, true, eco, idle, error);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerThrottling { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottling state, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(IntPtr process, int informationClass, ref PowerThrottling state, uint length);
}
