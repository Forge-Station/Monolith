#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Explosion;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// HOOK 22's contract, the armour-plate hole it closes (P4-D14 / P3-D3), and a prototype-only sanity sweep over
/// every surgery WP12-5 shipped.
/// </summary>
/// <remarks>
/// PLAN4 §6.2 T-EXPLOSION-PLATE, T-EXPLOSION-WRAPPER and T-SURGERY-PROTOTYPE-SANITY (WP12-9). The sanity test
/// is co-located because it needs no mob and no map.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedExplosionSystem))]
public sealed class WolfmedExplosionTest : GameTest
{
    // WOLFGATE: a bespoke carrier rather than a shipped vest. Every shipped plate carrier also has an `Armor`
    // component with its own coefficients, which would muddy "the plate is what made the difference"; parenting
    // the abstract ClothingArmorPlate gives exactly the holder, the storage and the container and nothing else.
    // ArmorPlateBlunt_Slash absorbs Blunt at ratio 1 (armor_plates.yml), i.e. a full block, and has
    // maxDurability 150, comfortably above the 40 this test throws at it.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedPlateVest
  parent: ClothingArmorPlate
  name: ""wolfmed test plate carrier""
  components:
  - type: Clothing
    slots: [outerClothing]
  - type: StorageFill
    contents:
    - id: ArmorPlateBlunt_Slash
";

    /// <summary>
    /// PLAN4 §6.2 T-EXPLOSION-PLATE. The gate on P4-D14: without WP12-8's `_routedModifiers` origin-flag
    /// passthrough, SharedArmorPlateSystem.OnBeforeDamageChanged sees `OriginFlag == null` on the routed pass
    /// and its `Origin == null &amp;&amp; OriginFlag != Explosion` guard refuses plate protection outright.
    /// </summary>
    [Test]
    public async Task DistributedExplosionDamageKeepsPlateProtectionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var inventory = entities.System<InventorySystem>();

            var plated = entities.SpawnEntity("MobHuman", map.GridCoords);
            var vest = entities.SpawnEntity("WolfmedPlateVest", map.GridCoords);
            Assert.That(inventory.TryEquip(plated, vest, "outerClothing", force: true));
            Assert.That(entities.HasComponent<Content.Shared.Armor.ArmorPlateProtectedComponent>(plated), Is.True,
                "equipping a carrier with an active plate is what arms the protection handler.");

            var bare = entities.SpawnEntity("MobHuman", map.GridCoords);

            // Routing-API level, the same style phase 3's T-AMP-EXPLOSION uses - no live grenade. Blunt rather
            // than Piercing because the per-part finishing minimum for Blunt is the host default 50, so a ~4
            // per-limb share can never trip the explosion amputation roll and make this flaky.
            Assert.That(Blast(routing, plated), Is.True);
            Assert.That(Blast(routing, bare), Is.True);

            var platedDamage = Total(entities, plated);
            var bareDamage = Total(entities, bare);

            Assert.Multiple(() =>
            {
                Assert.That(bareDamage, Is.GreaterThan(FixedPoint2.Zero),
                    "the unarmoured control must actually have taken the blast.");
                Assert.That(platedDamage, Is.LessThan(bareDamage),
                    "the plate flag must survive the distributed routed pass (P4-D14 / P3-D3).");
                // ArmorPlateBlunt_Slash's Blunt absorptionRatio is 1, so `remainder.Empty` cancels the hit
                // outright on every per-part share.
                Assert.That(platedDamage, Is.EqualTo(FixedPoint2.Zero));
            });

            // The both-directions closure: a mob WITHOUT WoundHostComponent never reaches the routed pass at
            // all, and its plate protection is the vanilla path, unchanged by phase 4. WolfmedSurgeryControlBody
            // is WolfmedWoundSurgeryTest's non-host fixture: the same body graph, no WoundHost.
            var damageable = entities.System<DamageableSystem>();
            var platedControl = entities.SpawnEntity("WolfmedSurgeryControlBody", map.GridCoords);
            var controlVest = entities.SpawnEntity("WolfmedPlateVest", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(platedControl), Is.False);
            Assert.That(inventory.TryEquip(platedControl, controlVest, "outerClothing", force: true));
            var bareControl = entities.SpawnEntity("WolfmedSurgeryControlBody", map.GridCoords);

            // Vanilla, unrouted: the damage lands on the BODY's own Damageable rather than being split across
            // parts, so this half is measured there.
            foreach (var target in new[] { platedControl, bareControl })
                damageable.TryChangeDamage(target, Spec("Blunt", 40),
                    ignoreResistances: true, ignoreGlobalModifiers: true,
                    originFlag: DamageableSystem.DamageOriginFlag.Explosion);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(bareControl).TotalDamage,
                    Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(entities.GetComponent<DamageableComponent>(platedControl).TotalDamage,
                    Is.EqualTo(FixedPoint2.Zero),
                    "a non-wound-host keeps flat vanilla plate protection against explosions (D2).");
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-EXPLOSION-WRAPPER. Pins HOOK 22's contract so the package cannot land with the three
    /// upstream lines forgotten and every other test still green - the exact P3-D3 failure shape.
    /// </summary>
    [Test]
    public async Task ExplosionWrapperSpreadsOnHostsAndFallsThroughOtherwiseTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var explosion = entities.System<WolfmedExplosionSystem>();
            var graph = entities.System<SharedBodySystem>();

            explosion.ForcedRoll = 1f; // no limb loss here, this test counts damaged parts.
            var host = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(host), Is.True);
            Assert.That(explosion.TryApplyExplosionDamage(host, Spec("Blunt", 40)), Is.True,
                "a wound host must be handled by the wrapper, which is what makes ExplosionSystem skip its own call.");

            var hit = graph.GetBodyChildren(host)
                .Count(part => entities.TryGetComponent(part.Id, out DamageableComponent? damageable) &&
                               damageable.TotalDamage > FixedPoint2.Zero);
            Assert.That(hit, Is.GreaterThanOrEqualTo(2),
                "the blast must spread across limbs instead of landing on one random part.");
            explosion.ForcedRoll = null;

            var control = entities.SpawnEntity("WolfmedSurgeryControlBody", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(control), Is.False);

            Assert.Multiple(() =>
            {
                Assert.That(explosion.TryApplyExplosionDamage(control, Spec("Blunt", 40)), Is.False,
                    "a non-host must fall through so ExplosionSystem's own TryChangeDamage still runs (D2).");
                Assert.That(entities.GetComponent<DamageableComponent>(control).TotalDamage,
                    Is.EqualTo(FixedPoint2.Zero),
                    "and the wrapper must apply nothing itself on that path.");
                Assert.That(Total(entities, control), Is.EqualTo(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>A big blast tears limbs off by a roll on its size; a small one never does, and the head and torso stay.</summary>
    [Test]
    public async Task BlastSizeRollsLimbsOffTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var explosion = server.System<WolfmedExplosionSystem>();

        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            int Limbs(EntityUid body) => graph.GetBodyChildren(body).Count();
            // Pinned: the blast's damage is spread over the parts by a weighted roll, and a rare split puts enough on
            // one limb for the ordinary damage-driven amputation to take it, which the forced roll cannot stop.
            server.ResolveDependency<IRobustRandom>().SetSeed(20260927);

            explosion.ForcedRoll = 0f;
            var small = entities.SpawnEntity("MobHuman", map.GridCoords);
            var before = Limbs(small);
            explosion.TryApplyExplosionDamage(small, Spec("Blunt", 25));
            Assert.That(Limbs(small), Is.EqualTo(before), "a blast under the minimum never severs.");

            // 200 is past the full mark of 150: two limbs are rolled, and the forced roll lands both.
            var big = entities.SpawnEntity("MobHuman", map.GridCoords);
            explosion.TryApplyExplosionDamage(big, Spec("Blunt", 200));
            Assert.Multiple(() =>
            {
                Assert.That(Limbs(big), Is.LessThanOrEqualTo(before - 2));
                Assert.That(graph.GetBodyChildrenOfType(big, BodyPartType.Head).Any(), Is.True);
                Assert.That(graph.GetBodyChildrenOfType(big, BodyPartType.Torso).Any(), Is.True);
            });

            explosion.ForcedRoll = 0.99f;
            var lucky = entities.SpawnEntity("MobHuman", map.GridCoords);
            explosion.TryApplyExplosionDamage(lucky, Spec("Blunt", 200));
            Assert.That(Limbs(lucky), Is.EqualTo(before), "a failed roll leaves every limb on.");
            explosion.ForcedRoll = null;
        });
        await server.WaitPost(() => explosion.ForcedRoll = null);
    }

    /// <summary>
    /// PLAN4 §6.2 T-SURGERY-PROTOTYPE-SANITY. Prototype-only. A typo'd step id is a runtime GetSingleton null
    /// that no lint can see, and the closed-loop invariant is what permanently kills CRITIQUE4 B1's bug class:
    /// an incision wound opened by a surgery that has no way to close it.
    /// </summary>
    [Test]
    public async Task EveryWoundSurgeryStepResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        // WP12-5's shipped set: six wound surgeries plus seven organ heals.
        string[] surgeryIds =
        [
            "WFSurgeryStopBleeding", "WFSurgeryStopInternalBleeding", "WFSurgeryMendFracture",
            "WFSurgeryHealAmputationConsequence", "WFSurgeryTendWoundsBruteDeep", "WFSurgeryTendWoundsBurnDeep",
            "WFSurgeryHealHeart", "WFSurgeryHealLungs", "WFSurgeryHealLiver", "WFSurgeryHealStomach",
            "WFSurgeryHealKidneys", "WFSurgeryHealBrain", "WFSurgeryHealEyes",
        ];

        string[] stepIds =
        [
            "WFSurgeryStepSutureBleeding", "WFSurgeryStepStopInternalBleeding", "WFSurgeryStepSetBone",
            "WFSurgeryStepMendFracture", "WFSurgeryStepHealAmputationConsequence", "WFSurgeryStepHealBrain",
            "WFSurgeryStepHealEyes", "WFSurgeryStepHealHeart", "WFSurgeryStepHealLungs", "WFSurgeryStepHealLiver",
            "WFSurgeryStepHealStomach", "WFSurgeryStepHealKidneys",
        ];

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in surgeryIds)
                {
                    Assert.That(prototypes.TryIndex(id, out EntityPrototype? proto), Is.True, $"{id} must exist");
                    Assert.That(proto!.TryGetComponent<SurgeryComponent>(out var surgery, factory), Is.True,
                        $"{id} must carry a Surgery component");
                    Assert.That(surgery!.Steps, Is.Not.Empty, $"{id} must declare steps");
                    Assert.That(proto.Name, Is.Not.Empty, $"{id} must have a name (CRITIQUE4 M6)");
                    Assert.That(proto.HideSpawnMenu, Is.True, $"{id} must be HideSpawnMenu (CRITIQUE4 M6)");

                    // A condition on the surgery is what keeps it off a healthy patient's list;
                    // SurgerySystem.RefreshUI raises SurgeryValidEvent on the surgery singleton only (§8.5 trap 9).
                    Assert.That(proto.Components.Keys.Any(IsCondition), Is.True,
                        $"{id} must carry at least one listing condition");

                    foreach (var stepId in surgery.Steps)
                    {
                        Assert.That(prototypes.TryIndex(stepId.Id, out EntityPrototype? stepProto), Is.True,
                            $"{id} references step {stepId.Id}, which does not resolve");
                        Assert.That(stepProto!.Components.ContainsKey("SurgeryStep"), Is.True,
                            $"{id} references {stepId.Id}, which is not a surgery step");
                    }

                    if (surgery.Requirement is { } requirement)
                        Assert.That(prototypes.HasIndex<EntityPrototype>(requirement.Id), Is.True,
                            $"{id}'s requirement {requirement.Id} must resolve");
                }

                foreach (var id in stepIds)
                {
                    Assert.That(prototypes.TryIndex(id, out EntityPrototype? proto), Is.True, $"{id} must exist");
                    Assert.That(proto!.Components.ContainsKey("SurgeryStep"), Is.True,
                        $"{id} must carry a SurgeryStep component");
                    Assert.That(proto.Name, Is.Not.Empty, $"{id} must have a name (CRITIQUE4 M6)");
                    Assert.That(proto.HideSpawnMenu, Is.True, $"{id} must be HideSpawnMenu (CRITIQUE4 M6)");
                }
            });

            // The closed loop (CRITIQUE4 B1 / §8.5 traps 15 and 15a). SurgicalIncisionWound is
            // `mergeMode: SeparateInstances`, so a step that opens one inside a surgery with no Close step leaks
            // a permanent bleeder, one per operation. Only real surgery STEPS are considered - a bare
            // [TestPrototypes] carrier of the effect component is not part of any operation.
            var opening = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract &&
                                proto.Components.ContainsKey("SurgeryStep") &&
                                proto.Components.ContainsKey("WolfmedSurgeryIncisionWoundEffect"))
                .Select(proto => proto.ID)
                .ToList();

            Assert.That(opening, Is.Not.Empty,
                "PROTO G must put the incision wound effect on at least one real step, or P4-D21 did not ship.");

            var closing = Treating(prototypes, factory, WolfmedIncisionTreatment.Close);
            var clamping = Treating(prototypes, factory, WolfmedIncisionTreatment.Clamp);

            Assert.Multiple(() =>
            {
                Assert.That(closing, Is.Not.Empty);
                Assert.That(clamping, Is.Not.Empty);
            });

            var surgeries = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.Components.ContainsKey("Surgery"))
                .ToList();

            List<string> Reachable(EntityPrototype proto)
            {
                var steps = new List<string>();
                var seen = new HashSet<string>();
                var current = proto;
                while (current != null && seen.Add(current.ID))
                {
                    if (!current.TryGetComponent<SurgeryComponent>(out var surgery, factory))
                        break;

                    steps.AddRange(surgery.Steps.Select(step => step.Id));
                    current = surgery.Requirement is { } requirement &&
                              prototypes.TryIndex(requirement.Id, out EntityPrototype? parent)
                        ? parent
                        : null;
                }

                return steps;
            }

            Assert.Multiple(() =>
            {
                // WOLFGATE (measured correction to PLAN4 §6.2's wording): PLAN4 asks that every surgery reaching
                // an opening step also reach a Close step through its own requirement chain. Shipped Shitmed does
                // not work that way and never has - `SurgeryCloseIncision` is a SEPARATE surgery the medic runs
                // afterwards, which is why ~30 shipped surgeries (SurgeryAttachHands, SurgeryInsertBorgBrain,
                // every organ remove/insert, and WP12-5's own two head organ heals ending on
                // SurgeryStepSealOrganWound) would fail that assertion. WP12-5's handoff note B says the same.
                // The invariant that actually kills CRITIQUE4 B1's bug class is CLAMPABILITY: a
                // `SeparateInstances` bleeder is only a bug if the operation that opened it cannot stop it.
                foreach (var step in opening)
                {
                    var users = surgeries.Where(proto => Reachable(proto).Contains(step)).ToList();
                    Assert.That(users, Is.Not.Empty, $"{step} is reachable from no surgery at all");
                    foreach (var user in users)
                    {
                        Assert.That(Reachable(user).Any(clamping.Contains), Is.True,
                            $"{user.ID} can open a SurgicalIncisionWound via {step} but reaches no Clamp step");
                    }
                }

                // And the wound is removable: SurgeryCloseIncision is the universal finisher, and WP12-5's five
                // incision-based wound surgeries close their own incision on SurgeryStepSealTendWound (PROTO G's
                // fourth site), so they need no second operation.
                Assert.That(Reachable(prototypes.Index<EntityPrototype>("SurgeryCloseIncision")).Any(closing.Contains),
                    Is.True, "SurgeryCloseIncision must carry the Close effect - it is the universal finisher.");

                foreach (var id in new[]
                         {
                             "WFSurgeryStopInternalBleeding", "WFSurgeryMendFracture", "WFSurgeryHealAmputationConsequence",
                             "WFSurgeryTendWoundsBruteDeep", "WFSurgeryTendWoundsBurnDeep",
                         })
                {
                    Assert.That(Reachable(prototypes.Index<EntityPrototype>(id)).Any(closing.Contains), Is.True,
                        $"{id} must close the incision it opened on its own seal step (PROTO G).");
                }

                // The two shallow tend surgeries use SurgeryStepCarefulIncisionScalpel, which carries no wound
                // effect and no clamp or close step. CRITIQUE4 B1 was exactly the mistake of putting the effect
                // there, on the commonest surgeries in the game.
                foreach (var id in new[] { "SurgeryTendWoundsBrute", "SurgeryTendWoundsBurn" })
                {
                    var proto = prototypes.Index<EntityPrototype>(id);
                    Assert.That(Reachable(proto).Any(opening.Contains), Is.False,
                        $"{id} must never reach a step that opens a SurgicalIncisionWound (§8.5 trap 15).");
                    Assert.That(Reachable(proto).Any(clamping.Contains), Is.False,
                        "and it is exactly because they have no clamp step that they must not open one.");
                }
            });
        });
    }

    private static HashSet<string> Treating(
        IPrototypeManager prototypes,
        IComponentFactory factory,
        WolfmedIncisionTreatment treatment)
    {
        return prototypes.EnumeratePrototypes<EntityPrototype>()
            .Where(proto => proto.Components.ContainsKey("SurgeryStep") &&
                            proto.TryGetComponent<WolfmedSurgeryIncisionTreatmentEffectComponent>(
                                out var effect, factory) &&
                            effect.Treatment == treatment)
            .Select(proto => proto.ID)
            .ToHashSet();
    }

    private static bool IsCondition(string componentName) =>
        componentName.StartsWith("Surgery") && componentName.EndsWith("Condition") ||
        componentName.StartsWith("WolfmedSurgery") && componentName.EndsWith("Condition");

    private static bool Blast(WoundDamageRoutingSystem routing, EntityUid body)
    {
        return routing.TryRouteDistributedDamage(body,
            Spec("Blunt", 40),
            Content.Shared._Shitmed.Targeting.TargetBodyPart.All,
            Content.Shared._Onyx.Targeting.DamageDistribution.SplitWithVariation,
            ignoreResistances: true,
            interruptsDoAfters: false,
            variation: 0f,
            isExplosion: true,
            woundSeverityMultiplier: 1f,
            originFlag: DamageableSystem.DamageOriginFlag.Explosion);
    }

    private static FixedPoint2 Total(IEntityManager entities, EntityUid body)
    {
        var total = FixedPoint2.Zero;
        foreach (var (part, _) in entities.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (entities.TryGetComponent(part, out DamageableComponent? damageable))
                total += damageable.TotalDamage;
        }

        return total;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
