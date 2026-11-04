#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The random vehicle spawners each leave one thing of their kind, and the military wrecks draw.</summary>
[TestFixture]
public sealed class VehicleSpawnerTest
{
    private static readonly string[] Drivable =
        { "WFRandomCivilianVehicleSpawner", "WFRandomVanSpawner", "WFRandomTankSpawner", "WFRandomMilitaryVehicleSpawner" };

    private static readonly string[] Scenery =
        { "WFRandomCivilianSceneryVehicleSpawner", "WFRandomMilitarySceneryVehicleSpawner", "WFRandomVanSceneryVehicleSpawner" };

    [Test]
    public async Task EachSpawnerLeavesOneOfItsKind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var spawned = new Dictionary<string, List<EntityUid>>();
        await server.WaitPost(() =>
        {
            foreach (var spawner in Drivable.Concat(Scenery).Append("WFRandomVehiclePartSpawner"))
            {
                var before = entMan.GetEntities().ToHashSet();
                entMan.SpawnEntity(spawner, map.GridCoords);
                spawned[spawner] = entMan.GetEntities().Where(e => !before.Contains(e) && entMan.GetComponent<TransformComponent>(e).ParentUid == map.Grid.Owner).ToList();
            }
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            // The spawners delete themselves.
            foreach (var list in spawned.Values)
                list.RemoveAll(e => !entMan.EntityExists(e));

            foreach (var spawner in Drivable)
                Assert.That(spawned[spawner].Count(entMan.HasComponent<RMCVehicleComponent>), Is.EqualTo(1), $"{spawner} should leave one drivable vehicle.");
            foreach (var spawner in Scenery)
            {
                var props = spawned[spawner].Where(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID is { } id
                    && (id.StartsWith("RMCPropVehicle") || id.StartsWith("WFScenery"))).ToList();
                Assert.That(props, Has.Count.EqualTo(1), $"{spawner} should leave one scenery vehicle.");
            }
            Assert.That(spawned["WFRandomVehiclePartSpawner"].Count(entMan.HasComponent<HardpointItemComponent>), Is.EqualTo(1),
                "The part spawner should leave one vehicle part.");
        });

        // Every wreck's layers exist in its sprite, which only the client checks.
        var wrecks = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.ID.StartsWith("WFScenery") && p.ID.EndsWith("Wreck")).Select(p => p.ID).ToList();
        Assert.That(wrecks, Has.Count.GreaterThanOrEqualTo(9));
        await pair.Client.WaitPost(() =>
        {
            foreach (var wreck in wrecks)
                pair.Client.EntMan.DeleteEntity(pair.Client.EntMan.SpawnEntity(wreck, MapCoordinates.Nullspace));
        });

        await pair.CleanReturnAsync();
    }
}
