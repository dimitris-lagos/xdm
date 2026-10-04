using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using XDM.Core.BrowserMonitoring;

namespace XDM.Tests
{
    public class DownloadControllerEventsTests
    {
        [Test]
        public void ReplaysShortDownloadLifecycleInOrder()
        {
            var before = DownloadControllerEvents.Sequence;
            DownloadControllerEvents.Publish(n => "start:" + n);
            DownloadControllerEvents.Publish(n => "finish:" + n);
            var replay = DownloadControllerEvents.Read(before, false)!;
            Assert.That(replay.Select(e => e.Sequence), Is.EqualTo(new[] { before + 1, before + 2 }));
            Assert.That(replay[0].Json, Does.StartWith("start:"));
            Assert.That(replay[1].Json, Does.StartWith("finish:"));
            Assert.That(DownloadControllerEvents.Read(before + 2, false), Is.Empty);
        }

        [Test]
        public void ExpiredAndFutureCursorsRequireSnapshot()
        {
            var before = DownloadControllerEvents.Sequence;
            for (var i = 0; i <= DownloadControllerEvents.Capacity; i++)
                DownloadControllerEvents.Publish(n => n.ToString());
            Assert.That(DownloadControllerEvents.Read(before, false), Is.Null);
            Assert.That(DownloadControllerEvents.Read(DownloadControllerEvents.Sequence + 1, false), Is.Null);
            Assert.That(DownloadControllerEvents.Read(DownloadControllerEvents.Sequence - 1, false)!.Length, Is.EqualTo(1));
        }

        [TestCase(10)]
        [TestCase(126)]
        [TestCase(65536)]
        public void FramesEncodeUtf8AndAllLengthFormats(int length)
        {
            using var stream = new MemoryStream();
            var value = new string('a', length) + "✓";
            DownloadControllerEvents.WriteText(stream, value);
            var frame = stream.ToArray();
            Assert.That(frame[0], Is.EqualTo(0x81));
            var offset = 2;
            long size = frame[1];
            if (size == 126) { size = (frame[2] << 8) | frame[3]; offset = 4; }
            else if (size == 127) {
                size = 0;
                for (var i = 2; i < 10; i++) size = (size << 8) | frame[i];
                offset = 10;
            }
            Assert.That(size, Is.EqualTo(Encoding.UTF8.GetByteCount(value)));
            Assert.That(Encoding.UTF8.GetString(frame, offset, (int)size), Is.EqualTo(value));
        }

        [Test]
        public async System.Threading.Tasks.Task WebSocketClientReceivesEventsAndClosesCleanly()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var server = System.Threading.Tasks.Task.Run(() => {
                using var tcp = listener.AcceptTcpClient();
                var context = XDM.Core.HttpServer.HttpParser.ParseContext(tcp);
                using var stream = context.UpgradeWebSocket(context.RequestHeaders["Sec-WebSocket-Key"][0]);
                DownloadControllerEvents.WriteText(stream, "{\"state\":\"Downloading\"}");
                DownloadControllerEvents.WriteText(stream, "{\"state\":\"Finished\"}");
                using var closed = new System.Threading.CancellationTokenSource();
                DownloadControllerEvents.ReadControls(stream, closed);
                Assert.That(closed.IsCancellationRequested, Is.True);
            });
            try
            {
                using var client = new System.Net.WebSockets.ClientWebSocket();
                client.Options.AddSubProtocol("xdm-controller");
                await client.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/controller/v1/events"), timeout.Token);
                var buffer = new byte[1024];
                foreach (var expected in new[] { "Downloading", "Finished" })
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    Assert.That(result.MessageType, Is.EqualTo(System.Net.WebSockets.WebSocketMessageType.Text));
                    Assert.That(Encoding.UTF8.GetString(buffer, 0, result.Count), Does.Contain(expected));
                }
                await client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", timeout.Token);
                await server;
            }
            finally { listener.Stop(); }
        }
        [Test]
        public void FailedSerializationDoesNotAdvanceCursor()
        {
            var before = DownloadControllerEvents.Sequence;
            Assert.Throws<InvalidOperationException>(() => DownloadControllerEvents.Publish(n => throw new InvalidOperationException()));
            Assert.That(DownloadControllerEvents.Sequence, Is.EqualTo(before));
        }
    }
}
