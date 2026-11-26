using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components; // WOLFGATE: BloodstreamComponent is server-only here.
using Content.Server._Shitmed.Medical.Surgery; // WOLFGATE: GetSingleton lives on the concrete server system.
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery; // WOLFGATE: SurgeryStepEvent.
using Content.Shared._Shitmed.Medical.Surgery.Conditions; // WOLFGATE: SurgeryValidEvent, the HOOK 25 seam.
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8, WolfmedBodyPartSystem.Get
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: Onyx's SharedBodySystem.TryDetachPart lives on WolfmedBodySystem here.
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// PLAN §8.3 trap 2: a part re-attached via SharedBodySystem.AttachPart must rejoin live wound tracking, not
/// merely keep the wound data it already had. WoundScarTest proves a wound survives the round trip; this proves
/// the part is still a working Woundable/Damageable that creates new wounds and bleeds after fresh damage.
/// </summary>
/// <remarks>PLAN3 §6.2 T-REATTACH.</remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedReattachTest : GameTest
{
    // WOLFGATE: Shitmed body graph instead of Onyx's Nubody InitialBody; Chest→Torso is irrelevant here since
    // the only limb is an arm. MobBloodstream supplies BloodstreamComponent so BleedAmount is observable.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedReattachBodyGraph
  name: ""wolfmed reattach body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left arm
    left arm:
      part: LeftArmHuman

- type: entity
  id: WolfmedReattachBody
  parent: [InventoryBase, MobBloodstream]
  components:
  - type: Body
    prototype: WolfmedReattachBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost
";

    [Test]
    public async Task ReattachedPartRejoinsWoundTrackingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedReattachBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wfBody = entities.System<WolfmedBodySystem>(); // WOLFGATE
            var wfPart = entities.System<WolfmedBodyPartSystem>(); // WOLFGATE: D8
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Arm).Id;
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);
            var woundable = entities.GetComponent<WoundableComponent>(arm);

            Assert.That(wounds.GetWounds((arm, woundable)), Is.Empty);

            Assert.That(wfBody.TryDetachPart(arm)); // WOLFGATE: §2.7 detach shim
            Assert.That(graph.AttachPart(torso, "left arm", arm)); // WOLFGATE: Shitmed's attach takes a slot id

            Assert.That(entities.HasComponent<WoundableComponent>(arm), Is.True,
                "PLAN §8.3 trap 2: a reattached part must keep live wound tracking, not just its old wound data.");
            Assert.That(entities.HasComponent<DamageableComponent>(arm), Is.True);

            // Prototype data is never mutated at runtime, so the thresholds must read exactly as parts.yml
            // defines them for WFWolfmedBaseLeftArm (P3-D13), regardless of the detach/reattach round trip.
            var thresholds = wfPart.Get(arm).AmputationThresholds;
            Assert.Multiple(() =>
            {
                Assert.That(thresholds[new ProtoId<DamageTypePrototype>("Slash")], Is.EqualTo(FixedPoint2.New(130)));
                Assert.That(thresholds[new ProtoId<DamageTypePrototype>("Piercing")], Is.EqualTo(FixedPoint2.New(250)));
                Assert.That(thresholds[new ProtoId<DamageTypePrototype>("Blunt")], Is.EqualTo(FixedPoint2.New(250)));
            });

            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 15)), Is.True);

            var armWounds = wounds.GetWounds((arm, woundable)).ToList();
            Assert.That(armWounds, Has.Count.EqualTo(1),
                "fresh damage on the reattached part must still create a wound.");
            Assert.That(armWounds[0].Comp.Prototype, Is.EqualTo(new ProtoId<WoundPrototype>("SlashWound")));
            Assert.That(bloodstream.BleedAmount, Is.GreaterThan(0f),
                "the reattached part's fresh wound must rejoin the body's bleed total.");
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-REATTACH-BLOCKED. HOOK 25 closes the phase-3 P3-D2 gap at the SURGERY layer, not at
    /// SharedBodySystem.CanAttachPart (P4-D18): the body layer has five non-surgery callers, two of which are
    /// the Mono prosthetics traits that exist precisely to bolt a limb onto a stump.
    /// </summary>
    [Test]
    public async Task AmputatedLimbCannotBeReattachedUntilConsequenceTreatedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // WolfmedAmputationTest's fixture: torso + head + both arms, and a torso that carries
            // `amputationConsequenceSeverity: 50`.
            var body = entities.SpawnEntity("WolfmedAmputationBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var surgeries = entities.System<SurgerySystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var attach = surgeries.GetSingleton("SurgeryAttachLeftArm")!.Value;

            // Arm Slash threshold 130 arms the limb; Slash 15 is the host finishing minimum and detaches it.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 130)));
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 15)));
            Assert.That(graph.BodyHasChild(body, arm), Is.False);
            Assert.That(Consequences(entities, wounds, torso), Has.Count.EqualTo(1));

            Assert.Multiple(() =>
            {
                Assert.That(AttachCancelled(entities, attach, body, torso), Is.True,
                    "an untreated stump must hide every SurgeryAttach* surgery that targets it (HOOK 25).");
                // P4-D18's guard: the BODY layer is deliberately NOT gated, so the two Mono prosthetics traits
                // and the three admin commands that go through AttachPart keep working on exactly this stump.
                Assert.That(graph.CanAttachPart(torso, "left arm", arm), Is.True,
                    "CanAttachPart must stay true - hooking it would silently QueueDel a prosthetic.");
            });

            // WolfmedWoundSurgeryTest's bare `WolfmedSurgeryTreatWoundEffect { woundPrototype:
            // AmputationConsequenceWound }` step - the same component WP12-5 puts on
            // WFSurgeryStepHealAmputationConsequence.
            var step = entities.SpawnEntity("WolfmedStepHealAmputation", map.GridCoords);
            var ev = new SurgeryStepEvent(body, body, torso, new List<EntityUid>(), step);
            entities.EventBus.RaiseLocalEvent(step, ref ev);

            Assert.Multiple(() =>
            {
                Assert.That(Consequences(entities, wounds, torso), Is.Empty);
                Assert.That(AttachCancelled(entities, attach, body, torso), Is.False,
                    "treating the consequence must put the attach surgery back on the list.");
                Assert.That(graph.CanAttachPart(torso, "left arm", arm), Is.True);
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-SURG-AMP-CLEAN. Only AmputationSystem's traumatic path sets the block; a surgical removal
    /// leaves a clean stump.
    /// </summary>
    [Test]
    public async Task SurgicallyRemovedLimbCanStillBeReattachedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedAmputationBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var surgeries = entities.System<SurgerySystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var attach = surgeries.GetSingleton("SurgeryAttachLeftArm")!.Value;

            // TryDetachPart is what Shitmed's SurgeryPartRemoved path ends in; ApplyAmputationConsequences has
            // exactly one caller, AmputationSystem.TryAmputate, and this is not it.
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(arm));

            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, arm), Is.False);
                Assert.That(Consequences(entities, wounds, torso), Is.Empty,
                    "a surgical removal must leave no amputation consequence on the stump.");
                Assert.That(AttachCancelled(entities, attach, body, torso), Is.False,
                    "so the limb can be put straight back on.");
            });
        });
    }

    private static bool AttachCancelled(
        IEntityManager entities,
        EntityUid surgery,
        EntityUid body,
        EntityUid part)
    {
        // WolfmedStumpBlocksAttachment is private, so HOOK 25 is asserted through the event SharedSurgerySystem
        // actually raises (SurgeryValidEvent on the surgery singleton) rather than through the helper.
        var ev = new SurgeryValidEvent(body, part);
        entities.EventBus.RaiseLocalEvent(surgery, ref ev);
        return ev.Cancelled;
    }

    private static List<Entity<WoundComponent>> Consequences(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("AmputationConsequenceWound"))
            .ToList();
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    /// <summary>
    /// Playtest 5: "the tissue rupture won't go away after sorting it". The stump wound a torn-off arm leaves on the
    /// torso never heals by itself; putting the arm back closes it, and the stump overlay with it, while the damage the
    /// tearing did (the amputation consequence) stays for its surgery.
    /// </summary>
    [Test]
    public async Task ReattachmentClosesTheStumpTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = graph.GetBodyChildren(body)
                .Single(part => part.Component is { PartType: BodyPartType.Arm, Symmetry: BodyPartSymmetry.Left }).Id;
            var host = entities.GetComponent<WoundHostComponent>(body);

            Assert.That(entities.System<AmputationSystem>().TryAmputate(body, arm), Is.True, "the arm did not come off.");
            List<Entity<WoundComponent>> Stumps() => wounds.GetWounds(torso)
                .Where(wound => entities.HasComponent<WolfmedStumpComponent>(wound)).ToList();
            Assert.That(Stumps(), Has.Count.EqualTo(1), "the torn-off arm left no tagged stump on the torso.");
            Assert.That(Stumps()[0].Comp.Prototype, Is.EqualTo(host.DismembermentWound));

            Assert.That(graph.AttachPart(torso, "left arm", arm), Is.True, "the arm did not go back on.");
            var overlays = entities.System<Content.Server._WF.Wolfmed.Damage.WolfmedWoundOverlaySystem>();
            overlays.Refresh(body);
            var prototypes = wounds.GetWounds(torso).Select(wound => wound.Comp.Prototype).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(Stumps(), Is.Empty, "the stump wound survived the arm going back on.");
                Assert.That(prototypes, Does.Not.Contain(host.DismembermentWound));
                Assert.That(prototypes, Does.Contain(host.AmputationConsequenceWound), "the tearing's own damage went with the stump.");
                Assert.That(entities.GetComponent<PartDamageVisualsComponent>(body).Stumps.GetValueOrDefault(WolfmedArterySite.LArm),
                    Is.EqualTo(WolfmedWoundOverlay.None), "the stump is still drawn.");
            });
        });
    }
}
