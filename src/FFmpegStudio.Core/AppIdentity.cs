namespace FFmpegStudio.Core;

public static class AppIdentity
{
    public const string Name="PixelPeeker";
    public const string ExecutableName="PixelPeeker.exe";
    // Keep these identifiers shared with previous releases so settings/queues carry over
    // and the old and renamed applications cannot open the same live database together.
    public const string DataFolderName="FFmpegStudio";
    public const string DataEnvironmentVariable="FFMPEG_STUDIO_DATA";
    public const string SingleInstanceMutex=@"Local\FFmpegStudio.Desktop";
}
