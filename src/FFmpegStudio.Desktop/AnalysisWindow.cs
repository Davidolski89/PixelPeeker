using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using FFmpegStudio.Core;
using Microsoft.Win32;

namespace FFmpegStudio.Desktop;

public sealed class AnalysisWindow : Window
{
    public AnalysisWindow(QualityResult result)
    {
        Ui.Prepare(this,L.T("Qualitätsanalyse · gespeicherte Messwerte"),1060,780);
        var root=new DockPanel{Margin=new Thickness(20)};
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text=$"VMAF {result.Vmaf:0.###}     SSIM {result.Ssim:0.######}     PSNR {result.Psnr:0.###} dB",FontSize=24,FontWeight=FontWeights.SemiBold});
        header.Children.Add(new TextBlock{Text=result.Conditions,FontSize=12,Margin=new Thickness(0,12,0,14)});
        var metric=new ComboBox{ItemsSource=new[]{"VMAF","SSIM","PSNR"},SelectedIndex=result.Vmaf.HasValue?0:result.Ssim.HasValue?1:2,Width=160,HorizontalAlignment=HorizontalAlignment.Left};
        header.Children.Add(metric);
        var chart=new Canvas{Height=220,Margin=new Thickness(0,5,0,12),ClipToBounds=true};chart.SetResourceReference(Panel.BackgroundProperty,"Bg");header.Children.Add(chart);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        buttons.Children.Add(Ui.Button(L.T("CSV exportieren"),async(_,_)=>
        {
            var dialog=new SaveFileDialog{Filter="CSV|*.csv",FileName="Qualitaetsverlauf.csv"};
            if(dialog.ShowDialog()!=true)return;
            var csv=new StringBuilder("seconds;vmaf;ssim;psnr\n");
            static string N(double? d)=>d?.ToString("R",CultureInfo.InvariantCulture)??"";
            foreach(var f in result.Frames)csv.AppendLine($"{N(f.Seconds)};{N(f.Vmaf)};{N(f.Ssim)};{N(f.Psnr)}");
            await File.WriteAllTextAsync(dialog.FileName,csv.ToString(),Encoding.UTF8);
        }));
        buttons.Children.Add(Ui.Button(L.T("JSON exportieren"),async(_,_)=>
        {
            var dialog=new SaveFileDialog{Filter="JSON|*.json",FileName="Analyse.json"};
            if(dialog.ShowDialog()==true)await File.WriteAllTextAsync(dialog.FileName,JsonSerializer.Serialize(result,Json.Options));
        }));
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,RowHeight=30};
        foreach(var (title,path) in new[]{(L.T("Zeit (s)"),nameof(QualityFrame.Seconds)),("VMAF",nameof(QualityFrame.Vmaf)),("SSIM",nameof(QualityFrame.Ssim)),("PSNR (dB)",nameof(QualityFrame.Psnr))})
            grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path){StringFormat="0.######"},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        var table=new DockPanel();var weak=new TextBlock{Text=L.T("Schwächste Frames zuerst · nach ausgewählter Metrik"),Margin=new Thickness(0,0,0,7)};DockPanel.SetDock(weak,Dock.Top);table.Children.Add(weak);table.Children.Add(grid);root.Children.Add(table);
        void Draw()
        {
            int selected=metric.SelectedIndex;
            double? Value(QualityFrame f)=>selected==0?f.Vmaf:selected==1?f.Ssim:f.Psnr;
            grid.ItemsSource=result.Frames.OrderBy(f=>Value(f)??double.PositiveInfinity).ToList();
            chart.Children.Clear();double w=Math.Max(100,chart.ActualWidth),h=chart.Height;
            var valid=result.Frames.Where(f=>Value(f) is { } v&&double.IsFinite(v)).ToList();
            if(valid.Count==0)return;
            var max=selected==0?100:selected==1?1:Math.Max(50,valid.Max(f=>Value(f)!.Value));
            var timeMax=Math.Max(.01,valid.Max(f=>f.Seconds));
            for(int i=0;i<=4;i++)
            {
                double y=15+(h-40)*i/4;
                var gridLine=new Line{X1=45,X2=w-10,Y1=y,Y2=y,StrokeThickness=1};
                gridLine.SetResourceReference(Shape.StrokeProperty,"Border");chart.Children.Add(gridLine);
                var label=new TextBlock{Text=(max*(1-i/4d)).ToString("0.##"),FontSize=10};Canvas.SetLeft(label,3);Canvas.SetTop(label,y-7);chart.Children.Add(label);
            }
            var polyline=new Polyline{StrokeThickness=1.5};polyline.SetResourceReference(Shape.StrokeProperty,"Accent");
            int step=Math.Max(1,valid.Count/4000);
            foreach(var f in valid.Where((_,i)=>i%step==0))polyline.Points.Add(new Point(45+f.Seconds/timeMax*(w-60),15+(1-Value(f)!.Value/max)*(h-40)));
            chart.Children.Add(polyline);
            var end=new TextBlock{Text=$"0 s → {timeMax:0.0} s",FontSize=10};Canvas.SetLeft(end,45);Canvas.SetTop(end,h-20);chart.Children.Add(end);
        }
        metric.SelectionChanged+=(_,_)=>Draw();chart.SizeChanged+=(_,_)=>Draw();Content=root;Loaded+=(_,_)=>Draw();
    }
}

public sealed class ResultsWindow : Window
{
    public ResultsWindow(List<EncodingJob> jobs)
    {
        Ui.Prepare(this,L.T("Encoding-Ergebnisse vergleichen"),1200,600);
        var root=new DockPanel{Margin=new Thickness(20)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text=L.T("Ergebnisse derselben Quelldatei"),FontSize=22,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text=L.T("Qualitätswerte nur bei gleichem Ausschnitt, Bildformat, Zeitraster und Analysebedingungen direkt vergleichen. Fehlende Werte bleiben leer."),Margin=new Thickness(0,10,0,12)});
        var source=new ComboBox{ItemsSource=jobs.Select(j=>j.Source.Path).Distinct().ToList(),SelectedIndex=0};top.Children.Add(source);
        var grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,RowHeight=40};
        foreach(var (title,path) in new[]{(L.T("Encoder"),"Encoder"),(L.T("Preset"),"Preset"),(L.T("Raster"),"Raster"),(L.T("Ausschnitt (s)"),"Clip"),("MiB","MiB"),(L.T("Dauer (s)"),"Seconds"),(L.T("Tempo"),"Speed"),("kbit/s","Bitrate"),("VMAF","Vmaf"),("SSIM","Ssim"),("PSNR","Psnr")})
            grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        void Refresh()=>grid.ItemsSource=jobs.Where(j=>j.Source.Path==(string?)source.SelectedItem).Select(j=>new
        {
            Encoder=j.Options.Video.Encoder,Preset=j.Options.Video.Preset,Raster=$"{j.OutputInfo?.Video?.Width} × {j.OutputInfo?.Video?.Height}",
            Clip=j.Options.ClipStart.HasValue?$"{j.Options.ClipStart:0.###} + {j.Options.ClipDuration:0.###}":L.T("Vollständig"),
            MiB=(j.OutputBytes/1048576d).ToString("0.00"),Seconds=(j.EndedAt-j.StartedAt)?.TotalSeconds.ToString("0.0"),Speed=j.Speed,
            Bitrate=(j.OutputInfo?.BitRate/1000d)?.ToString("0"),Vmaf=j.Analysis?.Vmaf?.ToString("0.###"),Ssim=j.Analysis?.Ssim?.ToString("0.######"),Psnr=j.Analysis?.Psnr?.ToString("0.###")
        }).ToList();
        source.SelectionChanged+=(_,_)=>Refresh();root.Children.Add(grid);Content=root;Refresh();
    }
}
