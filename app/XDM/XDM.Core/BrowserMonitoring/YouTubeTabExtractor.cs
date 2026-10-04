using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TraceLog;
using XDM.Core.Downloader.Adaptive.Hls;
using XDM.Core.Downloader.Progressive.DualHttp;
using XDM.Core.Downloader.Progressive.SingleHttp;
using XDM.Core.Util;
using YDLWrapper;

namespace XDM.Core.BrowserMonitoring
{
    internal sealed class YouTubeTabExtractor
    {
        private readonly object gate = new();
        private readonly Dictionary<string, Job> tabs = new();
        private readonly SemaphoreSlim workers = new(2);
        private int pending;

        internal void Clear()
        {
            lock (gate)
            {
                foreach (var job in tabs.Values) job.Process?.Cancel();
                tabs.Clear();
            }
        }

        internal void Update(ExtensionData message)
        {
            if (string.IsNullOrEmpty(message.TabId)) return;
            var url = YouTubeFormatCatalog.CanonicalUrl(message.TabUrl);
            lock (gate)
            {
                if (tabs.TryGetValue(message.TabId, out var previous))
                {
                    if (previous.Url == url && DateTime.UtcNow < previous.RetryAfter) return;
                    previous.Process?.Cancel();
                    tabs.Remove(message.TabId);
                    (ApplicationContext.VideoTracker as VideoTracker)?.RemoveYouTubeTab(message.TabId);
                }
                if (url == null || (!Config.Instance.IsBrowserMonitoringEnabled || !Config.Instance.IsYtdlpEnabled) || pending >= 16) return;
                var job = new Job { Url = url, TabId = message.TabId, RetryAfter = DateTime.MaxValue };
                tabs[message.TabId] = job;
                pending++;
                ThreadPool.QueueUserWorkItem(_ => Extract(job));
            }
        }

        private bool Current(Job job) => tabs.TryGetValue(job.TabId, out var current) && ReferenceEquals(job, current);
        private void Extract(Job job)
        {
            workers.Wait();
            var process = new YDLProcess { Uri = new Uri(job.Url), SingleVideo = true };
            try
            {
                lock (gate)
                {
                    if (!Current(job) || (!Config.Instance.IsBrowserMonitoringEnabled || !Config.Instance.IsYtdlpEnabled)) return;
                    job.Process = process;
                }
                MediaDiagnostics.Write("youtube.extract-start", job.Url, job.TabId);
                process.Start();
                var choices = YouTubeFormatCatalog.Parse(File.ReadAllText(process.JsonOutputFile!));
                lock (gate)
                {
                    if (!Current(job) || (!Config.Instance.IsBrowserMonitoringEnabled || !Config.Instance.IsYtdlpEnabled)) return;
                    foreach (var choice in choices) Publish(job, choice);
                    job.RetryAfter = choices.Count > 0 ? DateTime.UtcNow.AddMinutes(10) : DateTime.UtcNow.AddMinutes(1);
                    MediaDiagnostics.Write("youtube.extract-complete", job.Url, job.TabId, "choices=" + choices.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Automatic YouTube extraction failed");
                MediaDiagnostics.Write("youtube.extract-failed", job.Url, job.TabId, ex.Message);
                lock (gate) if (Current(job)) job.RetryAfter = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                if (process.JsonOutputFile != null)
                    try { File.Delete(process.JsonOutputFile); } catch { }
                lock (gate) { job.Process = null; pending--; }
                workers.Release();
            }
        }

        private static void Publish(Job job, YouTubeFormatChoice choice)
        {
            var display = new StreamingVideoDisplayInfo
            {
                TabId = job.TabId, TabUrl = job.Url, CreationTime = DateTime.Now, YouTubeExtraction = true,
                Quality = "[" + choice.Extension.ToUpperInvariant() + "] " + choice.Quality, Size = choice.Size
            };
            var file = FileHelper.SanitizeFileName(choice.Title) + "." + choice.Extension;
            if (choice.Hls)
                ApplicationContext.VideoTracker.AddVideoNotification(display, new MultiSourceHLSDownloadInfo
                {
                    VideoUri = choice.Url, AudioUri = choice.AudioUrl, File = file, Headers = choice.Headers
                });
            else if (choice.AudioUrl != null)
                ApplicationContext.VideoTracker.AddVideoNotification(display, new DualSourceHTTPDownloadInfo
                {
                    Uri1 = choice.Url, Uri2 = choice.AudioUrl, File = file,
                    Headers1 = choice.Headers, Headers2 = choice.AudioHeaders!, ContentLength = choice.Size
                });
            else
                ApplicationContext.VideoTracker.AddVideoNotification(display, new SingleSourceHTTPDownloadInfo
                {
                    Uri = choice.Url, File = file, Headers = choice.Headers, ContentLength = choice.Size
                });
        }

        private sealed class Job
        {
            internal string Url = "", TabId = "";
            internal DateTime RetryAfter;
            internal YDLProcess? Process;
        }
    }
}
