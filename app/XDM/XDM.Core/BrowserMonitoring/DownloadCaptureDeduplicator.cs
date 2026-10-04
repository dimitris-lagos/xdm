using System.Collections.Generic;
namespace XDM.Core.BrowserMonitoring
{
    // Reserve before dispatch: IPC requests arrive on separate threads.
    internal sealed class DownloadCaptureDeduplicator
    {
        private readonly Dictionary<string, long> captures = new();
        private readonly object gate = new();
        internal bool TryAccept(string key, long now, long lifetime)
        {
            lock (gate)
            {
                var expired = new List<string>();
                foreach (var entry in captures)
                    if (entry.Value <= now) expired.Add(entry.Key);
                foreach (var item in expired) captures.Remove(item);
                if (captures.ContainsKey(key)) return false;
                captures[key] = now + lifetime;
                return true;
            }
        }
    }
}
