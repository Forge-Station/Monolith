using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using System.Linq;
using Content.Server._WF.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Weather;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetWeatherTest
{
    [Test]
    public async Task SixWorldsShareOneClockForDaylightShadowsAndNightAudio()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        await SetWeather(pair, true);
        var server = pair.Server;
        var client = pair.Client;
        var worlds = new System.Collections.Generic.List<(string Name, System.Collections.Generic.List<EntityUid> Layers)>();
        await server.WaitPost(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            foreach (var name in new[] { "Asclepiu", "Fervidus", "Merak", "Aerumna", "Thrascias", "Carcinoma" })
            {
                var surface = proto.Index<WFPlanetSurfacePrototype>("WFSurface" + name);
                var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
                var layers = server.EntMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers.ToList();
                server.System<BiomeSystem>().SetEnabled((layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false);
                worlds.Add((name, layers));
            }
        });
        var unchangedDeadline = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            unchangedDeadline = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(30);
            foreach (var (_, layers) in worlds)
            {
                var network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
                server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).NextChange = unchangedDeadline;
            }
            var first = server.EntMan.GetComponent<CEZMapComponent>(worlds[0].Layers[0]).NetworkUid;
            server.EntMan.GetComponent<WFPlanetWeatherComponent>(first).NextChange = TimeSpan.Zero;
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < worlds.Count; i++)
            {
                var world = server.EntMan.GetComponent<CEZMapComponent>(worlds[i].Layers[0]).NetworkUid;
                var weather = server.EntMan.GetComponent<WFPlanetWeatherComponent>(world);
                if (i == 0) Assert.That(weather.Current, Is.Not.Null);
                else
                {
                    Assert.That(weather.Current, Is.Null, "One world's rain started weather on another.");
                    Assert.That(weather.NextChange, Is.EqualTo(unchangedDeadline));
                }
            }
        });
        var viewer = await AttachViewer(pair, worlds[0].Layers[0], Vector2.Zero);
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        foreach (var (name, layers) in worlds)
        {
            var groundNet = server.EntMan.GetNetEntity(layers[0]);
            var nightBrightness = 0f;
            foreach (var hour in new[] { 0.5, 12.0 })
            {
                await server.WaitPost(() =>
                {
                    var em = server.EntMan;
                    server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(layers[0], Vector2.Zero));
                    var world = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
                    var state = em.GetComponent<WFPlanetWeatherComponent>(world);
                    var profile = server.ResolveDependency<IPrototypeManager>().Index(state.Profile);
                    state.Epoch = server.ResolveDependency<IGameTiming>().CurTime -
                        TimeSpan.FromSeconds((hour - profile.InitialHour) / 24 * profile.DaySeconds);
                });
                await pair.RunTicksSync(pair.SecondsToTicks(3));
                await client.WaitAssertion(() =>
                {
                    var em = client.EntMan;
                    var ground = em.GetEntity(groundNet);
                    var environment = em.GetComponent<WFPlanetEnvironmentComponent>(ground);
                    var cycle = em.GetComponent<LightCycleComponent>(ground);
                    var shadows = em.GetComponent<SunShadowCycleComponent>(ground);
                    var actual = em.GetComponent<MapLightComponent>(ground).AmbientLightColor;
                    Assert.That(environment.MinuteOfDay, Is.InRange((int)(hour * 60), (int)(hour * 60) + 2));
                    Assert.That(environment.IsNight, Is.EqualTo(hour < 6), name + " audio phase");
                    Assert.That(cycle.InitialOffset, Is.False);
                    Assert.That(cycle.Duration, Is.EqualTo(TimeSpan.FromHours(1)));
                    Assert.That(shadows.Duration, Is.EqualTo(cycle.Duration));
                    Assert.That(shadows.Offset, Is.EqualTo(cycle.Offset));
                    var brightness = actual.R + actual.G + actual.B;
                    if (hour < 6)
                    {
                        nightBrightness = brightness;
                        Assert.That(brightness, Is.LessThan(0.25f), name + " must be dark at 00:30");
                    }
                    else Assert.That(brightness, Is.GreaterThan(nightBrightness + 0.5f), name + " must be bright at noon");
                });
                await server.WaitAssertion(() =>
                {
                    var groundCycle = server.EntMan.GetComponent<LightCycleComponent>(layers[0]);
                    foreach (var air in layers.Skip(1).Take(layers.Count - 2))
                    {
                        var cycle = server.EntMan.GetComponent<LightCycleComponent>(air);
                        Assert.That(cycle.Duration, Is.EqualTo(groundCycle.Duration));
                        Assert.That(cycle.Offset, Is.EqualTo(groundCycle.Offset));
                    }
                    Assert.That(server.EntMan.HasComponent<LightCycleComponent>(layers.Last()), Is.False,
                        "Orbit must retain space lighting.");
                });
            }
        }
        foreach (var (_, layers) in worlds) await Teardown(pair, layers);
        await SetWeather(pair, false);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryWorldHasAValidWeatherAndClockProfile()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            var profiles = proto.EnumeratePrototypes<WFPlanetWeatherPrototype>().ToList();
            Assert.That(profiles.Count, Is.EqualTo(6));
            var tiles = pair.Server.ResolveDependency<ITileDefinitionManager>();
            foreach (var id in new[] { "FloorPlanetGrass", "FloorBasalt", "FloorDesertPlanet", "FloorChromite", "FloorSnow", "WFFloorFlesh" })
                Assert.That(((ContentTileDefinition) tiles[id]).Weather, Is.True, $"{id} would hide outdoor weather.");
            Assert.That(((ContentTileDefinition) tiles["FloorFlesh"]).Weather, Is.False, "Indoor flesh flooring should remain sheltered.");
            foreach (var world in new[] { "Asclepiu", "Fervidus", "Merak", "Aerumna", "Thrascias", "Carcinoma" })
            {
                var surface = proto.Index<WFPlanetSurfacePrototype>($"WFSurface{world}");
                var profile = profiles.Single(p => p.PlanetType == surface.PlanetType);
                Assert.That(profile.Storms, Is.Not.Empty);
                Assert.That(profile.DaySeconds, Is.GreaterThan(0));
                Assert.That(profile.InitialHour, Is.InRange(0, 23.999f));
                Assert.That(profile.ClearMinSeconds, Is.GreaterThan(WeatherComponent.ShutdownTime.TotalSeconds));
                Assert.That(profile.ClearMaxSeconds, Is.GreaterThanOrEqualTo(profile.ClearMinSeconds));
                Assert.That(profile.WeatherMinSeconds, Is.GreaterThan(WeatherComponent.StartupTime.TotalSeconds));
                Assert.That(profile.WeatherMaxSeconds, Is.GreaterThanOrEqualTo(profile.WeatherMinSeconds));
                foreach (var storm in profile.Storms)
                {
                    Assert.That(proto.TryIndex(storm.Main, out _), Is.True);
                    Assert.That(storm.Telegraph is not { } telegraph || proto.TryIndex(telegraph, out _), Is.True);
                    Assert.That(storm.End is not { } end || proto.TryIndex(end, out _), Is.True);
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CyclesReachTheGroundOnlyAndReturnToClear()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        await SetWeather(pair, true);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var transit = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            state.NextChange = TimeSpan.Zero;
            var gap = em.EnsureComponent<CEZTransitMapComponent>(transit.MapUid);
            gap.PrimaryGrid = transit.Grid.Owner;
            gap.LowerMap = layers[1];
            gap.UpperMap = layers[2];
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var expected = em.GetComponent<WeatherComponent>(layers[0]).Weather.Keys.Single();
            foreach (var layer in layers.Skip(1).Append(transit.MapUid))
            {
                Assert.That(em.TryGetComponent<WeatherComponent>(layer, out var above) && above.Weather.Count > 0, Is.False,
                    "Weather fell above the surface.");
                Assert.That(em.HasComponent<WolfWeatherMapComponent>(layer), Is.False, "Weather state sits above the surface.");
            }
            foreach (var layer in layers.Take(layers.Count - 1).Append(transit.MapUid))
            {
                if (layer == layers[0])
                    Assert.That(em.GetComponent<WeatherComponent>(layer).Weather.ContainsKey(expected), Is.True);
                var environment = em.GetComponent<WFPlanetEnvironmentComponent>(layer);
                Assert.That(environment.PlanetName, Is.EqualTo("Asclepiu"));
                Assert.That(environment.Weather, Is.Not.EqualTo("Clear"));
            }
            var transitLight = em.GetComponent<LightCycleComponent>(transit.MapUid);
            var groundLight = em.GetComponent<LightCycleComponent>(layers[0]);
            Assert.That(transitLight.Offset, Is.EqualTo(groundLight.Offset));
            Assert.That(transitLight.Duration, Is.EqualTo(groundLight.Duration));
            Assert.That(em.TryGetComponent<WeatherComponent>(layers.Last(), out var orbit) && orbit.Weather.Count > 0, Is.False);
            Assert.That(em.GetComponent<WFPlanetEnvironmentComponent>(layers.Last()).Weather,
                Is.EqualTo(em.GetComponent<WFPlanetEnvironmentComponent>(layers[0]).Weather));
        });

        // A storm is up to three phases; each forced change moves it one on, and the last lands on clear skies.
        for (var step = 0; step < 3; step++)
        {
            var clear = false;
            await server.WaitPost(() =>
            {
                var em = server.EntMan;
                var state = em.GetComponent<WFPlanetWeatherComponent>(em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid);
                clear = state.Current == null;
                if (!clear)
                    state.NextChange = TimeSpan.Zero;
            });

            if (clear)
                break;

            await server.WaitRunTicks(pair.SecondsToTicks(3f));
        }

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            Assert.That(em.GetComponent<WFPlanetWeatherComponent>(network).Current, Is.Null);
        });
        // Stock weather fades for 15 seconds, then fully disappears on every affected map.
        await server.WaitRunTicks(pair.SecondsToTicks(17f));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<WeatherComponent>(layers[0]).Weather, Is.Empty);
            server.EntMan.DeleteEntity(transit.MapUid);
        });
        await Teardown(pair, layers);
        await SetWeather(pair, false);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReportSharesClockAcrossAltitudeWrapsAndReadsActualWeather()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var outside = await pair.CreateTestMap();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var system = server.System<WFPlanetWeatherSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            // Asclepiu starts at 08:00; 41 real minutes advance 16:24 local hours, crossing midnight.
            state.Epoch = now - TimeSpan.FromMinutes(41);
            foreach (var layer in layers)
            {
                Assert.That(system.TryGetReport(layer, out var planet, out var time, out var weather), Is.True);
                Assert.That(planet, Is.EqualTo("Asclepiu"));
                Assert.That(time, Is.EqualTo("00:24"));
                Assert.That(weather, Is.EqualTo("Clear"));
            }
            var proto = server.ResolveDependency<IPrototypeManager>();
            var rainId = "WFBloodRain";
            server.System<Content.Server.Weather.WeatherSystem>().SetWeather(
                em.GetComponent<MapComponent>(layers[0]).MapId, proto.Index<WeatherPrototype>(rainId), null);
            Assert.That(system.TryGetReport(layers.Last(), out _, out _, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo("Blood rain"), "Reports admin changes, not only the scheduler.");
            Assert.That(system.TryGetReport(outside.MapUid, out _, out _, out _), Is.False);
            em.DeleteEntity(outside.MapUid);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeatherOffKeepsEveryWorldClear()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).NextChange = TimeSpan.Zero;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            Assert.That(em.GetComponent<WFPlanetWeatherComponent>(network).Current, Is.Null, "Weather came while it is switched off.");
            Assert.That(em.TryGetComponent<WeatherComponent>(layers[0], out var ground) && ground.Weather.Count > 0, Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>An admin's weather shows and holds for its time while the cycle is off, then the world goes clear.</summary>
    [Test]
    public async Task AdminWeatherHoldsWhileTheCycleIsOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var weathers = server.System<WFPlanetWeatherSystem>();
        EntityUid network = default;
        ProtoId<WeatherPrototype> rain = default;
        await server.WaitPost(() =>
        {
            network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
            rain = server.ResolveDependency<IPrototypeManager>().Index(state.Profile).Storms[0].Main;
            Assert.That(weathers.SetWeather(network, rain, TimeSpan.FromSeconds(5)), Is.True);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() =>
        {
            var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
            Assert.That(state.Current, Is.EqualTo(rain), "The cycle being off cleared an admin's weather.");
            Assert.That(server.EntMan.TryGetComponent<WolfWeatherMapComponent>(layers[0], out var wolf)
                && SharedWolfWeatherSystem.GetCurrent(wolf)?.Weather == weathers.ResolveWolfWeather(state.Profile, rain), Is.True,
                "The admin's weather isn't shown on the ground.");
        });
        await server.WaitRunTicks(pair.SecondsToTicks(5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).Current, Is.Null, "The admin's weather outlasted its time.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Turns planet weather on or off; it is off by default.</summary>
    private static async Task SetWeather(TestPair pair, bool on)
    {
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, on));
    }
}
