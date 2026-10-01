using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes; // WOLFGATE(Wolfmed): Side, for the pain scale pin.
// WOLFGATE(Wolfmed): D13 moves the healing/bleeding systems to Content.Server but keeps their Onyx namespace.
using Content.Server.Medical.Components; // WOLFGATE(Wolfmed): D14, HealingComponent stays server-only.
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade.
using Content.Shared._WF.Wolfmed.Targeting; // WOLFGATE(Wolfmed): D10 resolver.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

[TestFixture]
[TestOf(typeof(WoundHealingSystem))]
public sealed class WoundHealingTest : GameTest
{
    // WOLFGATE(Wolfmed): Shitmed body graph instead of Onyx's Nubody `InitialBody`; `Injurable` (D19), `Repairable` and
    // `TransplantCompatibility` (Onyx-only, D7/D8) dropped; Chest → Torso (D9).
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WoundHealingBodyGraph
  name: ""wound healing body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
    head:
      part: HeadHuman

- type: entity
  id: WoundHealingBody
  parent: [InventoryBase, MobBloodstream]
  components:
  - type: Body
    prototype: WoundHealingBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WoundHealingItem
  components:
  - type: Healing
    damageContainers: [Biological]
    damage:
      types:
        Blunt: -10
        Slash: -10
        Piercing: -10
    bloodlossModifier: -10

- type: entity
  id: WoundHealingIncompatibleItem
  components:
  - type: Healing
    damageContainers: [StructuralInorganic]
    damage:
      types:
        Blunt: -10

- type: entity
  id: WoundHealingTargetingUser
  components:
  - type: Targeting
";

    [Test]
    public async Task HealsSelectedPartDamageWithoutWoundTest()
    {
        // WOLFGATE(Wolfmed): playtest 5 ships wolfmed.pain_scale 0.85; Onyx's pain figures below are at 1.
        await OverrideCVar(Side.Server, Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.PainScale, 1f);
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundHealingBody", map.GridCoords);
            var item = entityManager.SpawnEntity("WoundHealingItem", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var healing = entityManager.System<WoundHealingSystem>();
            var wounds = entityManager.System<WoundSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var pain = entityManager.System<PainSystem>();
            var head = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // WOLFGATE(Wolfmed): W0: 11 rather than Onyx's 15. WFWolfmedFractureProfile's Hairline threshold is 12 at
            // a 25 % roll, so a 15 Blunt hit would silently grow a bone fracture in one run out of four and
            // take the pain figures with it. 11 keeps the whole test deterministic.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 11)));
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(9.57)));
            var wound = wounds.GetWounds((head, entityManager.GetComponent<WoundableComponent>(head)))
                .Single(candidate => candidate.Comp.Prototype == new ProtoId<WoundPrototype>("BluntWound"));
            Assert.That(healing.TryApplyHealing(body, head, (item, entityManager.GetComponent<HealingComponent>(item)),
                body, out _, out _));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(1)));
            // WOLFGATE(Wolfmed): W0: BluntWound now carries Onyx's intended `healingMultiplier: 0.15`, so removing the
            // 10 points of Blunt the part still had takes only 1.5 off the wound. This is the whole point of
            // the setting: damage removal is not wound closure.
            Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(9.5)));
            Assert.That(damage.GetAllDamage(body).GetTotal(), Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(pain.GetRawPain(head), Is.EqualTo(FixedPoint2.New(9.57)));
            Assert.That(pain.GetRawPain(body), Is.EqualTo(FixedPoint2.New(9.57)));
        });
    }

    [Test]
    public async Task LegacySelectionBleedingIsolationAndValidationTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("WoundHealingBody", map.GridCoords);
            var otherBody = entityManager.SpawnEntity("WoundHealingBody", map.GridCoords);
            var item = entityManager.SpawnEntity("WoundHealingItem", map.GridCoords);
            var incompatible = entityManager.SpawnEntity("WoundHealingIncompatibleItem", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var routing = entityManager.System<WoundDamageRoutingSystem>();
            var healing = entityManager.System<WoundHealingSystem>();
            var bleeding = entityManager.System<WoundBleedingSystem>();
            var damage = entityManager.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9
            var foreignPart = graph.GetBodyChildren(otherBody).First().Id;

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 10)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Slash", 20)));
            Assert.That(healing.ResolveHealingPart(body, null,
                entityManager.GetComponent<HealingComponent>(item).Damage,
                new List<ProtoId<DamageContainerPrototype>> { "Biological" },
                new HashSet<TreatmentCapability> { TreatmentCapability.Biological }, null, -10), Is.EqualTo(torso));

            Assert.That(healing.TryApplyHealing(body, null, (item, entityManager.GetComponent<HealingComponent>(item)),
                body, out _, out var stopped));
            Assert.That(stopped);
            Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(bleeding.GetPartRate(torso), Is.LessThan(bleeding.GetPartRate(head)));

            Assert.That(healing.TryApplyHealing(body, foreignPart,
                (item, entityManager.GetComponent<HealingComponent>(item)), body, out _, out _), Is.False);
            Assert.That(healing.TryApplyHealing(body, head,
                (incompatible, entityManager.GetComponent<HealingComponent>(incompatible)), body, out _, out _), Is.False);
        });
    }

    [Test]
    public async Task UntargetedHealingTreatsDamageAcrossAllPartsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WoundHealingBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var damage = entities.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed)
            var head = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyPartDamage(body, torso, Spec("Blunt", 10)));
            Assert.That(routing.TryApplyDamage(body, Spec("Blunt", -10)));
            Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.New(5)));

            Assert.That(routing.TryApplyDistributedDamage(body, Spec("Heat", 10), TargetBodyPart.All,
                DamageDistribution.SplitEvenly));
            Assert.That(damage.GetAllDamage(head).DamageDict[new ProtoId<DamageTypePrototype>("Heat")],
                Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(damage.GetAllDamage(torso).DamageDict[new ProtoId<DamageTypePrototype>("Heat")],
                Is.EqualTo(FixedPoint2.New(5)));
        });
    }

    [Test]
    public async Task ExactTargetSelectionAndMissingRejectionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            configuration.SetCVar(CCVars.TargetingEnabled, true);
            var body = entities.SpawnEntity("WoundHealingBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var resolver = entities.System<WoundTargetResolver>(); // WOLFGATE(Wolfmed): D10
            var torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Head).Id;

            Assert.That(resolver.TryResolveExact(body, TargetBodyPart.Head, out var selected), Is.True);
            Assert.That(selected, Is.EqualTo(head));
            // WOLFGATE(Wolfmed): D9: TargetBodyPart.Groin survives and folds to the torso part.
            Assert.That(resolver.TryResolveExact(body, TargetBodyPart.Groin, out selected), Is.True);
            Assert.That(selected, Is.EqualTo(torso));
            Assert.That(resolver.TryResolveExact(body, TargetBodyPart.LeftHand, out _), Is.False);
        });

        await server.WaitPost(() =>
            configuration.SetCVar(CCVars.TargetingEnabled, CCVars.TargetingEnabled.DefaultValue));
    }

    // WOLFGATE(Wolfmed): Onyx's RepairSelectionAndSnapshotValidationTest is not ported — ResolveRepairPartEvent /
    // ValidateRepairPartEvent live in Content.Shared._Onyx.Repairable, which is outside the port (PLAN §6.1).

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
