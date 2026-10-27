using System.Diagnostics;
using FFmpegStudio.Core;

namespace FFmpegStudio.Infrastructure;

public sealed record PowerInvocation(string File, string[] Arguments);

public static class SessionAlerts
{
    public static string Notify() => L.T("Alle Jobs erfolgreich abgeschlossen.");

    public static void Beep()
    {
        try { Console.Beep(); }
        catch (PlatformNotSupportedException) { }
        catch (InvalidOperationException) { }
    }
}

/// <summary>
/// Linux completion power actions. Windows keeps its own countdown and power APIs in the WPF shell.
/// A missing systemctl/loginctl is a no-op with an explanation, not an error.
/// </summary>
public static class SessionPower
{
    public const int CountdownSeconds = 30;

    public static string Prompt(CompletionAction action) => action switch
    {
        CompletionAction.Energiesparen => L.T("Energiesparen startet nach dem Countdown. Abbrechen ist möglich."),
        CompletionAction.Herunterfahren => L.T("Herunterfahren startet nach dem Countdown. Abbrechen ist möglich."),
        _ => L.EnumText(action)
    };

    public static PowerInvocation? Resolve(CompletionAction action, Func<string, bool> exists)
    {
        (string File, string[] Arguments)[] choices = action switch
        {
            CompletionAction.Energiesparen => [("systemctl", ["suspend"]), ("loginctl", ["suspend"])],
            CompletionAction.Herunterfahren => [("systemctl", ["poweroff"]), ("loginctl", ["poweroff"])],
            _ => []
        };
        foreach (var choice in choices)
            if (exists(choice.File)) return new PowerInvocation(choice.File, choice.Arguments);
        return null;
    }

    public static async Task<string> ExecuteAsync(CompletionAction action, CancellationToken ct = default)
    {
        var command = Resolve(action, static name => Find(name) is not null);
        if (command is null) return L.T("Diese Energieaktion wird hier nicht unterstützt.");
        var file = Find(command.File) ?? command.File;
        using var process = Process.Start(new ProcessStartInfo(file, command.Arguments) { UseShellExecute = false, CreateNoWindow = true });
        if (process is null) return L.T("Diese Energieaktion wird hier nicht unterstützt.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0
            ? L.T("Energieaktion wurde gestartet.")
            : L.T("Diese Energieaktion wird hier nicht unterstützt.");
    }

    public static string? Find(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
