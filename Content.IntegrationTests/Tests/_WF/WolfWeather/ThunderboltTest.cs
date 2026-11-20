#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.WolfWeather;
using Content.IntegrationTests.Pair;
using Content.Server._WF.WolfWeather;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.WolfWeather;

/// <summary>Every visible bolt of lightning draws the thunderbolt: strikes on the server, far flashes on the client.</summary>
[TestFixture]
public sealed class ThunderboltTest
{
    private const string OpenTile = "FloorPlanetGrass";
    private const int Half = 12;

    [Test]
    public async Task EveryStormWithLightningHasABolt()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "WFStorm", "WFRainHeavy", "WFThunderstorm", "WFBloodStorm" })
            {
                var lightning = proto.Index<WolfWeatherPrototype>(id).Lightning;
                Assert.That(lightning, Is.Not.Null, id);
                Assert.That(lightning!.Bolt?.Id, Is.EqualTo("WFThunderbolt"), $"{id} flashes without a bolt.");
            }

            foreach (var weather in proto.EnumeratePrototypes<WolfWeatherPrototype>())
            {
                if (weather.Lightning is { } lightning)
                    Assert.That(lightning.Bolt, Is.Not.Null, $"{weather.ID} flashes without a bolt.");
            }

            Assert.That(proto.HasIndex<EntityPrototype>(WolfThunderboltSystem.FarBolt), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StrikeSpawnsTheBolt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            LayOpenGround(pair, map.Grid);
            var coords = new EntityCoordinates(map.Grid, 2.5f, 2.5f);
            server.System<WolfLightningSystem>().Strike(coords, new WolfWeatherLightning(), Filter.Empty());

            var bolts = em.AllEntities<MetaDataComponent>()
                .Where(e => e.Comp.EntityPrototype?.ID == "WFThunderbolt")
                .ToList();
            Assert.That(bolts, Has.Count.EqualTo(1), "A strike should drop exactly one bolt.");
            var at = em.GetComponent<TransformComponent>(bolts[0]).Coordinates;
            Assert.That(at.EntityId, Is.EqualTo(map.Grid.Owner));
            Assert.That((at.Position - coords.Position).Length(), Is.LessThan(0.01f));
            em.DeleteEntity(bolts[0]);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FarFlashDrawsABoltInView()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            LayOpenGround(pair, map.Grid);
            mob = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });
        await pair.RunTicksSync(5);

        // The server's far flash, as WolfLightningSystem sends it.
        await server.WaitPost(() => server.EntMan.EntityNetManager.SendSystemNetworkMessage(new WolfLightningEvent
        {
            Map = server.EntMan.GetNetEntity(map.MapUid),
            Weather = "WFRainHeavy",
            Strike = false,
        }));
        await pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var bolts = em.AllEntities<MetaDataComponent>()
                .Where(e => e.Comp.EntityPrototype?.ID == WolfThunderboltSystem.FarBolt.Id)
                .ToList();
            Assert.That(bolts, Has.Count.EqualTo(1), "A far flash drew no bolt.");
            Assert.That(em.IsClientSide(bolts[0]), Is.True, "The far bolt should be the client's own.");
            var player = em.GetComponent<TransformComponent>(
                client.ResolveDependency<Robust.Client.Player.IPlayerManager>().LocalEntity!.Value);
            var bolt = em.GetComponent<TransformComponent>(bolts[0]);
            Assert.That(bolt.MapUid, Is.EqualTo(player.MapUid));
            Assert.That((bolt.Coordinates.Position - player.Coordinates.Position).Length(), Is.LessThanOrEqualTo(8.5f),
                "The far bolt landed out of view.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(mob));
        await pair.CleanReturnAsync();
    }

    private static void LayOpenGround(TestPair pair, Entity<Robust.Shared.Map.Components.MapGridComponent> grid)
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
    }
}
