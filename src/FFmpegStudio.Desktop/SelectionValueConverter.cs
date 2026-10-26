using System.Globalization;
using System.Windows.Data;

namespace FFmpegStudio.Desktop;

/// <summary>Collection refreshes can clear a WPF selector without a user choosing a new value.</summary>
public sealed class SelectionValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value;
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) => value ?? Binding.DoNothing;
}
