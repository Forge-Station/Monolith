using System;
using Content.Client._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

/// <summary>Prevents missing or disabled collision telemetry from lighting a clear-status indication.</summary>
[TestFixture]
public sealed class WFCockpitTcasReadingTest
{
    [TestCase(false, false, WFCockpitTcasState.NoSignal)]
    [TestCase(false, true, WFCockpitTcasState.NoSignal)]
    [TestCase(true, false, WFCockpitTcasState.Off)]
    [TestCase(true, true, WFCockpitTcasState.Clear)]
    public void ClearRequiresAvailableEnabledTelemetry(bool available, bool enabled, WFCockpitTcasState expected)
    {
        Assert.That(WFCockpitTcasReading.From(available, enabled, null).State, Is.EqualTo(expected));
    }

    [TestCase(CollisionWarningLevel.Advisory, WFCockpitTcasState.Caution)]
    [TestCase(CollisionWarningLevel.Imminent, WFCockpitTcasState.Warning)]
    public void WarningsRetainTheirSeverityAndTargetDetails(CollisionWarningLevel level, WFCockpitTcasState expected)
    {
        var warning = new CollisionWarningComponent
        {
            Level = level,
            ImpactTime = TimeSpan.FromSeconds(8),
            ThreatName = "Approaching vessel",
            Bearing = 315,
            ClosingSpeed = 24,
        };
        var reading = WFCockpitTcasReading.From(true, true, warning);
        Assert.Multiple(() =>
        {
            Assert.That(reading.State, Is.EqualTo(expected));
            Assert.That(reading.ImpactTime, Is.EqualTo(warning.ImpactTime));
            Assert.That(reading.ThreatName, Is.EqualTo(warning.ThreatName));
            Assert.That(reading.Bearing, Is.EqualTo(warning.Bearing));
            Assert.That(reading.ClosingSpeed, Is.EqualTo(warning.ClosingSpeed));
            Assert.That(WFCockpitTcasReading.From(true, false, warning).State, Is.EqualTo(WFCockpitTcasState.Off));
            Assert.That(WFCockpitTcasReading.From(false, true, warning).State, Is.EqualTo(WFCockpitTcasState.NoSignal));
        });
    }

    [TestCase(0, true)]
    [TestCase(0.999, true)]
    [TestCase(1, false)]
    [TestCase(1.999, false)]
    [TestCase(2, true)]
    public void CautionFlashesOnASeparateTwoSecondCycle(double seconds, bool illuminated)
    {
        var lamps = new WFCockpitTcasReading(WFCockpitTcasState.Caution).LampsAt(TimeSpan.FromSeconds(seconds));
        Assert.That(lamps, Is.EqualTo(new WFCockpitTcasLamps(illuminated, false, false, false)));
    }

    [TestCase(0, true, true)]
    [TestCase(0.249, true, true)]
    [TestCase(0.25, true, false)]
    [TestCase(0.499, true, false)]
    [TestCase(0.5, true, true)]
    [TestCase(1, false, true)]
    [TestCase(1.25, false, false)]
    [TestCase(1.5, false, true)]
    [TestCase(2, true, true)]
    public void WarningFlashesFasterWithoutReplacingCaution(double seconds, bool caution, bool warning)
    {
        var lamps = new WFCockpitTcasReading(WFCockpitTcasState.Warning).LampsAt(TimeSpan.FromSeconds(seconds));
        Assert.That(lamps, Is.EqualTo(new WFCockpitTcasLamps(caution, warning, false, false)));
    }

    [TestCase(WFCockpitTcasState.Clear, true, false)]
    [TestCase(WFCockpitTcasState.Off, false, true)]
    [TestCase(WFCockpitTcasState.NoSignal, false, true)]
    public void OkAndFaultStaySteadyDuringEveryFlashPhase(WFCockpitTcasState state, bool ok, bool fault)
    {
        foreach (var seconds in new[] { 0, 0.25, 0.5, 1, 1.25, 2 })
            Assert.That(new WFCockpitTcasReading(state).LampsAt(TimeSpan.FromSeconds(seconds)),
                Is.EqualTo(new WFCockpitTcasLamps(false, false, ok, fault)));
    }

}
