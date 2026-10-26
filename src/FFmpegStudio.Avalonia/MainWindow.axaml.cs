using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.AddDropHandler(this, WindowDrop);
    }

    private async void OpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("Videos importieren"),
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(L.T("Videos importieren"))
                    {
                        Patterns = ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.m2ts", "*.mts", "*.ts", "*.m4v", "*.wmv", "*.mpg", "*.mpeg"]
                    },
                    FilePickerFileTypes.All
                ]
            });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
            if (files.Count > 0 && paths.Count == 0)
            {
                vm.StatusText = L.T("Die ausgewählte Datei hat keinen lokalen Pfad.");
                return;
            }
            if (paths.Count > 0) await vm.ImportPathsAsync(paths);
        }
        catch (Exception ex)
        {
            vm.StatusText = ex.Message;
        }
    }

    private async void WindowDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is { Count: > 0 }) await vm.ImportPathsAsync(paths);
    }
}
