using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using TraceLog;

namespace XDM.Core.BrowserMonitoring
{
    internal static class MediaDiagnostics
    {
        private const int Capacity = 500;
        private static readonly object syncRoot = new();
        private static readonly Queue<Entry> entries = new();

        internal static void Write(string eventName, string? sourceUrl = null, string? tabId = null,
            string? detail = null, long size = 0, long elapsedMilliseconds = 0)
        {
            var entry = new Entry
            {
                TimeUtc = DateTime.UtcNow,
                Event = eventName,
                Source = Fingerprint(sourceUrl),
                TabId = tabId ?? string.Empty,
                Detail = detail ?? string.Empty,
                Size = size,
                ElapsedMilliseconds = elapsedMilliseconds
            };
            lock (syncRoot)
            {
                while (entries.Count >= Capacity) entries.Dequeue();
                entries.Enqueue(entry);
            }
            Log.Debug($"media-diag event={entry.Event} source={entry.Source} tab={entry.TabId} size={entry.Size} elapsedMs={entry.ElapsedMilliseconds} detail={entry.Detail}");
        }

        internal static string ToJson()
        {
            lock (syncRoot)
            {
                return JsonConvert.SerializeObject(entries.ToArray());
            }
        }

        private static string Fingerprint(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
        }

        private sealed class Entry
        {
            [JsonProperty("timeUtc")]
            public DateTime TimeUtc { get; set; }
            [JsonProperty("event")]
            public string Event { get; set; } = string.Empty;
            [JsonProperty("source")]
            public string Source { get; set; } = string.Empty;
            [JsonProperty("tabId")]
            public string TabId { get; set; } = string.Empty;
            [JsonProperty("detail")]
            public string Detail { get; set; } = string.Empty;
            [JsonProperty("size")]
            public long Size { get; set; }
            [JsonProperty("elapsedMs")]
            public long ElapsedMilliseconds { get; set; }
        }
    }
}
