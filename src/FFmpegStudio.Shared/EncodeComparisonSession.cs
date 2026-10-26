using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public sealed record EncodeComparisonChoice(EncodeComparisonInput Input)
{
    public string Label { get; } = Describe(Input);
    public override string ToString() => Label;

    public static string Describe(EncodeComparisonInput input) => input.Job is { } job
        ? (job.Options.Video.RateControl is RateControl.Qualität or RateControl.QP
            ? (job.Options.Video.RateControl == RateControl.QP || job.Options.Video.Encoder is "libvvenc" or "librav1e" ? "QP" : job.Options.Video.IsNvenc ? "CQ" : "CRF") + job.Options.Video.Quality.ToString("0.##")
            : job.Options.Video.BitRateKbps + " kbit/s") + " · " + job.Options.Video.Encoder + " · " + job.Options.Video.Preset + " · " + Path.GetFileName(input.Media.Path)
        : Path.GetFileName(input.Media.Path) + " · " + input.Media.Video!.Codec + " · " + input.Media.Video.Width + " × " + input.Media.Video.Height;
}

/// <summary>Side-by-side encode comparison shared by Avalonia. The WPF window keeps its own image and sample-strip view.</summary>
public sealed partial class EncodeComparisonSession : ObservableObject
{
    private readonly EncodeComparisonService service;
    private readonly LoggingService log;

    public EncodeComparisonSession(EncodeComparisonService service, LoggingService log)
    {
        this.service = service;
        this.log = log;
        Status = L.T("Einen oder zwei Encodes wählen oder Dateien öffnen.");
    }

    public ObservableCollection<EncodeComparisonChoice> Choices { get; } = [];
    [ObservableProperty] private EncodeComparisonChoice? selectedA;
    [ObservableProperty] private EncodeComparisonChoice? selectedB;
    [ObservableProperty] private bool includeOriginal = true;
    [ObservableProperty] private bool compareTwo;
    [ObservableProperty] private bool matchReference;
    [ObservableProperty] private double position;
    [ObservableProperty] private double offsetA;
    [ObservableProperty] private double offsetB;
    [ObservableProperty] private double zoom = 1;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string? originalPath;
    [ObservableProperty] private string? pathA;
    [ObservableProperty] private string? pathB;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool loading;

    public void RefreshJobs(IEnumerable<EncodingJob> jobs)
    {
        var completed = jobs.Where(job => job.Status == JobStatus.Abgeschlossen && job.OutputInfo?.Video is not null).ToArray();
        foreach (var choice in Choices.Where(choice => choice.Input.Job is { } job && completed.All(item => item.Id != job.Id)).ToList())
            Choices.Remove(choice);
        foreach (var job in completed)
        {
            if (Choices.All(choice => choice.Input.Job?.Id != job.Id))
                Choices.Add(new EncodeComparisonChoice(EncodeComparisonInput.FromJob(job)));
        }
        SelectedA ??= Choices.FirstOrDefault();
        if (SelectedB is null && CompareTwo) SelectedB = Choices.FirstOrDefault(choice => !ReferenceEquals(choice, SelectedA));
    }

    public void AddExternal(EncodeComparisonInput input)
    {
        var choice = new EncodeComparisonChoice(input);
        Choices.Add(choice);
        if (SelectedA is null) SelectedA = choice;
        else if (CompareTwo && SelectedB is null) SelectedB = choice;
    }

    public Task LoadNearestAsync(CancellationToken ct = default) => LoadAsync(FrameSeek.Nearest, ct);
    public Task LoadPreviousAsync(CancellationToken ct = default) => LoadAsync(FrameSeek.Previous, ct);
    public Task LoadNextAsync(CancellationToken ct = default) => LoadAsync(FrameSeek.Next, ct);

    public async Task LoadAsync(FrameSeek direction, CancellationToken ct = default)
    {
        if (SelectedA is not { } first)
        {
            Status = L.T("Einen oder zwei Encodes wählen oder Dateien öffnen.");
            return;
        }
        Loading = true;
        try
        {
            var original = IncludeOriginal ? first.Input.Job?.Source : null;
            var second = CompareTwo ? SelectedB?.Input : null;
            var frames = await service.LoadAsync(original, first.Input, second, Position, IncludeOriginal, OffsetA, OffsetB, direction, ct, MatchReference && IncludeOriginal);
            Position = frames.SourceSeconds;
            OriginalPath = frames.Original?.Path;
            PathA = frames.A.Path;
            PathB = frames.B.Path;
            Message = string.Join(Environment.NewLine, new[] { frames.Original?.Message, frames.A.Message, frames.B.Message }.Where(part => !string.IsNullOrWhiteSpace(part)));
            Status = Message.Length == 0 ? L.F($"Encode-Vergleich · {frames.SourceSeconds:0.000} s") : Message;
        }
        catch (Exception e)
        {
            log.Write(L.T("Encode-Vergleich"), e);
            Status = e.Message;
        }
        finally { Loading = false; }
    }
}
