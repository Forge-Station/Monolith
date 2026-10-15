using System;
using Content.Server._Forge.KIAS;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasPhaseMeasurementTests
{
    [Test]
    public void NestedScopesKeepAdditiveExclusiveCosts()
    {
        var system = new KiasSystem { MeasureUpdates = true };
        using (new KiasPhaseMeasurement(system, KiasPhase.RuntimeUpdate))
        {
            using (new KiasPhaseMeasurement(system, KiasPhase.IoEmit))
                GC.KeepAlive(new byte[128]);
            using (new KiasPhaseMeasurement(system, KiasPhase.RuntimeCommands))
                GC.KeepAlive(new byte[64]);
        }
        var parent = system.Phases[(int) KiasPhase.RuntimeUpdate];
        var emit = system.Phases[(int) KiasPhase.IoEmit];
        var commands = system.Phases[(int) KiasPhase.RuntimeCommands];
        Assert.That(parent.Calls, Is.EqualTo(1));
        Assert.That(parent.ExclusiveTimestampTicks + emit.TimestampTicks + commands.TimestampTicks, Is.EqualTo(parent.TimestampTicks));
        Assert.That(parent.ExclusiveAllocatedBytes + emit.AllocatedBytes + commands.AllocatedBytes, Is.EqualTo(parent.AllocatedBytes));
        Assert.That(emit.ExclusiveTimestampTicks, Is.EqualTo(emit.TimestampTicks));
        Assert.That(parent.ExclusiveTimestampTicks, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void DisabledScopesDoNotRecordOrNotify()
    {
        var system = new KiasSystem();
        using (new KiasPhaseMeasurement(system, KiasPhase.CrewUpdate))
            GC.KeepAlive(new byte[64]);
        Assert.That(system.Phases[(int) KiasPhase.CrewUpdate].Calls, Is.Zero);
    }
}
