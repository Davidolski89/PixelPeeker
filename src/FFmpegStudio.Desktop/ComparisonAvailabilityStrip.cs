using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

/// <summary>Source-clock coverage. Solid = retained, hatching = missing, dots = unknown cut map.</summary>
public sealed class ComparisonAvailabilityStrip : Control
{
    public static readonly DependencyProperty IntervalsProperty=DependencyProperty.Register(nameof(Intervals),typeof(IReadOnlyList<ComparisonInterval>),typeof(ComparisonAvailabilityStrip),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty=DependencyProperty.Register(nameof(Duration),typeof(double),typeof(ComparisonAvailabilityStrip),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PositionProperty=DependencyProperty.Register(nameof(Position),typeof(double),typeof(ComparisonAvailabilityStrip),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ComparisonInterval>? Intervals{get=>(IReadOnlyList<ComparisonInterval>?)GetValue(IntervalsProperty);set=>SetValue(IntervalsProperty,value);}
    public double Duration{get=>(double)GetValue(DurationProperty);set=>SetValue(DurationProperty,value);}
    public double Position{get=>(double)GetValue(PositionProperty);set=>SetValue(PositionProperty,value);}
    public event Action<double>? SeekRequested;
    public ComparisonAvailabilityStrip()
    {
        Cursor=Cursors.Hand;Focusable=false;
        ToolTip="";ButtonHints.SetUsePreferences(this,true);
        ToolTipOpening+=(_,_)=>
        {
            var time=TimeAt(Mouse.GetPosition(this).X);
            var interval=Intervals?.FirstOrDefault(i=>time>=i.Start&&time<i.End)??Intervals?.LastOrDefault();
            ToolTip=interval is null?L.T("Schnittzuordnung unbekannt"):
                $"{TimeSpan.FromSeconds(interval.Start):hh\\:mm\\:ss\\.fff} – {TimeSpan.FromSeconds(interval.End):hh\\:mm\\:ss\\.fff}\n"+
                L.T(interval.Availability switch{ComparisonAvailability.Available=>"Im Encode vorhanden",ComparisonAvailability.Unavailable=>"Entfernt / außerhalb des Ausschnitts",_=>"Schnittzuordnung unbekannt"});
        };
    }
    public double TimeAt(double x)=>ActualWidth>0&&double.IsFinite(Duration)?Math.Clamp(x/ActualWidth,0,1)*Duration:0;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {base.OnMouseLeftButtonDown(e);SeekRequested?.Invoke(TimeAt(e.GetPosition(this).X));e.Handled=true;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(Duration<=0||!double.IsFinite(Duration)||ActualWidth<=0||ActualHeight<=0)return;
        var bounds=new Rect(0,0,ActualWidth,ActualHeight);dc.PushClip(new RectangleGeometry(bounds));
        dc.DrawRectangle(Background,null,bounds);
        foreach(var segment in Intervals??[])
        {
            var x=Math.Clamp(segment.Start/Duration,0,1)*ActualWidth;var end=Math.Clamp(segment.End/Duration,0,1)*ActualWidth;
            var rect=new Rect(x,0,Math.Max(0,end-x),ActualHeight);
            if(segment.Availability==ComparisonAvailability.Available)dc.DrawRectangle(Foreground,null,rect);
            else
            {
                dc.PushClip(new RectangleGeometry(rect));
                if(segment.Availability==ComparisonAvailability.Unavailable)
                {
                    dc.PushOpacity(.18);dc.DrawRectangle(BorderBrush,null,rect);dc.Pop();
                    for(var p=x-ActualHeight;p<end;p+=7)dc.DrawLine(new Pen(BorderBrush,1),new(p,ActualHeight),new(p+ActualHeight,0));
                }
                else for(var p=x+3;p<end;p+=7)dc.DrawEllipse(BorderBrush,null,new(p,ActualHeight/2),1,1);
                dc.Pop();
            }
        }
        var marker=Math.Clamp(Position/Duration,0,1)*ActualWidth;
        dc.DrawLine(new Pen(Brushes.Black,3),new(marker,0),new(marker,ActualHeight));
        dc.DrawLine(new Pen(Brushes.White,1),new(marker,0),new(marker,ActualHeight));dc.Pop();
    }
}
