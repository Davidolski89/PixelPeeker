using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Desktop;

internal static class Ui
{
    public static Button Button(string text,RoutedEventHandler action)
    {
        var b=new Button {Content=text};ButtonHints.SetKey(b,ButtonHelpCatalog.KeyForLabel(text));b.Click+=action;return b;
    }
    public static TextBlock Label(string text)
    {
        var label=new TextBlock{Text=text,Margin=new Thickness(0,12,0,5)};
        label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return label;
    }
    public static Window Prepare(Window window,string title,int width=740,int height=640)
    {
        window.Title=title;window.Width=width;window.Height=height;window.MinWidth=440;window.MinHeight=300;window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        window.SetResourceReference(Window.BackgroundProperty,"Bg");window.SetResourceReference(Window.ForegroundProperty,"Text");
        window.SetResourceReference(Control.FontFamilyProperty,"UiFontFamily");
        ButtonHints.Initialize();
        window.Loaded+=(_,_)=>BindTranslations(window);
        return window;
    }
    private static void BindTranslations(Window window)
    {
        var visited=new HashSet<DependencyObject>();
        void Bind(DependencyObject control,DependencyProperty property)
        {
            if(BindingOperations.IsDataBound(control,property)||control.ReadLocalValue(property) is not string text||L.SourceOf(text) is not {} source)return;
            if(control is FrameworkElement element)element.SetResourceReference(property,L.ResourceKey(source));
            else if(control is DataGridColumn column&&property==DataGridColumn.HeaderProperty)
                BindingOperations.SetBinding(column,property,TranslatedValueExtension.ForText(source));
        }
        void Visit(DependencyObject control)
        {
            if(!visited.Add(control))return;
            if(control is Window)Bind(control,Window.TitleProperty);
            if(control is TextBlock)Bind(control,TextBlock.TextProperty);
            if(control is ContentControl)Bind(control,ContentControl.ContentProperty);
            if(control is HeaderedContentControl)Bind(control,HeaderedContentControl.HeaderProperty);
            if(control is FrameworkElement)Bind(control,FrameworkElement.ToolTipProperty);
            if(control is DataGrid grid)foreach(var column in grid.Columns)Bind(column,DataGridColumn.HeaderProperty);
            foreach(var child in LogicalTreeHelper.GetChildren(control).OfType<DependencyObject>())Visit(child);
            if(control is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(control);i++)Visit(VisualTreeHelper.GetChild(control,i));
        }
        Visit(window);
    }
    public static TextBox TextBox(object source,string property)
    {
        var control=new TextBox();
        control.SetBinding(System.Windows.Controls.TextBox.TextProperty,new Binding(property){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true});
        return control;
    }
}

public sealed class TextDialog : Window
{
    public string Value {get;private set;}="";
    private TextDialog(string title,string label,string initial)
    {
        Ui.Prepare(this,title,520,220);MinHeight=200;ResizeMode=ResizeMode.NoResize;
        var root=new StackPanel{Margin=new Thickness(20)};
        root.Children.Add(Ui.Label(label));var input=new TextBox{Text=initial};root.Children.Add(input);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        buttons.Children.Add(Ui.Button(L.T("Speichern"),(_,_)=>{Value=input.Text;DialogResult=true;}));buttons.Children.Add(Ui.Button(L.T("Abbrechen"),(_,_)=>DialogResult=false));root.Children.Add(buttons);Content=root;
        Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
    }
    public static string? Ask(string title,string label,string initial)
    {
        var dialog=new TextDialog(title,label,initial){Owner=System.Windows.Application.Current.MainWindow};
        return dialog.ShowDialog()==true?dialog.Value:null;
    }
}

public sealed class TextWindow : Window
{
    public TextWindow(string title,string text)
    {
        Ui.Prepare(this,title,980,720);
        Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12,Margin=new Thickness(16)};
    }
}

public static class JsonEditor
{
    public static T? Edit<T>(string title,T value) where T:class
    {
        var window=Ui.Prepare(new Window{Owner=System.Windows.Application.Current.MainWindow},title,880,760);
        var root=new DockPanel{Margin=new Thickness(16)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var input=new TextBox{Text=JsonSerializer.Serialize(value,Json.Options),AcceptsReturn=true,AcceptsTab=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas"),FontSize=12};
        T? result=null;
        buttons.Children.Add(Ui.Button(L.T("Übernehmen"),(_,_)=>{try{result=JsonSerializer.Deserialize<T>(input.Text,Json.Options)??throw new JsonException(L.T("Leeres Objekt."));window.DialogResult=true;}catch(Exception e){MessageBox.Show(e.Message,L.T("Ungültiges JSON"));}}));
        buttons.Children.Add(Ui.Button(L.T("Abbrechen"),(_,_)=>window.DialogResult=false));root.Children.Add(input);window.Content=root;
        return window.ShowDialog()==true?result:null;
    }
}

public sealed class EncoderOptionsWindow : Window
{
    private readonly Dictionary<string,Control> inputs=[];
    public Dictionary<string,string> Values {get;}=[];
    public EncoderOptionsWindow(EncoderInfo encoder,Dictionary<string,string> current)
    {
        Ui.Prepare(this,L.T("Expertenoptionen · ")+encoder.Name,900,760);
        var root=new DockPanel{Margin=new Thickness(18)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        buttons.Children.Add(Ui.Button(L.T("Encoderhilfe anzeigen"),(_,_)=>new TextWindow(encoder.Name,encoder.Help){Owner=this}.Show()));
        buttons.Children.Add(Ui.Button(L.T("Übernehmen"),(_,_)=>
        {
            foreach(var (key,control) in inputs)
            {
                var text=control is ComboBox combo?combo.Text:((TextBox)control).Text;
                if(!string.IsNullOrWhiteSpace(text))Values[key]=text.Trim();
            }
            DialogResult=true;
        }));
        buttons.Children.Add(Ui.Button(L.T("Abbrechen"),(_,_)=>DialogResult=false));
        var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        stack.Children.Add(new TextBlock{Text=L.T("Tatsächlich erkannte Optionen dieser FFmpeg-Version. Leere Felder verwenden den Encoderstandard. Bereits im einfachen Modus gesetzte Optionen hier nicht doppelt setzen."),Margin=new Thickness(0,0,0,15)});
        var managed=new HashSet<string>(EncoderOptionEditor.Managed);
        foreach(var option in encoder.Options??[])
        {
            if(managed.Contains(option.Name))continue;
            stack.Children.Add(Ui.Label(option.Name+" · "+option.Type));
            var description=new TextBlock{Text=option.Description,FontSize=11,Margin=new Thickness(0,0,0,5)};
            description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(description);
            Control input=option.Choices.Count>0?new ComboBox{ItemsSource=new[]{""}.Concat(option.Choices),IsEditable=true,Text=current.GetValueOrDefault(option.Name,"")}:
                new TextBox{Text=current.GetValueOrDefault(option.Name,"")};
            stack.Children.Add(input);inputs[option.Name]=input;
        }
        Content=root;
    }
}

public sealed class SettingsWindow : Window
{
    public AppSettings Result {get;}
    public SettingsWindow(AppSettings settings,MediaInfo? media=null,EncodingOptions? options=null)
    {
        Result=Json.Clone(settings);Ui.Prepare(this,L.T("Einstellungen & Ordnerüberwachung"),940,800);
        var originalTheme=App.CurrentTheme.Id;
        var resolvedTheme=ThemeCatalog.Resolve(Result.ThemeId,Result.DarkMode);
        Result.ThemeId=resolvedTheme.Id;Result.DarkMode=resolvedTheme.IsDark;
        Closed+=(_,_)=>{if(DialogResult!=true)App.ApplyTheme(originalTheme);};
        var root=new DockPanel{Margin=new Thickness(18)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var tabs=new TabControl();root.Children.Add(tabs);
        var general=new StackPanel{Margin=new Thickness(5)};
        general.Children.Add(Ui.Label(L.T("Sprache")));
        var language=new ComboBox{Name="LanguageSelector",ItemsSource=LanguageOption.All,DisplayMemberPath=nameof(LanguageOption.Name),SelectedValuePath=nameof(LanguageOption.Code)};
        language.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,new Binding(nameof(Result.Language)){Source=Result,Mode=BindingMode.TwoWay});
        general.Children.Add(language);
        general.Children.Add(new TextBlock{Text=L.T("Die Sprache wird beim Speichern sofort übernommen."),FontSize=12,Margin=new Thickness(0,4,0,12)});
        var tooltips=new CheckBox{Name="TooltipsEnabledInput",Content=L.T("Tooltips anzeigen")};
        tooltips.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(Result.TooltipsEnabled)){Source=Result,Mode=BindingMode.TwoWay});general.Children.Add(tooltips);
        general.Children.Add(Ui.Label(L.T("Wartezeit für Tooltips (Sekunden; 0–30)")));
        var tooltipDelay=Ui.TextBox(Result,nameof(Result.TooltipDelaySeconds));tooltipDelay.Name="TooltipDelayInput";
        tooltipDelay.SetBinding(IsEnabledProperty,new Binding(nameof(CheckBox.IsChecked)){Source=tooltips});general.Children.Add(tooltipDelay);
        general.Children.Add(new TextBlock{Text=L.T("Standard: 2 Sekunden. Die Wartezeit gilt für jeden neuen Hinweis. Änderungen werden mit Speichern übernommen."),FontSize=12,Margin=new Thickness(0,4,0,12)});
        var testNotice=new CheckBox{Name="TestEncodeNotificationInput",Content=L.T("Hinweis beim Start eines Testencodes anzeigen")};
        testNotice.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(Result.ShowTestEncodeNotification)){Source=Result,Mode=BindingMode.TwoWay});general.Children.Add(testNotice);
        tabs.Items.Add(new TabItem{Header=L.T("Allgemein"),Content=new ScrollViewer{Content=general,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var themePicker=new ThemePickerControl(Result.ThemeId);
        themePicker.ThemeSelected+=theme=>{Result.ThemeId=theme.Id;Result.DarkMode=theme.IsDark;App.ApplyTheme(theme.Id);};
        tabs.Items.Add(new TabItem{Name="AppearanceSettingsTab",Header=L.T("Darstellung"),Content=themePicker});
        void Check(string label,string key)
        {
            var control=new CheckBox{Content=label};
            control.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(key){Source=Result,Mode=BindingMode.TwoWay});
            general.Children.Add(control);
        }
        void Number(string label,string key){general.Children.Add(Ui.Label(label));general.Children.Add(Ui.TextBox(Result,key));}
        Check(L.T("Unterordner beim manuellen Import einlesen"),nameof(Result.RecursiveImport));
        Check(L.T("Encodes im Windows-Effizienzmodus starten"),nameof(Result.EfficiencyMode));
        Check(L.T("Beim Start und regelmäßig nach FFmpeg-Updates suchen"),nameof(Result.CheckUpdates));
        Check(L.T("FFmpeg automatisch installieren, wenn es fehlt"),nameof(Result.AutoInstall));
        Check(L.T("Verwaltete FFmpeg-Installation automatisch aktualisieren"),nameof(Result.AutoUpdate));
        Number(L.T("Updateintervall in Stunden (1–720)"),nameof(Result.UpdateIntervalHours));Number(L.T("Maximale parallele Jobs (1–16)"),nameof(Result.ParallelJobs));
        Number(L.T("Maximale parallele NVIDIA-Jobs (1–8)"),nameof(Result.ParallelGpuJobs));Number(L.T("PNG-Cachelimit in MiB (32–16384)"),nameof(Result.CacheLimitMiB));
        Number(L.T("Standard-Ausgabeordner (leer = Unterordner neben Quelle)"),nameof(Result.DefaultOutputDirectory));
        general.Children.Add(Ui.Label(L.T("Aktion, wenn sämtliche Jobs erfolgreich abgeschlossen sind")));
        var completion=new ComboBox{ItemsSource=Enum.GetValues<CompletionAction>(),ItemTemplate=(DataTemplate)FindResource("LocalizedItemTemplate")};
        completion.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new Binding(nameof(Result.CompletionAction)){Source=Result,Mode=BindingMode.TwoWay});
        general.Children.Add(completion);
        general.Children.Add(new TextBlock{Text=L.T("Energieaktionen gelten nur für diese Sitzung. Vor Ausführung gibt es einen abbrechbaren 30-Sekunden-Countdown. Fehlgeschlagene, abgebrochene oder zurückgestellte Jobs verhindern die Aktion."),FontSize=12,Margin=new Thickness(0,5,0,10)});
        var watchRoot=new DockPanel();
        var intro=new TextBlock{Text=L.T("Für jede Regel einen eigenen Ausgangsordner und ein vorhandenes Preset angeben. Dateien müssen mehrfach unverändert und exklusiv lesbar sein. Eigene Ausgabeordner werden ausgeschlossen."),Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(intro,Dock.Top);watchRoot.Children.Add(intro);
        var rules=new ObservableCollection<WatchRule>(Result.WatchFolders);
        var ruleList=new ListBox{ItemsSource=rules,DisplayMemberPath=nameof(WatchRule.Folder),MinHeight=120};
        var watchButtons=new WrapPanel();DockPanel.SetDock(watchButtons,Dock.Bottom);watchRoot.Children.Add(watchButtons);
        watchButtons.Children.Add(Ui.Button(L.T("Regel hinzufügen"),(_,_)=>{var edited=EditRule(new WatchRule());if(edited is not null)rules.Add(edited);}));
        watchButtons.Children.Add(Ui.Button(L.T("Regel bearbeiten"),(_,_)=>{if(ruleList.SelectedItem is not WatchRule rule)return;var edited=EditRule(rule);if(edited is not null)rules[rules.IndexOf(rule)]=edited;}));
        watchButtons.Children.Add(Ui.Button(L.T("Regel entfernen"),(_,_)=>{if(ruleList.SelectedItem is WatchRule rule)rules.Remove(rule);}));
        watchRoot.Children.Add(ruleList);
        tabs.Items.Add(new TabItem{Header=L.T("Ordnerüberwachung"),Content=watchRoot});
        tabs.Items.Add(new TabItem{Name="FileNamingSettingsTab",Header=L.T("Dateinamen"),Content=new FileNamingControl(Result.FileNaming,media,options)});
        buttons.Children.Add(Ui.Button(L.T("Speichern"),(_,_)=>
        {
            try
            {
                if(Result.TooltipsEnabled&&(Validation.GetHasError(tooltipDelay)||!double.IsFinite(Result.TooltipDelaySeconds)||Result.TooltipDelaySeconds is < 0 or > TooltipTiming.MaximumSeconds))
                    throw new ArgumentException(L.T("Die Tooltip-Wartezeit muss zwischen 0 und 30 Sekunden liegen."));
                Result.TooltipDelaySeconds=TooltipTiming.Normalize(Result.TooltipDelaySeconds);
                Result.WatchFolders=rules.ToList();foreach(var rule in Result.WatchFolders.Where(r=>r.Enabled))FolderWatchService.ValidateRule(rule);DialogResult=true;
            }
            catch(Exception e){MessageBox.Show(e.Message,L.T("Einstellungen"));}
        }));
        buttons.Children.Add(Ui.Button(L.T("Abbrechen"),(_,_)=>DialogResult=false));Content=root;
    }
    private WatchRule? EditRule(WatchRule source)
    {
        var rule=Json.Clone(source);
        var window=Ui.Prepare(new Window{Owner=this},L.T("Überwachungsregel"),680,650);
        var panel=new StackPanel{Margin=new Thickness(20)};
        foreach(var (label,key) in new[]{(L.T("Eingangsordner"),nameof(rule.Folder)),(L.T("Ausgabeordner"),nameof(rule.OutputFolder)),(L.T("Presetname"),nameof(rule.PresetName)),(L.T("Dateiendungen, mit Semikolon getrennt (* = alle)"),nameof(rule.Extensions))})
        {
            panel.Children.Add(Ui.Label(label));var input=Ui.TextBox(rule,key);panel.Children.Add(input);
            if(key is nameof(rule.Folder) or nameof(rule.OutputFolder))panel.Children.Add(Ui.Button(L.T("Ordner auswählen"),(_,_)=>{var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog()==true){input.Text=dialog.FolderName;input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();}}));
        }
        foreach(var (label,key) in new[]{(L.T("Überwachung aktiv"),nameof(rule.Enabled)),(L.T("Unterordner einbeziehen"),nameof(rule.Recursive)),(L.T("Neue Jobs automatisch starten"),nameof(rule.AutoStart))})
        {
            var control=new CheckBox{Content=label};control.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(key){Source=rule,Mode=BindingMode.TwoWay});panel.Children.Add(control);
        }
        panel.Children.Add(Ui.Button(L.T("Übernehmen"),(_,_)=>{try{if(rule.Enabled)FolderWatchService.ValidateRule(rule);window.DialogResult=true;}catch(Exception e){MessageBox.Show(e.Message);}}));
        window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        return window.ShowDialog()==true?rule:null;
    }
}

public sealed class ManagementWindow : Window
{
    public ManagementWindow(MainViewModel vm)
    {
        Ui.Prepare(this,L.T("FFmpeg-Verwaltung"),760,540);DataContext=vm;
        var root=new StackPanel{Margin=new Thickness(24)};
        root.Children.Add(new TextBlock{Text=L.T("FFmpeg Git-Master · Full Build"),FontSize=23,FontWeight=FontWeights.SemiBold});
        root.Children.Add(Ui.Label(L.T("Quelle: gyan.dev · SHA-256-Prüfung · getrennte Versionen · Rollback")));
        foreach(var (label,binding) in new[]{(L.T("Installiert"),nameof(vm.InstalledVersion)),(L.T("Verfügbar"),nameof(vm.AvailableVersion))})
        {
            root.Children.Add(Ui.Label(label));var text=new TextBlock{FontSize=17};text.SetBinding(TextBlock.TextProperty,binding);root.Children.Add(text);
        }
        var buttons=new WrapPanel{Margin=new Thickness(0,18,0,12)};
        foreach(var (text,command) in new[]{(L.T("Updates prüfen"),"CheckUpdatesCommand"),(L.T("Herunterladen & installieren"),"InstallCommand"),(L.T("Lokale Programme …"),"SelectLocalCommand"),(L.T("Vorherige Version aktivieren"),"RollbackCommand"),(L.T("Abbrechen"),"CancelOperationCommand")})
        {var b=new Button{Content=text};ButtonHints.SetKey(b,command[..^7]);b.SetBinding(Button.CommandProperty,command);buttons.Children.Add(b);}
        root.Children.Add(buttons);
        var progress=new ProgressBar{Height=8};progress.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new Binding(nameof(vm.DownloadPercent)){TargetNullValue=0});root.Children.Add(progress);
        var status=new TextBlock{Margin=new Thickness(0,12,0,12)};status.SetBinding(TextBlock.TextProperty,nameof(vm.StatusText));root.Children.Add(status);
        root.Children.Add(new TextBlock{Text=L.T("Laufende Jobs verwenden ihren gespeicherten Programmpfad. Eine neue Version wird erst nach erfolgreichem Download, Hashprüfung und Programmstart aktiviert. Benutzerdefinierte Installationen werden nicht überschrieben.\n\nGyan-Builds: GPLv3. Lizenz- und Buildinformationen werden mit der Installation aufbewahrt."),FontSize=12});
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}

public sealed class CountdownWindow : Window
{
    public CountdownWindow(CompletionAction action)
    {
        Ui.Prepare(this,L.T("Aktion nach Queue-Abschluss"),560,240);MinHeight=220;
        int remaining=30;var root=new StackPanel{Margin=new Thickness(24)};
        var label=new TextBlock{FontSize=18,Margin=new Thickness(0,0,0,20)};root.Children.Add(label);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        void Update()=>label.Text=L.F($"{L.EnumText(action)} in {remaining} Sekunden.\nAlle Jobs sind erfolgreich abgeschlossen.");
        timer.Tick+=(_,_)=>{remaining--;Update();if(remaining<=0){timer.Stop();DialogResult=true;}};
        root.Children.Add(Ui.Button(L.T("Aktion abbrechen"),(_,_)=>DialogResult=false));Content=root;Update();
        Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>timer.Stop();
    }
}
