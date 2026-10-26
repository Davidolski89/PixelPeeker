using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace FFmpegStudio.Avalonia;

/// <summary>Hosts a native child window and hands its handle to libvlc. This replaces LibVLCSharp.Avalonia, which targets Avalonia 11.</summary>
public class VlcVideoView : NativeControlHost
{
    public static readonly StyledProperty<AvaloniaPlaybackSession?> SessionProperty =
        global::Avalonia.AvaloniaProperty.Register<VlcVideoView, AvaloniaPlaybackSession?>(nameof(Session));

    private IntPtr handle;

    public AvaloniaPlaybackSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    static VlcVideoView()
    {
        SessionProperty.Changed.AddClassHandler<VlcVideoView>((view, _) => view.PushSurface());
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var created = base.CreateNativeControlCore(parent);
        handle = created.Handle;
        PushSurface();
        return created;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        handle = IntPtr.Zero;
        Session?.AttachSurface(IntPtr.Zero);
        base.DestroyNativeControlCore(control);
    }

    private void PushSurface()
    {
        if (handle != IntPtr.Zero) Session?.AttachSurface(handle);
    }
}
