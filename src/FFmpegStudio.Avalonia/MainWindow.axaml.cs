using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.AddDropHandler(this, WindowDrop);
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private EditorViewModel? attached;

    private void AttachViewModel()
    {
        if (DataContext is not EditorViewModel vm || ReferenceEquals(attached, vm)) return;
        attached = vm;
        vm.PreparePlaybackSurface = PreparePlaybackAsync;
        vm.EditSettingsRequested = draft => StudioDialogs.EditSettingsAsync(this, draft);
        vm.EditExpertRequested = (encoder, current) => StudioDialogs.EditExpertAsync(this, encoder, current);
        vm.TestEncodeNoticeRequested = job => StudioDialogs.TestEncodeNoticeAsync(this, job);
        vm.ComparisonRequested = session => StudioDialogs.ShowComparisonAsync(this, session, () => vm.ProbeComparisonFileAsync());
        vm.AnalysisRequested = result => StudioDialogs.ShowAnalysisAsync(this, result);
        vm.ThemeChanged += _ => (global::Avalonia.Application.Current as App)?.ApplyTheme(vm.Settings);
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(EditorViewModel.PreviewPath))
                Dispatcher.UIThread.Post(ShowPreview);
        };
    }

    private async void OpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
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
        if (DataContext is not EditorViewModel vm) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is { Count: > 0 }) await vm.ImportPathsAsync(paths);
    }

    private Task PreparePlaybackAsync()
    {
        if (DataContext is not EditorViewModel vm || vm.Playback is not AvaloniaPlaybackSession session) return Task.CompletedTask;
        if (VideoHost.Content is VlcVideoView existing && ReferenceEquals(existing.Session, session)) return Task.CompletedTask;
        VideoHost.Content = new VlcVideoView { Session = session, Width = VideoHost.Bounds.Width, Height = VideoHost.Bounds.Height };
        return Task.CompletedTask;
    }

    private void ShowPreview()
    {
        PreviewImage.Source = PreviewBitmaps.Load(DataContext is EditorViewModel vm ? vm.PreviewPath : null);
    }
}
