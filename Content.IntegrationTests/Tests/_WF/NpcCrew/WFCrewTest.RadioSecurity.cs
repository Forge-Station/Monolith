#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Hidden officers learn injuries and casualties through working radios on their own ship.</summary>
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task CrewIncidentReportsRequireRadio(bool receiverEnabled, bool senderEnabled)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 11, gravity: true);
        var other = await CreateDeck(new Vector2(45, 20), 5, gravity: true);
        EntityUid victim = default, witness = default, radio = default, remote = default, attacker = default;
        await Server.WaitAssertion(() =>
        {
            for (var y = 0; y < 11; y++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(3.5f, y + 0.5f)));
            var crew = Server.System<WFCrewSystem>();
            victim = crew.SpawnCrewman(WFCrewRoles.Captain, new EntityCoordinates(deck, new Vector2(6.5f, 5.5f)), "incidents")!.Value;
            witness = crew.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(5.5f, 5.5f)), "incidents")!.Value;
            radio = crew.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(1.5f, 5.5f)), "incidents")!.Value;
            remote = crew.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(other, new Vector2(1.5f)), "incidents")!.Value;
            attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(other, new Vector2(3.5f)));
            foreach (var member in new[] { victim, witness, radio, remote })
            {
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
                SEntMan.EnsureComponent<WFCrewSecurityComponent>(member).Boarding = WFCrewSecurityResponse.Ignore;
            }
            // The attacker fires from outside both crews' decks, preventing an unrelated boarding announcement.
            Server.System<SharedTransformSystem>().SetCoordinates(attacker,
                new EntityCoordinates(MapData.MapUid, new Vector2(70, 20)));
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var inventory = Server.System<InventorySystem>();
            foreach (var member in new[] { victim, witness, radio })
            {
                Assert.That(inventory.TryGetSlotEntity(member, "ears", out var headset), Is.True);
                Server.System<HeadsetSystem>().SetEnabled(headset!.Value, member == radio ? receiverEnabled : senderEnabled);
            }
            Assert.That(Server.System<WFCrewWeaponSystem>().CanSee(radio, victim), Is.False);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", 1);
            Server.System<DamageableSystem>().TryChangeDamage(victim, damage, origin: attacker);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Sent(radio).Any(line => line.Line == WFRadioLine.Mayday), Is.EqualTo(receiverEnabled && senderEnabled));
            Assert.That(Sent(remote), Is.Empty, "A matching group name does not relay incidents between grids.");
            Server.System<MobStateSystem>().ChangeMobState(victim, MobState.Dead);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Sent(radio).Any(line => line.Line == WFRadioLine.CaptainDown), Is.EqualTo(receiverEnabled && senderEnabled));
            Assert.That(Sent(remote), Is.Empty);
        });
    }

    /// <summary>A radio officer seeing a boarder cannot inform an isolated officer through a broadcast event.</summary>
    [Test]
    public async Task BoardingWitnessDoesNotBypassReceiver()
    {
        var deck = await CreateDeck(new Vector2(20, 20), 11, gravity: true);
        EntityUid witness = default, hidden = default;
        await Server.WaitAssertion(() =>
        {
            for (var y = 0; y < 11; y++)
                SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(3.5f, y + 0.5f)));
            var crew = Server.System<WFCrewSystem>();
            witness = crew.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(5.5f)), "boarding")!.Value;
            hidden = crew.SpawnCrewman(WFCrewRoles.RadioOperator, new EntityCoordinates(deck, new Vector2(1.5f, 5.5f)), "boarding")!.Value;
            foreach (var member in new[] { witness, hidden })
            {
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
                SEntMan.EnsureComponent<WFCrewSecurityComponent>(member).Boarding = WFCrewSecurityResponse.Hostile;
            }
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(hidden, "ears", out var headset), Is.True);
            Server.System<HeadsetSystem>().SetEnabled(headset!.Value, false);
            SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 5.5f)));
        });
        // The security poll runs once a second; wait for the report, not for a fixed number of ticks.
        await WaitUntil(() => Sent(witness).Any(line => line.Line == WFRadioLine.BoardWarning), 300,
            () => "the witness never reported the boarder");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Sent(hidden), Is.Empty);
        });
    }

    /// <summary>Explicit docking hostility overrides a shared faction and clears when the policy is reset.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task GunnerHonorsHostileDockingAcrossSharedFaction(bool firedOnCrew)
    {
        var (deck, visitor, _, _) = await CreateDockingPair(gravity: true);
        EntityUid gunner = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            SEntMan.SpawnAtPosition("WFTestGunnery", new EntityCoordinates(deck, new Vector2(3.5f)));
            gunner = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Gunner,
                new EntityCoordinates(deck, new Vector2(1.5f, 3.5f)), "hostile-dock")!.Value;
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(gunner).Docking = WFCrewSecurityResponse.Hostile;
            SEntMan.EnsureComponent<CompanyComponent>(gunner).CompanyName = "MMC";
            SEntMan.EnsureComponent<CompanyComponent>(visitor).CompanyName = "PDV";
            var factions = Server.System<NpcFactionSystem>();
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(gunner).Factions)
                factions.AddFaction(visitor, faction.Id);
        });
        await WaitUntil(() => SEntMan.GetComponent<WFGunnerDutyComponent>(gunner).AtConsole, 600, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<NpcFactionSystem>().IsEntityFriendly(gunner, visitor), Is.True);
            var config = Server.System<DockingSystem>().GetDockingConfig(deck, visitor);
            Assert.That(config, Is.Not.Null);
            Server.System<ShuttleSystem>().FTLDock((deck, SEntMan.GetComponent<TransformComponent>(deck)), config!);
        });
        await WaitUntil(() => SEntMan.HasComponent<ShipTargetingComponent>(gunner), 120, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShipTargetingComponent>(gunner).Target.EntityId, Is.EqualTo(visitor));
            if (firedOnCrew)
            {
                var hit = new WFCrewHullHitEvent(deck, visitor);
                SEntMan.EventBus.RaiseLocalEvent(deck, ref hit, true);
            }
            Server.System<WFCrewSecuritySystem>().Reset(gunner);
            SEntMan.GetComponent<WFCrewSecurityComponent>(gunner).Docking = WFCrewSecurityResponse.Warn;
        });
        // The gunner only looks over its targets every quarter second.
        await RunTicks(Ticks(1));
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.EqualTo(firedOnCrew),
            "Resetting docking policy must clear docking hostility without forgiving actual incoming fire."));
    }
}
