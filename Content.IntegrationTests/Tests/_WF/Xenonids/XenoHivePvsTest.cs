#nullable enable
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Hive;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The hive entity lives in nullspace, so the server force-sends it to its xenos' clients.</summary>
[TestFixture]
public sealed class XenoHivePvsTest
{
    /// <summary>A queen's client receives her hive and can place a hive core node on the weeds beside her.</summary>
    [Test]
    public async Task ClientReceivesHiveAndPredictsOrder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntities = server.EntMan;
        var cEntities = client.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid hive = default, queen = default;
        EntityCoordinates target = default;
        await server.WaitPost(() =>
        {
            var hives = sEntities.System<SharedXenoHiveSystem>();
            var maps = sEntities.System<SharedMapSystem>();
            maps.SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            target = new EntityCoordinates(map.Grid, 1.5f, 0.5f);

            hive = sEntities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            hives.SetHive(sEntities.SpawnEntity("XenoWeeds", target), hive);
            hives.SetHive(sEntities.SpawnEntity("XenoWeeds", new EntityCoordinates(map.Grid, 0.5f, 0.5f)), hive);

            queen = sEntities.SpawnEntity("CMXenoQueen", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            hives.SetHive(queen, hive);
            server.PlayerMan.SetAttachedEntity(pair.Player!, queen);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var netHive = sEntities.GetNetEntity(hive);
            Assert.That(cEntities.TryGetEntity(netHive, out var cHive), Is.True, "The client should know its hive entity.");
            Assert.That(cEntities.HasComponent<HiveComponent>(cHive), "The client's hive entity should carry the hive component.");

            var cQueen = cEntities.GetEntity(sEntities.GetNetEntity(queen));
            var cTarget = cEntities.GetCoordinates(sEntities.GetNetCoordinates(target));
            var construction = cEntities.GetComponent<XenoConstructionComponent>(cQueen);
            Assert.That(cEntities.System<SharedXenoConstructionSystem>()
                    .CanOrderConstructionPopup((cQueen, construction), cTarget, "HiveCoreXenoConstructionNode", false),
                Is.True, "The client should predict the hive core node as placeable on weeds.");
        });

        await pair.CleanReturnAsync();
    }
}
