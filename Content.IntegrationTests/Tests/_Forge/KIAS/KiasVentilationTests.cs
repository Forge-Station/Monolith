#pragma warning disable RA0002
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasVentilationTests
{
    [Test]
    public void RestoreRequiresSettledSealedSafeAirVolume()
    {
        var system = new AtmosphereSystem();
        var grid = new GridAtmosphereComponent();
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        air.SetMoles(Gas.Oxygen, 21);
        air.SetMoles(Gas.Nitrogen, 79);
        var room = new TileAtmosphere(default, Vector2i.Zero, air);
        var space = new TileAtmosphere(default, new Vector2i(1, 0), space: true);
        var tiles = grid.Tiles;
        tiles.Add(Vector2i.Zero, room);
        tiles.Add(space.GridIndices, space);
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.True);
        grid.InvalidatedCoords.Add(Vector2i.Zero);
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.False);
        grid.InvalidatedCoords.Clear();
        room.AdjacentBits = AtmosDirection.East;
        room.AdjacentTiles[AtmosDirection.East.ToIndex()] = space;
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.False, "A safe tile exposed to space is not a sealed room.");
        room.AdjacentBits = AtmosDirection.Invalid;
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.True, "A closed airtight boundary blocks the space connection.");
        room.AdjacentBits = AtmosDirection.East;
        room.AdjacentTiles[AtmosDirection.East.ToIndex()] = null;
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.False);
        var next = new TileAtmosphere(default, new Vector2i(1, 0), air.Clone());
        room.AdjacentTiles[AtmosDirection.East.ToIndex()] = next;
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero, limit: 1), Is.False);
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.True);
        next.Air!.Temperature = 400;
        Assert.That(system.IsSealedSafeRoom(grid, Vector2i.Zero), Is.False);
    }
}
