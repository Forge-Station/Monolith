#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>What a cavern viewer standing on the gate pad actually loads.</summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class CavernGenerationTest
{
    /// <summary>Entities one cavern viewer may cause (risk 2); F3 measured 2,700-3,600 over the 81 chunks a viewer loads.</summary>
    private const int MaxEntities = 4000;

    /// <summary>Lights one cavern viewer may load: enough glow to find your way, not a light on every tile.</summary>
    private const int MinLights = 5;
    private const int MaxLights = 100;

    /// <summary>How far each way from the viewer the loaded floors are checked; chunks load at least this far.</summary>
    private const int LoadReach = 24;

    /// <summary>Edge of the square searched for a fauna marker; a viewer loads 81 chunks, 72 tiles across.</summary>
    private const int MarkerSearch = 72;

    /// <summary>How far from a fauna site the wildlife pass stands: past the 24-tile no-spawn ring, inside the 64-tile reach.</summary>
    private const float SiteDistance = 30f;

    /// <summary>Ticks the viewer stands still before the load is checked.</summary>
    private const int SettleTicks = 90;

    /// <summary>A viewer on each gate pad: the pad stays clear, the world's own floors load around it, the load and its lights stay bounded and the world's wildlife spawns.</summary>
    [Test]
    public async Task PadClearAndWorldFloors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var sampler = server.System<WFCavernSampler>();
        var ecology = server.System<WFPlanetFaunaSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        await DisableClaims(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var cavern = CavernOf(pair, surfaceId);
                var gate = await Gate(pair, world);
                var viewer = await PlanetFixture.AttachViewer(pair, world.Cavern, TileCentre(gate.ClimbTile));
                await pair.RunTicksSync(SettleTicks);

                var floors = new HashSet<string>();
                var entities = 0;
                var lights = 0;
                var loadedTiles = 0;

                await server.WaitAssertion(() =>
                {
                    var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                    var biomeComp = entMan.GetComponent<BiomeComponent>(world.Cavern);
                    var biome = (world.Cavern, biomeComp);
                    var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                    var climb = ground.ClimbPoints[gate.ClimbTile];
                    var (substrate, chamber) = Floors[surfaceId];

                    for (var x = -LoadReach; x <= LoadReach; x++)
                    for (var y = -LoadReach; y <= LoadReach; y++)
                    {
                        if (maps.TryGetTileRef(world.Cavern, grid, gate.ClimbTile + new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty)
                            floors.Add(tileDefs[tile.Tile.TypeId].ID);
                    }

                    var query = entMan.AllEntityQueryEnumerator<TransformComponent>();
                    while (query.MoveNext(out var uid, out var xform))
                    {
                        if (xform.MapUid == world.Cavern && uid != world.Cavern)
                            entities++;
                    }

                    var lightQuery = entMan.AllEntityQueryEnumerator<PointLightComponent, TransformComponent>();
                    while (lightQuery.MoveNext(out _, out _, out var xform))
                    {
                        if (xform.MapUid == world.Cavern)
                            lights++;
                    }

                    loadedTiles = biomeComp.LoadedChunks.Count * ChunkSize * ChunkSize;

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Cavern),
                            $"Precondition: {surfaceId}'s viewer left the cavern.");
                        Assert.That(biomes.WfIsChunkLoaded(biome, gate.ClimbTile + new Vector2i(LoadReach, LoadReach)), Is.True,
                            $"Precondition: {surfaceId}'s viewer never loaded the cavern around it.");

                        foreach (var index in gate.Pad(cavern.Mouths.PadRadius))
                        {
                            foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, grid, index))
                            {
                                if (anchored != climb && !IsLandingEntity(entMan, anchored, cavern.Mouths))
                                    Assert.Fail($"{surfaceId}: {entMan.ToPrettyString(anchored)} stands on pad tile {index}.");
                            }
                        }

                        Assert.That(floors, Does.Contain(substrate), $"{surfaceId}: no {substrate} rock floor loaded around the pad.");
                        Assert.That(floors, Does.Contain(chamber), $"{surfaceId}: no {chamber} chamber floor loaded around the pad.");
                        Assert.That(entities, Is.LessThanOrEqualTo(MaxEntities),
                            $"{surfaceId}: {entities} entities loaded over {loadedTiles} tiles around one viewer.");
                        Assert.That(lights, Is.InRange(MinLights, MaxLights),
                            $"{surfaceId}: {lights} lights loaded over {loadedTiles} tiles around one viewer, outside {MinLights}-{MaxLights}.");
                    }
                });

                TestContext.Out.WriteLine($"{surfaceId}: {entities} entities and {lights} lights over {loadedTiles} loaded tiles; floors {string.Join(", ", floors)}.");

                // A wildlife pass from beside one of the world's fauna markers in the loaded area.
                var marker = $"WFCavernFauna{surfaceId["WFSurface".Length..]}";
                await server.WaitAssertion(() =>
                {
                    var biomeComp = entMan.GetComponent<BiomeComponent>(world.Cavern);
                    var biome = (world.Cavern, biomeComp);
                    var sample = sampler.Sample(biomeComp, gate.ClimbTile, MarkerSearch);
                    var site = Enumerable.Range(0, sample.Count)
                        .Where(i => sample.Entities[i] == marker)
                        .Select(sample.IndexOf)
                        .Where(index => biomes.WfIsChunkLoaded(biome, index))
                        .Select(index => (Vector2i?) index)
                        .FirstOrDefault();

                    Assert.That(site, Is.Not.Null, $"Precondition: no {marker} marker in the chunks {surfaceId}'s viewer loaded.");
                    ecology.RefreshPopulation(new[] { new EntityCoordinates(world.Cavern, TileCentre(site!.Value) + new Vector2(SiteDistance, 0)) });
                });

                await server.WaitRunTicks(1);

                var wildlife = new List<string>();
                await server.WaitAssertion(() =>
                {
                    var query = entMan.EntityQueryEnumerator<WFPlanetWildlifeComponent, TransformComponent, MetaDataComponent>();
                    while (query.MoveNext(out _, out var wild, out var xform, out var meta))
                    {
                        if (wild.Ground == world.Cavern && xform.MapUid == world.Cavern)
                            wildlife.Add(meta.EntityPrototype?.ID ?? meta.EntityName);
                    }

                    Assert.That(wildlife, Is.Not.Empty, $"{surfaceId}: no wildlife spawned from the cavern's {marker} markers.");
                });

                TestContext.Out.WriteLine($"{surfaceId}: wildlife {string.Join(", ", wildlife)}.");
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }
}
