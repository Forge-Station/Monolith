#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.GroundCover;
using Content.Server._WF.Planets;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Movement.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Every world's lakes freeze and thaw by that world's own air, never by a fixed rule of the world.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class LakeClimateTest
{
    private const string Ground = "FloorPlanetGrass";
    private const float Margin = 2f;

    /// <summary>A 7x7 pond: its middle tile is four tiles from the shore.</summary>
    private static readonly Vector2i PondMin = new(-7, -7);
    private static readonly Vector2i Centre = new(-4, -4);
    private static readonly Vector2i Edge = new(-7, -4);
    private static readonly Vector2i Chunk = WFSnowCover.ChunkOf(Centre);

    /// <summary>Each surface with the kind of lake it grows in the open.</summary>
    [TestCase("WFSurfaceAsclepiu", "MonoFloorWaterEntity", true)]
    [TestCase("WFSurfaceMerak", "MonoFloorWaterEntity", false)]
    [TestCase("WFSurfaceAerumna", "MonoFloorWaterEntity", false)]
    [TestCase("WFSurfaceThrascias", "WFThrasciasLakeWater", true)]
    [TestCase("WFSurfaceCarcinoma", "WFBloodOcean", false)]
    public async Task LakesFollowTheirWorldsAir(string surfaceId, string lakeId, bool getsCold)
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var layers = new List<EntityUid>();
        var waters = new Dictionary<Vector2i, EntityUid>();
        EntityUid network = default;
        var thrascias = surfaceId == "WFSurfaceThrascias";

        await server.WaitPost(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(proto.Index<WFPlanetSurfacePrototype>(surfaceId), Vector2.Zero, surfaceId, null);
            Assert.That(built, Is.Not.Null);
            network = built!.Value;
            layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            waters = LayPond(pair, layers[0], lakeId);
        });

        // The world grows this lake in the open, by its own recipe.
        await server.WaitAssertion(() =>
        {
            var biome = server.EntMan.GetComponent<BiomeComponent>(layers[0]);
            Assert.That(WaterOf(server, biome.Layers), Does.Contain(lakeId), $"{surfaceId} doesn't grow {lakeId}.");
        });

        // The world's own season data says how cold and how warm it gets, once its climate has read the ground's air.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        float coldest = 0, warmest = 0;
        (string? Season, int Minute) coldAt = default, warmAt = default;
        await server.WaitAssertion(() =>
        {
            Extremes(server, network, out coldest, out coldAt, out warmest, out warmAt);
            TestContext.Out.WriteLine($"{surfaceId}: clear air {coldest - Atmospherics.T0C:F1} C ({coldAt}) to {warmest - Atmospherics.T0C:F1} C ({warmAt})");
            if (getsCold)
                Assert.That(coldest, Is.LessThan(Atmospherics.T0C - Margin), $"{surfaceId} never gets cold enough to freeze.");
            else
                Assert.That(coldest, Is.GreaterThan(Atmospherics.T0C), $"{surfaceId} was expected to stay above freezing.");
        });

        // Its coldest hour.
        await Drive(pair, network, coldAt);
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var air = server.System<WFPlanetClimateSystem>().GetOutsideTemperature(layers[0])!.Value;
            cover.UpdateIceChunk(layers[0], Chunk);
            Run(server, cover, layers[0], TimeSpan.FromHours(4));
            Assert.That(Frozen(server, waters[Centre]), Is.EqualTo(getsCold || thrascias),
                $"{surfaceId}'s lake at {air - Atmospherics.T0C:F1} C {(getsCold ? "didn't freeze" : "froze")}.");
        });

        if (!getsCold && !thrascias)
        {
            // Forced far below freezing, a lake that isn't water stays liquid, and water in a world that is warm freezes.
            await server.WaitPost(() => SetBase(server, network, 250f));
            await Drive(pair, network, coldAt);
            await server.WaitAssertion(() =>
            {
                var cover = server.System<WFGroundCoverSystem>();
                Run(server, cover, layers[0], TimeSpan.FromHours(4));
                var blood = lakeId == "WFBloodOcean";
                Assert.That(Frozen(server, waters[Centre]), Is.Not.EqualTo(blood),
                    blood ? "Blood froze like water." : $"{surfaceId}'s lake ignored a cold snap.");
            });
        }

        if (getsCold)
        {
            // Its warmest hour, held, melts the ice.
            await Drive(pair, network, warmAt);
            if (thrascias)
            {
                // Thrascias never warms by itself; an admin or a changed world warms its base air.
                await server.WaitPost(() => SetBase(server, network, Atmospherics.T0C + 12f));
                await Drive(pair, network, warmAt);
            }

            await server.WaitAssertion(() =>
            {
                var cover = server.System<WFGroundCoverSystem>();
                var air = server.System<WFPlanetClimateSystem>().GetOutsideTemperature(layers[0])!.Value;
                Assert.That(air, Is.GreaterThan(Atmospherics.T0C + Margin), "Precondition: the air is warm.");
                Assert.That(Frozen(server, waters[Centre]), Is.True, "Precondition: the lake was frozen.");
                Run(server, cover, layers[0], TimeSpan.FromHours(8));
                Assert.That(Frozen(server, waters[Centre]), Is.False, $"{surfaceId}'s lake didn't thaw at {air - Atmospherics.T0C:F1} C.");
                Assert.That(Frozen(server, waters[Edge]), Is.False);
            });
        }

        await server.WaitAssertion(() => server.System<WFPlanetNetworkSystem>().DeleteNetwork(network));
        await pair.CleanReturnAsync();
    }

    /// <summary>The frozen lake's thresholds are data: an odd freeze point is followed and the defaults are today's.</summary>
    [Test]
    public async Task FreezeAndThawPointsComeFromTheWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var waters = new Dictionary<Vector2i, EntityUid>();

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            waters = LayPond(pair, map.Grid, "MonoFloorWaterEntity");
            foreach (var water in waters.Values)
            {
                var comp = entMan.GetComponent<WFFreezableWaterComponent>(water);
                Assert.That(comp.FreezeAt, Is.EqualTo(Atmospherics.T0C - 2f), "The default freeze point moved.");
                Assert.That(comp.ThawAt, Is.EqualTo(Atmospherics.T0C), "The default thaw point moved.");
                comp.FreezeAt = Atmospherics.T0C - 20f;
                comp.ThawAt = Atmospherics.T0C - 10f;
            }
        });

        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            SetAir(pair, map, Atmospherics.T0C - 8f);
            cover.UpdateIceChunk(map.Grid, Chunk);
            Run(server, cover, map.Grid, TimeSpan.FromHours(3));
            Assert.That(Frozen(server, waters[Centre]), Is.False, "Water froze above its own freeze point.");

            SetAir(pair, map, Atmospherics.T0C - 30f);
            Run(server, cover, map.Grid, TimeSpan.FromHours(3));
            Assert.That(Frozen(server, waters[Centre]), Is.True, "Water didn't freeze below its own freeze point.");

            // Above its thaw point, at a temperature that is still below freezing, it thaws.
            SetAir(pair, map, Atmospherics.T0C - 5f);
            Run(server, cover, map.Grid, TimeSpan.FromHours(3));
            Assert.That(Frozen(server, waters[Centre]), Is.False, "Ice didn't thaw above its own thaw point.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A lake frozen through a winter and left unwatched is open when a player comes back in a later season.</summary>
    [Test]
    public async Task AFarChunkLeftInWinterIsOpenInSpring()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var layers = new List<EntityUid>();
        var waters = new Dictionary<Vector2i, EntityUid>();
        EntityUid network = default;

        await server.WaitPost(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(proto.Index<WFPlanetSurfacePrototype>("WFSurfaceAsclepiu"), Vector2.Zero, "Asclepiu", null);
            network = built!.Value;
            layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            waters = LayPond(pair, layers[0], "MonoFloorWaterEntity");
        });

        // A winter dawn, clear: the lake freezes through.
        await Drive(pair, network, ("Winter", 5 * 60));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            cover.UpdateIceChunk(layers[0], Chunk);
            Run(server, cover, layers[0], TimeSpan.FromMinutes(90));
            Assert.That(Frozen(server, waters[Centre]), Is.True, "Precondition: the lake froze in winter.");
            Assert.That(cover.GetIceChunk(layers[0], Centre)!.Thickness, Is.EqualTo(WFGroundCoverSystem.IceMaxThickness).Within(1f));
        });

        // Nobody looks at it for a season and a half, then a cold-ish autumn dawn: spring and summer happened meanwhile.
        const float gap = 14400f;
        await Drive(pair, network, ("Autumn", 5 * 60));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var chunk = cover.GetIceChunk(layers[0], Centre)!;
            var climate = server.System<WFPlanetClimateSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(climate.GetClearTemperatureAt(layers[0], now - TimeSpan.FromSeconds(gap / 2))!.Value, Is.GreaterThan(Atmospherics.T0C + 5f),
                "Precondition: spring and summer came in between.");
            Assert.That(climate.GetOutsideTemperature(layers[0])!.Value, Is.LessThan(Atmospherics.T0C + 8f),
                "Precondition: the air on return is only mildly warm, which an hour of it would not melt through.");

            chunk.Updated = now - TimeSpan.FromSeconds(gap);
            cover.UpdateIceChunk(layers[0], Chunk);
            Assert.That(Frozen(server, waters[Centre]), Is.False, "The lake came back frozen though a spring and a summer went by.");
            Assert.That(Frozen(server, waters[Edge]), Is.False);
        });

        await server.WaitAssertion(() => server.System<WFPlanetNetworkSystem>().DeleteNetwork(network));
        await pair.CleanReturnAsync();
    }

    /// <summary>Every standing-water entity a world grows is freezable water, or is a pool of something else.</summary>
    [Test]
    public async Task EveryWorldsWaterIsFreezableOrNotWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var checkedWaters = new HashSet<string>();

        foreach (var surface in proto.EnumeratePrototypes<WFPlanetSurfacePrototype>().ToList())
        {
            var layers = new List<EntityUid>();
            EntityUid network = default;
            await server.WaitPost(() =>
            {
                var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surface.ID, null);
                if (built == null)
                    return;
                network = built.Value;
                layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            });

            if (layers.Count == 0)
                continue;

            await server.WaitAssertion(() =>
            {
                var biome = server.EntMan.GetComponent<BiomeComponent>(layers[0]);
                foreach (var id in WaterOf(server, biome.Layers))
                {
                    var entity = proto.Index<EntityPrototype>(id);
                    var pool = entity.TryGetComponent<SolutionContainerManagerComponent>(out var solutions, factory)
                        ? solutions.Solutions?.GetValueOrDefault("pool")
                        : null;
                    var water = pool != null && pool.Contents.All(r => r.Reagent.Prototype == "Water");
                    var freezable = entity.TryGetComponent<WFFreezableWaterComponent>(out var kind, factory);
                    if (water)
                    {
                        Assert.That(freezable, Is.True, $"{surface.ID}'s {id} is water that can't freeze.");
                        Assert.That(kind!.FreezeAt, Is.GreaterThan(Atmospherics.TCMB), $"{id} never freezes.");
                        Assert.That(kind.ThawAt, Is.GreaterThanOrEqualTo(kind.FreezeAt), $"{id} thaws before it freezes.");
                    }
                    else if (freezable)
                    {
                        Assert.That(kind!.FreezeAt, Is.LessThanOrEqualTo(0f), $"{surface.ID}'s {id} isn't water but freezes into water ice.");
                    }

                    checkedWaters.Add(id);
                }
            });

            await server.WaitAssertion(() => server.System<WFPlanetNetworkSystem>().DeleteNetwork(network));
        }

        Assert.That(checkedWaters, Does.Contain("WFThrasciasLakeWater"));
        Assert.That(checkedWaters, Does.Contain("MonoFloorWaterEntity"));
        await pair.CleanReturnAsync();
    }

    /// <summary>Works a pond chunk through a time in air that stays as it is, in two-minute strides.</summary>
    private static void Run(RobustIntegrationTest.ServerIntegrationInstance server, WFGroundCoverSystem cover, EntityUid grid, TimeSpan time)
    {
        for (var passed = TimeSpan.Zero; passed < time; passed += TimeSpan.FromMinutes(2))
        {
            var chunk = cover.GetIceChunk(grid, Centre);
            if (chunk == null)
            {
                cover.UpdateIceChunk(grid, Chunk);
                continue;
            }

            chunk.Updated -= TimeSpan.FromMinutes(2);
            cover.UpdateIceChunk(grid, Chunk);
        }
    }

    /// <summary>Puts a world into a season and hour with clear skies, and lets its air settle there.</summary>
    private static async Task Drive(TestPair pair, EntityUid network, (string? Season, int Minute) at)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var weather = server.System<WFPlanetWeatherSystem>();
            if (at.Season != null)
                weather.SetSeason(network, at.Season);
            weather.SetMinuteOfDay(network, at.Minute);
            weather.SetWeather(network, null, TimeSpan.FromHours(6));
            server.System<WFPlanetClimateSystem>().Snap(network);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));
    }

    private static void SetBase(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid network, float kelvin)
    {
        server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).BaseTemperature = kelvin;
    }

    /// <summary>The coldest and warmest clear-sky air over the world's year, and the season and hour of each.</summary>
    private static void Extremes(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid network,
        out float coldest, out (string? Season, int Minute) coldAt, out float warmest, out (string? Season, int Minute) warmAt)
    {
        var weather = server.System<WFPlanetWeatherSystem>();
        var climate = server.System<WFPlanetClimateSystem>();
        var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
        var profile = server.ResolveDependency<IPrototypeManager>().Index(state.Profile);
        var now = server.ResolveDependency<IGameTiming>().CurTime;
        var year = Math.Max(profile.DaySeconds, profile.Seasons.Count > 1 ? profile.Seasons.Sum(s => s.Length) : 0f);
        var step = profile.DaySeconds / 48f;

        coldest = float.MaxValue;
        warmest = float.MinValue;
        coldAt = warmAt = default;
        for (var seconds = 0f; seconds < year; seconds += step)
        {
            var at = now + TimeSpan.FromSeconds(seconds);
            var value = climate.GetTarget(state, profile, at, null);
            var where = (weather.GetSeason(state, profile, at)?.Id, weather.GetMinuteOfDay(state, profile, at));
            if (value < coldest)
            {
                coldest = value;
                coldAt = where;
            }

            if (value > warmest)
            {
                warmest = value;
                warmAt = where;
            }
        }
    }

    /// <summary>The standing-water entities a biome's recipe can grow.</summary>
    private static HashSet<string> WaterOf(RobustIntegrationTest.ServerIntegrationInstance server, List<IBiomeLayer> layers, int depth = 0)
    {
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var found = new HashSet<string>();
        if (depth > 8)
            return found;

        foreach (var layer in layers)
        {
            switch (layer)
            {
                case BiomeEntityLayer entities:
                    foreach (var id in entities.Entities)
                    {
                        if (proto.TryIndex<EntityPrototype>(id, out var entity)
                            && entity.Components.ContainsKey(factory.GetComponentName<FloorOccluderComponent>()))
                            found.Add(id);
                    }

                    break;
                case BiomeMetaLayer meta when proto.TryIndex<BiomeTemplatePrototype>(meta.Template, out var template):
                    found.UnionWith(WaterOf(server, template.Layers, depth + 1));
                    break;
            }
        }

        return found;
    }

    private static Dictionary<Vector2i, EntityUid> LayPond(TestPair pair, EntityUid grid, string lake)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()[Ground].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -10; x <= 2; x++)
        {
            for (var y = -10; y <= 2; y++)
                tiles.Add((new Vector2i(x, y), tile));
        }

        maps.SetTiles(grid, gridComp, tiles);
        var waters = new Dictionary<Vector2i, EntityUid>();
        for (var x = PondMin.X; x < PondMin.X + 7; x++)
        {
            for (var y = PondMin.Y; y < PondMin.Y + 7; y++)
            {
                var at = new Vector2i(x, y);
                waters[at] = entMan.SpawnEntity(lake, new EntityCoordinates(grid, at + new Vector2(0.5f, 0.5f)));
            }
        }

        return waters;
    }

    private static void SetAir(TestPair pair, TestMapData map, float temperature)
    {
        var atmos = pair.Server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
        atmos.SetMapAtmosphere(map.MapUid, false, GroundCoverTest.Air(temperature));
        for (var x = -8; x <= 8; x++)
        {
            for (var y = -8; y <= 8; y++)
            {
                if (atmos.GetTileMixture((map.Grid.Owner, null, null), map.MapUid, new Vector2i(x, y)) is { } air)
                    air.Temperature = temperature;
            }
        }
    }

    private static bool Frozen(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid water)
    {
        return server.EntMan.GetComponent<WFFreezableWaterComponent>(water).Frozen;
    }
}
