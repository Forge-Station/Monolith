using Content.Client._WF.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WF.CombatConsole;

/// <summary>Keeps missile warnings quiet during repeated state refreshes and hidden-window updates.</summary>
[TestFixture]
public sealed class WFThreatAnnunciatorTest
{
    [Test]
    public void WarnsOnNewLockButDoesNotLoopWhileTracked()
    {
        var annunciator = new WFThreatAnnunciator();
        Assert.That(annunciator.Update(0, true), Is.False);
        Assert.That(annunciator.Update(1, true), Is.True);
        Assert.That(annunciator.Update(1, true), Is.False);
        Assert.That(annunciator.Update(3, true), Is.False);
        Assert.That(annunciator.Update(0, true), Is.False);
        Assert.That(annunciator.Update(2, true), Is.True);
    }

    [Test]
    public void HiddenUpdatesDoNotPlayOrQueueWarnings()
    {
        var annunciator = new WFThreatAnnunciator();
        Assert.That(annunciator.Update(1, false), Is.False);
        Assert.That(annunciator.Update(1, true), Is.False);
        Assert.That(annunciator.Update(0, false), Is.False);
        Assert.That(annunciator.Update(1, true), Is.True);
    }
}
