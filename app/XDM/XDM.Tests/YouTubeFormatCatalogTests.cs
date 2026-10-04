using NUnit.Framework;
using Newtonsoft.Json.Linq;
using System.Linq;
using XDM.Core.BrowserMonitoring;

namespace XDM.Tests
{
    public class YouTubeFormatCatalogTests
    {
        [TestCase("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=4")]
        [TestCase("https://youtu.be/jNQXAC9IVRw")]
        [TestCase("https://m.youtube.com/shorts/jNQXAC9IVRw")]
        [TestCase("https://www.youtube.com/live/jNQXAC9IVRw")]
        public void NormalizesVideoPages(string url) => Assert.AreEqual("https://www.youtube.com/watch?v=jNQXAC9IVRw", YouTubeFormatCatalog.CanonicalUrl(url));
        [TestCase("https://youtube.com.evil.test/watch?v=jNQXAC9IVRw")]
        [TestCase("file:///watch?v=jNQXAC9IVRw")]
        [TestCase("https://youtube.com/watch?v=x")]
        [TestCase("https://youtube.com/playlist?list=xxx")]
        public void RejectsOtherUrls(string url) => Assert.IsNull(YouTubeFormatCatalog.CanonicalUrl(url));

        private static JObject Format(string id, string ext, string vcodec, string acodec, string protocol = "https", int height = 0, int abr = 0) =>
            new JObject { ["format_id"] = id, ["url"] = "https://example.test/" + id, ["ext"] = ext,
                ["vcodec"] = vcodec, ["acodec"] = acodec, ["height"] = height, ["abr"] = abr,
                ["protocol"] = protocol, ["filesize"] = 100, ["http_headers"] = new JObject { ["User-Agent"] = "test-agent" } };

        [Test]
        public void PairsVideoWithCompatibleBestAudioAndKeepsAudioChoices()
        {
            var root = new JObject { ["title"] = "clip", ["formats"] = new JArray(
                Format("v", "mp4", "avc1", "none", height: 1080),
                Format("a", "m4a", "none", "aac", abr: 128),
                Format("a2", "m4a", "none", "aac", abr: 192),
                Format("a3", "webm", "none", "opus", abr: 256)) };
            var choices = YouTubeFormatCatalog.Parse(root.ToString());
            var video = choices.Single(c => !c.AudioOnly);
            Assert.AreEqual("https://example.test/a2", video.AudioUrl);
            Assert.AreEqual("mp4", video.Extension);
            Assert.AreEqual(200, video.Size);
            Assert.AreEqual("test-agent", video.Headers["User-Agent"].Single());
            Assert.AreEqual(2, choices.Count(c => c.AudioOnly));
            Assert.AreEqual("weba", choices.Single(c => c.AudioOnly && c.Url.EndsWith("a3")).Extension);
        }

        [Test]
        public void PrefersDirectStreamsOverDuplicateHlsAndRejectsStoryboardsAndDrm()
        {
            var drm = Format("drm", "mp4", "avc1", "aac", height: 720);
            drm["has_drm"] = true;
            var root = new JObject { ["formats"] = new JArray(
                Format("hls", "mp4", "avc1", "aac", "m3u8_native", 1080),
                Format("direct", "mp4", "avc1", "aac", height: 1080),
                Format("storyboard", "mhtml", "none", "none", "mhtml"), drm) };
            var choices = YouTubeFormatCatalog.Parse(root.ToString());
            Assert.AreEqual(1, choices.Count);
            Assert.IsFalse(choices[0].Hls);
            Assert.IsTrue(choices[0].Url.EndsWith("direct"));
        }

        [Test]
        public void DoesNotPairAnHttpVideoWithHlsAudio()
        {
            var root = new JObject { ["formats"] = new JArray(
                Format("v", "mp4", "avc1", "none", height: 1080),
                Format("a", "m4a", "none", "aac", "m3u8_native", abr: 128)) };
            Assert.IsTrue(YouTubeFormatCatalog.Parse(root.ToString()).All(c => c.AudioOnly));
        }
    }
}
