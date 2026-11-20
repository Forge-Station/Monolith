#nullable enable
using System.Numerics;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared._WF.Xenonids.Rest;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The xeno core: a larva joins a hive, regains plasma on its hive's weeds and can rest.</summary>
[TestFixture]
public sealed class XenoCoreTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CMXenoLarva
  id: WFTestXenoLarvaWeedPlasma
  components:
  - type: XenoRegen
    healOffWeeds: false
  - type: XenoPlasma
    plasma: 0
    maxPlasma: 100
    plasmaRegenOnWeeds: 10
    plasmaRegenOffWeeds: 0
";

    /// <summary>The larva and hive prototypes load, and a larva put in a hive is a xeno hive member.</summary>
    [Test]
    public async Task LarvaJoinsHive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var larva = entities.SpawnEntity("CMXenoLarva", map.GridCoords);
            hives.SetHive(larva, hive);

            Assert.That(entities.HasComponent<XenoComponent>(larva), "A larva is a xeno.");
            Assert.That(entities.TryGetComponent<HiveMemberComponent>(larva, out var member), "Joining a hive makes it a member.");
            Assert.That(member!.Hive, Is.EqualTo(hive));
            Assert.That(entities.HasComponent<HiveComponent>(hive));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A larva on its hive's weeds regains plasma much faster than one off weeds.</summary>
    [Test]
    public async Task PlasmaRegeneratesOnWeeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid onWeeds = default;
        EntityUid offWeeds = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var weeds = entities.SpawnEntity("XenoWeeds", map.GridCoords);
            hives.SetHive(weeds, hive);

            onWeeds = entities.SpawnEntity("WFTestXenoLarvaWeedPlasma", map.GridCoords);
            hives.SetHive(onWeeds, hive);

            offWeeds = entities.SpawnEntity("WFTestXenoLarvaWeedPlasma", new MapCoordinates(map.MapCoords.Position + new Vector2(20, 20), map.MapId));
            hives.SetHive(offWeeds, hive);
        });

        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var on = entities.GetComponent<XenoPlasmaComponent>(onWeeds).Plasma;
            var off = entities.GetComponent<XenoPlasmaComponent>(offWeeds).Plasma;
            Assert.That(on, Is.GreaterThanOrEqualTo(FixedPoint2.New(1)), "Plasma should regenerate on friendly weeds.");
            Assert.That(off, Is.LessThan(FixedPoint2.New(0.5)), "Plasma off weeds should regenerate far slower.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The rest action puts a larva to rest and a second use stands it back up.</summary>
    [Test]
    public async Task RestToggles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var rest = entities.System<XenoRestSystem>();
        var map = await pair.CreateTestMap();

        EntityUid larva = default;
        await server.WaitPost(() => larva = entities.SpawnEntity("CMXenoLarva", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(rest.IsResting(larva), Is.False);
            Assert.That(rest.TryRestAction(larva, ignoreCooldown: true), "The larva should have a rest action.");
            Assert.That(rest.IsResting(larva), "Resting should start.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(rest.TryRestAction(larva, ignoreCooldown: true));
            Assert.That(rest.IsResting(larva), Is.False, "A second use should stop resting.");
        });

        await pair.CleanReturnAsync();
    }
}
