#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Damage.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    private EntityUid ConvoyCrew(EntityUid grid, string role, string group, Vector2? position = null)
    {
        var member = Server.System<WFCrewSystem>().SpawnCrewman(role,
            new EntityCoordinates(grid, position ?? new Vector2(2.5f)), group)!.Value;
        SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
        return member;
    }

    /// <summary>An uncrewed freighter shares attacks through chained escorts, even after captain evasion begins.</summary>
    [Test]
    public async Task CrewEscortSharesAttacksAcrossGroupsAndEvasiveOrders()
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var a = await CreateDeck(new Vector2(600, 500), 7, true);
        var b = await CreateDeck(new Vector2(700, 500), 7, true);
        var c = await CreateDeck(new Vector2(800, 500), 7, true);
        var hostile = await CreateDeck(new Vector2(900, 500), 7, true);
        var second = await CreateDeck(new Vector2(1000, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var ships = new[] { a, b, c };
            var groups = new[] { "alpha", "bravo", "charlie" };
            var pilots = new EntityUid[3];
            var captains = new EntityUid[3];
            var flight = Server.System<WFPilotDutySystem>();
            for (var i = 0; i < ships.Length; i++)
            {
                pilots[i] = ConvoyCrew(ships[i], "WFCrewPilot", groups[i]);
                captains[i] = ConvoyCrew(ships[i], "WFCrewCaptain", groups[i], new Vector2(1.5f));
                flight.Escort(pilots[i], i == 0 ? leader : ships[i - 1], 100);
            }
            var escorts = Server.System<WFCrewEscortSystem>();
            Assert.That(escorts.GetFormation(leader), Is.EquivalentTo(new[] { leader, a, b, c }));
            var radio = ConvoyCrew(a, "WFCrewRadioOperator", "alpha", new Vector2(4.5f));
            var hit = new WFCrewHullHitEvent(leader, hostile);
            SEntMan.EventBus.RaiseLocalEvent(leader, ref hit, true);
            var alerts = Server.System<WFCrewAlertSystem>();
            for (var i = 0; i < ships.Length; i++)
            {
                Assert.That(alerts.GetHostileShips(ships[i], groups[i]), Does.Contain(hostile));
                Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilots[i]).Orders, Is.EqualTo(WFPilotOrder.Loiter));
            }
            Assert.That(escorts.GetFormation(leader), Is.EquivalentTo(new[] { leader, a, b, c }),
                "Temporary captain orders must not disconnect escorts.");
            Server.System<MobStateSystem>().ChangeMobState(captains[1], MobState.Dead);
            Server.System<WFCaptainSystem>().Update(0f);
            Assert.That(escorts.AreInFormation(b, leader), Is.True, "Losing the captain during evasion does not dissolve the assignment.");
            hit = new WFCrewHullHitEvent(c, second);
            SEntMan.EventBus.RaiseLocalEvent(c, ref hit, true);
            for (var i = 0; i < ships.Length; i++)
                Assert.That(alerts.GetHostileShips(ships[i], groups[i]), Is.EquivalentTo(new[] { hostile, second }));

            // A replacement escort order creates a cycle without the original freighter.
            flight.Escort(pilots[0], c, 100);
            Assert.That(escorts.GetFormation(a), Is.EquivalentTo(new[] { a, b, c }));
            Assert.That(escorts.AreInFormation(a, leader), Is.False);
            alerts.ReportShipThreat(b, "bravo", hostile);
            for (var i = 0; i < ships.Length; i++)
                Assert.That(alerts.GetHostileShips(ships[i], groups[i]), Is.EquivalentTo(new[] { hostile, second }));
        });
    }

    /// <summary>Explicit mission changes disconnect an escort even if its pilot died while other crew survived.</summary>
    [TestCase("Hold", false)]
    [TestCase("Follow", false)]
    [TestCase("Pause", false)]
    [TestCase("Pause", true)]
    [TestCase("Cancel", true)]
    [TestCase("Replace", true)]
    [TestCase("Skip", true)]
    public async Task CrewEscortMembershipEndsWithExplicitOrders(string change, bool deadPilot)
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var deck = await CreateDeck(new Vector2(600, 500), 7, true);
        var hostile = await CreateDeck(new Vector2(700, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(deck, "WFCrewPilot", "escort");
            ConvoyCrew(deck, "WFCrewDeckhand", "escort", new Vector2(1.5f));
            var objectives = Server.System<WFCrewObjectiveSystem>();
            var escorts = Server.System<WFCrewEscortSystem>();
            var flight = Server.System<WFPilotDutySystem>();
            Assert.That(objectives.SetQueue(deck, "escort", new()
            {
                new WFCrewObjective { Kind = WFCrewObjectiveKind.Escort, Target = SEntMan.GetNetEntity(leader) },
            }), Is.True);
            objectives.Update(.5f);
            Assert.That(escorts.AreInFormation(deck, leader), Is.True);
            if (deadPilot)
            {
                Server.System<MobStateSystem>().ChangeMobState(pilot, MobState.Dead);
                Assert.That(escorts.AreInFormation(deck, leader), Is.True,
                    "Surviving crew retain their defensive assignment after losing the pilot.");
            }
            switch (change)
            {
                case "Hold": flight.Hold(pilot); break;
                case "Follow": flight.Follow(pilot, leader, 100); break;
                case "Pause": objectives.Control(deck, "escort", WFCrewSetupAction.Pause); break;
                case "Cancel": objectives.Cancel(deck, "escort"); break;
                case "Replace": Assert.That(objectives.SetQueue(deck, "escort", new()), Is.True); break;
                case "Skip": objectives.Control(deck, "escort", WFCrewSetupAction.Skip); break;
            }
            Assert.That(escorts.AreInFormation(deck, leader), Is.False);
            var hit = new WFCrewHullHitEvent(leader, hostile);
            SEntMan.EventBus.RaiseLocalEvent(leader, ref hit, true);
            Assert.That(Server.System<WFCrewAlertSystem>().IsAlerted(deck, "escort"), Is.False);
        });
    }

    /// <summary>Queued future escort tasks and ordinary following do not join a convoy.</summary>
    [Test]
    public async Task CrewEscortMembershipRequiresCurrentEscort()
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var deck = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(deck, "WFCrewPilot", "future");
            var escorts = Server.System<WFCrewEscortSystem>();
            Server.System<WFPilotDutySystem>().Follow(pilot, leader, 100);
            Assert.That(escorts.AreInFormation(deck, leader), Is.False);
            var objectives = Server.System<WFCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "future", new()
            {
                new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold, Duration = 100 },
                new WFCrewObjective { Kind = WFCrewObjectiveKind.Escort, Target = SEntMan.GetNetEntity(leader) },
            }), Is.True);
            objectives.Update(.5f);
            Assert.That(escorts.AreInFormation(deck, leader), Is.False);
            Server.System<WFPilotDutySystem>().Escort(pilot, leader, 100);
            Assert.That(escorts.AreInFormation(deck, leader), Is.True);
            Server.System<MobStateSystem>().ChangeMobState(pilot, MobState.Dead);
            Assert.That(escorts.AreInFormation(deck, leader), Is.False, "An entirely dead crew cannot maintain an alliance.");
        });
    }

    /// <summary>Reported incoming fire overrides faction protection only until the vessel threat expires.</summary>
    [Test]
    public async Task CrewEscortRetaliatesAgainstSameFactionAndExpires()
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var deck = await CreateDeck(new Vector2(600, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(700, 500), 7, true);
        EntityUid gunner = default, weapon = default, wall = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            SEntMan.SpawnAtPosition("WFTestGunnery", new EntityCoordinates(deck, new Vector2(3.5f)));
            var pilot = ConvoyCrew(deck, "WFCrewPilot", "defender", new Vector2(1.5f));
            Server.System<WFPilotDutySystem>().Escort(pilot, leader, 100);
            gunner = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Gunner,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "defender")!.Value;
            var factions = Server.System<NpcFactionSystem>();
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(gunner).Factions)
                factions.AddFaction(attacker, faction.Id);
            wall = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(attacker, new Vector2(2.5f)));
            weapon = SEntMan.SpawnAtPosition(null, new EntityCoordinates(deck, new Vector2(4.5f)));
            Server.System<WFCrewFriendlyFireSystem>().TrackWeapon(weapon, gunner);
        });
        await WaitUntil(() => SEntMan.GetComponent<WFGunnerDutyComponent>(gunner).AtConsole, 600, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<NpcFactionSystem>().IsEntityFriendly(gunner, attacker), Is.True);
            Assert.That(Server.System<WFCrewFriendlyFireSystem>().Protected(weapon, wall), Is.True);
            var hit = new WFCrewHullHitEvent(leader, attacker);
            SEntMan.EventBus.RaiseLocalEvent(leader, ref hit, true);
        });
        await WaitUntil(() => SEntMan.HasComponent<ShipTargetingComponent>(gunner), 120, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShipTargetingComponent>(gunner).Target.EntityId, Is.EqualTo(attacker));
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            Assert.That(protection.Protected(weapon, wall), Is.False);
            Assert.That(protection.Protected(weapon, leader), Is.True, "The uncrewed escorted ship is protected without sharing a faction.");
            var before = SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage;
            var damage = new DamageSpecifier { DamageDict = { ["Piercing"] = 5 } };
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: weapon);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(wall).TotalDamage > before, Is.True);
        });
        await WaitUntil(() => !Server.System<WFCrewAlertSystem>().IsAlerted(deck, "defender"),
            4000, () => "Shared vessel threat did not expire without further incoming fire.");
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.False);
            Assert.That(Server.System<WFCrewFriendlyFireSystem>().Protected(weapon, wall), Is.True);
        });
    }
}
