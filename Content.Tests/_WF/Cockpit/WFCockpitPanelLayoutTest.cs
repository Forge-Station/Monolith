using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.Cockpit;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks where the world view, MFD and the panels around them sit in each cockpit arrangement.</summary>
[TestFixture]
public sealed class WFCockpitPanelLayoutTest
{
    private static WFCockpitPanelLayout.Metrics Screen(float width, float height, float strip = 0, float votes = 0) =>
        new(new Vector2(width, height), 8, 260, 400, 240, 44, strip, votes);

    private static bool Overlap(UIBox2 a, UIBox2 b) =>
        a.Left < b.Right - 0.5f && b.Left < a.Right - 0.5f && a.Top < b.Bottom - 0.5f && b.Top < a.Bottom - 0.5f;

    private static void AssertBox(UIBox2 actual, UIBox2 expected)
    {
        Assert.That(actual.Left, Is.EqualTo(expected.Left).Within(0.01f));
        Assert.That(actual.Top, Is.EqualTo(expected.Top).Within(0.01f));
        Assert.That(actual.Right, Is.EqualTo(expected.Right).Within(0.01f));
        Assert.That(actual.Bottom, Is.EqualTo(expected.Bottom).Within(0.01f));
    }

    [Test]
    public void NormalPutsTheWorldInTheCentreAndTheMfdInTheSideColumn()
    {
        var metrics = Screen(1920, 1080);
        var frame = WFCockpitPanelLayout.Compute(metrics, false, false);
        Assert.Multiple(() =>
        {
            Assert.That(frame.WorldShown, Is.True);
            Assert.That(frame.World.Left, Is.EqualTo(frame.Motion.Left));
            Assert.That(frame.World.Top, Is.EqualTo(frame.Motion.Bottom + WFCockpitPanelLayout.Gap).Within(0.01f));
            Assert.That(frame.World.Bottom, Is.EqualTo(frame.Row.Top - WFCockpitPanelLayout.Gap).Within(0.01f));
            Assert.That(frame.Mfd.Left, Is.GreaterThan(frame.World.Right));
            Assert.That(frame.Mfd.Right, Is.EqualTo(1920 - WFCockpitPanelLayout.Margin).Within(0.01f));
            Assert.That(frame.Mfd.Top, Is.EqualTo(metrics.Top));
            Assert.That(frame.Mfd.Bottom, Is.EqualTo(1080 - WFCockpitPanelLayout.Margin).Within(0.01f));
            Assert.That(frame.Row.Width, Is.EqualTo(frame.World.Width).Within(0.01f));
        });
    }

    [Test]
    public void SwappingTradesTheTwoBigAreasAndMovesNothingElse()
    {
        var metrics = Screen(1600, 900);
        var normal = WFCockpitPanelLayout.Compute(metrics, false, false);
        var swapped = WFCockpitPanelLayout.Compute(metrics, true, false);
        Assert.Multiple(() =>
        {
            Assert.That(swapped.WorldShown, Is.True);
            AssertBox(swapped.Mfd, normal.World);
            AssertBox(swapped.World, normal.Mfd);
            AssertBox(swapped.Motion, normal.Motion);
            AssertBox(swapped.Row, normal.Row);
        });
    }

    [Test]
    public void MaximisingHidesTheWorldAndGivesTheMfdItsSpace()
    {
        var metrics = Screen(1920, 1080, strip: 128);
        var normal = WFCockpitPanelLayout.Compute(metrics, false, false);
        var frame = WFCockpitPanelLayout.Compute(metrics, false, true);
        Assert.Multiple(() =>
        {
            Assert.That(frame.WorldShown, Is.False);
            Assert.That(frame.Mfd.Left, Is.EqualTo(normal.World.Left).Within(0.01f));
            Assert.That(frame.Mfd.Top, Is.EqualTo(normal.World.Top).Within(0.01f));
            Assert.That(frame.Mfd.Bottom, Is.EqualTo(normal.World.Bottom).Within(0.01f));
            Assert.That(frame.Mfd.Width, Is.GreaterThan(normal.World.Width + normal.Mfd.Width / 2));
            Assert.That(frame.Alerts.Left, Is.EqualTo(frame.Mfd.Right + WFCockpitPanelLayout.Gap).Within(0.01f));
            Assert.That(frame.Alerts.Width, Is.EqualTo(128));
            Assert.That(frame.Alerts.Right, Is.EqualTo(1920 - WFCockpitPanelLayout.Margin).Within(0.01f));
            Assert.That(frame.Row.Right, Is.EqualTo(1920 - WFCockpitPanelLayout.Margin).Within(0.01f));
            Assert.That(frame.Row.Left, Is.EqualTo(normal.Row.Left));
            Assert.That(frame.Motion.Width, Is.EqualTo(frame.Mfd.Width).Within(0.01f));
        });
    }

    [Test]
    public void MaximisingIgnoresWhetherThePanelsWereSwapped()
    {
        var metrics = Screen(1600, 900, 128);
        var plain = WFCockpitPanelLayout.Compute(metrics, false, true);
        var swapped = WFCockpitPanelLayout.Compute(metrics, true, true);
        AssertBox(swapped.Mfd, plain.Mfd);
        AssertBox(swapped.Alerts, plain.Alerts);
        AssertBox(swapped.Row, plain.Row);
    }

    [Test]
    public void VotesDockAboveAMaximisedMfdAndTakeItsHeight()
    {
        var without = WFCockpitPanelLayout.Compute(Screen(1600, 900, 128), false, true);
        var with = WFCockpitPanelLayout.Compute(Screen(1600, 900, 128, 100), false, true);
        Assert.Multiple(() =>
        {
            Assert.That(with.Votes.Height, Is.EqualTo(100));
            Assert.That(with.Votes.Top, Is.EqualTo(without.Mfd.Top).Within(0.01f));
            Assert.That(with.Mfd.Top, Is.EqualTo(with.Votes.Bottom + WFCockpitPanelLayout.Gap).Within(0.01f));
            Assert.That(with.Mfd.Bottom, Is.EqualTo(without.Mfd.Bottom).Within(0.01f));
            Assert.That(Overlap(with.Votes, with.Mfd), Is.False);
        });
    }

    private static float LeftColumn(float width) => Math.Clamp(width * 0.21f, 220, 320);

    /// <summary>The screen as the view measures it, with the side column at its normal or its expanded width.</summary>
    private static WFCockpitPanelLayout.Metrics Fitted(float width, float height, bool expanded, float strip = 0, float votes = 0)
    {
        var size = new Vector2(width, height);
        var left = LeftColumn(width);
        return new WFCockpitPanelLayout.Metrics(size, 8, left, WFCockpitPanelLayout.SideWidth(size, left, expanded),
            WFCockpitPanelLayout.RowHeight(size), 44, strip, votes);
    }

    [TestCase(636, 0)]
    [TestCase(720, 0)]
    [TestCase(900, 21.6f)]
    [TestCase(1080, 40)]
    [TestCase(1440, 40)]
    public void TheLowerRowOnlyGrowsWhereTheScreenCanSpareIt(float height, float extra)
    {
        var size = new Vector2(1920, height);
        Assert.That(WFCockpitPanelLayout.RowHeight(size) - WFCockpitPanelLayout.FlightHeight(size), Is.EqualTo(extra).Within(0.01f));
    }

    [Test]
    public void ExpandingGivesTheWorldViewAndTheMfdHalfTheWidthEach([Values(1600, 1920, 2560)] float width)
    {
        var normal = WFCockpitPanelLayout.Compute(Fitted(width, 1080, false), false, false);
        var expanded = WFCockpitPanelLayout.Compute(Fitted(width, 1080, true), false, false);
        Assert.Multiple(() =>
        {
            Assert.That(expanded.WorldShown, Is.True);
            Assert.That(expanded.Mfd.Width, Is.EqualTo(expanded.World.Width).Within(0.01f));
            Assert.That(expanded.Mfd.Width, Is.GreaterThan(normal.Mfd.Width));
            Assert.That(expanded.World.Width, Is.LessThan(normal.World.Width));
            Assert.That(expanded.World.Left, Is.EqualTo(normal.World.Left));
            Assert.That(expanded.Mfd.Right, Is.EqualTo(width - WFCockpitPanelLayout.Margin).Within(0.01f));
            Assert.That(expanded.Motion.Width, Is.EqualTo(expanded.World.Width).Within(0.01f));
            Assert.That(expanded.Row.Width, Is.EqualTo(expanded.World.Width).Within(0.01f));
            Assert.That(Overlap(expanded.World, expanded.Mfd), Is.False);
        });
    }

    [Test]
    public void ExpandingGrowsWhicheverPanelIsInTheSideColumnWhenSwapped([Values(1600, 1920, 2560)] float width)
    {
        var normal = WFCockpitPanelLayout.Compute(Fitted(width, 1080, false), true, false);
        var expanded = WFCockpitPanelLayout.Compute(Fitted(width, 1080, true), true, false);
        Assert.Multiple(() =>
        {
            Assert.That(expanded.WorldShown, Is.True);
            Assert.That(expanded.World.Right, Is.EqualTo(width - WFCockpitPanelLayout.Margin).Within(0.01f), "The world view stays in the side column.");
            Assert.That(expanded.World.Width, Is.EqualTo(expanded.Mfd.Width).Within(0.01f));
            Assert.That(expanded.World.Width, Is.GreaterThan(normal.World.Width));
            Assert.That(expanded.Mfd.Width, Is.LessThan(normal.Mfd.Width));
            Assert.That(expanded.Mfd.Left, Is.EqualTo(normal.Mfd.Left));
            Assert.That(expanded.Mfd.Left, Is.EqualTo(expanded.Motion.Left));
            Assert.That(Overlap(expanded.World, expanded.Mfd), Is.False);
        });
    }

    [Test]
    public void ExpandingIsTheSameSplitWhicheverWayRound([Values(1130, 1600, 1920)] float width)
    {
        var expanded = Fitted(width, 1080, true);
        var plain = WFCockpitPanelLayout.Compute(expanded, false, false);
        var swapped = WFCockpitPanelLayout.Compute(expanded, true, false);
        Assert.Multiple(() =>
        {
            AssertBox(swapped.Mfd, plain.World);
            AssertBox(swapped.World, plain.Mfd);
            AssertBox(swapped.Motion, plain.Motion);
            AssertBox(swapped.Row, plain.Row);
        });
    }

    [Test]
    public void ExpandingOnASmallWindowStillLeavesTheCentreColumnItsRoom()
    {
        const float width = 1130;
        var size = new Vector2(width, 636);
        Assert.That(WFCockpitPanelLayout.CanExpand(size, LeftColumn(width)), Is.True);
        var normal = WFCockpitPanelLayout.Compute(Fitted(width, 636, false), false, false);
        var expanded = WFCockpitPanelLayout.Compute(Fitted(width, 636, true), false, false);
        var swapped = WFCockpitPanelLayout.Compute(Fitted(width, 636, true), true, false);
        Assert.Multiple(() =>
        {
            Assert.That(expanded.Mfd.Width, Is.GreaterThan(normal.Mfd.Width + 1));
            Assert.That(expanded.World.Width, Is.GreaterThanOrEqualTo(500 - 0.01f));
            Assert.That(swapped.Mfd.Width, Is.GreaterThanOrEqualTo(500 - 0.01f));
            Assert.That(expanded.Mfd.Right, Is.LessThanOrEqualTo(width - WFCockpitPanelLayout.Margin + 0.01f));
        });
    }

    [Test]
    public void ExpandingChangesNothingOnAWindowTooNarrowForIt([Values(1000, 1090, 1097)] float width)
    {
        var size = new Vector2(width, 636);
        var left = LeftColumn(width);
        Assert.Multiple(() =>
        {
            Assert.That(WFCockpitPanelLayout.CanExpand(size, left), Is.False);
            Assert.That(WFCockpitPanelLayout.SideWidth(size, left, true), Is.EqualTo(WFCockpitPanelLayout.SideWidth(size, left, false)));
        });
        foreach (var swapped in new[] { false, true })
        {
            var normal = WFCockpitPanelLayout.Compute(Fitted(width, 636, false), swapped, false);
            var expanded = WFCockpitPanelLayout.Compute(Fitted(width, 636, true), swapped, false);
            AssertBox(expanded.Mfd, normal.Mfd);
            AssertBox(expanded.World, normal.World);
        }
    }

    [Test]
    public void ExpandingNeverNarrowsTheSideColumn([Values(800, 1000, 1130, 1280, 1600, 1920, 2560, 3840)] float width,
        [Values] bool gunnery)
    {
        var size = new Vector2(width, 900);
        // The gunnery bank widens the left column.
        var left = gunnery ? Math.Clamp(width * 0.27f, 320, 420) : LeftColumn(width);
        var normal = WFCockpitPanelLayout.SideWidth(size, left, false);
        var expanded = WFCockpitPanelLayout.SideWidth(size, left, true);
        var centre = WFCockpitPanelLayout.Center(size, left, normal);
        Assert.Multiple(() =>
        {
            Assert.That(expanded, Is.GreaterThanOrEqualTo(normal));
            Assert.That(WFCockpitPanelLayout.CanExpand(size, left), Is.EqualTo(expanded - normal >= 1));
            Assert.That(WFCockpitPanelLayout.Center(size, left, expanded), Is.GreaterThanOrEqualTo(Math.Min(centre, 500) - 0.01f),
                "Growing the side column never takes the centre below its minimum room.");
        });
    }

    [Test]
    public void MaximisingIgnoresWhetherTheMfdWasExpanded()
    {
        var plain = WFCockpitPanelLayout.Compute(Fitted(1920, 1080, false, 128, 90), false, true);
        var expanded = WFCockpitPanelLayout.Compute(Fitted(1920, 1080, true, 128, 90), false, true);
        var swapped = WFCockpitPanelLayout.Compute(Fitted(1920, 1080, true, 128, 90), true, true);
        Assert.Multiple(() =>
        {
            Assert.That(expanded.WorldShown, Is.False);
            AssertBox(expanded.Mfd, plain.Mfd);
            AssertBox(expanded.Alerts, plain.Alerts);
            AssertBox(expanded.Votes, plain.Votes);
            AssertBox(expanded.Motion, plain.Motion);
            AssertBox(expanded.Row, plain.Row);
            AssertBox(swapped.Mfd, plain.Mfd);
        });
    }

    [Test]
    public void NoArrangementOverlapsAPanelOrLeavesTheScreen([Values(1090, 1130, 1600, 1920, 2560)] float width,
        [Values(617, 636, 900, 1080, 1440)] float height, [Values] bool swapped, [Values] bool maximised, [Values] bool expanded)
    {
        var metrics = Fitted(width, height, expanded && !maximised, maximised ? 128 : 0, maximised ? 90 : 0);
        var frame = WFCockpitPanelLayout.Compute(metrics, swapped, maximised);
        var panels = new List<(string Name, UIBox2 Box)> { ("motion", frame.Motion), ("mfd", frame.Mfd), ("row", frame.Row) };
        if (frame.WorldShown)
            panels.Add(("world", frame.World));
        else
        {
            panels.Add(("votes", frame.Votes));
            panels.Add(("alerts", frame.Alerts));
        }
        foreach (var (name, box) in panels)
        {
            Assert.That(box.Left, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(box.Top, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(box.Right, Is.LessThanOrEqualTo(width), name);
            Assert.That(box.Bottom, Is.LessThanOrEqualTo(height), name);
        }
        for (var i = 0; i < panels.Count; i++)
        for (var j = i + 1; j < panels.Count; j++)
        {
            // An empty vote row has no height and so cannot overlap anything.
            if (panels[i].Box.Height == 0 || panels[j].Box.Height == 0)
                continue;
            Assert.That(Overlap(panels[i].Box, panels[j].Box), Is.False, $"{panels[i].Name} and {panels[j].Name} overlap");
        }
    }

    [Test]
    public void ShieldsTakeOnlyWhatTheirControlsNeedAndCommunicationsKeepTheRest()
    {
        var (comms, shields) = WFCockpitPanelLayout.SplitRow(1000, 300);
        Assert.That(shields, Is.EqualTo(300));
        Assert.That(comms, Is.EqualTo(1000 - WFCockpitPanelLayout.Gap - 300));
        Assert.That(comms, Is.GreaterThan(shields));
    }

    [Test]
    public void ShieldsNeverTakeMoreThanTheirOldShareOfANarrowRow()
    {
        var (comms, shields) = WFCockpitPanelLayout.SplitRow(520, 400);
        var usable = 520 - WFCockpitPanelLayout.Gap;
        Assert.That(shields, Is.EqualTo(usable * 0.57f).Within(0.01f));
        Assert.That(comms, Is.EqualTo(usable * 0.43f).Within(0.01f));
    }
}
