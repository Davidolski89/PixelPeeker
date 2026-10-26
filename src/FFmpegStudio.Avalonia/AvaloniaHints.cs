using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FFmpegStudio.Core;

namespace FFmpegStudio.Avalonia;

public static class AvaloniaHints
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["OpenFileButton"] = "ImportFiles",
        ["SettingsButton"] = "EditSettings",
        ["CompareButton"] = "CompareSelectedEncodes",
        ["PlayButton"] = "PlaySource",
        ["PauseButton"] = "PauseSource",
        ["StopButton"] = "StopSource",
        ["AddQueueButton"] = "AddQueue",
        ["StartQueueButton"] = "StartQueue",
        ["ResultsButton"] = "CompareResults",
        ["AnalyzeButton"] = "AnalyzeQuality",
        ["ExpertButton"] = "ExpertOptions",
        ["TestEncodeButton"] = "TestEncode"
    };

    public static void Apply(Control root, AppSettings settings)
    {
        var delay = TooltipTiming.Milliseconds(settings.TooltipDelaySeconds);
        var controls = root.GetLogicalDescendants().OfType<Control>()
            .Concat(root.GetVisualDescendants().OfType<Control>())
            .Append(root)
            .Distinct();
        foreach (var control in controls)
        {
            if (control is not (Button or CheckBox or ComboBox or NumericUpDown or ListBox or TextBox or TabItem)) continue;
            if (!settings.TooltipsEnabled)
            {
                ToolTip.SetTip(control, null);
                continue;
            }
            var text = Tip(control);
            if (string.IsNullOrWhiteSpace(text)) continue;
            ToolTip.SetTip(control, text);
            ToolTip.SetShowDelay(control, delay);
        }
    }

    private static string Tip(Control control)
    {
        if (control.Name is { Length: > 0 } name && Named.TryGetValue(name, out var key) && ButtonHelpCatalog.Text(key) is { Length: > 0 } named)
            return named;
        var label = control switch
        {
            ContentControl content when content.Content is string text => text,
            TabItem tab when tab.Header is string header => header,
            _ => ""
        };
        if (label.Length == 0) return "";
        var fromLabel = ButtonHelpCatalog.KeyForLabel(label);
        if (fromLabel is not null)
        {
            var catalog = ButtonHelpCatalog.Text(fromLabel);
            if (catalog.Length > 0) return catalog;
        }
        return label;
    }
}
