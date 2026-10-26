using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Desktop;

public sealed class TestEncodeNotificationWindow : Window
{
    private readonly CheckBox suppress=new(){Name="DontShowAgainInput"};
    private readonly TextBlock message=new(){Name="TestEncodeStatus",FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    public bool DontShowAgain=>suppress.IsChecked==true;
    public static string StatusMessage(EncodingJob job) => TestEncodeNotice.StatusMessage(job);
    public TestEncodeNotificationWindow(EncodingJob job)
    {
        Ui.Prepare(this,L.T("Testencode"),600,260);MinHeight=200;ResizeMode=ResizeMode.NoResize;SizeToContent=SizeToContent.Height;ShowInTaskbar=false;
        var panel=new StackPanel{Margin=new Thickness(22)};Content=panel;
        panel.Children.Add(message);
        var file=new TextBlock{Text=Path.GetFileName(job.OutputPath),TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,Margin=new Thickness(0,0,0,10),ToolTip=job.OutputPath};
        ButtonHints.SetUsePreferences(file,true);file.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(file);
        panel.Children.Add(new TextBlock{Text=L.T("Fortschritt und Details findest du in der Queue. Das Schließen dieses Hinweises stoppt den Encode nicht."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var footer=new DockPanel{LastChildFill=false};panel.Children.Add(footer);
        var close=Ui.Button(L.T("Schließen"),(_,_)=>Close());close.Name="CloseTestEncodeNotice";close.IsDefault=true;close.IsCancel=true;close.MinWidth=90;
        DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
        suppress.Content=L.T("Nicht mehr anzeigen");suppress.VerticalAlignment=VerticalAlignment.Center;suppress.Margin=new Thickness(0,0,14,0);
        suppress.ToolTip=L.T("Der Hinweis bleibt künftig ausgeblendet. Unter Einstellungen → Allgemein kannst du ihn wieder einschalten.");
        ButtonHints.SetUsePreferences(suppress,true);footer.Children.Add(suppress);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        void Refresh()=>message.Text=StatusMessage(job);
        timer.Tick+=(_,_)=>Refresh();Loaded+=(_,_)=>{Refresh();timer.Start();};Closed+=(_,_)=>timer.Stop();Refresh();
    }
}
