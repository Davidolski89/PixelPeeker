using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public sealed class LibraryMedia
{
    private LibraryMedia(string path, MediaInfo? info, string summary, string details, EncodingOptions? options)
    {
        Path = path;
        Info = info;
        Summary = summary;
        Details = details;
        Options = options;
    }

    public string Path { get; }
    public MediaInfo? Info { get; }
    public EncodingOptions? Options { get; }
    public string Summary { get; }
    public string Details { get; }
    public string Name => Info?.Name ?? System.IO.Path.GetFileName(Path);

    public static LibraryMedia FromProbe(MediaInfo info, EncodingOptions options) =>
        new(info.Path, info, info.Summary, MediaDetailsText.Format(info), options);

    public static LibraryMedia Unprobed(string path)
    {
        var file = new FileInfo(path);
        var summary = file.Exists ? $"{file.Length / 1048576d:0.0} MiB" : L.T("Mediendatei nicht gefunden.");
        return new(path, null, summary, MediaDetailsText.FormatUnprobed(path), null);
    }
}
