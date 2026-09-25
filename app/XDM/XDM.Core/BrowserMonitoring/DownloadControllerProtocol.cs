using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace XDM.Core.BrowserMonitoring
{
    internal static class DownloadControllerProtocol
    {
        internal const string ExtensionId = "hmbfgklncdkaclckkibeflpmlaedhgck";
        internal const string AllowedOrigin = "chrome-extension://" + ExtensionId;
        internal const int MaxActionBodyLength = 64;

        internal static bool IsOriginAllowed(string? origin)
        {
            return String.Equals(origin, AllowedOrigin, StringComparison.Ordinal);
        }

        internal static bool IsExtensionRequestAllowed(string? origin, string? clientId,
            string? fetchSite, string? fetchMode)
        {
            if (!String.Equals(clientId, ExtensionId, StringComparison.Ordinal)) return false;
            if (!String.IsNullOrEmpty(origin)) return IsOriginAllowed(origin);

            // Chromium/Opera extension fetches with host permission omit Origin.
            // Sec-Fetch-* cannot be supplied by ordinary page JavaScript, while the
            // explicit client header makes simple cross-origin page requests fail.
            return String.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase)
                && String.Equals(fetchMode, "cors", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TokenMatches(string expected, string? candidate)
        {
            if (candidate == null || candidate.Length != expected.Length) return false;
            var difference = 0;
            for (var i = 0; i < candidate.Length; i++) difference |= candidate[i] ^ expected[i];
            return difference == 0;
        }

        internal static bool IsJsonContentType(string? contentType)
        {
            return contentType != null && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsValidId(string id)
        {
            if (String.IsNullOrEmpty(id) || id.Length > 128) return false;
            return id.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.');
        }

        internal static bool IsActionAllowed(string state, string action)
        {
            switch (state)
            {
                case "Downloading": return action == "pause" || action == "stop";
                case "Waiting": return action == "stop";
                case "Stopped": return action == "resume" || action == "restart";
                case "Finished": return action == "restart";
                default: return false;
            }
        }

        internal static bool TryParseActionPath(string path, out string id, out string action)
        {
            id = String.Empty;
            action = String.Empty;
            const string prefix = "/controller/v1/downloads/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var parts = path.Substring(prefix.Length).Split('/');
            if (parts.Length != 2 || !IsValidId(parts[0])) return false;
            if (parts[1] != "pause" && parts[1] != "resume" && parts[1] != "stop" && parts[1] != "restart") return false;
            id = parts[0];
            action = parts[1];
            return true;
        }

        internal static string[] ActionsForState(string state)
        {
            switch (state)
            {
                case "Downloading": return new[] { "pause", "stop" };
                case "Waiting": return new[] { "stop" };
                case "Stopped": return new[] { "resume", "restart" };
                case "Finished": return new[] { "restart" };
                default: return Array.Empty<string>();
            }
        }

        internal static ControllerToolbarState Aggregate(IEnumerable<string> states)
        {
            var list = states.ToList();
            var active = list.Count(state => state == "Downloading" || state == "Waiting");
            return new ControllerToolbarState
            {
                ActiveCount = active,
                IsActive = active > 0,
                AllFinished = list.Count > 0 && list.All(state => state == "Finished")
            };
        }
    }

    internal sealed class ControllerToolbarState
    {
        public int ActiveCount { get; set; }
        public bool IsActive { get; set; }
        public bool AllFinished { get; set; }
    }

    internal static class DownloadControllerRuntimeState
    {
        private static readonly ConcurrentDictionary<string, ControllerRuntimeMetrics> Metrics = new();

        internal static void Update(string id, string speed, string eta)
        {
            Metrics[id] = new ControllerRuntimeMetrics { Speed = speed, Eta = eta };
        }

        internal static bool TryGet(string id, out ControllerRuntimeMetrics metrics) => Metrics.TryGetValue(id, out metrics!);

        internal static void Remove(string id) => Metrics.TryRemove(id, out _);
    }

    internal sealed class ControllerRuntimeMetrics
    {
        internal string? Speed { get; set; }
        internal string? Eta { get; set; }
    }

    internal sealed class ControllerDownloadDto
    {
        public string id { get; set; } = String.Empty;
        public string name { get; set; } = String.Empty;
        public DateTime dateAdded { get; set; }
        public int progress { get; set; }
        public string state { get; set; } = String.Empty;
        public long? totalBytes { get; set; }
        public long? downloadedBytes { get; set; }
        public string? speed { get; set; }
        public string? eta { get; set; }
        public string[] actions { get; set; } = Array.Empty<string>();
    }
}
