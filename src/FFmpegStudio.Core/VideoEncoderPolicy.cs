namespace FFmpegStudio.Core;

/// <summary>Video encoders offered by the Windows application, independent of the FFmpeg build.</summary>
public static class VideoEncoderPolicy
{
    public static bool IsAllowed(string? encoder) => encoder is
        "libx264" or "libx264rgb" or "libopenh264" or "libx265" or "libvvenc" or
        "libaom-av1" or "librav1e" or "libsvtav1" or
        "h264_nvenc" or "hevc_nvenc" or "av1_nvenc" or
        "h264_qsv" or "hevc_qsv" or "av1_qsv" or
        "h264_amf" or "hevc_amf" or "av1_amf";

    public static string UnavailableMessage(string? encoder) => L.F($"Encoder {encoder} wird in PixelPeeker nicht angeboten. Bitte einen Softwareencoder oder NVIDIA NVENC, Intel Quick Sync bzw. AMD AMF wählen.");
}
