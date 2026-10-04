using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XDM.Core.BrowserMonitoring
{
    // Pure URL/format parsing shared by automatic extraction and its tests.
    internal static class YouTubeFormatCatalog
    {
        internal static string? CanonicalUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            var host = uri.Host.ToLowerInvariant();
            string? id = null;
            if (host == "youtu.be") id = uri.AbsolutePath.Trim('/');
            else if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal))
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 2 && (parts[0] == "shorts" || parts[0] == "live" || parts[0] == "embed")) id = parts[1];
                else if (uri.AbsolutePath == "/watch")
                {
                    foreach (var parameter in uri.Query.TrimStart('?').Split('&'))
                        if (parameter.StartsWith("v=", StringComparison.Ordinal)) id = parameter.Substring(2);
                }
            }
            return id != null && Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$")
                ? "https://www.youtube.com/watch?v=" + id : null;
        }

        internal static List<YouTubeFormatChoice> Parse(string json)
        {
            var root = JObject.Parse(json);
            var formats = (root["formats"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(f => IsHttp((string?)f["url"]) && ((string?)f["protocol"] == "https" || (string?)f["protocol"] == "http"
                    || ((string?)f["protocol"] ?? "").StartsWith("m3u8", StringComparison.Ordinal)))
                .Where(f => f["has_drm"]?.Value<bool>() != true).ToList();
            var audio = formats.Where(f => Codec(f, "vcodec") == null && Codec(f, "acodec") != null).ToList();
            var choices = new List<YouTubeFormatChoice>();
            foreach (var format in formats.Where(f => Codec(f, "vcodec") != null))
            {
                JObject? companion = null;
                if (Codec(format, "acodec") == null)
                {
                    companion = audio.Where(a => Hls(a) == Hls(format))
                        .OrderByDescending(a => (string?)a["language"] == (string?)root["language"])
                        .ThenByDescending(a => (double?)a["language_preference"] ?? -1)
                        .ThenByDescending(a => ((string?)format["ext"] == "mp4") == ((string?)a["ext"] == "m4a"))
                        .ThenByDescending(a => (double?)a["abr"] ?? (double?)a["tbr"] ?? 0).FirstOrDefault();
                    if (companion == null) continue;
                }
                choices.Add(Create(root, format, companion, false));
            }
            // One preferred transport for each video resolution/codec/frame rate.
            choices = choices.GroupBy(c => c.QualityKey).Select(g => g.OrderBy(c => c.Hls)
                .ThenByDescending(c => c.Bitrate).First()).ToList();
            // Audio remains available even when the video has separate streams.
            choices.AddRange(audio.GroupBy(f => (string?)f["ext"] + ":" + Codec(f, "acodec") + ":" + (string?)f["language"])
                .Select(g => g.OrderBy(Hls).ThenByDescending(f => (double?)f["abr"] ?? (double?)f["tbr"] ?? 0).First())
                .Select(f => Create(root, f, null, true)));
            return choices;
        }

        private static YouTubeFormatChoice Create(JObject root, JObject f, JObject? audio, bool audioOnly)
        {
            var ext = (string?)f["ext"] ?? "mp4";
            if (audioOnly && ext == "webm") ext = "weba";
            if (audio != null && !(ext == "mp4" && (string?)audio["ext"] == "m4a")) ext = "mkv";
            var abr = (double?)(audio ?? f)["abr"] ?? (double?)(audio ?? f)["tbr"] ?? 0;
            var height = (int?)f["height"] ?? 0;
            var codec = Codec(f, audioOnly ? "acodec" : "vcodec") ?? "";
            var fps = (double?)f["fps"] ?? 0;
            return new YouTubeFormatChoice
            {
                Title = (string?)root["title"] ?? "YouTube",
                Url = (string)f["url"]!, AudioUrl = (string?)audio?["url"],
                Extension = ext, Hls = Hls(f), AudioOnly = audioOnly,
                Quality = audioOnly ? $"AUDIO {Math.Round(abr)} kbps {codec}" : $"{height}p {fps:0.##} fps {codec}",
                QualityKey = $"{height}:{fps}:{codec}", Bitrate = (double?)f["tbr"] ?? 0,
                Size = audio == null ? Size(f) : (Size(f) > 0 && Size(audio) > 0 ? Size(f) + Size(audio) : 0),
                Headers = Headers(root, f), AudioHeaders = audio == null ? null : Headers(root, audio)
            };
        }
        private static long Size(JObject f) => (long?)f["filesize"] ?? (long?)f["filesize_approx"] ?? 0;
        private static bool Hls(JObject f) => ((string?)f["protocol"] ?? "").StartsWith("m3u8", StringComparison.Ordinal);
        private static string? Codec(JObject f, string name) => (string?)f[name] is string value && value != "none" && value.Length > 0 ? value : null;
        private static bool IsHttp(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");
        private static Dictionary<string, List<string>> Headers(JObject root, JObject format)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in new[] { root["http_headers"] as JObject, format["http_headers"] as JObject })
                if (source != null) foreach (var property in source.Properties()) result[property.Name] = new List<string> { (string)property.Value! };
            return result;
        }
    }
    internal sealed class YouTubeFormatChoice
    {
        public string Title = "", Url = "", Extension = "", Quality = "", QualityKey = "";
        public string? AudioUrl;
        public bool Hls, AudioOnly;
        public long Size;
        public double Bitrate;
        public Dictionary<string, List<string>> Headers = new();
        public Dictionary<string, List<string>>? AudioHeaders;
    }
}
