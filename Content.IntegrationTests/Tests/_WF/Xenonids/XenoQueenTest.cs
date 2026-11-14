#nullable enable
using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Eye;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared._WF.Xenonids.Screech;
using Content.Shared._WF.Xenonids.SpitToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The queen and the command castes: hive queen registration, screech, spit toggle and the queen's eye.</summary>
[TestFixture]
public sealed class XenoQueenTest
{
    /// <summary>A queen spawned into a hive becomes the hive's queen.</summary>
    [Test]
    public async Task QueenBecomesHiveQueen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            var queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            hives.SetHive(queen, hive);

            Assert.That(entities.HasComponent<XenoComponent>(queen), "A queen is a xeno.");
            Assert.That(entities.GetComponent<HiveComponent>(hive).CurrentQueen, Is.EqualTo(queen), "The queen should be the hive's queen.");
            Assert.That(entities.HasComponent<PressureImmunityComponent>(queen), "Pressure doesn't hurt the queen.");
            Assert.That(entities.GetComponent<MovedByPressureComponent>(queen).Enabled, Is.False, "Pressure doesn't throw the queen.");
            var king = entities.SpawnEntity("RMCXenoKing", map.GridCoords);
            Assert.That(entities.HasComponent<PressureImmunityComponent>(king), "Pressure doesn't hurt the king.");
            Assert.That(entities.GetComponent<MovedByPressureComponent>(king).Enabled, Is.False, "Pressure doesn't throw the king.");
            var drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            Assert.That(entities.HasComponent<PressureImmunityComponent>(drone), Is.False, "Other castes keep RMC's pressure rules.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The king, hivelord and carrier prototypes spawn as xenos.</summary>
    [Test]
    public async Task CommandCastesSpawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "RMCXenoKing", "CMXenoHivelord", "CMXenoCarrier", "CMXenoHivelordDesigner", "RMCXenoCarrierEggsac" })
            {
                var ent = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.HasComponent<XenoComponent>(ent), $"{id} should be a xeno.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A queen screeches, spends plasma, and toggles her spit between neurotoxin and acid.</summary>
    [Test]
    public async Task QueenScreechesAndTogglesSpit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid queen = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            hives.SetHive(queen, hive);

            // Maturing normally grants these.
            entities.EnsureComponent<XenoScreechComponent>(queen);
            entities.EnsureComponent<XenoToggleSpitComponent>(queen);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var before = entities.GetComponent<XenoPlasmaComponent>(queen).Plasma;
            var screech = new XenoScreechActionEvent { Performer = queen };
            entities.EventBus.RaiseLocalEvent(queen, screech);
            Assert.That(screech.Handled, "The screech should go off.");
            Assert.That(entities.GetComponent<XenoPlasmaComponent>(queen).Plasma, Is.LessThan(before), "Screeching should spend plasma.");

            var toggle = new XenoSpitToggleActionEvent { Performer = queen };
            entities.EventBus.RaiseLocalEvent(queen, toggle);
            Assert.That(toggle.Handled, "Toggling the spit type should be handled.");
            Assert.That(entities.GetComponent<XenoToggleSpitComponent>(queen).UseAcid, Is.True);
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>The queen's eye action spawns an eye entity and a second use removes it.</summary>
    [Test]
    public async Task QueenEyeToggles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var map = await pair.CreateTestMap();

        EntityUid queen = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            hives.SetHive(queen, hive);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var open = new QueenEyeActionEvent { Performer = queen };
            entities.EventBus.RaiseLocalEvent(queen, open);
            var eye = entities.GetComponent<QueenEyeActionComponent>(queen).Eye;
            Assert.That(eye, Is.Not.Null, "The eye should be spawned.");
            Assert.That(entities.HasComponent<QueenEyeComponent>(eye!.Value));

            var close = new QueenEyeActionEvent { Performer = queen };
            entities.EventBus.RaiseLocalEvent(queen, close);
            Assert.That(entities.GetComponent<QueenEyeActionComponent>(queen).Eye, Is.Null, "A second use should close the eye.");
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }
}
