using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Desktop;

public sealed class FileNamingControl : UserControl
{
    public FileNamingControl(FileNamingSettings settings,MediaInfo? media=null,EncodingOptions? options=null)
    {
        var source=media??new MediaInfo{Path=Path.GetFullPath("Beispielvideo.mkv"),Streams=[new(){Type="video",Width=1920,Height=1080,SampleAspectRatio="1:1",AverageFrameRate="24/1"}]};
        var sample=options is null?EncodingOptions.ForMedia(source):Json.Clone(options);
        if(options is null){sample.Video.Encoder="libsvtav1";sample.Video.Quality=24;sample.Video.Preset="4";}
        sample.FileNameTimestamp=DateTimeOffset.Now;
        var panel=new StackPanel{Margin=new Thickness(8)};
        var intro=new TextBlock{Text=L.T("Wähle die Angaben, die dem ursprünglichen Dateinamen angefügt werden. Die Dateiendung richtet sich automatisch nach dem Ausgabeformat."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};
        panel.Children.Add(intro);
        var enabled=new CheckBox{Name="UseGlobalFileNames",Content=L.T("Diese Namensauswahl für neue Queue-Aufträge verwenden"),IsChecked=settings.Enabled,Margin=new Thickness(0,0,0,12)};
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock{Text=L.T("Bereits angelegte Queue-Aufträge behalten ihren Namen. Ist diese Option aus, gilt das individuelle Dateinamenschema im Video-Reiter."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        var parts=new UniformGrid{Columns=2};panel.Children.Add(parts);
        parts.SetBinding(IsEnabledProperty,new Binding(nameof(ToggleButton.IsChecked)){Source=enabled});
        var pattern=new TextBox{Name="NamingPatternPreview",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Consolas")};
        var example=new TextBox{Name="NamingExamplePreview",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Consolas"),MinHeight=58};
        void Refresh()
        {
            pattern.Text=settings.Enabled?FileNaming.Pattern(settings):sample.FileNamePattern;
            try{example.Text=Path.GetFileName(QueueService.SuggestedOutputPath(source,sample,settings));}
            catch(Exception){example.Text=L.T("Für diese Einstellungen ist noch kein gültiger Dateiname verfügbar.");}
        }
        void Part(string name,string label,string help,Func<bool> read,Action<bool> write)
        {
            var check=new CheckBox{Name=name,Content=L.T(label),ToolTip=L.T(help),IsChecked=read(),Margin=new Thickness(0,6,12,6)};
            ButtonHints.SetUsePreferences(check,true);
            check.Checked+=(_,_)=>{write(true);Refresh();};check.Unchecked+=(_,_)=>{write(false);Refresh();};parts.Children.Add(check);
        }
        Part("NamingDate","Datum (JJJJ-MM-TT)","Lokales Datum beim Hinzufügen zur Queue; der Name ändert sich beim späteren Start nicht.",()=>settings.IncludeDate,v=>settings.IncludeDate=v);
        Part("NamingTime","Uhrzeit (HH-MM-SS)","Lokale Uhrzeit beim Hinzufügen zur Queue, ohne für Windows unzulässige Doppelpunkte.",()=>settings.IncludeTime,v=>settings.IncludeTime=v);
        Part("NamingResolution","Auflösung (Breite × Höhe)","Berechnete Ausgabeauflösung einschließlich Skalierung, Zuschnitt, Drehung und Padding.",()=>settings.IncludeResolution,v=>settings.IncludeResolution=v);
        Part("NamingCodec","Videocodec (AV1, HEVC …)","Das Kompressionsformat des ausgegebenen Videos, beispielsweise AV1 oder HEVC.",()=>settings.IncludeCodec,v=>settings.IncludeCodec=v);
        Part("NamingEncoder","Encoder (libsvtav1 …)","Der konkrete Videoencoder, beispielsweise libsvtav1 oder av1_nvenc.",()=>settings.IncludeEncoder,v=>settings.IncludeEncoder=v);
        Part("NamingQuality","Qualitätswert (CRF / CQ / QP)","Fügt den aktiven Qualitätsparameter mit seinem Wert an, zum Beispiel CRF24, CQ23 oder QP20. Entfällt bei Bitratenmodi.",()=>settings.IncludeQuality,v=>settings.IncludeQuality=v);
        Part("NamingBitrate","Video-Zielbitrate (kbit/s)","Fügt bei Zielbitrate oder CBR den eingestellten Videowert an, zum Beispiel 4000kbps. Dies ist keine Messung der fertigen Datei.",()=>settings.IncludeBitrate,v=>settings.IncludeBitrate=v);
        Part("NamingSpeed","Geschwindigkeitspreset (preset-4 …)","Der an den Encoder übergebene Geschwindigkeitswert, beispielsweise preset-4 oder preset-slow.",()=>settings.IncludeSpeed,v=>settings.IncludeSpeed=v);
        Part("NamingPreset","Name des gespeicherten Presets","Der Name des gewählten Anwendungspresets. Dies ist unabhängig vom Geschwindigkeitspreset des Encoders.",()=>settings.IncludePreset,v=>settings.IncludePreset=v);
        Part("NamingFrameRate","Bildrate (FPS)","Die eingestellte Ausgabebildrate oder, bei unveränderter Bildrate, die von ffprobe gemeldete durchschnittliche Quellbildrate.",()=>settings.IncludeFrameRate,v=>settings.IncludeFrameRate=v);
        panel.Children.Add(Ui.Label(L.T("Erzeugtes Dateinamenschema")));panel.Children.Add(pattern);
        panel.Children.Add(Ui.Label(L.T(media is null?"Beispiel mit SVT-AV1, CRF 24 und Preset 4":"Vorschau mit der aktuell ausgewählten Datei")));panel.Children.Add(example);
        panel.Children.Add(new TextBlock{Text=L.T("Qualitätswert und Zielbitrate erscheinen nur im jeweils aktiven Modus. Gleiche Namen erhalten weiterhin _2, _3 usw. Der Originalname bleibt immer enthalten."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        enabled.Checked+=(_,_)=>{settings.Enabled=true;Refresh();};enabled.Unchecked+=(_,_)=>{settings.Enabled=false;Refresh();};
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Refresh();
    }
}
