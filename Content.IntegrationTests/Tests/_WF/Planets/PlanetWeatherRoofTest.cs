#nullable enable
using System.Numerics;
using Content.Shared._WF.PlanetCities;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>On a planet, roofs decide where it rains rather than the tile, a vessel is always under cover and a city's level goes by its roofs.</summary>
[TestFixture]
[TestOf(typeof(SharedWeatherSystem))]
public sealed class PlanetWeatherRoofTest
{
    [Test]
    public async Task RoofsNotTilesDecideTheRain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var hull = await BuildHull(pair, entMan.GetComponent<MapComponent>(ground).MapId, new Vector2(40, 40));

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var roofs = server.System<SharedRoofSystem>();
            var weather = server.System<SharedWeatherSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var steel = new Tile(tiles[FloorTile].TileId);
            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);

            // A steel floor is no weather tile, but out on a planet it is rained on all the same, until it is roofed.
            var open = new Vector2i(-30, -30);
            maps.SetTile(ground, groundGrid, open, steel);
            Assert.That(((ContentTileDefinition) tiles[FloorTile]).Weather, Is.False);
            Assert.That(weather.CanWeatherAffect(ground, groundGrid, maps.GetTileRef(ground, groundGrid, open)), Is.True,
                "An open steel floor on a planet stayed dry.");
            entMan.EnsureComponent<RoofComponent>(ground);
            roofs.SetRoof((ground, groundGrid, null), open, true);
            Assert.That(weather.CanWeatherAffect(ground, groundGrid, maps.GetTileRef(ground, groundGrid, open)), Is.False,
                "It rained under a roof.");

            // A vessel on the planet is under cover everywhere.
            var hullGrid = entMan.GetComponent<MapGridComponent>(hull);
            var enumerator = maps.GetAllTilesEnumerator(hull, hullGrid);
            Assert.That(enumerator.MoveNext(out var hullTile), Is.True);
            Assert.That(weather.CanWeatherAffect(hull, hullGrid, hullTile!.Value), Is.False, "It rained inside a vessel.");

            // A city's level is no vessel: its open tiles are rained on.
            entMan.EnsureComponent<WFPlanetCityGridComponent>(hull);
            Assert.That(weather.CanWeatherAffect(hull, hullGrid, hullTile.Value), Is.True, "A city's open rooftop stayed dry.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
