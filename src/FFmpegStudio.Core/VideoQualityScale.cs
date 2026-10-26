using System.Globalization;
using System.Text.RegularExpressions;

namespace FFmpegStudio.Core;

public sealed record QualityRange(double Minimum, double Maximum, double Step);

public static class VideoQualityScale
{
    public static string Parameter(VideoOptions video) => video.IsNvenc
        ? video.RateControl == RateControl.QP ? "qp" : "cq"
        : video.Encoder is "libvvenc" or "librav1e" || video.RateControl == RateControl.QP ? "qp" : "crf";

    public static QualityRange For(VideoOptions video, EncoderInfo? encoder = null)
    {
        var parameter = Parameter(video);
        var option = encoder?.Options?.FirstOrDefault(o => o.Name == parameter);
        // x264/x265 expose FLT_MAX/INT_MAX in FFmpeg help; their documented scale is 0–51.
        // AV1 NVENC QP 0 requests lossless coding, rejected by the tested NVIDIA hardware.
        double minimum = video.Encoder == "av1_nvenc" && parameter == "qp" ? 1 : 0;
        double maximum = video.Encoder == "librav1e" || video.Encoder == "av1_nvenc" && parameter == "qp" ? 255
            : ScalingService.Family(video.Encoder) is "AV1" or "VVC" ? 63 : 51;
        var match = Regex.Match(option?.Description ?? "", @"\(from (\S+) to (\S+)\)");
        if (match.Success && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var lower)
            && double.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var upper)
            && double.IsFinite(lower) && double.IsFinite(upper) && upper is > 0 and <= 255 && lower < upper)
        {
            minimum = Math.Max(minimum, lower); maximum = upper;
        }
        var step = parameter == "qp" || option?.Type is "int" or "int64" || video.Encoder is "libaom-av1" or "libsvtav1" ? 1 : .5;
        return new(minimum, maximum, step);
    }
}
