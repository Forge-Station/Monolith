#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// INFECTION: infection lives on the parts and travels towards the torso (hand to arm to torso, head to torso) with no
/// wound on the receiving part; only an infected torso or head starts sepsis; septic shock is sepsis's named late
/// stage. The infection tick is driven 5 s at a time, the real batch, so the timings are the ones a player gets.
/// </summary>
/// <remarks>
/// Shipped profile (<c>_WF/Wolfmed/Wounds/infection.yml</c>): an untreated risk-1 cut spreads at 60 (6 a minute, 10
/// minutes) and feeds its part 10 a minute, so the part spreads 6 minutes later (about 16); each hop to the parent is
/// 60 / 8 = 7.5 minutes.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedInfectionSystem))]
public sealed class WolfmedInfectionSpreadTest : GameTest
{
    private const float Tick = 5f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionEnabled, true);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionRate, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.SepsisEnabled, true);
        await OverrideCVar(Side.Server, WolfmedCVars.SepticShockAt, 80f);
    }

    /// <summary>
    /// <c>HandInfectionTravelsToTheTorsoTest</c>: an untreated cut on the left hand infects the hand, then the arm, then
    /// the torso, each only once it is septic (playtest 5: 25 minutes from the hand's spreading to the arm's, 33 from
    /// the arm's to the torso's), and sepsis starts on the tick the torso spreads, never before. The arm
    /// and torso carry no wound of their own; the analyzer shows the arm's tissue infection on the arm's card.
    /// </summary>
    [Test]
    public async Task HandInfectionTravelsToTheTorsoTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var log = string.Empty;

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var hand = Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torso = Part(body, BodyPartType.Torso);
            Cut(hand);

            float? handAt = null, armAt = null, torsoAt = null, sepsisAt = null;
            var sepsisEarly = false;
            var feverEarly = false;
            var cardChecked = false;
            for (var t = Tick; t <= 120 * 60f && sepsisAt == null; t += Tick)
            {
                infection.Update(Tick);
                var minute = t / 60f;
                handAt ??= Spreading(infection, hand) ? minute : (float?) null;
                armAt ??= Spreading(infection, arm) ? minute : (float?) null;
                torsoAt ??= Spreading(infection, torso) ? minute : (float?) null;
                if (SEntMan.HasComponent<WolfmedSepsisComponent>(body))
                {
                    sepsisAt = minute;
                    sepsisEarly |= torsoAt == null;
                }

                // Playtest 5: nothing the body feels, the fever included, starts before the chest.
                feverEarly |= torsoAt == null && infection.HasFever(body);

                // The analyzer's card for an arm that is spreading with no wound on it.
                if (armAt != null && torsoAt == null && !cardChecked)
                {
                    cardChecked = true;
                    var diagnostics = SEntMan.System<Content.Server.Medical.HealthAnalyzerSystem>().BuildWoundDiagnostics(body);
                    Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.LeftArm, out var card), Is.True,
                        "an infected arm with no wound has no card on the analyzer.");
                    Assert.That(card.PartInfection, Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading));
                    Assert.That(card.Infection, Is.EqualTo(WolfmedInfectionStage.None), "the arm grew an infected wound.");
                }
            }

            log = ($"HandInfectionTravelsToTheTorsoTest: hand spreading at {handAt:0.00} min, " +
                $"arm {armAt:0.00}, torso {torsoAt:0.00}, sepsis {sepsisAt:0.00}.");
            Assert.Multiple(() =>
            {
                Assert.That(handAt, Is.InRange(26f, 29f), "the hand did not spread about twelve minutes after its cut spread.");
                Assert.That(armAt - handAt, Is.InRange(24f, 26.5f), "the arm did not follow the hand once the hand was septic.");
                Assert.That(torsoAt - armAt, Is.InRange(32f, 35f), "the torso did not follow the arm once the arm was septic.");
                Assert.That(sepsisAt, Is.EqualTo(torsoAt), "sepsis did not start on the tick the torso spread.");
                Assert.That(sepsisEarly, Is.False, "sepsis started while only the hand and arm were infected.");
                Assert.That(feverEarly, Is.False, "a fever ran while only the hand and arm were infected.");
                Assert.That(infection.HasFever(body), Is.True, "no fever once the infection reached the torso.");
                Assert.That(cardChecked, Is.True, "the torso spread on the same tick as the arm.");
                Assert.That(Wounds(arm).Concat(Wounds(torso)).Any(wound => SEntMan.HasComponent<WolfmedInfectionComponent>(wound)),
                    Is.False, "the arm or torso carried an infectable wound.");
            });
        });

        TestContext.Out.WriteLine(log);
    }

    /// <summary>
    /// <c>AmputationStopsTheTravelTest</c>: the hand comes off while the arm is infected but under the spreading line.
    /// The hand takes its infection with it, the arm recovers to nothing at 4 a minute, and the torso and blood are
    /// never touched.
    /// </summary>
    [Test]
    public async Task AmputationStopsTheTravelTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var log = string.Empty;

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var profile = infection.Profile;
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var hand = Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torso = Part(body, BodyPartType.Torso);
            Cut(hand);

            // About 44 minutes (playtest 5): the hand septic at 32, the arm past local and still under spreading.
            for (var t = Tick; t <= 60 * 60f && infection.GetPartProgress(arm) < 35f; t += Tick)
                infection.Update(Tick);

            var armProgress = infection.GetPartProgress(arm);
            Assert.That(armProgress, Is.InRange(35f, profile.PartSpreadingAt - 1f), "the arm never got part-way.");
            Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(hand), Is.True);
            // The stump is closed, as a medic would; an open one is an infection risk of its own (2.5).
            infection.MarkSutured(arm, null);

            var clearedAt = -1f;
            for (var t = Tick; t <= 20 * 60f && clearedAt < 0f; t += Tick)
            {
                infection.Update(Tick);
                Assert.That(infection.GetPartProgress(torso), Is.Zero, "the torso caught the arm's infection.");
                if (!SEntMan.HasComponent<WolfmedPartInfectionComponent>(arm))
                    clearedAt = t / 60f;
            }

            log = ($"AmputationStopsTheTravelTest: arm at {armProgress:0.0} when the hand came off, " +
                $"clear {clearedAt:0.00} min later.");
            Assert.Multiple(() =>
            {
                Assert.That(clearedAt, Is.InRange(armProgress / profile.PartRecoveryPerMinute - 1f,
                    armProgress / profile.PartRecoveryPerMinute + 1f), "the arm did not recover at 4 a minute.");
                Assert.That(infection.GetPartStage(hand), Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading),
                    "the severed hand left its infection behind.");
                Assert.That(SEntMan.HasComponent<WolfmedSepsisComponent>(body), Is.False);
            });
        });

        TestContext.Out.WriteLine(log);
    }

    /// <summary>
    /// <c>HeadInfectionStartsSepsisTest</c>: a cut on the head infects the head, and the head is a core part, so sepsis
    /// starts when it spreads (about 16 minutes) with no limb involved.
    /// </summary>
    [Test]
    public async Task HeadInfectionStartsSepsisTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var log = string.Empty;

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var head = Part(body, BodyPartType.Head);
            Cut(head);

            var sepsisAt = -1f;
            for (var t = Tick; t <= 45 * 60f && sepsisAt < 0f; t += Tick)
            {
                infection.Update(Tick);
                if (SEntMan.HasComponent<WolfmedSepsisComponent>(body))
                    sepsisAt = t / 60f;
            }

            var limbs = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .Where(part => part.Component.PartType is not (BodyPartType.Head or BodyPartType.Torso))
                .Select(part => part.Id)
                .ToList();
            log = ($"HeadInfectionStartsSepsisTest: sepsis at {sepsisAt:0.00} min.");
            Assert.Multiple(() =>
            {
                Assert.That(sepsisAt, Is.InRange(26f, 29f), "a spreading head did not start sepsis.");
                Assert.That(infection.GetPartStage(head), Is.GreaterThanOrEqualTo(WolfmedInfectionStage.Spreading));
                Assert.That(limbs, Is.Not.Empty);
                Assert.That(limbs.Any(limb => SEntMan.HasComponent<WolfmedPartInfectionComponent>(limb)), Is.False,
                    "a limb caught the head's infection.");
            });
        });

        TestContext.Out.WriteLine(log);
    }

    /// <summary>
    /// <c>AntibioticsClearThePartsTest</c>: once a hand infection has reached the torso and started sepsis, 13 units of
    /// antibiotic (8 a unit off every part) clear every part, the wound with them, and pull the sepsis back.
    /// Antiseptic does nothing to a part.
    /// </summary>
    [Test]
    public async Task AntibioticsClearThePartsTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var hand = Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            var torso = Part(body, BodyPartType.Torso);
            var wound = Cut(hand);

            for (var t = Tick; t <= 120 * 60f && !SEntMan.HasComponent<WolfmedSepsisComponent>(body); t += Tick)
                infection.Update(Tick);

            Assert.That(SEntMan.HasComponent<WolfmedSepsisComponent>(body), Is.True, "the hand never reached sepsis.");
            // Review: three more minutes (playtest 5: 2 a minute) so the sepsis is well off zero and the dose has
            // something to pull back.
            for (var t = Tick; t <= 180f; t += Tick)
                infection.Update(Tick);
            var sepsisBefore = infection.GetSepsis(body);
            Assert.That(sepsisBefore, Is.GreaterThan(5f), "sepsis barely started in three minutes.");
            var torsoBefore = infection.GetPartProgress(torso);
            infection.Clean(body);
            Assert.That(infection.GetPartProgress(torso), Is.EqualTo(torsoBefore), "antiseptic reached a part.");

            Assert.That(infection.Treat(body, 13f), Is.True);
            var infected = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .Where(part => SEntMan.HasComponent<WolfmedPartInfectionComponent>(part.Id))
                .Select(part => part.Component.PartType.ToString())
                .ToList();
            Assert.Multiple(() =>
            {
                Assert.That(infected, Is.Empty, "13 units left a part infected.");
                Assert.That(infection.GetStage(wound), Is.EqualTo(WolfmedInfectionStage.None));
                Assert.That(infection.GetSepsis(body), Is.LessThan(sepsisBefore), "the dose did not pull the sepsis back.");
            });
        });
    }

    /// <summary>
    /// Playtest 5, "an infection has to have an actual reason to start, like being exposed to the air outside a suit":
    /// a clean cut under a sealed hardsuit holds at zero; a bite goes bad under it; a dirty tool in the cut is a
    /// reason; and with the suit off a fresh cut goes bad on its own. And "minor injuries shouldn't cause infections":
    /// with the suit off a minor cut and a minor burn hold at zero, while a minor bite still goes bad.
    /// </summary>
    [Test]
    public async Task InfectionNeedsAReasonTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var inventory = SEntMan.System<InventorySystem>();
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var suit = SEntMan.SpawnEntity("ClothingOuterHardsuitEngineering", map.GridCoords);
            Assert.That(inventory.TryEquip(body, suit, "outerClothing", force: true), Is.True, "the fixture could not suit the patient.");

            var arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var cut = Cut(arm);
            Assert.That(infection.IsExposed(arm), Is.False, "an arm under a hardsuit reads as exposed.");
            for (var t = Tick; t <= 10 * 60f; t += Tick)
                infection.Update(Tick);
            Assert.That(Progress(cut), Is.Zero, "a clean cut under a sealed suit went bad.");

            // A bite is dirty by nature.
            var bite = wounds.CreateOrMergeWound(arm, "WFWolfmedAvulsionWound", FixedPoint2.New(20));
            Assert.That(bite, Is.Not.Null);
            for (var t = Tick; t <= 5 * 60f; t += Tick)
                infection.Update(Tick);
            Assert.That(Progress(bite!.Value), Is.GreaterThan(0f), "a bite under a suit did not go bad.");
            Assert.That(Progress(cut), Is.Zero, "the clean cut caught the bite's reason.");

            // A dirty tool in the cut is a reason too.
            infection.Contaminate(cut);
            for (var t = Tick; t <= 5 * 60f; t += Tick)
                infection.Update(Tick);
            Assert.That(Progress(cut), Is.GreaterThan(0f), "a contaminated cut under a suit did not go bad.");

            // Suit off: a fresh cut on the other arm is open to the air.
            Assert.That(inventory.TryUnequip(body, "outerClothing", force: true), Is.True);
            var other = Part(body, BodyPartType.Arm, BodyPartSymmetry.Right);
            var open = Cut(other);
            Assert.That(infection.IsExposed(other), Is.True);
            for (var t = Tick; t <= 5 * 60f; t += Tick)
                infection.Update(Tick);
            Assert.That(Progress(open), Is.GreaterThan(0f), "an exposed cut did not go bad.");

            // Minor wounds, open to the air on the legs: the cut and the burn hold; the bite is dirty and does not.
            var leftLeg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var rightLeg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Right);
            var scratch = wounds.CreateOrMergeWound(leftLeg, "SlashWound", FixedPoint2.New(10));
            var scald = wounds.CreateOrMergeWound(rightLeg, "BurnWound", FixedPoint2.New(10));
            var nip = wounds.CreateOrMergeWound(rightLeg, "WFWolfmedAvulsionWound", FixedPoint2.New(10));
            Assert.That(scratch, Is.Not.Null);
            Assert.That(scald, Is.Not.Null);
            Assert.That(nip, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(infection.IsMinor(SEntMan.GetComponent<WoundComponent>(scratch!.Value)), Is.True);
                Assert.That(infection.IsMinor(SEntMan.GetComponent<WoundComponent>(open)), Is.False, "a 30 cut reads as minor.");
            });
            for (var t = Tick; t <= 30 * 60f; t += Tick)
                infection.Update(Tick);
            Assert.Multiple(() =>
            {
                Assert.That(Progress(scratch!.Value), Is.Zero, "a minor cut in the open went bad.");
                Assert.That(Progress(scald!.Value), Is.Zero, "a minor burn in the open went bad.");
                Assert.That(Progress(nip!.Value), Is.GreaterThan(0f), "a minor bite did not go bad.");
            });
        });
    }

    private float Progress(EntityUid wound) => SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Progress;

    /// <summary>
    /// <c>SepticShockTest</c>: <see cref="WolfmedInfectionSystem.InSepticShock"/> and the alert's severity flip at
    /// wolfmed.septic_shock_at; the analyzer's banner flag and "Do first" line and the examine text follow.
    /// </summary>
    [Test]
    public async Task SepticShockTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, medic = default;

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var analyzer = SEntMan.System<Content.Server.Medical.HealthAnalyzerSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var sepsis = SEntMan.EnsureComponent<WolfmedSepsisComponent>(body);

            // No source, so it falls 8 a minute: under half a point a tick.
            sepsis.Progress = 79.99f;
            Assert.That(infection.InSepticShock(body), Is.False, "79.99 is shock.");
            sepsis.Progress = 80f;
            Assert.That(infection.InSepticShock(body), Is.True, "80 is not shock.");

            sepsis.Progress = 70f;
            infection.Update(Tick);
            Assert.Multiple(() =>
            {
                Assert.That(infection.InSepticShock(body), Is.False);
                Assert.That(sepsis.Shock, Is.False);
                Assert.That(Severity(body), Is.EqualTo((short) 0), "sepsis under the line shows as shock.");
                Assert.That(Notes(body, medic), Does.Contain("flushed and sweating"));
            });

            sepsis.Progress = 88f;
            infection.Update(Tick);
            var report = analyzer.BuildVitals(body);
            Assert.Multiple(() =>
            {
                Assert.That(infection.InSepticShock(body), Is.True);
                Assert.That(sepsis.Shock, Is.True);
                Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State, Is.EqualTo(WolfmedConsciousness.Unconscious),
                    "septic shock did not put the patient out (playtest 5).");
                Assert.That(Severity(body), Is.EqualTo((short) 1), "septic shock is not severity 1.");
                Assert.That(report, Is.Not.Null);
                Assert.That(report!.SepticShock, Is.True, "the analyzer does not know it is shock.");
                Assert.That(WolfmedVitalsText.DoFirstLine(report),
                    Does.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Sepsis, false, shock: true)));
                Assert.That(Notes(body, medic), Does.Contain("grey and clammy").And.Not.Contain("flushed"));
            });
        });

        // The line is the CVar: raised past the patient, the same sepsis is no longer shock.
        await OverrideCVar(Side.Server, WolfmedCVars.SepticShockAt, 95f);
        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            infection.Update(Tick);
            Assert.Multiple(() =>
            {
                Assert.That(infection.InSepticShock(body), Is.False);
                Assert.That(SEntMan.GetComponent<WolfmedSepsisComponent>(body).Shock, Is.False);
                Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious),
                    "the patient stayed out once the line moved past them.");
                Assert.That(Severity(body), Is.EqualTo((short) 0));
            });
        });
    }

    /// <summary>
    /// <c>MachineNeverCarriesAPartInfectionTest</c>: a chassis part never gains a part infection, not even under a
    /// wound forced to septic and a request to kill the limb, so an IPC never goes septic.
    /// </summary>
    [Test]
    public async Task MachineNeverCarriesAPartInfectionTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var machine = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var hand = Part(machine, BodyPartType.Hand, BodyPartSymmetry.Left);
            var torso = Part(machine, BodyPartType.Torso);
            SEntMan.System<DamageableSystem>().TryChangeDamage(machine, Spec("Blunt", 20), ignoreResistances: true,
                targetPart: TargetBodyPart.LeftHand);
            SEntMan.System<DamageableSystem>().TryChangeDamage(machine, Spec("Blunt", 20), ignoreResistances: true,
                targetPart: TargetBodyPart.Torso);

            var forced = Wounds(hand).Concat(Wounds(torso)).ToList();
            Assert.That(forced, Is.Not.Empty, "the chassis took no wound.");
            foreach (var wound in forced)
            {
                var septic = SEntMan.EnsureComponent<WolfmedInfectionComponent>(wound);
                septic.Progress = infection.Profile.SepsisAt;
                septic.Stage = WolfmedInfectionStage.Septic;
            }

            Assert.That(SEntMan.System<WolfmedNecrosisSystem>().MakeNecrotic(hand), Is.Null);
            for (var t = Tick; t <= 20 * 60f; t += Tick)
                infection.Update(Tick);

            var infected = SEntMan.System<SharedBodySystem>().GetBodyChildren(machine)
                .Where(part => SEntMan.HasComponent<WolfmedPartInfectionComponent>(part.Id))
                .Select(part => part.Component.PartType.ToString())
                .ToList();
            Assert.Multiple(() =>
            {
                Assert.That(infected, Is.Empty, "a chassis part carries an infection.");
                Assert.That(SEntMan.HasComponent<WolfmedSepsisComponent>(machine), Is.False, "the chassis went septic.");
            });
        });
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;

    /// <summary>An untreated risk-1 cut, made directly so the part it lands on is certain.</summary>
    private EntityUid Cut(EntityUid part)
    {
        // Moderate: a minor cut in the open never goes bad (playtest 5).
        var wound = SEntMan.System<WoundSystem>().CreateOrMergeWound(part, "SlashWound", FixedPoint2.New(30));
        Assert.That(wound, Is.Not.Null, "the part took no cut.");
        Assert.That(SEntMan.HasComponent<WolfmedInfectionComponent>(wound!.Value), Is.True, "the cut is not infectable.");
        return wound.Value;
    }

    private List<EntityUid> Wounds(EntityUid part) =>
        SEntMan.System<WoundSystem>().GetWounds((part, SEntMan.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Owner)
            .ToList();

    private static bool Spreading(WolfmedInfectionSystem infection, EntityUid part) =>
        infection.GetPartStage(part) >= WolfmedInfectionStage.Spreading;

    private short? Severity(EntityUid body) =>
        SEntMan.System<AlertsSystem>().TryGetAlertState(body, new AlertKey(WolfmedInfectionSystem.SepsisAlert, null),
            out var state)
            ? state.Severity
            : null;

    private string Notes(EntityUid examined, EntityUid examiner)
    {
        var report = SEntMan.System<WolfmedVisualInspectionSystem>().GetLook(examined, examiner, true);
        Assert.That(report, Is.Not.Null, "the look profile is missing.");
        return string.Join("\n", report!.Notes.Select(note => FormattedMessage.RemoveMarkupPermissive(note)));
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
