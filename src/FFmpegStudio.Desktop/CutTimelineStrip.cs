using System.Windows;
using System.Windows.Media;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public sealed class CutTimelineStrip : FrameworkElement
{
    public static readonly DependencyProperty CutsProperty=DependencyProperty.Register(nameof(Cuts),typeof(IReadOnlyList<CutRegion>),typeof(CutTimelineStrip),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty=DependencyProperty.Register(nameof(Duration),typeof(double),typeof(CutTimelineStrip),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty=DependencyProperty.Register(nameof(Active),typeof(bool),typeof(CutTimelineStrip),new FrameworkPropertyMetadata(true,FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<CutRegion>? Cuts{get=>(IReadOnlyList<CutRegion>?)GetValue(CutsProperty);set=>SetValue(CutsProperty,value);}
    public double Duration{get=>(double)GetValue(DurationProperty);set=>SetValue(DurationProperty,value);}
    public bool Active{get=>(bool)GetValue(ActiveProperty);set=>SetValue(ActiveProperty,value);}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(Duration<=0||ActualWidth<=0)return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50,128,128,128)),null,new Rect(0,0,ActualWidth,ActualHeight),2,2);
        var brush=new SolidColorBrush(Color.FromArgb(Active?(byte)220:(byte)70,240,90,100));
        foreach(var cut in Cuts??[])
        {
            double start=Math.Clamp(cut.Start.Seconds/Duration,0,1)*ActualWidth,end=Math.Clamp((cut.End?.Seconds??Duration)/Duration,0,1)*ActualWidth;
            dc.DrawRoundedRectangle(brush,null,new Rect(start,0,Math.Max(0,end-start),ActualHeight),2,2);
        }
    }
}
