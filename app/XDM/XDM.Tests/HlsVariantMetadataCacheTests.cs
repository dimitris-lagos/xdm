using NUnit.Framework;
using System;
using XDM.Core.BrowserMonitoring;

namespace XDM.Tests
{
    public class HlsVariantMetadataCacheTests
    {
        [Test]
        public void ResolvesChildPlaylistMetadataAndIgnoresFragments()
        {
            var cache = new HlsVariantMetadataCache();
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            cache.Remember(new Uri("https://media.example/1080/index.m3u8?token=abc#source"),
                "1920x1080 6000 Kbps", now);

            Assert.That(cache.TryGet("https://media.example/1080/index.m3u8?token=abc", out var quality, now), Is.True);
            Assert.That(quality, Is.EqualTo("1920x1080 6000 Kbps"));
        }

        [Test]
        public void ExpiresOldSignedPlaylistMetadata()
        {
            var cache = new HlsVariantMetadataCache(TimeSpan.FromMinutes(5));
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            cache.Remember(new Uri("https://media.example/video.m3u8"), "1280x720", now);

            Assert.That(cache.TryGet("https://media.example/video.m3u8", out _, now.AddMinutes(6)), Is.False);
        }

        [Test]
        public void RecognizesAudioChildrenWithoutInventingVideoQuality()
        {
            var cache = new HlsVariantMetadataCache();
            var child = new Uri("https://media.example/audio/index.m3u8");
            cache.RememberAudio(child);

            Assert.That(cache.IsKnownChild(child.ToString(), out var quality), Is.True);
            Assert.That(quality, Is.Empty);
            Assert.That(cache.TryGet(child.ToString(), out _), Is.False);
        }
    }
}
