using System.ComponentModel;
using System.Windows.Threading;
using FFmpegStudio.Core;

namespace FFmpegStudio.Desktop;

public sealed partial class MainViewModel
{
    private readonly HashSet<object> switchingCodecs=[];
    private DispatcherTimer? codecSaveTimer;
    private Task codecSettingsSave=Task.CompletedTask;
    private static readonly HashSet<string> audioCodecFields=[nameof(AudioTrackOptions.BitRateKbps),nameof(AudioTrackOptions.CompressionLevel),nameof(AudioTrackOptions.BitrateMode),nameof(AudioTrackOptions.Application),nameof(AudioTrackOptions.Channels),nameof(AudioTrackOptions.SampleRate),nameof(AudioTrackOptions.ChannelLayout)];
    private void OptionsChanging(object? sender,PropertyChangingEventArgs e)
    {
        if(adjustingEncoder||applyingPreset||e.PropertyName!=nameof(VideoOptions.Encoder)||sender is null)return;
        if(sender is VideoOptions video)Settings.Codecs.Remember(video);
        else if(sender is AudioTrackOptions audio)Settings.Codecs.Remember(audio);
        else return;
        switchingCodecs.Add(sender);
    }
    private void RestoreAudioCodec(AudioTrackOptions audio)
    {
        adjustingEncoder=true;
        try{Settings.Codecs.Restore(audio);}
        finally{adjustingEncoder=false;}
    }
    private void RememberCodecChange(object? sender,PropertyChangedEventArgs e)
    {
        if(sender is VideoOptions video)Settings.Codecs.Remember(video);
        else if(sender is AudioTrackOptions audio&&(e.PropertyName==nameof(AudioTrackOptions.Encoder)||audioCodecFields.Contains(e.PropertyName??"")))Settings.Codecs.Remember(audio);
        else if(sender is SubtitleTrackOptions subtitle&&e.PropertyName==nameof(SubtitleTrackOptions.Encoder)&&!string.IsNullOrWhiteSpace(subtitle.Encoder))Settings.Codecs.LastSubtitleEncoder=subtitle.Encoder;
        else return;
        ScheduleCodecSave();
    }
    private void RememberSelectedVideo()
    {
        if(SelectedMedia is not {} item)return;
        Settings.Codecs.Remember(item.Options.Video);
        if(applyingPreset)
        {
            foreach(var audio in item.Options.Audio.Where(a=>a.Action==TrackAction.Encodieren))Settings.Codecs.Remember(audio);
            foreach(var subtitle in item.Options.Subtitles.Where(s=>s.Action==TrackAction.Encodieren))Settings.Codecs.LastSubtitleEncoder=subtitle.Encoder;
        }
        ScheduleCodecSave();
    }
    private void ScheduleCodecSave()
    {
        if(lifetime.IsCancellationRequested)return;
        if(codecSaveTimer is null)
        {
            codecSaveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};
            codecSaveTimer.Tick+=(_,_)=>{codecSaveTimer.Stop();codecSettingsSave=SaveCodecsAsync();};
        }
        codecSaveTimer.Stop();codecSaveTimer.Start();
    }
    private async Task SaveCodecsAsync()
    {
        try{await settings.SaveAsync();}
        catch(Exception e){log.Write(L.T("Codec-Einstellungen konnten nicht gespeichert werden."),e);StatusText=e.Message;}
    }
}
