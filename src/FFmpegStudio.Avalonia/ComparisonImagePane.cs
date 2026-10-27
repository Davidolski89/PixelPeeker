using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FFmpegStudio.Avalonia;

/// <summary>Fit or pixel zoom. Each pane keeps its own scroll host so the comparison window can synchronize them.</summary>
public sealed class ComparisonImagePane : Border
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ComparisonImagePane, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ComparisonImagePane, double>(nameof(Zoom));

    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<ComparisonImagePane, string?>(nameof(EmptyMessage));

    private readonly Image image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
    private readonly TextBlock empty = new()
    {
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        Margin = new Thickness(16),
        IsHitTestVisible = false
    };
    private Point? drag;
    private double dragX;
    private double dragY;

    public ScrollViewer Scroll { get; } = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public ComparisonImagePane()
    {
        Background = Brushes.Black;
        ClipToBounds = true;
        MinHeight = 70;
        Scroll.Content = image;
        var content = new Grid();
        content.Children.Add(Scroll);
        content.Children.Add(empty);
        Child = content;
        Scroll.SizeChanged += (_, _) => UpdateImage();
        Scroll.PointerPressed += (_, e) =>
        {
            if (Zoom <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            drag = e.GetPosition(this);
            dragX = Scroll.Offset.X;
            dragY = Scroll.Offset.Y;
            e.Pointer.Capture(Scroll);
            e.Handled = true;
        };
        Scroll.PointerMoved += (_, e) =>
        {
            if (drag is not { } start || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(this) - start;
            Scroll.Offset = new Vector(dragX - delta.X, dragY - delta.Y);
        };
        Scroll.PointerReleased += (_, _) => drag = null;
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == ZoomProperty || change.Property == EmptyMessageProperty)
            UpdateImage();
    }

    private void UpdateImage()
    {
        image.Source = Source;
        empty.Text = EmptyMessage;
        empty.IsVisible = Source is null && !string.IsNullOrWhiteSpace(EmptyMessage);
        if (Source is not { } bitmap)
        {
            image.Width = image.Height = double.NaN;
            image.Stretch = Stretch.Uniform;
            return;
        }
        if (Zoom <= 0)
        {
            image.Stretch = Stretch.Uniform;
            image.Width = image.Height = double.NaN;
            return;
        }
        image.Stretch = Stretch.None;
        image.Width = bitmap.PixelSize.Width * Zoom;
        image.Height = bitmap.PixelSize.Height * Zoom;
    }
}
