using Avalonia.Threading;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public sealed class AvaloniaUiMarshal : IUiMarshal
{
    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
