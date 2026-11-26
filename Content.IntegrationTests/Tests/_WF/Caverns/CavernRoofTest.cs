#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The ground roofs the cavern below it tile by tile, and a hole in the ground unroofs it.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSystem))]
public sealed class CavernRoofTest
{
    /// <summary>A patch of ground well away from the planet centre and anything the build pins.</summary>
    private static readonly Vector2i From = new(200, 200);
    private static readonly Vector2i To = new(202, 202);

    /// <summary>The tile in the patch that gets dug out again.</summary>
    private static readonly Vector2i Hole = new(201, 201);

    /// <summary>A tile just outside the patch, which nothing roofs.</summary>
    private static readonly Vector2i Outside = new(204, 201);

    /// <summary>Tiles laid on the ground roof those cavern tiles; emptying one unroofs it and leaves the rest.</summary>
    [Test]
    public async Task GroundTilesRoofCavern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var roofs = server.System<SharedRoofSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        await server.WaitAssertion(() =>
            Assert.That(IsRooved(entMan, roofs, world.Cavern, Hole), Is.False,
                "Precondition: the cavern is already roofed before any ground is laid."));

        await PlanetFixture.LayTiles(pair, world.Ground, From, To);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var index in Patch())
                {
                    Assert.That(IsRooved(entMan, roofs, world.Cavern, index), Is.True,
                        $"Cavern tile {index} is open to the sky under laid ground.");
                }

                Assert.That(IsRooved(entMan, roofs, world.Cavern, Outside), Is.False,
                    "A cavern tile with no ground above it is roofed.");
            }
        });

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
            maps.SetTile(world.Ground, grid, Hole, Tile.Empty);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsRooved(entMan, roofs, world.Cavern, Hole), Is.False,
                    "The cavern stayed roofed under a hole in the ground.");

                foreach (var index in Patch())
                {
                    if (index == Hole)
                        continue;

                    Assert.That(IsRooved(entMan, roofs, world.Cavern, index), Is.True,
                        $"Digging one hole unroofed cavern tile {index} as well.");
                }
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Every tile index in the laid patch.</summary>
    private static IEnumerable<Vector2i> Patch()
    {
        for (var x = From.X; x <= To.X; x++)
        for (var y = From.Y; y <= To.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    /// <summary>Whether a cavern tile is roofed, read through the roof API.</summary>
    private static bool IsRooved(IEntityManager entMan, SharedRoofSystem roofs, EntityUid cavern, Vector2i index)
    {
        var grid = entMan.GetComponent<MapGridComponent>(cavern);
        var roof = entMan.GetComponent<RoofComponent>(cavern);
        return roofs.IsRooved((cavern, grid, roof), index);
    }
}
