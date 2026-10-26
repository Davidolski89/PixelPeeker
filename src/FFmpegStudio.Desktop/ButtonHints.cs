using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public static partial class ButtonHints
{
    private sealed partial class Preferences : ObservableObject
    {
        [ObservableProperty] private bool enabled = true;
        [ObservableProperty] private int delayMilliseconds = 2000;
    }
    private static readonly Preferences preferences = new();
    private static readonly HashSet<ToolTip> openTooltips = [];
    private static bool initialized;
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key",typeof(string),typeof(ButtonHints),new PropertyMetadata(null,KeyChanged));
    public static readonly DependencyProperty UsePreferencesProperty = DependencyProperty.RegisterAttached("UsePreferences",typeof(bool),typeof(ButtonHints),new PropertyMetadata(false,(target,e)=>
    {if(target is FrameworkElement element&&e.NewValue is true){Initialize();Configure(element);}}));
    private static readonly DependencyProperty ConfiguredProperty = DependencyProperty.RegisterAttached("Configured",typeof(bool),typeof(ButtonHints),new PropertyMetadata(false));
    public static void SetKey(DependencyObject target,string? value)=>target.SetValue(KeyProperty,value);
    public static string? GetKey(DependencyObject target)=>(string?)target.GetValue(KeyProperty);
    public static void SetUsePreferences(DependencyObject target,bool value)=>target.SetValue(UsePreferencesProperty,value);
    public static bool GetUsePreferences(DependencyObject target)=>(bool)target.GetValue(UsePreferencesProperty);
    public static void Initialize()
    {
        if(initialized)return;initialized=true;
        EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.OpenedEvent,new RoutedEventHandler((sender,_)=>
        {if(sender is ToolTip tooltip){if(preferences.Enabled)openTooltips.Add(tooltip);else tooltip.IsOpen=false;}}));
        EventManager.RegisterClassHandler(typeof(ToolTip),ToolTip.ClosedEvent,new RoutedEventHandler((sender,_)=>
        {if(sender is ToolTip tooltip)openTooltips.Remove(tooltip);}));
    }
    public static void Apply(AppSettings settings)
    {
        Initialize();preferences.DelayMilliseconds=TooltipTiming.Milliseconds(settings.TooltipDelaySeconds);
        preferences.Enabled=settings.TooltipsEnabled;
        if(!preferences.Enabled)foreach(var tooltip in openTooltips.ToArray())tooltip.IsOpen=false;
    }
    private static void KeyChanged(DependencyObject target,DependencyPropertyChangedEventArgs e)
    {
        if(target is not FrameworkElement element || e.NewValue is not string key)return;
        Initialize();Configure(element);
        var binding=new MultiBinding{Converter=new HelpConverter(),Mode=BindingMode.OneWay};
        binding.Bindings.Add(new Binding{Source=key});
        binding.Bindings.Add(new Binding(nameof(LanguageContext.Language)){Source=LanguageContext.Instance});
        BindingOperations.SetBinding(element,FrameworkElement.ToolTipProperty,binding);
    }
    private static void Configure(FrameworkElement element)
    {
        if((bool)element.GetValue(ConfiguredProperty))return;
        element.SetValue(ConfiguredProperty,true);
        element.SetBinding(ToolTipService.IsEnabledProperty,new Binding(nameof(Preferences.Enabled)){Source=preferences,Mode=BindingMode.OneWay});
        element.SetBinding(ToolTipService.InitialShowDelayProperty,new Binding(nameof(Preferences.DelayMilliseconds)){Source=preferences,Mode=BindingMode.OneWay});
        ToolTipService.SetBetweenShowDelay(element,0);
        ToolTipService.SetShowDuration(element,60000);
        ToolTipService.SetShowOnDisabled(element,true);
    }
    private sealed class HelpConverter : IMultiValueConverter
    {
        public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)=>values[0] is string key?ButtonHelpCatalog.Text(key):"";
        public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
