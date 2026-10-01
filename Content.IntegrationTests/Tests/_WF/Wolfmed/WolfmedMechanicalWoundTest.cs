#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W6: what a hit does to a chassis rather than to flesh, and the wall between the two. Five mechanical
/// wounds that only an IPC or a cybernetic limb can carry, the organic set that a chassis can never carry,
/// and the tools that put each of them right.
/// </summary>
/// <remarks>
/// The wounds and their thresholds are data (<c>_WF/Wolfmed/Wounds/mechanical.yml</c> and the rules); the
/// new behaviour is <see cref="WolfmedShortCircuitSystem"/> and
/// <see cref="WolfmedOverheatingSystem"/>, plus the organic gate added to
/// <see cref="WolfmedInfectionSystem"/> and <see cref="WolfmedNecrosisSystem"/>.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedOverheatingSystem))]
public sealed class WolfmedMechanicalWoundTest : GameTest
{
    /// <summary>Wounds that belong to flesh and must never appear on a chassis.</summary>
    private static readonly string[] OrganicOnly =
    [
        "WFWolfmedCrushInjuryWound",
        "WFWolfmedConcussionWound",
        "WFWolfmedDislocationWound",
        "WFWolfmedArterialBleedWound",
        "WFWolfmedTendonCutWound",
        "WFWolfmedAvulsionWound",
        "WFWolfmedFrostbiteWound",
        "WFWolfmedChemicalBurnWound",
        "WFWolfmedCharringWound",
        "WFWolfmedInternalBurnWound",
        "WFWolfmedNecrosisWound",
        "WFWolfmedGunshotWound",
        "WFWolfmedLodgedRoundWound",
        "WFWolfmedShrapnelWound",
    ];

    /// <summary>Wounds that belong to a chassis and must never appear on flesh.</summary>
    private static readonly string[] MechanicalOnly =
    [
        "WFWolfmedDentWound",
        "WFWolfmedBreachWound",
        "WFWolfmedShortCircuitWound",
        "WFWolfmedServoDamageWound",
        "WFWolfmedOverheatingWound",
    ];

    /// <summary>
    /// The wall itself, from both sides: the part profiles refuse the other set outright, and real hits
    /// that would make an organic wound on flesh make nothing of the sort on a chassis.
    /// </summary>
    [Test]
    public async Task OrganicAndMechanicalWoundsDoNotCrossTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();

            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            var chassis = Part(entities, machine, BodyPartType.Torso);
            var torso = Part(entities, flesh, BodyPartType.Torso);

            Assert.Multiple(() =>
            {
                Assert.That(traits.IsMechanical(chassis), Is.True);
                Assert.That(traits.IsOrganic(chassis), Is.False);
                Assert.That(traits.IsMechanical(torso), Is.False);
                Assert.That(traits.IsOrganic(torso), Is.True);
            });

            Assert.Multiple(() =>
            {
                foreach (var id in OrganicOnly)
                {
                    Assert.That(wounds.CanCreateWound(torso, id), Is.True, $"{id} belongs on flesh.");
                    Assert.That(wounds.CanCreateWound(chassis, id), Is.False, $"{id} must not reach a chassis.");
                }

                foreach (var id in MechanicalOnly)
                {
                    Assert.That(wounds.CanCreateWound(chassis, id), Is.True, $"{id} belongs on a chassis.");
                    Assert.That(wounds.CanCreateWound(torso, id), Is.False, $"{id} must not reach flesh.");
                }
            });

            // And the rules agree with the profiles: the same four hits that make organic wounds on a
            // human make only mechanical ones on an IPC.
            Damage(entities, machine, TargetBodyPart.Torso, "Blunt", 40);
            Damage(entities, machine, TargetBodyPart.Torso, "Slash", 30);
            Damage(entities, machine, TargetBodyPart.Torso, "Cold", 30);
            Damage(entities, machine, TargetBodyPart.Torso, "Shock", 40);

            var found = Prototypes(entities, wounds, chassis);
            Assert.Multiple(() =>
            {
                foreach (var id in OrganicOnly)
                    Assert.That(found, Does.Not.Contain(id));

                Assert.That(found, Does.Contain("WFWolfmedDentWound").And.Contain("WFWolfmedBreachWound")
                        .And.Contain("WFWolfmedShortCircuitWound"),
                    "a chassis gets the mechanical answers to the same four hits.");
                Assert.That(found, Does.Contain("IpcMechanicalDamageWound"),
                    "and the generic chassis wound is still the total, because W6 adds rather than replaces.");
            });

            // The other direction: a human takes none of the five, whatever hits them.
            foreach (var (type, amount) in new[] { ("Blunt", 40), ("Slash", 30), ("Heat", 40), ("Shock", 40) })
                Damage(entities, flesh, TargetBodyPart.Torso, type, amount);

            var organic = Prototypes(entities, wounds, torso);
            Assert.Multiple(() =>
            {
                foreach (var id in MechanicalOnly)
                    Assert.That(organic, Does.Not.Contain(id));
            });
        });
    }

    /// <summary>
    /// A dent is blunt force on something that does not bruise, and a breach is the mechanical bleed: it
    /// never clots, and a round that opened it has to come out before anything can seal it.
    /// </summary>
    [Test]
    public async Task DentsAndBreachesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var embedded = entities.System<WolfmedEmbeddedObjectSystem>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var chassis = Part(entities, body, BodyPartType.Torso);

            // severityMultiplier 0.9: 20 Blunt is an 18-severity dent, short of the 30 the penalty starts at.
            Damage(entities, body, TargetBodyPart.Torso, "Blunt", 20);
            var dent = FindWound(entities, wounds, chassis, "WFWolfmedDentWound");
            Assert.That(entities.GetComponent<WoundComponent>(dent).Severity, Is.EqualTo(FixedPoint2.New(18)));

            // A breach leaks and never clots, which is the whole difference from an organic cut.
            Damage(entities, body, TargetBodyPart.Torso, "Slash", 20);
            var breach = FindWound(entities, wounds, chassis, "WFWolfmedBreachWound");
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(breach).Severity, Is.EqualTo(FixedPoint2.New(16)));
                Assert.That(Rate(entities, breach), Is.GreaterThan(0f), "a breach leaks.");
                Assert.That(entities.GetComponent<WoundBleedingComponent>(breach).AutomaticClottingAt, Is.Null,
                    "and nothing in a chassis is trying to clot it.");
            });

            // Welding is damage removal, so it closes both. That is why the two carry healingMultiplier 1.
            Assert.That(routing.TryApplyPartDamage(body, chassis, Spec("Blunt", -40), null,
                ignoreResistances: true, healWounds: true), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, chassis, Spec("Slash", -40), null,
                ignoreResistances: true, healWounds: true), Is.True);
            Assert.That(Prototypes(entities, wounds, chassis),
                Does.Not.Contain("WFWolfmedDentWound").And.Not.Contain("WFWolfmedBreachWound"));

            // A round keeps the chassis shut: W1's embedded machinery on an ordinary breach, so the repair
            // is refused until the object is out rather than needing a mechanical lodged-round wound.
            var shot = entities.SpawnEntity("MobIPC", map.GridCoords);
            var shotChassis = Part(entities, shot, BodyPartType.Torso);
            var round = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            entities.System<DamageableSystem>().TryChangeDamage(shot, Spec("Piercing", 20),
                origin: null, targetPart: TargetBodyPart.Torso, tool: round);

            var pierced = FindWound(entities, wounds, shotChassis, "WFWolfmedBreachWound");
            Assert.Multiple(() =>
            {
                Assert.That(embedded.GetPartCount(shotChassis), Is.GreaterThan(0),
                    "a round that went into a chassis is still in it.");
                Assert.That(wounds.TreatWound(pierced, FixedPoint2.MaxValue), Is.False,
                    "and nothing seals the hole around it.");
            });

            Assert.That(embedded.TryTakeOne(pierced, out _), Is.True);
            Assert.That(wounds.TreatWound(pierced, FixedPoint2.MaxValue), Is.True,
                "once it is out, the breach welds shut like any other.");
        });
    }

    /// <summary>
    /// Current in a chassis locks the frame up and sparks; the wiring behind a cut panel does not, and
    /// both are the cable coil's job rather than the welder's.
    /// </summary>
    [Test]
    public async Task ShortCircuitAndServoDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var chassis = Part(entities, body, BodyPartType.Torso);

            // The IPC modifier set multiplies Shock by 2.5 before routing, so 12 reaches the part as 30,
            // and the rule's severityMultiplier 0.7 makes that a 21-severity short. M6 (P25): this file's Damage
            // helper ignores resistances, which the routing now honours, so this hit takes the resistances.
            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Shock", 12), origin: null,
                targetPart: TargetBodyPart.Torso);
            var short_ = FindWound(entities, wounds, chassis, "WFWolfmedShortCircuitWound");

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(short_).Severity,
                    Is.EqualTo(FixedPoint2.New(21)));
                Assert.That(entities.HasComponent<StunnedComponent>(body), Is.True,
                    "the frame locks up each time the short lands.");
                Assert.That(traits.TryGetBehavior(short_, out WolfmedShortCircuitBehavior arc), Is.True);
                Assert.That(arc.Sound, Is.Not.Null, "and something sparks.");
                Assert.That(arc.Effect, Is.Not.Null);
            });

            // A cut actuator: the mechanical severed tendon, on a limb only.
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftLeg, "Slash", 30);
            var servo = FindWound(entities, wounds, leg, "WFWolfmedServoDamageWound");

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(servo).Severity, Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(traits.TryGetLimbPenalty(leg, mobility: true, out var limp), Is.True);
                Assert.That(limp, Is.LessThan(1f), "the limb it is on is lame until it is rewired.");
                Assert.That(Prototypes(entities, wounds, Part(entities, body, BodyPartType.Torso)),
                    Does.Not.Contain("WFWolfmedServoDamageWound"),
                    "there is no servo in a torso to sever.");
            });

            // The servo wound carries no damage type, so welding the panel - which is damage removal -
            // leaves it exactly where it was, and so would the cable coil's own healing block.
            var before = entities.GetComponent<WoundComponent>(servo).Severity;
            Assert.That(entities.System<WoundDamageRoutingSystem>().TryApplyPartDamage(body, leg,
                Spec("Slash", -30), null, ignoreResistances: true, healWounds: true), Is.True);
            Assert.That(entities.GetComponent<WoundComponent>(servo).Severity, Is.EqualTo(before),
                "welding the panel shut does nothing for the run behind it.");

            // The exit is the procedure: open the limb, replace the run with a cable coil, seal it.
            Assert.Multiple(() =>
            {
                Assert.That(prototypes.Index<WoundPrototype>("WFWolfmedServoDamageWound").DamageTypes, Is.Empty);
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryReplaceServo"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryStepReplaceServo"));
                Assert.That(entities.HasComponent<WolfmedServoKitComponent>(
                    entities.SpawnEntity("CableApcStack", map.GridCoords)), Is.True,
                    "and the coil a mechanic already carries is the step's tool.");
            });

            // Which is what the step's effect does.
            wounds.TreatWound(servo, FixedPoint2.MaxValue);
            Assert.That(Prototypes(entities, wounds, leg), Does.Not.Contain("WFWolfmedServoDamageWound"));
        });
    }

    /// <summary>
    /// Overheating is the one injury whose treatment is time. It reaches no tool, sheds severity on its
    /// own tick, faster under cold, and in one step when the machine is hosed down.
    /// </summary>
    [Test]
    public async Task OverheatingCoolsRatherThanHealsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var overheating = entities.System<WolfmedOverheatingSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            // The IPC modifier set multiplies Heat by 1.5, so 40 reaches the part as 60; the rule's
            // severityMultiplier 0.8 makes that 48, past the 45 the worst stage starts at. M6 (P25): the Damage
            // helper ignores resistances, which the routing now honours, so this hit takes the resistances.
            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Heat", 40), origin: null,
                targetPart: TargetBodyPart.LeftArm);
            var wound = FindWound(entities, wounds, arm, "WFWolfmedOverheatingWound");

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.EqualTo(FixedPoint2.New(48)));
                Assert.That(overheating.IsOverheating(arm), Is.True);
                Assert.That(prototypes.Index<WoundPrototype>("WFWolfmedOverheatingWound").DamageTypes, Is.Empty,
                    "no damage type means no tool and no damage removal reaches it.");
                Assert.That(traits.TryGetLimbPenalty(arm, mobility: false, out var slow), Is.True);
                Assert.That(slow, Is.GreaterThan(1f), "a hot part works slowly.");
            });

            // Cold on the part is the cheap way to say "put it somewhere cold". M6: with resistances, as above; the
            // chassis's own Cold modifier is what leaves some heat for the next step to shed.
            var hot = entities.GetComponent<WoundComponent>(wound).Severity;
            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Cold", 50), origin: null,
                targetPart: TargetBodyPart.LeftArm);
            var cooled = entities.GetComponent<WoundComponent>(wound).Severity;
            Assert.That(cooled, Is.LessThan(hot), "cold takes heat out of it.");

            // And it cools with nothing helping at all, which no other wound in the model does.
            overheating.Update(60f);
            Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.LessThan(cooled));

            // A dousing, which is what the water touch reaction on both mob bases calls.
            Assert.That(overheating.Douse(body), Is.EqualTo(1));
            overheating.Update(600f);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, arm), Does.Not.Contain("WFWolfmedOverheatingWound"),
                    "left alone long enough, the part is simply cool again.");
                Assert.That(overheating.IsOverheating(arm), Is.False);
                Assert.That(overheating.Douse(body), Is.Zero);
            });
        });
    }

    /// <summary>
    /// A chassis cannot rot and cannot go septic, whatever is done to it: no source starts a necrosis
    /// clock on one, and a wound that would be infectable in flesh is inert on it.
    /// </summary>
    [Test]
    public async Task ChassisNeitherInfectsNorNecrosesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var infection = entities.System<WolfmedInfectionSystem>();
            var necrosis = entities.System<WolfmedNecrosisSystem>();

            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            var chassis = Part(entities, machine, BodyPartType.Torso);
            var torso = Part(entities, flesh, BodyPartType.Torso);

            // SurgicalIncisionWound carries W5's infection risk and is supported by both profiles, so it is
            // the one wound that can test the gate on identical data.
            var opened = wounds.CreateOrMergeWound(chassis, "SurgicalIncisionWound", 20);
            var cut = wounds.CreateOrMergeWound(torso, "SurgicalIncisionWound", 20);

            Assert.Multiple(() =>
            {
                Assert.That(cut, Is.Not.Null);
                Assert.That(opened, Is.Not.Null);
                Assert.That(entities.HasComponent<WolfmedInfectionComponent>(cut!.Value), Is.True,
                    "an open incision in flesh can go bad.");
                Assert.That(entities.HasComponent<WolfmedInfectionComponent>(opened!.Value), Is.False,
                    "the same incision in a chassis cannot.");
            });

            infection.Contaminate(opened.Value);
            Assert.That(entities.HasComponent<WolfmedInfectionComponent>(opened.Value), Is.False,
                "and digging something out of one with a dirty knife costs nothing later.");

            necrosis.OnTourniquetApplied(chassis);
            necrosis.Start(chassis, WolfmedNecrosisSource.Tourniquet, TimeSpan.FromMinutes(1));
            necrosis.OnDetached(chassis);

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WolfmedNecrosisComponent>(chassis), Is.False);
                Assert.That(necrosis.MakeNecrotic(chassis), Is.Null);
                Assert.That(necrosis.IsNecrotic(chassis), Is.False);
                Assert.That(necrosis.IsAtRisk(chassis), Is.False);
            });

            // Flesh is unaffected by the gate.
            necrosis.Start(torso, WolfmedNecrosisSource.Tourniquet, TimeSpan.FromMinutes(1));
            Assert.That(necrosis.IsAtRisk(torso), Is.True);
        });
    }

    /// <summary>
    /// A wrench is the fuel-free repair tool for a dent: the same do-after path a welder uses, with the
    /// fuel check skipped because it has none.
    /// </summary>
    [Test]
    public async Task WrenchTakesDentsOutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, part = default;
        Content.Shared.DoAfter.DoAfter? repair = null;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(user);

            part = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            Damage(entities, body, TargetBodyPart.LeftLeg, "Blunt", 12);
            Assert.That(Prototypes(entities, entities.System<WoundSystem>(), part),
                Does.Contain("WFWolfmedDentWound"));

            var tool = entities.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.LeftLeg;

            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            Assert.That(interact.Handled, Is.True, "a wrench with no fuel still starts a repair.");
            repair = entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.Single();
        });

        await Pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            Assert.That(repair!.Cancelled, Is.False);
            Assert.That(repair.Completed, Is.True);
            Assert.That(Prototypes(entities, entities.System<WoundSystem>(), part),
                Does.Not.Contain("WFWolfmedDentWound"), "the panel comes back out.");
        });
    }

    /// <summary>
    /// The analyzer names the five wounds and reads a chassis in chassis words: phase 5 shipped the
    /// -mechanical and -frame locale variants unconsumed, and W6's payload flag is what selects them.
    /// </summary>
    [Test]
    public async Task MechanicalAnalyzerWordingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();

            // Separate parts: W4's cautery seals a bleed on whatever part the heat lands on, chassis
            // included, so a torso that is both cut and burned would not be leaking by the time it is read.
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            Damage(entities, machine, TargetBodyPart.Torso, "Slash", 20);
            Damage(entities, machine, TargetBodyPart.LeftArm, "Heat", 40);

            var diagnostics = analyzer.BuildWoundDiagnostics(machine);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.Torso, out var chassis));
            Assert.That(diagnostics.Parts.TryGetValue(TargetBodyPart.LeftArm, out var hot));

            var flesh = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, flesh, TargetBodyPart.Torso, "Slash", 20);
            var human = analyzer.BuildWoundDiagnostics(flesh)!.Parts[TargetBodyPart.Torso];

            Assert.Multiple(() =>
            {
                Assert.That(chassis.Mechanical, Is.True);
                Assert.That(chassis.BleedingRate, Is.GreaterThan(0f));
                Assert.That(hot.Mechanical, Is.True);
                Assert.That(hot.Overheating, Is.True);
                Assert.That(human.Mechanical, Is.False);
                Assert.That(human.Overheating, Is.False);

                Assert.That(chassis.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-breach"));
                Assert.That(hot.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-overheating"));

                foreach (var key in new[]
                         {
                             "wolfmed-wound-name-dent",
                             "wolfmed-wound-name-breach",
                             "wolfmed-wound-name-short-circuit",
                             "wolfmed-wound-name-servo-damage",
                             "wolfmed-wound-name-overheating",
                             "wolfmed-wound-stage-warm",
                             "wolfmed-wound-stage-hot",
                             "wolfmed-wound-stage-overheated",
                             "wolfmed-overheating-doused",
                             "health-analyzer-wound-overheating-short",
                             "health-analyzer-wound-bleeding-short-mechanical",
                             "health-analyzer-wound-fracture-short-frame",
                             "health-analyzer-wound-fracture-treated-short-frame",
                         })
                    Assert.That(locale.HasString(key), Is.True, key);
            });
        });
    }

    private static void Damage(IEntityManager entities, EntityUid body, TargetBodyPart target,
        string type, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(type, amount),
            ignoreResistances: true, origin: null, targetPart: target);
    }

    private static float Rate(IEntityManager entities, EntityUid wound) =>
        entities.TryGetComponent(wound, out WoundBleedingComponent? bleeding) ? bleeding.CurrentRate : 0f;

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, WoundSystem wounds, EntityUid part)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
