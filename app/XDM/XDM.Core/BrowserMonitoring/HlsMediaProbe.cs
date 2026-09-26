using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using TraceLog;
using XDM.Core.MediaProcessor;

namespace XDM.Core.BrowserMonitoring
{
    internal static class HlsMediaProbe
    {
        private const int TimeoutMilliseconds = 8000;
        private static readonly Regex ResolutionPattern = new(@"(?:^|[ ,])(\d{2,5})x(\d{2,5})(?:[ ,])", RegexOptions.Compiled);
        private static readonly Regex BitratePattern = new(@"(?:^|[ ,])(\d+)\s*kb/s", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> completed = new(StringComparer.Ordinal);
        private static readonly SemaphoreSlim probeGate = new(1, 1);

        internal static void EnrichInBackground(Message message, string container)
        {
            if (completed.TryGetValue(message.Url, out var knownQuality))
            {
                MediaDiagnostics.Write("probe.cache-hit", message.Url, message.TabId, knownQuality);
                ApplicationContext.VideoTracker.UpdateMediaQuality(message.Url, $"[{container}] {knownQuality}");
                return;
            }
            if (!pending.TryAdd(message.Url, 0)) return;

            _ = Task.Run(async () =>
            {
                await probeGate.WaitAsync().ConfigureAwait(false);
                var timer = Stopwatch.StartNew();
                try
                {
                    MediaDiagnostics.Write("probe.started", message.Url, message.TabId);
                    var quality = Probe(message);
                    if (string.IsNullOrEmpty(quality))
                    {
                        MediaDiagnostics.Write("probe.no-result", message.Url, message.TabId,
                            elapsedMilliseconds: timer.ElapsedMilliseconds);
                        return;
                    }
                    if (completed.Count >= 256) completed.Clear();
                    completed[message.Url] = quality;
                    MediaDiagnostics.Write("probe.completed", message.Url, message.TabId, quality,
                        elapsedMilliseconds: timer.ElapsedMilliseconds);
                    ApplicationContext.VideoTracker.UpdateMediaQuality(message.Url, $"[{container}] {quality}");
                }
                catch (Exception ex)
                {
                    MediaDiagnostics.Write("probe.failed", message.Url, message.TabId, ex.GetType().Name,
                        elapsedMilliseconds: timer.ElapsedMilliseconds);
                    Log.Debug(ex, "HLS metadata probe failed");
                }
                finally
                {
                    pending.TryRemove(message.Url, out _);
                    probeGate.Release();
                }
            });
        }

        internal static string? ParseQuality(string probeOutput)
        {
            var resolution = ResolutionPattern.Match(probeOutput);
            if (!resolution.Success) return null;
            var quality = $"{resolution.Groups[1].Value}x{resolution.Groups[2].Value}";
            var bitrate = BitratePattern.Match(probeOutput);
            if (bitrate.Success) quality += $" {bitrate.Groups[1].Value} Kbps";
            return quality;
        }

        private static string? Probe(Message message)
        {
            var arguments = new List<string>
            {
                "-hide_banner", "-loglevel", "info",
                "-analyzeduration", "2000000", "-probesize", "2000000"
            };
            var headers = BuildHeaders(message);
            if (!string.IsNullOrEmpty(headers))
            {
                arguments.Add("-headers");
                arguments.Add(headers);
            }
            arguments.Add("-i");
            arguments.Add(message.Url);

            var startInfo = new ProcessStartInfo
            {
                FileName = FFmpegMediaProcessor.FindFFmpegBinary(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
#if NET5_0_OR_GREATER
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
#else
            startInfo.Arguments = XDM.Compatibility.ProcessStartInfoHelper.ArgumentListToArgsString(arguments.ToArray());
#endif

            using var process = Process.Start(startInfo);
            if (process == null) return null;
            var output = new StringBuilder();
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null) output.AppendLine(eventArgs.Data);
            };
            process.BeginErrorReadLine();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                process.WaitForExit();
            }
            return ParseQuality(output.ToString());
        }

        private static string BuildHeaders(Message message)
        {
            var headers = new StringBuilder();
            AddHeader(headers, "User-Agent", message.GetRequestHeaderFirstValue("User-Agent"));
            AddHeader(headers, "Referer", message.GetRequestHeaderFirstValue("Referer"));
            AddHeader(headers, "Cookie", message.Cookies);
            return headers.ToString();
        }

        private static void AddHeader(StringBuilder headers, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var sanitized = value.Replace("\r", string.Empty).Replace("\n", string.Empty);
            headers.Append(name).Append(": ").Append(sanitized).Append("\r\n");
        }
    }
}
