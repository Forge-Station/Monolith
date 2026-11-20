#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.GroundCover;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Atmos;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using ClientGroundCoverSystem = Content.Client._WF.GroundCover.WFGroundCoverSystem;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Wet ground in rain, and snow cover that piles up, slows, shovels off, melts and reaches clients.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class GroundCoverTest
{
    private const string OpenTile = "FloorPlanetGrass";
    private const int Half = 8;

    /// <summary>A tile under a roof, and one in the open.</summary>
    private static readonly Vector2i Roofed = new(2, 2);
    private static readonly Vector2i Open = new(1, 1);

    [Test]
    public async Task RainWetsOpenGroundAndItDriesAfter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            LayOpenGround(pair, map.Grid);
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFRainHeavy", null, 0);
        });

        // Past the weather's fade-in, so it rains at full strength.
        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(entMan.TryGetComponent<WFGroundWeatherComponent>(map.MapUid, out var ground), Is.True,
                "Rain left the map without ground weather.");
            Assert.That(cover.GetPrecipitation(map.MapUid).Rain, Is.EqualTo(1f).Within(0.01f));

            cover.StepMap((map.MapUid, ground!), 120f);
            Assert.That(ground!.WetnessExact, Is.EqualTo(1f).Within(0.001f), "Two minutes of heavy rain left the ground dry.");
            Assert.That(ground.Wetness, Is.EqualTo(1f).Within(0.001f), "Clients weren't sent the wetness.");
            Assert.That(ground.PuddlesExact, Is.GreaterThan(0.4f), "No puddles formed in heavy rain.");

            Assert.That(cover.GetTileWetness(map.Grid, Open), Is.GreaterThan(0.9f));
            Assert.That(cover.GetTileWetness(map.Grid, Roofed), Is.Zero, "It got wet under a roof.");

            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, null, null, 0);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            Assert.That(cover.GetPrecipitation(map.MapUid).Rain, Is.Zero);

            var before = ground.WetnessExact;
            cover.StepMap((map.MapUid, ground), 60f);
            Assert.That(ground.WetnessExact, Is.LessThan(before), "The ground didn't dry once the rain stopped.");
            Assert.That(ground.WetnessExact, Is.GreaterThan(0f), "The ground dried in a minute; it should take a few.");

            cover.StepMap((map.MapUid, ground), 600f);
            Assert.That(ground.WetnessExact, Is.Zero);
            Assert.That(ground.PuddlesExact, Is.Zero, "Puddles outlasted ten dry minutes.");
            Assert.That(ground.Wetness, Is.Zero);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SnowPilesUpSlowsShovelsOffAndMelts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var chunk = WFSnowCover.ChunkOf(Open);
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            LayOpenGround(pair, map.Grid);
            // Cold air, so the snow settles and stays.
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, Air(250f));
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFBlizzard", null, 0);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            Assert.That(cover.GetPrecipitation(map.MapUid).Snow, Is.EqualTo(1.5f).Within(0.01f));

            // Start the chunk thin, then lay a minute of blizzard on it.
            ground.BaseDepth = 0f;
            Assert.That(cover.UpdateChunk(map.Grid, chunk), Is.True, "Snowfall started no cover chunk.");
            var thin = cover.GetDepth(map.Grid, Open);

            cover.StepMap((map.MapUid, ground), 60f);
            cover.UpdateChunk(map.Grid, chunk);
            var deep = cover.GetDepth(map.Grid, Open);
            Assert.That(deep, Is.GreaterThan(thin + WFSnowCover.UnitsPerLevel), "A minute of blizzard laid barely any snow.");
            Assert.That(cover.GetDepth(map.Grid, Roofed), Is.Zero, "Snow settled under a roof.");

            cover.StepMap((map.MapUid, ground), 120f);
            cover.UpdateChunk(map.Grid, chunk);
            Assert.That(cover.GetDepth(map.Grid, Open), Is.GreaterThan(WFSnowCover.UnitsPerLevel * 2));
            Assert.That(cover.GetDepth(map.Grid, Open), Is.LessThanOrEqualTo(WFSnowCover.MaxDepth));

            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, Open + new Vector2(0.5f, 0.5f)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
        {
            var speed = server.System<WFSnowSlowSystem>().GetSpeed(mob);
            Assert.That(speed, Is.LessThan(1f).And.GreaterThanOrEqualTo(WFSnowCover.DeepestSpeed), "Deep snow didn't slow a walker.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier, Is.EqualTo(speed).Within(0.001f),
                "The walker's speed doesn't have the snow in it.");
        });

        // A shovel clears the tile it's used on.
        await server.WaitPost(() =>
        {
            var shovel = entMan.SpawnEntity("Shovel", entMan.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(mob, shovel), Is.True, "Precondition: the mob can't hold the shovel.");
            server.System<SharedInteractionSystem>().UserInteraction(mob, new EntityCoordinates(map.Grid, Open + new Vector2(0.5f, 0.5f)), null);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(cover.GetDepth(map.Grid, Open), Is.Zero, "The shovel left the snow.");
            Assert.That(cover.GetDepth(map.Grid, Open + new Vector2i(1, 0)), Is.GreaterThan(0), "The shovel cleared more than its tile.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f),
                "Cleared ground still slowed the mob."));

        // Without snow but below freezing it stays; above freezing it melts.
        await server.WaitPost(() => server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, null, null, 0));
        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var ground = entMan.GetComponent<WFGroundWeatherComponent>(map.MapUid);
            Assert.That(cover.GetPrecipitation(map.MapUid).Snow, Is.Zero);

            cover.UpdateChunk(map.Grid, chunk);
            var cold = cover.GetDepth(map.Grid, Open + new Vector2i(1, 0));
            cover.StepMap((map.MapUid, ground), 600f);
            cover.UpdateChunk(map.Grid, chunk);
            Assert.That(cover.GetDepth(map.Grid, Open + new Vector2i(1, 0)), Is.EqualTo(cold), "Snow melted below freezing.");

            // Warm the map and, as atmos would in time, the air over its tiles.
            var atmos = server.System<AtmosphereSystem>();
            atmos.SetMapAtmosphere(map.MapUid, false, Air(293.15f));
            for (var x = -Half; x <= Half; x++)
            {
                for (var y = -Half; y <= Half; y++)
                {
                    if (atmos.GetTileMixture((map.Grid.Owner, null, null), map.MapUid, new Vector2i(x, y)) is { } air)
                        air.Temperature = 293.15f;
                }
            }

            Assert.That(cover.TileTemperature(map.Grid, map.MapUid, Open), Is.GreaterThan(WFGroundCoverSystem.Freezing),
                "Precondition: the warm map air didn't reach its tiles.");
            cover.StepMap((map.MapUid, ground), 600f);
            cover.UpdateChunk(map.Grid, chunk);
            Assert.That(cover.GetDepth(map.Grid, Open + new Vector2i(1, 0)), Is.Zero, "Snow outlasted ten warm minutes.");
            Assert.That(ground.BaseDepth, Is.Zero, "The map's cover didn't melt in warm air.");
            Assert.That(entMan.HasComponent<WFSnowCoverComponent>(map.Grid), Is.False, "Melted cover kept its chunks.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A fire melts the snow round it even in frost, and leaves the snow beyond its warmth alone.</summary>
    [Test]
    public async Task AFireMeltsTheSnowRoundIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var near = new Vector2i(-2, -1);
        var far = new Vector2i(-7, -7);

        await server.WaitPost(() =>
        {
            LayOpenGround(pair, map.Grid);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, Air(250f));
            var cover = server.System<WFGroundCoverSystem>();
            cover.SetDepth(map.Grid, near, WFSnowCover.MaxDepth);
            cover.SetDepth(map.Grid, far, WFSnowCover.MaxDepth);
            server.EntMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid, new Vector2(-2.5f, -0.5f)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(30));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Assert.That(cover.GetDepth(map.Grid, near), Is.LessThan(WFSnowCover.MaxDepth / 2), "The fire left the snow beside it.");
            Assert.That(cover.GetDepth(map.Grid, far), Is.EqualTo(WFSnowCover.MaxDepth), "Snow out of the fire's reach melted in frost.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SnowAndWetnessReachTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            LayOpenGround(pair, map.Grid);
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);

            var ground = entMan.EnsureComponent<WFGroundWeatherComponent>(map.MapUid);
            ground.WetnessExact = 0.5f;
            ground.PuddlesExact = 0.25f;
            server.System<WFGroundCoverSystem>().StepMap((map.MapUid, ground), 0f);
            server.System<WFGroundCoverSystem>().SetDepth(map.Grid, Open, 150);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await pair.Client.WaitAssertion(() =>
        {
            var clientEnt = pair.Client.EntMan;
            var grid = clientEnt.GetEntity(entMan.GetNetEntity(map.Grid.Owner));
            var mapUid = clientEnt.GetEntity(entMan.GetNetEntity(map.MapUid));
            var cover = pair.Client.System<ClientGroundCoverSystem>();
            Assert.That(cover.GetDepth(grid, Open), Is.EqualTo(150), "The client wasn't sent the snow cover.");
            Assert.That(cover.GetDepth(grid, Roofed), Is.Zero);
            Assert.That(clientEnt.GetComponent<WFGroundWeatherComponent>(mapUid).Wetness, Is.EqualTo(0.5f).Within(0.02f),
                "The client wasn't sent the wetness.");
        });

        // A client that dropped the grid's snow, as a full state resync can make it, is sent it again.
        await pair.Client.WaitPost(() =>
        {
            var net = entMan.GetNetEntity(map.Grid.Owner);
            pair.Client.System<ClientGroundCoverSystem>().Forget(new List<NetEntity> { net });
        });
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await pair.Client.WaitAssertion(() =>
        {
            var grid = pair.Client.EntMan.GetEntity(entMan.GetNetEntity(map.Grid.Owner));
            Assert.That(pair.Client.System<ClientGroundCoverSystem>().GetDepth(grid, Open), Is.EqualTo(150),
                "Snow the client dropped wasn't sent again.");
        });

        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, Open, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await pair.Client.WaitAssertion(() =>
        {
            var grid = pair.Client.EntMan.GetEntity(entMan.GetNetEntity(map.Grid.Owner));
            Assert.That(pair.Client.System<ClientGroundCoverSystem>().GetDepth(grid, Open), Is.Zero,
                "The client kept snow that was shoveled.");
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    internal static GasMixture Air(float temperature)
    {
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
        air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        return air;
    }

    /// <summary>Lays open weather ground on a test grid around its origin, with one roofed tile.</summary>
    internal static void LayOpenGround(TestPair pair, Entity<MapGridComponent> grid)
    {
        var maps = pair.Server.System<SharedMapSystem>();
        var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -Half; x <= Half; x++)
        {
            for (var y = -Half; y <= Half; y++)
                tiles.Add((new Vector2i(x, y), tile));
        }

        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        var roof = pair.Server.EntMan.EnsureComponent<RoofComponent>(grid.Owner);
        pair.Server.System<SharedRoofSystem>().SetRoof((grid.Owner, grid.Comp, roof), Roofed, true);
    }
}
