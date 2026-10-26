using System.Text.Json;
using FFmpegStudio.Core;
using Microsoft.Data.Sqlite;

namespace FFmpegStudio.Infrastructure;

public sealed class AppPaths
{
    public string Root { get; }
    public string Versions => Directory.CreateDirectory(Path.Combine(Root, "ffmpeg")).FullName;
    public string Cache => Directory.CreateDirectory(Path.Combine(Root, "cache")).FullName;
    public string Logs => Directory.CreateDirectory(Path.Combine(Root, "logs")).FullName;
    public string Presets => Directory.CreateDirectory(Path.Combine(Root, "presets")).FullName;
    public string Analyses => Directory.CreateDirectory(Path.Combine(Root, "analyses")).FullName;
    public AppPaths(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable(AppIdentity.DataEnvironmentVariable)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.DataFolderName));
        Directory.CreateDirectory(Root);
    }
}

public static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(string path, T data, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data, Json.Options), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct = default) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json.Options) : default;
}

public sealed class LoggingService(AppPaths paths)
{
    private readonly object gate = new();
    public event Action<string>? Written;
    public void Write(string message, Exception? exception = null)
    {
        var line = $"{DateTimeOffset.Now:O}  {message}{(exception is null ? "" : Environment.NewLine + exception)}";
        lock (gate) File.AppendAllText(Path.Combine(paths.Logs, $"studio-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        Written?.Invoke(line);
    }
}

public sealed class SettingsService(AppPaths paths, LoggingService log)
{
    private readonly SemaphoreSlim gate = new(1);
    public AppSettings Current { get; private set; } = new();
    public async Task LoadAsync()
    {
        try { Current = await AtomicFile.ReadJsonAsync<AppSettings>(Path.Combine(paths.Root, "settings.json")) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { log.Write(L.T("Einstellungen konnten nicht geladen werden; sichere Standards aktiv."), e); }
        Current.ParallelJobs = Math.Clamp(Current.ParallelJobs, 1, 16);
        Current.Language = L.Normalize(Current.Language);
        var theme = ThemeCatalog.Resolve(Current.ThemeId, Current.DarkMode);
        Current.ThemeId = theme.Id; Current.DarkMode = theme.IsDark;
        Current.TooltipDelaySeconds = TooltipTiming.Normalize(Current.TooltipDelaySeconds);
        Current.FileNaming ??= new();
        Current.Codecs??=new();Current.Codecs.Normalize();
        Current.ParallelGpuJobs = Math.Clamp(Current.ParallelGpuJobs, 1, 8);
        Current.CompletionAction = CompletionAction.Nichts; // Power actions must be explicitly armed for this session.
    }
    public async Task SaveAsync()
    {
        await gate.WaitAsync();
        try { await AtomicFile.WriteJsonAsync(Path.Combine(paths.Root, "settings.json"), Current); }
        finally { gate.Release(); }
    }
}

public sealed class QueueStore(AppPaths paths)
{
    private readonly SemaphoreSlim gate = new(1);
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(paths.Root, "studio.db"), DefaultTimeout = 15 }.ToString();
    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS schema_versions(version INTEGER PRIMARY KEY, applied TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY, sort_order INTEGER NOT NULL, payload TEXT NOT NULL, updated TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS analyses(job_id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS watch_imports(fingerprint TEXT PRIMARY KEY, imported TEXT NOT NULL);
            INSERT OR IGNORE INTO schema_versions VALUES(1, datetime('now'));
            """;
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task<List<EncodingJob>> LoadJobsAsync()
    {
        await using var c = new SqliteConnection(ConnectionString); await c.OpenAsync();
        var cmd = c.CreateCommand(); cmd.CommandText = "SELECT payload FROM jobs ORDER BY sort_order";
        var jobs = new List<EncodingJob>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var job = JsonSerializer.Deserialize<EncodingJob>(reader.GetString(0), Json.Options) ?? throw new InvalidDataException(L.T("Ungültiger Queue-Eintrag."));
            if (job.Status == JobStatus.Läuft) { job.Status = JobStatus.Unterbrochen; job.Error = L.T("Die Anwendung wurde während des Encodes beendet. Erneut starten ist möglich."); }
            jobs.Add(job);
        }
        return jobs;
    }
    public async Task SaveJobAsync(EncodingJob job)
    {
        var payload = JsonSerializer.Serialize(job, Json.Options);
        await ExecuteAsync("INSERT INTO jobs VALUES($id,$order,$payload,$time) ON CONFLICT(id) DO UPDATE SET sort_order=$order,payload=$payload,updated=$time",
            ("$id", job.Id.ToString()), ("$order", job.Order), ("$payload", payload), ("$time", DateTimeOffset.UtcNow.ToString("O")));
    }
    public Task DeleteJobAsync(Guid id) => ExecuteAsync("DELETE FROM jobs WHERE id=$id", ("$id", id.ToString()));
    public Task SaveAnalysisAsync(Guid id, QualityResult result) => ExecuteAsync("INSERT INTO analyses VALUES($id,$payload) ON CONFLICT(job_id) DO UPDATE SET payload=$payload",
        ("$id", id.ToString()), ("$payload", JsonSerializer.Serialize(result, Json.Options)));
    public async Task<bool> WasWatchedAsync(string fingerprint)
    {
        await using var c = new SqliteConnection(ConnectionString); await c.OpenAsync();
        var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM watch_imports WHERE fingerprint=$f"; cmd.Parameters.AddWithValue("$f", fingerprint);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }
    public Task MarkWatchedAsync(string fingerprint) => ExecuteAsync("INSERT OR IGNORE INTO watch_imports VALUES($f,$t)", ("$f", fingerprint), ("$t", DateTimeOffset.UtcNow.ToString("O")));
    private async Task ExecuteAsync(string sql, params (string Key, object Value)[] parameters)
    {
        await gate.WaitAsync();
        try
        {
            await using var c = new SqliteConnection(ConnectionString); await c.OpenAsync();
            var cmd = c.CreateCommand(); cmd.CommandText = sql;
            foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { gate.Release(); }
    }
}
