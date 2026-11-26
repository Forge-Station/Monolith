#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hive;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Every xeno caste and strain spawns into a hive with its actions and sits in the Xenonids spawn category.</summary>
[TestFixture]
public sealed class XenoRosterTest
{
    private const string Category = "WFXenonids";

    [Test]
    public async Task EveryCasteSpawnsWithHiveActionsAndCategory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        var xenoName = factory.GetComponentName<XenoComponent>();
        var roster = new List<EntityPrototype>();
        foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract ||
                !(proto.ID.StartsWith("CMXeno") || proto.ID.StartsWith("RMCXeno")) ||
                !proto.Components.ContainsKey(xenoName))
            {
                continue;
            }

            roster.Add(proto);
        }

        Assert.That(roster.Count, Is.GreaterThan(20), "The roster should hold every caste and strain.");
        TestContext.Out.WriteLine($"Spawning {roster.Count} xeno prototypes.");

        await server.WaitAssertion(() =>
        {
            foreach (var proto in roster)
            {
                Assert.That(proto.Categories.Any(c => c.ID == Category), $"{proto.ID} should be in the {Category} category.");

                var uid = entities.SpawnEntity(proto.ID, map.GridCoords);
                Assert.That(entities.TryGetComponent(uid, out XenoComponent? xeno), $"{proto.ID} should have XenoComponent.");
                Assert.That(hives.GetHive(uid), Is.Not.Null, $"{proto.ID} should be in a hive.");

                var actionIds = xeno!.ActionIds;
                var actions = xeno.Actions;
                foreach (var actionId in actionIds)
                {
                    Assert.That(actions.TryGetValue(actionId, out var action) && entities.EntityExists(action),
                        $"{proto.ID} action {actionId} should resolve to an action entity.");
                }
            }
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }
}
