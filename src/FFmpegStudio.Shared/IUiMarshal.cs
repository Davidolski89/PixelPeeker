namespace FFmpegStudio.Shared;

/// <summary>Posts work to the UI thread. View models stay free of WPF and Avalonia types.</summary>
public interface IUiMarshal
{
    void Invoke(Action action);
}

public sealed class DirectUiMarshal : IUiMarshal
{
    public void Invoke(Action action) => action();
}
