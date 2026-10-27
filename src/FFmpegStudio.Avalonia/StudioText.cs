using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using FFmpegStudio.Core;

namespace FFmpegStudio.Avalonia;

public sealed class UiText : INotifyPropertyChanged
{
    public static UiText Current { get; } = new();
    public int Stamp { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        Stamp++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Stamp)));
    }
}

public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string text) => Text = text;
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget target
            || target.TargetObject is not AvaloniaObject targetObject
            || target.TargetProperty is not AvaloniaProperty targetProperty)
            return L.T(Text);
        targetObject.Bind(targetProperty, new Binding(nameof(UiText.Stamp))
        {
            Source = UiText.Current,
            Converter = new SourceConverter(Text)
        });
        return targetObject.GetValue(targetProperty) ?? L.T(Text);
    }

    private sealed class SourceConverter(string source) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => L.T(source);
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum item ? L.EnumText(item) : value?.ToString() ?? "";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
