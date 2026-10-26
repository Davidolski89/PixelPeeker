using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public sealed class AvaloniaEditorPrompts : IEditorPrompts
{
    public Func<Window?> Owner { get; set; } = () => null;

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var window = Owner() ?? throw new InvalidOperationException(L.T("Kein Fenster für die Rückfrage."));
        var accepted = false;
        var dialog = Dialog(title, 440, 220);
        var yes = new Button { Content = L.T("Ja"), MinWidth = 88 };
        var no = new Button { Content = L.T("Nein"), MinWidth = 88 };
        yes.Click += (_, _) => { accepted = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = Stack(message, yes, no);
        await dialog.ShowDialog(window);
        return accepted;
    }

    public async Task<string?> AskAsync(string title, string label, string initial)
    {
        var window = Owner() ?? throw new InvalidOperationException(L.T("Kein Fenster für die Eingabe."));
        string? value = null;
        var dialog = Dialog(title, 440, 220);
        var input = new TextBox { Text = initial, PlaceholderText = label };
        var ok = new Button { Content = L.T("OK"), MinWidth = 88 };
        var cancel = new Button { Content = L.T("Abbrechen"), MinWidth = 88 };
        ok.Click += (_, _) => { value = input.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 12,
            Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input, Buttons(ok, cancel) }
        };
        await dialog.ShowDialog(window);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task<IReadOnlyList<string>?> PickOpenFilesAsync()
    {
        var storage = Owner()?.StorageProvider ?? throw new InvalidOperationException(L.T("Dateiauswahl ist nicht verfügbar."));
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Videos importieren"),
            AllowMultiple = true,
            FileTypeFilter = [VideoType(), FilePickerFileTypes.All]
        });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        return paths.Count == 0 ? null : paths;
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        var storage = Owner()?.StorageProvider ?? throw new InvalidOperationException(L.T("Dateiauswahl ist nicht verfügbar."));
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = Owner()?.StorageProvider ?? throw new InvalidOperationException(L.T("Ordnerauswahl ist nicht verfügbar."));
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        var storage = Owner()?.StorageProvider ?? throw new InvalidOperationException(L.T("Dateiauswahl ist nicht verfügbar."));
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType VideoType() => new(L.T("Videos importieren"))
    {
        Patterns = ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.m2ts", "*.mts", "*.ts", "*.m4v", "*.wmv", "*.mpg", "*.mpeg", "*.json"]
    };

    private static Window Dialog(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static StackPanel Stack(string message, Button first, Button second) => new()
    {
        Margin = new global::Avalonia.Thickness(16),
        Spacing = 12,
        Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, Buttons(first, second) }
    };

    private static StackPanel Buttons(Button first, Button second) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
        Children = { first, second }
    };
}
