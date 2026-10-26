using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public enum PlaybackSignalKind { Playing, Time, Ended, Failed, Buffering }
public sealed record PlaybackSignal(long Session, PlaybackSignalKind Kind, double Value = 0);

/// <summary>
/// Platform playback. The Windows shell uses bundled LibVLC. The Avalonia shell uses LibVLCSharp
/// against the system libvlc and does not bundle native plugins.
/// </summary>
public interface IPlaybackSession : IAsyncDisposable
{
    event Action<PlaybackSignal>? Changed;
    string EngineName { get; }
    bool HasInput { get; }
    bool IsSurfaceReady { get; }
    object? NativePlayer { get; }
    Task OpenAsync(long id, MediaInfo source, double seconds, int volume, bool muted, CancellationToken ct);
    Task PlayAsync(long id, CancellationToken ct);
    Task SeekAsync(long id, double seconds, CancellationToken ct);
    Task<double> HoldAsync(long id, bool stop, CancellationToken ct);
    Task StopAsync(long id);
    Task SetAudioAsync(int volume, bool muted);
}

public sealed class UnavailablePlaybackSession : IPlaybackSession
{
    public event Action<PlaybackSignal>? Changed { add { } remove { } }
    public string EngineName => "";
    public bool HasInput => false;
    public bool IsSurfaceReady => false;
    public object? NativePlayer => null;
    public Task OpenAsync(long id, MediaInfo source, double seconds, int volume, bool muted, CancellationToken ct) =>
        throw new InvalidOperationException(L.T("Wiedergabe ist nicht verfügbar."));
    public Task PlayAsync(long id, CancellationToken ct) => Task.CompletedTask;
    public Task SeekAsync(long id, double seconds, CancellationToken ct) => Task.CompletedTask;
    public Task<double> HoldAsync(long id, bool stop, CancellationToken ct) => Task.FromResult(0d);
    public Task StopAsync(long id) => Task.CompletedTask;
    public Task SetAudioAsync(int volume, bool muted) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
