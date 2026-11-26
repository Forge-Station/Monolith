#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "more accessible sutures ... a downgraded one of the medicated ones ... and makeshift variants". Three
/// tiers: the medicated suture on top, the plain suture (half its closing per use, slower, marks the wound sutured and
/// leaves it clean), and the makeshift suture (weaker and slower again, and dirty: what it closes is contaminated and
/// not counted as sutured, so it keeps an infection clock).
/// </summary>
/// <remarks>
/// Every cut is 15 Slash on a left arm: a bleeding SlashWound, under the tendon (16) and artery (22) rule lines, so
/// nothing else is made.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedDirtyTreatmentComponent))]
public sealed class WolfmedSutureTiersTest : GameTest
{
    private const string Medicated = "MedicatedSuture";
    private const string Plain = "WFWolfmedSuture";
    private const string Makeshift = "WFWolfmedMakeshiftSuture";

    /// <summary>One use of each on the same cut: each tier removes less and takes longer than the one above it.</summary>
    [Test]
    public async Task EachTierClosesLessPerUseAndTakesLongerTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            var healing = SEntMan.System<WoundHealingSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            var removed = new FixedPoint2[3];
            var severity = new FixedPoint2[3];
            var delay = new float[3];
            var items = new[] { Medicated, Plain, Makeshift };
            for (var i = 0; i < items.Length; i++)
            {
                var patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var arm = Cut(s, patient, medic);
                var wound = Wound(arm);
                var before = SEntMan.GetComponent<WoundComponent>(wound).Severity;
                var rate = bleeding.GetPartRate(arm);

                var item = SEntMan.SpawnEntity(items[i], map.GridCoords);
                var component = SEntMan.GetComponent<HealingComponent>(item);
                Assert.That(healing.TryApplyHealing(patient, arm, (item, component), medic, out var healed, out _), Is.True,
                    $"{items[i]} did nothing for a cut.");

                removed[i] = -healed.DamageDict.GetValueOrDefault("Slash");
                severity[i] = before - SEntMan.GetComponent<WoundComponent>(wound).Severity;
                delay[i] = component.Delay;
                Assert.That(bleeding.GetPartRate(arm), Is.LessThan(rate), $"{items[i]} did not slow the bleeding.");
            }

            Assert.Multiple(() =>
            {
                Assert.That(removed[0], Is.GreaterThan(removed[1]), "the plain suture closed as much as the medicated one.");
                Assert.That(removed[1], Is.GreaterThan(removed[2]), "the makeshift suture closed as much as the plain one.");
                Assert.That(removed[2], Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(severity[0], Is.GreaterThan(severity[1]));
                Assert.That(severity[1], Is.GreaterThan(severity[2]));
                Assert.That(delay[0], Is.LessThan(delay[1]), "the plain suture is not slower to use.");
                Assert.That(delay[1], Is.LessThan(delay[2]), "the makeshift suture is not slower to use.");
            });
        });
    }

    /// <summary>
    /// Through the do-after, as a player uses them: the plain suture marks the cut sutured and leaves it clean; the
    /// makeshift one leaves it contaminated (the multiplier every infection tick reads) and not sutured.
    /// </summary>
    [Test]
    public async Task PlainSutureIsCleanAndMakeshiftSutureIsDirtyTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid cleanWound = default, dirtyWound = default;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            var plainMedic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var makeshiftMedic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var clean = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var dirty = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            cleanWound = Wound(Cut(s, clean, plainMedic));
            dirtyWound = Wound(Cut(s, dirty, makeshiftMedic));
            Assert.That(Contamination(cleanWound), Is.EqualTo(1f));
            Assert.That(Contamination(dirtyWound), Is.EqualTo(1f));

            Assert.That(Use(plainMedic, clean, Plain, map), Is.True, "the plain suture would not start on a cut.");
            Assert.That(Use(makeshiftMedic, dirty, Makeshift, map), Is.True, "the makeshift suture would not start on a cut.");
        });

        // The plain suture's three seconds, not the makeshift's five.
        await RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedSuturedComponent>(cleanWound), Is.True,
                    "the plain suture did not count as sutured.");
                Assert.That(Contamination(cleanWound), Is.EqualTo(1f), "the plain suture contaminated the cut.");
                Assert.That(Contamination(dirtyWound), Is.EqualTo(1f), "the makeshift suture finished before its five seconds.");
            });
        });

        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(dirtyWound), Is.False, "one makeshift stitch closed the whole cut.");
                Assert.That(Contamination(dirtyWound), Is.GreaterThan(1f), "the makeshift suture left the cut clean.");
                Assert.That(SEntMan.HasComponent<WolfmedSuturedComponent>(dirtyWound), Is.False,
                    "a dirty stitch counted as a sutured wound, which would stop the infection it causes.");
            });
        });
    }

    /// <summary>15 Slash on the patient's left arm; returns the arm.</summary>
    private EntityUid Cut(WolfmedScenario s, EntityUid patient, EntityUid attacker)
    {
        var arm = s.Part(patient, BodyPartType.Arm, BodyPartSymmetry.Left);
        SEntMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(patient, arm, WolfmedScenario.Spec("Slash", 15), attacker);
        Assert.That(SEntMan.System<WoundBleedingSystem>().GetPartRate(arm), Is.GreaterThan(0f), "a 15 Slash did not bleed.");
        return arm;
    }

    private EntityUid Wound(EntityUid arm) =>
        SEntMan.System<WoundSystem>().GetWounds(arm)
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("SlashWound")).Owner;

    private float Contamination(EntityUid wound) => SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Contamination;

    /// <summary>Puts the item in the medic's hand, aims at the left arm and clicks the patient with it.</summary>
    private bool Use(EntityUid medic, EntityUid patient, string id, TestMapData map)
    {
        var item = SEntMan.SpawnEntity(id, map.GridCoords);
        Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(medic, item), Is.True);
        SEntMan.GetComponent<TargetingComponent>(medic).Target = TargetBodyPart.LeftArm;
        var click = new AfterInteractEvent(medic, item, patient, map.GridCoords, canReach: true);
        SEntMan.EventBus.RaiseLocalEvent(item, click);
        return click.Handled;
    }
}
