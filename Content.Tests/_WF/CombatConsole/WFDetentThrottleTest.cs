using System;
using Content.Client._WF.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WF.CombatConsole;

/// <summary>Prevents rapid bearing movement from stacking audio or leaving queued clicks.</summary>
[TestFixture]
public sealed class WFDetentThrottleTest
{
    [Test]
    public void ContinuousDragSpacesClicksBeyondTheClipLength()
    {
        var throttle = new WFDetentThrottle();
        Assert.That(throttle.TryPlay(TimeSpan.Zero), Is.True);
        for (var milliseconds = 1; milliseconds < 150; milliseconds++)
            Assert.That(throttle.TryPlay(TimeSpan.FromMilliseconds(milliseconds)), Is.False);
        Assert.That(throttle.TryPlay(TimeSpan.FromMilliseconds(150)), Is.True);
        Assert.That(throttle.TryPlay(TimeSpan.FromMilliseconds(299)), Is.False);
        Assert.That(throttle.TryPlay(TimeSpan.FromMilliseconds(300)), Is.True);
    }

    [Test]
    public void ResumingAfterIdlePlaysOnceWithoutCatchingUp()
    {
        var throttle = new WFDetentThrottle();
        Assert.That(throttle.TryPlay(TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(throttle.TryPlay(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(throttle.TryPlay(TimeSpan.FromSeconds(10)), Is.False);
        Assert.That(throttle.TryPlay(TimeSpan.FromMilliseconds(10001)), Is.False);
    }
}
