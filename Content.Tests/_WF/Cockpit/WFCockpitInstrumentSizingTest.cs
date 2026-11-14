using Content.Client._WF.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks when the dial column swaps to compact faces and how small they get.</summary>
[TestFixture]
public sealed class WFCockpitInstrumentSizingTest
{
    [Test]
    public void FullDialsNeedAColumnThatShowsThemWhole()
    {
        var fits = WFCockpitInstrumentSizing.FullColumnHeight + WFCockpitInstrumentSizing.ColumnChrome;
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits), Is.False);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits + 200), Is.False);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits - 1), Is.True);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(80), Is.True);
    }

    [TestCase(176, 110)]
    [TestCase(160, 100)]
    public void CompactFacesKeepTheEarlierReadableSizes(float full, float compact)
    {
        Assert.That(WFCockpitInstrumentSizing.CompactHeight(full), Is.EqualTo(compact));
        Assert.That(WFCockpitInstrumentSizing.CompactHeight(full), Is.LessThan(full));
    }
}
