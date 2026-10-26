using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegStudio.Core;

namespace FFmpegStudio.Shared;

public sealed partial class EditorMedia : ObservableObject
{
    public EditorMedia(string path, MediaInfo? info, EncodingOptions? options)
    {
        Path = path;
        Info = info;
        Options = options;
    }

    public string Path { get; }
    public MediaInfo? Info { get; }
    [ObservableProperty] private EncodingOptions? options;
    public string Name => Info?.Name ?? System.IO.Path.GetFileName(Path);
    public string Summary => Info?.Summary ?? (File.Exists(Path) ? $"{new FileInfo(Path).Length / 1048576d:0.0} MiB" : L.T("Mediendatei nicht gefunden."));
    public string Details => Info is null ? MediaDetailsText.FormatUnprobed(Path) : MediaDetailsText.Format(Info);
    public bool CanEdit => Info is not null && Options is not null;
}
