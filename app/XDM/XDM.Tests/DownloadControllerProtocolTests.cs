using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using XDM.Core.BrowserMonitoring;
using XDM.Core.HttpServer;

namespace XDM.Tests
{
    public class DownloadControllerProtocolTests
    {
        [Test]
        public void ParsesMethodAndPathFromRequestLine()
        {
            HttpParser.ParseRequestLine("POST /controller/v1/downloads/abc/stop HTTP/1.1", out var method, out var path);
            Assert.That(method, Is.EqualTo("POST"));
            Assert.That(path, Is.EqualTo("/controller/v1/downloads/abc/stop"));
            Assert.Throws<HttpRequestException>(() => HttpParser.ParseRequestLine("broken", out _, out _));
        }

        [Test]
        public void ParsesHeadersCaseInsensitivelyAndRejectsOversizedBodies()
        {
            var headers = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["content-length"] = new List<string> { "42" }
            };
            Assert.That(HttpParser.ParseContentLength(headers), Is.EqualTo(42));
            headers["content-length"] = new List<string> { (HttpParser.MaxRequestBodyLength + 1).ToString() };
            var error = Assert.Throws<HttpRequestException>(() => HttpParser.ParseContentLength(headers));
            Assert.That(error!.StatusCode, Is.EqualTo(413));
            Assert.Throws<HttpRequestException>(() => HttpParser.ParseHeader("malformed", out _, out _));
            headers["content-length"] = new List<string> { "1", "1" };
            Assert.Throws<HttpRequestException>(() => HttpParser.ParseContentLength(headers));
        }

        [TestCase(null, false)]
        [TestCase("null", false)]
        [TestCase("https://example.com", false)]
        [TestCase(DownloadControllerProtocol.AllowedOrigin, true)]
        public void AllowsOnlyTheFixedExtensionOrigin(string? origin, bool expected)
        {
            Assert.That(DownloadControllerProtocol.IsOriginAllowed(origin), Is.EqualTo(expected));
        }

        [Test]
        public void AllowsOriginlessOperaExtensionRequestsWithBrowserProofOnly()
        {
            var id = DownloadControllerProtocol.ExtensionId;
            Assert.That(DownloadControllerProtocol.IsExtensionRequestAllowed(null, id, "none", "cors"), Is.True);
            Assert.That(DownloadControllerProtocol.IsExtensionRequestAllowed(DownloadControllerProtocol.AllowedOrigin, id, null, null), Is.True);
            Assert.That(DownloadControllerProtocol.IsExtensionRequestAllowed(null, null, "none", "cors"), Is.False);
            Assert.That(DownloadControllerProtocol.IsExtensionRequestAllowed(null, id, "cross-site", "cors"), Is.False);
            Assert.That(DownloadControllerProtocol.IsExtensionRequestAllowed("https://example.com", id, "cross-site", "cors"), Is.False);
        }

        [Test]
        public void RequiresAnExactSessionToken()
        {
            Assert.That(DownloadControllerProtocol.TokenMatches("secret", null), Is.False);
            Assert.That(DownloadControllerProtocol.TokenMatches("secret", "wrong!"), Is.False);
            Assert.That(DownloadControllerProtocol.TokenMatches("secret", "secret"), Is.True);
        }

        [Test]
        public void RuntimeMetricsAreThreadSafeAndRemovable()
        {
            DownloadControllerRuntimeState.Update("download-1", "2 MiB/s", "00:12");
            Assert.That(DownloadControllerRuntimeState.TryGet("download-1", out var metrics), Is.True);
            Assert.That(metrics.Speed, Is.EqualTo("2 MiB/s"));
            Assert.That(metrics.Eta, Is.EqualTo("00:12"));
            DownloadControllerRuntimeState.Remove("download-1");
            Assert.That(DownloadControllerRuntimeState.TryGet("download-1", out _), Is.False);
        }

        [Test]
        public void RequiresJsonContentType()
        {
            Assert.That(DownloadControllerProtocol.IsJsonContentType("application/json"), Is.True);
            Assert.That(DownloadControllerProtocol.IsJsonContentType("application/json; charset=utf-8"), Is.True);
            Assert.That(DownloadControllerProtocol.IsJsonContentType("text/plain"), Is.False);
            Assert.That(DownloadControllerProtocol.IsJsonContentType(null), Is.False);
        }

        [TestCase("Downloading", "pause", true)]
        [TestCase("Downloading", "stop", true)]
        [TestCase("Downloading", "resume", false)]
        [TestCase("Waiting", "stop", true)]
        [TestCase("Waiting", "pause", false)]
        [TestCase("Stopped", "resume", true)]
        [TestCase("Stopped", "restart", true)]
        [TestCase("Finished", "restart", false)]
        [TestCase("Finished", "open", true)]
        [TestCase("Finished", "open-folder", true)]
        [TestCase("Finished", "resume", false)]
        public void EnforcesStateActionMatrix(string state, string action, bool expected)
        {
            Assert.That(DownloadControllerProtocol.IsActionAllowed(state, action), Is.EqualTo(expected));
        }

        [Test]
        public void RejectsUnknownIdsAndActions()
        {
            Assert.That(DownloadControllerProtocol.TryParseActionPath("/controller/v1/downloads/abc/resume", out var id, out _), Is.True);
            Assert.That(id, Is.EqualTo("abc"));
            Assert.That(DownloadControllerProtocol.TryParseActionPath("/controller/v1/downloads/abc/open-folder", out _, out _), Is.True);
            Assert.That(DownloadControllerProtocol.TryParseActionPath("/controller/v1/downloads/a%2Fb/resume", out _, out _), Is.False);
            Assert.That(DownloadControllerProtocol.TryParseActionPath("/controller/v1/downloads/abc/delete", out _, out _), Is.False);
        }

        [Test]
        public void AggregatesToolbarStateWithoutTreatingStoppedAsFinished()
        {
            var active = DownloadControllerProtocol.Aggregate(new[] { "Downloading", "Waiting", "Finished" });
            Assert.That(active.ActiveCount, Is.EqualTo(2));
            Assert.That(active.IsActive, Is.True);
            Assert.That(DownloadControllerProtocol.Aggregate(new[] { "Finished" }).AllFinished, Is.True);
            Assert.That(DownloadControllerProtocol.Aggregate(new[] { "Finished", "Stopped" }).AllFinished, Is.False);
            Assert.That(DownloadControllerProtocol.Aggregate(System.Array.Empty<string>()).AllFinished, Is.False);
        }

        [Test]
        public void SnapshotSchemaContainsNoSensitiveFields()
        {
            var names = typeof(ControllerDownloadDto).GetProperties().Select(property => property.Name).ToArray();
            CollectionAssert.AreEquivalent(new[]
            {
                "id", "name", "dateAdded", "progress", "state", "totalBytes", "downloadedBytes", "speed", "eta", "actions"
            }, names);
            Assert.That(names.Any(name => name.Contains("url", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("path", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("cookie", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("credential", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("proxy", System.StringComparison.OrdinalIgnoreCase)), Is.False);
        }
    }
}
