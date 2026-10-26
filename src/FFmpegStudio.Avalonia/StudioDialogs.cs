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
    public static async Task<AppSettings?> EditSettingsAsync(Window owner, AppSettings draft)
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
        tabs.Items.Add(new TabItem { Header = L.T("Dateinamen"), Content = NamingTab(draft) });
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
        var dialog = Window(L.T("Encode-Vergleich"), 980, 700, owner);
        var imageA = new Image { Stretch = Stretch.Uniform };
        var imageB = new Image { Stretch = Stretch.Uniform };
        var original = new Image { Stretch = Stretch.Uniform };
        void Show()
        {
            imageA.Source = PreviewBitmaps.Load(session.PathA);
            imageB.Source = PreviewBitmaps.Load(session.PathB);
            original.Source = PreviewBitmaps.Load(session.OriginalPath);
            var scale = new ScaleTransform(session.Zoom, session.Zoom);
            imageA.RenderTransform = scale;
            imageB.RenderTransform = new ScaleTransform(session.Zoom, session.Zoom);
            original.RenderTransform = new ScaleTransform(session.Zoom, session.Zoom);
        }
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(EncodeComparisonSession.PathA) or nameof(EncodeComparisonSession.PathB) or nameof(EncodeComparisonSession.OriginalPath) or nameof(EncodeComparisonSession.Zoom))
                Dispatcher.UIThread.Post(Show);
        };
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
        close.Click += (_, _) => dialog.Close();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding(nameof(EncodeComparisonSession.Status)) { Source = session });
        var images = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
        images.Children.Add(Frame(L.T("Original"), original));
        Grid.SetColumn(images.Children[0], 0);
        var paneA = Frame("A", imageA);
        Grid.SetColumn(paneA, 1);
        images.Children.Add(paneA);
        var paneB = Frame("B", imageB);
        Grid.SetColumn(paneB, 2);
        images.Children.Add(paneB);
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
                        new WrapPanel { Orientation = Orientation.Horizontal, Children = { two, withOriginal, matched, Label(L.T("Zeit")), position, Label("A"), offsetA, Label("B"), offsetB, zoom } },
                        new WrapPanel { Orientation = Orientation.Horizontal, Children = { open, load, previous, next, close } },
                        status
                    }
                }),
                images
            }
        };
        await dialog.ShowDialog(owner);
    }

    public static async Task ShowAnalysisAsync(Window owner, QualityResult result)
    {
        var dialog = Window(L.T("Qualitätsanalyse · gespeicherte Messwerte"), 860, 640, owner);
        static string N(double? value) => value?.ToString("0.###") ?? L.T("nicht verfügbar");
        var header = new TextBlock { Text = $"VMAF {N(result.Vmaf)}    SSIM {N(result.Ssim)}    PSNR {N(result.Psnr)} dB", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var canvas = new Canvas { Height = 180, Background = new SolidColorBrush(Color.Parse("#10151F")) };
        var metric = new ComboBox { ItemsSource = new[] { "VMAF", "SSIM", "PSNR" }, SelectedIndex = result.Vmaf.HasValue ? 0 : result.Ssim.HasValue ? 1 : 2, Width = 140 };
        var rows = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,monospace"), Height = 220 };
        void Draw()
        {
            canvas.Children.Clear();
            double? Value(QualityFrame frame) => metric.SelectedIndex switch { 1 => frame.Ssim, 2 => frame.Psnr, _ => frame.Vmaf };
            var ranked = result.Frames.OrderBy(frame => Value(frame) ?? double.PositiveInfinity).ToList();
            rows.Text = string.Join(Environment.NewLine, ranked.Take(40).Select(frame => $"{frame.Seconds:0.000}s  VMAF {N(frame.Vmaf)}  SSIM {N(frame.Ssim)}  PSNR {N(frame.Psnr)}"));
            var series = result.Frames.Select((frame, index) => (index, Value: Value(frame))).Where(point => point.Value.HasValue).ToList();
            if (series.Count == 0) return;
            var min = series.Min(point => point.Value!.Value);
            var max = series.Max(point => point.Value!.Value);
            var span = Math.Max(0.0001, max - min);
            var width = Math.Max(10, canvas.Bounds.Width);
            var line = new global::Avalonia.Controls.Shapes.Polyline { Stroke = Brushes.White, StrokeThickness = 2, Points = [] };
            foreach (var point in series)
            {
                var x = series.Count == 1 ? width / 2 : point.index * (width - 8) / Math.Max(1, result.Frames.Count - 1);
                var y = 8 + (1 - (point.Value!.Value - min) / span) * 160;
                line.Points.Add(new global::Avalonia.Point(x, y));
            }
            canvas.Children.Add(line);
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
                Docked(Dock.Top, new StackPanel { Spacing = 8, Children = { header, new TextBlock { Text = result.Conditions, TextWrapping = TextWrapping.Wrap }, metric, canvas } }),
                Docked(Dock.Bottom, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { csv, json, close } }),
                rows
            }
        };
        dialog.Opened += (_, _) => Draw();
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
        var themes = new ComboBox { ItemsSource = ThemeCatalog.All, SelectedItem = ThemeCatalog.Resolve(draft.ThemeId, draft.DarkMode) };
        themes.ItemTemplate = new FuncDataTemplate<AppTheme>((theme, _) => new TextBlock { Text = theme is null ? "" : theme.Name + " · " + theme.Description });
        themes.SelectionChanged += (_, _) =>
        {
            if (themes.SelectedItem is not AppTheme theme) return;
            draft.ThemeId = theme.Id;
            draft.DarkMode = theme.IsDark;
            (global::Avalonia.Application.Current as App)?.ApplyTheme(draft);
        };
        return new StackPanel { Spacing = 8, Margin = new global::Avalonia.Thickness(4), Children = { Label(L.T("Darstellung")), themes } };
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

    private static Control NamingTab(AppSettings draft)
    {
        var naming = draft.FileNaming;
        var pattern = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Refresh() => pattern.Text = naming.Enabled ? FileNaming.Pattern(naming) : L.T("Ist diese Option aus, gilt das individuelle Dateinamenschema im Video-Reiter.");
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
                pattern
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
