#nullable enable
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Brutalize;
using Content.Shared._WF.Xenonids.Crest;
using Content.Shared._WF.Xenonids.Dodge;
using Content.Shared._WF.Xenonids.Fortify;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The praetorian, defender and crusher lines: the castes spawn with their abilities and the crest toggles.</summary>
[TestFixture]
public sealed class XenoLineTest
{
    /// <summary>The three castes spawn as xenos with their ability components.</summary>
    [Test]
    public async Task CastesSpawnWithAbilities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var praetorian = entities.SpawnEntity("CMXenoPraetorian", map.GridCoords);
            var dancer = entities.SpawnEntity("RMCXenoPraetorianDancer", map.GridCoords);
            var defender = entities.SpawnEntity("CMXenoDefender", map.GridCoords);
            var crusher = entities.SpawnEntity("RMCXenoCrusher", map.GridCoords);
            var charger = entities.SpawnEntity("RMCXenoCrusherCharger", map.GridCoords);

            foreach (var xeno in new[] { praetorian, dancer, defender, crusher, charger })
            {
                Assert.That(entities.HasComponent<XenoComponent>(xeno), $"{entities.ToPrettyString(xeno)} is a xeno.");
            }

            Assert.That(entities.HasComponent<XenoDodgeComponent>(dancer), "The dancer dodges.");
            Assert.That(entities.HasComponent<XenoCrestComponent>(defender), "The defender has a crest.");
            Assert.That(entities.HasComponent<XenoFortifyComponent>(defender), "The defender fortifies.");
            Assert.That(entities.HasComponent<XenoBrutalizeComponent>(crusher), "The crusher brutalizes.");
            Assert.That(entities.HasComponent<ActionsComponent>(defender), "Xenos get their action list.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Using the crest action lowers the defender's crest, and using it again raises it.</summary>
    [Test]
    public async Task DefenderTogglesCrest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid defender = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            defender = entities.SpawnEntity("CMXenoDefender", map.GridCoords);
            hives.SetHive(defender, hive);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var crest = entities.GetComponent<XenoCrestComponent>(defender);
            Assert.That(crest.Lowered, Is.False, "The crest starts raised.");

            var lower = new XenoToggleCrestActionEvent { Performer = defender };
            entities.EventBus.RaiseLocalEvent(defender, lower);
            Assert.That(lower.Handled, "The toggle is handled.");
            Assert.That(entities.GetComponent<XenoCrestComponent>(defender).Lowered, Is.True, "The crest lowers.");

            var raise = new XenoToggleCrestActionEvent { Performer = defender };
            entities.EventBus.RaiseLocalEvent(defender, raise);
            Assert.That(entities.GetComponent<XenoCrestComponent>(defender).Lowered, Is.False, "The crest raises again.");
        });

        await pair.CleanReturnAsync();
    }
}
