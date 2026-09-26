using System;
using System.Collections.Generic;

namespace XDM.Core.BrowserMonitoring
{
    internal sealed class HlsVariantMetadataCache
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        private readonly TimeSpan lifetime;

        internal HlsVariantMetadataCache(TimeSpan? lifetime = null)
        {
            this.lifetime = lifetime ?? TimeSpan.FromMinutes(30);
        }

        internal void Remember(Uri? playlist, string? quality, DateTime? now = null)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(quality)) return;
            RememberCore(playlist, quality.Trim(), now ?? DateTime.UtcNow);
        }

        internal void RememberAudio(Uri? playlist, DateTime? now = null)
        {
            if (playlist == null) return;
            RememberCore(playlist, string.Empty, now ?? DateTime.UtcNow);
        }

        internal bool IsKnownChild(string playlistUrl, out string quality, DateTime? now = null)
        {
            quality = string.Empty;
            if (!Uri.TryCreate(playlistUrl, UriKind.Absolute, out var playlist)) return false;
            var timestamp = now ?? DateTime.UtcNow;
            lock (syncRoot)
            {
                RemoveExpired(timestamp);
                if (!entries.TryGetValue(Normalize(playlist), out var entry)) return false;
                quality = entry.Quality;
                return true;
            }
        }

        internal bool TryGet(string playlistUrl, out string quality, DateTime? now = null)
        {
            quality = string.Empty;
            return IsKnownChild(playlistUrl, out quality, now) && !string.IsNullOrEmpty(quality);
        }

        private void RememberCore(Uri playlist, string quality, DateTime timestamp)
        {
            lock (syncRoot)
            {
                RemoveExpired(timestamp);
                entries[Normalize(playlist)] = new Entry(quality, timestamp);
            }
        }

        private void RemoveExpired(DateTime now)
        {
            var expired = new List<string>();
            foreach (var pair in entries)
            {
                if (now - pair.Value.CreatedAt > lifetime) expired.Add(pair.Key);
            }
            foreach (var key in expired) entries.Remove(key);
        }

        private static string Normalize(Uri playlist)
        {
            var builder = new UriBuilder(playlist) { Fragment = string.Empty };
            return builder.Uri.AbsoluteUri;
        }

        private readonly struct Entry
        {
            internal Entry(string quality, DateTime createdAt)
            {
                Quality = quality;
                CreatedAt = createdAt;
            }

            internal string Quality { get; }
            internal DateTime CreatedAt { get; }
        }
    }
}
