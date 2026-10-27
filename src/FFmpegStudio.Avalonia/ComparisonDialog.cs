using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public static class ComparisonDialog
{
    public static async Task ShowAsync(Window owner, EncodeComparisonSession session, Func<Task<EncodeComparisonInput?>> openFile)
    {
        var dialog = StudioDialogs.CreateWindow(L.T("Vergleich"), 1500, 950, owner);
        var originalPane = new ComparisonImagePane();
        var paneA = new ComparisonImagePane();
        var paneB = new ComparisonImagePane();
        var focusPane = new ComparisonImagePane();
        var samples = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var stripA = new ComparisonAvailabilityView { Height = 12, Margin = new Thickness(8, 1, 8, 3) };
        var stripB = new ComparisonAvailabilityView { Height = 12, Margin = new Thickness(8, 1, 8, 3) };
        var status = new TextBlock { Foreground = Brush("Accent"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        var referenceNote = new TextBlock { Classes = { "muted" }, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        var focusLabel = new TextBlock { FontWeight = FontWeight.SemiBold, Foreground = Brush("Accent"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 4) };
        var headerA = Header("");
        var headerB = Header("");
        var headerOriginal = Header("");
        var messageA = Message();
        var messageB = Message();
        var messageOriginal = Message();
        var focusMessage = Message();
        Control columnB = new StackPanel();
        var originalColumn = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        var sideGrid = new UniformGrid { Columns = 2 };
        var singleGrid = new Grid { RowDefinitions = new RowDefinitions("38,*,47"), IsVisible = false };
        var syncing = false;

        void Paint()
        {
            var zoom = session.Zoom;
            originalPane.Source = PreviewBitmaps.Load(session.OriginalPath);
            paneA.Source = PreviewBitmaps.Load(session.PathA);
            paneB.Source = PreviewBitmaps.Load(session.PathB);
            var focusPath = session.FocusIndex == 2 && session.OriginalVisible ? session.OriginalPath : session.FocusIndex == 1 ? session.PathB : session.PathA;
            focusPane.Source = PreviewBitmaps.Load(focusPath);
            originalPane.Zoom = paneA.Zoom = paneB.Zoom = focusPane.Zoom = zoom;
            originalPane.EmptyMessage = session.OriginalVisible && string.IsNullOrWhiteSpace(session.OriginalPath) ? L.T("Referenzdatei auswählen, um die Referenz anzuzeigen.") : "";
            paneA.EmptyMessage = string.IsNullOrWhiteSpace(session.PathA) ? L.T("Frames laden") : "";
            paneB.EmptyMessage = string.IsNullOrWhiteSpace(session.PathB) ? L.T("Frames laden") : "";
            focusPane.EmptyMessage = session.FocusIndex == 2 ? originalPane.EmptyMessage : session.FocusIndex == 1 ? paneB.EmptyMessage : paneA.EmptyMessage;
            headerA.Text = "A · " + session.SelectedA?.Label;
            headerB.Text = "B · " + session.SelectedB?.Label;
            headerOriginal.Text = session.ReferenceLabel;
            messageA.Text = session.PathA is null ? "" : session.Status;
            messageB.Text = session.PathB is null ? "" : session.Status;
            messageOriginal.Text = originalPane.EmptyMessage;
            focusLabel.Text = session.FocusLabel;
            focusMessage.Text = focusPane.EmptyMessage;
            status.Text = session.Status;
            referenceNote.Text = session.ReferenceDescription;
            referenceNote.IsVisible = session.OriginalVisible;
            columnB.IsVisible = session.CompareTwoEncodes;
            stripB.IsVisible = session.CompareTwoEncodes;
            originalColumn.IsVisible = session.OriginalVisible;
            sideGrid.IsVisible = session.SideBySide;
            sideGrid.Columns = Math.Max(1, session.PanelCount);
            singleGrid.IsVisible = session.SingleVisible;
            stripA.Duration = stripB.Duration = session.Maximum;
            stripA.Position = stripB.Position = session.Position;
            stripA.Intervals = session.CoverageA;
            stripB.Intervals = session.CoverageB;
            stripA.InvalidateVisual();
            stripB.InvalidateVisual();
        }

        void Track(ScrollViewer source)
        {
            if (syncing) return;
            syncing = true;
            foreach (var other in new[] { originalPane.Scroll, paneA.Scroll, paneB.Scroll, focusPane.Scroll })
            {
                if (ReferenceEquals(other, source)) continue;
                var x = source.Extent.Width > source.Viewport.Width ? source.Offset.X / (source.Extent.Width - source.Viewport.Width) : 0;
                var y = source.Extent.Height > source.Viewport.Height ? source.Offset.Y / (source.Extent.Height - source.Viewport.Height) : 0;
                other.Offset = new Vector(x * Math.Max(0, other.Extent.Width - other.Viewport.Width), y * Math.Max(0, other.Extent.Height - other.Viewport.Height));
            }
            syncing = false;
        }
        foreach (var pane in new[] { originalPane, paneA, paneB, focusPane })
            pane.Scroll.ScrollChanged += (_, _) => Track(pane.Scroll);

        void RebuildSamples()
        {
            samples.Children.Clear();
            foreach (var shot in session.Samples)
            {
                var current = shot;
                var button = new Button
                {
                    Content = new StackPanel
                    {
                        Margin = new Thickness(4, 0),
                        Children =
                        {
                            new Image { Source = PreviewBitmaps.Load(shot.PathA ?? shot.OriginalPath), Width = 110, Height = 62, Stretch = Stretch.Uniform },
                            new TextBlock { Text = shot.Label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
                        }
                    }
                };
                button.Click += async (_, _) => { session.SelectSample(current); await session.LoadNearestAsync(); Paint(); };
                samples.Children.Add(button);
            }
        }

        session.PropertyChanged += (_, _) => DispatcherPost(Paint);
        session.Samples.CollectionChanged += (_, _) => DispatcherPost(RebuildSamples);

        var choiceA = new ComboBox { ItemsSource = session.Choices, SelectedItem = session.SelectedA };
        var choiceB = new ComboBox { ItemsSource = session.Choices, SelectedItem = session.SelectedB };
        choiceA.SelectionChanged += (_, _) => session.SelectedA = choiceA.SelectedItem as EncodeComparisonChoice;
        choiceB.SelectionChanged += (_, _) => session.SelectedB = choiceB.SelectedItem as EncodeComparisonChoice;
        var mode = new ComboBox { Width = 300, ItemsSource = session.ModeChoices, SelectedItem = session.ModeChoices.First(item => item.Value == session.Mode) };
        mode.SelectionChanged += (_, _) => { if (mode.SelectedItem is ComparisonModeOption option) session.Mode = option.Value; };
        var matched = new CheckBox { Content = L.T("Referenz an Encode A anpassen"), IsChecked = session.MatchReference, IsEnabled = session.CanMatchReference, Margin = new Thickness(0, 0, 12, 0) };
        matched.IsCheckedChanged += (_, _) => session.MatchReference = matched.IsChecked == true;
        var position = new TextBox { Text = session.Position.ToString("F3"), Width = 76, Margin = new Thickness(0, 0, 6, 3) };
        position.LostFocus += async (_, _) =>
        {
            if (double.TryParse(position.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var seconds)
                || double.TryParse(position.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                session.Position = seconds;
                await session.LoadNearestAsync();
            }
        };
        var offsetA = OffsetBox(session.OffsetA, session.CanOffsetA, value => session.OffsetA = value);
        var offsetB = OffsetBox(session.OffsetB, session.CanOffsetB, value => session.OffsetB = value);
        var zoom = new ComboBox { Width = 99, ItemsSource = session.ZoomChoices, SelectedItem = session.ZoomChoices.First(item => item.Value == session.Zoom) };
        zoom.SelectionChanged += (_, _) => { if (zoom.SelectedItem is ComparisonZoomOption option) session.Zoom = option.Value; };
        var focus = new ComboBox { Width = 240, Height = 32, Margin = new Thickness(0, 0, 0, 4) };
        focus.Items.Add(new ComboBoxItem { Content = L.T("Encode A") });
        focus.Items.Add(new ComboBoxItem { Content = L.T("Encode B"), IsEnabled = session.CompareTwoEncodes });
        focus.Items.Add(new ComboBoxItem { Content = session.ReferenceLabel, IsEnabled = session.OriginalVisible });
        focus.SelectedIndex = session.FocusIndex;
        focus.SelectionChanged += (_, _) => { if (focus.SelectedIndex >= 0) session.FocusIndex = focus.SelectedIndex; };

        var openA = new Button { Content = L.T("Datei A …"), Padding = new Thickness(8, 4) };
        var openB = new Button { Content = L.T("Datei B …"), Padding = new Thickness(8, 4) };
        var openOriginal = new Button { Content = L.T("Original wählen …"), Padding = new Thickness(8, 4), IsVisible = session.OriginalVisible };
        async Task Open(int slot)
        {
            try
            {
                var input = await openFile();
                if (input is null) return;
                session.AddExternal(input);
                if (slot == 0) choiceA.SelectedItem = session.Choices.LastOrDefault();
                else if (slot == 1) choiceB.SelectedItem = session.Choices.LastOrDefault();
                else session.IncludeOriginal = true;
            }
            catch (Exception e) { session.Status = e.Message; }
        }
        openA.Click += async (_, _) => await Open(0);
        openB.Click += async (_, _) => await Open(1);
        openOriginal.Click += async (_, _) => await Open(2);
        var load = new Button { Content = L.T("Frames laden"), Padding = new Thickness(8, 4) };
        var previous = new Button { Content = "←", Padding = new Thickness(9, 4) };
        var next = new Button { Content = "→", Padding = new Thickness(9, 4) };
        var sampleButton = new Button { Content = L.T("Fünf Vergleichsbilder erzeugen"), Padding = new Thickness(8, 4) };
        var toggle = new Button { Content = session.ToggleLabel, Padding = new Thickness(8, 4) };
        var sideBySide = new CheckBox { Content = L.T("Nebeneinander"), IsChecked = session.SideBySide, Margin = new Thickness(2, 0, 8, 0) };
        load.Click += async (_, _) => await session.LoadNearestAsync();
        previous.Click += async (_, _) => await session.LoadPreviousAsync();
        next.Click += async (_, _) => await session.LoadNextAsync();
        sampleButton.Click += async (_, _) => await session.GenerateSamplesAsync();
        toggle.Click += (_, _) =>
        {
            session.EndReferenceHold();
            session.FocusIndex = session.CompareTwoEncodes ? (session.FocusIndex == 1 ? 0 : 1) : (session.FocusIndex == 2 ? 0 : 2);
            session.SideBySide = false;
            sideBySide.IsChecked = false;
            focus.SelectedIndex = session.FocusIndex;
        };
        sideBySide.IsCheckedChanged += (_, _) => session.SideBySide = sideBySide.IsChecked == true;
        var timeline = new Slider { Minimum = 0, Maximum = session.Maximum, Value = session.Position, Height = 20, Margin = new Thickness(0), TickFrequency = 0.04 };
        timeline.PropertyChanged += async (_, change) =>
        {
            if (change.Property != Slider.ValueProperty || Math.Abs(timeline.Value - session.Position) < 0.0001) return;
            session.Position = timeline.Value;
            await session.LoadNearestAsync();
        };
        timeline.KeyDown += async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right)) return;
            e.Handled = true;
            if (e.Key == Key.Left) await session.LoadPreviousAsync();
            else await session.LoadNextAsync();
        };

        async Task Seek(double seconds)
        {
            session.Position = seconds;
            timeline.Value = seconds;
            await session.LoadNearestAsync();
        }
        stripA.SeekRequested += seconds => _ = Seek(seconds);
        stripB.SeekRequested += seconds => _ = Seek(seconds);

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || Editing(e.Source as StyledElement)) return;
            if (!session.BeginReferenceHold()) return;
            focus.SelectedIndex = session.FocusIndex;
            sideBySide.IsChecked = session.SideBySide;
            Paint();
            e.Handled = true;
        };
        dialog.KeyUp += (_, e) =>
        {
            if (e.Key != Key.Space) return;
            session.EndReferenceHold();
            focus.SelectedIndex = session.FocusIndex;
            sideBySide.IsChecked = session.SideBySide;
            Paint();
            e.Handled = true;
        };
        dialog.Deactivated += (_, _) => session.EndReferenceHold();
        dialog.Closed += (_, _) => session.EndReferenceHold();

        originalColumn.RowDefinitions = new RowDefinitions("Auto,*,Auto");
        originalColumn.Children.Add(headerOriginal);
        Grid.SetRow(originalPane, 1);
        originalColumn.Children.Add(originalPane);
        Grid.SetRow(messageOriginal, 2);
        originalColumn.Children.Add(messageOriginal);
        var columnA = PaneColumn(headerA, paneA, messageA);
        columnB = PaneColumn(headerB, paneB, messageB);
        sideGrid.Children.Add(originalColumn);
        sideGrid.Children.Add(columnA);
        sideGrid.Children.Add(columnB);

        var focusHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        focusHeader.Children.Add(focusLabel);
        Grid.SetColumn(focus, 1);
        focusHeader.Children.Add(focus);
        singleGrid.Children.Add(focusHeader);
        Grid.SetRow(focusPane, 1);
        singleGrid.Children.Add(focusPane);
        Grid.SetRow(focusMessage, 2);
        singleGrid.Children.Add(focusMessage);

        var images = new Grid { MinHeight = 100 };
        images.Children.Add(sideGrid);
        images.Children.Add(singleGrid);

        dialog.Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children =
            {
                Selectors(choiceA, openA, offsetA, choiceB, openB, offsetB, columnB),
                Controls(mode, matched, openOriginal, stripA, stripB, position, previous, next, load, sideBySide, toggle, zoom, timeline),
                Placed(images, 2),
                Placed(new StackPanel
                {
                    Margin = new Thickness(0, 7, 0, 0),
                    Children =
                    {
                        new WrapPanel { Children = { sampleButton, new TextBlock { Text = L.T("Leertaste halten: Referenz · loslassen: zurück zum Vergleich."), IsVisible = session.OriginalVisible, FontSize = 11, Foreground = Brush("Muted"), VerticalAlignment = VerticalAlignment.Center } } },
                        new ScrollViewer { Height = 100, Margin = new Thickness(0, 4, 0, 6), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = samples },
                        status,
                        referenceNote
                    }
                }, 3)
            }
        };
        Paint();
        await dialog.ShowDialog(owner);

        static void DispatcherPost(Action action) => global::Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private static StackPanel Selectors(ComboBox choiceA, Button openA, Control offsetA, ComboBox choiceB, Button openB, Control offsetB, Control columnB)
    {
        var left = Selector(L.T("Encode A"), openA, choiceA, offsetA);
        var right = Selector(L.T("Encode B"), openB, choiceB, offsetB);
        Grid.SetColumn(right, 2);
        right.IsVisible = columnB.IsVisible;
        columnB.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty) right.IsVisible = columnB.IsVisible;
        };
        return new StackPanel { Children = { new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Children = { left, right } } } };
    }

    private static StackPanel Selector(string title, Button open, ComboBox choice, Control offset)
    {
        var header = new DockPanel();
        DockPanel.SetDock(open, Dock.Right);
        header.Children.Add(open);
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Brush("Accent") });
        return new StackPanel { Children = { header, choice, offset } };
    }

    private static StackPanel Controls(ComboBox mode, CheckBox matched, Button openOriginal, ComparisonAvailabilityView stripA, ComparisonAvailabilityView stripB, TextBox position, Button previous, Button next, Button load, CheckBox sideBySide, Button toggle, ComboBox zoom, Slider timeline)
    {
        var legend = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 2),
            Children =
            {
                new TextBlock { Text = "━", Foreground = Brush("Accent"), Margin = new Thickness(0, 0, 4, 0) },
                new TextBlock { Text = L.T("Im Encode vorhanden"), FontSize = 10, Margin = new Thickness(0, 0, 14, 0) },
                new TextBlock { Text = "▨", Foreground = Brush("Warning"), Margin = new Thickness(0, 0, 4, 0) },
                new TextBlock { Text = L.T("Entfernt / außerhalb des Ausschnitts"), FontSize = 10, Margin = new Thickness(0, 0, 14, 0) },
                new TextBlock { Text = "···", Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 4, 0) },
                new TextBlock { Text = L.T("Schnittzuordnung unbekannt"), FontSize = 10 }
            }
        };
        var strips = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        strips.Children.Add(legend);
        Grid.SetColumnSpan(legend, 2);
        strips.Children.Add(new TextBlock { Text = "A", FontSize = 10, FontWeight = FontWeight.SemiBold });
        Grid.SetRow(stripA, 1);
        Grid.SetColumn(stripA, 1);
        strips.Children.Add(stripA);
        var labelB = new TextBlock { Text = "B", FontSize = 10, FontWeight = FontWeight.SemiBold };
        Grid.SetRow(labelB, 2);
        strips.Children.Add(labelB);
        Grid.SetRow(stripB, 2);
        Grid.SetColumn(stripB, 1);
        strips.Children.Add(stripB);
        Grid.SetRow(timeline, 3);
        Grid.SetColumn(timeline, 1);
        strips.Children.Add(timeline);
        stripB.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty) labelB.IsVisible = stripB.IsVisible;
        };
        return new StackPanel
        {
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), Children = { new TextBlock { Text = L.T("Vergleichsmodus"), Margin = new Thickness(0, 0, 8, 5), VerticalAlignment = VerticalAlignment.Center }, mode } },
                        matched,
                        openOriginal
                    }
                },
                strips,
                new WrapPanel
                {
                    Margin = new Thickness(0, 3, 0, 4),
                    Children = { new TextBlock { Text = L.T("Quellzeit (s)"), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }, position, previous, next, load, sideBySide, toggle, zoom }
                }
            }
        };
    }

    private static Grid PaneColumn(TextBlock header, Control pane, TextBlock message)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 4, 0), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(header);
        Grid.SetRow(pane, 1);
        grid.Children.Add(pane);
        Grid.SetRow(message, 2);
        grid.Children.Add(message);
        return grid;
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Foreground = Brush("Accent"), TextTrimming = TextTrimming.CharacterEllipsis, Height = 34, Margin = new Thickness(0, 0, 0, 4) };
    private static TextBlock Message() => new() { FontSize = 10, Height = 42, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private static Control OffsetBox(double value, bool enabled, Action<double> changed)
    {
        var box = new TextBox { Text = value.ToString("0.###"), Width = 72, IsEnabled = enabled };
        box.LostFocus += (_, _) =>
        {
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                changed(seconds);
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = L.T("Quellstart (s):"), FontSize = 10, Foreground = Brush("Muted"), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center },
                box
            }
        };
    }

    private static Control Placed(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static IBrush Brush(string key) =>
        global::Avalonia.Application.Current?.TryFindResource(key, null, out var brush) == true && brush is IBrush found
            ? found
            : Brushes.White;

    private static bool Editing(StyledElement? element)
    {
        while (element is not null)
        {
            if (element is TextBox or ComboBox or CheckBox) return true;
            element = element.Parent as StyledElement;
        }
        return false;
    }
}
