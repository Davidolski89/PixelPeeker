using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public partial class EncodeComparisonControl : UserControl
{
    private bool syncing;
    public EncodeComparisonControl()
    {
        InitializeComponent();
        foreach(var strip in new[]{AvailabilityA,AvailabilityB})strip.SeekRequested+=seconds=>
        {if(DataContext is EncodeComparisonViewModel vm)vm.Position=seconds;};
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Space&&Keyboard.Modifiers==ModifierKeys.None&&!Editing(e.OriginalSource as DependencyObject)&&DataContext is EncodeComparisonViewModel vm&&vm.BeginReferenceHold())e.Handled=true;
        };
        PreviewKeyUp+=(_,e)=>{if(e.Key==Key.Space&&DataContext is EncodeComparisonViewModel vm){vm.EndReferenceHold();e.Handled=true;}};
        Unloaded+=(_,_)=>{if(DataContext is EncodeComparisonViewModel vm)vm.EndReferenceHold();};
        foreach(var pane in new[]{OriginalPane,PaneA,PaneB})pane.Scroll.ScrollChanged+=(_,e)=>
        {
            if(syncing||e.HorizontalChange==0&&e.VerticalChange==0)return;
            syncing=true;
            try
            {
                foreach(var other in new[]{OriginalPane,PaneA,PaneB}.Where(p=>p!=pane))
                {
                    other.Scroll.ScrollToHorizontalOffset(pane.Scroll.ScrollableWidth>0?pane.Scroll.HorizontalOffset/pane.Scroll.ScrollableWidth*other.Scroll.ScrollableWidth:0);
                    other.Scroll.ScrollToVerticalOffset(pane.Scroll.ScrollableHeight>0?pane.Scroll.VerticalOffset/pane.Scroll.ScrollableHeight*other.Scroll.ScrollableHeight:0);
                }
            }
            finally{syncing=false;}
        };
    }
    private static bool Editing(DependencyObject? element)
    {
        while(element is not null)
        {
            if(element is TextBoxBase or ComboBox or ToggleButton)return true;
            element=element is System.Windows.Media.Visual?System.Windows.Media.VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private void Timeline_KeyDown(object sender,KeyEventArgs e)
    {
        if(DataContext is not EncodeComparisonViewModel vm||Keyboard.Modifiers!=ModifierKeys.None||e.Key is not (Key.Left or Key.Right))return;
        e.Handled=true;var command=e.Key==Key.Left?vm.PreviousCommand:vm.NextCommand;if(command.CanExecute(null))command.Execute(null);
    }
}
public sealed class EncodeComparisonWindow : Window
{
    internal static EncodeComparisonWindow ShowFor(Window owner,EncodeComparisonViewModel vm)
    {
        var existing=owner.OwnedWindows.OfType<EncodeComparisonWindow>().FirstOrDefault(w=>ReferenceEquals(w.DataContext,vm));
        if(existing is not null)
        {
            if(existing.WindowState==WindowState.Minimized)existing.WindowState=WindowState.Maximized;
            existing.Activate();return existing;
        }
        var window=new EncodeComparisonWindow(vm){Owner=owner};window.Show();return window;
    }
    public EncodeComparisonWindow(EncodeComparisonViewModel vm)
    {
        SetResourceReference(FontFamilyProperty,"UiFontFamily");
        SetResourceReference(TitleProperty,L.ResourceKey("Vergleich"));Width=1500;Height=950;MinWidth=900;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowState=WindowState.Maximized;
        DataContext=vm;Content=new EncodeComparisonControl{Margin=new Thickness(16)};
        Deactivated+=(_,_)=>vm.EndReferenceHold();Closed+=(_,_)=>vm.EndReferenceHold();
    }
}
