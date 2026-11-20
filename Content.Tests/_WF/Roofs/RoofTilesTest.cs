using System.Collections.Generic;
using Content.Client._WF.Roofs;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Roofs;

/// <summary>A roof seen from above covers a vessel's enclosed rooms and a building's open floor, never its walls or doors.</summary>
[TestFixture]
[TestOf(typeof(WFRoofTiles))]
public sealed class RoofTilesTest
{
    // A ship deck 0-10 by 0-6: walls round a room at 2-8 by 1-5 with a door at (5, 1), open plating outside the walls.
    private static HashSet<Vector2i> Deck()
    {
        var floor = new HashSet<Vector2i>();
        for (var x = 0; x <= 10; x++)
        for (var y = 0; y <= 6; y++)
            floor.Add(new Vector2i(x, y));
        return floor;
    }

    private static bool RoomWall(Vector2i tile) =>
        tile.X is >= 2 and <= 8 && tile.Y is >= 1 and <= 5 && (tile.X is 2 or 8 || tile.Y is 1 or 5);

    [Test]
    public void OnlyTheEnclosedRoomIsRoofed()
    {
        var roofed = new HashSet<Vector2i>();
        WFRoofTiles.Enclosed(Deck(), _ => false, RoomWall, roofed);

        Assert.That(roofed, Does.Contain(new Vector2i(5, 3)), "The room inside the walls is roofed.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(2, 3)), "Its walls stay uncovered, to be seen from outside.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(5, 1)), "...and so does the door in them.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(0, 0)), "Open plating outside is left bare.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(9, 3)), "...even right against the wall.");
    }

    [Test]
    public void ABreachOpensTheRoomToTheSky()
    {
        var roofed = new HashSet<Vector2i>();
        WFRoofTiles.Enclosed(Deck(), _ => false, tile => RoomWall(tile) && tile != new Vector2i(8, 3), roofed);
        Assert.That(roofed, Does.Not.Contain(new Vector2i(5, 3)), "A hole in the wall leaves the room open.");
    }

    [Test]
    public void OnlyOpenFloorIsCovered()
    {
        Assert.That(WFRoofTiles.Covers(roofed: true, structure: false), Is.True, "Roofed floor is covered.");
        Assert.That(WFRoofTiles.Covers(roofed: true, structure: true), Is.False,
            "A wall, window or door under the roof is never covered.");
        Assert.That(WFRoofTiles.Covers(roofed: false, structure: false), Is.False, "Open ground has no roof.");
    }

    [Test]
    public void LatticeCountsAsOutside()
    {
        var floor = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) };
        var roofed = new HashSet<Vector2i>();
        // Fully surrounded by more deck in a ring, but the tile is lattice.
        WFRoofTiles.Enclosed(floor, tile => tile == new Vector2i(1, 0), _ => false, roofed);
        Assert.That(roofed, Is.Empty);
    }
}
