using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;

namespace FFmpegStudio.Desktop;

public sealed partial class MainViewModel
{
    private INotifyCollectionChanged? observedCuts;
    [ObservableProperty] private SourceFramePosition? pendingCutStart;
    [ObservableProperty] private CutRegion? selectedCut;
    public IReadOnlyList<CutRegion> CutEntries=>SelectedMedia?.Options.Cuts.ToArray()??[];
    public string CutSummary
    {
        get
        {
            if(SelectedMedia is not {} item)return L.T("Eine Videodatei auswählen.");
            var duration=CutTimeline.Duration(item.Info,item.Options);
            return !CutTimeline.Active(item.Options)?L.T("Kein aktiver Schnitt. Das gesamte Video bleibt erhalten."):
                L.F($"{item.Options.Cuts.Count} Schnittbereiche · verbleibende Dauer ca. {duration:0.###} s");
        }
    }
    public string CutPositionStatus=>PendingCutStart is {} start?L.F($"Schnittstart: {start.Seconds:0.000000} s · erstes erhaltenes Bild suchen und Ende setzen."):
        SelectedMedia is {} item&&DisplayedPreviewFrame is {} frame&&CutTimeline.Active(item.Options)&&item.Options.Cuts.Any(c=>frame.Seconds>=c.Start.Seconds-1e-7&&(c.End is null||frame.Seconds<c.End.Seconds-1e-7))?
        L.T("Dieses Quellbild wird beim Export entfernt."):L.T("Start = erstes zu entfernendes Bild · Ende = erstes danach zu erhaltendes Bild.");
    private void AttachCuts()
    {
        if(observedCuts is not null)observedCuts.CollectionChanged-=CutsChanged;
        observedCuts=SelectedMedia?.Options.Cuts;
        if(observedCuts is not null)observedCuts.CollectionChanged+=CutsChanged;
        PendingCutStart=null;SelectedCut=null;RefreshCuts();
    }
    private void CutsChanged(object? sender,NotifyCollectionChangedEventArgs e){RefreshCuts();RefreshSummary();}
    private void EnsureCutAudio()
    {
        if(SelectedMedia is not {} item)return;
        CutEditing.EnsureAudio(item.Options);
    }
    private void RefreshCuts()
    {
        OnPropertyChanged(nameof(CutEntries));OnPropertyChanged(nameof(CutSummary));OnPropertyChanged(nameof(CutPositionStatus));
        MarkCutStartCommand.NotifyCanExecuteChanged();MarkCutEndCommand.NotifyCanExecuteChanged();CutToEndCommand.NotifyCanExecuteChanged();
        JumpCutStartCommand.NotifyCanExecuteChanged();JumpCutEndCommand.NotifyCanExecuteChanged();RemoveCutCommand.NotifyCanExecuteChanged();ClearCutsCommand.NotifyCanExecuteChanged();
    }
    partial void OnPendingCutStartChanged(SourceFramePosition? value)=>RefreshCuts();
    partial void OnSelectedCutChanged(CutRegion? value)=>RefreshCuts();
    private bool CanEndCut()=>CanStepPreview()&&PendingCutStart is {} start&&DisplayedPreviewFrame!.Seconds>start.Seconds+1e-7;
    private bool HasCutStart()=>!PlaybackActive&&!PlaybackTransition&&PendingCutStart is not null&&SelectedMedia is not null;
    private bool HasSelectedCut()=>SelectedCut is not null&&SelectedMedia is not null;
    private bool HasSelectedCutEnd()=>HasSelectedCut()&&SelectedCut!.End is not null;
    private bool HasCuts()=>SelectedMedia?.Options.Cuts.Count>0;
    [RelayCommand(CanExecute=nameof(CanStepPreview))] private void MarkCutStart()=>PendingCutStart=DisplayedPreviewFrame;
    [RelayCommand(CanExecute=nameof(CanEndCut))] private void MarkCutEnd()=>AddCut(DisplayedPreviewFrame);
    [RelayCommand(CanExecute=nameof(HasCutStart))] private void CutToEnd()=>AddCut(null);
    private void AddCut(SourceFramePosition? end)
    {
        if(SelectedMedia is not {} item||PendingCutStart is not {} start)return;
        SelectedCut=CutEditing.Add(item.Options,start,end);PendingCutStart=null;
        RefreshCuts();RefreshSummary();
    }
    [RelayCommand(CanExecute=nameof(HasSelectedCut))] private Task JumpCutStartAsync()=>RequestPreviewAsync(SelectedCut!.Start.Seconds);
    [RelayCommand(CanExecute=nameof(HasSelectedCutEnd))] private Task JumpCutEndAsync()=>RequestPreviewAsync(SelectedCut!.End!.Seconds);
    [RelayCommand(CanExecute=nameof(HasSelectedCut))] private void RemoveCut(){SelectedMedia!.Options.Cuts.Remove(SelectedCut!);SelectedCut=null;}
    [RelayCommand(CanExecute=nameof(HasCuts))] private void ClearCuts(){SelectedMedia!.Options.Cuts.Clear();PendingCutStart=null;SelectedCut=null;}
}
