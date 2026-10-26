using System.Collections.ObjectModel;
using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public static class EncoderChoices
{
    public readonly record struct Result(string EncoderInfo, bool CanSetPreset, bool CanSetTune);

    public static Result Apply(
        VideoOptions video,
        Capabilities capabilities,
        bool defaults,
        bool cutsActive,
        Action<VideoOptions> restore,
        ObservableCollection<string> pixelFormats,
        ObservableCollection<string> presetChoices,
        ObservableCollection<string> tuneChoices,
        ObservableCollection<string> profileChoices,
        ObservableCollection<string> videoPresetChoices,
        Func<IEnumerable<string>> labeledPresets)
    {
        if (defaults)
        {
            restore(video);
            if (cutsActive) video.CudaScale = false;
        }
        var preset = video.Preset ?? "";
        var tune = video.Tune ?? "";
        var profile = video.Profile ?? "";
        var pixelFormat = video.PixelFormat ?? "";
        var allowed = VideoEncoderPolicy.IsAllowed(video.Encoder);
        var encoder = allowed ? capabilities.Find(video.Encoder) : null;
        var info = !allowed
            ? VideoEncoderPolicy.UnavailableMessage(video.Encoder)
            : encoder is null
                ? L.T("Encoder im Build nicht vorhanden.")
                : $"{encoder.Family} · {encoder.Name}\n{encoder.Description}\n{(encoder.Hardware ? L.T("Hardware-Unterstützung wird vor dem Start tatsächlich geprüft.") : L.T("Softwareencoder"))}";
        Fill(pixelFormats, encoder?.PixelFormats);
        Fill(presetChoices, encoder?.Options?.FirstOrDefault(option => option.Name == (video.Encoder == "libaom-av1" ? "cpu-used" : video.Encoder == "librav1e" ? "speed" : "preset"))?.Choices);
        Fill(tuneChoices, encoder?.Options?.FirstOrDefault(option => option.Name == "tune")?.Choices);
        Fill(profileChoices, encoder?.Options?.FirstOrDefault(option => option.Name == "profile")?.Choices);
        var canSetPreset = encoder?.Options?.Any(option => option.Name is "preset" or "speed" or "cpu-used") == true;
        var canSetTune = encoder?.Options?.Any(option => option.Name == "tune") == true;
        if (video.Encoder is "libx264" or "libx264rgb" or "libx265")
            Fill(presetChoices, ["ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"]);
        video.Preset = preset;
        video.Tune = tune;
        video.Profile = profile;
        video.PixelFormat = pixelFormat;
        Fill(videoPresetChoices, labeledPresets(), false);
        return new Result(info, canSetPreset, canSetTune);
    }

    public static void Fill(ObservableCollection<string> target, IEnumerable<string>? items, bool includeEmpty = true)
    {
        var values = (includeEmpty ? new[] { "" } : Array.Empty<string>()).Concat(items ?? []).Distinct().ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            if (i < target.Count && target[i] == values[i]) continue;
            var existing = target.IndexOf(values[i]);
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, values[i]);
        }
        while (target.Count > values.Length) target.RemoveAt(target.Count - 1);
    }
}
