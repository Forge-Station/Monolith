#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);

    /// <summary>A friendly-faction mob that attacks crew loses protection from their hand weapons and cannons.</summary>
    [Test]
    public async Task CrewProtectionEndsForAttackers()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = ConvoyCrew(deck, "WFCrewDeckhand", "victims", new Vector2(1.5f));
            var attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(3.5f)));
            var factions = Server.System<NpcFactionSystem>();
            factions.ClearFactions(attacker);
            foreach (var faction in SEntMan.GetComponent<NpcFactionMemberComponent>(crew).Factions)
                factions.AddFaction(attacker, faction.Id);
            var gun = SEntMan.SpawnAtPosition(null, new EntityCoordinates(deck, new Vector2(4.5f)));
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            protection.TrackWeapon(gun, crew);
            Assert.That(protection.Protected(crew, attacker), Is.True);
            Assert.That(protection.Protected(gun, attacker), Is.True, "Mobs aboard the crew's own ship are sheltered from its cannons.");

            var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } };
            var damageable = Server.System<DamageableSystem>();
            damageable.TryChangeDamage(crew, damage, origin: attacker);
            Assert.That(protection.Protected(crew, attacker), Is.False, "An attacker loses hand-weapon protection.");
            Assert.That(protection.Protected(gun, attacker), Is.False, "An attacker loses cannon protection.");
            var before = SEntMan.GetComponent<DamageableComponent>(attacker).TotalDamage;
            damageable.TryChangeDamage(attacker, damage, ignoreResistances: true, origin: crew);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(attacker).TotalDamage > before, Is.True);
        });
    }

    /// <summary>Crews sharing a group label but serving different ships are separate crews.</summary>
    [Test]
    public async Task CrewSameLabelOnAnotherShipIsNotAnAlly()
    {
        var home = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var other = await CreateDeck(new Vector2(6, 12), 5, gravity: true);
        EntityUid guard = default, boarder = default;
        await Server.WaitAssertion(() =>
        {
            guard = ConvoyCrew(home, "WFCrewDeckhand", "crew", new Vector2(1.5f));
            var mate = ConvoyCrew(home, "WFCrewDeckhand", "crew", new Vector2(3.5f, 1.5f));
            boarder = ConvoyCrew(other, "WFCrewDeckhand", "crew", new Vector2(1.5f));
            Server.System<NpcFactionSystem>().ClearFactions(boarder);
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(guard).Boarding = WFCrewSecurityResponse.Hostile;
            var crews = Server.System<WFCrewSystem>();
            Assert.That(crews.SameCrew(guard, mate), Is.True);
            Assert.That(crews.SameCrew(guard, boarder), Is.False);
            Server.System<SharedTransformSystem>().SetCoordinates(boarder, new EntityCoordinates(home, new Vector2(2.5f, 1.5f)));
            Assert.That(crews.SameCrew(guard, boarder), Is.False, "Boarding another ship does not change a crewman's home.");
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            Assert.That(protection.Protected(guard, mate), Is.True);
            Assert.That(protection.Protected(guard, boarder), Is.False);
        });
        await RunTicks(Ticks(1.5));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<FactionExceptionComponent>(guard, out var exceptions)
                && exceptions.Hostiles.Any(target => target == boarder), Is.True,
                "A same-label crewman from another ship is a boarder.");
        });
    }

    /// <summary>A ship counts as disabled only after 10 s without thrust or weapons; regained thrust restarts the count.</summary>
    [Test]
    public async Task ShipDisabledNeedsSustainedIncapacity()
    {
        var ship = await CreateDeck(new Vector2(20, 0), 3, gravity: true);
        await Server.WaitPost(() => SEntMan.EnsureComponent<ShuttleComponent>(ship));
        Assert.That(await Watch(10f, 1), Is.False);
        Assert.That(await Watch(0f, 6), Is.False, "Six seconds without thrust is not yet disabled.");
        Assert.That(await Watch(10f, 3), Is.False);
        Assert.That(await Watch(0f, 6), Is.False, "Regained thrust restarts the count.");
        Assert.That(await Watch(0f, 8), Is.True, "Ten seconds without thrust or weapons disables the ship.");

        // Holds the thrust for a while, reading the status as often as gunners do; true if it ever read disabled.
        async Task<bool> Watch(float thrust, double seconds)
        {
            var disabled = false;
            for (var waited = 0; waited < Ticks(seconds); waited += 10)
            {
                await Server.WaitPost(() =>
                {
                    SEntMan.GetComponent<ShuttleComponent>(ship).LinearThrust[0] = thrust;
                    disabled |= Server.System<WFCrewShipStatusSystem>().IsDisabled(ship);
                });
                await RunTicks(10);
            }
            return disabled;
        }
    }

    /// <summary>Each attacking vessel expires on its own clock, and repeat fire from a known attacker raises no new alert.</summary>
    [Test]
    public async Task CrewShipThreatsExpirePerVessel()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 5, true);
        var first = await CreateDeck(new Vector2(600, 500), 5, true);
        var second = await CreateDeck(new Vector2(700, 500), 5, true);
        var alerts = Server.System<WFCrewAlertSystem>();
        var observer = Server.System<CrewAlertObserver>();
        await Server.WaitAssertion(() =>
        {
            var started = observer.Started;
            alerts.ReportShipThreat(deck, "expiry", first);
            alerts.ReportShipThreat(deck, "expiry", first);
            Assert.That(observer.Started, Is.EqualTo(started + 1), "A known attacker only extends its window.");
        });
        await RunTicks(Ticks(30));
        await Server.WaitAssertion(() =>
        {
            alerts.ReportShipThreat(deck, "expiry", second);
            Assert.That(alerts.GetHostileShips(deck, "expiry"), Is.EquivalentTo(new[] { first, second }));
        });
        await RunTicks(Ticks(32));
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.GetHostileShips(deck, "expiry"), Is.EquivalentTo(new[] { second }),
                "The first attacker's window closed although the second still fires.");
            Assert.That(alerts.IsHostileShip(deck, "expiry", first), Is.False);
            Assert.That(alerts.IsHostileShip(deck, "expiry", second), Is.True);
        });
    }

    /// <summary>
    /// A crew that only deters attackers still answers one firing on it from beyond its disengage range, while a
    /// ship merely inside its zone that far out is left alone.
    /// </summary>
    [Test]
    public async Task DeterringCrewAnswersFireFromBeyondItsReach()
    {
        var deck = await CreateDeck(new Vector2(700, 1300), 5, true);
        var sniper = await CreateDeck(new Vector2(1300, 1300), 5, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "deter");
            Server.System<WFCrewShipStatusSystem>().SetPolicy(deck, WFCrewDisengage.Deter, 300f);
            var alerts = Server.System<WFCrewAlertSystem>();
            alerts.ReportZoneThreat(deck, "deter", sniper);
            Assert.That(alerts.IsHostileShip(deck, "deter", sniper), Is.False, "A zone intruder beyond the disengage range is let be.");
            alerts.ReportShipThreat(deck, "deter", sniper);
            Assert.That(alerts.IsHostileShip(deck, "deter", sniper), Is.True, "One that fires on the ship is answered however far off.");
            Assert.That(alerts.GetHostileShips(deck, "deter"), Is.EquivalentTo(new[] { sniper }));
        });
    }

    /// <summary>Someone who attacks a crew makes an enemy vessel of whatever ship he goes back to.</summary>
    [Test]
    public async Task CrewAttackersShipIsAnEnemyVessel()
    {
        var deck = await CreateDeck(new Vector2(500, 1100), 5, true);
        var other = await CreateDeck(new Vector2(600, 1100), 5, true);
        EntityUid attacker = default;
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "boarded");
            attacker = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(deck, new Vector2(2.5f)));
            Server.System<WFCrewSystem>().AnswerAttack(deck, "boarded", attacker);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(deck, "boarded"), Is.Empty, "Aboard the crew's own ship he has no vessel to blame.");
            Server.System<SharedTransformSystem>().SetCoordinates(attacker, new EntityCoordinates(other, new Vector2(2.5f)));
        });
        await RunTicks(Ticks(2));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewAlertSystem>().IsHostileShip(deck, "boarded", other), Is.True);
            SEntMan.DeleteEntity(attacker);
        });
    }

    /// <summary>Starting to strip a living crewman makes him and his crew's fighters the thief's enemies.</summary>
    [Test]
    public async Task StrippingACrewmanIsAnAttack()
    {
        var deck = await CreateDeck(new Vector2(500, 1300), 5, true);
        await Server.WaitAssertion(() =>
        {
            var victim = ConvoyCrew(deck, "WFCrewDeckhand", "stripped", new Vector2(1.5f));
            var guard = ConvoyCrew(deck, "WFCrewMarine", "stripped", new Vector2(3.5f, 1.5f));
            var thief = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(deck, new Vector2(2.5f)));
            var before = SEntMan.GetComponent<NPCRetaliationComponent>(guard).AttackMemories;
            Assert.That(before.ContainsKey(thief), Is.False);
            Server.System<Content.Shared.Strip.SharedStrippableSystem>().GetStripTimeModifiers(thief, victim, null, TimeSpan.FromSeconds(1));
            var victimMemories = SEntMan.GetComponent<NPCRetaliationComponent>(victim).AttackMemories;
            var guardMemories = SEntMan.GetComponent<NPCRetaliationComponent>(guard).AttackMemories;
            Assert.That(victimMemories.ContainsKey(thief), Is.True, "The crewman takes the thief for an attacker.");
            Assert.That(guardMemories.ContainsKey(thief), Is.True, "And so do his crew's fighters.");
            SEntMan.DeleteEntity(thief);
        });
    }

    /// <summary>When a crew is cleared away, a dead crewman carried off to another ship is left there as an ordinary body.</summary>
    [Test]
    public async Task CarriedOffCorpsesOutliveTheirCrew()
    {
        var deck = await CreateDeck(new Vector2(500, 1500), 5, true);
        var other = await CreateDeck(new Vector2(600, 1500), 5, true);
        EntityUid taken = default, left = default, living = default;
        await Server.WaitAssertion(() =>
        {
            taken = ConvoyCrew(deck, "WFCrewDeckhand", "corpses", new Vector2(1.5f));
            left = ConvoyCrew(deck, "WFCrewDeckhand", "corpses", new Vector2(2.5f, 1.5f));
            living = ConvoyCrew(deck, "WFCrewDeckhand", "corpses", new Vector2(3.5f, 1.5f));
            var mobs = Server.System<MobStateSystem>();
            mobs.ChangeMobState(taken, MobState.Dead);
            mobs.ChangeMobState(left, MobState.Dead);
            Server.System<SharedTransformSystem>().SetCoordinates(taken, new EntityCoordinates(other, new Vector2(2.5f)));
            Server.System<WFCrewSetupSystem>().ClearCrew(deck, "corpses", keepCorpses: true);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(taken), Is.True, "The body somebody carried off stays.");
            Assert.That(SEntMan.HasComponent<WFCrewComponent>(taken), Is.False, "And is crew no longer.");
            Assert.That(SEntMan.EntityExists(left), Is.False, "The body left aboard goes with the crew.");
            Assert.That(SEntMan.EntityExists(living), Is.False);
            SEntMan.DeleteEntity(taken);
        });
    }

    /// <summary>
    /// A gunner with guns to spare shares them out among his attackers, each gun on the one nearest it; with one
    /// attacker, or few guns, they all stay on the one target.
    /// </summary>
    [Test]
    public async Task GunnerSharesGunsAmongAttackers()
    {
        var deck = await CreateDeck(new Vector2(500, 1700), 9, true);
        var east = await CreateDeck(new Vector2(525, 1702), 5, true);
        // As far off the west guns as east is off the east guns: a gun takes the target whose origin is nearest it.
        var west = await CreateDeck(new Vector2(484, 1702), 5, true);
        EntityUid gunner = default;
        var eastGuns = new List<EntityUid>();
        var westGuns = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            for (var y = 1; y <= 2; y++)
            {
                eastGuns.Add(SEntMan.SpawnEntity("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(8.5f, y + 0.5f))));
                westGuns.Add(SEntMan.SpawnEntity("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(0.5f, y + 0.5f))));
            }

            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            // An anchored gun passes its recoil to its grid and a hit shoves the target's; the split is judged by position,
            // so nothing here may move. Static after the shuttle component, which makes its grid dynamic on startup.
            var physics = Server.System<SharedPhysicsSystem>();
            foreach (var still in new[] { deck, east, west })
            {
                physics.SetBodyType(still, BodyType.Static);
            }

            SEntMan.SpawnAtPosition("WFTestGunnery", new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            gunner = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Gunner,
                new EntityCoordinates(deck, new Vector2(4.5f, 3.5f)), "battery")!.Value;
        });
        await WaitUntil(() => SEntMan.GetComponent<WFGunnerDutyComponent>(gunner).AtConsole, 600, () => Describe(gunner));
        await Server.WaitPost(() => Server.System<WFCrewAlertSystem>().ReportShipThreat(deck, "battery", east));
        await WaitUntil(() => Server.System<WFGunnerDutySystem>().Batteries(gunner).Count == 1, Ticks(20),
            () => "The gunner should lay the guns on the one attacker.");
        await Server.WaitAssertion(() =>
        {
            var groups = Server.System<WFGunnerDutySystem>().Batteries(gunner);
            Assert.That(groups[0].Target, Is.EqualTo(east));
            Assert.That(groups[0].Guns, Has.Count.EqualTo(4), "One attacker has every gun.");
            Server.System<WFCrewAlertSystem>().ReportShipThreat(deck, "battery", west);
        });
        await WaitUntil(() => Server.System<WFGunnerDutySystem>().Batteries(gunner).Count == 2, Ticks(10),
            () => "A second attacker should take a share of the guns.");
        await Server.WaitAssertion(() =>
        {
            var groups = Server.System<WFGunnerDutySystem>().Batteries(gunner);
            Assert.That(groups.First(group => group.Target == east).Guns, Is.EquivalentTo(eastGuns), "The guns on the east side fire east.");
            Assert.That(groups.First(group => group.Target == west).Guns, Is.EquivalentTo(westGuns), "And those on the west side fire west.");
        });
    }

    /// <summary>
    /// A few stray hits from a vessel of the ship's own faction are let pass; a sustained fire is an attack, and a
    /// stranger's first hit always is.
    /// </summary>
    [Test]
    public async Task StrayShotsFromTheSameFactionAreExcused()
    {
        var deck = await CreateDeck(new Vector2(500, 1900), 5, true);
        var friend = await CreateDeck(new Vector2(600, 1900), 5, true);
        var stranger = await CreateDeck(new Vector2(700, 1900), 5, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "strays");
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(deck).CompanyName = "TSF";
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(friend).CompanyName = "TSFCivilian";
            // An NPC ship of the same faction; a player one is allowed far more (OwnFactionStrayHits).
            Server.System<WFCrewSetupSystem>().MarkProfile(friend, "WFCrewProfileHauler");
        });

        var alerts = Server.System<WFCrewAlertSystem>();
        for (var hit = 0; hit < WFCrewAlertSystem.StrayHits; hit++)
        {
            await Server.WaitPost(() =>
            {
                // A shell that breaks several plates is one hit.
                for (var plate = 0; plate < 3; plate++)
                {
                    var ev = new WFCrewHullHitEvent(deck, friend);
                    SEntMan.EventBus.RaiseLocalEvent(deck, ref ev, true);
                }
            });
            await RunTicks(Ticks(0.6));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsHostileShip(deck, "strays", friend), Is.False, "A few stray shots from a friend are let pass.");
            Assert.That(alerts.IsExcused(deck, friend), Is.True);
            var one = new WFCrewHullHitEvent(deck, stranger);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref one, true);
            Assert.That(alerts.IsHostileShip(deck, "strays", stranger), Is.True, "A stranger's first hit is an attack.");
            var more = new WFCrewHullHitEvent(deck, friend);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref more, true);
            Assert.That(alerts.IsHostileShip(deck, "strays", friend), Is.True, "A friend who keeps firing is an attacker.");
            Assert.That(alerts.IsExcused(deck, friend), Is.False);
        });
    }

    /// <summary>A ship rammed at speed takes the rammer for an attacker; a bump is let pass.</summary>
    [Test]
    public async Task RammingIsAnAttackAndBumpingIsNot()
    {
        var deck = await CreateDeck(new Vector2(500, 900), 5, true);
        var rammer = await CreateDeck(new Vector2(600, 900), 5, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewPilot", "rammed");
            var ramming = Server.System<WFCrewRammingSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(ramming.Ram(deck, rammer, WFCrewRammingSystem.RamSpeed - 1f), Is.False);
            Assert.That(alerts.IsHostileShip(deck, "rammed", rammer), Is.False, "A bump is no attack.");
            Assert.That(ramming.Ram(deck, rammer, WFCrewRammingSystem.RamSpeed + 1f), Is.True);
            Assert.That(alerts.IsHostileShip(deck, "rammed", rammer), Is.True, "A ramming is.");
            Assert.That(ramming.Ram(rammer, deck, WFCrewRammingSystem.RamSpeed + 1f), Is.False, "A crew's own clumsy helm starts no fight.");
        });
    }

    /// <summary>A formation partner that fires on another member's hull becomes that ship's attacker.</summary>
    [Test]
    public async Task FormationPartnerFiringOnHullBecomesHostile()
    {
        var leader = await CreateDeck(new Vector2(500, 500), 7, true);
        var escort = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(escort, "WFCrewPilot", "partner-escort");
            ConvoyCrew(leader, "WFCrewRadioOperator", "partner-ward");
            Server.System<WFPilotDutySystem>().Escort(pilot, leader, 100);
            var escorts = Server.System<WFCrewEscortSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(escorts.AreInFormation(leader, escort), Is.True);
            var hit = new WFCrewHullHitEvent(leader, escort);
            SEntMan.EventBus.RaiseLocalEvent(leader, ref hit, true);
            Assert.That(alerts.GetHostileShips(leader, "partner-ward"), Is.EquivalentTo(new[] { escort }));
            Assert.That(alerts.IsHostileShip(leader, "partner-ward", escort), Is.True);
            Assert.That(alerts.GetHostileShips(escort, "partner-escort"), Is.Empty, "Only the victim turns on its partner.");
            Assert.That(escorts.AreInFormation(leader, escort), Is.True, "The assignment itself survives the attack window.");
        });
    }

    /// <summary>A carried gun fired at a shield is not a ship attack; a mounted weapon is.</summary>
    [Test]
    public async Task HandheldShieldHitsAreNotShipAttacks()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewRadioOperator", "shield-target");
            var alerts = Server.System<WFCrewAlertSystem>();
            var shooter = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(attacker, new Vector2(2.5f)));
            var carried = SEntMan.SpawnAttachedTo(null, new EntityCoordinates(shooter, Vector2.Zero));
            var handheld = new WFShipShieldAttackedEvent(deck, attacker, shooter, carried);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref handheld, true);
            Assert.That(alerts.GetHostileShips(deck, "shield-target"), Is.Empty, "A handheld shot must not frame the shooter's ship.");
            var mounted = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(4.5f)));
            var volley = new WFShipShieldAttackedEvent(deck, attacker, null, mounted);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref volley, true);
            Assert.That(alerts.GetHostileShips(deck, "shield-target"), Is.EquivalentTo(new[] { attacker }));
        });
    }

    /// <summary>Ship hitscan damage to a hull alerts the crew the way a projectile impact does.</summary>
    [Test]
    public async Task ShipBeamHullHitAlertsCrew()
    {
        var deck = await CreateDeck(new Vector2(500, 500), 7, true);
        var attacker = await CreateDeck(new Vector2(600, 500), 7, true);
        await Server.WaitAssertion(() =>
        {
            ConvoyCrew(deck, "WFCrewRadioOperator", "beam-target");
            var wall = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(4.5f)));
            if (!SEntMan.GetComponent<TransformComponent>(wall).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(wall);
            Assert.That(SEntMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "Precondition: the hull wall is anchored.");
            var battery = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(2.5f)));
            var beam = SEntMan.SpawnAtPosition(null, new EntityCoordinates(attacker, new Vector2(3.5f)));
            SEntMan.EnsureComponent<HitscanBasicDamageComponent>(beam);
            var damage = new DamageSpecifier { DamageDict = { ["Heat"] = 5 } };
            Server.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true, origin: battery, tool: beam);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(deck, "beam-target"), Is.EquivalentTo(new[] { attacker }));
        });
    }

    /// <summary>A weapon fired on its own keeps a crew attribution only while that crewman still commands it.</summary>
    [Test]
    public async Task CannonAttributionEndsWithItsGunner()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var deckhand = ConvoyCrew(deck, "WFCrewDeckhand", "cannoneers", new Vector2(1.5f));
            var gunner = ConvoyCrew(deck, "WFCrewGunner", "cannoneers", new Vector2(2.5f, 1.5f));
            var cannon = SEntMan.SpawnAtPosition("WFTestCrewCannon", new EntityCoordinates(deck, new Vector2(3.5f)));
            var protection = Server.System<WFCrewFriendlyFireSystem>();
            protection.TrackWeapon(cannon, deckhand);
            Assert.That(Attempt(), Is.True, "The commanding crewman's own burst stays attributed.");
            protection.TrackWeapon(cannon, gunner);
            Assert.That(Attempt(), Is.False, "A gunner away from its console no longer commands the weapon.");
            protection.TrackWeapon(cannon, deckhand);
            Server.System<MobStateSystem>().ChangeMobState(deckhand, MobState.Dead);
            Assert.That(Attempt(), Is.False, "Later fire must not inherit a dead crewman's alliances.");

            bool Attempt()
            {
                var shot = new ShotAttemptedEvent { User = cannon, Used = (cannon, SEntMan.GetComponent<GunComponent>(cannon)) };
                SEntMan.EventBus.RaiseLocalEvent(cannon, ref shot);
                return SEntMan.HasComponent<WFCrewShipFireComponent>(cannon);
            }
        });
    }

    /// <summary>An expired attack memory is pruned and no longer strips hostility a boarding rule still owns.</summary>
    [Test]
    public async Task ExpiredRetaliationKeepsBoardingHostility()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid guard = default, boarder = default;
        var memory = 0.0;
        await Server.WaitAssertion(() =>
        {
            guard = ConvoyCrew(deck, "WFCrewDeckhand", "keepers", new Vector2(1.5f));
            boarder = ConvoyCrew(deck, "WFCrewDeckhand", "raiders", new Vector2(2.5f));
            Server.System<NpcFactionSystem>().ClearFactions(boarder);
            SEntMan.EnsureComponent<WFCrewSecurityComponent>(guard).Boarding = WFCrewSecurityResponse.Hostile;
            var retaliation = SEntMan.GetComponent<NPCRetaliationComponent>(guard);
            Assert.That(Server.System<NPCRetaliationSystem>().TryRetaliate((guard, retaliation), boarder), Is.True);
            memory = retaliation.AttackMemoryLength!.Value.TotalSeconds;
            // A boarder in sight is not forgotten by himself, so the memory is ended by hand.
            var held = retaliation.AttackMemories;
            held[boarder] = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
        });
        await RunTicks(Ticks(2));
        await Server.WaitAssertion(() =>
        {
            var memories = SEntMan.GetComponent<NPCRetaliationComponent>(guard).AttackMemories;
            Assert.That(memories.ContainsKey(boarder), Is.False, "Expired attack memories are pruned.");
            Assert.That(Server.System<WFCrewSecuritySystem>().IsHostileVisitor(guard, boarder), Is.True);
            Assert.That(Server.System<NpcFactionSystem>().GetHostiles(guard), Does.Contain(boarder),
                "The boarding rule's hostility survives the memory's expiry.");
        });
    }
}
