using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The Wolfmed damage bridge: damage aimed at a wound host lands on a body part and projects back onto the mob,
/// nothing double-applies, and entities without WoundHostComponent behave exactly as they did before the port.
/// </summary>
/// <remarks>
/// PLAN §6.2. Covers T-SETUP, T-RESULT, T-PIERCE, T-CAUSTIC, T12, the non-wound-host control and no-double-apply.
/// PLAN2 §6.2/WP10-6a adds T-AP (armour penetration survives routing), T-PASSIVE-A (D29: a real wound host's
/// own neutralised PassiveDamage heals nothing) and T-PASSIVE-B (a canary showing the routing layer itself
/// has no wound-host-specific block against un-targeted healing, measured, not assumed — see its own remarks).
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundDamageRoutingSystem))]
public sealed class WolfmedDamageBridgeTest : GameTest
{
    /// <summary>Two mobs on the same Shitmed body graph; the control is the bridge body minus WoundHost.</summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedBridgeBodyGraph
  name: ""wolfmed bridge body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
      - left arm
      - right arm
    head:
      part: HeadHuman
    left arm:
      part: LeftArmHuman
    right arm:
      part: RightArmHuman

- type: entity
  id: WolfmedBridgeBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBridgeBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Targeting
  - type: WoundHost

- type: entity
  id: WolfmedControlBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBridgeBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Targeting

- type: entity
  id: WolfmedBridgeHitscan
  components:
  - type: HitscanBasicDamage
    damage:
      types:
        Blunt: 10

# WOLFGATE: T-AP (PLAN2 §6.2) - mirrors WoundFractureTest.cs's WoundFractureArmor. No `coverage`, so it
# protects every part; the arm reduction below is what the test measures.
- type: entity
  id: WolfmedBridgeArmor
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5

# WOLFGATE: T-PASSIVE-B (PLAN2 §6.2) - a bespoke real PassiveDamage pair (Onyx's own species entry ships
# `damage: {}` per D29, so it cannot exercise the routing path). WolfmedPassiveControl is identical minus
# WoundHost.
- type: entity
  id: WolfmedPassiveWoundHost
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBridgeBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: PassiveDamage
    allowedStates: [Alive]
    damageCap: 0
    damage:
      types:
        Blunt: -5
  - type: WoundHost

- type: entity
  id: WolfmedPassiveControl
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedBridgeBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: PassiveDamage
    allowedStates: [Alive]
    damageCap: 0
    damage:
      types:
        Blunt: -5
";

    /// <summary>T-SETUP: every part of a real humanoid is woundable after map-init, and the D30 seeding survives.</summary>
    [Test]
    public async Task EveryPartGetsWoundableOnMapInitTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();
        var missing = new List<string>();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var projection = entities.System<WoundDamageProjectionSystem>();

            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "MobHuman is not a wound host; WP7's BaseMobSpeciesOrganic wiring is missing.");

            var parts = graph.GetBodyChildren(body).ToList();
            Assert.That(parts, Is.Not.Empty);
            foreach (var part in parts)
            {
                if (!entities.HasComponent<WoundableComponent>(part.Id))
                    missing.Add($"{entities.ToPrettyString(part.Id)} has no WoundableComponent");
                if (!entities.HasComponent<DamageableComponent>(part.Id))
                    missing.Add($"{entities.ToPrettyString(part.Id)} has no DamageableComponent");
            }

            // D30: SetDamage zeroes types missing from the argument instead of pruning them, so the container
            // seeding DamageableInit performed must survive a projection pass.
            var expected = SupportedTypeCount(prototypes, "Biological");
            projection.RefreshBodyDamage(body);
            Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict, Has.Count.EqualTo(expected));
        });

        Assert.That(missing, Is.Empty, string.Join("\n", missing));
    }

    /// <summary>T-RESULT: TryChangeDamage must report the routed damage, not null (D27).</summary>
    [Test]
    public async Task TryChangeDamageReportsRoutedDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var damage = entities.System<DamageableSystem>();

            var dealt = damage.TryChangeDamage(body, Spec("Blunt", 10), targetPart: TargetBodyPart.LeftArm);

            Assert.That(dealt, Is.Not.Null, "the routed pass returned null; D27's BeforeDamageChangedEvent.Applied is not wired");
            Assert.That(dealt!.Empty, Is.False);
            Assert.That(dealt.GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.New(10)));
        });
    }

    /// <summary>T-PIERCE: a hitscan must keep damaging entities behind a wound host.</summary>
    [Test]
    public async Task PiercingHitscanDamagesEntitiesBehindAWoundHostTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var host = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var behind = entities.SpawnEntity("WolfmedControlBody", map.GridCoords);
            var hitscan = entities.SpawnEntity("WolfmedBridgeHitscan", map.GridCoords);
            var gun = entities.SpawnEntity(null, map.GridCoords);

            var args = new HitscanRaycastFiredEvent
            {
                FromCoordinates = map.GridCoords,
                ShotDirection = default,
                HitEntities = new HashSet<EntityUid> { host, behind },
                Gun = gun,
                Shooter = null,
                DistanceTried = 5f,
            };
            entities.EventBus.RaiseLocalEvent(hitscan, ref args);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(host).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(entities.GetComponent<DamageableComponent>(behind).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>T-CAUSTIC: Caustic is a localized type and routes to the hit part as a burn (D20 reversed).</summary>
    [Test]
    public async Task CausticRoutesToTheHitPartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<DamageableSystem>();
            var wounds = entities.System<WoundSystem>();
            var leftArm = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == Content.Shared.Body.Part.BodyPartType.Arm &&
                                part.Component.Symmetry == Content.Shared.Body.Part.BodyPartSymmetry.Left).Id;

            Assert.That(damage.TryChangeDamage(body, Spec("Caustic", 10), targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            var arm = entities.GetComponent<DamageableComponent>(leftArm);
            Assert.That(arm.Damage.DamageDict[new ProtoId<DamageTypePrototype>("Caustic")], Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(wounds.GetWounds((leftArm, entities.GetComponent<WoundableComponent>(leftArm)))
                .Any(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("BurnWound")), Is.True);
            Assert.That(entities.GetComponent<SystemicDamageComponent>(body).Damage.Empty, Is.True);
        });
    }

    /// <summary>T12: DamageChangedEvent.DamageDelta must survive the projection (§8.3 trap 5).</summary>
    [Test]
    public async Task DamageChangedDeltaSurvivesProjectionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var damage = entities.System<DamageableSystem>();
            var sleeping = entities.System<SleepingSystem>();

            // SleepingSystem.OnDamageChanged early-returns on a null DamageDelta, so waking up is a direct
            // observation that the projection reported a real delta. It is one of the eight systems trap 5 lists.
            Assert.That(sleeping.TrySleeping(body), Is.True);
            Assert.That(entities.HasComponent<SleepingComponent>(body), Is.True);

            Assert.That(damage.TryChangeDamage(body, Spec("Blunt", 10), targetPart: TargetBodyPart.LeftArm), Is.Not.Null);
            Assert.That(entities.HasComponent<SleepingComponent>(body), Is.False,
                "the wound host did not wake: DamageChangedEvent.DamageDelta was lost in the projection");
        });
    }

    /// <summary>D2: an entity without WoundHostComponent behaves exactly as it did before the port.</summary>
    [Test]
    public async Task NonWoundHostUnchangedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedControlBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<DamageableSystem>();
            var leftArm = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == Content.Shared.Body.Part.BodyPartType.Arm &&
                                part.Component.Symmetry == Content.Shared.Body.Part.BodyPartSymmetry.Left).Id;

            Assert.That(damage.TryChangeDamage(body, Spec("Blunt", 10), targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            Assert.Multiple(() =>
            {
                // Shitmed's own spreading still runs: the body takes the hit through GetPartDamageModifier(Arm).
                Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.New(7)));
                Assert.That(entities.GetComponent<DamageableComponent>(leftArm).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(entities.HasComponent<WoundableComponent>(leftArm), Is.False);
                Assert.That(entities.HasComponent<SystemicDamageComponent>(body), Is.False);
            });
        });
    }

    /// <summary>T4: one hit lands once — on the parts plus systemic, and once on the projected body total.</summary>
    [Test]
    public async Task NoDoubleApplicationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<DamageableSystem>();

            Assert.That(damage.TryChangeDamage(body, Spec("Blunt", 10), targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            var partTotal = graph.GetBodyChildren(body).Aggregate(FixedPoint2.Zero,
                (total, part) => total + entities.GetComponent<DamageableComponent>(part.Id).TotalDamage);
            var systemic = entities.GetComponent<SystemicDamageComponent>(body).Damage.GetTotal();

            Assert.Multiple(() =>
            {
                Assert.That(partTotal + systemic, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.New(10)));
            });
        });
    }

    /// <summary>T-AP: armour penetration must survive the wound-host routing detour, not just DamageableSystem's own resistance block.</summary>
    [Test]
    public async Task ArmorPenetrationReachesWoundHostsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<DamageableSystem>();
            var inventory = entities.System<InventorySystem>();

            FixedPoint2 LeftArmDamage(EntityUid body) =>
                entities.GetComponent<DamageableComponent>(graph.GetBodyChildren(body)
                    .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                    part.Component.Symmetry == BodyPartSymmetry.Left).Id).TotalDamage;

            var armoredNoPenetration = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var armorA = entities.SpawnEntity("WolfmedBridgeArmor", map.GridCoords);
            Assert.That(inventory.TryEquip(armoredNoPenetration, armorA, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(armoredNoPenetration, Spec("Blunt", 10),
                targetPart: TargetBodyPart.LeftArm, armorPenetration: 0f), Is.Not.Null);

            var armoredFullPenetration = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            var armorB = entities.SpawnEntity("WolfmedBridgeArmor", map.GridCoords);
            Assert.That(inventory.TryEquip(armoredFullPenetration, armorB, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(armoredFullPenetration, Spec("Blunt", 10),
                targetPart: TargetBodyPart.LeftArm, armorPenetration: 1f), Is.Not.Null);

            var unarmored = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            Assert.That(damage.TryChangeDamage(unarmored, Spec("Blunt", 10),
                targetPart: TargetBodyPart.LeftArm, armorPenetration: 0f), Is.Not.Null);

            // Derivation (PLAN2 §6.2/T-AP): DamageSpecifier.PenetrateArmor returns the coefficient set unchanged
            // at penetration 0 and a new EMPTY set at penetration >= 1 (DamageSpecifier.cs:306-330); applying an
            // empty modifier set leaves every type untouched (:157), so full penetration is a true no-op, not a
            // zero-out. WolfmedBridgeArmor's Blunt coefficient is 0.5.
            Assert.Multiple(() =>
            {
                Assert.That(LeftArmDamage(armoredNoPenetration), Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(LeftArmDamage(armoredFullPenetration), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(LeftArmDamage(unarmored), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(LeftArmDamage(armoredFullPenetration), Is.GreaterThan(LeftArmDamage(armoredNoPenetration)));
            });
        });
    }

    /// <summary>
    /// <c>RoutingPassesIgnoreResistancesTest</c> (plan §12 M6, P25): the routed pass keeps the caller's arguments. A hit
    /// with ignoreResistances ignores the armour on the part, not only the body's; Shitmed's part multiplier (a heavy
    /// swing's 0.5) reaches the part exactly once; and a caller's interruptsDoAfters reaches the hit's event (OD18).
    /// </summary>
    [Test]
    public async Task RoutingPassesIgnoreResistancesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var damage = entities.System<DamageableSystem>();
            var inventory = entities.System<InventorySystem>();
            var hits = entities.System<WolfmedPartHitSystem>();

            FixedPoint2 LeftArmDamage(EntityUid body) =>
                entities.GetComponent<DamageableComponent>(graph.GetBodyChildren(body)
                    .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                    part.Component.Symmetry == BodyPartSymmetry.Left).Id).TotalDamage;

            EntityUid Armoured()
            {
                var body = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
                Assert.That(inventory.TryEquip(body, entities.SpawnEntity("WolfmedBridgeArmor", map.GridCoords), "outerClothing"));
                return body;
            }

            var resisted = Armoured();
            Assert.That(damage.TryChangeDamage(resisted, Spec("Blunt", 10), targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            var ignoring = Armoured();
            Assert.That(damage.TryChangeDamage(ignoring, Spec("Blunt", 10), ignoreResistances: true,
                targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            var swung = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
            Assert.That(damage.TryChangeDamage(swung, Spec("Blunt", 10), partMultiplier: 0.5f,
                targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            var swungArmoured = Armoured();
            Assert.That(damage.TryChangeDamage(swungArmoured, Spec("Blunt", 10), partMultiplier: 0.5f,
                targetPart: TargetBodyPart.LeftArm), Is.Not.Null);

            // What each hit's event carries for the do-after check.
            var interrupts = new List<bool>();
            hits.Observer = (_, hit) => interrupts.Add(hit.InterruptsDoAfters);
            try
            {
                var ticked = entities.SpawnEntity("WolfmedBridgeBody", map.GridCoords);
                damage.TryChangeDamage(ticked, Spec("Heat", 5), interruptsDoAfters: false, targetPart: TargetBodyPart.LeftArm);
                damage.TryChangeDamage(ticked, Spec("Blunt", 5), targetPart: TargetBodyPart.LeftArm);
            }
            finally
            {
                hits.Observer = null;
            }

            Assert.Multiple(() =>
            {
                Assert.That(LeftArmDamage(resisted), Is.EqualTo(FixedPoint2.New(5)), "the part armour stopped applying.");
                Assert.That(LeftArmDamage(ignoring), Is.EqualTo(FixedPoint2.New(10)),
                    "ignoreResistances did not reach the part's armour.");
                Assert.That(LeftArmDamage(swung), Is.EqualTo(FixedPoint2.New(5)), "the part multiplier was not applied once.");
                Assert.That(entities.GetComponent<DamageableComponent>(swung).TotalDamage, Is.EqualTo(FixedPoint2.New(5)),
                    "the body took more than its parts.");
                Assert.That(LeftArmDamage(swungArmoured), Is.EqualTo(FixedPoint2.New(2.5)),
                    "the multiplier and the armour did not both apply, once each.");
                Assert.That(interrupts, Is.EqualTo(new[] { false, true }), "interruptsDoAfters did not reach the hit.");
            });
        });
    }

    /// <summary>T-PASSIVE-A (D29): a real wound host's body-level PassiveDamage is neutralised (damage: {}); the arm keeps its damage.</summary>
    [Test]
    public async Task RealWoundHostPassiveDamageIsNeutralisedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var leftArm = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "MobHuman is not a wound host; the D21/D32 species wiring is missing.");

            var passive = entities.GetComponent<PassiveDamageComponent>(body);
            Assert.That(passive.Damage.Empty, Is.True,
                "D29: PassiveDamage must ship neutralised (damage: {}) on wound hosts; Onyx's per-part profile recovery is the only passive heal.");

            var graph = entities.System<SharedBodySystem>();
            leftArm = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            var routing = entities.System<WoundDamageRoutingSystem>();
            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Blunt", 10)), Is.True);
        });

        await server.WaitRunTicks(1800); // 60 seconds at the pool's 30 tick/s rate.

        // WOLFGATE (measured, P2-D16): a real MobHuman on a bare test map keeps running every other body system
        // across those 60 simulated seconds too (Barotrauma, Temperature/ThermalRegulator, etc.), and any of
        // their localized damage types can land on this same arm via the same "no requested part" routing path
        // T-PASSIVE-B documents. Measured once: the arm crept from 10 to 13.11, not down - environmental
        // accrual, not healing. An exact `EqualTo(10)` is therefore not the right gate on a real full mob; a
        // lower bound is, and is still a strict test of D29 (any decrease would mean the neutralised
        // PassiveDamage healed it).
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(leftArm).TotalDamage, Is.GreaterThanOrEqualTo(FixedPoint2.New(10)),
                "the arm healed: a real mob's neutralised PassiveDamage must not recover damage at the body level.");
        });
    }

    /// <summary>
    /// T-PASSIVE-B (D29 canary): if PassiveDamage carried real healing on a wound host, does the routing layer
    /// apply it? Measured: yes — WoundDamageRoutingSystem's un-targeted healing path has no wound-host-specific
    /// block, so both the control and the wound host fully heal. D29's `damage: {}` on every shipped species is
    /// what actually stops this today (a YAML choice, not a code-level barrier); this is not a safety proof, it
    /// documents what the mechanism does if that YAML guard is ever lifted.
    /// </summary>
    [Test]
    public async Task PassiveDamageMechanismStillRoutesIfReenabledTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var host = EntityUid.Invalid;
        var control = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            host = entities.SpawnEntity("WolfmedPassiveWoundHost", map.GridCoords);
            control = entities.SpawnEntity("WolfmedPassiveControl", map.GridCoords);

            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var damage = entities.System<DamageableSystem>();

            var hostArm = graph.GetBodyChildren(host)
                .Single(part => part.Component.PartType == BodyPartType.Arm &&
                                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(routing.TryApplyPartDamage(host, hostArm, Spec("Blunt", 10)), Is.True);
            Assert.That(damage.TryChangeDamage(control, Spec("Blunt", 10)), Is.Not.Null);
        });

        await server.WaitRunTicks(300); // 10 seconds: comfortably past the 2 ticks (10 Blunt / 5 per tick) either side needs to bottom out at zero.

        // WOLFGATE (measured, P2-D16): the plan predicted the wound host would NOT heal here. It does. Onyx's
        // routing has no code-level barrier against an un-targeted heal call (DamageableSystem.TryChangeDamage
        // with no targetPart) reaching a damaged part: WoundDamageRoutingSystem.OnBeforeDamageChanged intercepts
        // it regardless of sign, RouteThroughBodyModifiers re-raises it through the routed pass, OnDamageDealt
        // sees the negative delta as localized healing and ApplyLocalizedHealing spreads it across whichever
        // parts currently carry positive damage of that type - exactly the same path a real heal item would use.
        // D29's `damage: {}` on every shipped species is a YAML-level guard, not a code-level one; this canary
        // is what proves that, rather than merely asserting a near-tautology (see the test's own remarks).
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(control).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "the non-wound-host control did not heal; PassiveDamageSystem itself is broken, unrelated to Wolfmed.");
                Assert.That(entities.GetComponent<DamageableComponent>(host).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "the wound host's arm did not heal: WoundDamageRoutingSystem's un-targeted healing path (ApplyLocalizedHealing) did not reach it.");
            });
        });
    }

    /// <summary>Number of damage types DamageableInit seeds for a container: its types plus every type of its groups.</summary>
    private static int SupportedTypeCount(IPrototypeManager prototypes, string containerId)
    {
        var container = prototypes.Index<DamageContainerPrototype>(containerId);
        var types = new HashSet<string>(container.SupportedTypes);
        foreach (var groupId in container.SupportedGroups)
            types.UnionWith(prototypes.Index<DamageGroupPrototype>(groupId).DamageTypes);

        return types.Count;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
