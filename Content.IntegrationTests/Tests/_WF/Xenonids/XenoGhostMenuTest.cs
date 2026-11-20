#nullable enable
using System.Linq;
using Content.Server._WF.Ghost;
using Content.Shared._WF.Ghost;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The ghost orbit menu lists player xenos in their own section, queen first.</summary>
[TestFixture]
public sealed class XenoGhostMenuTest
{
    [Test]
    public async Task XenosGetTheirOwnSection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var orbit = entities.System<GhostOrbitSystem>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var minds = entities.System<SharedMindSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions[0];

        EntityUid viewer = default;
        EntityUid drone = default;
        EntityUid queen = default;
        EntityUid mindless = default;
        EntityUid human = default;
        await server.WaitPost(() =>
        {
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            hives.ChangeBurrowedLarva((hive, entities.GetComponent<HiveComponent>(hive)), 2);

            viewer = entities.SpawnEntity("MobObserver", map.GridCoords);
            drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);
            mindless = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            human = entities.SpawnEntity("MobHuman", map.GridCoords);

            // Only one session is connected, so its mind visits each body in turn.
            minds.TransferTo(minds.CreateMind(session.UserId), drone);
        });

        await pair.RunTicksSync(5);

        int droneOrder = 0;
        await server.WaitAssertion(() =>
        {
            var targets = orbit.BuildTargets(viewer, false);
            GhostOrbitTarget Find(EntityUid uid) => targets.Single(t => t.Entity == entities.GetNetEntity(uid));

            Assert.That(Find(drone).Category, Is.EqualTo(GhostOrbitCategory.Xenonid), "A player drone is listed with the xenos.");
            Assert.That(Find(drone).Detail, Is.Not.Null.And.Not.Empty, "The caste is shown.");
            Assert.That(Find(mindless).Category, Is.EqualTo(GhostOrbitCategory.Npc), "A drone without a mind is not in the xeno section.");
            Assert.That(Find(human).Category, Is.Not.EqualTo(GhostOrbitCategory.Xenonid), "Crew stay out of the xeno section.");
            droneOrder = Find(drone).Order;
            Assert.That(hives.GetBurrowedLarva(), Is.EqualTo(2));
        });

        await server.WaitPost(() => minds.TransferTo(session.ContentData()!.Mind!.Value, queen));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var targets = orbit.BuildTargets(viewer, false);
            var entry = targets.Single(t => t.Entity == entities.GetNetEntity(queen));
            Assert.That(entry.Category, Is.EqualTo(GhostOrbitCategory.Xenonid));
            Assert.That(entry.Order, Is.LessThan(droneOrder), "The queen sorts before a drone.");
        });

        await server.WaitPost(() => minds.TransferTo(session.ContentData()!.Mind!.Value, human));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var targets = orbit.BuildTargets(viewer, false);
            Assert.That(targets.Single(t => t.Entity == entities.GetNetEntity(human)).Category, Is.EqualTo(GhostOrbitCategory.Alive));
            Assert.That(targets.Any(t => t.Category == GhostOrbitCategory.Xenonid), Is.False, "Without a player in a xeno the section is empty.");
        });

        await pair.CleanReturnAsync();
    }
}
