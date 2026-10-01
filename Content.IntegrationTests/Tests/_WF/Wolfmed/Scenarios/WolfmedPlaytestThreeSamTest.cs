#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 3, S.A.M. round (2026-09-24): the pod holds one patient, an AUTO run is one run, the stall guard only
/// speaks for a step that really is doing nothing, and the pod undresses what it cannot cut.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedPlaytestThreeSamTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedSamTestAutodoc
  parent: WFMachineAutodoc
  suffix: playtest 3 sam
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
    clothingCutDelay: 0.5
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedSamTestOpiateJug
  parent: Jug
  suffix: opiate
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 200
        reagents:
        - ReagentId: WFWolfmedOpiate
          Quantity: 200
";

    /// <summary>
    /// "You can put two people into the pod if one is left on top of it." A is ejected, B goes in, and A tries the
    /// verb and a drag-drop. The pod holds exactly one, its panel shows B, and A is off the pod on the floor beside
    /// it, not lying on its tile under the lid where it looks like a second occupant.
    /// </summary>
    [Test]
    public async Task PodHoldsOneTest()
    {
        Out = TestContext.Out;
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid a = default, b = default;

        await Server.WaitAssertion(() =>
        {
            Floor(map);
            pod = Pod(new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            a = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            b = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 1.5f));

            // A is out cold, the way a patient usually arrives (a conscious body lifted in by somebody else gets a
            // do-after first). A body that does not move is not pushed off the machine by physics, which is what kept
            // the owner's patient lying on the lid.
            Consciousness().SetExternalPressure(a, PressureKey, 1f);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(DragDrop(pod, b, a), Is.True, "A could not be put in.");
            Assert.That(autodoc.GetOccupant(pod), Is.EqualTo(a));
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            autodoc.Control(pod, AutodocControl.Eject, b);
            Assert.That(autodoc.GetOccupant(pod), Is.Null, "the eject left A inside.");
        });
        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(OnPodTile(pod, a), Is.False,
                $"A was ejected onto the pod's own tile ({Position(a)}), where the next occupant's lid covers them.");

            Assert.That(DragDrop(pod, b, b), Is.True, "B could not climb in.");

            // A comes round, then tries both ways in while B is inside.
            Consciousness().SetExternalPressure(a, PressureKey, 0f);
        });
        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(pod, a, typeof(AlternativeVerb));
            var enter = verbs.FirstOrDefault(verb => verb.Text == Loc.GetString("wolfmed-autodoc-verb-enter"));
            enter?.Act?.Invoke();
            DragDrop(pod, a, a);
            // Every constructible machine is climbable here; the pod refuses, so nobody lies on its lid.
            var climbed = SEntMan.System<Content.Shared.Climbing.Systems.ClimbSystem>().TryClimb(a, a, pod.Owner, out _);

            Assert.Multiple(() =>
            {
                Assert.That(enter, Is.Null, "the enter verb was offered to A with B inside.");
                Assert.That(climbed, Is.False, "A was allowed to climb onto the occupied pod.");
                Assert.That(Contents(pod), Is.EqualTo(new[] { b }), "the pod does not hold exactly B.");
                Assert.That(autodoc.GetOccupant(pod), Is.EqualTo(b));
                Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(a), Is.False,
                    "A ended up in a container.");
                Assert.That(OnPodTile(pod, a), Is.False, "A is back on the pod's tile.");
                Assert.That(SEntMan.GetComponent<Content.Shared.Climbing.Components.ClimbingComponent>(a).IsClimbing,
                    Is.False, "A is still climbing on something.");
            });

            autodoc.UpdateUi(pod);
            Assert.That(SEntMan.System<UserInterfaceSystem>()
                    .TryGetUiState<AutodocBuiState>(pod.Owner, AutodocUiKey.Key, out var state), Is.True);
            Assert.That(state!.Diagnostics?.TargetEntity, Is.EqualTo(SEntMan.GetNetEntity(b)),
                "the patient panel is not showing B.");
        });

        // A is free: nothing holds them on the pod or in it, and they can walk off.
        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(OnPodTile(pod, a), Is.False, $"A drifted onto the pod ({Position(a)}).");
            Assert.That(Contents(pod), Is.EqualTo(new[] { b }));
        });
    }

    /// <summary>
    /// "On autofix, it queues 1 item, finishes it, then starts a new queue, which produces a lot of dialog." A round
    /// lodged in a deep chest wound needs the round out, then the deep tend, then the shallow tend the deep one leaves
    /// behind, and each used to be its own queue with its own AUTO ENGAGED, start line, anaesthetic line and QUEUE
    /// COMPLETE. Now it is one run: each of those once, and one announcement per procedure.
    /// </summary>
    [Test]
    public async Task AutoIsOneRunTest()
    {
        Out = TestContext.Out;
        var map = await Pair.CreateTestMap();
        var scenario = new WolfmedScenario(SEntMan);
        Entity<AutodocComponent> pod = default;
        EntityUid patient = default, torso = default;
        var heads = new List<string>();

        await Server.WaitAssertion(() =>
        {
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            var autodoc = SEntMan.System<AutodocSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            // Pain would faint the patient mid-run, which is a different story.
            SEntMan.EnsureComponent<Content.Shared.Traits.Assorted.PainNumbnessComponent>(patient);

            torso = Part(patient, BodyPartType.Torso, BodyPartSymmetry.None);
            var round = SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "WFWolfmedLodgedRoundWound", FixedPoint2.New(110));
            Assert.That(round, Is.Not.Null, "the fixture could not open the wound.");
            SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(round!.Value, "WFWolfmedSpentRound", 1, 6);
            // The damage the round did, on the part, as a real hit leaves it: the tend surgeries only list on a hurt body.
            SEntMan.System<DamageableSystem>().SetDamage(torso, SEntMan.GetComponent<DamageableComponent>(torso),
                Spec("Piercing", 110));

            pod = Pod(map.GridCoords);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                SEntMan.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                SEntMan.SpawnEntity("WolfmedSamTestOpiateJug", map.GridCoords), null), Is.True);
            autodoc.SetAuto(pod, true);
            // A conscious patient lifted in by the medic would go in through a do-after; the run is what this test is about.
            Assert.That(autodoc.TryInsert(pod, patient), Is.True, "the patient could not be put in.");
        });

        var done = false;
        for (var i = 0; i < 80 && !done; i++)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() =>
            {
                if (pod.Comp!.Queue.Count > 0)
                {
                    var head = $"{pod.Comp.Queue[0].Surgery}{(pod.Comp.Queue[0].Continuation ? " (closure)" : "")}";
                    if (heads.Count == 0 || heads[^1] != head)
                        heads.Add(head);
                }

                done = pod.Comp.State == AutodocState.Complete && pod.Comp.AutoSaidNothing;
            });
        }

        await Server.WaitAssertion(() =>
        {
            var events = pod.Comp!.VoiceEvents;
            int Said(AutodocVoiceEvent voice) => events.GetValueOrDefault(voice);
            var procedures = heads.Count(head => !head.EndsWith("(closure)"));
            Out.WriteLine($"AutoIsOneRunTest: procedures in order: {string.Join(" > ", heads)}");
            Out.WriteLine($"AutoIsOneRunTest: voice events: {string.Join(", ", events.Select(pair => $"{pair.Key}={pair.Value}"))}; " +
                          $"procedures announced {pod.Comp.ProceduresAnnounced}");

            Assert.Multiple(() =>
            {
                Assert.That(done, Is.True, $"the run never ended (state {pod.Comp.State}, queue {pod.Comp.Queue.Count}).");
                Assert.That(procedures, Is.GreaterThanOrEqualTo(3), "the fixture should need at least three procedures.");
                Assert.That(SEntMan.System<WolfmedEmbeddedObjectSystem>().GetPartCount(torso), Is.Zero, "the round is still in.");
                // The hole the round left is the patient's, so the run treats it rather than calling it the pod's work.
                Assert.That(SEntMan.System<WoundSystem>().GetWounds(torso).Any(w => !SEntMan.HasComponent<WolfmedPodWoundComponent>(w)),
                    Is.False, "the run ended with the patient's own wound still open on the torso.");
                Assert.That(Said(AutodocVoiceEvent.AutoEngaged), Is.EqualTo(1), "AUTO ENGAGED");
                Assert.That(Said(AutodocVoiceEvent.QueueComplete), Is.EqualTo(1), "QUEUE COMPLETE");
                Assert.That(Said(AutodocVoiceEvent.Greeting), Is.EqualTo(1), "the greeting");
                Assert.That(Said(AutodocVoiceEvent.OperatorStart), Is.EqualTo(1), "the start line");
                Assert.That(Said(AutodocVoiceEvent.Anaesthetic) + Said(AutodocVoiceEvent.NoAnaesthetic), Is.EqualTo(1),
                    "the anaesthetic line");
                Assert.That(Said(AutodocVoiceEvent.AutoNothing), Is.Zero, "NOTHING MORE I CAN DO after QUEUE COMPLETE");
                Assert.That(Said(AutodocVoiceEvent.Stall), Is.Zero, "THIS IS NOT WORKING");
                Assert.That(pod.Comp.ProceduresAnnounced, Is.EqualTo(procedures), "one announcement per procedure");
                Assert.That(pod.Comp.FailedProcedures, Is.Empty, "a procedure was abandoned");
            });
        });
    }

    /// <summary>
    /// "This is not working" while it is working. A full organic queue on a patient a medic has already dosed: the
    /// packs took the Brute and Burn damage off, the wounds are still there. The tend step used to return before it
    /// touched a wound unless the body still carried damage of its group, so its passes changed nothing and the guard
    /// abandoned it after the third. No Stall line may fire, nothing may be abandoned, and every wound gets treated.
    /// </summary>
    [Test]
    public async Task NoStallWhileWorkingTest()
    {
        Out = TestContext.Out;
        var map = await Pair.CreateTestMap();
        var scenario = new WolfmedScenario(SEntMan);
        Entity<AutodocComponent> pod = default;
        EntityUid patient = default;

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = 1f);
        await Server.WaitAssertion(() =>
        {
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            var autodoc = SEntMan.System<AutodocSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var damage = SEntMan.System<DamageableSystem>();
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<Content.Shared.Traits.Assorted.PainNumbnessComponent>(patient);

            damage.TryChangeDamage(patient, Spec("Slash", 40), origin: null, targetPart: TargetBodyPart.LeftArm);
            damage.TryChangeDamage(patient, Spec("Blunt", 30), origin: null, targetPart: TargetBodyPart.RightLeg);
            damage.TryChangeDamage(patient, Spec("Heat", 30), origin: null, targetPart: TargetBodyPart.Torso);

            // A brute pack and a tube of ointment: the damage goes, the wounds do not.
            var packs = new DamageSpecifier();
            foreach (var type in new[] { "Slash", "Blunt", "Piercing", "Heat" })
                packs.DamageDict[type] = FixedPoint2.New(-200);
            damage.TryChangeDamage(patient, packs, true);

            pod = Pod(map.GridCoords);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                SEntMan.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                SEntMan.SpawnEntity("WolfmedSamTestOpiateJug", map.GridCoords), null), Is.True);
            autodoc.SetAuto(pod, true);
            Assert.That(autodoc.TryInsert(pod, patient), Is.True);
        });

        var done = false;
        for (var i = 0; i < 80 && !done; i++)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() => done = pod.Comp!.State == AutodocState.Complete && pod.Comp.AutoSaidNothing);
        }

        await Server.WaitAssertion(() =>
        {
            var left = new List<string>();
            foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(patient))
            {
                foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(part))
                {
                    if (!SEntMan.HasComponent<WolfmedPodWoundComponent>(wound) && wound.Comp.State == WoundState.Open)
                        left.Add($"{wound.Comp.Prototype}={wound.Comp.Severity}");
                }
            }

            Out.WriteLine($"NoStallWhileWorkingTest: voice events: {string.Join(", ", pod.Comp!.VoiceEvents.Select(pair => $"{pair.Key}={pair.Value}"))}");
            Assert.Multiple(() =>
            {
                Assert.That(done, Is.True, $"the run never ended (state {pod.Comp.State}).");
                Assert.That(pod.Comp.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.Stall), Is.Zero,
                    "the pod said THIS IS NOT WORKING.");
                Assert.That(pod.Comp.FailedProcedures, Is.Empty,
                    $"the pod abandoned {string.Join(", ", pod.Comp.FailedProcedures)}.");
                Assert.That(left, Is.Empty, $"open wounds left untreated: {string.Join(", ", left)}");
            });
        });

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// "WAITING: Clothing forever." The owner's loadout on a patient who cannot undress, hurt on the head, a hand, a
    /// foot and the torso. Shitmed's armour check reads the head slot for the head, the gloves for a hand, the shoes
    /// for a foot and the suit and jumpsuit for the torso; the pod only ever cut the last two. Now AUTO cuts those,
    /// takes the gloves and boots off whole (the tray first, the floor beside the pod after), leaves the goggles,
    /// mask, strobe, headset, back-slot hardsuit, belt and tank alone, and treats all four parts.
    /// </summary>
    [Test]
    public async Task PodUndressesWhatItCannotCutTest()
    {
        Out = TestContext.Out;
        var map = await Pair.CreateTestMap();
        var scenario = new WolfmedScenario(SEntMan);
        Entity<AutodocComponent> pod = default;
        EntityUid patient = default;
        var kit = new Dictionary<string, EntityUid>();

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = 1f);
        await Server.WaitAssertion(() =>
        {
            Floor(map);
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            patient = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            SEntMan.EnsureComponent<Content.Shared.Traits.Assorted.PainNumbnessComponent>(patient);

            // Hurt first, dressed after, so the armour does not change the wounds.
            foreach (var part in new[] { TargetBodyPart.Head, TargetBodyPart.LeftHand, TargetBodyPart.RightFoot, TargetBodyPart.Torso })
                SEntMan.System<DamageableSystem>().TryChangeDamage(patient, Spec("Slash", 20), origin: null, targetPart: part);

            Dress(patient, kit, new()
            {
                ["jumpsuit"] = "ClothingUniformJumpsuitEngineering",
                ["outerClothing"] = "ClothingOuterVestWebElite",
                ["eyes"] = "ClothingEyesGlassesMeson",
                ["mask"] = "ClothingMaskGasSecurity",
                ["neck"] = "ClothingNeckIFFNeutral",
                ["ears"] = "ClothingHeadsetEngineering",
                ["back"] = "ClothingOuterHardsuitEngineering",
                ["gloves"] = "ClothingHandsGlovesCombat",
                ["belt"] = "ClothingBeltUtility",
                ["shoes"] = "ClothingShoesBootsCombat",
                ["suitstorage"] = "NitrogenTankFilled",
            });

            // Out cold: nobody is going to undress this patient, so AUTO does it.
            Consciousness().SetExternalPressure(patient, PressureKey, 1f);

            pod = Pod(new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                SEntMan.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            SEntMan.System<AutodocSystem>().SetAuto(pod, true);
            Assert.That(SEntMan.System<AutodocSystem>().TryInsert(pod, patient), Is.True);
        });

        var done = false;
        var sawGarment = false;
        for (var i = 0; i < 120 && !done; i++)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() =>
            {
                sawGarment |= pod.Comp!.BlockingGarment is { } garment &&
                              (garment == kit["gloves"] || garment == kit["shoes"]);
                done = pod.Comp.State == AutodocState.Complete && pod.Comp.AutoSaidNothing;
            });
        }

        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            var tray = SEntMan.System<ItemSlotsSystem>().GetItemOrNull(pod.Owner, AutodocComponent.TraySlotId);
            Out.WriteLine($"PodUndressesWhatItCannotCutTest: voice events: {string.Join(", ", pod.Comp!.VoiceEvents.Select(pair => $"{pair.Key}={pair.Value}"))}");
            Out.WriteLine($"PodUndressesWhatItCannotCutTest: tray holds {(tray is { } held ? SEntMan.ToPrettyString(held) : "nothing")}");

            Assert.Multiple(() =>
            {
                Assert.That(done, Is.True, $"the run never ended (state {pod.Comp.State}, blocked {pod.Comp.BlockedReason}).");
                Assert.That(sawGarment, Is.True, "the WAITING line never named the gloves or the boots.");
                Assert.That(pod.Comp.FailedProcedures, Is.Empty, $"abandoned: {string.Join(", ", pod.Comp.FailedProcedures)}");
                Assert.That(pod.Comp.VoiceEvents.GetValueOrDefault(AutodocVoiceEvent.Stall), Is.Zero, "THIS IS NOT WORKING");

                // Cut: the suit and the jumpsuit, as before.
                Assert.That(SEntMan.Deleted(kit["outerClothing"]), Is.True, "the suit was not cut off.");
                Assert.That(SEntMan.Deleted(kit["jumpsuit"]), Is.True, "the jumpsuit was not cut off.");

                // Taken off whole: the gloves and the boots, one in the tray and one beside the pod.
                foreach (var slot in new[] { "gloves", "shoes" })
                {
                    Assert.That(SEntMan.Deleted(kit[slot]), Is.False, $"the {slot} were destroyed.");
                    Assert.That(inventory.TryGetSlotEntity(patient, slot, out _), Is.False, $"the {slot} are still on.");
                }

                Assert.That(tray == kit["gloves"] || tray == kit["shoes"], Is.True, "neither went into the tray.");
                var floor = tray == kit["gloves"] ? kit["shoes"] : kit["gloves"];
                Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(floor), Is.False,
                    "the second one is not on the floor.");
                Assert.That(OnPodTile(pod, floor), Is.False, "the second one was dropped on the pod rather than beside it.");

                // Nothing the armour check does not read was touched.
                foreach (var slot in new[] { "eyes", "mask", "neck", "ears", "back", "belt", "suitstorage" })
                {
                    Assert.That(inventory.TryGetSlotEntity(patient, slot, out var worn) && worn == kit[slot], Is.True,
                        $"the {slot} item was taken off, and it never blocked anything.");
                }

                Assert.That(OpenWounds(patient), Is.Empty, "a part was left untreated.");
            });
        });

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// Something the pod cannot take off: an unremovable helmet over a hurt head. Shitmed's armour check reads only
    /// the head slot for the head (a mask never blocks it), so the helmet is the case the spec's locked mask stands for.
    /// The pod names it on the WAITING line, says the clothing line once, waits its delay, gives the head procedures up
    /// and finishes the rest.
    /// </summary>
    [Test]
    public async Task PodUndressesWhatItCannotCutLockedTest()
    {
        Out = TestContext.Out;
        var map = await Pair.CreateTestMap();
        var scenario = new WolfmedScenario(SEntMan);
        Entity<AutodocComponent> pod = default;
        EntityUid patient = default, helmet = default;
        string? status = null;

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = 1f);
        await Server.WaitAssertion(() =>
        {
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<Content.Shared.Traits.Assorted.PainNumbnessComponent>(patient);

            foreach (var part in new[] { TargetBodyPart.Head, TargetBodyPart.Torso })
                SEntMan.System<DamageableSystem>().TryChangeDamage(patient, Spec("Slash", 20), origin: null, targetPart: part);

            helmet = SEntMan.SpawnEntity("ClothingHeadHelmetBasic", map.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(patient, helmet, "head", force: true), Is.True);
            SEntMan.EnsureComponent<Content.Shared.Interaction.Components.UnremoveableComponent>(helmet);

            Consciousness().SetExternalPressure(patient, PressureKey, 1f);

            pod = Pod(map.GridCoords);
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                SEntMan.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            SEntMan.System<AutodocSystem>().SetAuto(pod, true);
            Assert.That(SEntMan.System<AutodocSystem>().TryInsert(pod, patient), Is.True);
        });

        var done = false;
        for (var i = 0; i < 120 && !done; i++)
        {
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() =>
            {
                if (pod.Comp!.GarmentStuck && status == null)
                {
                    SEntMan.System<AutodocSystem>().UpdateUi(pod);
                    if (SEntMan.System<UserInterfaceSystem>().TryGetUiState<AutodocBuiState>(pod.Owner, AutodocUiKey.Key, out var state))
                        status = state.Status;
                }

                done = pod.Comp.State == AutodocState.Complete && pod.Comp.AutoSaidNothing;
            });
        }

        await Server.WaitAssertion(() =>
        {
            var said = pod.Comp!.VoiceEvents;
            var clothingLines = said.GetValueOrDefault(AutodocVoiceEvent.Clothing) + said.GetValueOrDefault(AutodocVoiceEvent.ClothingAuto);
            var head = Part(patient, BodyPartType.Head, BodyPartSymmetry.None);
            var torso = Part(patient, BodyPartType.Torso, BodyPartSymmetry.None);
            Out.WriteLine($"PodUndressesWhatItCannotCutLockedTest: status '{status}', failed [{string.Join(", ", pod.Comp.FailedProcedures)}], " +
                          $"voice events: {string.Join(", ", said.Select(pair => $"{pair.Key}={pair.Value}"))}");

            Assert.Multiple(() =>
            {
                Assert.That(done, Is.True, $"the run never ended (state {pod.Comp.State}, blocked {pod.Comp.BlockedReason}).");
                Assert.That(status, Does.Contain(SEntMan.GetComponent<MetaDataComponent>(helmet).EntityName.ToUpperInvariant())
                    .And.Contain("HEAD"), "the WAITING line does not name the helmet and its slot.");
                Assert.That(clothingLines, Is.EqualTo(1), "the clothing line was not said exactly once.");
                Assert.That(said.GetValueOrDefault(AutodocVoiceEvent.Stall), Is.Zero, "THIS IS NOT WORKING");
                Assert.That(pod.Comp.FailedProcedures.Any(failed => failed.Part == TargetBodyPart.Head), Is.True,
                    "the head procedure was never given up.");
                Assert.That(pod.Comp.FailedProcedures.All(failed => failed.Part == TargetBodyPart.Head), Is.True,
                    "something other than the head was given up.");
                Assert.That(SEntMan.Deleted(helmet), Is.False, "the helmet was destroyed.");
                Assert.That(OpenWounds(patient, torso), Is.Empty, "the torso was not treated.");
                Assert.That(OpenWounds(patient, head), Is.Not.Empty, "the fixture's head wound vanished without a procedure.");
            });
        });

        await Server.WaitPost(() => SEntMan.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    private void Dress(EntityUid body, Dictionary<string, EntityUid> kit, Dictionary<string, string> loadout)
    {
        var inventory = SEntMan.System<InventorySystem>();
        var coords = SEntMan.GetComponent<TransformComponent>(body).Coordinates;
        foreach (var (slot, prototype) in loadout)
        {
            var item = SEntMan.SpawnEntity(prototype, coords);
            Assert.That(inventory.TryEquip(body, item, slot, force: true), Is.True, $"could not put {prototype} on {slot}.");
            kit[slot] = item;
        }
    }

    /// <summary>Open wounds the patient walked in with (the pod's own incisions and sutures do not count).</summary>
    private List<string> OpenWounds(EntityUid body, EntityUid? only = null)
    {
        var left = new List<string>();
        foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (only != null && part != only)
                continue;

            foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(part))
            {
                if (!SEntMan.HasComponent<WolfmedPodWoundComponent>(wound) && wound.Comp.State == WoundState.Open)
                    left.Add($"{wound.Comp.Prototype}={wound.Comp.Severity}");
            }
        }

        return left;
    }

    private const string PressureKey = "wolfmed-sam-test";

    /// <summary>The test's own output, captured on the test thread: the server thread belongs to whichever test made the pair.</summary>
    private System.IO.TextWriter Out = System.IO.TextWriter.Null;

    private Content.Server._WF.Wolfmed.Consciousness.WolfmedConsciousnessSystem Consciousness() =>
        SEntMan.System<Content.Server._WF.Wolfmed.Consciousness.WolfmedConsciousnessSystem>();

    private Entity<AutodocComponent> Pod(EntityCoordinates coords, string prototype = "WolfmedSamTestAutodoc")
    {
        var uid = SEntMan.SpawnEntity(prototype, coords);
        return (uid, SEntMan.GetComponent<AutodocComponent>(uid));
    }

    /// <summary>A three by three floor around the test map's one tile, so the pod has sides.</summary>
    private void Floor(Robust.UnitTesting.Pool.TestMapData map)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 2; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
        }
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private bool DragDrop(Entity<AutodocComponent> pod, EntityUid user, EntityUid dragged)
    {
        var check = new CanDropTargetEvent(user, dragged);
        SEntMan.EventBus.RaiseLocalEvent(pod.Owner, ref check);
        if (check.Handled && !check.CanDrop)
            return false;

        var drop = new DragDropTargetEvent(user, dragged);
        SEntMan.EventBus.RaiseLocalEvent(pod.Owner, ref drop);
        return drop.Handled;
    }

    private EntityUid[] Contents(Entity<AutodocComponent> pod) =>
        SEntMan.System<SharedContainerSystem>().GetContainer(pod.Owner, AutodocComponent.BodyContainerId)
            .ContainedEntities.ToArray();

    private Vector2 Position(EntityUid uid) => SEntMan.System<SharedTransformSystem>().GetWorldPosition(uid);

    /// <summary>Within the pod's own tile, which is where the lid is drawn.</summary>
    private bool OnPodTile(Entity<AutodocComponent> pod, EntityUid body)
    {
        var offset = Position(body) - Position(pod.Owner);
        return MathF.Abs(offset.X) < 0.5f && MathF.Abs(offset.Y) < 0.5f;
    }

    private static DamageSpecifier Spec(string type, float amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
