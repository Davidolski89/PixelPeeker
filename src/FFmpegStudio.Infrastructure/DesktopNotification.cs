using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FFmpegStudio.Infrastructure;

/// <summary>
/// Completion balloon. Linux uses the freedesktop notification service, then notify-send.
/// Windows uses the same shell balloon the WPF shell shows with NotifyIcon. A missing bus is ignored.
/// </summary>
public static class DesktopNotification
{
    public static async Task ShowAsync(string title, string body, CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsBalloon.Show(title, body);
                return;
            }
            var summary = Sanitize(title);
            var message = Sanitize(body);
            if (await RunAsync("gdbus",
            [
                "call", "--session", "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.Notify",
                summary, "0", "", summary, message, "[]", "{}", "5000"
            ], ct) == 0) return;
            if (await RunAsync("dbus-send",
            [
                "--session", "--dest=org.freedesktop.Notifications", "--type=method_call",
                "/org/freedesktop/Notifications", "org.freedesktop.Notifications.Notify",
                "string:" + summary, "uint32:0", "string:", "string:" + summary, "string:" + message,
                "array:string:", "dict:string:string:", "int32:5000"
            ], ct) == 0) return;
            await RunAsync("notify-send", ["-a", summary, summary, message], ct);
        }
        catch (Exception)
        {
            // Encode completion still stands when the session has no notification service.
        }
    }

    private static string Sanitize(string value) => value.Replace("\r", " ").Replace("\n", " ");

    private static async Task<int> RunAsync(string file, string[] arguments, CancellationToken ct)
    {
        if (SessionPower.Find(file) is null) return -1;
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true });
        if (process is null) return -1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            return -1;
        }
        return process.ExitCode;
    }

    private static class WindowsBalloon
    {
        public static void Show(string title, string body)
        {
            var data = new NotifyIconData
            {
                cbSize = Marshal.SizeOf<NotifyIconData>(),
                hWnd = GetDesktopWindow(),
                uID = 1,
                uFlags = 0x10,
                szTip = "",
                szInfo = Trim(body, 255),
                szInfoTitle = Trim(title, 63),
                dwInfoFlags = 1,
                uTimeoutOrVersion = 5000
            };
            Shell_NotifyIcon(0, ref data);
        }

        private static string Trim(string value, int length) => value.Length <= length ? value : value[..length];

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
        }
    }
}
