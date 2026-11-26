#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A grid built on a planet's air layers breathes the planet's air, sealed rooms included; one built on orbit doesn't.
/// </summary>
[TestFixture]
public sealed class AirLayerAtmosphereTest
{
    private const string Wall = "WallSolid";

    private const string Mob = "MobHuman";

    /// <summary>A walled room and an open deck on a held-up grid, on the lowest air layer, the top one and orbit.</summary>
    [Test]
    public async Task GridsAboveTheGroundHaveThePlanetsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        // The climate sets the world's outdoor air on its first pass; a room sealed before then keeps the old air.
        await server.WaitRunTicks(pair.SecondsToTicks(2.5f));
        var orbit = layers[^1];
        var room = Area(new Vector2i(1, 1), new Vector2i(3, 3)).ToList();
        var deck = Area(new Vector2i(6, 0), new Vector2i(8, 4)).ToList();
        var grids = new Dictionary<EntityUid, EntityUid>();
        var mobs = new Dictionary<EntityUid, EntityUid>();

        await server.WaitPost(() =>
        {
            foreach (var layer in new[] { layers[1], layers[^2], orbit })
            {
                var grid = BuildBlock(pair, layer, room, deck);
                grids[layer] = grid;
                mobs[layer] = entMan.SpawnEntity(Mob, new EntityCoordinates(grid, 2.5f, 2.5f));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            foreach (var (layer, grid) in grids)
            {
                var depth = layers.IndexOf(layer);
                Assert.That(entMan.HasComponent<GridAtmosphereComponent>(grid), Is.True, $"The grid on layer {depth} never got an atmosphere.");
                var mapAir = atmos.GetTileMixture(null, layer, Vector2i.Zero)!;

                if (layer == orbit)
                {
                    Assert.That(mapAir.Pressure, Is.LessThan(1f), "Orbit has air.");
                    Assert.That(room.Max(index => atmos.GetTileMixture(grid, layer, index)?.Pressure ?? 0f), Is.LessThan(1f),
                        "A room built on orbit was filled with air.");
                    continue;
                }

                Assert.That(mapAir.Pressure, Is.GreaterThan(Atmospherics.OneAtmosphere * 0.9f), $"Air layer {depth} has no air of its own.");
                foreach (var index in room.Concat(deck))
                {
                    var air = atmos.GetTileMixture(grid, layer, index);
                    Assert.That(air?.Pressure ?? 0f, Is.EqualTo(mapAir.Pressure).Within(mapAir.Pressure * 0.05f),
                        $"Tile {index} of the grid on air layer {depth} is at {air?.Pressure ?? 0f} kPa, not the planet's {mapAir.Pressure}.");
                }

                var mob = mobs[layer];
                Assert.That(entMan.GetComponent<BarotraumaComponent>(mob).TakingDamage, Is.False, $"The mob on air layer {depth} takes pressure damage.");
                Assert.That(entMan.GetComponent<RespiratorComponent>(mob).SuffocationCycles, Is.Zero, $"The mob on air layer {depth} suffocates.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A static, held-up grid with a walled room and an open deck beside it.</summary>
    private static EntityUid BuildBlock(TestPair pair, EntityUid layer, List<Vector2i> room, List<Vector2i> deck)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(entMan.GetComponent<MapComponent>(layer).MapId);
        entMan.EnsureComponent<CEZMappingAnchorGridComponent>(grid);
        server.System<SharedPhysicsSystem>().SetBodyType(grid, BodyType.Static);
        server.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, new Vector2(20f, 20f));

        var floor = new Tile(server.ResolveDependency<ITileDefinitionManager>()[FloorTile].TileId);
        var block = Area(Vector2i.Zero, new Vector2i(4, 4)).ToList();
        maps.SetTiles(grid.Owner, grid.Comp, block.Concat(deck).Select(index => (index, floor)).ToList());

        foreach (var index in block.Except(room))
        {
            entMan.SpawnEntity(Wall, new EntityCoordinates(grid, index.X + 0.5f, index.Y + 0.5f));
        }

        return grid;
    }

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }
}
