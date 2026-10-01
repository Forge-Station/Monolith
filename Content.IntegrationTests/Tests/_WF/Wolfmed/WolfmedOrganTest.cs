using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Shitmed.DelayedDeath; // WOLFGATE: DelayedDeathComponent is server-only here.
using Content.Shared._Onyx.Body;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8, organ health lives on WolfmedOrganComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech.Muting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Organ damage and its consequences: WP11-2's prototype data, the per-application cap, destruction and the
/// Shitmed consequence chain destruction hands off to.
/// </summary>
/// <remarks>
/// PLAN3 §6.2 T-ORG-DATA, T-ORG-CAP, T-ORG-DESTROY, T-ORG-HEART, T-ORG-BRAIN, T-ORG-EYES, T-ORG-FUNC and
/// T-ORG-INERT (WP11-5). Destruction is driven through OrganHealthSystem.SetHealth rather than through
/// damage, because the per-hit organ roll is ~1-4 % (PLAN3 §8.4) and would make every test flaky.
/// </remarks>
[TestFixture]
[TestOf(typeof(OrganHealthSystem))]
public sealed class WolfmedOrganTest : GameTest
{
    // WOLFGATE: the organ-damage roll is `chances[partType]` from the part's bodyPartProfile, which is
    // 0.04 for a torso on the shipped OrganicBodyPartProfile. T-ORG-CAP forces it to 1.0 in a bespoke
    // profile so the cap can be measured deterministically; `maxAffected: 1` plus a single organ with
    // `hitChance: 1` removes the draw-without-replacement roll as well.
    //
    // M3 (plan §8): the roll only runs on a part without reach lines, so the T-ORG-CAP torso declares an empty
    // `organReach`. WolfmedOrganReachBody keeps the shipped torso lines for the reach test.
    //
    // WoundableComponent is EnsureComp'd at runtime by WoundDamageProjectionSystem.SetupPart and defaults
    // its Profile to OrganicBodyPartProfile, so the only way to attach a different profile is to declare
    // the component statically on the part prototype - EnsureComp leaves an existing component alone.
    //
    // WolfmedOrganFuncOrgan declares `- type: OrganEffect` explicitly rather than relying on
    // SharedBodySystem.OnMapInit's EnsureComp, so the onAdd grant cannot lose a race with body init.
    [TestPrototypes]
    private const string Prototypes = @"
- type: bodyPartProfile
  id: WolfmedOrganTestProfile
  organDamage:
    chances:
      Torso: 1
    maxAffected: 1

- type: body
  id: WolfmedOrganTestGraph
  name: ""wolfmed organ test body""
  root: torso
  slots:
    torso:
      part: WolfmedOrganTestTorso
      organs:
        wolfmedtest: WolfmedOrganTestOrgan

- type: entity
  id: WolfmedOrganTestBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedOrganTestGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WolfmedOrganControlBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedOrganTestGraph
  - type: Damageable
    damageContainer: Biological

- type: entity
  id: WolfmedOrganTestTorso
  parent: TorsoHuman
  components:
  - type: Woundable
    profile: WolfmedOrganTestProfile
  - type: WolfmedBodyPart
    organReach: {}

- type: body
  id: WolfmedOrganReachGraph
  name: ""wolfmed organ reach body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      organs:
        wolfmedtest: WolfmedOrganTestOrgan

- type: entity
  id: WolfmedOrganReachBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedOrganReachGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WolfmedOrganTestOrgan
  parent: BaseHumanOrgan
  name: ""wolfmed test organ""
  components:
  - type: Organ
    slotId: wolfmedtest
  - type: WolfmedOrgan
  - type: OrganDamage
    hitChance: 1
    selectionWeight: 1
    damageMultipliers:
      Piercing: 0.5

- type: body
  id: WolfmedOrganFuncGraph
  name: ""wolfmed organ function body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      organs:
        wolfmedfunc: WolfmedOrganFuncOrgan

- type: entity
  id: WolfmedOrganFuncBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedOrganFuncGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WolfmedOrganFuncOrgan
  parent: BaseHumanOrgan
  name: ""wolfmed function test organ""
  components:
  - type: Organ
    slotId: wolfmedfunc
    onAdd:
    - type: Muted
  - type: OrganEffect
  - type: WolfmedOrgan
";

    /// <summary>PLAN3 §6.2 T-ORG-DATA. Catches a mistyped PROTO A `parent:` for a penny.</summary>
    [Test]
    public async Task OrganPrototypesCarryWolfmedDataTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // WOLFGATE: every literal below is WP11-2's MEASURED resolution of PROTO A, cross-checked
            // against ONYX Resources/Prototypes/Body/base_organs.yml. WolfmedOrganComponent defaults
            // Health/MaxHealth to 15, which is Onyx's own C# default; since playtest 4 the torso bases set 25 and
            // the brain and eyes keep the default (AssertOrgan checks both).
            AssertOrgan(entities, map.GridCoords, "OrganHumanBrain", 0.8f, 0.75f, "Piercing", 0.42f, null, 0);
            AssertOrgan(entities, map.GridCoords, "OrganHumanEyes", 0.7f, 0.2275f, "Piercing", 0.4375f, null, 0);
            AssertOrgan(entities, map.GridCoords, "OrganHumanLungs", 1f, 1.38f, "Heat", 0.165f,
                "InternalBleedingWound", 35);
            AssertOrgan(entities, map.GridCoords, "OrganHumanHeart", 0.8f, 0.64f, "Piercing", 0.455f,
                "InternalBleedingWound", 45);
            AssertOrgan(entities, map.GridCoords, "OrganHumanStomach", 0.85f, 0.56f, "Slash", 0.275f,
                "InternalBleedingWound", 25);
            AssertOrgan(entities, map.GridCoords, "OrganHumanLiver", 1f, 1.1f, "Slash", 0.3f,
                "InternalBleedingWound", 40);
            AssertOrgan(entities, map.GridCoords, "OrganHumanKidneys", 0.9f, 0.51f, "Piercing", 0.4025f,
                "InternalBleedingWound", 30);
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-ORG-CAP. The per-application cap is what makes Onyx's organ roll a slow ratchet. M3: the roll
    /// is left only for parts without reach lines, which this torso is.
    /// </summary>
    [Test]
    public async Task OrganDamageIsCappedPerApplicationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedOrganTestBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var organ = graph.GetPartOrgans(torso).Single().Id;
            var health = entities.GetComponent<WolfmedOrganComponent>(organ);

            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(15)));

            // OrganDamageSystem: applied = sum(damage[type] * damageMultipliers[type]) = 100 * 0.5 = 50,
            // then clamped to MaxHealth * MaxDamageFraction = 15 * 0.3 = 4.5 (OrganDamageComponent's
            // MaxDamageFraction default, overridden by no prototype in either tree).
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 100)));
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(10.5f)),
                "a single application must never take more than MaxHealth * MaxDamageFraction = 4.5.");

            // The cap does not care how big the hit is; 1000 Piercing removes exactly the same 4.5. This
            // is why PLAN3 §8.4 can say weapon damage above ~11 Piercing is irrelevant to organs.
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 1000)));
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(6)),
                "the cap is per application, not per point of damage.");
        });
    }

    /// <summary>
    /// M3 (plan §8): a part with reach lines. A hit at the line reaches nothing; past it, the organ takes
    /// (hit - line) × its multiplier × its weight share × wolfmed.organ_damage_scale, capped per hit by
    /// wolfmed.organ_hit_cap, every time and with no roll.
    /// </summary>
    [Test]
    public async Task OrganDamageFollowsTheReachLineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        await OverrideCVar(Side.Server, Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.OrganDamageScale, 3.4f);
        await OverrideCVar(Side.Server, Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.OrganHitCap, 5f);

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedOrganReachBody", map.GridCoords);
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var organ = graph.GetPartOrgans(torso).Single().Id;
            var health = entities.GetComponent<WolfmedOrganComponent>(organ);

            // The torso's Piercing line is 10: a hit of 10 reaches nothing.
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 10), attacker));
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(15)), "a hit at the line reached the organ.");

            // Piercing 12: (12 - 10) × 0.5 × 1 (the only organ) × 3.4 = 3.4.
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 12), attacker));
            Assert.That(health.Health.Float(), Is.EqualTo(11.6f).Within(0.011f));

            // Piercing 100 would be 153; the per-hit cap holds it at 5, and so it does for 1000.
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 100), attacker));
            Assert.That(health.Health.Float(), Is.EqualTo(6.6f).Within(0.011f), "the per-hit cap did not hold.");
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 1000), attacker));
            Assert.That(health.Health.Float(), Is.EqualTo(1.6f).Within(0.011f), "the cap is per hit, not per point.");
        });
    }

    /// <summary>PLAN3 §6.2 T-ORG-DESTROY.</summary>
    [Test]
    public async Task OrganDestructionMergesConsequenceWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var torso = EntityUid.Invalid;
        var lungs = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            lungs = FindOrgan(entities, graph, torso, "OrganHumanLungs");

            entities.System<OrganHealthSystem>()
                .SetHealth((lungs, entities.GetComponent<WolfmedOrganComponent>(lungs)), FixedPoint2.Zero);
        });

        // OrganHealthSystem.Update destroys zero-health organs on the next server tick, then QueueDel runs.
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>()
                .GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("InternalBleedingWound"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(lungs), Is.True, "a destroyed organ is deleted, not disabled.");
                Assert.That(graph.GetPartOrgans(torso).Any(organ => organ.Id == lungs), Is.False);
                // WFWolfmedOrganLungs: destructionWound InternalBleedingWound, destructionWoundSeverity 35
                // (_WF/Wolfmed/Body/organs.yml, Onyx's base_organs.yml value).
                Assert.That(wounds, Has.Count.EqualTo(1));
                Assert.That(wounds[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(35)));
            });
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-ORG-HEART. The single most important organ assertion: it is the only proof that
    /// Shitmed's own removal consequences fire from OrganHealthSystem.DestroyOrgan, which is the whole of
    /// P3-D8's argument for skipping seven of Onyx's nine consequence pieces.
    /// </summary>
    [Test]
    public async Task DestroyedHeartAppliesDelayedDeathTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var heart = FindOrgan(entities, graph, torso, "OrganHumanHeart");

            Assert.That(entities.HasComponent<DelayedDeathComponent>(body), Is.False);
            entities.System<OrganHealthSystem>()
                .SetHealth((heart, entities.GetComponent<WolfmedOrganComponent>(heart)), FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<DelayedDeathComponent>(body), Is.True,
                "destroying the heart must route through Shitmed's HeartSystem removal handler."));
    }

    /// <summary>PLAN3 §6.2 T-ORG-BRAIN. Pins P3-D22: the brain branch kills instead of destroying.</summary>
    [Test]
    public async Task DestroyedBrainKillsWithoutDeletingOrganTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var brain = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            brain = FindOrgan(entities, graph, head, "OrganHumanBrain");

            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False);
            entities.System<OrganHealthSystem>()
                .SetHealth((brain, entities.GetComponent<WolfmedOrganComponent>(brain)), FixedPoint2.Zero);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.True);
                // OrganHealthSystem's brain branch `continue`s before DestroyOrgan, so the organ survives -
                // the mob is dead but still has a brain to defib/clone from.
                Assert.That(entities.Deleted(brain), Is.False,
                    "a zero-health brain must kill the mob, not be destroyed (P3-D22).");
                Assert.That(entities.HasComponent<OrganComponent>(brain), Is.True);
            });
        });
    }

    /// <summary>PLAN3 §6.2 T-ORG-EYES.</summary>
    [Test]
    public async Task DestroyedEyesBlindTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var eyes = FindOrgan(entities, graph, head, "OrganHumanEyes");

            Assert.That(entities.HasComponent<TemporaryBlindnessComponent>(body), Is.False);
            entities.System<OrganHealthSystem>()
                .SetHealth((eyes, entities.GetComponent<WolfmedOrganComponent>(eyes)), FixedPoint2.Zero);

            // Blindness lands in the same call, before destruction: SetHealth raises
            // OrganFunctionChangedEvent, WolfmedOrganConsequenceSystem turns it into
            // OrganEnableChangedEvent(false), and SharedBodySystem.DisableOrgan raises OrganDisabledEvent
            // for EyesComponent -> EyesSystem EnsureComps TemporaryBlindness on the body.
            Assert.That(entities.HasComponent<TemporaryBlindnessComponent>(body), Is.True);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<TemporaryBlindnessComponent>(body), Is.True,
                "destruction must not undo the blindness the disable applied."));
    }

    /// <summary>
    /// PLAN3 §6.2 T-ORG-FUNC. The only coverage of WolfmedOrganConsequenceSystem (PLAN3 §2.2), including
    /// the deliberate double disable.
    /// </summary>
    [Test]
    public async Task ZeroHealthOrganRevokesGrantedComponentsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedOrganFuncBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var organ = graph.GetPartOrgans(torso).Single().Id;

            Assert.That(entities.HasComponent<MutedComponent>(body), Is.True,
                "the organ's onAdd grant must be live before the test can prove it is revoked.");

            // No tick: this is the one-tick window in which the organ is at 0 HP but not yet destroyed,
            // which is the only thing WolfmedOrganConsequenceSystem exists to cover.
            entities.System<OrganHealthSystem>()
                .SetHealth((organ, entities.GetComponent<WolfmedOrganComponent>(organ)), FixedPoint2.Zero);
            Assert.That(entities.HasComponent<MutedComponent>(body), Is.False,
                "a non-functional organ must stop granting its onAdd components immediately.");
        });

        // One tick later DestroyOrgan -> RemoveOrgan raises OrganEnableChangedEvent(false) a SECOND time,
        // and OnOrganEnableChanged does not early-return on an unchanged value, so the revoke runs twice.
        // Removing an already-removed component is a no-op; this asserts the second pass is harmless, so
        // nobody "fixes" it with a guard that also suppresses the first pass (PLAN3 §2.2).
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<MutedComponent>(body), Is.False));
    }

    /// <summary>PLAN3 §6.2 T-ORG-INERT. The D2/D3/D32 regression guard.</summary>
    [Test]
    public async Task NonWoundHostTakesNoOrganDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // Same graph, same torso profile (organ-damage chance forced to 1.0) and the same organ as
            // T-ORG-CAP - the ONLY difference is the missing WoundHostComponent.
            var body = entities.SpawnEntity("WolfmedOrganControlBody", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.False);

            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var organ = graph.GetPartOrgans(torso).Single().Id;
            var health = entities.GetComponent<WolfmedOrganComponent>(organ);

            // Routing refuses the entity outright ...
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Piercing", 500)), Is.False);
            // ... and ordinary damage takes the pre-port path, which never raises PartDamageAppliedEvent,
            // the single fan-out point OrganDamageSystem listens on. D2 holds structurally: there is no
            // IsServer or CVar check anywhere in this path, only the absence of the component.
            Assert.That(entities.System<WolfmedDamageableSystem>()
                    .TryChangeDamage(body, Spec("Piercing", 500), out _), Is.True,
                "the control must actually have taken the damage for this guard to mean anything.");

            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(15)),
                "an entity without WoundHostComponent must take no organ damage at all.");
        });
    }

    private static void AssertOrgan(
        IEntityManager entities,
        EntityCoordinates coords,
        string prototype,
        float hitChance,
        float selectionWeight,
        string multiplierType,
        float multiplier,
        string destructionWound,
        int destructionSeverity)
    {
        var organ = entities.SpawnEntity(prototype, coords);
        var health = entities.GetComponent<WolfmedOrganComponent>(organ);
        var policy = entities.GetComponent<OrganDamageComponent>(organ);

        Assert.Multiple(() =>
        {
            // Playtest 4: torso organs carry 25 in the Wolfmed bases; the brain and eyes keep the component's 15.
            var expected = prototype.EndsWith("Brain") || prototype.EndsWith("Eyes") ? 15 : 25;
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(expected)), $"{prototype} health");
            Assert.That(health.MaxHealth, Is.EqualTo(FixedPoint2.New(expected)), $"{prototype} maxHealth");
            Assert.That(policy.HitChance, Is.EqualTo(hitChance).Within(0.0001f), $"{prototype} hitChance");
            Assert.That(policy.SelectionWeight, Is.EqualTo(selectionWeight).Within(0.0001f),
                $"{prototype} selectionWeight");
            Assert.That(policy.DamageMultipliers[new ProtoId<DamageTypePrototype>(multiplierType)],
                Is.EqualTo(multiplier).Within(0.0001f), $"{prototype} {multiplierType} multiplier");
            Assert.That(policy.MaxDamageFraction, Is.EqualTo(0.3f).Within(0.0001f),
                $"{prototype} maxDamageFraction");

            if (destructionWound == null)
            {
                // The brain is never destroyed (P3-D22) and Onyx's eyes leave no internal bleeding.
                Assert.That(health.DestructionWound, Is.Null, $"{prototype} destructionWound");
            }
            else
            {
                Assert.That(health.DestructionWound,
                    Is.EqualTo(new ProtoId<WoundPrototype>(destructionWound)), $"{prototype} destructionWound");
                Assert.That(health.DestructionWoundSeverity, Is.EqualTo(FixedPoint2.New(destructionSeverity)),
                    $"{prototype} destructionWoundSeverity");
            }
        });
    }

    private static EntityUid FindOrgan(
        IEntityManager entities,
        SharedBodySystem graph,
        EntityUid part,
        string prototype)
    {
        return graph.GetPartOrgans(part)
            .Single(organ => entities.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID == prototype)
            .Id;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
