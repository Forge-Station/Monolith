using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
// WOLFGATE(Wolfmed): D7, Onyx's Content.Shared._Onyx.Medical.Surgery is not ported; phase 4 re-expressed its wound
// surgeries on Shitmed's step system, which is what SurgeryStepEvent below belongs to.
using Content.Server._Shitmed.Medical.Surgery; // WOLFGATE(Wolfmed): GetSingleton lives on the concrete server system.
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery; // WOLFGATE(Wolfmed): SurgeryStepEvent.
using Content.Shared._Shitmed.Medical.Surgery.Conditions; // WOLFGATE(Wolfmed): SurgeryValidEvent, the HOOK 25 seam.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12, GetAllDamage/SetDamage live on the compat facade.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

/// <summary>
/// Onyx's AmputationConsequenceTest, reduced to the three cases that survive the port.
/// </summary>
/// <remarks>
/// PLAN3 §6.2 T-AMP-CONSEQUENCE-1/-2/-3 (WP11-5), plus Onyx's SurgicalHealRemovesConsequenceAndUnblocks
/// restored in WP12-9 (PLAN4 §6.2).
/// <list type="bullet">
/// <item>SurgicalHealRemovesConsequenceAndUnblocksTest - RESTORED. Phase 3's skip was correct only while
/// D7 left Onyx's surgery out and nothing could clear the wound; WP12-4/WP12-5 re-expressed the cure on
/// Shitmed's step system (WolfmedSurgeryTreatWoundEffect + WFSurgeryHealAmputationConsequence) and HOOK 25
/// made the block real. Onyx's `graph.TryAttachPart(torso, spare) Is.False` half is asserted through
/// SurgeryValidEvent instead, because P4-D18 deliberately leaves SharedBodySystem.CanAttachPart
/// ungated - see WolfmedReattachTest for the full block/unblock pair.</item>
/// <item>HealingDamageKeepsConsequenceBlockedTest - still absent. Its entire payload is
/// `graph.TryAttachPart(torso, spare) Is.False`, i.e. the body-layer gate P4-D18 rules out; the
/// surgery-layer equivalent is WolfmedReattachTest's T-REATTACH-BLOCKED.</item>
/// </list>
/// For the same reason the surviving tests drop Onyx's `graph.HasAmputationConsequence(torso)` -
/// that method does not exist in Wolfgate.
/// </remarks>
[TestFixture]
[TestOf(typeof(AmputationSystem))]
public sealed class AmputationConsequenceTest : GameTest
{
    // WOLFGATE(Wolfmed): Onyx's fixture is Nubody-shaped (`InitialBody`, `TransplantCompatibility`, a bespoke
    // `bodyPartProfile`, `partType: Chest`). Rebuilt on a Shitmed body graph like WoundBleedingTest's, with
    // the two Onyx part-level datafields moved onto `- type: WolfmedBodyPart` (D8). Both parts inherit the
    // real human parts so they pick up Woundable/Damageable/vital exactly as a live limb does.
    //
    // The torso's `amputationConsequenceSeverity: 50` is load-bearing and must not be dropped (PLAN3
    // §8.7 hazard 7 / CRITIQUE3 M3-2): WolfmedBodyPartComponent defaults the field to 35 AND
    // WolfmedBodyPartSystem.Get's zeroed fallback singleton also reads 35, so on a stock body an
    // implementation that wrongly read the severity off the SEVERED PART instead of the parent stump
    // would still assert green. 50 is the only value that can tell the two apart.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: AmputationConsequenceTestGraph
  name: ""amputation consequence test body""
  root: torso
  slots:
    torso:
      part: AmputationConsequenceTestTorso
      connections:
      - head
    head:
      part: AmputationConsequenceTestHead

- type: entity
  id: AmputationConsequenceTestBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: AmputationConsequenceTestGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: AmputationConsequenceTestTorso
  parent: TorsoHuman
  components:
  - type: WolfmedBodyPart
    amputationConsequenceSeverity: 50

- type: entity
  id: AmputationConsequenceTestHead
  parent: HeadHuman
  components:
  - type: WolfmedBodyPart
    amputationThresholds:
      Slash: 70
";

    /// <summary>PLAN3 §6.2 T-AMP-CONSEQUENCE-1.</summary>
    [Test]
    public async Task TraumaticAmputationCreatesConsequenceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("AmputationConsequenceTestBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var damage = entities.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9

            // The fixture head's Slash threshold is 70, so progress = 70/70 = 1.0 arms the limb without
            // detaching it (AmputationSystem.HandlePartDamageApplied's `!Severable` branch returns).
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 70)));
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.True);
                Assert.That(entities.GetComponent<WoundableComponent>(head).Severable, Is.True);
            });

            // Slash 15 is exactly DefaultDismembermentFinishingDamage["Slash"], and damageBeforeHit (70)
            // is still at the threshold, so this hit detaches.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 15)));

            var wounds = entities.System<WoundSystem>()
                .GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .ToList();
            var consequence = wounds
                .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("AmputationConsequenceWound"));

            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.False);
                // WOLFGATE(Wolfmed): 50 comes from the fixture TORSO's amputationConsequenceSeverity, i.e. the
                // parent the stump is left on - not from the severed head, which carries the 35 default.
                Assert.That(consequence.Comp.Severity, Is.EqualTo(FixedPoint2.New(50)),
                    "the consequence severity must be read off the parent stump, not the severed part.");
                // WOLFGATE(Wolfmed): DismembermentSeverities[Head] = 200; also lands on the parent.
                Assert.That(wounds.Count(wound =>
                        wound.Comp.Prototype == new ProtoId<WoundPrototype>("DismembermentWound")),
                    Is.EqualTo(1));
                // Onyx's assertion, kept: AmputationConsequenceWound has `damageTypes: {}`, so creating it
                // must not put any damage on the stump.
                Assert.That(damage.GetAllDamage(torso).GetTotal(), Is.EqualTo(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>PLAN3 §6.2 T-AMP-CONSEQUENCE-2. A heal is never a finishing hit.</summary>
    [Test]
    public async Task HealingPartAboveThresholdDoesNotAmputateTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("AmputationConsequenceTestBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var damage = entities.System<WolfmedDamageableSystem>(); // WOLFGATE(Wolfmed): D12
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // SetDamage bypasses routing, so the part sits above its 70 threshold without ever having been
            // marked Severable - exactly Onyx's setup.
            damage.SetDamage(head, Spec("Slash", 80));
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", -1)));

            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.True,
                    "a negative damage application must never detach a limb.");
                // 80 - 1 = 79. IsFinishingHit skips every entry with amount <= 0, so the heal cannot
                // finish the limb no matter how far past the threshold the part is.
                Assert.That(damage.GetAllDamage(head).DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(79)));
            });
        });
    }

    /// <summary>PLAN3 §6.2 T-AMP-CONSEQUENCE-3. The full Severable set/reset cycle.</summary>
    [Test]
    public async Task HealingBelowResetRatioClearsSeverableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("AmputationConsequenceTestBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var woundable = entities.GetComponent<WoundableComponent>(head);

            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 70)));
            Assert.That(woundable.Severable, Is.True);

            // 70 - 15 = 55; progress 55/70 = 0.786 < WoundHostComponent.SeverableResetRatio (0.8), so the
            // limb is disarmed again.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", -15)));
            Assert.That(woundable.Severable, Is.False);

            // Back to 70 - but damageBeforeHit is 55, whose progress (0.786) is below 1, so ReachedThreshold
            // is false and this hit only re-arms the limb instead of taking it off.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 15)));
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, head), Is.True);
                Assert.That(woundable.Severable, Is.True);
            });
        });
    }

    /// <summary>
    /// Onyx's SurgicalHealRemovesConsequenceAndUnblocksTest, restored in WP12-9 (PLAN4 §6.2 T-SURG-AMPCONSEQ's
    /// sibling): the amputation consequence is curable, and curing it puts the attach surgery back.
    /// </summary>
    [Test]
    public async Task SurgicalHealRemovesConsequenceAndUnblocksTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("AmputationConsequenceTestBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var head = parts.Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9
            var attach = entities.System<SurgerySystem>().GetSingleton("SurgeryAttachHead")!.Value;

            // The fixture head's Slash threshold is 70, and Slash 15 is the host finishing minimum.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 70)));
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 15)));
            Assert.That(graph.BodyHasChild(body, head), Is.False);

            var woundable = entities.GetComponent<WoundableComponent>(torso);
            var consequence = new ProtoId<WoundPrototype>("AmputationConsequenceWound");
            Assert.Multiple(() =>
            {
                Assert.That(wounds.GetWounds((torso, woundable)).Count(w => w.Comp.Prototype == consequence),
                    Is.EqualTo(1));
                // WOLFGATE(Wolfmed): Onyx asserts `TryAttachPart Is.False` here. P4-D18 moves the block to the surgery
                // layer, so the equivalent assertion is that the attach surgery is not listable.
                Assert.That(AttachCancelled(entities, attach, body, torso), Is.True);
            });

            // WolfmedWoundSurgeryTest's bare step carrying the same WolfmedSurgeryTreatWoundEffect that
            // WFSurgeryStepHealAmputationConsequence ships with (WP12-5).
            var step = entities.SpawnEntity("WolfmedStepHealAmputation", map.GridCoords);
            var ev = new SurgeryStepEvent(body, body, torso, new List<EntityUid>(), step);
            entities.EventBus.RaiseLocalEvent(step, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(wounds.GetWounds((torso, woundable)).Count(w => w.Comp.Prototype == consequence),
                    Is.Zero, "the surgery is the only cure - AmputationConsequenceWound has `damageTypes: {}`.");
                Assert.That(AttachCancelled(entities, attach, body, torso), Is.False);
            });
        });
    }

    private static bool AttachCancelled(
        IEntityManager entities,
        EntityUid surgery,
        EntityUid body,
        EntityUid part)
    {
        var ev = new SurgeryValidEvent(body, part);
        entities.EventBus.RaiseLocalEvent(surgery, ref ev);
        return ev.Cancelled;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
