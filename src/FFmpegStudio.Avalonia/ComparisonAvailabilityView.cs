using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FFmpegStudio.Core;

namespace FFmpegStudio.Avalonia;

public sealed class ComparisonAvailabilityView : Control
{
    public static readonly StyledProperty<IReadOnlyList<ComparisonInterval>?> IntervalsProperty =
        AvaloniaProperty.Register<ComparisonAvailabilityView, IReadOnlyList<ComparisonInterval>?>(nameof(Intervals));

    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<ComparisonAvailabilityView, double>(nameof(Duration), 1);

    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<ComparisonAvailabilityView, double>(nameof(Position));

    static ComparisonAvailabilityView()
    {
        AffectsRender<ComparisonAvailabilityView>(IntervalsProperty, DurationProperty, PositionProperty);
    }

    public ComparisonAvailabilityView()
    {
        Height = 12;
        MinHeight = 12;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
    }

    public IReadOnlyList<ComparisonInterval>? Intervals
    {
        get => GetValue(IntervalsProperty);
        set => SetValue(IntervalsProperty, value);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public event Action<double>? SeekRequested;

    public double TimeAt(double x) => Bounds.Width > 0 && double.IsFinite(Duration) && Duration > 0
        ? Math.Clamp(x / Bounds.Width, 0, 1) * Duration
        : 0;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var time = TimeAt(e.GetPosition(this).X);
        var interval = Intervals?.FirstOrDefault(item => time >= item.Start && time < item.End) ?? Intervals?.LastOrDefault();
        ToolTip.SetTip(this, interval is null
            ? L.T("Schnittzuordnung unbekannt")
            : $"{TimeSpan.FromSeconds(interval.Start):hh\\:mm\\:ss\\.fff} – {TimeSpan.FromSeconds(interval.End):hh\\:mm\\:ss\\.fff}\n" +
              L.T(interval.Availability switch
              {
                  ComparisonAvailability.Available => "Im Encode vorhanden",
                  ComparisonAvailability.Unavailable => "Entfernt / außerhalb des Ausschnitts",
                  _ => "Schnittzuordnung unbekannt"
              }));
        SeekRequested?.Invoke(time);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (Duration <= 0 || !double.IsFinite(Duration) || width <= 0 || height <= 0) return;
        var available = new SolidColorBrush(Color.Parse("#70DBC1"));
        var border = new SolidColorBrush(Color.Parse("#FFCB7B"));
        context.FillRectangle(new SolidColorBrush(Color.Parse("#202A3A")), new Rect(0, 0, width, height));
        foreach (var segment in Intervals ?? [])
        {
            var x = Math.Clamp(segment.Start / Duration, 0, 1) * width;
            var end = Math.Clamp(segment.End / Duration, 0, 1) * width;
            var rect = new Rect(x, 0, Math.Max(0, end - x), height);
            if (rect.Width <= 0) continue;
            if (segment.Availability == ComparisonAvailability.Available)
            {
                context.FillRectangle(available, rect);
                continue;
            }
            using (context.PushClip(rect))
            {
                if (segment.Availability == ComparisonAvailability.Unavailable)
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(48, 165, 180, 201)), rect);
                    var pen = new Pen(border, 1);
                    for (var p = x - height; p < end; p += 7)
                        context.DrawLine(pen, new Point(p, height), new Point(p + height, 0));
                }
                else
                {
                    for (var p = x + 3; p < end; p += 7)
                        context.DrawEllipse(border, null, new Point(p, height / 2), 1, 1);
                }
            }
        }
        var marker = Math.Clamp(Position / Duration, 0, 1) * width;
        context.DrawLine(new Pen(Brushes.Black, 3), new Point(marker, 0), new Point(marker, height));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(marker, 0), new Point(marker, height));
    }
}
