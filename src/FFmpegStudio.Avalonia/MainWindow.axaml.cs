using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private EditorViewModel? attached;

    private void AttachViewModel()
    {
        if (DataContext is not EditorViewModel vm || ReferenceEquals(attached, vm)) return;
        attached = vm;
        vm.PreparePlaybackSurface = PreparePlaybackAsync;
        vm.EditExpertRequested = (encoder, current) => StudioDialogs.EditExpertAsync(this, encoder, current);
        vm.TestEncodeNoticeRequested = job => StudioDialogs.TestEncodeNoticeAsync(this, job);
        vm.ComparisonRequested = session => StudioDialogs.ShowComparisonAsync(this, session, () => vm.ProbeComparisonFileAsync());
        vm.AnalysisRequested = result => StudioDialogs.ShowAnalysisAsync(this, result);
        vm.EditSettingsRequested = draft => StudioDialogs.EditSettingsAsync(this, draft, vm.SelectedMedia?.Info, vm.SelectedMedia?.Options);
        vm.CompletionRequested = action => action is CompletionAction.Energiesparen or CompletionAction.Herunterfahren or CompletionAction.AnwendungSchließen or CompletionAction.Benachrichtigung
            ? StudioDialogs.ConfirmCountdownAsync(this, action)
            : Task.FromResult(true);
        vm.ResultsRequested = jobs => StudioDialogs.ShowResultsAsync(this, jobs);
        vm.CloseRequested = Close;
        vm.ManageFfmpegRequested = () => StudioDialogs.ManageFfmpegAsync(this, vm);
        vm.EditTextRequested = (title, initial) => StudioDialogs.EditTextAsync(this, title, initial);
        vm.TextWindowRequested = (title, text) => StudioDialogs.ShowTextAsync(this, title, text);
        vm.RevealPath = path =>
        {
            EditorViewModel.Reveal(path);
            if (!Directory.Exists(path) && !File.Exists(path))
                vm.StatusText = L.T("Der Ordner ist noch nicht vorhanden.");
        };
        vm.ThemeChanged += _ =>
        {
            (global::Avalonia.Application.Current as App)?.ApplyTheme(vm.Settings);
            UiText.Current.Refresh();
        };
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(EditorViewModel.PreviewPath))
                global::Avalonia.Threading.Dispatcher.UIThread.Post(ShowPreview);
            if (change.PropertyName is nameof(EditorViewModel.PhaseNote) or nameof(EditorViewModel.Settings))
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UiText.Current.Refresh();
                    ApplyHints();
                    UpdateWorkspaceSummary();
                });
        };
        Opened += (_, _) =>
        {
            ApplyHints();
            UpdateWorkspaceSummary();
        };
        ApplyHints();
    }

    private void MainTabsChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl) UpdateWorkspaceSummary();
    }

    private void UpdateWorkspaceSummary()
    {
        if (MainPreviewTabs is null || ConversionSummaryCard is null || ValidationSummaryCard is null) return;
        ConversionSummaryCard.IsVisible = ReferenceEquals(MainPreviewTabs.SelectedItem, EncodeSettingsTab) && Bounds.Height >= 850;
        ValidationSummaryCard.IsVisible = ReferenceEquals(MainPreviewTabs.SelectedItem, PreviewTab) || ReferenceEquals(MainPreviewTabs.SelectedItem, EncodeSettingsTab);
        if (PresetSelector is not null) PresetSelector.Width = Bounds.Width < 1300 ? 180 : 280;
    }

    private async void AddSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
        await vm.AddManyAsync(MediaList.SelectedItems?.OfType<EditorMedia>().ToArray() ?? []);
    }

    private void ApplySelectedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
        vm.ApplyToMany(MediaList.SelectedItems?.OfType<EditorMedia>().ToArray() ?? []);
    }

    private void StartSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
        vm.StartSelected(QueueGrid.SelectedItems?.OfType<EncodingJob>().ToArray() ?? []);
    }

    private void ExportTrackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm || sender is not Button { DataContext: SubtitleTrackOptions track }) return;
        vm.ExportSubtitleCommand.Execute(track);
    }

    private async void CopyCommandClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm || string.IsNullOrWhiteSpace(vm.CommandText)) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.CommandText);
    }

    private void ApplyHints()
    {
        if (DataContext is EditorViewModel vm) AvaloniaHints.Apply(this, vm.Settings);
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
