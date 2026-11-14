using System.Linq;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10, Onyx's own Targeting stack is not vendored.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade + §2.7 TryDetachPart.
using Content.Shared._WF.Wolfmed.Targeting; // WOLFGATE(Wolfmed): D10 resolver.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Rejuvenate;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

[TestFixture]
[TestOf(typeof(WoundDamageRoutingSystem))]
public sealed class WoundDamageFoundationTest : GameTest
{
    // WOLFGATE(Wolfmed): Onyx's Nubody `InitialBody` + `organs:` becomes a Shitmed `body` prototype; `Injurable` is dropped
    // (D19); `partType: Chest` becomes `Torso` (D9); armour has no `coverage`/`partModifiers` in Wolfgate.
    [TestPrototypes]
    private const string Prototypes = @"
- type: bodyPartProfile
  id: WoundFoundationRestrictedProfile
  bleedingMultiplier: 0
  acceptedDamageTypes: [Blunt]
  supportedWounds: [BluntWound, SurgicalIncisionWound]

- type: body
  id: WoundFoundationBodyGraph
  name: ""wound foundation body""
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
  id: WoundFoundationBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WoundFoundationBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: PainShockTarget
  - type: WoundHost
  # WOLFGATE(Wolfmed): pain shock paralyses through Wolfgate's *old* status-effect system, which refuses any entity
  # without StatusEffectsComponent. Onyx's stun goes through StatusEffectNew and needs no such component.
  - type: StatusEffects
    allowed:
    - Stun
    - KnockedDown

- type: entity
  id: WoundFoundationArmor
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5

# WOLFGATE(Wolfmed): WP11-3, P3-D5: Onyx's four locational-armour fixtures. `coverage: [Chest]` becomes `[Torso]` (D9 —
# Wolfgate's BodyPartType has no Chest member and the Release lint rejects it), and Onyx's `WoundFoundationArmorAll`
# is renamed `WoundFoundationArmorAllHead` because it is worn in the *head* slot while protecting everything.
- type: entity
  id: WoundFoundationArmorHead
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    coverage: [Head]
    modifiers:
      coefficients:
        Blunt: 0.5

- type: entity
  id: WoundFoundationArmorAllHead
  components:
  - type: Clothing
    slots: [head]
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5

- type: entity
  id: WoundFoundationArmorLeftArm
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    coverage: [Arm]
    coverageSymmetry: [Left]
    modifiers:
      coefficients:
        Blunt: 0.5

- type: entity
  id: WoundFoundationArmorLocational
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    coverage: [Torso]
    modifiers:
      coefficients:
        Blunt: 0.8
    partModifiers:
    - parts: [Head]
      modifiers:
        coefficients:
          Blunt: 0.25
    - parts: [Arm]
      symmetry: [Left]
      modifiers:
        coefficients:
          Blunt: 0.5
    - parts: [Arm]
      symmetry: [Right]
      modifiers:
        coefficients:
          Blunt: 0.75

- type: entity
  id: WoundFoundationVanillaBody
  parent: InventoryBase
  components:
  - type: Damageable
    damageContainer: Biological

- type: entity
  id: WoundFoundationAttacker
  components:
  - type: Targeting
";

    [Test]
    public async Task TargetingContractAndRoutingTest()
    {
        // WOLFGATE(Wolfmed): Onyx's SharedTargetingSystem.TryConvert and TargetingComponent.DefaultOdds() are part of the
        // Targeting stack D10 declines to vendor. Wolfgate folds Groin onto the torso (D9) and phase 1 resolves
        // the requested part exactly, with no anatomical-odds scatter (§2.13), so only IsSelectable survives here.
        Assert.Multiple(() =>
        {
            Assert.That(SharedTargetingSystem.IsSelectable((TargetBodyPart) ushort.MaxValue), Is.False);
            Assert.That(SharedTargetingSystem.IsSelectable(TargetBodyPart.Arms), Is.False);
            Assert.That(SharedTargetingSystem.IsSelectable(TargetBodyPart.Groin), Is.True);
            Assert.That(SharedTargetingSystem.IsSelectable(TargetBodyPart.Head), Is.True);
        });

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            configuration.SetCVar(CCVars.TargetingEnabled, true);
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var attacker = entityManager.SpawnEntity("WoundFoundationAttacker", map.GridCoords);
            var targeting = entityManager.GetComponent<TargetingComponent>(attacker);
            var resolver = entityManager.System<WoundTargetResolver>(); // WOLFGATE(Wolfmed)
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            targeting.Target = TargetBodyPart.Head;
            Assert.That(resolver.TryResolve(body, attacker, out var resolvedHead), Is.True);
            Assert.That(resolvedHead, Is.EqualTo(head));

            // WOLFGATE(Wolfmed): D9: Groin folds onto the torso instead of being its own part.
            targeting.Target = TargetBodyPart.Groin;
            Assert.That(resolver.TryResolve(body, attacker, out var resolvedGroin), Is.True);
            Assert.That(resolvedGroin, Is.EqualTo(torso));

            targeting.Target = TargetBodyPart.LeftHand;
            Assert.That(resolver.TryResolve(body, attacker, out var resolvedHand), Is.True);
            Assert.That(resolvedHand, Is.EqualTo(leftArm));
            Assert.That(routing.TryApplyDamage(body, Spec("Blunt", 10), attacker), Is.True);
            Assert.That(damage.GetAllDamage(leftArm).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
        });

        await server.WaitPost(() =>
            configuration.SetCVar(CCVars.TargetingEnabled, CCVars.TargetingEnabled.DefaultValue));
    }

    [Test]
    public async Task CombatSnapshotsRemainStableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            configuration.SetCVar(CCVars.TargetingEnabled, true);
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var shooter = entityManager.SpawnEntity("WoundFoundationAttacker", map.GridCoords);
            var first = entityManager.SpawnEntity(null, map.GridCoords);
            var spread = entityManager.SpawnEntity(null, map.GridCoords);
            var thrown = entityManager.SpawnEntity(null, map.GridCoords);
            var targeting = entityManager.GetComponent<TargetingComponent>(shooter);
            var snapshots = entityManager.System<TargetingSnapshotSystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var graph = entityManager.System<SharedBodySystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            targeting.Target = TargetBodyPart.Head;
            Assert.That(snapshots.Capture(first, shooter));
            Assert.That(snapshots.Capture(spread, shooter));
            var thrownEvent = new ThrownEvent(shooter, thrown);
            entityManager.EventBus.RaiseLocalEvent(thrown, ref thrownEvent, true);

            targeting.Target = TargetBodyPart.LeftArm;
            Assert.That(entityManager.GetComponent<TargetingSnapshotComponent>(first).RequestedTarget, Is.EqualTo(TargetBodyPart.Head));
            Assert.That(entityManager.GetComponent<TargetingSnapshotComponent>(spread).RequestedTarget, Is.EqualTo(TargetBodyPart.Head));
            Assert.That(entityManager.GetComponent<TargetingSnapshotComponent>(thrown).RequestedTarget, Is.EqualTo(TargetBodyPart.Head));
            Assert.That(routing.TryApplyCarrierDamage(body, first, Spec("Blunt", 4), shooter, out _));
            Assert.That(routing.TryApplyCarrierDamage(body, spread, Spec("Blunt", 3), shooter, out _));
            Assert.That(routing.TryApplyCarrierDamage(body, thrown, Spec("Blunt", 2), shooter, out _));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(9)));
            Assert.That(damage.GetAllDamage(leftArm).GetTotal(), Is.EqualTo(FixedPoint2.Zero));

            Assert.That(routing.TryApplyTargetedDamage(body, Spec("Blunt", 1), TargetBodyPart.LeftArm, shooter, out _));
            Assert.That(damage.GetAllDamage(leftArm).GetTotal(), Is.EqualTo(FixedPoint2.New(1)));
        });

        await server.WaitPost(() =>
            configuration.SetCVar(CCVars.TargetingEnabled, CCVars.TargetingEnabled.DefaultValue));
    }

    [Test]
    public async Task RoutesAndProjectsDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            Assert.That(entityManager.HasComponent<WoundableComponent>(head));
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 10)));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(damage.GetAllDamage(body).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Caustic", 3)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Shock", 2)));
            Assert.That(damage.GetAllDamage(head).DamageDict[new ProtoId<DamageTypePrototype>("Caustic")],
                Is.EqualTo(FixedPoint2.New(3)));
            Assert.That(damage.GetAllDamage(torso).DamageDict[new ProtoId<DamageTypePrototype>("Shock")],
                Is.EqualTo(FixedPoint2.New(2)));
            Assert.That(entityManager.GetComponent<SystemicDamageComponent>(body).Damage.Empty, Is.True);

            Assert.That(routing.TryApplyDamage(body, Spec("Asphyxiation", 4)));
            Assert.That(entityManager.GetComponent<SystemicDamageComponent>(body).Damage.GetTotal(), Is.EqualTo(FixedPoint2.New(4)));
            Assert.That(damage.GetAllDamage(body).GetTotal(), Is.EqualTo(FixedPoint2.New(19)));

            Assert.That(wfBody.TryDetachPart(head)); // WOLFGATE(Wolfmed)
            // WOLFGATE(Wolfmed): the head's 13 leaves the projection (Onyx's 19 - 13 = 6), but Wolfgate's `HeadHuman`
            // is a vital part, so Shitmed's PartRemoveDamage (SharedBodySystem.Parts.cs:405) adds 100
            // Bloodloss on removal — systemic damage that the projection then includes. Onyx's bare test
            // part had no vitality, hence its flat 6.
            // WOLFGATE(Wolfmed): WP11-1, PLAN3 P3-D1: WolfmedBodyPartLifecycleSystem.ChargeVitalPartLoss now also
            // charges the lost vital part's own damage as systemic Bloodloss, so CheckVitalDamage cannot
            // drop when a head comes off. The head carried Blunt 10 + Caustic 3 = 13, and
            // BodyPartRemovedEvent (Parts.cs:357) fires before PartRemoveDamage (:362), so the systemic
            // Bloodloss is 13 + 100 = 113 and the projected body total is 6 + 113 = 119.
            // Was 100 / 106 before P3-D1.
            Assert.That(entityManager.GetComponent<SystemicDamageComponent>(body).Damage
                .DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Bloodloss")),
                Is.EqualTo(FixedPoint2.New(113)));
            Assert.That(damage.GetAllDamage(body).GetTotal(), Is.EqualTo(FixedPoint2.New(119)));
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 1)), Is.False);
        });
    }

    [Test]
    public async Task NonTargetingOriginUsesWeightedFallbackTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var source = entityManager.SpawnEntity(null, map.GridCoords);
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = entityManager.System<SharedBodySystem>().GetBodyChildren(body).ToList();

            Assert.That(routing.TryApplyDamage(body, Spec("Blunt", 10), source), Is.True);
            var partTotal = parts.Aggregate(FixedPoint2.Zero,
                (total, part) => total + damage.GetAllDamage(part.Id).GetTotal());
            Assert.That(partTotal, Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(damage.GetAllDamage(body).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
        });
    }

    [Test]
    public async Task DistributedDamageMasksAndRoundingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var attacker = entityManager.SpawnEntity("WoundFoundationAttacker", map.GridCoords);
            entityManager.GetComponent<TargetingComponent>(attacker).Target = TargetBodyPart.Head;
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var resolver = entityManager.System<WoundTargetResolver>(); // WOLFGATE(Wolfmed)
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var rightArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                part.Component.Symmetry == BodyPartSymmetry.Right).Id;

            // WOLFGATE(Wolfmed): D9: Groin resolves onto the torso, so the Torso|Groin mask still matches exactly one part.
            Assert.That(resolver.GetMatchingParts(body, TargetBodyPart.Torso | TargetBodyPart.Groin), Is.EqualTo(new[] { torso }));
            // WOLFGATE(Wolfmed): Wolfgate's TargetBodyPart has `Arms`, not Onyx's `FullArms`.
            // WOLFGATE(Wolfmed): Is.EqualTo compares HashSets positionally; the contract is set equality.
            Assert.That(resolver.GetMatchingParts(body, TargetBodyPart.Arms),
                Is.EquivalentTo(new[] { leftArm, rightArm }));
            Assert.That(resolver.GetMatchingParts(body, TargetBodyPart.All).Count, Is.EqualTo(4));

            var distributed = Spec("Blunt", 10);
            distributed.DamageDict[new ProtoId<DamageTypePrototype>("Heat")] = FixedPoint2.New(7);
            distributed.DamageDict[new ProtoId<DamageTypePrototype>("Asphyxiation")] = FixedPoint2.New(4);
            Assert.That(routing.TryApplyDistributedDamage(body,
                distributed,
                TargetBodyPart.All,
                DamageDistribution.SplitByPartWeight,
                attacker));
            Assert.That(parts.Select(part => damage.GetAllDamage(part.Id).DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Blunt"))).Sum(),
                Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(parts.Select(part => damage.GetAllDamage(part.Id).DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Heat"))).Sum(),
                Is.EqualTo(FixedPoint2.New(7)));
            Assert.That(damage.GetAllDamage(head).DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Blunt")),
                Is.LessThan(FixedPoint2.New(10)));
            Assert.That(entityManager.GetComponent<SystemicDamageComponent>(body).Damage.DamageDict[new ProtoId<DamageTypePrototype>("Asphyxiation")],
                Is.EqualTo(FixedPoint2.New(4)));

            Assert.That(wfBody.TryDetachPart(head)); // WOLFGATE(Wolfmed)
            // WOLFGATE(Wolfmed): Wolfgate has no TargetBodyPart.Vital; Head|Torso is the same set Onyx's Vital names.
            Assert.That(resolver.GetMatchingParts(body, TargetBodyPart.Head | TargetBodyPart.Torso), Is.EqualTo(new[] { torso }));
            Assert.That(routing.TryApplyDistributedDamage(body,
                Spec("Slash", 1),
                TargetBodyPart.Head | TargetBodyPart.Torso,
                DamageDistribution.SplitEvenly));
            Assert.That(damage.GetAllDamage(torso).DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Slash")), Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(damage.GetAllDamage(head).DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Slash")), Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task CreatesMergesHealsAndPreservesWoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var wounds = entityManager.System<WoundSystem>();
            var head = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Head).Id;

            var created = wounds.CreateOrMergeWound(head, "BluntWound", 10);
            Assert.That(created, Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(head, "BluntWound", 5), Is.EqualTo(created));
            var wound = wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head)))
                .Single(candidate => candidate.Comp.Prototype == new ProtoId<WoundPrototype>("BluntWound"));
            Assert.That(wound.Comp.Prototype, Is.EqualTo(new ProtoId<WoundPrototype>("BluntWound")));
            Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(15)));
            Assert.That(wound.Comp.PeakSeverity, Is.EqualTo(FixedPoint2.New(15)));
            Assert.That(wound.Comp.HoldingPart, Is.EqualTo(head));

            Assert.That(wfBody.TryDetachPart(head)); // WOLFGATE(Wolfmed)
            Assert.That(wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head)))
                .Single(candidate => candidate.Comp.Prototype == new ProtoId<WoundPrototype>("BluntWound")).Owner, Is.EqualTo(wound.Owner));
            var torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            Assert.That(graph.AttachPart(torso, "head", head)); // WOLFGATE(Wolfmed)
            Assert.That(wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head)))
                .Single(candidate => candidate.Comp.Prototype == new ProtoId<WoundPrototype>("BluntWound")).Owner, Is.EqualTo(wound.Owner));

            Assert.That(wounds.ChangeSeverity(wound, -15));
            Assert.That(wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head))), Is.Empty);

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 4)));
            entityManager.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());
            Assert.That(wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head))), Is.Empty);
        });
    }

    [Test]
    public async Task BodyPartProfileContractsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var head = entityManager.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var woundable = entityManager.GetComponent<WoundableComponent>(head);
            woundable.Profile = "WoundFoundationRestrictedProfile";

            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var wounds = entityManager.System<WoundSystem>();

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 10)), Is.False);
            Assert.That(routing.TryRoutePartDamage(body, head, Spec("Slash", 10), null, out var dealt), Is.True);
            Assert.That(dealt.Empty, Is.True);
            // WOLFGATE(Wolfmed): DamageableInit seeds every supported type to zero here (D30, §8.3 trap 1), so the
            // part's DamageSpecifier is never `Empty` — the contract is that nothing landed, not that the
            // dictionary is bare.
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(wounds.CreateOrMergeWound(head, "SlashWound", 10), Is.Null);
            var incision = wounds.CreateOrMergeWound(head, "SurgicalIncisionWound", 10);
            Assert.That(incision, Is.Not.Null);
            Assert.That(entityManager.HasComponent<WoundBleedingComponent>(incision.Value), Is.False);
        });
    }

    [Test]
    public async Task AppliesArmorExactlyOnceTest()
    {
        // WOLFGATE(Wolfmed): the contract here is that armour applies exactly once per routed hit, never twice.
        // WoundFoundationArmor declares no `coverage`, so WP11-3's locational gate (P3-D5, Option B) is a verified
        // no-op for it and both parts still take 5 — this test is the regression guard that says so. The
        // coverage/symmetry/partModifiers behaviour itself is covered by the four locational tests below.
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armor = entityManager.SpawnEntity("WoundFoundationArmor", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            Assert.That(inventory.TryEquip(body, armor, "outerClothing"), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 10)));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
        });
    }

    [Test]
    public async Task NonWoundHostUsesVanillaArmorTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationVanillaBody", map.GridCoords);
            var armor = entityManager.SpawnEntity("WoundFoundationArmor", map.GridCoords);
            var inventory = entityManager.System<InventorySystem>();
            var damage = entityManager.System<DamageableSystem>();

            Assert.That(inventory.TryEquip(body, armor, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(body, Spec("Blunt", 10)), Is.Not.Null);
            Assert.That(entityManager.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.New(5)));
        });
    }

    /// <summary>T-P3ARM-1: an armour with `coverage: [Head]` armours the head and leaves the torso alone.</summary>
    [Test]
    public async Task AppliesLocationalArmorExactlyOnceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armor = entityManager.SpawnEntity("WoundFoundationArmorHead", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12 facade, as at :440.
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9.

            Assert.That(inventory.TryEquip(body, armor, "outerClothing"), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 10)));

            // WOLFGATE(Wolfmed): P3-D5: derivation — head is in `coverage`, so the global Blunt 0.5 applies: 10 -> 5.
            // The torso is not, so Covers() returns false before any modifier maths: 10 -> 10. This assertion is
            // RED against Onyx's own shipped code, whose <Onyx-ArmorGlobalProtection-edited> block never consults
            // `coverage` at all and would give the torso 5; Option B is what makes coverage load-bearing.
            Assert.Multiple(() =>
            {
                Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            });
        });
    }

    /// <summary>T-P3ARM-2: unset coverage protects every part regardless of worn slot; a symmetry set excludes the other side.</summary>
    [Test]
    public async Task EmptyCoverageAndSymmetryTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var allArmor = entityManager.SpawnEntity("WoundFoundationArmorAllHead", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12 facade.
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9.
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var rightArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                part.Component.Symmetry == BodyPartSymmetry.Right).Id;

            // WOLFGATE(Wolfmed): P3-D5: null/empty coverage means "protects everything", and the worn slot is
            // deliberately irrelevant — a head-slot armour still protects the torso. Getting the empty case
            // backwards would invert every one of the game's 272 unannotated `- type: Armor` entries, and no
            // slot-derived default (Option C) may creep in: 10 -> 5 on the torso.
            Assert.That(inventory.TryEquip(body, allArmor, "head"), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 10)));
            Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(inventory.TryUnequip(body, "head"), Is.True);

            var symmetryArmor = entityManager.SpawnEntity("WoundFoundationArmorLeftArm", map.GridCoords);
            Assert.That(inventory.TryEquip(body, symmetryArmor, "outerClothing"), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(body, rightArm, Spec("Blunt", 10)));

            // WOLFGATE(Wolfmed): P3-D5: `coverage: [Arm]` + `coverageSymmetry: [Left]` — the left arm matches both sets
            // (10 -> 5), the right arm fails the symmetry set (10 -> 10). Symmetry is checked independently of
            // Parts, so a bare `symmetry: [Left]` would mean "any left part". RED against Onyx's shipped code.
            Assert.Multiple(() =>
            {
                Assert.That(damage.GetAllDamage(leftArm).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(damage.GetAllDamage(rightArm).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            });
        });
    }

    /// <summary>T-P3ARM-3: the first matching partModifiers entry wins and skips the coverage gate; unmatched parts fall back.</summary>
    [Test]
    public async Task LocationalModifierOverridesAndFallbackTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armor = entityManager.SpawnEntity("WoundFoundationArmorLocational", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12 facade.
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9.
            var leftArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                               part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var rightArm = parts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                part.Component.Symmetry == BodyPartSymmetry.Right).Id;

            Assert.That(inventory.TryEquip(body, armor, "outerClothing"), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 20)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 20)));
            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Blunt", 20)));
            Assert.That(routing.TryApplyPartDamage(body, rightArm, Spec("Blunt", 20)));

            // WOLFGATE(Wolfmed): P3-D5: derivation from WoundFoundationArmorLocational — head matches the first
            // partModifiers entry (0.25) and takes 5 *despite* `coverage: [Torso]`, which is precisely why the
            // coverage gate must sit AFTER the loop; the torso matches no entry and falls back through the gate
            // to the global 0.8 -> 16; left arm 0.5 -> 10; right arm 0.75 -> 15. Green under both options.
            Assert.Multiple(() =>
            {
                Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(16)));
                Assert.That(damage.GetAllDamage(leftArm).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(damage.GetAllDamage(rightArm).GetTotal(), Is.EqualTo(FixedPoint2.New(15)));
            });
        });
    }

    /// <summary>T-P3ARM-AP: armour penetration reaches the partModifiers branch, not just the global fallback.</summary>
    [Test]
    public async Task PartModifiersRouteThroughArmorPenetrationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var damage = entityManager.System<DamageableSystem>();
            var facade = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12 facade.

            EntityUid Head(EntityUid body) => graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // AP only reaches the part pass through TryChangeDamage (WoundDamageRoutingSystem.cs:79 stores it and
            // :747 puts it on PartDamageModifyEvent), so this test cannot use TryApplyPartDamage.
            var penetrated = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armorA = entityManager.SpawnEntity("WoundFoundationArmorLocational", map.GridCoords);
            Assert.That(inventory.TryEquip(penetrated, armorA, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(penetrated, Spec("Blunt", 20),
                targetPart: TargetBodyPart.Head, armorPenetration: 1f), Is.Not.Null);

            var unpenetrated = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armorB = entityManager.SpawnEntity("WoundFoundationArmorLocational", map.GridCoords);
            Assert.That(inventory.TryEquip(unpenetrated, armorB, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(unpenetrated, Spec("Blunt", 20),
                targetPart: TargetBodyPart.Head, armorPenetration: 0f), Is.Not.Null);

            // WOLFGATE(Wolfmed): D23: derivation — the head is armoured by the partModifiers Blunt 0.25 entry, and that
            // set must be wrapped in DamageSpecifier.PenetrateArmor exactly as the global fallback is.
            // PenetrateArmor returns a new EMPTY set at penetration >= 1 (DamageSpecifier.cs:306-330) and an
            // empty set leaves every type untouched (:157), so AP 1 gives the full 20 and AP 0 gives 5.
            // Without the wrap both numbers would read 5 and every AP weapon would silently lose its AP against
            // any armour that declares a part profile. Nothing in Onyx covers this.
            Assert.Multiple(() =>
            {
                Assert.That(facade.GetAllDamage(Head(penetrated)).GetTotal(), Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(facade.GetAllDamage(Head(unpenetrated)).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
            });
        });
    }

    /// <summary>T-P3ARM-UNCOVERED-AP: an uncovered part ignores the armour entirely, so AP cannot change its damage.</summary>
    [Test]
    public async Task UncoveredPartIgnoresArmorPenetrationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var graph = entityManager.System<SharedBodySystem>();
            var inventory = entityManager.System<InventorySystem>();
            var damage = entityManager.System<DamageableSystem>();
            var facade = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12 facade.

            EntityUid Torso(EntityUid body) => graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9.

            var penetrated = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armorA = entityManager.SpawnEntity("WoundFoundationArmorHead", map.GridCoords);
            Assert.That(inventory.TryEquip(penetrated, armorA, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(penetrated, Spec("Blunt", 10),
                targetPart: TargetBodyPart.Torso, armorPenetration: 1f), Is.Not.Null);

            var unpenetrated = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var armorB = entityManager.SpawnEntity("WoundFoundationArmorHead", map.GridCoords);
            Assert.That(inventory.TryEquip(unpenetrated, armorB, "outerClothing"), Is.True);
            Assert.That(damage.TryChangeDamage(unpenetrated, Spec("Blunt", 10),
                targetPart: TargetBodyPart.Torso, armorPenetration: 0f), Is.Not.Null);

            // WOLFGATE(Wolfmed): P3-D5: the coverage gate returns before any modifier maths, so an uncovered part takes
            // the full hit at every penetration value. 10 in both cases — an AP-dependent number here would mean
            // the gate had been moved below the modifier application.
            Assert.Multiple(() =>
            {
                Assert.That(facade.GetAllDamage(Torso(penetrated)).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(facade.GetAllDamage(Torso(unpenetrated)).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            });
        });
    }

    [Test]
    public async Task PainApiAndProjectionTest()
    {
        // WOLFGATE(Wolfmed): playtest 4, the loose rate would change Onyx's recovery arithmetic asserted below.
        await OverrideCVar(Side.Server, Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.PainLooseRecovery, 0f);
        // WOLFGATE(Wolfmed): playtest 5 ships wolfmed.pain_scale 0.85; Onyx's figures below are at 1.
        await OverrideCVar(Side.Server, Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.PainScale, 1f);
        // WOLFGATE(Wolfmed): canary for the language trap that made the whole pain system inert. On a record struct
        // with a primary constructor, `new T()` binds to the implicit parameterless struct constructor and
        // zeroes the field rather than taking the primary constructor's `= 1f` default, so Onyx's
        // `new ModifyPainGainEvent()` multiplied every pain gain by zero. PainSystem now passes `1f`
        // explicitly. If this assertion ever fails, the language behaviour changed and that workaround
        // (two `// WOLFGATE` lines in PainSystem.cs) can go.
        Assert.That(new ModifyPainGainEvent().Multiplier, Is.EqualTo(0f));

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var pain = entityManager.System<PainSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 10)));
            // WOLFGATE(Wolfmed): pinpoint diagnostics — pain reaching zero here has three distinct causes (no routed
            // damage, no PainComponent on the part, or a zeroed gain multiplier) and the bare total hides them.
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(10)),
                "the routed hit did not land on the head");
            Assert.That(entityManager.HasComponent<PainComponent>(head), Is.True,
                "SetupPart did not give the part a PainComponent");
            Assert.That(pain.GetRawPain(head), Is.EqualTo(FixedPoint2.New(8.7)));
            Assert.That(pain.GetPain(head), Is.EqualTo(FixedPoint2.New(8.7)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(8.7)));

            var systemicBody = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var systemicParts = graph.GetBodyChildren(systemicBody).ToList();
            var systemicTorso = systemicParts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var systemicArm = systemicParts.Single(part => part.Component.PartType == BodyPartType.Arm &&
                                                           part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            Assert.That(routing.TryApplyPartDamage(systemicBody, systemicArm, Spec("Poison", 10)));
            Assert.That(damage.GetAllDamage(systemicArm).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(entityManager.GetComponent<SystemicDamageComponent>(systemicBody).Damage.GetTotal(),
                Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(pain.GetPain(systemicArm), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(pain.GetPain(systemicTorso), Is.EqualTo(FixedPoint2.New(7)));
            Assert.That(pain.GetPain(systemicBody), Is.EqualTo(FixedPoint2.New(7)));

            var healingBody = entityManager.SpawnEntity("WoundFoundationBody", map.GridCoords);
            var healingHead = graph.GetBodyChildren(healingBody)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            Assert.That(routing.TryApplyPartDamage(healingBody, healingHead, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(healingBody, healingHead, Spec("Blunt", -10)));
            Assert.That(damage.GetAllDamage(healingHead).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(pain.GetRawPain(healingHead), Is.EqualTo(FixedPoint2.New(8.7)));
            Assert.That(pain.GetRawPain(healingBody), Is.EqualTo(FixedPoint2.New(8.7)));

            // WOLFGATE(Wolfmed): D16: Onyx's SuppressPain entity effect is phase 4; PainSystem.SuppressPain is the same
            // code path the effect calls, and the identifier accumulation reproduces Onyx's numbers exactly.
            Assert.That(pain.SuppressPain(body, "Suppressant", 2, TimeSpan.FromSeconds(10)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(6.7)));
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(8.7)));
            Assert.That(pain.SuppressPain(body, "Suppressant", 2, TimeSpan.FromSeconds(10)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(4.7)));
            Assert.That(entityManager.GetComponent<PainComponent>(body).Suppression, Is.EqualTo(FixedPoint2.New(4)));

            Assert.That(pain.SuppressPain(body, "SecondSuppressant", 1, TimeSpan.FromSeconds(10)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(3.7)));
            Assert.That(pain.DecayPainSuppression(body, 1f));
            Assert.That(entityManager.GetComponent<PainComponent>(body).Suppression, Is.EqualTo(FixedPoint2.New(4.5)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(4.2)));

            Assert.That(pain.RecoverPain(head, 1f), Is.False);
            Assert.That(pain.GetRawPain(head), Is.EqualTo(FixedPoint2.New(8.7)));
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(8.7)));
            // WOLFGATE(Wolfmed): Onyx expects 8.62 (a 0.08/s recovery); `PainComponent.RecoveryPerSecond` is
            // `FixedPoint2.New(1f / 9f)` in both trees, which is 0.11 at FixedPoint2's two decimals here,
            // so one second of recovery takes 8.70 to 8.59. A precision/rounding difference, not a port edit.
            Assert.That(pain.RecoverPain(healingHead, 1f));
            Assert.That(pain.GetRawPain(healingHead), Is.EqualTo(FixedPoint2.New(8.59)));
            Assert.That(pain.GetRawPain(healingBody), Is.EqualTo(FixedPoint2.New(8.59)));

            Assert.That(wfBody.TryDetachPart(head)); // WOLFGATE(Wolfmed)
            // WOLFGATE(Wolfmed): BRAIN: losing the head is death on a wound host now. This test is about pain
            // bookkeeping on a living body, so the body is put back on its feet, and re-armed, for the pain-shock checks.
            entityManager.System<MobStateSystem>().ChangeMobState(body, MobState.Alive);
            entityManager.GetComponent<PainShockTargetComponent>(body).Armed = true; // death disarms it
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.Zero));
            // WOLFGATE(Wolfmed): Onyx's 13.05 assumes the detached head takes the full 5 Blunt. Wolfgate's Shitmed
            // `OnPartDamageModify` (<BodyPartComponent, DamageModifyEvent>) still runs on a loose limb and
            // applies the `PartDamage` modifier set plus `GetPartDamageModifier(Head) = 0.5`, so 5 lands as
            // 2 and pain goes 8.70 -> 10.44. Routed hits on an attached part bypass this (ignoreResistances).
            Assert.That(damage.ChangeDamage(head, Spec("Blunt", 5)).Empty, Is.False);
            Assert.That(pain.GetPain(head), Is.EqualTo(FixedPoint2.New(10.44)));

            Assert.That(pain.SetPain(head, FixedPoint2.New(-1)), Is.True);
            Assert.That(pain.GetPain(head), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(pain.ChangePain(head, FixedPoint2.New(2)), Is.True);
            Assert.That(pain.GetPain(head), Is.EqualTo(FixedPoint2.New(2)));
            Assert.That(pain.ChangePain(head, FixedPoint2.New(-3)), Is.True);
            Assert.That(pain.GetPain(head), Is.EqualTo(FixedPoint2.Zero));

            // WOLFGATE(Wolfmed): Onyx's pain-shock numbers below assume no residual suppression, but the decay block
            // above deliberately leaves 4.5 on the body and nothing in between clears it, so every figure
            // would be off by 4.5 * the adrenaline multiplier. Clear it explicitly instead of re-deriving them.
            Assert.That(pain.ClearPainSuppression(body));
            // WOLFGATE(Wolfmed): M1a: P13, the body's pain is min(135, sum of the parts), so the 200 goes on a part (it
            // clamps at the part's own 135) and a direct set on the body would be rederived. OD5: adrenaline no
            // longer takes 30% off the reading, so every 94.5 below is 135 and 73.5 is 105.
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            Assert.That(pain.SetPain(torso, FixedPoint2.New(200)), Is.True);
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(135)));
            Assert.That(entityManager.HasComponent<StunnedComponent>(body), Is.True);
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(135))); // WOLFGATE(Wolfmed): M1a
            Assert.That(entityManager.GetComponent<PainShockTargetComponent>(body).Armed, Is.False);

            Assert.That(pain.SuppressPain(body, "PainShockTest", 30, TimeSpan.FromSeconds(10)));
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(105))); // WOLFGATE(Wolfmed): M1a
            Assert.That(entityManager.GetComponent<PainShockTargetComponent>(body).Armed, Is.True);
            Assert.That(pain.ClearPainSuppression(body));
            Assert.That(entityManager.HasComponent<StunnedComponent>(body), Is.True);
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.New(135))); // WOLFGATE(Wolfmed): M1a
            // WOLFGATE(Wolfmed): M1a: back at 135 with no adrenaline discount, the re-armed shock fires again at once.
            Assert.That(entityManager.GetComponent<PainShockTargetComponent>(body).Armed, Is.False);

            entityManager.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());
            Assert.That(pain.GetPain(body), Is.EqualTo(FixedPoint2.Zero));
        });
    }

    private static DamageSpecifier Spec(string type, int amount)
    {
        return new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
        };
    }
}
