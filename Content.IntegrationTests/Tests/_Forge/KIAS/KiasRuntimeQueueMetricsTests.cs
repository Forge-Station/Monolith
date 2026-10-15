using Content.Server._Forge.KIAS.Controllers;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasRuntimeQueueMetricsTests
{
    [Test]
    public void QueueAgeIncludesDelayedTailAndKeepsExactOverflowMaximum()
    {
        var stats = new KiasRuntimeQueueMetrics();
        for (var tick = 1u; tick <= 100; tick++)
        {
            stats.Enqueue((int) tick);
            stats.Consume(200, 200 + tick);
        }
        Assert.That(stats.Percentile(.5), Is.EqualTo(50));
        Assert.That(stats.Percentile(.95), Is.EqualTo(95));
        Assert.That(stats.Percentile(.99), Is.EqualTo(99));
        Assert.That(stats.HighWater, Is.EqualTo(100));
        Assert.That(stats.Consumed, Is.EqualTo(stats.Enqueued));
        stats.Enqueue(1);
        stats.Consume(0, 12000);
        Assert.That(stats.MaxAgeTicks, Is.EqualTo(12000));
        Assert.That(stats.AgeTicks[4096], Is.EqualTo(1));
    }
}
