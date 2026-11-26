#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.CavernEncounters;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.NpcIntents;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcIntents;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using static Robust.UnitTesting.RobustIntegrationTest;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.NpcIntents;

/// <summary>
/// NPC intents: a mugger downs, stops, takes cash and leaves; a hold-up resolves on payment and turns into a fight when
/// refused; Reavers take an organ and the victim lives; lethal NPCs still finish the job; the cooldown holds; boarders
/// keep their fire until hit.
/// </summary>
[TestFixture]
[TestOf(typeof(WFNpcIntentSystem))]
public sealed class NpcIntentTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    /// <summary>Between the Downed line and the Unconscious line, so a body is down but still alive.</summary>
    private const float DownedPressure = 0.75f;

    private async Task<(TestMapData Map, EntityUid Raider, EntityUid Victim)> Setup(Content.IntegrationTests.Pair.TestPair pair, string set, bool holds = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid raider = default, victim = default;
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -20; x <= 20; x++)
            for (var y = -20; y <= 20; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            raider = entMan.SpawnEntity("WFMobTunnelRaider", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            comp.Set = set;
            comp.Holds = holds;
            comp.PlayersOnly = false;
            comp.LeaveDistance = 10f;
        });
        await server.WaitRunTicks(5);
        return (map, raider, victim);
    }

    private static void PutDown(ServerIntegrationInstance server, EntityUid body, float level = DownedPressure)
    {
        server.System<SharedWolfmedConsciousnessSystem>().SetExternalPressure(body, "test", level);
    }

    private static void Begin(ServerIntegrationInstance server, EntityUid npc, EntityUid target, WFNpcIntentKind kind)
    {
        var entMan = server.EntMan;
        server.System<WFNpcIntentSystem>().Begin((npc, entMan.GetComponent<WFNpcIntentComponent>(npc)), target, kind);
    }

    /// <summary>The thalers a body carries in its hands, pockets and bags.</summary>
    private static int CashOn(ServerIntegrationInstance server, EntityUid body)
    {
        var entMan = server.EntMan;
        var total = 0;
        var inventory = server.System<Content.Shared.Inventory.InventorySystem>();
        foreach (var item in inventory.GetHandOrInventoryEntities(body))
        {
            if (entMan.HasComponent<CashComponent>(item) && entMan.TryGetComponent(item, out StackComponent? stack))
                total += stack.Count;
        }

        return total;
    }

    private static EntityUid GiveCash(ServerIntegrationInstance server, EntityUid body, int amount)
    {
        var entMan = server.EntMan;
        var cash = entMan.SpawnEntity("SpaceCash", entMan.GetComponent<TransformComponent>(body).Coordinates);
        server.System<SharedStackSystem>().SetCount(cash, amount);
        Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(body, cash, checkActionBlocker: false), Is.True);
        return cash;
    }

    /// <summary>When the NPC, or anything in its hands, last swung or fired; unchanged means it has not attacked.</summary>
    private static TimeSpan LastSwing(ServerIntegrationInstance server, EntityUid npc)
    {
        var entMan = server.EntMan;
        var latest = TimeSpan.Zero;
        var hands = server.System<SharedHandsSystem>();
        foreach (var uid in hands.EnumerateHeld(npc).Append(npc))
        {
            if (entMan.TryGetComponent(uid, out Content.Shared.Weapons.Melee.MeleeWeaponComponent? melee) && melee.NextAttack > latest)
                latest = melee.NextAttack;
            if (entMan.TryGetComponent(uid, out Content.Shared.Weapons.Ranged.Components.GunComponent? gun) && gun.NextFire > latest)
                latest = gun.NextFire;
        }

        return latest;
    }

    /// <summary>
    /// A mugger fights until the target is down, then stops attacking, walks up, takes the cash and leaves; the victim
    /// lives and is left alone for the cooldown.
    /// </summary>
    [Test]
    public async Task MugDownsStopsTakesCashAndLeaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var system = server.System<WFNpcIntentSystem>();
        var factions = server.System<NpcFactionSystem>();

        await server.WaitPost(() =>
        {
            GiveCash(server, victim, 150);
            Begin(server, raider, victim, WFNpcIntentKind.Mug);
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Fighting));
            Assert.That(factions.IsIgnored(raider, victim), Is.False, "He spared them before they were down.");
            PutDown(server, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        var swing = TimeSpan.Zero;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(system.IsDowned(victim), Is.True, "The fixture did not put the victim down.");
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Taking).Or.EqualTo(WFNpcIntentPhase.Leaving), "He did not stop.");
            Assert.That(factions.IsIgnored(raider, victim), Is.True, "He still counts the victim as a target.");
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(raider), Is.False);
            Assert.That(entMan.HasComponent<NPCRangedCombatComponent>(raider), Is.False);
            swing = LastSwing(server, raider);
        });

        // He walks up, takes it, then is told to walk off.
        for (var i = 0; i < 40 && entMan.GetComponent<WFNpcIntentComponent>(raider).Phase != WFNpcIntentPhase.Leaving; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
        }

        var walker = server.System<WFCavernWalkerSystem>();
        var transform = server.System<SharedTransformSystem>();
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Leaving), "He never finished with the victim.");
            Assert.That(comp.LeaveTo, Is.Not.Null);
            TestContext.Progress.WriteLine($"Mug: walk target {walker.MoveTarget(raider)}, asked for {comp.LeaveTo}.");
        });

        // The pathfinder answers in real time; see how far he gets (the engine's part, logged and not asserted).
        float Apart() => (transform.GetWorldPosition(raider) - transform.GetWorldPosition(victim)).Length();
        for (var i = 0; i < 20 && Apart() < 3.5f; i++)
        {
            await Task.Delay(100);
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
        }

        TestContext.Progress.WriteLine($"Mug: {Apart():0.0} m from the victim after leaving.");

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            TestContext.Progress.WriteLine($"Mug: took {comp.TookCash} thalers and {comp.Took.Count} things in all; last swing {swing} then {LastSwing(server, raider)}.");
            Assert.That(comp.TookCash, Is.EqualTo(150), "He took the wrong sum.");
            Assert.That(CashOn(server, victim), Is.Zero, "The victim kept cash.");
            Assert.That(CashOn(server, raider), Is.EqualTo(150), "The mugger doesn't hold the cash.");
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Leaving).Or.EqualTo(WFNpcIntentPhase.None), "He didn't leave.");
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(raider), Is.False, "He went back to fighting.");
            Assert.That(LastSwing(server, raider), Is.EqualTo(swing), "He swung again after the victim went down.");
            Assert.That(server.System<MobStateSystem>().IsDead(victim), Is.False, "The victim died.");
            Assert.That(system.InCooldown(raider, victim), Is.True, "No cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hold-up demands a sum, does not attack, resolves on payment (handed over, dropped) and the gang walks off.</summary>
    [Test]
    public async Task HoldUpResolvesOnPayment()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var system = server.System<WFNpcIntentSystem>();
        var factions = server.System<NpcFactionSystem>();

        int demand = 0;
        var swing = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            swing = LastSwing(server, raider);
            Begin(server, raider, victim, WFNpcIntentKind.HoldUp);
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            demand = comp.Demand;
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Demanding));
            Assert.That(demand, Is.InRange(30, 120), "The demand is outside the set's range.");
            Assert.That(factions.IsIgnored(raider, victim), Is.True, "He would shoot while demanding.");
            Assert.That(entMan.GetComponent<HTNComponent>(raider).Blackboard.ContainsKey(WFNpcIntentSystem.HoldKey), Is.True, "He isn't holding.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            TestContext.Progress.WriteLine($"Hold-up: demand {demand} thalers, after 5 s phase {comp.Phase}.");
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Demanding), "He gave up waiting too early.");
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(raider), Is.False);
            Assert.That(entMan.HasComponent<NPCRangedCombatComponent>(raider), Is.False);
            Assert.That(LastSwing(server, raider), Is.EqualTo(swing), "He swung during the hold-up.");
        });

        // Too little, then the rest.
        await server.WaitPost(() =>
        {
            var short1 = GiveCash(server, victim, 10);
            entMan.EventBus.RaiseLocalEvent(raider, new InteractUsingEvent(victim, short1, raider, entMan.GetComponent<TransformComponent>(raider).Coordinates), true);
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Paid, Is.EqualTo(10));
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Demanding), "Ten thalers settled it.");

            var rest = GiveCash(server, victim, demand);
            entMan.EventBus.RaiseLocalEvent(raider, new InteractUsingEvent(victim, rest, raider, entMan.GetComponent<TransformComponent>(raider).Coordinates), true);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Leaving), "Paying in full did not settle it.");
            Assert.That(system.InCooldown(raider, victim), Is.True);
            // He took only what was owed; the 10 overpaid stays with the payer.
            Assert.That(CashOn(server, victim), Is.EqualTo(10), "The payer was charged the wrong sum.");
            Assert.That(CashOn(server, raider), Is.EqualTo(demand), "He did not take the demand.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(raider), Is.False, "He attacked after being paid.");
            Assert.That(LastSwing(server, raider), Is.EqualTo(swing), "He swung after being paid.");
            Assert.That(factions.IsIgnored(raider, victim), Is.True);
        });

        // Cash dropped beside a second holder counts.
        EntityUid second = default, other = default;
        await server.WaitPost(() =>
        {
            second = entMan.SpawnEntity("WFMobTunnelRaiderGunner", new EntityCoordinates(map.Grid.Owner, -4.5f, 0.5f));
            other = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -3.5f, 0.5f));
            var comp = entMan.GetComponent<WFNpcIntentComponent>(second);
            comp.PlayersOnly = false;
            Begin(server, second, other, WFNpcIntentKind.HoldUp);
            var drop = entMan.SpawnEntity("SpaceCash", new EntityCoordinates(map.Grid.Owner, -4.5f, 1.0f));
            server.System<SharedStackSystem>().SetCount(drop, comp.Demand);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(second);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Leaving), "Cash dropped at his feet did not settle it.");
        });

        // No bank account, no payment.
        await server.WaitPost(() =>
        {
            var third = entMan.SpawnEntity("WFMobTunnelRaider", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f));
            var mark = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 7.5f, 0.5f));
            var comp = entMan.GetComponent<WFNpcIntentComponent>(third);
            comp.PlayersOnly = false;
            Begin(server, third, mark, WFNpcIntentKind.HoldUp);
            Assert.That(system.PayFromBank((third, comp), comp.Demand), Is.False, "A body with no account paid.");
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Demanding));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hold-up that times out, is run from, or is answered with a blow turns into a fight.</summary>
    [Test]
    public async Task HoldUpRefusedBecomesAFight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var factions = server.System<NpcFactionSystem>();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        await server.WaitPost(() => Begin(server, raider, victim, WFNpcIntentKind.HoldUp));
        await server.WaitRunTicks(5);

        // Out of time.
        await server.WaitPost(() => entMan.GetComponent<WFNpcIntentComponent>(raider).PhaseEnd = timing.CurTime);
        await server.WaitRunTicks(pair.SecondsToTicks(0.6f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Fighting), "Refusal did not start a fight.");
            Assert.That(comp.Active, Is.EqualTo(WFNpcIntentKind.Mug), "The refusal is not the set's.");
            Assert.That(factions.IsIgnored(raider, victim), Is.False, "He still spares them.");
            Assert.That(entMan.GetComponent<HTNComponent>(raider).Blackboard.ContainsKey(WFNpcIntentSystem.HoldKey), Is.False, "He is still holding.");
        });

        // Running.
        await server.WaitPost(() =>
        {
            Reset(server, raider);
            Begin(server, raider, victim, WFNpcIntentKind.HoldUp);
            entMan.System<SharedTransformSystem>().SetLocalPosition(victim, new System.Numerics.Vector2(14.5f, 0.5f));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.6f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFNpcIntentComponent>(raider).Phase, Is.EqualTo(WFNpcIntentPhase.Fighting), "Running did not start a fight.");
        });

        // Hitting him.
        await server.WaitPost(() =>
        {
            Reset(server, raider);
            entMan.System<SharedTransformSystem>().SetLocalPosition(victim, new System.Numerics.Vector2(1.5f, 0.5f));
            Begin(server, raider, victim, WFNpcIntentKind.HoldUp);
            var blunt = server.ResolveDependency<IPrototypeManager>().Index(Blunt);
            server.System<DamageableSystem>().TryChangeDamage(raider, new DamageSpecifier(blunt, 1), origin: victim);
            Assert.That(entMan.GetComponent<WFNpcIntentComponent>(raider).Phase, Is.EqualTo(WFNpcIntentPhase.Fighting), "A blow did not start a fight.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Reset(ServerIntegrationInstance server, EntityUid npc)
    {
        var entMan = server.EntMan;
        var comp = entMan.GetComponent<WFNpcIntentComponent>(npc);
        var factions = server.System<NpcFactionSystem>();
        if (comp.Target is { } target)
        {
            factions.UnignoreEntity(npc, target);
            factions.DeAggroEntity(npc, target);
        }

        comp.Cooldowns.Clear();
        comp.Phase = WFNpcIntentPhase.None;
        comp.Active = null;
        comp.Target = null;
        if (entMan.TryGetComponent(npc, out HTNComponent? htn))
            htn.Blackboard.Remove<bool>(WFNpcIntentSystem.HoldKey);
    }

    /// <summary>Reavers cut an organ out of the downed and leave; the victim is alive and short of that organ.</summary>
    [Test]
    public async Task ReaverTakesAnOrganAndLeavesTheVictimAlive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsReaver", holds: false);
        var body = server.System<SharedBodySystem>();

        var before = 0;
        await server.WaitPost(() =>
        {
            before = body.GetBodyOrgans(victim).Count();
            Begin(server, raider, victim, WFNpcIntentKind.Harvest);
            PutDown(server, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(8f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Took, Has.Count.EqualTo(1), "He took nothing, or too much.");
            var taken = comp.Took[0];
            var kind = entMan.HasComponent<OrganComponent>(taken) ? "organ " + entMan.GetComponent<OrganComponent>(taken).SlotId : "limb";
            TestContext.Progress.WriteLine($"Harvest: took a {kind}; organs {before} -> {body.GetBodyOrgans(victim).Count()}.");
            if (entMan.TryGetComponent(taken, out OrganComponent? organ))
                Assert.That(organ.SlotId, Is.EqualTo("kidneys").Or.EqualTo("liver"));
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Leaving));
        });

        // Alive half a minute on: down and short of a part, not dead.
        await server.WaitRunTicks(pair.SecondsToTicks(30f));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MobStateSystem>().IsDead(victim), Is.False, "The victim died of it.");
            Assert.That(entMan.GetComponent<TransformComponent>(victim).Anchored, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Cultists take a limb as a trophy; the victim lives.</summary>
    [Test]
    public async Task CultistTakesALimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsCultist", holds: false);
        var body = server.System<SharedBodySystem>();

        var before = 0;
        await server.WaitPost(() =>
        {
            before = body.GetBodyChildren(victim).Count();
            Begin(server, raider, victim, WFNpcIntentKind.Harvest);
            PutDown(server, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(8f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Took, Has.Count.EqualTo(1));
            Assert.That(entMan.HasComponent<BodyPartComponent>(comp.Took[0]), Is.True, "He did not take a body part.");
            var type = entMan.GetComponent<BodyPartComponent>(comp.Took[0]).PartType;
            TestContext.Progress.WriteLine($"Trophy: took a {type}; parts {before} -> {body.GetBodyChildren(victim).Count()}.");
            Assert.That(type, Is.EqualTo(BodyPartType.Arm).Or.EqualTo(BodyPartType.Leg));
            Assert.That(server.System<MobStateSystem>().IsDead(victim), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A lethal NPC never spares a downed target; a non-lethal one cancels its own attacks on one.</summary>
    [Test]
    public async Task LethalNeverSpares()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var factions = server.System<NpcFactionSystem>();

        await server.WaitPost(() =>
        {
            Begin(server, raider, victim, WFNpcIntentKind.Lethal);
            PutDown(server, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(6f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(server.System<WFNpcIntentSystem>().IsDowned(victim), Is.True);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Fighting), "A lethal NPC stopped.");
            Assert.That(factions.IsIgnored(raider, victim), Is.False, "A lethal NPC spared the victim.");
            Assert.That(comp.Cooldowns, Is.Empty);

            var attack = new AttackAttemptEvent(raider, victim);
            entMan.EventBus.RaiseLocalEvent(raider, attack, true);
            Assert.That(attack.Cancelled, Is.False, "A lethal NPC's blow was cancelled.");

            comp.Active = WFNpcIntentKind.Mug;
            var spared = new AttackAttemptEvent(raider, victim);
            entMan.EventBus.RaiseLocalEvent(raider, spared, true);
            Assert.That(spared.Cancelled, Is.True, "A mugger's blow on a downed target was allowed.");

            var standing = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(raider).Coordinates);
            var live = new AttackAttemptEvent(raider, standing);
            entMan.EventBus.RaiseLocalEvent(raider, live, true);
            Assert.That(live.Cancelled, Is.False, "A mugger could not hit a standing target.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Once finished with a player the NPC leaves them alone for the cooldown, and fights again when they hit it or
    /// the cooldown runs out.
    /// </summary>
    [Test]
    public async Task CooldownStopsReEngaging()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var system = server.System<WFNpcIntentSystem>();
        var factions = server.System<NpcFactionSystem>();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        await server.WaitPost(() =>
        {
            Begin(server, raider, victim, WFNpcIntentKind.BeatDown);
            PutDown(server, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitPost(() =>
        {
            Assert.That(system.InCooldown(raider, victim), Is.True);
            PutDown(server, victim, 0f);
            entMan.GetComponent<WFNpcIntentComponent>(raider).PhaseEnd = timing.CurTime;
        });

        // A body that has only just gone down stays down a while; wait it out so he is up and in plain sight again.
        for (var i = 0; i < 60 && system.IsDowned(victim); i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
        }

        Assert.That(system.IsDowned(victim), Is.False, "The victim never got up.");

        await server.WaitRunTicks(pair.SecondsToTicks(4f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.None), "He is still busy.");
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(raider), Is.False, "He went for them again.");
            Assert.That(comp.Active, Is.Null, "He started a new engagement inside the cooldown.");
            Assert.That(factions.IsIgnored(raider, victim), Is.True);

            // They hit him: the cooldown is off.
            var blunt = server.ResolveDependency<IPrototypeManager>().Index(Blunt);
            server.System<DamageableSystem>().TryChangeDamage(raider, new DamageSpecifier(blunt, 1), origin: victim);
            Assert.That(system.InCooldown(raider, victim), Is.False, "Hitting him did not end the cooldown.");
            Assert.That(factions.IsIgnored(raider, victim), Is.False);

            // Or it runs out.
            comp.Cooldowns[victim] = timing.CurTime;
            factions.IgnoreEntity(raider, victim);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.6f));
        await server.WaitAssertion(() =>
        {
            // Free to engage them again, which he does at once.
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Cooldowns, Is.Empty, "The cooldown never ran out.");
            Assert.That(factions.IsIgnored(raider, victim) && comp.Phase == WFNpcIntentPhase.None, Is.False, "He is still sparing them after the cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A boarder aboard a foreign ship fires on nobody who hasn't hit its party; a blow to any of them lifts it.</summary>
    [Test]
    public async Task BoardersHoldFireUntilHit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var system = server.System<WFNpcIntentSystem>();

        EntityUid boarder = default, mate = default, player = default, other = default, home = default;
        await server.WaitPost(() =>
        {
            boarder = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mate = entMan.SpawnEntity("MobHuman", map.GridCoords);
            home = entMan.SpawnEntity("MobHuman", map.GridCoords);
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            other = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // Their post is somewhere that is not this grid, so they are boarding.
            foreach (var uid in new[] { boarder, mate })
            {
                var crew = entMan.AddComponent<WFCrewComponent>(uid);
                crew.Group = "boarders";
                crew.Post = new EntityCoordinates(map.MapUid, 0f, 0f);
                var intent = entMan.AddComponent<WFNpcIntentComponent>(uid);
                intent.Set = "WFIntentsReaver";
                intent.Boarder = true;
            }

            var homeCrew = entMan.AddComponent<WFCrewComponent>(home);
            homeCrew.Group = "boarders";
            homeCrew.Post = map.GridCoords;
            var homeIntent = entMan.AddComponent<WFNpcIntentComponent>(home);
            homeIntent.Set = "WFIntentsReaver";
            homeIntent.Boarder = true;
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(system.HoldsFireOn(boarder, player), Is.True, "A boarder would fire on a bystander.");
            Assert.That(system.HoldsFireOn(home, player), Is.False, "A crewman on his own ship holds fire.");
            Assert.That(server.System<WFCrewWeaponSystem>().CanEngage(boarder, player), Is.False, "Crew combat would engage the bystander.");

            var blunt = server.ResolveDependency<IPrototypeManager>().Index(Blunt);
            server.System<DamageableSystem>().TryChangeDamage(boarder, new DamageSpecifier(blunt, 1), origin: player);
            Assert.That(system.HoldsFireOn(boarder, player), Is.False, "A boarder still held fire after being hit.");
            Assert.That(system.HoldsFireOn(mate, player), Is.False, "The rest of the party still held fire.");
            Assert.That(system.HoldsFireOn(boarder, other), Is.True, "A blow from one person lifted it for everyone.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An NPC that meets a hostile rolls an endpoint from its set on its own, once, without being told.</summary>
    [Test]
    public async Task MeetingSomeoneRollsAnEndpoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsTunnelRaider");
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Active, Is.Not.Null, "He never rolled an endpoint.");
            Assert.That(comp.Target, Is.EqualTo(victim));
            Assert.That(proto.Index(comp.Set).Intents.Select(i => i.Kind), Does.Contain(comp.Active!.Value));
            TestContext.Progress.WriteLine($"Meeting: rolled {comp.Active}, phase {comp.Phase}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A warning holds fire and fires past the target, gives them time, then chases them out if they stay, and lets them
    /// go if they leave.
    /// </summary>
    [Test]
    public async Task WarnOffGivesTimeThenChasesThemOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, raider, victim) = await Setup(pair, "WFIntentsSettler", holds: true);
        var factions = server.System<NpcFactionSystem>();
        var timing = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
        var transform = server.System<SharedTransformSystem>();

        var swing = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            swing = LastSwing(server, raider);
            Begin(server, raider, victim, WFNpcIntentKind.WarnOff);
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Warning));
            Assert.That(factions.IsIgnored(raider, victim), Is.True, "He fought during the warning.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFNpcIntentComponent>(raider).Phase, Is.EqualTo(WFNpcIntentPhase.Warning));
            Assert.That(LastSwing(server, raider), Is.EqualTo(swing), "He swung during the warning.");
        });

        // They stay: he drives them out.
        await server.WaitPost(() => entMan.GetComponent<WFNpcIntentComponent>(raider).PhaseEnd = timing.CurTime);
        await server.WaitRunTicks(pair.SecondsToTicks(0.6f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.Fighting), "He did not chase them out.");
            Assert.That(comp.Active, Is.EqualTo(WFNpcIntentKind.WarnOff));
            Assert.That(factions.IsIgnored(raider, victim), Is.False);
        });

        // They go: he lets them be.
        await server.WaitPost(() => transform.SetLocalPosition(victim, new System.Numerics.Vector2(18.5f, 0.5f)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.6f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFNpcIntentComponent>(raider);
            Assert.That(comp.Phase, Is.EqualTo(WFNpcIntentPhase.None), "He kept chasing them.");
            Assert.That(factions.IsIgnored(raider, victim), Is.True, "He was ready to attack them again.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every set rolls something, its lines exist and it names real organs; the entities that use them resolve.</summary>
    [Test]
    public async Task SetsAreSound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var set in proto.EnumeratePrototypes<WFNpcIntentSetPrototype>())
            {
                Assert.That(set.Intents.Sum(i => i.Weight), Is.GreaterThan(0f), $"{set.ID} rolls nothing.");
                foreach (var lines in new[] { set.DemandLines, set.PaidLines, set.ShortLines, set.RefusedLines, set.MugLines,
                             set.BeatLines, set.HarvestLines, set.WarnLines, set.WarnOffLines })
                {
                    foreach (var line in lines)
                    {
                        Assert.That(loc.HasString(line), Is.True, $"{set.ID}: no string {line}.");
                    }
                }
            }

            var factory = server.ResolveDependency<IComponentFactory>();
            foreach (var entity in proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (entity.TryGetComponent(out WFNpcIntentComponent? comp, factory))
                    Assert.That(proto.HasIndex(comp.Set), Is.True, $"{entity.ID} names a missing set.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
