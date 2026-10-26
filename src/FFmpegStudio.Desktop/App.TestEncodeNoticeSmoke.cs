using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FFmpegStudio.Core;
using FFmpegStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FFmpegStudio.Desktop;

public partial class App
{
    internal static async Task<object> CheckPublishedTestEncodeNoticeAsync(MainWindow window,ServiceProvider services,string output,bool allLanguages=false)
    {
        var vm=window.ViewModel;var queue=services.GetRequiredService<QueueService>();var settings=services.GetRequiredService<SettingsService>();
        var paths=services.GetRequiredService<AppPaths>();var runner=services.GetRequiredService<ProcessRunner>();var versions=services.GetRequiredService<FFmpegVersionService>();
        var originalMedia=vm.SelectedMedia;var language=L.Language;var originalPreference=vm.Settings.ShowTestEncodeNotification;var parallel=vm.Settings.ParallelJobs;var seconds=vm.TestSeconds;
        var existing=queue.Jobs.Select(j=>j.Id).ToHashSet();Directory.CreateDirectory(output);
        static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
        static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject
        {foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()){if(child is T typed)yield return typed;foreach(var nested in Children<T>(child))yield return nested;}}
        static async Task Until(Func<bool> ready)
        {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(!ready())await Task.Delay(25,timeout.Token);}
        void Capture(Window dialog,string name)
        {
            dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));png.Save(stream);
        }
        void Place(Window dialog){if(window.Left<0){dialog.Left=-10000;dialog.Top=-10000;dialog.ShowActivated=false;}}
        try
        {
            Require(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}",Json.Options)!.ShowTestEncodeNotification,"Legacy settings did not enable test notices by default.");
            var fake=new EncodingJob{OutputPath=Path.Combine(paths.Root,"test-encode.mkv"),Status=JobStatus.Wartend};
            foreach(var code in allLanguages?L.SupportedLanguages:[language])
            {
                ApplyLanguage(code);fake.Status=JobStatus.Wartend;
                var preview=new TestEncodeNotificationWindow(fake){Owner=window,ShowActivated=false};preview.Show();Place(preview);
                try
                {
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var text=Children<TextBlock>(preview).Single(t=>t.Name=="TestEncodeStatus");
                    Require(text.Text==TestEncodeNotificationWindow.StatusMessage(fake),"Queued message was incorrect.");
                    Require(Children<CheckBox>(preview).Single().Content.Equals(L.T("Nicht mehr anzeigen")),"Suppression checkbox was not translated.");
                    Require(Children<Button>(preview).Single().ToolTip.Equals(ButtonHelpCatalog.Text("CloseTestEncodeNotice")),"Close help was missing.");
                    Capture(preview,code+"-test-encode-queued.png");
                    fake.Status=JobStatus.Läuft;await Until(()=>text.Text==TestEncodeNotificationWindow.StatusMessage(fake));
                    Capture(preview,code+"-test-encode-running.png");
                    fake.Status=JobStatus.Abgeschlossen;await Until(()=>text.Text==TestEncodeNotificationWindow.StatusMessage(fake));
                    fake.Status=JobStatus.Fehlgeschlagen;await Until(()=>text.Text==TestEncodeNotificationWindow.StatusMessage(fake));
                }
                finally{preview.Close();}
            }
            ApplyLanguage(language);
            if(versions.Active is null)
            {
                var testData=Environment.GetEnvironmentVariable("FFMPEG_STUDIO_TEST_DATA")??throw new InvalidDataException("Test FFmpeg data required.");
                var sourceVersions=new FFmpegVersionService(new AppPaths(testData),runner);await sourceVersions.LoadAsync();await versions.ActivateAsync(sourceVersions.Active!);
            }
            var tools=versions.Active!;var source=Path.Combine(paths.Root,"notification-source.mkv");
            (await runner.RunAsync(tools.FFmpegPath,["-v","error","-y","-f","lavfi","-i","testsrc2=size=160x90:rate=20:duration=3","-c:v","ffv1",source])).EnsureSuccess("Test notification source");
            var media=await services.GetRequiredService<MediaProbeService>().ProbeAsync(tools,source);var options=EncodingOptions.ForMedia(media);
            options.Video.Encoder="libx264";options.Video.Preset="ultrafast";options.Video.PixelFormat="yuv420p";options.AutoAnalyze=false;
            options.Filters.CustomChain="realtime";options.OutputDirectory=Path.Combine(paths.Root,"notification-encodes");
            vm.SelectedMedia=new(){Info=media,Options=options};vm.PreviewSeconds=0;vm.TestSeconds=2;vm.Settings.ParallelJobs=1;vm.Settings.ShowTestEncodeNotification=true;
            await vm.WaitForPreviewAsync();
            // Occupy the only slot so the real test command must first wait, then begin encoding.
            var blocker=await queue.AddAsync(media,options);queue.Start(blocker);await Until(()=>blocker.Status==JobStatus.Läuft);
            async Task Submit(bool expected,bool suppress,bool waitForStart,bool closeWithX)
            {
                var before=queue.Jobs.Select(j=>j.Id).ToHashSet();Exception? failure=null;bool seen=false;
                var observer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
                observer.Tick+=async(_,_)=>
                {
                    var notice=window.OwnedWindows.OfType<TestEncodeNotificationWindow>().FirstOrDefault();if(notice is null||seen)return;
                    seen=true;observer.Stop();Place(notice);
                    try
                    {
                        Require(expected,"A suppressed test notification reappeared.");
                        var job=queue.Jobs.Single(j=>!before.Contains(j.Id));var text=Children<TextBlock>(notice).Single(t=>t.Name=="TestEncodeStatus");
                        if(waitForStart)
                        {
                            Require(job.Status==JobStatus.Wartend,"Test encode did not wait for the occupied slot.");
                            Require(text.Text==TestEncodeNotificationWindow.StatusMessage(job),"Real waiting status was incorrect.");
                            await Until(()=>job.Status==JobStatus.Läuft&&text.Text==TestEncodeNotificationWindow.StatusMessage(job));
                            Capture(notice,language+"-real-test-encode.png");
                        }
                        Children<CheckBox>(notice).Single().IsChecked=suppress;
                        if(closeWithX)notice.Close();else Children<Button>(notice).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception e){failure=e;notice.Close();}
                };
                observer.Start();
                try{await vm.TestEncodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(25));}
                finally{observer.Stop();}
                if(failure is not null)throw failure;Require(seen==expected,"Test notification was not shown as expected.");
                var submitted=queue.Jobs.Single(j=>!before.Contains(j.Id));await Until(()=>submitted.Status is not (JobStatus.Wartend or JobStatus.Läuft));
                Require(submitted.Status==JobStatus.Abgeschlossen,"Closing the notification interrupted the test encode: "+submitted.Error);
            }
            await Submit(true,false,true,false);Require(vm.Settings.ShowTestEncodeNotification,"Closing the notice disabled future notices.");
            await Submit(true,true,false,true);Require(!vm.Settings.ShowTestEncodeNotification,"Don't show again was not applied.");
            var restored=new SettingsService(paths,services.GetRequiredService<LoggingService>());await restored.LoadAsync();
            Require(!restored.Current.ShowTestEncodeNotification,"Don't show again was not persisted.");
            await Submit(false,false,false,false);
            async Task Reenable(bool save)
            {
                Exception? failure=null;
                _=window.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=window.OwnedWindows.OfType<SettingsWindow>().Single();Place(dialog);
                    try
                    {
                        var toggle=Children<CheckBox>(dialog).Single(c=>c.Name=="TestEncodeNotificationInput");toggle.IsChecked=true;
                        Require(!vm.Settings.ShowTestEncodeNotification,"Unsaved settings changed the preference.");
                        Children<Button>(dialog).Single(b=>ButtonHints.GetKey(b)==(save?"DialogSave":"DialogCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch(Exception e){failure=e;dialog.Close();}
                });
                await vm.EditSettingsCommand.ExecuteAsync(null);if(failure is not null)throw failure;
            }
            await Reenable(false);Require(!vm.Settings.ShowTestEncodeNotification,"Cancelling settings re-enabled the notice.");
            await Reenable(true);Require(vm.Settings.ShowTestEncodeNotification,"Settings did not re-enable test notices.");
            await Submit(true,false,false,false);
            await restored.LoadAsync();Require(restored.Current.ShowTestEncodeNotification,"Re-enabled notice preference was not saved.");
            return new{Queued=true,Encoding=true,LiveStatus=true,SuppressionPersisted=true,SuppressedOnNextEncode=true,SettingsReenabled=true,ClosingKeepsEncoding=true,Languages=allLanguages?L.SupportedLanguages.ToArray():[language]};
        }
        finally
        {
            foreach(var notice in window.OwnedWindows.OfType<TestEncodeNotificationWindow>().ToArray())notice.Close();
            var added=queue.Jobs.Where(j=>!existing.Contains(j.Id)).ToArray();foreach(var job in added)queue.Cancel(job);
            await Until(()=>added.All(j=>j.Status!=JobStatus.Läuft));
            // Completed status precedes the final database write and scheduler cleanup. Wait for
            // those too, so closing the smoke-test window cannot open the active-jobs confirmation.
            await Until(()=>!queue.IsRunning);
            foreach(var job in added){await queue.RemoveAsync(job);vm.Jobs.Remove(job);}
            vm.SelectedMedia=originalMedia;vm.TestSeconds=seconds;vm.Settings.ParallelJobs=parallel;vm.Settings.ShowTestEncodeNotification=originalPreference;await settings.SaveAsync();ApplyLanguage(language);
        }
    }
}
