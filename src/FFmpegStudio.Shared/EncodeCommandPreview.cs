using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;

namespace FFmpegStudio.Shared;

public static class EncodeCommandPreview
{
    public readonly record struct Current(string Conversion, string Validation, string Estimate, string OutputFileName, string Command);

    public static Current ForMedia(MediaInfo info, EncodingOptions options, AppSettings settings, Capabilities? capabilities, FFmpegCommandBuilder builder, string ffmpegPath)
    {
        var conversion = ScalingService.Describe(info, options);
        var errors = builder.Validate(info, options, capabilities);
        var validation = string.Join("\n", errors);
        var estimated = SizeEstimator.Estimate(info, options);
        var estimate = estimated.HasValue
            ? L.F($"Bitraten-Schätzung: ca. {estimated / 1048576d:0.0} MiB (inkl. 2 % Containerzuschlag).")
            : L.T("CRF/CQ: Dateigröße unbekannt. Ein Testencode ermöglicht eine grobe Hochrechnung.");
        try
        {
            var output = QueueService.SuggestedOutputPath(info, options, settings.FileNaming);
            var temporary = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".unvollstaendig-VORSCHAU" + Path.GetExtension(output));
            var command = FFmpegCommandBuilder.Display(ffmpegPath, builder.Build(info, options, temporary, capabilities));
            return new Current(conversion, validation, estimate, Path.GetFileName(output), command);
        }
        catch (Exception e)
        {
            return new Current(conversion, validation.Length == 0 ? e.Message : validation, estimate, "", "");
        }
    }
}
