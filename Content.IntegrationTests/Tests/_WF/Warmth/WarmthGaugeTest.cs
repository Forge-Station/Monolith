#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Warmth;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Warmth;

/// <summary>The HUD temperature gauge: where the body sits, which way it is heading, and a fire reaching it.</summary>
[TestFixture]
public sealed class WarmthGaugeTest
{
    private const string OpenTile = "FloorPlanetGrass";
    private const float WinterNight = 257.15f;
    private const int Half = 12;

    /// <summary>
    /// A core a hair over normal used to sit there for good in the cold: its recovery step was under a float step, so it
    /// never reached normal and the cooling branch never ran. Whether a body spawned that way depended on timing.
    /// </summary>
    [Test]
    public async Task CoreAHairOverNormalStillCools()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() => mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));
        await pair.RunTicksSync(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var system = server.System<Content.Server._WF.Wolfmed.Life.WolfmedBodyTemperatureSystem>();
            var temperature = entMan.GetComponent<Content.Server.Temperature.Components.TemperatureComponent>(mob);
            var normal = system.GetLines(mob)!.Value.Normal;
            system.SetCore(mob, normal + 0.0009f);
            temperature.CurrentTemperature = 250f;
            for (var i = 0; i < 300; i++)
            {
                system.Tick(mob, 1f);
            }

            Assert.That(system.GetCore(mob), Is.LessThan(normal - 1f), "A core just over normal never began to cool.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GaugeFollowsTheColdAndAFire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            var air = WolfmedScenario.Air();
            air.Temperature = WinterNight;
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            new WolfmedScenario(entMan).KeepGrid(map.Grid);

            var maps = server.System<SharedMapSystem>();
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -Half; x <= Half; x++)
            {
                for (var y = -Half; y <= Half; y++)
                    tiles.Add((new Vector2i(x, y), tile));
            }

            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);

            // A blizzard already at full strength.
            var weather = server.System<SharedWolfWeatherSystem>();
            weather.SetWeather(map.MapUid, new ProtoId<WolfWeatherPrototype>("WFBlizzard"), null, 1);
            var comp = entMan.GetComponent<WolfWeatherMapComponent>(map.MapUid);
            var weathers = comp.Weathers;
            weathers.Clear();
            weathers.Add(new WolfWeatherEntry
            {
                Weather = "WFBlizzard",
                Start = server.Resolve<Robust.Shared.Timing.IGameTiming>().CurTime - TimeSpan.FromHours(1),
                Seed = 1,
            });

            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        // Out in the cold with no fire: the gauge sinks and its arrow points down.
        await pair.RunTicksSync(pair.SecondsToTicks(240f));
        await server.WaitAssertion(() =>
        {
            var warmth = entMan.GetComponent<WFWarmthComponent>(mob);
            Assert.That(warmth.NearWarmth, Is.False, "No fire is lit, yet the body is near warmth.");
            Assert.That(warmth.Gauge, Is.LessThan(50), "A body in a blizzard should read colder than comfortable.");
            Assert.That(warmth.Trend, Is.EqualTo(WFWarmthTrend.Cooling), "A body in a blizzard should be cooling.");
        });

        // The same body is told at the client too.
        await pair.Client.WaitAssertion(() =>
        {
            var clientMob = pair.Client.EntMan.GetEntity(entMan.GetNetEntity(mob));
            var seen = pair.Client.EntMan.GetComponent<WFWarmthComponent>(clientMob);
            Assert.That(seen.Trend, Is.EqualTo(WFWarmthTrend.Cooling));
            Assert.That(seen.Gauge, Is.LessThan(50));
        });

        // Beside a bonfire the gauge says so, and the body warms.
        var coldGauge = 0;
        await server.WaitPost(() =>
        {
            coldGauge = entMan.GetComponent<WFWarmthComponent>(mob).Gauge;
            entMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
        });

        var warming = false;
        for (var i = 0; i < 24 && !warming; i++)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(5f));
            await server.WaitPost(() => warming = entMan.GetComponent<WFWarmthComponent>(mob).Trend == WFWarmthTrend.Warming);
        }

        await server.WaitAssertion(() =>
        {
            var warmth = entMan.GetComponent<WFWarmthComponent>(mob);
            Assert.That(warmth.NearWarmth, Is.True, "A lit bonfire at arm's reach did not show on the gauge.");
            Assert.That(warming, Is.True, "A body at a bonfire never started warming.");
            Assert.That(warmth.Gauge, Is.GreaterThanOrEqualTo(coldGauge));
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }
}
