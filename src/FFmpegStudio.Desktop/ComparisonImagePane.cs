using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FFmpegStudio.Desktop;

/// <summary>Fit or true pixel zoom. Each pane keeps its own scroll host; the parent synchronizes normalized offsets.</summary>
public sealed class ComparisonImagePane : Border
{
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(BitmapSource),typeof(ComparisonImagePane),new PropertyMetadata(null,(d,_)=>((ComparisonImagePane)d).UpdateImage()));
    public static readonly DependencyProperty ZoomProperty=DependencyProperty.Register(nameof(Zoom),typeof(double),typeof(ComparisonImagePane),new PropertyMetadata(0d,(d,_)=>((ComparisonImagePane)d).UpdateImage()));
    public static readonly DependencyProperty EmptyMessageProperty=DependencyProperty.Register(nameof(EmptyMessage),typeof(string),typeof(ComparisonImagePane),new PropertyMetadata("",(d,_)=>((ComparisonImagePane)d).UpdateMessage()));
    public string EmptyMessage{get=>(string)GetValue(EmptyMessageProperty);set=>SetValue(EmptyMessageProperty,value);}
    public BitmapSource? Source{get=>(BitmapSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value);}
    public double Zoom{get=>(double)GetValue(ZoomProperty);set=>SetValue(ZoomProperty,value);}
    public ScrollViewer Scroll{get;}=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,CanContentScroll=false};
    private readonly Image image=new(){Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock empty=new(){Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16),IsHitTestVisible=false};
    private Point? drag;private double x,y;
    public ComparisonImagePane()
    {
        Background=Brushes.Black;ClipToBounds=true;Scroll.Content=image;
        var content=new Grid();content.Children.Add(Scroll);content.Children.Add(empty);Child=content;
        MinHeight=70;UseLayoutRounding=true;image.SnapsToDevicePixels=true;
        Scroll.SizeChanged+=(_,_)=>UpdateImage();
        Scroll.PreviewMouseLeftButtonDown+=(_,e)=>{if(Zoom<=0)return;drag=e.GetPosition(this);x=Scroll.HorizontalOffset;y=Scroll.VerticalOffset;Scroll.CaptureMouse();e.Handled=true;};
        Scroll.PreviewMouseMove+=(_,e)=>{if(drag is not {} start||e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(this)-start;Scroll.ScrollToHorizontalOffset(x-delta.X);Scroll.ScrollToVerticalOffset(y-delta.Y);};
        Scroll.PreviewMouseLeftButtonUp+=(_,_)=>{drag=null;Scroll.ReleaseMouseCapture();};
    }
    private void UpdateImage()
    {
        image.Source=Source;UpdateMessage();
        if(Source is not {} bitmap){image.Width=image.Height=double.NaN;return;}
        var width=Math.Max(1,Scroll.ActualWidth-4);var height=Math.Max(1,Scroll.ActualHeight-4);
        var factor=Math.Min(width/bitmap.PixelWidth,height/bitmap.PixelHeight);var dpi=VisualTreeHelper.GetDpi(this);
        image.Width=bitmap.PixelWidth*(Zoom>0?Zoom/dpi.DpiScaleX:factor);image.Height=bitmap.PixelHeight*(Zoom>0?Zoom/dpi.DpiScaleY:factor);
        RenderOptions.SetBitmapScalingMode(image,Zoom>=1?BitmapScalingMode.NearestNeighbor:BitmapScalingMode.HighQuality);
    }
    private void UpdateMessage(){empty.Text=EmptyMessage;empty.Visibility=Source is null&&!string.IsNullOrWhiteSpace(EmptyMessage)?Visibility.Visible:Visibility.Collapsed;}
}
