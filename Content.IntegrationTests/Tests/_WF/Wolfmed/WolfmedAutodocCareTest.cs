#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC5: what the pod leaves behind. No ghost damage after it closes a wound, blood out of the
/// reservoir when the patient is low, and clothing it can cut instead of waiting on for ever.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedAutodocCareTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedCareTestAutodoc
  parent: WFMachineAutodoc
  suffix: care test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
    clothingCutDelay: 0.2
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedCareTestBloodJug
  parent: Jug
  suffix: blood
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 300
        reagents:
        - ReagentId: Blood
          Quantity: 300
";

    /// <summary>
    /// A part whose wounds have been closed gives up the damage they were made from, and the body's total
    /// goes with it. Surgery closes wounds directly, so before this the patient came out of the pod with no
    /// wounds, no part the analyzer called hurt, and brute only a brute pack could clear.
    /// </summary>
    /// <remarks>
    /// The wounds are removed here rather than tended by the pod: a real procedure opens, cauterises and
    /// closes the arm, so it ends with surgical wounds of its own and no fixed number to assert. This is the
    /// end state every one of those paths reaches, and the sync is what the tend step calls.
    /// </remarks>
    [Test]
    public async Task ClosedWoundsLeaveNoGhostDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 25);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var blunt = new ProtoId<DamageTypePrototype>("Blunt");

            Assert.That(entities.GetComponent<DamageableComponent>(arm).Damage.DamageDict[blunt],
                Is.GreaterThan(FixedPoint2.Zero), "the arm was never hurt.");
            Assert.That(wounds.GetWounds(arm).ToList(), Is.Not.Empty, "the blow opened no wound.");

            foreach (var wound in wounds.GetWounds(arm).ToList())
                wounds.RemoveWound(wound.Owner);

            Assert.That(entities.GetComponent<DamageableComponent>(arm).Damage.DamageDict[blunt],
                Is.GreaterThan(FixedPoint2.Zero),
                "the fixture no longer shows the ghost damage this is about.");

            entities.System<WolfmedWoundDamageSyncSystem>().SyncPart(arm);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(arm).Damage.DamageDict[blunt],
                    Is.EqualTo(FixedPoint2.Zero),
                    "the arm kept the damage its closed wounds were made from.");
                Assert.That(entities.GetComponent<DamageableComponent>(body).Damage.DamageDict[blunt],
                    Is.EqualTo(FixedPoint2.Zero), "the body still totals damage no part is carrying.");
            });
        });
    }

    /// <summary>A jug of blood in the reservoir and a bled-out patient: the pod puts it back before it cuts.</summary>
    [Test]
    public async Task ReservoirBloodIsTransfusedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var blood = entities.System<BloodstreamSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            blood.TryModifyBloodLevel(body, FixedPoint2.New(-150));
            Assert.That(blood.GetBloodLevelPercentage(body), Is.LessThan(0.8f), "the fixture is not short of blood.");

            pod = Pod(entities, map, "WolfmedCareTestAutodoc");
            entities.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                entities.SpawnEntity("WolfmedCareTestBloodJug", map.GridCoords), null);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var blood = entities.System<BloodstreamSystem>();
            Assert.That(pod.Comp!.Transfusing || autodoc.NeedsTransfusion(body), Is.True,
                "the pod does not think the patient needs blood.");
            Assert.That(autodoc.TryTransfuse(pod, body), Is.True, "the pod refused to transfuse.");
            Assert.That(blood.GetBloodLevelPercentage(body), Is.GreaterThanOrEqualTo(0.89f),
                "the patient was not brought back up to the pod's target.");
            Assert.That(autodoc.NeedsTransfusion(body), Is.False);
        });
    }

    /// <summary>
    /// A dressed patient used to sit at WAITING FOR MATERIAL for ever. CUT CLOTHING destroys the jumpsuit
    /// and the procedure carries on. The pod's own cut is held off (a long delay) so the button is what unblocks it.
    /// </summary>
    [Test]
    public async Task CutClothingUnblocksTheProcedureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 25);

            suit = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, suit, "jumpsuit", force: true), Is.True,
                "the fixture could not dress the patient.");

            pod = Pod(entities, map, "WolfmedCareTestAutodoc");
            pod.Comp.ClothingCutDelay = 600f;
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryTendWoundsBrute", TargetBodyPart.LeftArm), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(pod.Comp!.BlockedReason, Is.Not.Null,
                $"the clothing never blocked the pod (state {pod.Comp.State}).");
            Assert.That(entities.Deleted(suit), Is.False, "the pod cut without waiting its delay out.");
            entities.System<AutodocSystem>().Control(pod, AutodocControl.CutClothing, null);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(suit), Is.True, "the jumpsuit survived being cut off.");
                Assert.That(pod.Comp!.BlockedReason, Is.Null, "the pod is still blocked on clothing.");
            });
        });
    }

    /// <summary>
    /// Playtest 5, "the autodoc should cut clothing automatically": a manual run on a patient who is awake and could
    /// undress is told to, and cut anyway once the delay is out. It used to wait on them for ever.
    /// </summary>
    [Test]
    public async Task PodCutsClothingOffAnAwakePatientAfterItsDelayTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 25);

            suit = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, suit, "jumpsuit", force: true), Is.True);

            pod = Pod(entities, map, "WolfmedCareTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryTendWoundsBrute", TargetBodyPart.LeftArm), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(suit), Is.True,
                    $"the pod never cut the jumpsuit off an awake patient (state {pod.Comp!.State}, blocked {pod.Comp.BlockedReason}).");
                Assert.That(pod.Comp!.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.Clothing), Is.EqualTo(1), "the patient was not told to undress first.");
                Assert.That(pod.Comp.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.ClothingAuto), Is.Zero, "an awake patient heard the helpless line.");
                Assert.That(pod.Comp.BlockedReason, Is.Null, "the pod is still blocked on clothing.");
            });
        });
    }

    /// <summary>With AUTO on and nobody able to undress the patient, the pod cuts by itself.</summary>
    [Test]
    public async Task AutoCutsClothingOffAHelplessPatientTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 25);

            suit = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, suit, "jumpsuit", force: true), Is.True);

            // Unconscious: there is nobody to take the clothes off, so AUTO does it. Pushed through
            // consciousness, which owns a wound host's mob state and would undo a mob state set by hand.
            entities.System<Content.Server._WF.Wolfmed.Consciousness.WolfmedConsciousnessSystem>()
                .SetExternalPressure(body, "wolfmed-care-test", 1f);

            pod = Pod(entities, map, "WolfmedCareTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
            pod.Comp.Auto = true;
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryTendWoundsBrute", TargetBodyPart.LeftArm), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(suit), Is.True,
                $"AUTO never cut the jumpsuit off (state {pod.Comp!.State}, blocked {pod.Comp.BlockedReason})."));
    }

    private static Entity<AutodocComponent> Pod(IEntityManager entities, TestMapData map, string prototype)
    {
        var pod = entities.SpawnEntity(prototype, map.GridCoords);
        return (pod, entities.GetComponent<AutodocComponent>(pod));
    }

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(amount), origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Spec(int amount, string type = "Blunt") => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
