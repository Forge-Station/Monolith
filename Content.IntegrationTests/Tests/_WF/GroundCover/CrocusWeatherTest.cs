#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.PlanetCities;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.GroundCover;
using Content.Server._WF.PlanetPois;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>
/// Crocus's streets are open to the sky and its rooms are not, and snow lies on its streets in planet frost though
/// the street's own air is whatever it was when the city was built.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class CrocusWeatherTest
{
    private const string Street = "RMCPlanetHybrisaAsphalt";
    private const string Room = "FloorWood";

    [Test]
    public async Task CrocusStreetsTakeWeatherAndRoomsDoNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = 1977 });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var atmos = server.System<AtmosphereSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await PlanetFixture.EnableFeature(pair);
        var (layers, _, holds) = await FixedCityTest.BuildCrocus(pair, false, () => server.CfgMan.SetCVar(CCVars.NPCEnabled, false));
        var ground = layers[0];
        Vector2i? street = null;
        Vector2i? room = null;

        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
            Assert.That(record.GenerationError, Is.Null);
            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True);
            var bounds = record.Sites.Single(site => site.Name == "wf-city-crocus").Bounds;
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var roof = entMan.GetComponent<RoofComponent>(ground);
            var cover = server.System<WFGroundCoverSystem>();
            var roofs = server.System<SharedRoofSystem>();

            var tiles = maps.GetAllTilesEnumerator(ground, grid);
            while (tiles.MoveNext(out var tile))
            {
                var index = tile.Value.GridIndices;
                if (index.X < bounds.Left || index.X >= bounds.Right || index.Y < bounds.Bottom || index.Y >= bounds.Top)
                    continue;

                var name = tileDefs[tile.Value.Tile.TypeId].ID;
                if (street == null && name == Street && cover.IsOpen(ground, index))
                    street = index;
                else if (room == null && name == Room && roofs.IsRooved((ground, grid, roof), index) && !Blocked(entMan, maps, (ground, grid), index))
                    room = index;
            }

            Assert.That(street, Is.Not.Null, "No Crocus street is open to the sky.");
            Assert.That(room, Is.Not.Null, "No Crocus room has a roof.");

            var wolf = server.System<SharedWolfWeatherSystem>();
            var roofed = new Entity<MapGridComponent, RoofComponent?>(ground, grid, roof);
            Assert.That(wolf.IsExposed(roofed, maps.GetTileRef(ground, grid, street!.Value)), Is.True);
            Assert.That(wolf.IsExposed(roofed, maps.GetTileRef(ground, grid, room!.Value)), Is.False, "Weather reaches into a Crocus room.");

            // A city built on a mild day keeps that air on its streets (set below); frost comes only to the planet's.
            var mapAtmos = entMan.GetComponent<MapAtmosphereComponent>(ground);
            atmos.SetMapGasMixture(ground, new GasMixture(mapAtmos.Mixture) { Temperature = 263.15f }, mapAtmos);
            wolf.SetWeather(ground, "WFBlizzard", null, 0);
        });

        // Past the blizzard's fade-in.
        await server.WaitRunTicks(pair.SecondsToTicks(16));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var weather = entMan.GetComponent<WFGroundWeatherComponent>(ground);
            foreach (var tile in new[] { street!.Value, room!.Value })
            {
                if (atmos.GetTileMixture((ground, null, null), ground, tile) is { } air)
                    air.Temperature = 288.15f;
            }

            cover.StepMap((ground, weather), 60f);
            cover.UpdateChunk(ground, WFSnowCover.ChunkOf(street.Value));
            cover.UpdateChunk(ground, WFSnowCover.ChunkOf(room.Value));

            Assert.That(atmos.GetTileMixture((ground, null, null), ground, street.Value)?.Temperature, Is.GreaterThan(Atmospherics.T0C + 5f),
                "Precondition: the street's own air isn't warm.");
            Assert.That(cover.GetDepth(ground, street.Value), Is.GreaterThan(0), "No snow settled on a Crocus street in frost.");
            Assert.That(cover.GetDepth(ground, room.Value), Is.Zero, "Snow settled in a Crocus room.");

            weather.WetnessExact = 1f;
            Assert.That(cover.GetTileWetness(ground, street.Value), Is.GreaterThan(0.9f), "Rain doesn't wet a Crocus street.");
            Assert.That(cover.GetTileWetness(ground, room.Value), Is.Zero, "Rain wets a Crocus room.");
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static bool Blocked(IEntityManager entMan, SharedMapSystem maps, Entity<MapGridComponent> grid, Vector2i tile)
    {
        return maps.GetAnchoredEntities(grid, tile).Any(uid =>
            entMan.TryGetComponent<PhysicsComponent>(uid, out var body) && body.Hard && body.CanCollide);
    }
}
