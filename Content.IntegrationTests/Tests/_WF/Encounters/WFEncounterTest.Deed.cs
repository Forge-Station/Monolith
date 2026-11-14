#nullable enable
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    private static ProtoId<WFEncounterPrototype> WFTestStoryRivalProto => "WFTestStoryRival";

    /// <summary>
    /// An encounter ship's captain drops its deed when he dies; used, it cuts the ship loose from its encounter, which
    /// then never removes it, even when ended.
    /// </summary>
    [Test]
    public async Task CaptainsDeedMakesHisShipPermanent()
    {
        EntityUid encounter = default, ship = default, captain = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFTestStoryRivalProto);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-45000, -45000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["rival"].Grid;

            var carriers = 0;
            var query = SEntMan.EntityQueryEnumerator<WFEncounterDeedDropComponent>();
            while (query.MoveNext(out var uid, out var drop))
            {
                if (drop.Ship != ship)
                    continue;
                carriers++;
                captain = uid;
            }

            Assert.That(carriers, Is.EqualTo(1), "One crewman carries the deed.");
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(captain).Role, Is.EqualTo(WFCrewRoles.Captain), "The captain carries it.");
            Server.System<MobStateSystem>().ChangeMobState(captain, MobState.Dead);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            EntityUid? deed = null;
            var query = SEntMan.EntityQueryEnumerator<WFEncounterDeedComponent>();
            while (query.MoveNext(out var uid, out var held))
            {
                if (held.Ship == ship)
                    deed = uid;
            }

            Assert.That(deed, Is.Not.Null, "The captain dropped the deed.");
            var use = new UseInHandEvent(SEntMan.GetEntity(Player));
            SEntMan.EventBus.RaiseLocalEvent(deed!.Value, use);
            Assert.That(use.Handled, Is.True);

            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Ships.ContainsKey("rival"), Is.False, "The encounter lets the ship go.");
            Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Completed), "With its only ship gone, the encounter is over.");
            Assert.That(SEntMan.HasComponent<WFEncounterGridComponent>(ship), Is.False, "The ship belongs to no encounter.");
            Assert.That(SEntMan.HasComponent<CleanupImmuneComponent>(ship), Is.True, "No cleanup takes it.");

            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.True, "Ending the encounter leaves the ship.");
            SEntMan.DeleteEntity(ship);
        });
    }
}
