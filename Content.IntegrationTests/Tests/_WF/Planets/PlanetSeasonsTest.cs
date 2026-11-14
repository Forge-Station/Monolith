#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Planets;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Administration;
using Content.Server._WF.Planets.Forecast;
using Content.Shared.Atmos;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Planets.Administration;
using Content.Shared._WF.Planets.Forecast;
using Content.Shared.Weather;
using Robust.Server.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Seasons, the outdoor temperature, the rolled-ahead weather and the forecast console.</summary>
[TestFixture]
public sealed class PlanetSeasonsTest
{
    private const float SeasonSeconds = 5400f;

    [Test]
    public async Task EveryWorldHasItsSeasons()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            string[] Seasons(string world) => proto.Index<WFPlanetWeatherPrototype>("WFPlanetWeather" + world).Seasons
                .Select(s => s.Id).ToArray();

            Assert.That(Seasons("Asclepiu"), Is.EqualTo(new[] { "Spring", "Summer", "Autumn", "Winter" }));
            Assert.That(Seasons("Merak"), Is.EqualTo(new[] { "Dry", "Dust" }));
            Assert.That(Seasons("Aerumna"), Is.EqualTo(new[] { "Dry", "Monsoon" }));
            Assert.That(Seasons("Thrascias"), Is.EqualTo(new[] { "DeepWinter" }));
            Assert.That(Seasons("Fervidus"), Is.Empty);
            Assert.That(Seasons("Carcinoma"), Is.Empty);

            foreach (var profile in proto.EnumeratePrototypes<WFPlanetWeatherPrototype>())
            {
                foreach (var season in profile.Seasons)
                {
                    if (profile.Seasons.Count > 1)
                        Assert.That(season.Length, Is.EqualTo(SeasonSeconds), $"{profile.ID} {season.Id}");
                    foreach (var storm in season.Storms ?? new List<WFPlanetStorm>())
                    {
                        foreach (var weather in new[] { storm.Telegraph, storm.Main, storm.End })
                        {
                            if (weather is { } id)
                                Assert.That(profile.WolfWeathers.ContainsKey(id), Is.True, $"{profile.ID} {season.Id} {id} shows nothing.");
                        }
                    }
                }
            }

            // Asclepiu's year is six of its days.
            var asclepiu = proto.Index<WFPlanetWeatherPrototype>("WFPlanetWeatherAsclepiu");
            Assert.That(asclepiu.Seasons.Sum(s => s.Length), Is.EqualTo(asclepiu.DaySeconds * 6));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeasonsCycleInOrderAndPerpetualOrNoneHold()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var networks = new Dictionary<string, EntityUid>();
        await server.WaitPost(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            foreach (var name in new[] { "Asclepiu", "Thrascias", "Fervidus" })
            {
                var built = server.System<WFPlanetNetworkSystem>().BuildNetwork(proto.Index<WFPlanetSurfacePrototype>("WFSurface" + name),
                    new Vector2(networks.Count * 4000, 0), name, null);
                networks[name] = built!.Value;
            }
        });

        await server.WaitAssertion(() =>
        {
            var weather = server.System<WFPlanetWeatherSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            string? At(string world, float seconds)
            {
                Assert.That(weather.TryGetSeason(networks[world], out var season, now + TimeSpan.FromSeconds(seconds)), Is.True);
                return season?.Id;
            }

            Assert.That(weather.SetSeason(networks["Asclepiu"], "Spring"), Is.True);
            Assert.That(At("Asclepiu", 0), Is.EqualTo("Spring"));
            Assert.That(At("Asclepiu", SeasonSeconds - 1), Is.EqualTo("Spring"));
            Assert.That(At("Asclepiu", SeasonSeconds + 1), Is.EqualTo("Summer"));
            Assert.That(At("Asclepiu", SeasonSeconds * 2 + 1), Is.EqualTo("Autumn"));
            Assert.That(At("Asclepiu", SeasonSeconds * 3 + 1), Is.EqualTo("Winter"));
            Assert.That(At("Asclepiu", SeasonSeconds * 4 + 1), Is.EqualTo("Spring"), "The year should loop.");
            Assert.That(weather.SetSeason(networks["Asclepiu"], "Monsoon"), Is.False, "A season from another world was set.");

            foreach (var seconds in new[] { 0f, SeasonSeconds * 3, 1e6f })
            {
                Assert.That(At("Thrascias", seconds), Is.EqualTo("DeepWinter"));
                Assert.That(At("Fervidus", seconds), Is.Null);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            string? Shown(string world) => em.GetComponent<WFPlanetEnvironmentComponent>(
                em.GetComponent<WFPlanetNetworkComponent>(networks[world]).GroundMap).Season?.Id;
            Assert.That(Shown("Asclepiu"), Is.EqualTo("wf-planet-season-spring"));
            Assert.That(Shown("Thrascias"), Is.EqualTo("wf-planet-season-deep-winter"));
            Assert.That(Shown("Fervidus"), Is.Null);
            foreach (var network in networks.Values)
                server.System<WFPlanetNetworkSystem>().DeleteNetwork(network);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeasonPicksTheWeatherAndTheTemperature()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        await SetCycle(pair, true);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var snow = new HashSet<string> { "WFSnowLight", "WFSnowStorm", "WFFogMist", "WFFog" };

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var weather = server.System<WFPlanetWeatherSystem>();
            var climate = server.System<WFPlanetClimateSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            var profile = proto.Index(state.Profile);
            var schedule = new List<WFScheduledWeather>();

            Assert.That(weather.SetSeason(network, "Winter"), Is.True);
            weather.TryGetSchedule(network, schedule);
            Assert.That(schedule.Last().End - now, Is.GreaterThanOrEqualTo(WFPlanetWeatherSystem.ForecastHorizon),
                "The weather should be rolled three hours ahead.");
            // A storm already under way keeps its phases; everything rolled after it is the season's.
            var winter = schedule.SkipWhile(s => s.Weather != null)
                .Where(s => s.Weather != null && s.Start < now + TimeSpan.FromSeconds(SeasonSeconds)).ToList();
            Assert.That(winter, Is.Not.Empty);
            Assert.That(winter.All(s => snow.Contains(s.Weather!.Value.Id)), Is.True,
                "Winter brought " + string.Join(", ", winter.Select(s => s.Weather)));

            Assert.That(weather.SetSeason(network, "Summer"), Is.True);
            weather.TryGetSchedule(network, schedule);
            var summer = schedule.SkipWhile(s => s.Weather != null)
                .Where(s => s.Weather != null && s.Start < now + TimeSpan.FromSeconds(SeasonSeconds)).ToList();
            Assert.That(summer.Any(s => snow.Contains(s.Weather!.Value.Id)), Is.False, "Snow fell in summer.");

            // Same hour and sky, different seasons: summer is warmer than winter by the gap between their offsets.
            weather.SetMinuteOfDay(network, 5 * 60);
            var noon = now + TimeSpan.FromSeconds(1);
            var summerAir = climate.GetTarget(state, profile, noon, null);
            weather.SetSeason(network, "Winter");
            var winterAir = climate.GetTarget(state, profile, noon, null);
            var dayFactor = WFPlanetClimateSystem.DayCurve(weather.GetMinuteOfDay(state, profile, noon));
            Assert.That(summerAir - winterAir, Is.EqualTo(26f + 3f * MathF.Max(0f, dayFactor)).Within(0.01f));
            Assert.That(winterAir, Is.LessThan(Atmospherics.T0C), "Asclepiu's winter should freeze.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TemperatureFollowsTheHourAndWeatherAndReachesTheAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        EntityUid network = default;

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var weather = server.System<WFPlanetWeatherSystem>();
            var climate = server.System<WFPlanetClimateSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var timing = server.ResolveDependency<IGameTiming>();
            network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            var profile = proto.Index(state.Profile);
            weather.SetSeason(network, "Spring");

            Assert.That(WFPlanetClimateSystem.DayCurve(5 * 60), Is.EqualTo(-1f).Within(0.001f), "Coldest before dawn.");
            Assert.That(WFPlanetClimateSystem.DayCurve(15 * 60), Is.EqualTo(1f).Within(0.001f), "Warmest mid-afternoon.");
            Assert.That(WFPlanetClimateSystem.DayCurve(10 * 60), Is.EqualTo(0f).Within(0.001f));

            // Spring has no clear-sky warming, so the hour alone moves the clear-sky air by the swing either side.
            weather.SetMinuteOfDay(network, 15 * 60);
            var afternoon = climate.GetTarget(state, profile, timing.CurTime, null);
            weather.SetMinuteOfDay(network, 5 * 60);
            var dawn = climate.GetTarget(state, profile, timing.CurTime, null);
            Assert.That(afternoon - dawn, Is.EqualTo(profile.DayTemperatureSwing * 2).Within(0.05f));

            var thunder = climate.GetTarget(state, profile, timing.CurTime, new ProtoId<WeatherPrototype>("WFRainHigh"));
            Assert.That(thunder - dawn, Is.EqualTo(-4f).Within(0.01f), "A thunderstorm should cool the air.");

            // Snap to the target, as a newly built world does.
            state.Temperature = float.NaN;
            state.AppliedTemperature = float.NaN;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        float settled = 0;
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            var climate = server.System<WFPlanetClimateSystem>();
            settled = state.Temperature;
            Assert.That(state.BaseTemperature, Is.EqualTo(288.15f).Within(0.01f), "Asclepiu's base is its ground air.");
            Assert.That(climate.GetOutsideTemperature(layers[0]), Is.EqualTo(settled));
            foreach (var layer in layers.Take(layers.Count - 1))
            {
                var air = em.GetComponent<MapAtmosphereComponent>(layer).Mixture;
                Assert.That(air.Temperature, Is.EqualTo(settled).Within(WFPlanetClimateSystem.ApplyThreshold),
                    "The outdoor air did not reach the climate.");
            }

            Assert.That(em.GetComponent<MapAtmosphereComponent>(layers[^1]).Space, Is.True, "Orbit should stay space.");
            var environment = em.GetComponent<WFPlanetEnvironmentComponent>(layers[0]);
            Assert.That(environment.TemperatureCelsius, Is.EqualTo((int) MathF.Round(settled - Atmospherics.T0C)));
            Assert.That(em.GetComponent<WFPlanetEnvironmentComponent>(layers[^1]).TemperatureCelsius, Is.Null);

            // A season change moves the air gradually, not in one jump.
            server.System<WFPlanetWeatherSystem>().SetSeason(network, "Winter");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));
        await server.WaitAssertion(() =>
        {
            var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
            Assert.That(state.Temperature, Is.LessThan(settled));
            Assert.That(settled - state.Temperature, Is.LessThanOrEqualTo(WFPlanetClimateSystem.MaxRate * 6f + 0.01f));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForecastIsWhatHappensNext()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        await SetCycle(pair, true);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        EntityUid network = default;
        var expected = new List<WFScheduledWeather>();

        // The scheduler rolls ahead on its next update once the cycle is on.
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitPost(() =>
        {
            network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            server.System<WFPlanetWeatherSystem>().TryGetSchedule(network, expected);
        });

        for (var step = 1; step <= 5; step++)
        {
            await server.WaitPost(() => server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).NextChange = TimeSpan.Zero);
            await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
            var index = step;
            await server.WaitAssertion(() =>
            {
                var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                Assert.That(state.Current, Is.EqualTo(expected[index].Weather), $"Phase {index} was not the forecast one.");
                Assert.That(state.Phase, Is.EqualTo(expected[index].Phase));
                var length = (expected[index].End - expected[index].Start).TotalSeconds;
                Assert.That((state.NextChange - now).TotalSeconds, Is.InRange(length - 2, length + 0.01),
                    "The phase did not last as long as forecast.");
            });
        }

        // An admin's weather throws the forecast out: clear skies follow it, then a fresh roll.
        await server.WaitAssertion(() =>
        {
            var weather = server.System<WFPlanetWeatherSystem>();
            var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
            Assert.That(weather.SetWeather(network, new ProtoId<WeatherPrototype>("WFRainMed"), TimeSpan.FromSeconds(60)), Is.True);
            weather.TryGetSchedule(network, expected);
            Assert.That(expected[0].Weather?.Id, Is.EqualTo("WFRainMed"));
            Assert.That(expected[1].Weather, Is.Null, "Clear skies should follow an admin's weather.");
            Assert.That(expected[1].Start, Is.EqualTo(state.NextChange));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForecastConsoleShowsTheWorldsWeather()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        await SetCycle(pair, true);
        var layers = await BuildStandalone(pair);
        var map = await pair.CreateTestMap();
        var server = pair.Server;

        await server.WaitRunTicks(pair.SecondsToTicks(2.5f));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var console = em.SpawnEntity("WFComputerWeatherForecast", map.GridCoords);
            var comp = em.GetComponent<WFWeatherForecastConsoleComponent>(console);
            var forecast = server.System<WFWeatherForecastConsoleSystem>();
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            var now = server.ResolveDependency<IGameTiming>().CurTime;

            var ui = forecast.BuildState((console, comp));
            Assert.That(ui.Worlds.Select(w => w.Name), Does.Contain("Asclepiu"));
            Assert.That(ui.Selected, Is.Not.Null);
            comp.Selected = em.GetNetEntity(network);
            ui = forecast.BuildState((console, comp));
            Assert.That(ui.Selected, Is.EqualTo(em.GetNetEntity(network)));
            Assert.That(ui.Cycling, Is.True);
            Assert.That(ui.Season, Is.Not.Null, "Asclepiu has seasons.");
            Assert.That(ui.Rows.Count, Is.GreaterThan(2));
            Assert.That(ui.Rows[0].Weather, Is.EqualTo(state.Current?.Id ?? string.Empty));
            Assert.That(ui.Rows[0].Celsius, Is.EqualTo((int) MathF.Round(state.Temperature - Atmospherics.T0C)));
            Assert.That(ui.Rows[1].Certainty, Is.EqualTo(WFForecastCertainty.Exact));
            Assert.That(ui.Rows[1].InSeconds, Is.EqualTo((float) (state.NextChange - now).TotalSeconds).Within(0.01f));
            Assert.That(ui.Rows[1].Weather, Is.EqualTo(state.Upcoming[0].Weather?.Id ?? string.Empty).Or.EqualTo(
                state.Upcoming.FirstOrDefault(p => p.Phase == WFStormPhase.Main)?.Weather?.Id),
                "The next row should be the next rolled phase.");
            Assert.That(ui.Rows.Last().Certainty, Is.EqualTo(WFForecastCertainty.Possible), "Far rows should be vague.");
            Assert.That(ui.Rows.Zip(ui.Rows.Skip(1)).All(p => p.First.InSeconds <= p.Second.InSeconds), Is.True);
            em.DeleteEntity(console);
        });

        await SetCycle(pair, false);
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminSetsTheSeason()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var (layers, body, bodyMap) = await BuildOwnedStack(pair);
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var control = server.System<PlanetControlSystem>();
            var weather = server.System<WFPlanetWeatherSystem>();
            var network = em.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;

            var summary = control.Apply(body, new PlanetControlSetEvent { Planet = em.GetNetEntity(body), Season = "Autumn" }, null);
            Assert.That(summary, Does.Contain("season=Autumn"));
            Assert.That(weather.TryGetSeason(network, out var season), Is.True);
            Assert.That(season?.Id, Is.EqualTo("Autumn"));

            var info = control.BuildList().Single(p => p.Planet == em.GetNetEntity(body));
            Assert.That(info.Season, Is.EqualTo("Autumn"));
            Assert.That(info.SeasonIds, Is.EqualTo(new[] { "Spring", "Summer", "Autumn", "Winter" }));
            Assert.That(info.SeasonSecondsLeft, Is.EqualTo(SeasonSeconds).Within(1f));

            em.System<MetaDataSystem>().SetEntityName(body, "Testworld");
            server.ResolveDependency<IServerConsoleHost>().ExecuteCommand(null, "planetcontrol Testworld season Winter");
            weather.TryGetSeason(network, out season);
            Assert.That(season?.Id, Is.EqualTo("Winter"), "The console command did not set the season.");
        });

        await Teardown(pair, layers);
        await server.WaitPost(() => server.EntMan.DeleteEntity(bodyMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>An admin's winter blizzard is cold at once, not minutes later once the air has eased down to it.</summary>
    [Test]
    public async Task AdminBlizzardIsColdAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var (layers, body, bodyMap) = await BuildOwnedStack(pair);
        var server = pair.Server;
        var network = EntityUid.Invalid;

        // A warm start: the world has already settled on its air.
        await server.WaitPost(() =>
        {
            network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            server.System<WFPlanetWeatherSystem>().SetSeason(network, "Summer");
            server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).Temperature = float.NaN;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        var warm = 0f;
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            warm = em.GetComponent<WFPlanetWeatherComponent>(network).Temperature;
            Assert.That(warm, Is.GreaterThan(Atmospherics.T0C + 5f), "Precondition: a summer world starts warm.");

            var summary = server.System<PlanetControlSystem>().Apply(body, new PlanetControlSetEvent
            {
                Planet = em.GetNetEntity(body),
                Season = "Winter",
                SetWeather = true,
                Weather = "WFSnowStorm",
            }, null);
            Assert.That(summary, Does.Contain("season=Winter"));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var state = em.GetComponent<WFPlanetWeatherComponent>(network);
            var climate = server.System<WFPlanetClimateSystem>();
            var target = climate.GetTarget(state, server.ResolveDependency<IPrototypeManager>().Index(state.Profile),
                server.ResolveDependency<IGameTiming>().CurTime, "WFSnowStorm");

            Assert.That(state.Temperature, Is.EqualTo(target).Within(WFPlanetClimateSystem.ApplyThreshold),
                "The air did not jump to the blizzard's target.");
            Assert.That(state.Temperature, Is.LessThan(Atmospherics.T0C), "A winter blizzard should be below freezing.");
            Assert.That(em.GetComponent<MapAtmosphereComponent>(layers[0]).Mixture.Temperature, Is.LessThan(Atmospherics.T0C),
                "The ground's air is still warm, so the snow would melt.");
            Assert.That(em.GetComponent<WFPlanetEnvironmentComponent>(layers[0]).TemperatureCelsius, Is.LessThan(0));
        });

        await Teardown(pair, layers);
        await server.WaitPost(() => server.EntMan.DeleteEntity(bodyMap));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HudShowsTheSeasonAndTemperature()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await AttachViewer(pair, layers[0], Vector2.Zero);
        var server = pair.Server;
        var client = pair.Client;
        var celsius = 0;

        await server.WaitPost(() =>
        {
            var network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            server.System<WFPlanetWeatherSystem>().SetSeason(network, "Winter");
            // Settle at once, so the reading doesn't drift across a degree while the client catches up.
            server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).Temperature = float.NaN;
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3f));
        await server.WaitPost(() =>
            celsius = server.EntMan.GetComponent<WFPlanetEnvironmentComponent>(layers[0]).TemperatureCelsius ?? -999);
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var ground = em.GetEntity(server.EntMan.GetNetEntity(layers[0]));
            var environment = em.GetComponent<WFPlanetEnvironmentComponent>(ground);
            Assert.That(environment.Season?.Id, Is.EqualTo("wf-planet-season-winter"));
            Assert.That(environment.TemperatureCelsius, Is.EqualTo(celsius));
            var hud = client.System<WFPlanetTimepieceHudSystem>();
            Assert.That(hud.DisplayedClimate, Is.EqualTo($"Winter · Outside {celsius}°C"));
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task SetCycle(TestPair pair, bool on)
    {
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, on));
    }
}
