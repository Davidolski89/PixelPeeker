using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
        vm.ThemeChanged += _ => (global::Avalonia.Application.Current as App)?.ApplyTheme(vm.Settings);
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(EditorViewModel.PreviewPath))
                Dispatcher.UIThread.Post(ShowPreview);
            if (change.PropertyName == nameof(EditorViewModel.AudioTracks))
                Dispatcher.UIThread.Post(RebuildAudio);
            if (change.PropertyName is nameof(EditorViewModel.PhaseNote) or nameof(EditorViewModel.Settings))
                Dispatcher.UIThread.Post(ApplyHints);
        };
        Opened += (_, _) => { RebuildAudio(); ApplyHints(); };
        RebuildAudio();
        ApplyHints();
    }

    private AudioTrackOptions[]? shownAudio;
    private bool bindingAudio;

    private void RebuildAudio()
    {
        if (DataContext is not EditorViewModel vm) return;
        var tracks = vm.AudioTracks.ToArray();
        if (shownAudio is not null && shownAudio.SequenceEqual(tracks)) return;
        shownAudio = tracks;
        if (tracks.Length == 0)
        {
            AudioHost.Content = new TextBlock { Text = L.T("Eine Videodatei auswählen."), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
            ApplyHints();
            return;
        }
        var picker = new ComboBox { ItemsSource = tracks, SelectedItem = vm.SelectedAudio ?? tracks[0] };
        picker.ItemTemplate = new FuncDataTemplate<AudioTrackOptions>((track, _) => new TextBlock { Text = track?.SourceSummary ?? "" });
        var action = new ComboBox { ItemsSource = AudioTrackEditing.Actions };
        action.ItemTemplate = new FuncDataTemplate<TrackAction>((value, _) => new TextBlock { Text = L.EnumText(value) });
        var encoder = new ComboBox { ItemsSource = AudioTrackEditing.Encoders };
        var bitrate = new NumericUpDown { Minimum = 0, Maximum = 1536, Increment = 32, FormatString = "0", Width = 120 };
        void Show(AudioTrackOptions? track)
        {
            bindingAudio = true;
            try
            {
                vm.SelectedAudio = track;
                var enabled = track is not null;
                action.IsEnabled = enabled;
                encoder.IsVisible = track?.CanEncode == true;
                bitrate.IsVisible = track?.CanSetBitrate == true;
                if (track is null) return;
                action.SelectedItem = AudioTrackEditing.Actions.Contains(track.Action) ? track.Action : TrackAction.Kopieren;
                encoder.SelectedItem = AudioTrackEditing.Encoders.Contains(track.Encoder) ? track.Encoder : AudioTrackEditing.Encoders[0];
                bitrate.Value = track.BitRateKbps;
            }
            finally { bindingAudio = false; }
        }
        picker.SelectionChanged += (_, _) => Show(picker.SelectedItem as AudioTrackOptions);
        action.SelectionChanged += (_, _) =>
        {
            if (bindingAudio || vm.SelectedAudio is not { } track || action.SelectedItem is not TrackAction value) return;
            track.Action = value;
            Show(track);
        };
        encoder.SelectionChanged += (_, _) =>
        {
            if (bindingAudio || vm.SelectedAudio is not { } track || encoder.SelectedItem is not string value) return;
            track.Encoder = value;
            Show(track);
        };
        bitrate.ValueChanged += (_, _) =>
        {
            if (bindingAudio || vm.SelectedAudio is not { } track) return;
            track.BitRateKbps = (int)(bitrate.Value ?? track.BitRateKbps);
        };
        AudioHost.Content = new StackPanel
        {
            Spacing = 6,
            Children = { picker, action, encoder, bitrate }
        };
        Show(picker.SelectedItem as AudioTrackOptions);
        ApplyHints();
    }

    private void ApplyHints()
    {
        if (DataContext is EditorViewModel vm) AvaloniaHints.Apply(this, vm.Settings);
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
