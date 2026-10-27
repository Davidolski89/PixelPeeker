using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FFmpegStudio.Core;

namespace FFmpegStudio.Avalonia;

/// <summary>Red cut ranges on the source timeline, matching the WPF strip.</summary>
public sealed class CutTimelineView : Control
{
    public static readonly StyledProperty<IReadOnlyList<CutRegion>?> CutsProperty =
        AvaloniaProperty.Register<CutTimelineView, IReadOnlyList<CutRegion>?>(nameof(Cuts));

    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<CutTimelineView, double>(nameof(Duration), 1);

    public static readonly StyledProperty<bool> ActiveProperty =
        AvaloniaProperty.Register<CutTimelineView, bool>(nameof(Active), true);

    static CutTimelineView()
    {
        AffectsRender<CutTimelineView>(CutsProperty, DurationProperty, ActiveProperty);
    }

    public IReadOnlyList<CutRegion>? Cuts
    {
        get => GetValue(CutsProperty);
        set => SetValue(CutsProperty, value);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public bool Active
    {
        get => GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (Duration <= 0 || width <= 0 || height <= 0) return;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), null, new Rect(0, 0, width, height), 2, 2);
        var brush = new SolidColorBrush(Color.FromArgb(Active ? (byte)220 : (byte)70, 240, 90, 100));
        foreach (var cut in Cuts ?? [])
        {
            var start = Math.Clamp(cut.Start.Seconds / Duration, 0, 1) * width;
            var end = Math.Clamp((cut.End?.Seconds ?? Duration) / Duration, 0, 1) * width;
            context.DrawRectangle(brush, null, new Rect(start, 0, Math.Max(0, end - start), height), 2, 2);
        }
    }
}
