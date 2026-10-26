using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public enum LinuxLibcKind { Unknown, Glibc, Musl }

/// <summary>What the process can run the pinned static FFmpeg builds on.</summary>
public readonly record struct LinuxLibcInfo(LinuxLibcKind Kind, Version? Version, string Evidence)
{
    /// <summary>
    /// BtbN publishes glibc 2.28 and Linux 4.18 as the floor for its Linux builds.
    /// John Van Sickle's static builds are also glibc binaries (not musl) and do not publish a lower glibc floor.
    /// Both downloads are refused below 2.28 and on musl.
    /// </summary>
    public static readonly Version MinimumGlibc = new(2, 28);

    public bool SupportsStaticFfmpeg =>
        Kind == LinuxLibcKind.Glibc && Version is { } version && version >= MinimumGlibc;
}

public static partial class LinuxLibc
{
    public static LinuxLibcInfo Detect()
    {
        if (!OperatingSystem.IsLinux()) return new(LinuxLibcKind.Unknown, null, "not-linux");
        var ldd = ReadCommand("ldd", ["--version"]);
        var getconf = ReadCommand("getconf", ["GNU_LIBC_VERSION"]);
        string? osRelease = null;
        try { if (File.Exists("/etc/os-release")) osRelease = File.ReadAllText("/etc/os-release"); }
        catch (IOException) { }
        var alpine = File.Exists("/etc/alpine-release");
        string? linker = null;
        var self = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(self) && File.Exists(self)) linker = ReadCommand("ldd", [self]);
        return Parse(ldd, getconf, osRelease, alpine, linker);
    }

    public static LinuxLibcInfo Parse(string? lddVersion = null, string? getconf = null, string? osRelease = null, bool alpineReleasePresent = false, string? dynamicLinkerHint = null)
    {
        var evidence = string.Join(" | ", new[] { lddVersion, getconf, osRelease, dynamicLinkerHint }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        if (evidence.Length > 500) evidence = evidence[..500];
        if (alpineReleasePresent || IsAlpine(osRelease) || MentionsMusl(lddVersion) || MentionsMusl(getconf) || MentionsMusl(dynamicLinkerHint))
            return new(LinuxLibcKind.Musl, MuslVersion(lddVersion) ?? MuslVersion(getconf), evidence);
        var glibc = GlibcVersion(lddVersion) ?? GlibcVersion(getconf);
        if (glibc is not null) return new(LinuxLibcKind.Glibc, glibc, evidence);
        return new(LinuxLibcKind.Unknown, null, evidence);
    }

    public static string Guidance(LinuxLibcInfo info)
    {
        var reason = info.Kind switch
        {
            LinuxLibcKind.Musl => L.T("Dieses System nutzt musl (zum Beispiel Alpine). Die statischen GPL-Builds von BtbN und John Van Sickle sind glibc-Programme und werden hier nicht heruntergeladen."),
            LinuxLibcKind.Glibc when info.Version is { } version => L.F($"glibc {version} ist älter als 2.28. Die statischen Builds brauchen mindestens glibc 2.28 und werden nicht heruntergeladen."),
            _ => L.T("Die C-Bibliothek konnte nicht als glibc 2.28 oder neuer bestätigt werden. Der statische Download wird nicht versucht.")
        };
        return reason + Environment.NewLine + L.T("Installiere FFmpeg über die Paketverwaltung und wähle danach „System-FFmpeg“ (ffmpeg und ffprobe im PATH):\nAlpine: apk add ffmpeg\nDebian/Ubuntu: sudo apt install ffmpeg\nFedora/RHEL: sudo dnf install ffmpeg\nArch: sudo pacman -S ffmpeg");
    }

    private static bool IsAlpine(string? osRelease)
    {
        if (string.IsNullOrWhiteSpace(osRelease)) return false;
        foreach (var raw in osRelease.Split('\n'))
        {
            var line = raw.Trim();
            var split = line.Split('=', 2);
            if (split.Length != 2) continue;
            var value = split[1].Trim().Trim('"');
            if (split[0] == "ID" && value == "alpine") return true;
            if (split[0] == "ID_LIKE" && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("alpine")) return true;
        }
        return false;
    }

    private static bool MentionsMusl(string? text) =>
        text is not null && (text.Contains("musl", StringComparison.OrdinalIgnoreCase) || text.Contains("ld-musl", StringComparison.OrdinalIgnoreCase));

    private static Version? GlibcVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || MentionsMusl(text)) return null;
        var match = GlibcPattern().Match(text);
        if (!match.Success) return null;
        return new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static Version? MuslVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = MuslPattern().Match(text);
        if (!match.Success) return null;
        return Version.Parse(match.Groups[1].Value);
    }

    private static string? ReadCommand(string file, string[] args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null) return null;
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(true); } catch (Exception) { }
                return null;
            }
            var text = process.StandardOutput.ReadToEnd() + "\n" + process.StandardError.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception) { return null; }
    }

    [GeneratedRegex(@"(?i)(?:glibc|gnu libc)[^\d]{0,24}(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex GlibcPattern();

    [GeneratedRegex(@"(?i)version\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex MuslPattern();
}
