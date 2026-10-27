using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.AddDropHandler(this, WindowDrop);
        DragDrop.SetAllowDrop(QueueGrid, true);
        QueueGrid.AddHandler(InputElement.PointerPressedEvent, QueuePressed, RoutingStrategies.Tunnel);
        DragDrop.AddDragOverHandler(QueueGrid, QueueDragOver);
        DragDrop.AddDropHandler(QueueGrid, QueueDrop);
        foreach (var slider in this.GetVisualDescendants().OfType<Slider>().Where(slider => slider.Classes.Contains("point")))
            MoveSliderToPoint(slider);
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
        if (DataContext is not EditorViewModel vm || !vm.CanCopyCommand || string.IsNullOrWhiteSpace(vm.DisplayedCommandText)) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.DisplayedCommandText);
    }

    private void ApplyHints()
    {
        if (DataContext is EditorViewModel vm) AvaloniaHints.Apply(this, vm.Settings);
    }

    private async void CompareSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
        var selected = QueueGrid.SelectedItems?.OfType<EncodingJob>()
            .Where(job => job.Status == JobStatus.Abgeschlossen && job.OutputInfo?.Video is not null)
            .Take(2)
            .ToArray() ?? [];
        await vm.OpenComparisonAsync(selected);
    }

    private void PreviewTimelineKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel vm || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right or Key.Space)) return;
        e.Handled = true;
        var command = e.Key == Key.Space
            ? vm.PlaybackActive ? vm.PauseCommand : vm.PlayCommand
            : vm.PlaybackActive
                ? e.Key == Key.Left ? vm.SeekBackwardCommand : vm.SeekForwardCommand
                : e.Key == Key.Left ? vm.PreviousFrameCommand : vm.NextFrameCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void QueueLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Index % 2 != 1) return;
        if (global::Avalonia.Application.Current?.TryFindResource("Raised", null, out var brush) == true && brush is IBrush raised)
            e.Row.Background = raised;
    }

    private static readonly DataFormat<EncodingJob> JobDrag = DataFormat.CreateInProcessFormat<EncodingJob>("pixelpeeker-job");

    private async void QueuePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(QueueGrid).Properties.IsLeftButtonPressed) return;
        if (RowAt(e.Source)?.DataContext is not EncodingJob job) return;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(JobDrag, job));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void QueueDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(JobDrag)) return;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void QueueDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not EditorViewModel vm || e.DataTransfer.TryGetValue(JobDrag) is not EncodingJob job) return;
        e.Handled = true;
        var target = RowAt(e.Source)?.DataContext as EncodingJob;
        await vm.MoveJobAsync(job, target is null ? Math.Max(0, vm.Jobs.Count - 1) : vm.Jobs.IndexOf(target));
    }

    private static DataGridRow? RowAt(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is DataGridRow row) return row;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private static void MoveSliderToPoint(Slider slider)
    {
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Thumb || !e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) return;
            var span = Math.Max(1, slider.Bounds.Width - 14);
            var ratio = Math.Clamp((e.GetPosition(slider).X - 7) / span, 0, 1);
            var value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
            if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0)
                value = slider.Minimum + Math.Round((value - slider.Minimum) / slider.TickFrequency) * slider.TickFrequency;
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }, RoutingStrategies.Tunnel, true);
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
