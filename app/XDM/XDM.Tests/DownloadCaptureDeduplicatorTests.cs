using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using XDM.Core.BrowserMonitoring;
public class DownloadCaptureDeduplicatorTests
{
    [Test] public void ThreeConcurrentCapturesAreAcceptedOnce()
    {
        var guard = new DownloadCaptureDeduplicator();
        var accepted = 0;
        Parallel.For(0, 3, _ => { if (guard.TryAccept("same", 100, 5000)) Interlocked.Increment(ref accepted); });
        Assert.AreEqual(1, accepted);
    }
    [Test] public void DistinctDownloadsAndExpiredLegacyCapturesAreAccepted()
    {
        var guard = new DownloadCaptureDeduplicator();
        Assert.IsTrue(guard.TryAccept("event1:url", 0, 5000));
        Assert.IsTrue(guard.TryAccept("event2:url", 0, 5000));
        Assert.IsFalse(guard.TryAccept("event1:url", 4999, 5000));
        Assert.IsTrue(guard.TryAccept("event1:url", 5000, 5000));
    }
}
