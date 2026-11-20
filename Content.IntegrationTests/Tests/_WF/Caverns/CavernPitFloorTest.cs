#nullable enable
using Content.Client._WF.Caverns;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>A hole's lip shows the floor round it, whatever that floor is, and follows it when it is retiled.</summary>
[TestFixture]
[TestOf(typeof(WFCavernShadeVisualsSystem))]
public sealed class CavernPitFloorTest
{
    private const string Shade = "WFCavernShadeMerak";
    private const string Asphalt = "FloorAsphalt";
    private const string Sand = "WFFloorMerakGlassSand";

    /// <summary>A shade cut into asphalt lays asphalt under its lip; retiled to sand round it, it lays sand.</summary>
    [Test]
    public async Task LipTakesTheFloorRoundIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var serverDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientDefs = client.ResolveDependency<ITileDefinitionManager>();
        var mapSys = server.System<SharedMapSystem>();
        var asphalt = serverDefs[Asphalt].TileId;
        var sand = serverDefs[Sand].TileId;
        EntityUid shade = default;
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        // A planet's ground is its map's own grid, so an unanchored shade over an empty tile stays on it.
        // Asphalt all round an empty tile, but for one sand tile the majority outweighs.
        await server.WaitPost(() =>
        {
            var map = mapSys.CreateMap(out _);
            gridUid = map;
            gridComp = server.EntMan.EnsureComponent<MapGridComponent>(map);

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var tile = dx == 0 && dy == 0 ? Tile.Empty : new Tile(dx == 1 && dy == 1 ? sand : asphalt);
                    mapSys.SetTile(gridUid, gridComp, new Vector2i(dx, dy), tile);
                }
            }

            shade = server.EntMan.SpawnEntity(Shade, new EntityCoordinates(gridUid, 0.5f, 0.5f));
        });
        await pair.RunTicksSync(10);

        await AssertFloor(pair, clientDefs, shade, Asphalt);

        await server.WaitPost(() =>
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx != 0 || dy != 0)
                        mapSys.SetTile(gridUid, gridComp, new Vector2i(dx, dy), new Tile(sand));
                }
            }
        });
        await pair.RunTicksSync(10);

        await AssertFloor(pair, clientDefs, shade, Sand);

        await server.WaitPost(() => server.EntMan.DeleteEntity(gridUid));
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull over a hole hides its lip, which would otherwise lie across the deck; it shows again once the hull has gone.</summary>
    [Test]
    public async Task LipHidesUnderAHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var mapSys = server.System<SharedMapSystem>();
        var asphalt = server.ResolveDependency<ITileDefinitionManager>()[Asphalt].TileId;
        EntityUid shade = default;
        EntityUid ground = default;
        EntityUid hull = default;

        await server.WaitPost(() =>
        {
            ground = mapSys.CreateMap(out var mapId);
            var groundGrid = server.EntMan.EnsureComponent<MapGridComponent>(ground);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    mapSys.SetTile(ground, groundGrid, new Vector2i(dx, dy), dx == 0 && dy == 0 ? Tile.Empty : new Tile(asphalt));
                }
            }

            shade = server.EntMan.SpawnEntity(Shade, new EntityCoordinates(ground, 0.5f, 0.5f));

            var created = mapSys.CreateGridEntity(mapId);
            hull = created.Owner;
            mapSys.SetTile(hull, created.Comp, Vector2i.Zero, new Tile(asphalt));
            server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(ground, 40f, 40f));
        });
        await pair.RunTicksSync(30);

        await AssertShown(pair, shade, true);

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(ground, 0f, 0f)));
        await pair.RunTicksSync(30);

        await AssertShown(pair, shade, false);

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(ground, 40f, 40f)));
        await pair.RunTicksSync(30);

        await AssertShown(pair, shade, true);

        await server.WaitPost(() => server.EntMan.DeleteEntity(ground));
        await pair.CleanReturnAsync();
    }

    private static async Task AssertShown(Pair.TestPair pair, EntityUid serverShade, bool shown)
    {
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var sprite = client.EntMan.GetComponent<SpriteComponent>(pair.ToClientUid(serverShade));
            Assert.That(sprite.Visible, Is.EqualTo(shown), shown ? "A lip clear of the hull is hidden." : "A lip under the hull still shows.");
        });
    }

    /// <summary>On the client the shade picked the floor and each of its four corner floor layers shows it.</summary>
    private static async Task AssertFloor(Pair.TestPair pair, ITileDefinitionManager clientDefs, EntityUid serverShade, string expected)
    {
        var client = pair.Client;
        var visuals = client.System<WFCavernShadeVisualsSystem>();
        var sprites = client.System<SpriteSystem>();

        await client.WaitAssertion(() =>
        {
            var shade = pair.ToClientUid(serverShade);
            Assert.That(visuals.TryGetFloor(shade, out var floor), Is.True, "The shade picked no floor.");
            Assert.That(clientDefs[floor.TypeId].ID, Is.EqualTo(expected));

            var sprite = client.EntMan.GetComponent<SpriteComponent>(shade);
            foreach (var key in new[] { "ne", "nw", "se", "sw" })
            {
                Assert.That(sprites.LayerMapTryGet((shade, sprite), $"wf-pit-floor-{key}", out var index, false), Is.True,
                    $"No {key} floor layer.");
                var layer = (SpriteComponent.Layer) sprite[index];
                Assert.That(layer.Visible, Is.True, $"The {key} floor layer is hidden.");
                Assert.That(layer.Texture, Is.Not.Null, $"The {key} floor layer has no texture.");
            }
        });
    }
}
