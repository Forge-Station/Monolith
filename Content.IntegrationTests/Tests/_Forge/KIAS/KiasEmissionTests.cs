using Content.Server._Forge.KIAS;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasEmissionTests
{
    [Test]
    public void BurstSourcesEscalationAndCooldownRemainIndependent()
    {
        var gate = new KiasEmissionGate();
        var grid = new EntityUid(1);
        Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.Zero, 10, 2, out _), Is.True);
        for (var i = 0; i < 100; i++)
            Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.FromSeconds(1), 10, 2, out _), Is.False);
        Assert.That(gate.Allow(grid, "fire:bridge", TimeSpan.FromSeconds(1), 10, 3, out _), Is.True);
        Assert.That(gate.Allow(grid, "impact:fore", TimeSpan.FromSeconds(1), 10, 2, out _), Is.True);
        Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.FromSeconds(2), 10, 3, out var suppressed), Is.True);
        Assert.That(suppressed, Is.EqualTo(100));
        Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.FromSeconds(3), 10, 2, out _), Is.False);
        Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.FromSeconds(12), 10, 2, out suppressed), Is.True);
        Assert.That(suppressed, Is.EqualTo(1));
        gate.Remove(grid);
        Assert.That(gate.Allow(grid, "impact:aft", TimeSpan.FromSeconds(12), 10, 2, out _), Is.True);
    }
}
