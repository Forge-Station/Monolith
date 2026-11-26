#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Merak's desert air is hot but liveable: its wildlife, a depot's mobs and an unsuited crewman last in it.</summary>
[TestFixture]
public sealed class MerakHeatTest
{
    private static readonly string[] Mobs =
    {
        "MobHuman",
        "MobMonkey",
        "MobLizard",
        "MobCobraSpace",
        "WFMobDesertTarantula",
        "MobFleshLoverExpeditions",
    };

    /// <summary>Ten minutes in the hottest air Merak's climate makes leaves every body short of its heat threshold.</summary>
    [Test]
    public async Task MobsOutlastTheHottestAfternoon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var air = protos.Index<PlanetPrototype>("WFMerakSurface").Atmosphere;
        var weather = protos.Index<WFPlanetWeatherPrototype>("WFPlanetWeatherMerak");

        // A summer afternoon at its peak under the hottest weather, as WFPlanetClimateSystem.GetTarget adds them up.
        var hottestWeather = weather.WeatherTemperature.Values.DefaultIfEmpty(0f).Max();
        var hottest = air.Temperature + weather.DayTemperatureSwing
            + weather.Seasons.Select(season => season.Temperature + MathF.Max(season.ClearDayTemperature, hottestWeather))
                .DefaultIfEmpty(hottestWeather).Max();

        var testMaps = new List<TestMapData>();
        var mobs = new List<EntityUid>();
        foreach (var _ in Mobs)
            testMaps.Add(await pair.CreateTestMap());

        await server.WaitPost(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            var maps = server.System<SharedMapSystem>();
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var i = 0; i < Mobs.Length; i++)
            {
                var grid = testMaps[i].Grid;
                for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), plating);

                var mix = new GasMixture(air.Volume) { Temperature = hottest };
                for (var gas = 0; gas < Atmospherics.TotalNumberOfGases; gas++)
                    mix.AdjustMoles(gas, air.GetMoles(gas));
                atmos.SetMapAtmosphere(testMaps[i].MapUid, false, mix);
                mobs.Add(em.SpawnEntity(Mobs[i], new EntityCoordinates(grid.Owner, 0.5f, 0.5f)));
            }
        });

        for (var second = 0; second < 600; second += 30)
            await server.WaitRunTicks(30 * 30);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < Mobs.Length; i++)
            {
                Assert.That(em.EntityExists(mobs[i]), Is.True, $"{Mobs[i]} was deleted.");
                if (!em.TryGetComponent<TemperatureComponent>(mobs[i], out var temperature))
                {
                    Assert.Fail($"{Mobs[i]} ({em.ToPrettyString(mobs[i])}) has no temperature.");
                    continue;
                }
                Assert.That(temperature.CurrentTemperature, Is.LessThan(temperature.HeatDamageThreshold),
                    $"{Mobs[i]} overheated in Merak's hottest air ({hottest} K).");
            }
        });

        await pair.CleanReturnAsync();
    }
}
