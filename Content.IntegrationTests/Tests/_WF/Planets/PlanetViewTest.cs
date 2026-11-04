#nullable enable
using System.Numerics;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// The planet's perspective, its lower layers drawn smaller, holds in the open air, on the open ground and on a city's
/// rooftop, and drops only for a viewer inside a building, under a roof on the ground or a city's level.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetView))]
public sealed class PlanetViewTest
{
    [Test]
    public async Task OnlyABuildingDropsThePerspective()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);

        try
        {
            await server.WaitAssertion(() =>
            {
                var maps = server.System<SharedMapSystem>();
                var roofs = server.System<SharedRoofSystem>();
                var tiles = server.ResolveDependency<ITileDefinitionManager>();
                var steel = new Tile(tiles[FloorTile].TileId);
                var ground = layers[0];
                var air = layers[1];
                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);

                // Looked at from where a viewer stands: in the open air, on the ground or on a grid.
                float Shrink(EntityUid parent, Vector2 position)
                {
                    var viewer = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(parent, position));
                    var shrink = WFPlanetView.Shrink(entMan, ground, viewer);
                    entMan.DeleteEntity(viewer);
                    return shrink;
                }

                var house = new Vector2i(20, 20);
                maps.SetTile(ground, groundGrid, house, steel);
                entMan.EnsureComponent<RoofComponent>(ground);

                // A city's level on the air layer above: one roofed room and its open rooftop.
                var city = maps.CreateGridEntity(entMan.GetComponent<MapComponent>(air).MapId);
                city.Comp.CanSplit = false;
                entMan.AddComponent<WFPlanetCityGridComponent>(city);
                entMan.EnsureComponent<RoofComponent>(city);
                var room = new Vector2i(60, 60);
                var rooftop = room + new Vector2i(1, 0);
                maps.SetTile(city.Owner, city.Comp, room, steel);
                maps.SetTile(city.Owner, city.Comp, rooftop, steel);
                roofs.SetRoof((city.Owner, city.Comp, null), room, true);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(Shrink(air, new Vector2(-40.5f, -40.5f)), Is.EqualTo(WFPlanetView.LayerShrink),
                        "In the open air the ground is drawn full size.");
                    Assert.That(Shrink(ground, house + new Vector2(0.5f)), Is.EqualTo(WFPlanetView.LayerShrink),
                        "On the open ground the cavern is drawn full size.");
                    Assert.That(Shrink(city, rooftop + new Vector2(0.5f)), Is.EqualTo(WFPlanetView.LayerShrink),
                        "On a city's open rooftop the street is drawn full size.");
                    Assert.That(Shrink(city, room + new Vector2(0.5f)), Is.EqualTo(1f),
                        "Inside a city's upper floor the floors below are drawn small.");
                }

                roofs.SetRoof((ground, groundGrid, null), house, true);
                Assert.That(Shrink(ground, house + new Vector2(0.5f)), Is.EqualTo(1f),
                    "Inside a house on the ground its basement is drawn small.");

                entMan.DeleteEntity(city);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
