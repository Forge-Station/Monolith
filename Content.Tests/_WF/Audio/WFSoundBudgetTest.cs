using System;
using Content.Shared._WF.Audio;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._WF.Audio;

/// <summary>A sound budget counts per place and per window.</summary>
[TestFixture]
[TestOf(typeof(WFSoundBudget))]
public sealed class WFSoundBudgetTest
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    /// <summary>A place gets its allowance once a window, however many times it asks.</summary>
    [Test]
    public void APlaceGetsItsAllowanceOnceAWindow()
    {
        var budget = new WFSoundBudget(3, Second);
        var place = new EntityUid(10);
        var now = TimeSpan.FromSeconds(100);
        var admitted = 0;

        for (var i = 0; i < 50; i++)
        {
            // Spread over most of the window: a later ask must not start a new one.
            if (budget.Allow(place, now + TimeSpan.FromMilliseconds(i * 15)))
                admitted++;
        }

        Assert.That(admitted, Is.EqualTo(3));
        Assert.That(budget.Allow(place, now + Second), Is.True, "The allowance did not come back when the window ended.");
    }

    /// <summary>One place spending its allowance leaves another's whole.</summary>
    [Test]
    public void PlacesDoNotShare()
    {
        var budget = new WFSoundBudget(2, Second);
        var crashing = new EntityUid(10);
        var elsewhere = new EntityUid(11);
        var now = TimeSpan.FromSeconds(5);

        for (var i = 0; i < 20; i++)
        {
            budget.Allow(crashing, now);
        }

        Assert.Multiple(() =>
        {
            Assert.That(budget.Allow(crashing, now), Is.False);
            Assert.That(budget.Allow(elsewhere, now), Is.True);
            Assert.That(budget.Allow(elsewhere, now), Is.True);
            Assert.That(budget.Allow(elsewhere, now), Is.False);
        });
    }

    /// <summary>Places whose window is long over are forgotten, so grids that came and went leave nothing behind.</summary>
    [Test]
    public void StalePlacesAreForgotten()
    {
        var budget = new WFSoundBudget(2, Second);

        for (var i = 1; i <= 500; i++)
        {
            budget.Allow(new EntityUid(i), TimeSpan.FromSeconds(i * 2));
        }

        Assert.That(budget.Places, Is.LessThan(100));
    }
}
