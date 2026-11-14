#nullable enable
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Sensor;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The resin sensor: orderable by the queen, limited per hive, and a mass scanner only the hive can open.</summary>
[TestFixture]
public sealed class XenoSensorTest
{
    private const string Node = "HiveSensorXenoConstructionNode";
    private const string Sensor = "HiveSensorXeno";

    /// <summary>The queen and drone can order the node, which builds a limited, hive-bound sensor with a radar.</summary>
    [Test]
    public async Task NodeIsOrderableAndSensorHasRadar()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var caste in new[] { "CMXenoQueen", "CMXenoDrone" })
            {
                Assert.That(prototypes.Index<EntityPrototype>(caste).TryGetComponent(out XenoConstructionComponent? order, factory), Is.True);
                Assert.That(order!.CanOrderConstruction, Does.Contain(new EntProtoId(Node)),
                    $"{caste} should be able to order the resin sensor.");
            }

            Assert.That(prototypes.Index<EntityPrototype>(Node).TryGetComponent(out HiveConstructionNodeComponent? node, factory), Is.True);
            Assert.That(node!.Spawn.Id, Is.EqualTo(Sensor));

            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var sensor = entities.SpawnEntity(Sensor, map.GridCoords);
            hives.SetHive(sensor, hive);
            var drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            hives.SetHive(drone, hive);

            Assert.That(entities.HasComponent<RadarConsoleComponent>(sensor), "The sensor should be a radar console.");
            Assert.That(entities.GetComponent<RadarConsoleComponent>(sensor).FollowEntity, Is.True, "The radar should centre on the sensor.");
            Assert.That(entities.HasComponent<XenoSensorComponent>(sensor));
            Assert.That(hives.IsMember(sensor, hive), "The built sensor should belong to the hive.");
            Assert.That(hives.TryGetStructureLimit((hive, entities.GetComponent<HiveComponent>(hive)), Sensor, out var limit) ? limit : 0,
                Is.EqualTo(2), "A hive should be limited to two sensors.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A drone of the hive opens the sensor's scanner; a human can't, nor can a xeno of another hive.</summary>
    [Test]
    public async Task OnlyHiveXenosOpenScanner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var hives = entities.System<SharedXenoHiveSystem>();
        var ui = entities.System<SharedUserInterfaceSystem>();
        var map = await pair.CreateTestMap();

        EntityUid sensor = default, drone = default, human = default, stranger = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var other = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            sensor = entities.SpawnEntity(Sensor, map.GridCoords);
            hives.SetHive(sensor, hive);
            drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            hives.SetHive(drone, hive);
            stranger = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            hives.SetHive(stranger, other);
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
        });

        await pair.RunTicksSync(5);

        foreach (var (who, expected) in new[] { (human, false), (stranger, false), (drone, true) })
        {
            await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, who));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                entities.EventBus.RaiseLocalEvent(sensor, new ActivateInWorldEvent(who, sensor, true), true);
                var open = ui.IsUiOpen(sensor, RadarConsoleUiKey.Key, who);
                Assert.That(open, Is.EqualTo(expected), $"{entities.ToPrettyString(who)} opening the scanner: expected {expected}.");
                if (open)
                    ui.CloseUi(sensor, RadarConsoleUiKey.Key, who);
            });
        }

        await pair.CleanReturnAsync();
    }
}
