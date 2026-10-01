using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Silicon.WeldingHealable; // WOLFGATE: the IPC repair gate lives here.
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Body.Components; // WOLFGATE: BloodstreamComponent is server-only here.
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Body;
using Content.Shared._Shitmed.Body.Organ; // M4: HeartComponent
using Content.Shared.Body.Components; // M4: LungComponent
using Content.Shared._Onyx.Chemistry.Circulation;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Phase 5's whole-mob behaviour: the IPC and protogen hosts that PROTO Q and EXT 3 create, the two tool
/// edits that keep an IPC repairable, the gib ceilings, and the prototype-wide circulatory-stream invariant.
/// </summary>
/// <remarks>
/// <para>PLAN5 §6.2 T-P5-2 / -3 / -9 / -10 / -11 / -12 / -14 / -20 (WP13-6).</para>
/// <para>
/// Trap 8 governs every destruction assertion: <c>GibPartBehavior</c> ends in <c>QueueDel</c>
/// (<c>GibbingSystem.TryGibEntityWithRef</c>, <c>BodySystem.GibPart</c>), which is only flushed on a tick, so
/// the damage and the <c>Deleted</c> check never share a block. Trap 7 governs every severing assertion:
/// <c>AmputationSystem</c> needs two qualifying hits (the crossing hit only calls <c>SetSeverable(true)</c>)
/// while <c>DestructibleSystem</c> fires on the crossing hit itself, so wherever the gib rung is at or below
/// the amputation threshold the gib wins deterministically.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedSpeciesSpawnTest : GameTest
{
    /// <summary>
    /// PLAN5 §6.2 T-P5-2. PROTO Q in one assertion block: an IPC is a wound host whose every limb carries the
    /// IPC profile, whose bloodstream holds oil and no chemicals at all (U16), and which spawns and deletes
    /// cleanly.
    /// </summary>
    [Test]
    public async Task IpcIsAWoundHostWithTheIpcProfileTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);

            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True);
            Assert.That(entities.HasComponent<PainShockTargetComponent>(body), Is.True,
                "P5-D10: Wolfgate keeps PainShockTarget on BaseMobSpeciesOrganic, which MobIPC does not " +
                "inherit, so PROTO Q restates it - Onyx has it on BaseSpeciesMob, which its MobIpc parents.");

            var parts = graph.GetBodyChildren(body).ToList();
            Assert.That(parts, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var (part, component) in parts)
                {
                    Assert.That(entities.GetComponent<WoundableComponent>(part).Profile,
                        Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("IpcBodyPartProfile")),
                        $"every IPC slot takes the chassis profile, including {component.PartType}.");
                    // fractureProfile: null on WFWolfmedPartIpc - no IPC slot can ever fracture.
                    Assert.That(entities.System<WolfmedBodyPartSystem>().Get(part).FractureProfile, Is.Null);
                }

                // PROTO Q's Bloodstream block, field by field. Onyx's `bloodReferenceSolution: Oil 250` has no
                // Wolfgate equivalent, so it maps onto the classic bloodReagent + bloodMaxVolume pair (P5-D4).
                // Playtest 3 IPC 2: the pool is hydraulic fluid now, not Oil (lamp oil a lit welder set alight).
                Assert.That(bloodstream.BloodReagent, Is.EqualTo(new ProtoId<ReagentPrototype>("WFWolfmedHydraulicFluid")));
                Assert.That(bloodstream.BloodMaxVolume, Is.EqualTo(FixedPoint2.New(250)));
                // U16: an IPC has no metabolizer (OrganIPCPump's Metabolizer block is commented out), so a
                // 250u chemical solution would be a trap. InjectableSolution is deliberately absent with it.
                Assert.That(bloodstream.ChemicalMaxVolume, Is.EqualTo(FixedPoint2.Zero));

                // Oil loss deals no damage at all: Heat would route to a part and feed its own leak (P5-D6), and
                // Bloodloss reads as oxygen loss on a machine that does not breathe.
                Assert.That(bloodstream.BloodlossDamage.Empty && bloodstream.BloodlossHealDamage.Empty);
            });

            entities.DeleteEntity(body);
        });

        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(body), Is.True,
            "an IPC must spawn and delete without a RemCompDeferred or container assert."));
    }

    /// <summary>
    /// An IPC leaks its hydraulic fluid and the leak registers on the bleed total, but it takes no Airloss-group damage from
    /// it or from anything else: it does not breathe (owner decision, 2026-09-19, reversing P5-D5).
    /// </summary>
    [Test]
    public async Task IpcLeaksOilAndTakesNoAirlossTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);
            var blood = entities.System<BloodstreamSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();

            Assert.That(entities.GetComponent<DamageableComponent>(body).DamageContainerID,
                Is.EqualTo("WFSiliconWolfmed"),
                "PROTO Q moves the IPC mob off the stock Silicon container.");

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 30), null,
                ignoreResistances: true));

            // IpcMechanicalDamageWound bleeds at wound level with chance 1 and no minimumSeverity, and
            // WoundBleedingSystem.RefreshBody pushes the total into BloodstreamComponent.BleedAmount through
            // CirculatoryStreamSystem.SetBleedRates.
            Assert.That(bloodstream.BleedAmount, Is.GreaterThan(0f),
                "a wounded chassis must register on the body's bleed total, or the analyzer and the " +
                "tourniquet have nothing to act on.");

            // The spilled fluid is the chassis's own, not Blood. Playtest 3 IPC 2: hydraulic fluid, which does not burn;
            // it was Oil, flammability 2 with a FlammableTileReaction, and a welder set the trail alight.
            Assert.That(solutions.TryGetSolution(body, bloodstream.BloodSolutionName, out var solution, out _));
            Assert.That(solution!.Value.Comp.Solution.Contents.Select(reagent => reagent.Reagent.Prototype),
                Does.Contain("WFWolfmedHydraulicFluid"));

            var before = blood.GetBloodLevelPercentage(body);
            Assert.That(blood.TryModifyBloodLevel(body, FixedPoint2.New(-50)));
            Assert.That(blood.GetBloodLevelPercentage(body), Is.LessThan(before),
                "and the level falls as it leaks.");

            // An IPC does not breathe, so it never takes Airloss-group damage: Bloodloss reads as oxygen loss
            // on the analyzer. Oil loss is a leak, not a damage source.
            foreach (var type in new[] { "Bloodloss", "Asphyxiation" })
            {
                entities.System<WolfmedDamageableSystem>().TryChangeDamage(body, Spec(type, 10), out _);
                Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict
                        .GetValueOrDefault(new ProtoId<DamageTypePrototype>(type)),
                    Is.EqualTo(FixedPoint2.Zero),
                    $"an IPC must never hold {type} damage.");
            }

            Assert.That(bloodstream.BloodlossDamage.Empty && bloodstream.BloodlossHealDamage.Empty,
                "and its bloodstream deals none when the oil runs low.");
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-12. PROTO S/T are not optional (PLAN5 R3): <c>WeldingHealableSystem</c> gates on the
    /// tool's <c>damageContainers</c> list, so PROTO Q's container change would otherwise silently remove the
    /// only way to repair an IPC. <c>SiliconRepairFinishedEvent</c> is <c>protected</c> inside
    /// <c>SharedWeldingHealableSystem</c> and cannot be raised from a test, so the gate is asserted directly
    /// and the healing line the handler runs after it is driven by hand.
    /// </summary>
    [Test]
    public async Task WelderStillRepairsAnIpcTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var welder = entities.SpawnEntity("Welder", map.GridCoords);
            var nanite = entities.SpawnEntity("NaniteApplicator", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var container = entities.GetComponent<DamageableComponent>(body).DamageContainerID;

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WeldingHealableComponent>(body), Is.True,
                    "MobIPC is welder-repairable (P5-D15 keeps it that way rather than adding Repairable).");
                Assert.That(entities.GetComponent<WeldingHealingComponent>(welder).DamageContainers,
                    Does.Contain(container), "PROTO S - without this line the welder stops repairing IPCs.");
                Assert.That(entities.GetComponent<WeldingHealingComponent>(nanite).DamageContainers,
                    Does.Contain(container), "PROTO T - same for the Mono nanite applicator.");
            });

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 40), null,
                ignoreResistances: true));
            var wound = FindWound(entities, wounds, arm, "IpcMechanicalDamageWound");
            Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(40)));

            // WeldingHealableSystem.OnRepairFinished's payload line, verbatim:
            //   _damageableSystem.TryChangeDamage(uid, component.Damage, true, false, origin: args.User)
            // The welder's block is Blunt/Piercing/Slash -25 each. No treatment-capability scope is opened by
            // that path, and WoundDamageRoutingSystem.CanTreatPart returns true with no scope open, so the
            // heal reaches the chassis wound for free - which is why P5-D16 does not add a Healing block.
            var repair = entities.GetComponent<WeldingHealingComponent>(welder).Damage;
            Assert.That(entities.System<WolfmedDamageableSystem>()
                .TryChangeDamage(body, repair, out _, ignoreResistances: true, interruptsDoAfters: false));

            Assert.That(wound.Comp.Severity, Is.LessThan(FixedPoint2.New(40)),
                "the welder's own damage block must reduce chassis-wound severity, not just the body total.");
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-11 (U3′(b) shipped). <c>PartIPCBase</c> is a single <c>Destructible</c> block that is
    /// first in every IPC slot's parent list, so its two rungs replace <c>MajorLimb</c>'s and
    /// <c>MinorLimb</c>'s wholesale. This documents trap 7 rather than asserting a bug: Slash severs because
    /// 130 &lt; 210, pure Blunt destroys because 190 &lt; 250.
    /// </summary>
    [Test]
    public async Task IpcLimbSeverabilityAndGibCeilingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var slashed = EntityUid.Invalid;
        var blunted = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var routing = Routing(entities);

            // (i) 140 pure Slash. The IPC arm inherits WFWolfmedBaseLeftArm's organic amputation set - Slash 130
            // (P5-D7: Onyx's own 270/400/600 are NOT ported, they sit on a different part chain) - and
            // PartIPCBase's Slash rung is 210, so the limb survives and AmputationSystem arms it.
            var first = entities.SpawnEntity("MobIPC", map.GridCoords);
            slashed = Part(entities, first, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(routing.TryApplyPartDamage(first, slashed, Spec("Slash", 140), null,
                ignoreResistances: true));

            // (ii) 195 pure Blunt on a fresh IPC. The Blunt rung is 190 and the Blunt amputation threshold is
            // 250, so the part is destroyed before it can ever be severed - the same contradiction organic
            // limbs already live with (PLAN5 R11, handed to the balance pass).
            var second = entities.SpawnEntity("MobIPC", map.GridCoords);
            blunted = Part(entities, second, BodyPartType.Arm, BodyPartSymmetry.Left);
            // M1b: somebody deals it; damage with no origin stops at the limb's ambient ceiling.
            Assert.That(routing.TryApplyPartDamage(second, blunted, Spec("Blunt", 195), first,
                ignoreResistances: true));
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(slashed), Is.False,
                    "Slash 140 is well under PartIPCBase's Slash rung of 210 (PROTO O(c), raised from 150).");
                Assert.That(entities.GetComponent<WoundableComponent>(slashed).Severable, Is.True,
                    "140/130 >= 1, so the crossing hit arms the limb for a later finishing hit.");
                Assert.That(entities.Deleted(blunted), Is.True,
                    "Blunt 195 clears the 190 rung (PROTO O(c), raised from 110) and GibPart always QueueDels.");
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-10. Regression guard for P5-D11: body damage on a wound host is the sum of every part's
    /// positive damage (<c>WoundDamageProjectionSystem.RefreshBodyDamage</c>), so <c>MobIPC</c>'s old flat
    /// Blunt 400 was reachable from routine limb damage — the same reason D22 raised organics to 1500.
    /// </summary>
    [Test]
    public async Task IpcDoesNotGibFromRoutineLimbDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var routing = Routing(entities);
            var limbs = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Where(part => part.Component.PartType is BodyPartType.Arm or BodyPartType.Leg)
                .Select(part => part.Id)
                .ToList();
            Assert.That(limbs, Has.Count.EqualTo(4));

            // 150 per limb keeps every part under its own 190 Blunt rung while putting 600 on the body - well
            // past the old 400 threshold and well short of the new 1500 one.
            foreach (var limb in limbs)
                Assert.That(routing.TryApplyPartDamage(body, limb, Spec("Blunt", 150), null,
                    ignoreResistances: true));

            Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict
                    .GetValueOrDefault(new ProtoId<DamageTypePrototype>("Blunt")),
                Is.GreaterThan(FixedPoint2.New(400)),
                "the projected body total must genuinely clear the OLD threshold, or this guard is vacuous.");
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() => Assert.That(entities.Deleted(body), Is.False,
            "PROTO Q raised MobIPC's gib threshold 400 -> 1500 precisely so routine limb damage cannot " +
            "delete the corpse (D22/P5-D11)."));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-9 (U4(a) shipped). The <c>WoundHost</c> exclusion is lifted: protogen is a plain
    /// organic host. The organ half pins P5-D19/U12′ as a KNOWN gap rather than letting it rot — protogen
    /// organs are <c>BaseProtogenOrgan</c>-derived and carry no <c>OrganDamage</c>, so there is no organ
    /// damage, no organ destruction, no internal bleeding and none of the seven organ surgeries.
    /// </summary>
    [Test]
    public async Task ProtogenIsAWoundHostTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var hosts = new EntityUid[2];

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            // Both hosts EXT 3 creates. MobProtogenDummy parents BaseSpeciesDummy and is deliberately not one.
            hosts[0] = entities.SpawnEntity("MobProtogen", map.GridCoords);
            hosts[1] = entities.SpawnEntity("MobProtogenRandom", map.GridCoords);

            Assert.Multiple(() =>
            {
                foreach (var host in hosts)
                {
                    Assert.That(entities.HasComponent<WoundHostComponent>(host), Is.True,
                        "WolfmedWoundHostExclusionSystem.ExcludedAncestors is empty as of P5-D9.");

                    var parts = graph.GetBodyChildren(host).ToList();
                    Assert.That(parts, Is.Not.Empty);
                    foreach (var (part, component) in parts)
                        Assert.That(entities.GetComponent<WoundableComponent>(part).Profile,
                            Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("OrganicBodyPartProfile")),
                            $"protogen is organic, not cybernetic (U4(a)); {component.PartType} must prove it.");

                    // P5-D19 / U12′, flipped by M4 (OD16 parity, plan §9.2 group C): the brain, heart and lungs
                    // carry Wolfmed data now (marked parent edits in _Mono/Body/Organs/protogen.yml). The other
                    // protogen organs still carry none, as the human's eyes-to-kidneys set is outside §9.1's checks.
                    foreach (var (organ, _) in graph.GetBodyOrgans(host))
                    {
                        var vital = entities.HasComponent<BrainComponent>(organ) ||
                                    entities.HasComponent<HeartComponent>(organ) ||
                                    entities.HasComponent<LungComponent>(organ);
                        Assert.That(entities.HasComponent<OrganDamageComponent>(organ), Is.EqualTo(vital),
                            "protogen brain, heart and lungs carry OrganDamage (M4); the rest do not.");
                    }
                }
            });

            foreach (var host in hosts)
                entities.DeleteEntity(host);
        });

        // The WP9 crash class the exclusion system's own comment records: a RemCompDeferred/_deleteSet assert
        // while a host tears down.
        await Pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            foreach (var host in hosts)
                Assert.That(entities.Deleted(host), Is.True);
        }));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-20 (U17(a) shipped). Stops "diona limbs cannot be severed" being read as "diona limbs
    /// are indestructible": <c>amputationThresholds: {}</c> disables <c>AmputationSystem</c> only, while the
    /// inherited <c>MajorLimb</c> gib rung still deletes the limb outright — so no thrown limb, no stump
    /// <c>AmputationConsequenceWound</c>, no reattachment.
    /// </summary>
    [Test]
    public async Task DionaLimbIsDestroyedNotSeveredTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var arm = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobDiona", map.GridCoords);
            arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).AmputationThresholds, Is.Empty);
            // 215 Slash clears MajorLimb's Slash rung of 210. WFWolfmedPartDiona declares no Destructible of its
            // own, so the limb keeps the trigger every organic arm and leg carries (Body/Parts/base.yml).
            // M1b: somebody deals it; damage with no origin stops at the limb's ambient ceiling.
            // M3 (P19): the Blunt rung this used (190) is 400 now, above every limb's Blunt sever threshold; the
            // Slash rung is unchanged, and a diona limb still cannot be severed, so it is still destroyed.
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 215), attacker,
                ignoreResistances: true));
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() => Assert.That(entities.Deleted(arm), Is.True,
            "an unseverable limb is still destructible - the plant profile changes amputation, not " +
            "Destructible (PLAN5 §8.1a, U17(a))."));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-14. Enforces P5-D3 permanently and loudly. Wolfgate's trimmed
    /// <c>CirculatoryStreamSystem.SetBleedRates</c> is the only reachable stream-aware path, and a profile
    /// that named a second stream would silently zero that species' bleeding with no log at all.
    /// </summary>
    [Test]
    public async Task EveryBodyPartProfileUsesThePrimaryStreamTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var profiles = prototypes.EnumeratePrototypes<BodyPartProfilePrototype>().ToList();
            Assert.That(profiles, Has.Count.GreaterThanOrEqualTo(5),
                "phase 5 brings the shipped set to the five Onyx profiles plus any test fixtures.");

            Assert.Multiple(() =>
            {
                foreach (var profile in profiles)
                    Assert.That(profile.CirculatoryStream, Is.EqualTo(CirculatoryStreamPrototype.PrimaryStream),
                        $"{profile.ID} names a non-primary circulatory stream. Wolfgate has exactly one blood " +
                        "solution per body, so SetBleedRates would drop that species' bleed rate to zero " +
                        "silently. Onyx sets this key on none of its own five profiles either (P5-D3).");
            });
        });
    }

    /// <summary>
    /// IPC limbs are markings, and the Arms category holds both sides. Losing one arm used to take both arms
    /// and both hands off the body sprite and put all four on the dropped limb.
    /// </summary>
    [Test]
    public async Task IpcLosesOnlyTheSeveredArmTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(entities.System<AmputationSystem>().TryAmputate(body, arm));

            var markings = entities.GetComponent<Content.Shared.Humanoid.HumanoidAppearanceComponent>(body)
                .MarkingSet.Markings.Values.SelectMany(list => list).Select(marking => marking.MarkingId).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(markings, Does.Not.Contain("MobIPCLArmDefault"), "the severed arm leaves the body.");
                Assert.That(markings, Does.Contain("MobIPCRArmDefault"), "the other arm stays.");
                Assert.That(markings, Does.Contain("MobIPCRHandDefault"), "and so does its hand.");
            });
        });
    }

    /// <summary>
    /// Routed damage had no ceiling: ten parts and no per-limb cap let a burning body climb into the
    /// thousands. M1b: a living body's parts each stop at the ambient per-part ceiling (0.8 of the part's lowest
    /// destruction threshold; the torso keeps its own 250), so nothing a fire does is stored past it.
    /// </summary>
    [Test]
    public async Task BodyDamageIsCappedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // An IPC is the worst case: no ashing trigger, so a burning chassis never sheds the limbs that hold
            // the damage.
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var parts = entities.System<SharedBodySystem>().GetBodyChildren(body).Select(part => part.Id).ToList();
            var ceilings = entities.System<Content.Shared._WF.Wolfmed.Body.WolfmedBodyPartSystem>();

            // Small hits, like fire: under every finishing-hit threshold, so nothing is severed. 300 in all per
            // part, past every ceiling including the torso's 250.
            for (var i = 0; i < 60; i++)
            {
                foreach (var part in parts)
                    Routing(entities).TryApplyPartDamage(body, part, Spec("Heat", 5), null, ignoreResistances: true);
            }

            Assert.Multiple(() =>
            {
                foreach (var part in parts)
                {
                    var stored = entities.GetComponent<DamageableComponent>(part).TotalDamage;
                    var ceiling = ceilings.AmbientCeiling(part) ?? FixedPoint2.New(250);
                    var own = ceilings.Get(part).MaxDamage;
                    if (own > FixedPoint2.Zero)
                        ceiling = FixedPoint2.Min(ceiling, own);
                    Assert.That(stored, Is.LessThanOrEqualTo(ceiling + 1), $"{entities.ToPrettyString(part)} stored past its ceiling.");
                    Assert.That(stored, Is.GreaterThan(ceiling - 10), "the ceiling is a ceiling, not a refusal to take damage.");
                }
            });
        });
    }

    /// <summary>
    /// The ceiling is for damage nobody dealt. A corpse drifts up to it by itself (cold, blood loss), and when the
    /// ceiling also swallowed attacks a dead body could not be wounded or dismembered at all.
    /// </summary>
    [Test]
    public async Task CappedCorpseCanStillBeDismemberedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var cap = FixedPoint2.New(server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>()
                .GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BodyDamageCap));

            // M1b: the body-wide ceiling is the corpse ceiling now, so the body is dead first.
            entities.System<Content.Shared.Mobs.Systems.MobStateSystem>()
                .ChangeMobState(body, Content.Shared.Mobs.MobState.Dead);
            Assert.That(entities.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsDead(body), Is.True);

            // Ambient damage runs the body up to the ceiling and stops there.
            for (var i = 0; i < 200; i++)
            {
                foreach (var part in entities.System<SharedBodySystem>().GetBodyChildren(body).Select(p => p.Id).ToList())
                    Routing(entities).TryApplyPartDamage(body, part, Spec("Cold", 5), null, ignoreResistances: true);
            }

            Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.LessThanOrEqualTo(cap + 1));

            // An attacker's blade still lands, and takes the arm off.
            for (var i = 0; i < 12 && entities.System<SharedBodySystem>().BodyHasChild(body, arm); i++)
                Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 40), attacker, ignoreResistances: true);

            Assert.That(entities.System<SharedBodySystem>().BodyHasChild(body, arm), Is.False,
                "a corpse at the damage ceiling must still be dismemberable.");
        });
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static WoundDamageRoutingSystem Routing(IEntityManager entities) =>
        entities.System<WoundDamageRoutingSystem>();

    private static Entity<WoundComponent> FindWound(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part,
        string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype));
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
