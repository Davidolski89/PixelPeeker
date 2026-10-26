using System.Windows.Controls;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public partial class ThemePickerControl : UserControl
{
    public event Action<AppTheme>? ThemeSelected;
    public ThemePickerControl(string themeId)
    {
        InitializeComponent();
        ThemePreviewHelp.SetResourceReference(TextBlock.TextProperty,L.ResourceKey("Die Auswahl wird sofort als Vorschau angezeigt. Speichern übernimmt das Farbschema; Abbrechen stellt die vorherige Auswahl wieder her."));
        ThemeSelector.ItemsSource=ThemeCatalog.All;
        ThemeSelector.SelectedValue=ThemeCatalog.Resolve(themeId).Id;
    }
    private void ThemeSelector_SelectionChanged(object sender,SelectionChangedEventArgs e)
    { if(ThemeSelector.SelectedItem is AppTheme theme)ThemeSelected?.Invoke(theme); }
}
