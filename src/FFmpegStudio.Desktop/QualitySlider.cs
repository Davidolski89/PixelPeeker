using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

/// <summary>Keeps Slider's range coercion from overwriting stored values during codec/preset changes.</summary>
public sealed class QualitySlider : Slider
{
    public static readonly DependencyProperty QualityValueProperty = DependencyProperty.Register(
        nameof(QualityValue), typeof(double), typeof(QualitySlider),
        new FrameworkPropertyMetadata(23d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnInputChanged));
    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
        nameof(Range), typeof(QualityRange), typeof(QualitySlider),
        new PropertyMetadata(new QualityRange(0, 51, .5), OnInputChanged));
    private bool synchronizing;

    public double QualityValue { get => (double)GetValue(QualityValueProperty); set => SetValue(QualityValueProperty, value); }
    public QualityRange Range { get => (QualityRange)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }

    public QualitySlider()
    {
        IsSnapToTickEnabled = true; IsMoveToPointEnabled = true;
        Synchronize();
    }

    private static void OnInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((QualitySlider)sender).Synchronize();
    private void Synchronize()
    {
        synchronizing = true;
        try
        {
            var range = Range;
            Minimum = range.Minimum; Maximum = range.Maximum;
            TickFrequency = SmallChange = range.Step; LargeChange = Math.Max(range.Step, (range.Maximum - range.Minimum) / 10);
            Value = double.IsFinite(QualityValue) ? Math.Clamp(QualityValue, Minimum, Maximum) : Minimum;
        }
        finally { synchronizing = false; }
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (!synchronizing) SetCurrentValue(QualityValueProperty, newValue);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        // A click on the already clamped endpoint must also repair an invalid imported value.
        if (IsEnabled && QualityValue != Value) SetCurrentValue(QualityValueProperty, Value);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsEnabled && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            SetCurrentValue(QualityValueProperty, Value);
    }
}
