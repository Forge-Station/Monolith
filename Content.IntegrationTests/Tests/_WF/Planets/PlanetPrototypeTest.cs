#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Planet prototypes: each spawns and every sprite state exists.</summary>
[TestFixture]
public sealed class PlanetPrototypeTest
{
    /// <summary>Every spawnable flight prototype added.</summary>
    private static readonly string[] Prototypes =
    {
        "WFThrusterLanding",
        "WFLandingThrusterKit",
    };

    private const string HullTile = "FloorSteel";


    [Test]
    public async Task EveryPrototypeSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var spawned = await SpawnAll(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (id, uid) in spawned)
                {
                    Assert.That(entMan.EntityExists(uid), Is.True, $"{id} did not survive being spawned.");
                    Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID, Is.EqualTo(id),
                        $"{id} spawned as something else.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every sprite layer names a state its RSI actually has, borrowed sheets included.</summary>
    [Test]
    public async Task EverySpriteStateExists()
    {
        // Connected, because the sprite layers only exist on the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var clientEntMan = client.EntMan;

        var spawned = await SpawnAll(pair);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (id, serverUid) in spawned)
                {
                    var uid = pair.ToClientUid(serverUid);

                    Assert.That(clientEntMan.EntityExists(uid), Is.True, $"{id} never reached the client.");

                    if (!clientEntMan.TryGetComponent(uid, out SpriteComponent? sprite))
                        continue;

                    var layer = 0;

                    foreach (var spriteLayer in sprite.AllLayers)
                    {
                        layer++;

                        if (spriteLayer.RsiState.Name is not { } state)
                            continue;

                        Assert.That(spriteLayer.ActualRsi, Is.Not.Null,
                            $"{id} layer {layer} names state '{state}' but resolves no RSI.");
                        Assert.That(spriteLayer.ActualRsi!.TryGetState(state, out _), Is.True,
                            $"{id} layer {layer} names state '{state}', which its RSI does not have.");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns one of every new prototype, each on its own tile of a throwaway grid.</summary>
    private static async Task<Dictionary<string, EntityUid>> SpawnAll(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        var map = await pair.CreateTestMap();
        var spawned = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            var grid = map.Grid;
            var floor = new Tile(tileDefs[HullTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            // Three tiles apart, clear of each neighbour's footprint.
            for (var x = 0; x < Prototypes.Length * 3; x++)
            for (var y = 0; y < 3; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);

            for (var i = 0; i < Prototypes.Length; i++)
            {
                var coords = new EntityCoordinates(grid.Owner, new Vector2(i * 3 + 1.5f, 1.5f));
                spawned[Prototypes[i]] = entMan.SpawnEntity(Prototypes[i], coords);
            }
        });

        await server.WaitRunTicks(5);
        return spawned;
    }
}
