#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._WF.Dormancy;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.Planets.Natives;
using Content.Server._WF.PlanetVents;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Shared._DV.Planet;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetVents;

/// <summary>
/// Carcinoma breathes: its air is mostly carbon dioxide that only its own life can live in, and its vents exhale warm
/// carbon dioxide into their tile, out in the fields and never on a road or a site.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetVentSystem))]
public sealed class CarcinomaBreathTest
{
    private const string Surface = "WFSurfaceCarcinoma";
    private const string Vent = "WFCarcinomaVent";
    private const string Puff = "WFCarcinomaVentPuff";
    private const string Ground = "WFFloorFlesh";

    /// <summary>Every layer with a sky holds the same mostly carbon dioxide air, warm, at about one atmosphere.</summary>
    [Test]
    public async Task CarcinomaAirIsMostlyCarbonDioxide()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var layers = await Build(pair);

        try
        {
            await server.WaitAssertion(() =>
            {
                var network = entMan.GetComponent<WFPlanetNetworkComponent>(NetworkOf(entMan, layers[0]));
                var planet = server.ProtoMan.Index<PlanetPrototype>("WFCarcinomaSurface");
                var airs = new List<(string, GasMixture)> { ("the planet prototype", planet.Atmosphere) };
                foreach (var layer in network.Layers.Where(l => l != network.OrbitMap))
                    airs.Add(($"layer {layer}", MapAir(entMan, layer)));

                foreach (var (name, air) in airs)
                {
                    var pressure = air.Pressure;
                    Assert.That(air.GetMoles(Gas.CarbonDioxide) / air.TotalMoles, Is.GreaterThan(0.8f), $"{name} is not mostly carbon dioxide.");
                    Assert.That(pressure * air.GetMoles(Gas.Oxygen) / air.TotalMoles, Is.LessThan(16f), $"{name} has enough oxygen to breathe.");
                    Assert.That(pressure, Is.EqualTo(105f).Within(1f), $"{name} is not at about one atmosphere.");
                    Assert.That(air.Temperature, Is.EqualTo(310.15f).Within(5f), $"{name} lost its body warmth.");
                    Assert.That(WFCrewPlannerSystem.IsBreathable(air, pressure), Is.False, $"NPCs would go without internals in {name}.");
                }
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The world's wildlife and slimes live in the air, without breathing gear; a human chokes on it.</summary>
    [Test]
    public async Task NativesLiveInItAndOutsidersChoke()
    {
        // A player stands among them: NPC bodies far from every player don't breathe at all (_WF/Dormancy).
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var layers = await Build(pair);
        var ground = layers[0];
        var natives = new List<EntityUid>();
        var human = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() =>
            {
                var comp = entMan.GetComponent<WFPlanetNativesComponent>(ground);
                var ids = server.System<WFPlanetNativesSystem>().NativePrototypes(comp)
                    .Where(id => server.ProtoMan.Index<EntityPrototype>(id).Components.ContainsKey("MobState"))
                    .OrderBy(id => id)
                    .ToList();
                Assert.That(ids, Has.Count.GreaterThan(10), "Carcinoma names too few natives.");

                var spots = new List<Vector2i>();
                for (var i = 0; i <= ids.Count; i++)
                    spots.Add(new Vector2i(i % 6 * 6, i / 6 * 6));
                SetTiles(pair, ground, Ground, spots.Append(new Vector2i(15, -8)));

                for (var i = 0; i < ids.Count; i++)
                    natives.Add(SpawnStill(entMan, ids[i], ground, spots[i]));

                // A wild Carcinoma slime: a lab's slime prototype, told apart by its breed.
                var slimeAt = spots[ids.Count];
                entMan.SpawnEntity("WFMobSlimeBabyCarcinoma", new EntityCoordinates(ground, slimeAt.X + 0.5f, slimeAt.Y + 0.5f));
            });

            // The player is the outsider: a human with a mind, breathing the open ground's air.
            human = await AttachViewer(pair, ground, new Vector2(15.5f, -7.5f));
            await server.WaitPost(() => Assert.That(natives.Any(server.System<WFDormancySystem>().BodySleeps), Is.False,
                "Some natives are too far from the player to breathe."));
            await server.WaitPost(() =>
            {
                var slimes = entMan.EntityQueryEnumerator<SlimeComponent, TransformComponent>();
                while (slimes.MoveNext(out var uid, out var slime, out var xform))
                {
                    if (xform.ParentUid != ground || slime.Breed != "WFCarcinomaMutation")
                        continue;
                    entMan.RemoveComponent<HTNComponent>(uid);
                    natives.Add(uid);
                }
            });

            // The client keeps up, or PVS falls back to sending every entity and trips on ones only the server changed.
            await pair.RunTicksSync(pair.SecondsToTicks(30f));

            await server.WaitAssertion(() =>
            {
                var mobs = server.System<MobStateSystem>();
                var breathers = 0;
                foreach (var native in natives)
                {
                    var name = entMan.GetComponent<MetaDataComponent>(native).EntityPrototype!.ID;
                    if (entMan.HasComponent<InventoryComponent>(native))
                        TestContext.Progress.WriteLine($"Native with an inventory: {name}");
                    if (!entMan.HasComponent<RespiratorComponent>(native))
                        continue;

                    breathers++;
                    Assert.That(entMan.HasComponent<BreathingImmunityComponent>(native), Is.True, $"{name} still has to breathe.");
                    Assert.That(Damage(entMan, native, "Asphyxiation"), Is.Zero, $"{name} choked on its own world's air.");
                    Assert.That(mobs.IsAlive(native), Is.True, $"{name} died on its own world.");
                }

                TestContext.Progress.WriteLine($"Natives: {natives.Count}, of which breathe: {breathers}");
                Assert.That(breathers, Is.GreaterThan(0), "No native has lungs to test.");
                Assert.That(natives.Any(n => entMan.HasComponent<SlimeComponent>(n)), Is.True, "No wild slime was tested.");

                Assert.That(entMan.HasComponent<BreathingImmunityComponent>(human), Is.False, "A human was taken for a native.");
                var hurt = Damage(entMan, human, "Asphyxiation") + Damage(entMan, human, "Poison");
                var lungs = entMan.GetComponent<RespiratorComponent>(human);
                TestContext.Progress.WriteLine($"Human after 30 s: {hurt} asphyxiation and poison, saturation {lungs.Saturation} " +
                                               $"of {lungs.MaxSaturation}, {lungs.SuffocationCycles} short breaths");
                Assert.That(hurt > 0f || lungs.SuffocationCycles > 0, Is.True, "A human breathed Carcinoma's air without running short.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The Gut below breathes the same carbon dioxide, and the life born down there lives in it too.</summary>
    [Test]
    public async Task CavernAirIsCarbonDioxideAndItsNativesLiveInIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await CavernFixture.EnableCaverns(pair);
        var world = await CavernFixture.BuildWorld(pair, Surface);
        var natives = new List<EntityUid>();

        try
        {
            Assert.That(world.Cavern, Is.Not.EqualTo(EntityUid.Invalid), "Carcinoma was built without its cavern.");
            var gate = await CavernFixture.Gate(pair, world);

            await server.WaitPost(() =>
            {
                var air = MapAir(entMan, world.Cavern);
                Assert.That(air.GetMoles(Gas.CarbonDioxide) / air.TotalMoles, Is.GreaterThan(0.8f), "The cavern is not mostly carbon dioxide.");
                Assert.That(air.GetMoles(Gas.Ammonia), Is.Zero, "The cavern still holds ammonia.");
                Assert.That(air.Pressure * air.GetMoles(Gas.Oxygen) / air.TotalMoles, Is.LessThan(16f), "The cavern has enough oxygen to breathe.");

                // Each cavern fauna table's mobs are natives on the cavern map; those with lungs are spawned there.
                var comp = entMan.GetComponent<WFPlanetNativesComponent>(world.Cavern);
                var known = server.System<WFPlanetNativesSystem>().NativePrototypes(comp);
                var context = new EntityTableContext();
                var spot = CavernFixture.TileCentre(CavernFixture.ClimbTile(gate) + new Vector2i(0, -1));
                foreach (var tableId in CavernTables)
                {
                    var table = server.ProtoMan.Index<EntityTablePrototype>(tableId);
                    foreach (var (id, _) in table.Table.ListSpawns(entMan, server.ProtoMan, context))
                    {
                        Assert.That(known, Does.Contain(id), $"{id} from {tableId} isn't a native of the cavern.");
                        if (!server.ProtoMan.Index<EntityPrototype>(id).Components.ContainsKey("Respirator") || natives.Count >= 4)
                            continue;

                        var mob = entMan.SpawnEntity(id, new EntityCoordinates(world.Cavern, spot));
                        entMan.RemoveComponent<HTNComponent>(mob);
                        natives.Add(mob);
                    }
                }

                Assert.That(natives, Is.Not.Empty, "No cavern native has lungs to test.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2f));
            await server.WaitAssertion(() =>
            {
                foreach (var native in natives)
                {
                    var name = entMan.GetComponent<MetaDataComponent>(native).EntityPrototype!.ID;
                    Assert.That(entMan.HasComponent<BreathingImmunityComponent>(native), Is.True, $"{name} has to breathe in its own cavern.");
                }
            });
        }
        finally
        {
            await CavernFixture.Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The fauna tables the Gut's cavern spawns from.</summary>
    private static readonly string[] CavernTables =
    {
        "WFCavernFaunaCarcinoma",
        "WFCavernFaunaCarcinomaStomach",
        "WFCavernFaunaCarcinomaNerve",
        "WFCavernFaunaCarcinomaOssuary",
        "WFCavernFaunaCarcinomaBlood",
    };

    /// <summary>
    /// A vent walled into a room breathes in and out on its cycle and fills the room with warm carbon dioxide; one out on
    /// the open ground leaves the world's air as it is.
    /// </summary>
    [Test]
    public async Task AVentExhalesCarbonDioxideIntoItsTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var layers = await Build(pair);
        var ground = layers[0];
        var room = Area(Vector2i.Zero, new Vector2i(2, 2)).ToList();
        var vent = EntityUid.Invalid;
        var outside = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() =>
            {
                SetTiles(pair, ground, Ground, Area(new Vector2i(-4, -4), new Vector2i(12, 12)));
                SetTiles(pair, ground, FloorTile, room);
                foreach (var index in Area(new Vector2i(-1, -1), new Vector2i(3, 3)).Except(room))
                    entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2f));

            var carbonBefore = 0f;
            var temperatureBefore = 0f;
            var mapCarbon = 0f;
            await server.WaitPost(() =>
            {
                Assert.That(room.Select(i => atmos.GetTileMixture(ground, ground, i)), Has.All.Matches<GasMixture?>(a => a is { Immutable: false }),
                    "The room has no air of its own.");
                carbonBefore = RoomGas(atmos, ground, room, Gas.CarbonDioxide);
                temperatureBefore = atmos.GetTileMixture(ground, ground, new Vector2i(1, 1))!.Temperature;
                mapCarbon = MapAir(entMan, ground).GetMoles(Gas.CarbonDioxide);

                vent = Quicken(entMan, timing, entMan.SpawnEntity(Vent, new EntityCoordinates(ground, 1.5f, 1.5f)));
                outside = Quicken(entMan, timing, entMan.SpawnEntity(Vent, new EntityCoordinates(ground, 9.5f, 9.5f)));
            });

            // Three breaths of a second and a half each.
            await server.WaitRunTicks(pair.SecondsToTicks(5f));

            await server.WaitAssertion(() =>
            {
                var comp = entMan.GetComponent<WFPlanetVentComponent>(vent);
                var gained = RoomGas(atmos, ground, room, Gas.CarbonDioxide) - carbonBefore;
                TestContext.Progress.WriteLine($"Room carbon dioxide gained: {gained} mol");
                Assert.That(gained, Is.GreaterThan(comp.ExhaleMoles * 1.5f), "The vent did not breathe carbon dioxide into its room.");
                Assert.That(atmos.GetTileMixture(ground, ground, new Vector2i(1, 1))!.Temperature, Is.GreaterThan(temperatureBefore),
                    "The vent's breath is not warm.");
                Assert.That(entMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == Puff), Is.GreaterThan(0),
                    "The vent puffed nothing visible.");
                Assert.That(MapAir(entMan, ground).GetMoles(Gas.CarbonDioxide), Is.EqualTo(mapCarbon),
                    "A vent on the open ground changed the world's air.");
                Assert.That(entMan.GetComponent<WFPlanetVentComponent>(outside).NextPhase, Is.GreaterThan(timing.CurTime - TimeSpan.FromSeconds(1)),
                    "The open-ground vent stopped breathing.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Vents grow in fields on the surface, a few to a screen there and sparse over the world, and in the caverns.</summary>
    [Test]
    public async Task VentsGrowInFields()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CavernCVars.Caverns, true);
            server.CfgMan.SetCVar(CavernCVars.CavernClaims, false);
        });
        var entMan = server.EntMan;
        var layers = await Build(pair);

        try
        {
            await server.WaitAssertion(() =>
            {
                var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(Surface);
                var network = entMan.GetComponent<WFPlanetNetworkComponent>(NetworkOf(entMan, layers[0]));
                var generator = server.System<SharedBiomeSystem>();
                var radius = surface.Radius;

                var (vents, sampled) = Sample(generator, entMan.GetComponent<BiomeComponent>(layers[0]), radius);
                var share = 100.0 * vents.Count / sampled;

                // A screen is about 15 tiles across.
                var screens = vents.GroupBy(v => new Vector2i((int) MathF.Floor(v.X / 15f), (int) MathF.Floor(v.Y / 15f)))
                    .Select(g => g.Count()).ToList();
                TestContext.Progress.WriteLine($"Carcinoma vents: {vents.Count} of {sampled} tiles ({share:F3}%), " +
                                               $"{screens.Count} screens with any, at most {screens.Max()} on one, " +
                                               $"{screens.Count(c => c >= 2)} with two or more");
                Assert.That(vents.Count, Is.GreaterThan(100), "Too few vents on the surface.");
                Assert.That(share, Is.LessThan(1.0), "Vents cover too much of the surface.");
                Assert.That(screens.Max(), Is.LessThanOrEqualTo(10), "A screen is crowded with vents.");
                Assert.That(screens.Count(c => c >= 2), Is.GreaterThan(10), "Vents never come a few to a screen.");

                Assert.That(network.LowerLayers, Is.Not.Empty, "Carcinoma has no caverns.");
                var (cavernVents, cavernSampled) = Sample(generator, entMan.GetComponent<BiomeComponent>(network.LowerLayers[0]), radius);
                TestContext.Progress.WriteLine($"Cavern vents: {cavernVents.Count} of {cavernSampled} tiles");
                Assert.That(cavernVents.Count, Is.GreaterThan(20), "Vents don't grow in the caverns.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>On a formed world, with its sites and routes laid first, no vent stands on a site, road, shoulder or trail.</summary>
    [Test]
    public async Task VentsNeverGrowOnRoadsOrSites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.GenerationBudget, 1000f);
            var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Carcinoma", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        var ground = layers[0];

        try
        {
            for (var i = 0; i < 600 && !entMan.HasComponent<WFPlanetPreloadedComponent>(ground); i++)
                await pair.RunTicksSync(10);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True, "Carcinoma never finished forming.");
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);

                // A site's own ground and everything under a wreck's hull is pinned before the terrain loads; the open
                // ground round a hull, inside its bounding box, is the world's.
                var kept = new HashSet<Vector2i>(record.RoadTiles);
                kept.UnionWith(record.ShoulderTiles);
                kept.UnionWith(record.TrailTiles);
                foreach (var site in record.Sites)
                    kept.UnionWith(site.Tiles);

                var grid = entMan.GetComponent<MapGridComponent>(ground);
                var biome = entMan.GetComponent<BiomeComponent>(ground);
                var biomes = server.System<BiomeSystem>();
                var maps = server.System<SharedMapSystem>();
                var vents = 0;
                var query = entMan.EntityQueryEnumerator<WFPlanetVentComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    if (xform.ParentUid != ground)
                        continue;
                    vents++;
                    var tile = maps.TileIndicesFor(ground, grid, xform.Coordinates);
                    Assert.That(kept, Does.Not.Contain(tile), $"A vent grew on a site or route at {tile}.");
                    Assert.That(biomes.WfIsPinned((ground, biome), tile), Is.False, $"A vent grew on pinned ground at {tile}.");
                }

                TestContext.Progress.WriteLine($"Formed Carcinoma: {vents} vents, {record.Sites.Count} sites, " +
                                               $"{record.RoadTiles.Count + record.TrailTiles.Count} route tiles");
                Assert.That(vents, Is.GreaterThan(0), "The formed world grew no vents to check.");
                Assert.That(record.Sites, Is.Not.Empty, "The formed world has no sites to check.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static async Task<List<EntityUid>> Build(TestPair pair)
    {
        var server = pair.Server;
        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Carcinoma", null);
            Assert.That(network, Is.Not.Null, "Carcinoma failed to build.");
            layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        await server.WaitRunTicks(1);
        return layers;
    }

    private static EntityUid NetworkOf(IEntityManager entMan, EntityUid layer)
    {
        var query = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (query.MoveNext(out var uid, out var network))
        {
            if (network.Layers.Contains(layer))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>Spawns a mob on a tile with its AI taken out, so it neither wanders nor fights.</summary>
    private static EntityUid SpawnStill(IEntityManager entMan, string id, EntityUid ground, Vector2i tile)
    {
        var mob = entMan.SpawnEntity(id, new EntityCoordinates(ground, tile.X + 0.5f, tile.Y + 0.5f));
        entMan.RemoveComponent<HTNComponent>(mob);
        return mob;
    }

    /// <summary>Sets a vent breathing every second and a half from now, with nobody near.</summary>
    private static EntityUid Quicken(IEntityManager entMan, IGameTiming timing, EntityUid vent)
    {
        var comp = entMan.GetComponent<WFPlanetVentComponent>(vent);
        comp.RequirePlayer = false;
        comp.InhaleTime = TimeSpan.FromSeconds(0.5);
        comp.ExhaleTime = TimeSpan.FromSeconds(0.5);
        comp.MinRest = TimeSpan.FromSeconds(0.5);
        comp.MaxRest = TimeSpan.FromSeconds(0.5);
        comp.NextPhase = timing.CurTime;
        return vent;
    }

    private static GasMixture MapAir(IEntityManager entMan, EntityUid map)
    {
        GasMixture air = entMan.GetComponent<MapAtmosphereComponent>(map).Mixture;
        return air;
    }

    private static float Damage(IEntityManager entMan, EntityUid uid, string type)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type).Float();
    }

    private static (List<Vector2i> Vents, int Sampled) Sample(SharedBiomeSystem generator, BiomeComponent biome, int radius)
    {
        var vents = new List<Vector2i>();
        var sampled = 0;
        for (var x = -radius; x < radius; x++)
        for (var y = -radius; y < radius; y++)
        {
            if (x * x + y * y > radius * radius)
                continue;
            var pos = new Vector2i(x, y);
            if (!generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile) || tile == null)
                continue;
            sampled++;
            if (generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature) &&
                feature == Vent)
            {
                vents.Add(pos);
            }
        }

        return (vents, sampled);
    }

    private static float RoomGas(AtmosphereSystem atmos, EntityUid ground, List<Vector2i> room, Gas gas)
    {
        return room.Sum(index => atmos.GetTileMixture(ground, ground, index)?.GetMoles(gas) ?? 0f);
    }

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    private static void SetTiles(TestPair pair, EntityUid ground, string tile, IEnumerable<Vector2i> indices)
    {
        var server = pair.Server;
        var id = server.ResolveDependency<ITileDefinitionManager>()[tile].TileId;
        var grid = server.EntMan.GetComponent<MapGridComponent>(ground);
        server.System<SharedMapSystem>().SetTiles(ground, grid, indices.Select(index => (index, new Tile(id))).ToList());
    }
}
