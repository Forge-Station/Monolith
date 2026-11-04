#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Two huge blasts on a world's preloaded ground leave no station plating or lattice: natural ground stays ground or
/// opens into the caverns, a site's mapped floor gives the ground back, and a player's floor breaks the station way
/// down to the ground it was built on, never through it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetTileBreakSystem))]
public sealed class PlanetExplosionGroundTest
{
    /// <summary>Small enough to preload in seconds.</summary>
    private const int Radius = 64;

    /// <summary>How far round the epicentre the patches lie and nothing shields the floor.</summary>
    private const int Reach = 4;

    /// <summary>How far the blast reaches; nothing out to here may show plating or lattice.</summary>
    private const int BlastReach = 14;

    /// <summary>Floors a site's map lays on the ground, plating among them.</summary>
    private static readonly string[] Mapped = { "Plating", "FloorSteel", "N14FloorConcrete" };

    private static readonly string[] Subfloors = { "Plating", "Lattice" };

    [Test]
    [TestCase("WFSurfaceThrascias")]
    [TestCase("WFSurfaceAsclepiu")]
    [TestCase("WFSurfaceAerumna")]
    [TestCase("WFSurfaceCarcinoma")]
    public async Task BlastsLeaveTheGround(string surfaceId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.FixedCities, false);
            server.CfgMan.SetCVar(CavernCVars.Caverns, true);
        });

        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(surfaceId);
            var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surfaceId.Replace("WFSurface", string.Empty), null);
            Assert.That(built, Is.Not.Null, $"The {surfaceId} network failed to build.");
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers);
        });
        var ground = layers[0];

        try
        {
            await PlanetBoundsTest.WaitPreloaded(pair, ground);

            var spot = Vector2i.Zero;
            var natural = new Dictionary<Vector2i, Tile>();
            var site = new HashSet<Vector2i>();
            var players = new HashSet<Vector2i>();
            string Id(Tile tile) => tile.IsEmpty ? "empty" : tileDefs[tile.TypeId].ID;

            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                var biome = entMan.GetComponent<BiomeComponent>(ground);
                Tile Natural(Vector2i index)
                {
                    Assert.That(biomes.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile), Is.True);
                    return tile!.Value;
                }

                // Untouched ground clear of the gate and the sites: every tile the blast reaches as the biome laid it.
                var found = false;
                for (var x = 20; x < 60 && !found; x += 4)
                for (var y = -40; y < 40 && !found; y += 4)
                {
                    var candidate = new Vector2i(x, y);
                    if (Square(candidate, BlastReach).All(index => maps.GetTileRef(ground, grid, index).Tile == Natural(index)))
                    {
                        spot = candidate;
                        found = true;
                    }
                }

                Assert.That(found, Is.True, $"Precondition: no untouched ground on {surfaceId} to blast.");

                foreach (var index in Square(spot, BlastReach))
                {
                    natural[index] = Natural(index);
                }

                // Nothing standing near the epicentre shields its floor from the blast.
                foreach (var index in Square(spot, Reach))
                {
                    foreach (var uid in maps.GetAnchoredEntities(ground, grid, index).ToList())
                    {
                        entMan.DeleteEntity(uid);
                    }
                }

                // A site's floors east of the epicentre, pinned as a site's are; a player's floor west of it.
                var mapped = new List<(Vector2i, Tile)>();
                var laid = new List<(Vector2i, Tile)>();
                var underlay = entMan.EnsureComponent<WFPlanetBuiltTilesComponent>(ground).Underlay;
                for (var dx = 1; dx <= 3; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    var east = spot + new Vector2i(dx, dy);
                    site.Add(east);
                    mapped.Add((east, new Tile(tileDefs[Mapped[(dx + dy + 3) % Mapped.Length]].TileId)));

                    var west = spot + new Vector2i(-dx, dy);
                    players.Add(west);
                    underlay[west] = natural[west];
                    laid.Add((west, new Tile(tileDefs["FloorSteel"].TileId)));
                }

                maps.SetTiles(ground, grid, mapped);
                maps.SetTiles(ground, grid, laid);
                biomes.WfPinTiles((ground, biome), site.ToList());
            });
            await server.WaitRunTicks(2);

            // Two blasts, so the second breaks what the first left.
            for (var blast = 0; blast < 2; blast++)
            {
                await server.WaitPost(() =>
                {
                    var mapId = entMan.GetComponent<MapComponent>(ground).MapId;
                    server.System<ExplosionSystem>().QueueExplosion(new MapCoordinates(TileCentre(spot), mapId), "Default", 6000f, 4f, 130f, null);
                });
                await server.WaitRunTicks(pair.SecondsToTicks(5f));

                if (blast > 0)
                    continue;

                // The player's floor after the first blast: once it gives the ground back, that ground is the world's.
                await server.WaitAssertion(() =>
                {
                    var grid = entMan.GetComponent<MapGridComponent>(ground);
                    var broken = 0;

                    using (Assert.EnterMultipleScope())
                    {
                        foreach (var index in players)
                        {
                            var now = maps.GetTileRef(ground, grid, index).Tile;
                            var id = Id(now);
                            if (id != "FloorSteel")
                                broken++;

                            Assert.That(now.IsEmpty, Is.False, $"{surfaceId}: the blast went through a player's floor at {index}.");
                            Assert.That(id == "FloorSteel" || Subfloors.Contains(id) || now == natural[index], Is.True,
                                $"{surfaceId}: a player's floor at {index} broke to {id}, not the station way or the ground under it.");
                        }

                        Assert.That(broken, Is.GreaterThan(0), $"Precondition: the blast on {surfaceId} broke none of the player's floor.");
                    }
                });
            }

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                var opened = 0;

                using (Assert.EnterMultipleScope())
                {
                    foreach (var (index, was) in natural)
                    {
                        var now = maps.GetTileRef(ground, grid, index).Tile;
                        var id = Id(now);

                        // What is left of a player's floor still breaks the station way.
                        if (players.Contains(index) && (id == "FloorSteel" || Subfloors.Contains(id)))
                            continue;

                        Assert.That(Subfloors, Does.Not.Contain(id), $"{surfaceId}: the blast left {id} on the ground at {index}.");

                        if (now.IsEmpty)
                        {
                            // Only ground a blast may break opens; a floor gives the ground back, which the second blast may open.
                            opened++;
                            var through = site.Contains(index) ? "a site's floor over " : string.Empty;
                            Assert.That(((ContentTileDefinition) tileDefs[was.TypeId]).Indestructible, Is.False,
                                $"{surfaceId}: the blast opened a hole at {index} through {through}indestructible {Id(was)}.");
                            continue;
                        }

                        Assert.That(now, Is.EqualTo(was), $"{surfaceId}: {Id(was)} at {index} is {id} after the blasts.");
                    }
                }

                TestContext.Out.WriteLine($"{surfaceId}: the blasts opened {opened} holes in the ground.");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Vector2i> Square(Vector2i centre, int reach)
    {
        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            yield return centre + new Vector2i(x, y);
        }
    }
}
