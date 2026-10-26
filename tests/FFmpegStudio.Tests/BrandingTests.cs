using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Xunit;

namespace FFmpegStudio.Tests;

[Collection("Localization")]
public sealed class BrandingTests
{
    [Fact]
    public void New_export_paths_use_the_brand_but_saved_paths_are_preserved()
    {
        var source=new MediaInfo{Path=Path.GetFullPath("source.mkv"),Streams=[new(){Index=0,Type="video",Codec="h264",Width=1920,Height=1080}]};
        var fresh=EncodingOptions.ForMedia(source);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(source.Path)!,"PixelPeeker"),fresh.OutputDirectory);
        fresh.OutputDirectory="";
        Assert.Equal("PixelPeeker",Path.GetFileName(Path.GetDirectoryName(QueueService.SuggestedOutputPath(source,fresh))));
        fresh.OutputDirectory=Path.Combine(Path.GetDirectoryName(source.Path)!,"FFmpeg Studio");
        Assert.Equal(fresh.OutputDirectory,Path.GetDirectoryName(QueueService.SuggestedOutputPath(source,Json.Clone(fresh))));
    }

    [Fact]
    public async Task Existing_data_override_settings_and_queue_are_reopened_after_the_rename()
    {
        var root=Path.Combine(Path.GetTempPath(),"ffstudio-rebrand-"+Guid.NewGuid());
        var previous=Environment.GetEnvironmentVariable("FFMPEG_STUDIO_DATA");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root,"settings.json"),"""{"Language":"pl","ThemeId":"sakura","ParallelJobs":3,"TooltipsEnabled":false,"Codecs":{"LastVideoEncoder":"libsvtav1","LastAudioEncoder":"aac"}}""");
            Environment.SetEnvironmentVariable("FFMPEG_STUDIO_DATA",root);
            var paths=new AppPaths();Assert.Equal(root,paths.Root);
            var settings=new SettingsService(paths,new(paths));await settings.LoadAsync();
            Assert.Equal("pl",settings.Current.Language);Assert.Equal(3,settings.Current.ParallelJobs);
            Assert.False(settings.Current.TooltipsEnabled);Assert.Equal("libsvtav1",settings.Current.Codecs.LastVideoEncoder);
            var store=new QueueStore(paths);await store.InitializeAsync();
            var job=new EncodingJob{OutputPath=Path.Combine(root,"FFmpeg Studio","saved-result.mkv"),Options=new(){OutputDirectory=Path.Combine(root,"FFmpeg Studio")}};
            await store.SaveJobAsync(job);
            var restored=Assert.Single(await new QueueStore(new AppPaths()).LoadJobsAsync());
            Assert.Equal(job.Id,restored.Id);Assert.Equal(job.OutputPath,restored.OutputPath);Assert.Equal(job.Options.OutputDirectory,restored.Options.OutputDirectory);
            Assert.Equal("FFmpegStudio",AppIdentity.DataFolderName);
            Assert.Equal(@"Local\FFmpegStudio.Desktop",AppIdentity.SingleInstanceMutex);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FFMPEG_STUDIO_DATA",previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"ffstudio-rebrand-"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected temporary path");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
