using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Desktop;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private bool closing;
    private Point dragStart;
    private EncodingJob? draggedJob;
    public MainWindow(MainViewModel viewModel)
    {
        ButtonHints.Initialize();ViewModel=viewModel; App.ApplyLanguage(L.Language); InitializeComponent(); DataContext=viewModel; Closing+=OnClosing;
        SizeChanged+=(_,_)=>UpdateWorkspaceSummary();
        UpdateWorkspaceSummary();
        viewModel.PlaybackViewRequested+=ShowPlayback;
        viewModel.CompareEncodes.Requested+=ShowEncodeComparison;
        Closed+=(_,_)=>{viewModel.PlaybackViewRequested-=ShowPlayback;viewModel.CompareEncodes.Requested-=ShowEncodeComparison;SourceVideoView.Dispose();};
        foreach(var column in QueueGrid.Columns)
            if(column.Header is string text&&L.SourceOf(text) is {} source)
                System.Windows.Data.BindingOperations.SetBinding(column,DataGridColumn.HeaderProperty,TranslatedValueExtension.ForText(source));
    }
    private async void Window_Drop(object sender,DragEventArgs e)
    {
        if(e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Handled=true; await ViewModel.ImportPathsAsync((string[])e.Data.GetData(DataFormats.FileDrop)); }
    }
    private void Window_DragOver(object sender,DragEventArgs e)
    {
        e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; e.Handled=true;
    }
    private async void AddSelected_Click(object sender,RoutedEventArgs e) => await ViewModel.AddManyAsync(MediaList.SelectedItems.Cast<MediaItem>().ToArray());
    private void ShowPlayback()=>MainPreviewTabs.SelectedItem=PreviewTab;
    private async void ShowEncodeComparison()
    {
        var comparison=ViewModel.CompareEncodes;comparison.RefreshJobs(ViewModel.Jobs);
        EncodeComparisonWindow.ShowFor(this,comparison);
        if(ViewModel.PauseSourceCommand.CanExecute(null))await ViewModel.PauseSourceCommand.ExecuteAsync(null);
    }
    private void CompareSelectedEncodes_Click(object sender,RoutedEventArgs e)
    {
        var comparison=ViewModel.CompareEncodes;
        var selected=QueueGrid.SelectedItems.Cast<EncodingJob>().Where(j=>j.Status==JobStatus.Abgeschlossen&&j.OutputInfo?.Video is not null).Take(2).ToArray();
        if(selected.Length>0&&(comparison.SelectedA?.Input.Job?.Id!=selected[0].Id||comparison.CompareTwoEncodes!=(selected.Length==2)||selected.Length==2&&comparison.SelectedB?.Input.Job?.Id!=selected[1].Id))comparison.SelectJobs(selected);
        else ShowEncodeComparison();
    }
    private void UpdateWorkspaceSummary()
    {
        var selected=MainPreviewTabs.SelectedItem;
        // Leave room for translated preset actions in the smallest window.
        if(PresetSelector is not null)PresetSelector.Width=ActualWidth<1300?180:280;
        if(ConversionSummaryCard is not null)ConversionSummaryCard.Visibility=selected==EncodeSettingsTab&&ActualHeight>=850?Visibility.Visible:Visibility.Collapsed;
        if(ValidationSummaryCard is not null)ValidationSummaryCard.Visibility=selected==PreviewTab||selected==EncodeSettingsTab?Visibility.Visible:Visibility.Collapsed;
    }
    private void PreviewLayout_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(PreviewWorkspace is null||PreviewControls is null||PreviewImageArea is null||PreviewSettingsColumn is null)return;
        // Reserve the controls' measured height before fitting the 16:9 surface.
        // Giving the remaining width to the settings keeps cut actions in view.
        var maximumWidth=Math.Max(0,PreviewWorkspace.ActualWidth-PreviewSettingsColumn.MinWidth-12);
        var maximumHeight=Math.Max(0,PreviewWorkspace.ActualHeight-PreviewControls.ActualHeight-PreviewControls.Margin.Top-PreviewControls.Margin.Bottom);
        var imageWidth=Math.Min(maximumWidth,maximumHeight*16/9);
        var paneWidth=Math.Min(maximumWidth,Math.Max(400,imageWidth));
        if(Math.Abs(PreviewPaneColumn.Width.Value-paneWidth)>.01)PreviewPaneColumn.Width=new GridLength(paneWidth);
        PreviewImageArea.Width=imageWidth;
        PreviewImageArea.Height=imageWidth*9/16;
    }
    private async void PreviewTabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ReferenceEquals(e.Source,MainPreviewTabs))return;
        var selected=MainPreviewTabs.SelectedItem;
        UpdateWorkspaceSummary();
        // A hidden native video surface must not keep playing audio on another page.
        if(selected!=PreviewTab&&ViewModel.PauseSourceCommand.CanExecute(null))await ViewModel.PauseSourceCommand.ExecuteAsync(null);
    }
    private void PreviewTimeline_KeyDown(object sender,KeyEventArgs e)
    {
        if(Keyboard.Modifiers!=ModifierKeys.None||e.Key is not (Key.Left or Key.Right or Key.Space))return;
        e.Handled=true;
        System.Windows.Input.ICommand command=e.Key==Key.Space?(ViewModel.PlaybackActive?ViewModel.PauseSourceCommand:ViewModel.PlaySourceCommand):
            ViewModel.PlaybackActive?(e.Key==Key.Left?ViewModel.SeekBackwardCommand:ViewModel.SeekForwardCommand):
            e.Key==Key.Left?ViewModel.PreviousFrameCommand:ViewModel.NextFrameCommand;
        if(command.CanExecute(null))command.Execute(null);
    }
    private void ApplySelected_Click(object sender,RoutedEventArgs e) => ViewModel.ApplyToMany(MediaList.SelectedItems.Cast<MediaItem>().ToArray());
    private void StartSelectedJobs_Click(object sender,RoutedEventArgs e) => ViewModel.StartSelectedJobs(QueueGrid.SelectedItems.Cast<EncodingJob>().ToArray());
    private void Queue_MouseDown(object sender,MouseButtonEventArgs e)
    {
        dragStart=e.GetPosition(QueueGrid); draggedJob=Ancestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as EncodingJob;
    }
    private void Queue_MouseMove(object sender,MouseEventArgs e)
    {
        var delta=e.GetPosition(QueueGrid)-dragStart;
        if(e.LeftButton==MouseButtonState.Pressed&&draggedJob is not null&&(Math.Abs(delta.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(delta.Y)>SystemParameters.MinimumVerticalDragDistance))
        { DragDrop.DoDragDrop(QueueGrid,new DataObject(typeof(EncodingJob),draggedJob),DragDropEffects.Move); draggedJob=null; }
    }
    private void Queue_DragOver(object sender,DragEventArgs e)
    {
        if(e.Data.GetDataPresent(typeof(EncodingJob))) {e.Effects=DragDropEffects.Move;e.Handled=true;}
    }
    private async void Queue_Drop(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(EncodingJob)) is not EncodingJob source) return;
        e.Handled=true; var target=Ancestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as EncodingJob;
        await ViewModel.MoveJobAsync(source,target is null?ViewModel.Jobs.Count-1:ViewModel.Jobs.IndexOf(target));
    }
    private static T? Ancestor<T>(DependencyObject? source) where T:DependencyObject
    {
        while(source is not null) {if(source is T item)return item;source=VisualTreeHelper.GetParent(source);} return null;
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closing)return; e.Cancel=true;
        if(ViewModel.HasActiveJobs&&MessageBox.Show(L.T("Laufende Jobs abbrechen und PixelPeeker schließen? Die Queue bleibt gespeichert."),"PixelPeeker",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        closing=true; IsEnabled=false;
        await ViewModel.ShutdownAsync();
        Close();
    }
}
