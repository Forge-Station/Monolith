using System;
using System.Numerics;
using Content.Client._WF.Caverns;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using NUnit.Framework;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Caverns;

/// <summary>When the z-level renderer draws the cavern under a ground layer, and at what depth.</summary>
[TestFixture]
[TestOf(typeof(WFCavernViewSystem))]
public sealed class CavernPassTest
{
    /// <summary>To an observer on the ground, standing or jumping, with the cavern known and a mouth in view, it is drawn one level below the ground.</summary>
    [TestCase(0f, -1f)]
    [TestCase(-0.25f, -1.25f)]
    public void CavernDrawsOneLevelUnderTheGround(float groundDepth, float expected)
    {
        Assert.That(WFCavernViewSystem.CavernPassDepth(groundDepth, true, true, true), Is.EqualTo(expected));
    }

    /// <summary>Without the cavern's map, or with no mouth in view, the ground stays the floor of the view.</summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void GroundStaysTheFloorOtherwise(bool known, bool mouthInView)
    {
        Assert.That(WFCavernViewSystem.CavernPassDepth(0f, known, mouthInView, true), Is.Null);
    }

    /// <summary>Seen from the air or orbit, a mouth in view draws no cavern: the hole stays dark.</summary>
    [TestCase(-1f)]
    [TestCase(-4f)]
    public void NoCavernFromAbove(float groundDepth)
    {
        Assert.That(WFCavernViewSystem.CavernPassDepth(groundDepth, true, true, false), Is.Null);
    }

    /// <summary>
    /// The box searched for holes is what the renderer's pass eye shows, at any altitude or turn, with the renderer's
    /// own per-level scale (1 since Monolith stopped drawing lower levels smaller) and with the 0.85 it had before.
    /// </summary>
    [TestCase(-1f, 0f, 0.0, CESharedZLevelsSystem.ZLevelViewShrink)]
    [TestCase(-1.9f, -0.9f, 0.0, CESharedZLevelsSystem.ZLevelViewShrink)]
    [TestCase(-3.3f, -0.3f, 0.0, CESharedZLevelsSystem.ZLevelViewShrink)]
    [TestCase(-1.6f, -0.6f, 1.2, CESharedZLevelsSystem.ZLevelViewShrink)]
    [TestCase(-1f, 0f, 0.0, 0.85f)]
    [TestCase(-3.3f, -0.3f, 0.0, 0.85f)]
    [TestCase(-1.6f, -0.6f, 1.2, 0.85f)]
    public void LevelViewMatchesThePassEye(float depth, float ownDepth, double turn, float shrink)
    {
        var rotation = new Angle(turn);
        var observer = new Eye
        {
            Position = new MapCoordinates(new Vector2(12.3f, -40.7f), MapId.Nullspace),
            Offset = new Vector2(0.3f, -0.2f),
            Rotation = rotation,
            Scale = new Vector2(0.5f),
        };

        // As ScalingViewport builds a pass eye at this depth.
        Angle turned = rotation * -1;
        var pass = new Eye
        {
            Position = observer.Position,
            Offset = observer.Offset + turned.ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset * (depth - ownDepth),
            Rotation = rotation,
            Scale = observer.Scale * MathF.Pow(shrink, -depth),
        };

        var box = WFCavernViewSystem.LevelViewBox(Shows(observer), rotation, depth, ownDepth, shrink);
        var expected = Shows(pass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(box.Left, Is.EqualTo(expected.Left).Within(0.001f), "Left edge.");
            Assert.That(box.Right, Is.EqualTo(expected.Right).Within(0.001f), "Right edge.");
            Assert.That(box.Bottom, Is.EqualTo(expected.Bottom).Within(0.001f), "Bottom edge.");
            Assert.That(box.Top, Is.EqualTo(expected.Top).Within(0.001f), "Top edge.");
        }
    }

    /// <summary>The world box round a 20 by 14 screen seen through an eye.</summary>
    private static Box2 Shows(Eye eye)
    {
        eye.GetViewMatrixInv(out var inverse, Vector2.One);
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        var corners = new[] { new Vector2(-10, -7), new Vector2(10, -7), new Vector2(-10, 7), new Vector2(10, 7) };

        foreach (var corner in corners)
        {
            var world = Vector2.Transform(corner, inverse);
            min = Vector2.Min(min, world);
            max = Vector2.Max(max, world);
        }

        return new Box2(min, max);
    }
}
