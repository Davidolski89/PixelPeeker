using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Desktop;

public sealed partial class MainViewModel
{
    private readonly FrameNavigationService frameNavigation;
    private readonly AccuratePreviewService accuratePreview;
    private CancellationTokenSource? previewCancellation;
    private readonly HashSet<Task> previewTasks=[];
    private Task currentPreview=Task.CompletedTask;
    private long previewGeneration;
    private bool settingPreviewPosition,filteredPreview;
    private string previewError="";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousFrameCommand),nameof(NextFrameCommand),nameof(MarkCutStartCommand),nameof(MarkCutEndCommand))]
    private bool previewLoading;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousFrameCommand),nameof(NextFrameCommand),nameof(MarkCutStartCommand),nameof(MarkCutEndCommand))]
    private SourceFramePosition? displayedPreviewFrame;
    [ObservableProperty] private string previewStatus=L.T("Zeitregler bewegen: Die Vorschau wird automatisch geladen.");
    private bool CanStepPreview()=>!PlaybackActive&&!PlaybackTransition&&!PreviewLoading&&DisplayedPreviewFrame is not null&&SelectedMedia is not null;
    partial void OnDisplayedPreviewFrameChanged(SourceFramePosition? value)=>OnPropertyChanged(nameof(CutPositionStatus));
    partial void OnPreviewSecondsChanged(double value)
    {if(!settingPreviewPosition){if(PlaybackActive||PlaybackTransition)RequestQuickSeek(value);else _=RequestPreviewAsync(value,FrameSeek.Nearest,true);}}
    public Task WaitForPreviewAsync()=>currentPreview;
    private void ResetPreview()
    {
        previewGeneration++;previewCancellation?.Cancel();previewCancellation=null;
        accuratePreview.Reset();
        DisplayedPreviewFrame=null;PreviewLoading=false;previewError="";UpdatePreviewStatus();
    }
    private void UpdatePreviewStatus()
    {
        PreviewStatus=PlaybackTransition?L.T("Wiedergabe wird angehalten. Exaktes Quellbild wird geladen …"):
            PlaybackActive?L.T("Eingabedatei wird abgespielt · schnelle Suche. Für genaue Schnitte pausieren."):
            previewError.Length>0?L.F($"Vorschau fehlgeschlagen: {previewError}"):
            PreviewLoading?L.T("Vorschau wird geladen …"):
            DisplayedPreviewFrame is {} frame?L.F($"Geladenes Quellbild: {frame.Seconds:0.000000} s")+" · "+L.T(filteredPreview?"Filtervorschau":"Originalframe"):
            L.T("Zeitregler bewegen: Die Vorschau wird automatisch geladen.");
    }
    private Task RequestPreviewAsync(double seconds,FrameSeek direction=FrameSeek.Nearest,bool debounce=false)
    {
        if(PlaybackTransition){requestedPlaybackPosition=seconds;return currentPreview=currentPlayback;}
        if(PlaybackActive)return currentPreview=TrackPlayback(HoldPlaybackAsync(false,seconds));
        var generation=++previewGeneration;previewCancellation?.Cancel();previewCancellation=null;
        if(lifetime.IsCancellationRequested||SelectedMedia is not {} item||versions.Active is not {} tools||!File.Exists(item.Info.Path))
        {DisplayedPreviewFrame=null;PreviewLoading=false;UpdatePreviewStatus();return currentPreview=Task.CompletedTask;}
        var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);cancellation.CancelAfter(TimeSpan.FromSeconds(45));
        previewCancellation=cancellation;PreviewLoading=true;previewError="";UpdatePreviewStatus();
        var options=Json.Clone(item.Options);var filtered=filteredPreview;
        var task=LoadPreviewAsync();previewTasks.Add(task);_=ForgetAsync(task);return currentPreview=task;
        async Task ForgetAsync(Task work){try{await work;}finally{previewTasks.Remove(work);}}
        async Task LoadPreviewAsync()
        {
            try
            {
                if(debounce)await Task.Delay(80,cancellation.Token);
                var frame=await frameNavigation.ResolveAsync(tools,item.Info,seconds,direction,cancellation.Token);
                options.Video.CudaScale=false;
                // Frame navigation always follows the source timeline; fps conversion is an export operation.
                options.Filters.FrameRate=0;
                if(filtered&&options.Subtitles.Any(s=>s.IsBitmap&&s.Action==TrackAction.Einbrennen))throw new InvalidOperationException(L.T("Bilduntertitel-Burn-in bitte über einen Testencode prüfen."));
                var filter=filtered?FilterService.Build(item.Info,options):"";
                var geometry=ScalingService.Calculate(item.Info.Video!,filtered?options.Filters:new FilterOptions());
                var width=filtered&&options.Filters.PadEnabled?options.Filters.PadWidth:geometry.Width;
                System.Windows.Media.ImageSource image;
                if(filtered&&options.Filters.CustomChain.Length>0)
                {
                    // Free filter graphs can drop/reorder frames; retain their isolated extraction semantics.
                    accuratePreview.Reset();
                    image=LoadImage(await preview.FrameAsync(tools,item.Info,frame.Seconds,filter,width:Math.Min(1280,width),ct:cancellation.Token,exactFrame:frame));
                }
                else
                {
                    var pixels=await accuratePreview.FrameAsync(tools,item.Info,frame,filter,width:Math.Min(1280,width),ct:cancellation.Token);
                    var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(pixels.Width,pixels.Height,96,96,System.Windows.Media.PixelFormats.Bgr24,null,pixels.Pixels,pixels.Stride);
                    bitmap.Freeze();image=bitmap;
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if(generation!=previewGeneration||!ReferenceEquals(item,SelectedMedia))return;
                PreviewImage=image;DisplayedPreviewFrame=frame;
                settingPreviewPosition=true;try{PreviewMaximum=Math.Max(PreviewMaximum,frame.Seconds);PreviewSeconds=frame.Seconds;}finally{settingPreviewPosition=false;}
            }
            catch(OperationCanceledException)when(generation!=previewGeneration||lifetime.IsCancellationRequested){}
            catch(Exception e)
            {
                if(generation==previewGeneration)
                {previewError=e is OperationCanceledException?L.T("Zeitlimit beim Laden der Vorschau überschritten."):e.Message;DisplayedPreviewFrame=null;log.Write("Frame-Vorschau",e);}
            }
            finally
            {
                if(generation==previewGeneration){previewCancellation=null;PreviewLoading=false;UpdatePreviewStatus();}
                cancellation.Dispose();
            }
        }
    }
    [RelayCommand] private Task ShowPreviewAsync(){filteredPreview=false;return RequestPreviewAsync(PreviewSeconds);}
    [RelayCommand] private Task ShowFilteredPreviewAsync(){filteredPreview=true;return RequestPreviewAsync(PreviewSeconds);}
    [RelayCommand(CanExecute=nameof(CanStepPreview))] private Task PreviousFrameAsync()=>RequestPreviewAsync(DisplayedPreviewFrame!.Seconds,FrameSeek.Previous);
    [RelayCommand(CanExecute=nameof(CanStepPreview))] private Task NextFrameAsync()=>RequestPreviewAsync(DisplayedPreviewFrame!.Seconds,FrameSeek.Next);
}
