using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public sealed class LanguageContext : INotifyPropertyChanged
{
    public static LanguageContext Instance { get; } = new();
    public string Language => L.Language;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Language)));
}

/// <summary>Retranslates displayed enum/preset labels without replacing their actual bound values.</summary>
public sealed class TranslatedValueExtension : MarkupExtension
{
    public string Path { get; set; } = ".";
    public string? Format { get; set; }
    public string NullText { get; set; } = "";
    internal static MultiBinding ForText(string text)
    {
        var binding = new MultiBinding { Converter = new DisplayTextConverter(), ConverterParameter = ((string?)null,""), Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding { Source = text });
        binding.Bindings.Add(new Binding(nameof(LanguageContext.Language)) { Source = LanguageContext.Instance });
        return binding;
    }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = new DisplayTextConverter(), ConverterParameter = (Format,NullText), Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(new Binding(nameof(LanguageContext.Language)) { Source = LanguageContext.Instance });
        if(Format=="VideoRateControl")binding.Bindings.Add(new Binding("DataContext.SelectedMedia.Options.Video.Encoder")
            {RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Window),1)});
        return binding.ProvideValue(serviceProvider);
    }
    private sealed class DisplayTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var value = values[0];
            var (format,nullText) = ((string?,string))parameter;
            if(format=="VideoRateControl"&&value is RateControl mode)
                return VideoPresentation.RateControlLabel(mode,values.Length>2?values[2] as string:null);
            if(format=="VideoEncoder"&&value is EncoderInfo encoder)return VideoPresentation.EncoderLabel(encoder);
            if ((value is null || value == DependencyProperty.UnsetValue) && nullText.Length>0) return L.T(nullText);
            var text = value == DependencyProperty.UnsetValue ? "" : value is Enum e ? L.EnumText(e) : L.T(value?.ToString() ?? "");
            var pattern=format switch { "Percent"=>"{0:0.0} %", "Preset"=>L.T("Preset: {0}"), _=>format is null?null:L.T(format) };
            return pattern is not null ? string.Format(CultureInfo.CurrentCulture, pattern, value is IFormattable && value is not Enum ? value : text) : text;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
    public static IReadOnlyList<LanguageOption> All { get; } = [new("de", "Deutsch"), new("en", "English"), new("pl", "Polski"),new("fr","Français"),new("es","Español"),new("tr","Türkçe"),new("zh","简体中文"),new("ru","Русский"),new("ja","日本語")];
}
