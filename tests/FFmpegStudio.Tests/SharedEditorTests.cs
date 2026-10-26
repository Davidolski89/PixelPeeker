using System.Collections.ObjectModel;
using FFmpegStudio.Core;
using FFmpegStudio.Shared;
using Xunit;

public sealed class SharedEditorTests
{
    [Fact]
    public void Encoder_choices_keep_x264_presets_and_report_the_encoder()
    {
        var video = new VideoOptions { Encoder = "libx264", Preset = "fast", PixelFormat = "yuv420p" };
        var capabilities = new Capabilities
        {
            Encoders =
            [
                new EncoderInfo("libx264", "H.264", "video", "H.264", false, PixelFormats: ["yuv420p", "yuv420p10le"],
                    Options: [new EncoderOption("preset", "enum", "", ["ultrafast", "medium"]), new EncoderOption("tune", "enum", "", ["film"])])
            ]
        };
        var pixels = new ObservableCollection<string>();
        var presets = new ObservableCollection<string>();
        var tunes = new ObservableCollection<string>();
        var profiles = new ObservableCollection<string>();
        var labels = new ObservableCollection<string>();
        var state = EncoderChoices.Apply(video, capabilities, false, false, _ => { }, pixels, presets, tunes, profiles, labels,
            () => ["fast"]);
        Assert.Contains("libx264", state.EncoderInfo);
        Assert.True(state.CanSetPreset);
        Assert.True(state.CanSetTune);
        Assert.Contains("ultrafast", presets);
        Assert.Contains("veryslow", presets);
        Assert.Equal("fast", video.Preset);
        Assert.Equal("yuv420p", video.PixelFormat);
        Assert.Equal(["fast"], labels);
    }

    [Fact]
    public void Adding_a_cut_enables_export_and_reencodes_copied_audio()
    {
        var options = new EncodingOptions { Container = "mp4" };
        options.Video.CudaScale = true;
        options.Audio.Add(new AudioTrackOptions { Action = TrackAction.Kopieren, Encoder = "copy" });
        var selected = CutEditing.Add(options, new SourceFramePosition(1, 1000), new SourceFramePosition(2, 2000));
        Assert.True(options.CutsEnabled);
        Assert.NotNull(selected);
        Assert.Single(options.Cuts);
        Assert.Equal(TrackAction.Encodieren, options.Audio[0].Action);
        Assert.Equal("aac", options.Audio[0].Encoder);
        Assert.False(options.Video.CudaScale);
    }
}
