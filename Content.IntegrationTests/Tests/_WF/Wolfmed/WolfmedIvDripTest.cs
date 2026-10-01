#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Medical;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared.Atmos;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 4, IV: the IV drip. A hung Bloodpack stack refills a bled patient with their own blood at the flow rate;
/// take mode fills a beaker with the patient's blood and pings when it is full; walking off rips the needle out of an
/// arm, while a patient put in a pod is detached cleanly.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedIvDripSystem))]
public sealed class WolfmedIvDripTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Drip = "WFWolfmedIvDrip";

    /// <summary>Patients whose own blood regeneration is off, so every unit that moves is the drip's.</summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedIvTestHuman
  parent: MobHuman
  components:
  - type: Bloodstream
    bloodRefreshAmount: 0

- type: entity
  id: WolfmedIvTestSlime
  parent: MobSlimePerson
  components:
  - type: Bloodstream
    bloodRefreshAmount: 0
";

    private WolfmedIvDripSystem Iv => SEntMan.System<WolfmedIvDripSystem>();

    private BloodstreamSystem Bloodstream => SEntMan.System<BloodstreamSystem>();

    /// <summary>
    /// A drip on a fresh grid in station air with <paramref name="mob"/> one tile east of it and
    /// <paramref name="hung"/> hanging on it (a Bloodpack stack cut to <paramref name="packs"/>). The grid is kept from
    /// Mono's cleanup. Vacuum would wound the patient and bleed them, and the tests count every unit.
    /// </summary>
    private async Task<(TestMapData Map, Entity<WolfmedIvDripComponent> Drip, EntityUid Patient, EntityUid Hung)> Setup(
        string mob, string hung, int packs = 0, float blood = 1f)
    {
        var map = await Pair.CreateTestMap();
        Entity<WolfmedIvDripComponent> drip = default;
        EntityUid patient = default;
        EntityUid item = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, StationAir());
            var uid = SEntMan.SpawnEntity(Drip, map.GridCoords);
            drip = (uid, SEntMan.GetComponent<WolfmedIvDripComponent>(uid));
            patient = SEntMan.SpawnEntity(mob, map.GridCoords.Offset(new Vector2(1f, 0f)));

            item = SEntMan.SpawnEntity(hung, map.GridCoords);
            if (packs > 0)
                SEntMan.System<SharedStackSystem>().SetCount(item, packs);

            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(uid, WolfmedIvDripComponent.ContainerId, out var slot), Is.True);
            Assert.That(Iv.Fits(drip, item), Is.True, $"{hung} does not fit the drip.");
            Assert.That(containers.Insert(item, slot!), Is.True);

            if (blood < 1f)
            {
                FixedPoint2 pool = SEntMan.GetComponent<BloodstreamComponent>(patient).BloodMaxVolume;
                Bloodstream.TryModifyBloodLevel(patient, FixedPoint2.New(-(1f - blood) * pool.Float()));
            }
        });

        return (map, drip, patient, item);
    }

    /// <summary>Station air's moles at room temperature.</summary>
    public static GasMixture StationAir()
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        return new GasMixture(moles, Atmospherics.T20C);
    }

    private FixedPoint2 BloodVolume(EntityUid body) =>
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(body,
            SEntMan.GetComponent<BloodstreamComponent>(body).BloodSolutionName, out _, out var solution)
            ? solution.Volume
            : FixedPoint2.Zero;

    private Solution BloodSolution(EntityUid body)
    {
        Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(body,
            SEntMan.GetComponent<BloodstreamComponent>(body).BloodSolutionName, out _, out var solution), Is.True);
        return solution!;
    }

    /// <summary>Runs a second at a time until the condition holds; the seconds it took, or null past the limit.</summary>
    private async Task<int?> SecondsUntil(int limit, Func<bool> condition)
    {
        for (var second = 1; second <= limit; second++)
        {
            await RunSeconds(1);
            var met = false;
            await Server.WaitPost(() => met = condition());
            if (met)
                return second;
        }

        return null;
    }

    /// <summary>
    /// (1) A human bled to 0.6 with a Bloodpack stack on the drip reaches 0.9 in the time the default flow predicts
    /// (90 u at 5 u/s, 18 s), as their own Blood, and the stack is short exactly the packs that blood was worth.
    /// </summary>
    [Test]
    public async Task PackRefillsABledPatientAtTheFlowRateTest()
    {
        var (_, drip, patient, pack) = await Setup("WolfmedIvTestHuman", "Bloodpack", packs: 5, blood: 0.6f);
        float start = 0f, rate = 0f, perPack = 0f;

        await Server.WaitAssertion(() =>
        {
            start = BloodVolume(patient).Float();
            rate = drip.Comp.Rate;
            perPack = Iv.UnitsPerPack;
            Assert.That(drip.Comp.Mode, Is.EqualTo(WolfmedIvMode.Inject));
            Assert.That(Bloodstream.GetBloodLevelPercentage(patient), Is.EqualTo(0.6f).Within(0.005f));
            Iv.Attach(drip, patient);
        });

        FixedPoint2 pool = default;
        await Server.WaitPost(() => pool = SEntMan.GetComponent<BloodstreamComponent>(patient).BloodMaxVolume);
        var predicted = (0.9f - 0.6f) * pool.Float() / rate;
        var took = await SecondsUntil(60, () => Bloodstream.GetBloodLevelPercentage(patient) >= 0.9f - 0.001f);
        TestContext.Out.WriteLine($"0.6 to 0.9 at {rate} u/s: {took} s (predicted {predicted:F1} s).");

        Assert.That(took, Is.Not.Null, "the drip never brought the patient to 0.9.");
        Assert.That(took!.Value, Is.InRange(predicted - 1f, predicted + 3f), "the refill did not follow the flow rate.");

        await Server.WaitAssertion(() =>
        {
            var given = BloodVolume(patient).Float() - start;
            var spent = (5 - SEntMan.GetComponent<StackComponent>(pack).Count) * perPack - drip.Comp.PackOpened;
            Assert.That(spent, Is.EqualTo(given).Within(0.1f), "the packs spent do not match the blood given.");
            Assert.That(BloodSolution(patient).Contents.All(reagent => reagent.Reagent.Prototype == "Blood"), Is.True,
                "the human was given something other than Blood.");
            Assert.That(drip.Comp.Patient, Is.EqualTo(patient), "the needle came out on its own.");

            // The examine lines resolve, the plural pack line included.
            var examine = SEntMan.System<ExamineSystemShared>().GetExamineText(drip, patient).ToString();
            Assert.That(examine, Does.Contain("injecting at 5").And.Contain("blood packs").And.Contain("is connected"),
                examine);
        });
    }

    /// <summary>
    /// (2) Take mode: an empty beaker on a full human fills with their blood at the flow rate, the drip pings once and
    /// stops the flow, and the patient is down by exactly what the beaker holds.
    /// </summary>
    [Test]
    public async Task TakeModeFillsABeakerAndPingsTest()
    {
        var (_, drip, patient, beaker) = await Setup("WolfmedIvTestHuman", "Beaker");
        FixedPoint2 start = default;
        FixedPoint2 capacity = default;

        await Server.WaitAssertion(() =>
        {
            start = BloodVolume(patient);
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetFitsInDispenser(beaker, out _, out var empty),
                Is.True);
            Assert.That(empty!.Volume, Is.EqualTo(FixedPoint2.Zero), "the beaker is not empty.");
            capacity = empty.MaxVolume;

            Iv.SetMode(drip, WolfmedIvMode.Take);
            Iv.Attach(drip, patient);
        });

        var rate = drip.Comp.Rate;
        var predicted = capacity.Float() / rate;
        var took = await SecondsUntil(60, () => drip.Comp.Pings > 0);
        TestContext.Out.WriteLine($"{capacity} u beaker at {rate} u/s pinged after {took} s (full at {predicted:F1} s).");
        Assert.That(took, Is.Not.Null, "the drip never pinged.");
        Assert.That(took!.Value, Is.InRange(predicted - 1f, predicted + 3f));

        // Stopped, not pinging again.
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetFitsInDispenser(beaker, out _, out var full),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(full!.Volume, Is.EqualTo(capacity), "the beaker is not full.");
                Assert.That(full.Contents.All(reagent => reagent.Reagent.Prototype == "Blood"), Is.True,
                    "something other than the patient's blood went into the beaker.");
                Assert.That(start - BloodVolume(patient), Is.EqualTo(full.Volume),
                    "the patient did not lose what the beaker gained.");
                Assert.That(drip.Comp.Pings, Is.EqualTo(1), "the drip pinged more than once.");
                Assert.That(drip.Comp.Rate, Is.EqualTo(0f), "the flow did not stop at the ping.");
            });
        });
    }

    /// <summary>
    /// (3) The patient moved two tiles off rips the needle out: detached, and a new puncture on an arm. Put in a pod
    /// instead, the patient is detached with nothing torn.
    /// </summary>
    [Test]
    public async Task WalkingAwayRipsTheNeedleOutTest()
    {
        var (map, drip, patient, _) = await Setup("WolfmedIvTestHuman", "Bloodpack", packs: 5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ArmPunctures(patient), Is.Zero, "the fixture already has a puncture on an arm.");
            Iv.Attach(drip, patient);
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(drip.Comp.Patient, Is.EqualTo(patient), "the needle came out of a patient who stayed put.");
            SEntMan.System<SharedTransformSystem>().SetCoordinates(patient, map.GridCoords.Offset(new Vector2(2f, 0f)));
        });

        await RunTicksSync(5);

        var rip = string.Empty;
        await Server.WaitAssertion(() =>
        {
            Assert.That(drip.Comp.Patient, Is.Null, "the needle stayed in a patient two tiles away.");
            Assert.That(ArmPunctures(patient), Is.EqualTo(1), "ripping the needle out opened no puncture on an arm.");

            var wound = ArmWounds(patient).First(wound => wound.Comp.Prototype == "PiercingWound");
            var bleed = SEntMan.TryGetComponent<WoundBleedingComponent>(wound, out var bleeding) ? bleeding.CurrentRate : 0f;
            rip = $"rip: PiercingWound severity {wound.Comp.Severity}, bleed rate {bleed}.";
            Assert.That(bleed, Is.GreaterThan(0f), "the torn puncture does not bleed.");
        });
        TestContext.Out.WriteLine(rip);

        // Back beside the stand, then into a pod: a clean detach, nothing new torn.
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedTransformSystem>().SetCoordinates(patient, map.GridCoords.Offset(new Vector2(1f, 0f)));
            Iv.Attach(drip, patient);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(drip.Comp.Patient, Is.EqualTo(patient));
            var pod = SEntMan.SpawnEntity("WFMachineAutodoc", map.GridCoords.Offset(new Vector2(1f, 1f)));
            Assert.That(SEntMan.System<AutodocSystem>().TryInsert((pod, SEntMan.GetComponent<AutodocComponent>(pod)), patient),
                Is.True);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(drip.Comp.Patient, Is.Null, "the needle stayed in a patient lying in a pod.");
            Assert.That(ArmPunctures(patient), Is.EqualTo(1), "going into the pod tore the needle out.");
        });
    }

    /// <summary>
    /// (4) One blood type: a pack given to a Slime person restores Slime, their own blood reagent, and no human Blood.
    /// </summary>
    [Test]
    public async Task PackGivesASlimePersonSlimeTest()
    {
        var (_, drip, patient, _) = await Setup("WolfmedIvTestSlime", "Bloodpack", packs: 5, blood: 0.6f);
        FixedPoint2 slimeBefore = default;

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(patient).BloodReagent.Id, Is.EqualTo("Slime"),
                "the fixture is not Slime-blooded.");
            slimeBefore = BloodSolution(patient).GetTotalPrototypeQuantity("Slime");
            Iv.Attach(drip, patient);
        });

        await RunSeconds(8);

        var note = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var solution = BloodSolution(patient);
            var slime = solution.GetTotalPrototypeQuantity("Slime");
            note = $"Slime {slimeBefore} -> {slime}; Blood {solution.GetTotalPrototypeQuantity("Blood")}.";
            Assert.Multiple(() =>
            {
                Assert.That(slime - slimeBefore, Is.GreaterThan(FixedPoint2.New(20)), "the pack restored no Slime.");
                Assert.That(solution.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.Zero),
                    "the pack put human Blood into a Slime person.");
            });
        });
        TestContext.Out.WriteLine(note);
    }

    private System.Collections.Generic.IEnumerable<Entity<WoundComponent>> ArmWounds(EntityUid body)
    {
        var wounds = SEntMan.System<WoundSystem>();
        foreach (var (arm, _) in SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Arm))
        {
            foreach (var wound in wounds.GetWounds(arm))
                yield return wound;
        }
    }

    private int ArmPunctures(EntityUid body) =>
        ArmWounds(body).Count(wound => wound.Comp.Prototype == "PiercingWound");
}
