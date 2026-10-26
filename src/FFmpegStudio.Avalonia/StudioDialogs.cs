using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Avalonia;

public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => PreviewBitmaps.Load(value as string);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal static class PreviewBitmaps
{
    public static Bitmap? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        using var input = File.OpenRead(path);
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        copy.Position = 0;
        return new Bitmap(copy);
    }
}

public static class StudioDialogs
{
    public static async Task<AppSettings?> EditSettingsAsync(Window owner, AppSettings draft, MediaInfo? media = null, EncodingOptions? options = null)
    {
        var originalTheme = draft.ThemeId;
        var originalDark = draft.DarkMode;
        AppSettings? result = null;
        var dialog = Window(L.T("Einstellungen"), 760, 640, owner);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = L.T("Allgemein"), Content = GeneralTab(draft) });
        tabs.Items.Add(new TabItem { Header = L.T("Darstellung"), Content = ThemeTab(draft) });
        tabs.Items.Add(new TabItem { Header = L.T("Ordnerüberwachung"), Content = WatchTab(owner, draft) });
        tabs.Items.Add(new TabItem { Header = L.T("Dateinamen"), Content = NamingTab(draft, media, options) });
        var save = new Button { Content = L.T("Speichern"), MinWidth = 96 };
        var cancel = new Button { Content = L.T("Abbrechen"), MinWidth = 96 };
        save.Click += (_, _) =>
        {
            try
            {
                foreach (var rule in draft.WatchFolders.Where(rule => rule.Enabled)) FolderWatchService.ValidateRule(rule);
                if (draft.TooltipsEnabled && (!double.IsFinite(draft.TooltipDelaySeconds) || draft.TooltipDelaySeconds is < 0 or > TooltipTiming.MaximumSeconds))
                    throw new ArgumentException(L.T("Die Tooltip-Wartezeit muss zwischen 0 und 30 Sekunden liegen."));
                draft.TooltipDelaySeconds = TooltipTiming.Normalize(draft.TooltipDelaySeconds);
                result = draft;
                dialog.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        };
        cancel.Click += (_, _) =>
        {
            draft.ThemeId = originalTheme;
            draft.DarkMode = originalDark;
            (global::Avalonia.Application.Current as App)?.ApplyTheme(draft);
            dialog.Close();
        };
        dialog.Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Children =
            {
                Footer(error, save, cancel),
                tabs
            }
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<IReadOnlyDictionary<string, string>?> EditExpertAsync(Window owner, EncoderInfo encoder, IReadOnlyDictionary<string, string> current)
    {
        IReadOnlyDictionary<string, string>? result = null;
        var dialog = Window(L.T("Expertenoptionen · ") + encoder.Name, 720, 640, owner);
        var inputs = new Dictionary<string, TextBox>();
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Tatsächlich erkannte Optionen dieser FFmpeg-Version. Leere Felder verwenden den Encoderstandard. Bereits im einfachen Modus gesetzte Optionen hier nicht doppelt setzen."),
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var option in EncoderOptionEditor.Editable(encoder))
        {
            stack.Children.Add(new TextBlock { Text = option.Name + " · " + option.Type, FontWeight = FontWeight.SemiBold });
            if (option.Description.Length > 0)
                stack.Children.Add(new TextBlock { Text = option.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            var box = new TextBox { Text = current.GetValueOrDefault(option.Name, ""), PlaceholderText = option.Choices.Count == 0 ? "" : string.Join(", ", option.Choices) };
            inputs[option.Name] = box;
            stack.Children.Add(box);
        }
        if (inputs.Count == 0)
            stack.Children.Add(new TextBlock { Text = L.T("Dieser Encoder hat keine zusätzlichen Expertenoptionen."), TextWrapping = TextWrapping.Wrap });
        var help = new Button { Content = L.T("Encoderhilfe anzeigen"), MinWidth = 140 };
        var helpText = new TextBox { Text = encoder.Help, IsReadOnly = true, AcceptsReturn = true, Height = 160, IsVisible = false };
        help.Click += (_, _) => helpText.IsVisible = !helpText.IsVisible;
        var apply = new Button { Content = L.T("Übernehmen"), MinWidth = 96 };
        var cancel = new Button { Content = L.T("Abbrechen"), MinWidth = 96 };
        apply.Click += (_, _) =>
        {
            result = inputs.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text)).ToDictionary(pair => pair.Key, pair => pair.Value.Text!.Trim());
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        var root = new DockPanel { Margin = new global::Avalonia.Thickness(16) };
        DockPanel.SetDock(helpText, Dock.Bottom);
        root.Children.Add(Footer(help, apply, cancel));
        root.Children.Add(helpText);
        root.Children.Add(new ScrollViewer { Content = stack });
        dialog.Content = root;
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> TestEncodeNoticeAsync(Window owner, EncodingJob job)
    {
        var suppress = new CheckBox { Content = L.T("Nicht mehr anzeigen") };
        var message = new TextBlock { Text = TestEncodeNotice.StatusMessage(job), FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var dialog = Window(L.T("Testencode"), 560, 240, owner);
        var close = new Button { Content = L.T("Schließen"), MinWidth = 96 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(18),
            Spacing = 12,
            Children =
            {
                message,
                new TextBlock { Text = Path.GetFileName(job.OutputPath), TextWrapping = TextWrapping.NoWrap },
                new TextBlock { Text = L.T("Fortschritt und Details findest du in der Queue. Das Schließen dieses Hinweises stoppt den Encode nicht."), TextWrapping = TextWrapping.Wrap },
                suppress,
                close
            }
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => message.Text = TestEncodeNotice.StatusMessage(job);
        dialog.Opened += (_, _) => timer.Start();
        dialog.Closed += (_, _) => timer.Stop();
        await dialog.ShowDialog(owner);
        return suppress.IsChecked == true;
    }

    public static async Task ShowComparisonAsync(Window owner, EncodeComparisonSession session, Func<Task<EncodeComparisonInput?>> openFile)
    {
        var dialog = Window(L.T("Encode-Vergleich"), 1100, 780, owner);
        var imageA = new Image { Stretch = Stretch.Uniform };
        var imageB = new Image { Stretch = Stretch.Uniform };
        var original = new Image { Stretch = Stretch.Uniform };
        var focusImage = new Image { Stretch = Stretch.Uniform };
        var focusCaption = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new global::Avalonia.Thickness(6) };
        var samples = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var stripA = new ComparisonAvailabilityView();
        var stripB = new ComparisonAvailabilityView();
        void ShowFrames()
        {
            imageA.Source = PreviewBitmaps.Load(session.PathA);
            imageB.Source = PreviewBitmaps.Load(session.PathB);
            original.Source = PreviewBitmaps.Load(session.OriginalPath);
            focusImage.Source = PreviewBitmaps.Load(session.FocusPath);
            var scale = new ScaleTransform(session.Zoom, session.Zoom);
            imageA.RenderTransform = scale;
            imageB.RenderTransform = new ScaleTransform(session.Zoom, session.Zoom);
            original.RenderTransform = new ScaleTransform(session.Zoom, session.Zoom);
            focusImage.RenderTransform = new ScaleTransform(session.Zoom, session.Zoom);
            focusCaption.Text = L.T("Fokus") + " · " + session.Focus switch
            {
                ComparisonFocus.Original => L.T("Original"),
                ComparisonFocus.B => "B",
                _ => "A"
            };
            var duration = session.SelectedA?.Input.Job?.Source.Duration ?? session.SelectedA?.Input.Media.Duration ?? 1;
            if (duration <= 0 || !double.IsFinite(duration)) duration = 1;
            stripA.Duration = duration;
            stripB.Duration = duration;
            stripA.Position = session.Position;
            stripB.Position = session.Position;
            stripA.Intervals = session.CoverageA;
            stripB.Intervals = session.CoverageB;
            stripB.IsVisible = session.CompareTwo;
            stripA.InvalidateVisual();
            stripB.InvalidateVisual();
        }
        void RebuildSamples()
        {
            samples.Children.Clear();
            foreach (var shot in session.Samples)
            {
                var thumb = new Image { Source = PreviewBitmaps.Load(shot.PathA ?? shot.OriginalPath), Width = 112, Height = 63, Stretch = Stretch.Uniform };
                var button = new Button
                {
                    Content = new StackPanel { Spacing = 2, Children = { thumb, new TextBlock { Text = shot.Label, HorizontalAlignment = HorizontalAlignment.Center } } },
                    Padding = new global::Avalonia.Thickness(2)
                };
                var current = shot;
                button.Click += (_, _) => { session.SelectSample(current); ShowFrames(); };
                samples.Children.Add(button);
            }
        }
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(EncodeComparisonSession.PathA) or nameof(EncodeComparisonSession.PathB)
                or nameof(EncodeComparisonSession.OriginalPath) or nameof(EncodeComparisonSession.Zoom)
                or nameof(EncodeComparisonSession.FocusPath) or nameof(EncodeComparisonSession.Position)
                or nameof(EncodeComparisonSession.SelectedA) or nameof(EncodeComparisonSession.SelectedB)
                or nameof(EncodeComparisonSession.OffsetA) or nameof(EncodeComparisonSession.OffsetB)
                or nameof(EncodeComparisonSession.CompareTwo))
                Dispatcher.UIThread.Post(ShowFrames);
        };
        session.Samples.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RebuildSamples);
        var choiceA = new ComboBox { ItemsSource = session.Choices, SelectedItem = session.SelectedA };
        var choiceB = new ComboBox { ItemsSource = session.Choices, SelectedItem = session.SelectedB };
        choiceA.SelectionChanged += (_, _) => session.SelectedA = choiceA.SelectedItem as EncodeComparisonChoice;
        choiceB.SelectionChanged += (_, _) => session.SelectedB = choiceB.SelectedItem as EncodeComparisonChoice;
        var two = new CheckBox { Content = L.T("Encode A + Encode B"), IsChecked = session.CompareTwo };
        var withOriginal = new CheckBox { Content = L.T("Original"), IsChecked = session.IncludeOriginal };
        var matched = new CheckBox { Content = L.T("Referenz mit Filtern von Encode A"), IsChecked = session.MatchReference };
        two.IsCheckedChanged += (_, _) => session.CompareTwo = two.IsChecked == true;
        withOriginal.IsCheckedChanged += (_, _) => session.IncludeOriginal = withOriginal.IsChecked == true;
        matched.IsCheckedChanged += (_, _) => session.MatchReference = matched.IsChecked == true;
        var position = new NumericUpDown { Value = (decimal)session.Position, Minimum = 0, Increment = 0.04m, FormatString = "0.000", Width = 120 };
        position.ValueChanged += (_, _) => session.Position = (double)(position.Value ?? 0);
        var offsetA = new NumericUpDown { Value = (decimal)session.OffsetA, Increment = 0.04m, FormatString = "0.000", Width = 110 };
        var offsetB = new NumericUpDown { Value = (decimal)session.OffsetB, Increment = 0.04m, FormatString = "0.000", Width = 110 };
        offsetA.ValueChanged += (_, _) => session.OffsetA = (double)(offsetA.Value ?? 0);
        offsetB.ValueChanged += (_, _) => session.OffsetB = (double)(offsetB.Value ?? 0);
        var zoom = new ComboBox { ItemsSource = new double[] { 1, 2, 4 }, SelectedItem = session.Zoom, Width = 80 };
        zoom.SelectionChanged += (_, _) => { if (zoom.SelectedItem is double value) session.Zoom = value; };
        var open = new Button { Content = L.T("Datei öffnen") };
        var load = new Button { Content = L.T("Vergleichen") };
        var previous = new Button { Content = L.T("Vorheriges Bild") };
        var next = new Button { Content = L.T("Nächstes Bild") };
        var sampleButton = new Button { Content = L.T("Beispielbilder laden") };
        var focusA = new Button { Content = "A" };
        var focusB = new Button { Content = "B" };
        var focusOriginal = new Button { Content = L.T("Original") };
        var close = new Button { Content = L.T("Schließen") };
        open.Click += async (_, _) =>
        {
            try
            {
                var input = await openFile();
                if (input is null) return;
                session.AddExternal(input);
                choiceA.SelectedItem = session.SelectedA;
                choiceB.SelectedItem = session.SelectedB;
            }
            catch (Exception e) { session.Status = e.Message; }
        };
        load.Click += async (_, _) => await session.LoadNearestAsync();
        previous.Click += async (_, _) => await session.LoadPreviousAsync();
        next.Click += async (_, _) => await session.LoadNextAsync();
        sampleButton.Click += async (_, _) => await session.GenerateSamplesAsync();
        focusA.Click += (_, _) => { session.Focus = ComparisonFocus.A; ShowFrames(); };
        focusB.Click += (_, _) => { session.Focus = ComparisonFocus.B; ShowFrames(); };
        focusOriginal.Click += (_, _) => { session.Focus = ComparisonFocus.Original; ShowFrames(); };
        async Task Seek(double seconds)
        {
            session.Position = seconds;
            position.Value = (decimal)seconds;
            await session.LoadNearestAsync();
        }
        stripA.SeekRequested += seconds => _ = Seek(seconds);
        stripB.SeekRequested += seconds => _ = Seek(seconds);
        close.Click += (_, _) => dialog.Close();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding(nameof(EncodeComparisonSession.Status)) { Source = session });
        var images = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8, MinHeight = 180 };
        images.Children.Add(Frame(L.T("Original"), original));
        var paneA = Frame("A", imageA);
        Grid.SetColumn(paneA, 1);
        images.Children.Add(paneA);
        var paneB = Frame("B", imageB);
        Grid.SetColumn(paneB, 2);
        images.Children.Add(paneB);
        var focusPane = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#080B10")),
            MinHeight = 180,
            Child = new DockPanel
            {
                Children = { Docked(Dock.Top, focusCaption), new Viewbox { Child = focusImage } }
            }
        };
        dialog.Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(12),
            Children =
            {
                Docked(Dock.Top, new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        choiceA, choiceB,
                        new WrapPanel { Orientation = Orientation.Horizontal, Children = { two, withOriginal, matched, Label(L.T("Zeit (s)")), position, Label("A"), offsetA, Label("B"), offsetB, zoom } },
                        new WrapPanel { Orientation = Orientation.Horizontal, Children = { open, load, previous, next, sampleButton, focusA, focusB, focusOriginal, close } },
                        status,
                        Label(L.T("Beispielbilder")),
                        new ScrollViewer { HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = samples },
                        Label("A · " + L.T("Verfügbarkeit auf der Quellzeitachse. Vollfläche: vorhanden. Schraffiert: entfernt oder außerhalb des Ausschnitts. Gepunktet: Schnittzuordnung unbekannt. Klicken, um zu dieser Quellposition zu springen. Bei externen Dateien sind nur Dateigrenzen und Zeitversatz bekannt.")),
                        stripA,
                        stripB
                    }
                }),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 8,
                    Children = { images, focusPane }
                }
            }
        };
        Grid.SetColumn(focusPane, 1);
        ShowFrames();
        await dialog.ShowDialog(owner);
    }

    public static async Task ShowAnalysisAsync(Window owner, QualityResult result)
    {
        var dialog = Window(L.T("Qualitätsanalyse · gespeicherte Messwerte"), 900, 720, owner);
        static string N(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? L.T("nicht verfügbar");
        var header = new TextBlock { Text = $"VMAF {N(result.Vmaf)}    SSIM {N(result.Ssim)}    PSNR {N(result.Psnr)} dB", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var canvas = new Canvas { Height = 220, Background = new SolidColorBrush(Color.Parse("#10151F")) };
        var metric = new ComboBox { ItemsSource = new[] { "VMAF", "SSIM", "PSNR" }, SelectedIndex = result.Vmaf.HasValue ? 0 : result.Ssim.HasValue ? 1 : 2, Width = 140 };
        var rows = new ListBox { MaxHeight = 240 };
        var axisNote = new TextBlock { Opacity = 0.8 };
        void Draw()
        {
            canvas.Children.Clear();
            var name = metric.SelectedItem as string ?? "VMAF";
            var sorted = QualityChart.Sorted(result.Frames, name);
            rows.ItemsSource = sorted.Select(frame => $"{frame.Seconds:0.000} s    VMAF {N(frame.Vmaf)}    SSIM {N(frame.Ssim)}    PSNR {N(frame.Psnr)}").ToList();
            var series = result.Frames.Select(frame => (frame.Seconds, Value: QualityChart.Value(frame, name))).Where(point => point.Value.HasValue && double.IsFinite(point.Value.Value)).ToList();
            var width = Math.Max(120, canvas.Bounds.Width);
            var height = canvas.Height;
            var max = name switch { "SSIM" => 1d, "PSNR" => Math.Max(50, series.Count == 0 ? 50 : series.Max(point => point.Value!.Value)), _ => 100d };
            foreach (var tick in QualityChart.Axis(0, max, 4))
            {
                var y = 16 + (1 - tick / Math.Max(max, 0.0001)) * (height - 48);
                canvas.Children.Add(new global::Avalonia.Controls.Shapes.Line { StartPoint = new global::Avalonia.Point(48, y), EndPoint = new global::Avalonia.Point(width - 12, y), Stroke = new SolidColorBrush(Color.Parse("#344155")), StrokeThickness = 1 });
                var label = new TextBlock { Text = tick.ToString("0.##", CultureInfo.InvariantCulture), FontSize = 10, Foreground = Brushes.White };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, y - 7);
                canvas.Children.Add(label);
            }
            if (series.Count > 0)
            {
                var timeMax = Math.Max(0.01, series.Max(point => point.Seconds));
                var line = new global::Avalonia.Controls.Shapes.Polyline { Stroke = Brushes.White, StrokeThickness = 1.5, Points = [] };
                foreach (var point in series)
                {
                    var x = 48 + point.Seconds / timeMax * (width - 64);
                    var y = 16 + (1 - point.Value!.Value / Math.Max(max, 0.0001)) * (height - 48);
                    line.Points.Add(new global::Avalonia.Point(x, y));
                }
                canvas.Children.Add(line);
                var end = new TextBlock { Text = $"0 s → {timeMax:0.0} s", FontSize = 10, Foreground = Brushes.White };
                Canvas.SetLeft(end, 48);
                Canvas.SetTop(end, height - 18);
                canvas.Children.Add(end);
                axisNote.Text = name + " · 0 – " + max.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }
        metric.SelectionChanged += (_, _) => Draw();
        canvas.SizeChanged += (_, _) => Draw();
        var csv = new Button { Content = L.T("CSV exportieren") };
        var json = new Button { Content = L.T("JSON exportieren") };
        var close = new Button { Content = L.T("Schließen") };
        csv.Click += async (_, _) => await Save(dialog, "Qualitaetsverlauf.csv", QualityReport.Csv(result));
        json.Click += async (_, _) => await Save(dialog, "Analyse.json", QualityReport.JsonText(result));
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Children =
            {
                Docked(Dock.Top, new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        header,
                        new TextBlock { Text = result.Conditions, TextWrapping = TextWrapping.Wrap },
                        metric,
                        canvas,
                        axisNote,
                        new TextBlock { Text = L.T("Schwächste Frames zuerst · nach ausgewählter Metrik") + " · " + L.T("Zeit (s)") + " · VMAF · SSIM · PSNR" }
                    }
                }),
                Docked(Dock.Bottom, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { csv, json, close } }),
                rows
            }
        };
        dialog.Opened += (_, _) => Draw();
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> ConfirmCountdownAsync(Window owner, CompletionAction action)
    {
        var dialog = Window(L.T("Aktion nach Queue-Abschluss"), 560, 260, owner);
        var remaining = SessionPower.CountdownSeconds;
        var accepted = false;
        var label = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        void Update() => label.Text = L.F($"{L.EnumText(action)} in {remaining} Sekunden.\nAlle Jobs sind erfolgreich abgeschlossen.");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0)
            {
                accepted = true;
                timer.Stop();
                dialog.Close();
                return;
            }
            Update();
        };
        dialog.Opened += (_, _) => timer.Start();
        dialog.Closed += (_, _) => timer.Stop();
        var cancel = new Button { Content = L.T("Aktion abbrechen"), MinWidth = 140 };
        cancel.Click += (_, _) => dialog.Close();
        Update();
        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = SessionPower.Prompt(action), TextWrapping = TextWrapping.Wrap },
                label,
                cancel
            }
        };
        await dialog.ShowDialog(owner);
        return accepted;
    }

    public static async Task ShowResultsAsync(Window owner, IReadOnlyList<EncodingJob> jobs)
    {
        var dialog = Window(L.T("Encoding-Ergebnisse vergleichen"), 980, 640, owner);
        var sources = EncodingResults.Sources(jobs);
        var source = new ComboBox { ItemsSource = sources, SelectedIndex = sources.Count == 0 ? -1 : 0 };
        var rows = new ListBox();
        void Refresh()
        {
            var selected = source.SelectedItem as string;
            var table = EncodingResults.Rows(jobs, selected);
            rows.ItemsSource = table.Count == 0
                ? new[] { L.T("Einen abgeschlossenen Test- oder vollständigen Encode auswählen.") }
                : table.Select(row => string.Join("   ", new[]
                {
                    row.Encoder, row.Preset, row.Raster, row.Clip, row.MiB + " MiB", row.Seconds + " s", row.Speed, row.Bitrate + " kbit/s",
                    "VMAF " + row.Vmaf, "SSIM " + row.Ssim, "PSNR " + row.Psnr
                })).ToArray();
        }
        source.SelectionChanged += (_, _) => Refresh();
        var close = new Button { Content = L.T("Schließen") };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Children =
            {
                Docked(Dock.Top, new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = L.T("Ergebnisse derselben Quelldatei"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = L.T("Qualitätswerte nur bei gleichem Ausschnitt, Bildformat, Zeitraster und Analysebedingungen direkt vergleichen. Fehlende Werte bleiben leer."), TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = L.T("Encoder") + " · " + L.T("Preset") + " · " + L.T("Raster") + " · " + L.T("Ausschnitt (s)") + " · MiB · " + L.T("Dauer (s)") + " · " + L.T("Tempo") + " · kbit/s · VMAF · SSIM · PSNR", TextWrapping = TextWrapping.Wrap },
                        source
                    }
                }),
                Docked(Dock.Bottom, close),
                rows
            }
        };
        Refresh();
        await dialog.ShowDialog(owner);
    }

    private static Control GeneralTab(AppSettings draft)
    {
        var language = new ComboBox { ItemsSource = UiLanguage.All, SelectedItem = UiLanguage.All.FirstOrDefault(item => item.Code == L.Normalize(draft.Language)) };
        language.SelectionChanged += (_, _) => { if (language.SelectedItem is UiLanguage selected) draft.Language = selected.Code; };
        var tooltips = Check(L.T("Tooltips anzeigen"), draft.TooltipsEnabled, value => draft.TooltipsEnabled = value);
        var delay = Box(draft.TooltipDelaySeconds.ToString(CultureInfo.CurrentCulture));
        delay.TextChanged += (_, _) => { if (double.TryParse(delay.Text, out var seconds)) draft.TooltipDelaySeconds = seconds; };
        var notice = Check(L.T("Hinweis beim Start eines Testencodes anzeigen"), draft.ShowTestEncodeNotification, value => draft.ShowTestEncodeNotification = value);
        var recursive = Check(L.T("Unterordner beim manuellen Import einlesen"), draft.RecursiveImport, value => draft.RecursiveImport = value);
        var efficiency = Check(L.T("Encodes im Windows-Effizienzmodus starten"), draft.EfficiencyMode, value => draft.EfficiencyMode = value);
        var updates = Check(L.T("Beim Start und regelmäßig nach FFmpeg-Updates suchen"), draft.CheckUpdates, value => draft.CheckUpdates = value);
        var autoInstall = Check(L.T("FFmpeg automatisch installieren, wenn es fehlt"), draft.AutoInstall, value => draft.AutoInstall = value);
        var autoUpdate = Check(L.T("Verwaltete FFmpeg-Installation automatisch aktualisieren"), draft.AutoUpdate, value => draft.AutoUpdate = value);
        var interval = Box(draft.UpdateIntervalHours.ToString(CultureInfo.InvariantCulture));
        var jobs = Box(draft.ParallelJobs.ToString(CultureInfo.InvariantCulture));
        var gpu = Box(draft.ParallelGpuJobs.ToString(CultureInfo.InvariantCulture));
        var cache = Box(draft.CacheLimitMiB.ToString(CultureInfo.InvariantCulture));
        var output = Box(draft.DefaultOutputDirectory);
        interval.TextChanged += (_, _) => { if (int.TryParse(interval.Text, out var value)) draft.UpdateIntervalHours = value; };
        jobs.TextChanged += (_, _) => { if (int.TryParse(jobs.Text, out var value)) draft.ParallelJobs = value; };
        gpu.TextChanged += (_, _) => { if (int.TryParse(gpu.Text, out var value)) draft.ParallelGpuJobs = value; };
        cache.TextChanged += (_, _) => { if (int.TryParse(cache.Text, out var value)) draft.CacheLimitMiB = value; };
        output.TextChanged += (_, _) => draft.DefaultOutputDirectory = output.Text ?? "";
        var completion = new ComboBox { ItemsSource = Enum.GetValues<CompletionAction>(), SelectedItem = draft.CompletionAction };
        completion.ItemTemplate = new FuncDataTemplate<CompletionAction>((action, _) => new TextBlock { Text = L.EnumText(action) });
        completion.SelectionChanged += (_, _) => { if (completion.SelectedItem is CompletionAction action) draft.CompletionAction = action; };
        var stack = new StackPanel { Spacing = 8, Children = { Label(L.T("Sprache")), language, tooltips, Label(L.T("Wartezeit für Tooltips (Sekunden; 0–30)")), delay, notice, recursive, efficiency, updates, autoInstall, autoUpdate, Label(L.T("Updateintervall in Stunden (1–720)")), interval, Label(L.T("Maximale parallele Jobs (1–16)")), jobs, Label(L.T("Maximale parallele NVIDIA-Jobs (1–8)")), gpu, Label(L.T("PNG-Cachelimit in MiB (32–16384)")), cache, Label(L.T("Standard-Ausgabeordner (leer = Unterordner neben Quelle)")), output, Label(L.T("Aktion, wenn sämtliche Jobs erfolgreich abgeschlossen sind")), completion } };
        return new ScrollViewer { Content = stack };
    }

    private static Control ThemeTab(AppSettings draft)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        void Mark()
        {
            foreach (var button in panel.Children.OfType<Button>())
            {
                var theme = button.Tag as AppTheme;
                button.BorderBrush = theme is not null && theme.Id == draft.ThemeId
                    ? new SolidColorBrush(Color.Parse(theme.Accent))
                    : new SolidColorBrush(Color.Parse("#344155"));
                button.BorderThickness = new global::Avalonia.Thickness(theme is not null && theme.Id == draft.ThemeId ? 3 : 1);
            }
        }
        foreach (var theme in ThemeCatalog.All)
        {
            var swatch = new Border
            {
                Background = new SolidColorBrush(Color.Parse(theme.Bg)),
                Padding = new global::Avalonia.Thickness(10),
                Width = 220,
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = L.T(theme.Name), Foreground = new SolidColorBrush(Color.Parse(theme.Text)), FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = L.T(theme.Description), Foreground = new SolidColorBrush(Color.Parse(theme.Muted)), TextWrapping = TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Height = 10,
                            Children =
                            {
                                new Border { Width = 48, Background = new SolidColorBrush(Color.Parse(theme.Surface)) },
                                new Border { Width = 48, Background = new SolidColorBrush(Color.Parse(theme.Raised)) },
                                new Border { Width = 48, Background = new SolidColorBrush(Color.Parse(theme.Selection)) },
                                new Border { Width = 48, Background = new SolidColorBrush(Color.Parse(theme.Accent)) }
                            }
                        }
                    }
                }
            };
            var button = new Button { Content = swatch, Tag = theme, Margin = new global::Avalonia.Thickness(4), Padding = new global::Avalonia.Thickness(2), Background = Brushes.Transparent };
            button.Click += (_, _) =>
            {
                draft.ThemeId = theme.Id;
                draft.DarkMode = theme.IsDark;
                (global::Avalonia.Application.Current as App)?.ApplyTheme(draft);
                Mark();
            };
            panel.Children.Add(button);
        }
        Mark();
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = L.T("Die Auswahl wird sofort als Vorschau angezeigt. Speichern übernimmt das Farbschema; Abbrechen stellt die vorherige Auswahl wieder her."), TextWrapping = TextWrapping.Wrap },
                    panel
                }
            }
        };
    }

    private static Control WatchTab(Window owner, AppSettings draft)
    {
        var rules = new ListBox { ItemsSource = draft.WatchFolders, Height = 180 };
        rules.ItemTemplate = new FuncDataTemplate<WatchRule>((rule, _) => new TextBlock { Text = rule is null ? "" : rule.Folder + " → " + rule.OutputFolder });
        var add = new Button { Content = L.T("Regel hinzufügen") };
        var edit = new Button { Content = L.T("Regel bearbeiten") };
        var remove = new Button { Content = L.T("Regel entfernen") };
        add.Click += async (_, _) =>
        {
            var created = await EditRuleAsync(owner, new WatchRule());
            if (created is null) return;
            draft.WatchFolders.Add(created);
            rules.ItemsSource = null;
            rules.ItemsSource = draft.WatchFolders;
        };
        edit.Click += async (_, _) =>
        {
            if (rules.SelectedItem is not WatchRule rule) return;
            var edited = await EditRuleAsync(owner, Json.Clone(rule));
            if (edited is null) return;
            var index = draft.WatchFolders.IndexOf(rule);
            if (index < 0) return;
            draft.WatchFolders[index] = edited;
            rules.ItemsSource = null;
            rules.ItemsSource = draft.WatchFolders;
        };
        remove.Click += (_, _) =>
        {
            if (rules.SelectedItem is not WatchRule rule) return;
            draft.WatchFolders.Remove(rule);
            rules.ItemsSource = null;
            rules.ItemsSource = draft.WatchFolders;
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = L.T("Für jede Regel einen eigenen Ausgangsordner und ein vorhandenes Preset angeben. Dateien müssen mehrfach unverändert und exklusiv lesbar sein. Eigene Ausgabeordner werden ausgeschlossen."), TextWrapping = TextWrapping.Wrap },
                rules,
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { add, edit, remove } }
            }
        };
    }

    private static async Task<WatchRule?> EditRuleAsync(Window owner, WatchRule rule)
    {
        WatchRule? result = null;
        var dialog = Window(L.T("Überwachungsregel"), 560, 460, owner);
        var folder = Box(rule.Folder);
        var output = Box(rule.OutputFolder);
        var preset = Box(rule.PresetName);
        var extensions = Box(rule.Extensions);
        var enabled = Check(L.T("Überwachung aktiv"), rule.Enabled, value => rule.Enabled = value);
        var recursive = Check(L.T("Unterordner einbeziehen"), rule.Recursive, value => rule.Recursive = value);
        var auto = Check(L.T("Neue Jobs automatisch starten"), rule.AutoStart, value => rule.AutoStart = value);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        var pickIn = new Button { Content = L.T("Ordner auswählen") };
        var pickOut = new Button { Content = L.T("Ordner auswählen") };
        pickIn.Click += async (_, _) => { var path = await PickFolder(dialog, L.T("Eingangsordner")); if (path is not null) folder.Text = path; };
        pickOut.Click += async (_, _) => { var path = await PickFolder(dialog, L.T("Ausgabeordner")); if (path is not null) output.Text = path; };
        var ok = new Button { Content = L.T("Übernehmen"), MinWidth = 96 };
        var cancel = new Button { Content = L.T("Abbrechen"), MinWidth = 96 };
        ok.Click += (_, _) =>
        {
            rule.Folder = folder.Text?.Trim() ?? "";
            rule.OutputFolder = output.Text?.Trim() ?? "";
            rule.PresetName = preset.Text?.Trim() ?? "";
            rule.Extensions = extensions.Text?.Trim() ?? "";
            try
            {
                if (rule.Enabled) FolderWatchService.ValidateRule(rule);
                result = rule;
                dialog.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new global::Avalonia.Thickness(16),
                Spacing = 8,
                Children = { Label(L.T("Eingangsordner")), folder, pickIn, Label(L.T("Ausgabeordner")), output, pickOut, Label(L.T("Presetname")), preset, Label(L.T("Dateiendungen, mit Semikolon getrennt (* = alle)")), extensions, enabled, recursive, auto, error, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } } }
            }
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Control NamingTab(AppSettings draft, MediaInfo? media, EncodingOptions? options)
    {
        var naming = draft.FileNaming;
        var pattern = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var example = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,monospace") };
        void Refresh()
        {
            pattern.Text = naming.Enabled ? FileNaming.Pattern(naming) : options?.FileNamePattern ?? L.T("Ist diese Option aus, gilt das individuelle Dateinamenschema im Video-Reiter.");
            example.Text = FileNamePreview.Of(media, options, naming);
        }
        var enabled = Check(L.T("Diese Namensauswahl für neue Queue-Aufträge verwenden"), naming.Enabled, value => { naming.Enabled = value; Refresh(); });
        CheckBox Part(string label, bool value, Action<bool> write) => Check(L.T(label), value, current => { write(current); Refresh(); });
        var stack = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = L.T("Wähle die Angaben, die dem ursprünglichen Dateinamen angefügt werden. Die Dateiendung richtet sich automatisch nach dem Ausgabeformat."), TextWrapping = TextWrapping.Wrap },
                enabled,
                Part("Datum (JJJJ-MM-TT)", naming.IncludeDate, value => naming.IncludeDate = value),
                Part("Uhrzeit (HH-MM-SS)", naming.IncludeTime, value => naming.IncludeTime = value),
                Part("Auflösung (Breite × Höhe)", naming.IncludeResolution, value => naming.IncludeResolution = value),
                Part("Videocodec (AV1, HEVC …)", naming.IncludeCodec, value => naming.IncludeCodec = value),
                Part("Encoder (libsvtav1 …)", naming.IncludeEncoder, value => naming.IncludeEncoder = value),
                Part("Qualitätswert (CRF / CQ / QP)", naming.IncludeQuality, value => naming.IncludeQuality = value),
                Part("Video-Zielbitrate (kbit/s)", naming.IncludeBitrate, value => naming.IncludeBitrate = value),
                Part("Geschwindigkeitspreset (preset-4 …)", naming.IncludeSpeed, value => naming.IncludeSpeed = value),
                Part("Name des gespeicherten Presets", naming.IncludePreset, value => naming.IncludePreset = value),
                Part("Bildrate (FPS)", naming.IncludeFrameRate, value => naming.IncludeFrameRate = value),
                Label(L.T("Erzeugtes Dateinamenschema")),
                pattern,
                Label(L.T(media is null ? "Eine Videodatei auswählen." : "Vorschau mit der aktuell ausgewählten Datei")),
                example
            }
        };
        Refresh();
        return new ScrollViewer { Content = stack };
    }

    private static async Task Save(Window owner, string name, string contents)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = name, SuggestedFileName = name });
        var path = file?.TryGetLocalPath();
        if (path is not null) await File.WriteAllTextAsync(path, contents);
    }

    private static async Task<string?> PickFolder(Window owner, string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static Window Window(string title, double width, double height, Window owner) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = 420,
        MinHeight = 240,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = owner.Background,
        Foreground = owner.Foreground
    };

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
    private static TextBox Box(string text) => new() { Text = text };
    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = value };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private static Control Footer(params Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new global::Avalonia.Thickness(0, 12, 0, 0) };
        foreach (var control in controls) panel.Children.Add(control);
        DockPanel.SetDock(panel, Dock.Bottom);
        return panel;
    }

    private static Control Docked(Dock dock, Control control)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static Border Frame(string title, Image image) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#080B10")),
        Child = new DockPanel
        {
            Children =
            {
                Docked(Dock.Top, new TextBlock { Text = title, Margin = new global::Avalonia.Thickness(6) }),
                new Viewbox { Child = image }
            }
        }
    };
}
