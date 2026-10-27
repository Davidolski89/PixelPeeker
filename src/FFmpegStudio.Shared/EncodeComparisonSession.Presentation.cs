using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public enum StudioComparisonMode { EncodeAndReference, TwoEncodes, TwoEncodesAndReference }

public sealed record ComparisonModeOption(StudioComparisonMode Value)
{
    public string Label => L.T(Value switch
    {
        StudioComparisonMode.TwoEncodes => "Zwei Encodes",
        StudioComparisonMode.TwoEncodesAndReference => "Zwei Encodes und Referenz",
        _ => "Encode A und Referenz"
    });
    public override string ToString() => Label;
}

public sealed record ComparisonZoomOption(double Value)
{
    public string Label => Value == 0 ? L.T("Einpassen") : Value.ToString("0 %");
    public override string ToString() => Label;
}

public sealed partial class EncodeComparisonSession
{
    private (int Focus, bool Side)? referenceHold;

    public IReadOnlyList<ComparisonModeOption> ModeChoices { get; } = Enum.GetValues<StudioComparisonMode>().Select(mode => new ComparisonModeOption(mode)).ToArray();
    public IReadOnlyList<ComparisonZoomOption> ZoomChoices { get; } = [new(0), new(1), new(2), new(4)];

    [ObservableProperty] private bool sideBySide = true;

    public StudioComparisonMode Mode
    {
        get => CompareTwo
            ? IncludeOriginal ? StudioComparisonMode.TwoEncodesAndReference : StudioComparisonMode.TwoEncodes
            : StudioComparisonMode.EncodeAndReference;
        set
        {
            CompareTwo = value != StudioComparisonMode.EncodeAndReference;
            IncludeOriginal = value != StudioComparisonMode.TwoEncodes;
            RaiseChrome();
        }
    }

    public bool CompareTwoEncodes => CompareTwo;
    public bool OriginalVisible => IncludeOriginal;
    public bool SingleVisible => !SideBySide;
    public int PanelCount => (OriginalVisible ? 1 : 0) + (CompareTwoEncodes ? 2 : 1);
    public bool CanOffsetA => SelectedA?.Input.Job is null;
    public bool CanOffsetB => SelectedB?.Input.Job is null;
    public bool CanMatchReference => OriginalVisible && SelectedA?.Input.Job is not null;
    public string ToggleLabel => CompareTwoEncodes ? "A ↔ B" : L.T("Referenz ↔ Encode");
    public string ReferenceLabel => MatchReference && CanMatchReference ? L.T("Referenz mit Filtern von Encode A") : L.T("Original");
    public string ReferenceDescription => MatchReference && CanMatchReference
        ? L.T("Referenz mit identischer Geometrie; Ausgabe auf Vergleichsraster. ")
        : L.T("Original unverändert; Filter- und Größenunterschiede bleiben sichtbar. RGB/SDR-Vorschaubilder; bei anderer Bildrate wird der zeitlich nächste Frame gezeigt.");
    public string OriginalLabel => string.IsNullOrWhiteSpace(OriginalPath) ? L.T("Kein Original gewählt") : Path.GetFileName(OriginalPath);
    public double Maximum
    {
        get
        {
            var duration = SelectedA?.Input.Job?.Source.Duration
                ?? SelectedA?.Input.Media.Duration
                ?? (CompareTwo ? SelectedB?.Input.Job?.Source.Duration ?? SelectedB?.Input.Media.Duration : null)
                ?? 1;
            return Math.Max(0.1, duration);
        }
    }

    public int FocusIndex
    {
        get => Focus switch { ComparisonFocus.B => 1, ComparisonFocus.Original => 2, _ => 0 };
        set => Focus = value switch { 1 => ComparisonFocus.B, 2 => ComparisonFocus.Original, _ => ComparisonFocus.A };
    }

    public string FocusLabel => FocusIndex == 2 && OriginalVisible ? ReferenceLabel : FocusIndex == 1 ? "B · " + SelectedB?.Label : "A · " + SelectedA?.Label;

    public bool BeginReferenceHold()
    {
        if (referenceHold is not null) return true;
        if (Loading || !OriginalVisible || string.IsNullOrWhiteSpace(OriginalPath)) return false;
        referenceHold = (FocusIndex, SideBySide);
        FocusIndex = 2;
        SideBySide = false;
        return true;
    }

    public void EndReferenceHold()
    {
        if (referenceHold is not { } previous) return;
        referenceHold = null;
        FocusIndex = previous.Focus;
        SideBySide = previous.Side;
    }

    public void SelectJobs(IEnumerable<EncodingJob> selected)
    {
        var jobs = selected.Where(job => job.Status == JobStatus.Abgeschlossen && job.OutputInfo?.Video is not null).Take(2).ToArray();
        if (jobs.Length == 0)
        {
            Status = L.T("Einen oder zwei Encodes wählen oder Dateien öffnen.");
            return;
        }
        if (jobs.Length == 2 && !EncodeComparisonService.SameFile(jobs[0].Source.Path, jobs[1].Source.Path))
        {
            Status = L.T("Die Queue-Encodes müssen dieselbe Quelldatei verwenden.");
            return;
        }
        RefreshJobs(jobs.Concat(Choices.Where(choice => choice.Input.Job is not null).Select(choice => choice.Input.Job!)));
        Mode = jobs.Length == 1 ? StudioComparisonMode.EncodeAndReference : StudioComparisonMode.TwoEncodesAndReference;
        MatchReference = !CompareTwoEncodes;
        SelectedA = Choices.First(choice => choice.Input.Job?.Id == jobs[0].Id);
        SelectedB = jobs.Length == 2 ? Choices.First(choice => choice.Input.Job?.Id == jobs[1].Id) : null;
        OffsetA = OffsetB = 0;
        Position = CutTimeline.Keep(jobs[0].Options).FirstOrDefault()?.Start ?? 0;
        RaiseChrome();
    }

    partial void OnCompareTwoChanged(bool value) => RaiseChrome();
    partial void OnIncludeOriginalChanged(bool value) => RaiseChrome();
    partial void OnMatchReferenceChanged(bool value) => RaiseChrome();
    partial void OnSideBySideChanged(bool value) => OnPropertyChanged(nameof(SingleVisible));
    partial void OnPositionChanged(double value) => RaiseCoverage();
    partial void OnOffsetAChanged(double value) => RaiseCoverage();
    partial void OnOffsetBChanged(double value) => RaiseCoverage();
    partial void OnSelectedAChanged(EncodeComparisonChoice? value) => RaiseChrome();
    partial void OnSelectedBChanged(EncodeComparisonChoice? value) => RaiseChrome();

    private void RaiseCoverage()
    {
        OnPropertyChanged(nameof(CoverageA));
        OnPropertyChanged(nameof(CoverageB));
        OnPropertyChanged(nameof(Maximum));
    }

    private void RaiseChrome()
    {
        if (!CanMatchReference && MatchReference) MatchReference = false;
        if (!OriginalVisible && Focus == ComparisonFocus.Original) Focus = ComparisonFocus.A;
        if (!CompareTwoEncodes && Focus == ComparisonFocus.B) Focus = ComparisonFocus.A;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(CompareTwoEncodes));
        OnPropertyChanged(nameof(OriginalVisible));
        OnPropertyChanged(nameof(PanelCount));
        OnPropertyChanged(nameof(CanOffsetA));
        OnPropertyChanged(nameof(CanOffsetB));
        OnPropertyChanged(nameof(CanMatchReference));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(ReferenceLabel));
        OnPropertyChanged(nameof(ReferenceDescription));
        OnPropertyChanged(nameof(OriginalLabel));
        OnPropertyChanged(nameof(FocusLabel));
        OnPropertyChanged(nameof(FocusIndex));
        OnPropertyChanged(nameof(SingleVisible));
        RaiseCoverage();
    }
}
