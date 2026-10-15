using System.Collections.Generic;
using System.Diagnostics;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasTopologyTests
{
    [Test]
    public void ServiceRadiusAndCuts()
    {
        var topology = new KiasTopology();
        var tiles = new List<Vector2i>();
        for (var x = 0; x <= 10; x++)
            tiles.Add(new Vector2i(x, 0));
        topology.Rebuild(tiles);
        Assert.Multiple(() =>
        {
            Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(10, 2)), Is.True);
            Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(10, 3)), Is.False);
        });
        tiles.Remove(new Vector2i(5, 0));
        topology.Rebuild(tiles);
        Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(10, 0)), Is.False);
        tiles.Add(new Vector2i(5, 0));
        topology.Rebuild(tiles);
        Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(10, 0)), Is.True);
    }

    [Test]
    public void DiagonalCablesDoNotConnectAndEmptyRebuildClearsCoverage()
    {
        var topology = new KiasTopology();
        topology.Rebuild(new[] { new Vector2i(0, 0), new Vector2i(1, 1) });
        Assert.That(topology.SegmentCount, Is.EqualTo(2));
        Assert.That(topology.Connected(new Vector2i(-2, 0), new Vector2i(3, 1)), Is.False);
        topology.Rebuild(System.Array.Empty<Vector2i>());
        Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(0, 0)), Is.False);
    }

    [Test]
    public void FleetTopologyStressHarness()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 100; x++)
            tiles.Add(new Vector2i(x, 0));
        var allocated = System.GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var fleet = new List<KiasTopology>();
        for (var ship = 0; ship < 200; ship++)
        {
            var topology = new KiasTopology();
            topology.Rebuild(tiles);
            fleet.Add(topology);
        }
        TestContext.WriteLine($"200 grids / 20000 cables: {timer.Elapsed.TotalMilliseconds:F2} ms, {System.GC.GetAllocatedBytesForCurrentThread() - allocated} bytes");
        timer.Restart();
        foreach (var topology in fleet)
            Assert.That(topology.Connected(new Vector2i(0, 0), new Vector2i(99, 2)), Is.True);
        TestContext.WriteLine($"200 cached connections: {timer.Elapsed.TotalMilliseconds:F2} ms");
    }
}
