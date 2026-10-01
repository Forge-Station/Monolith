#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Construction;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "a makeshift tourniquet that can be made out of a jumpsuit". Torn from any jumpsuit by hand, it clamps
/// a limb like the real one, takes six times as long to tie, and one hit of 15 or more on the strapped limb knocks it
/// loose, where the real one holds.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedTourniquetSlipSystem))]
public sealed class WolfmedMakeshiftTourniquetTest : GameTest
{
    private const string Makeshift = "WFWolfmedMakeshiftTourniquet";

    /// <summary>The construction recipe tears the held jumpsuit into the strap, and leaves the worn one alone.</summary>
    [Test]
    public async Task JumpsuitTearsIntoAMakeshiftTourniquetTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid user = default, held = default, worn = default;

        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            worn = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            held = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(user, worn, "jumpsuit", silent: true, force: true), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, held), Is.True);
        });
        await RunSeconds(1);

#pragma warning disable CS4014 // Legacy construction code awaits its own do-after.
        await Server.WaitPost(() => SEntMan.System<ConstructionSystem>().TryStartItemConstruction(Makeshift, user));
#pragma warning restore CS4014
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Straps(), Is.Empty, "the strap was torn before its three seconds were up.");
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Straps(), Has.Length.EqualTo(1), "tearing a jumpsuit made no makeshift tourniquet.");
                Assert.That(SEntMan.Deleted(held), Is.True, "the held jumpsuit was not used up.");
                Assert.That(SEntMan.Deleted(worn), Is.False, "the worn jumpsuit was torn instead of the held one.");
            });
        });
    }

    /// <summary>
    /// Both straps stop a leg bleeding. The makeshift one is not on after one second (the real one is done in half
    /// of one) and is after four; a 10-damage hit leaves it on; a 20 Slash knocks it loose and the leg bleeds again,
    /// while the real tourniquet on the other leg holds through the same hit.
    /// </summary>
    [Test]
    public async Task MakeshiftTourniquetStopsTheBleedButSlipsOnAHardHitTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid patient = default, medic = default, attacker = default, strap = default;
        EntityUid right = default, left = default;
        var routing = SEntMan.System<WoundDamageRoutingSystem>();
        var bleeding = SEntMan.System<WoundBleedingSystem>();

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            right = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Right);
            left = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Left);
            routing.TryApplyPartDamage(patient, right, WolfmedScenario.Spec("Slash", 40), attacker);
            routing.TryApplyPartDamage(patient, left, WolfmedScenario.Spec("Slash", 40), attacker);
            Assert.That(bleeding.GetPartRate(right), Is.GreaterThan(0f));
            Assert.That(bleeding.GetPartRate(left), Is.GreaterThan(0f));

            // The real tourniquet on the left leg.
            Assert.That(Apply(medic, patient, "Tourniquet", TargetBodyPart.LeftLeg, map), Is.True);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(bleeding.GetPartRate(left), Is.EqualTo(0f), "the real tourniquet did not stop the left leg.");
            strap = SpawnInHand(medic, Makeshift, map);
            Assert.That(Apply(medic, patient, strap, TargetBodyPart.RightLeg, map), Is.True,
                "the makeshift tourniquet would not start on a bleeding leg.");
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(right), Is.GreaterThan(0f), "the makeshift strap was tied in a second.");
                Assert.That(SEntMan.Deleted(strap), Is.False);
            });
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(right), Is.EqualTo(0f), "the makeshift tourniquet did not stop the leg.");
                Assert.That(SEntMan.GetComponent<WolfmedTourniquetComponent>(right).SlipDamage, Is.EqualTo(15f));
                Assert.That(SEntMan.GetComponent<WolfmedTourniquetComponent>(left).SlipDamage, Is.Null,
                    "the real tourniquet was recorded as one that slips.");
            });

            // A light knock (the do-after line) leaves it on.
            routing.TryApplyPartDamage(patient, right, WolfmedScenario.Spec("Blunt", 10), attacker);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedTourniquetComponent>(right), Is.True, "a 10 hit knocked it loose.");
                Assert.That(bleeding.GetPartRate(right), Is.EqualTo(0f));
            });

            // A hard hit on both legs: the cloth goes, the real strap stays.
            routing.TryApplyPartDamage(patient, right, WolfmedScenario.Spec("Slash", 20), attacker);
            routing.TryApplyPartDamage(patient, left, WolfmedScenario.Spec("Slash", 20), attacker);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedTourniquetComponent>(right), Is.False,
                    "the makeshift tourniquet held through a 20 Slash.");
                Assert.That(bleeding.GetPartRate(right), Is.GreaterThan(0f), "the leg did not bleed again once it slipped.");
                Assert.That(SEntMan.HasComponent<WolfmedTourniquetComponent>(left), Is.True);
                Assert.That(bleeding.GetPartRate(left), Is.EqualTo(0f), "the real tourniquet let go.");
            });
        });
    }

    /// <summary>
    /// A strap on a leg ties off the foot as well (playtest 4), so knocking it loose releases the foot too; before, a
    /// strap that came off left the foot's bleed clamped with nothing holding it.
    /// </summary>
    [Test]
    public async Task SlippedStrapReleasesTheFootTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid patient = default, attacker = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var routing = SEntMan.System<WoundDamageRoutingSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var leg = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Right);
            var foot = s.Part(patient, BodyPartType.Foot, BodyPartSymmetry.Right);
            routing.TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Slash", 40), attacker);
            routing.TryApplyPartDamage(patient, foot, WolfmedScenario.Spec("Slash", 30), attacker);

            var strap = SEntMan.SpawnEntity(Makeshift, map.GridCoords);
            Assert.That(SEntMan.System<Content.Shared._Onyx.Medical.Tourniquet.TourniquetSystem>().Apply(patient, leg), Is.True);
            SEntMan.System<WolfmedTourniquetSlipSystem>().OnApplied(leg, strap);
            Assert.That(bleeding.GetPartRate(foot), Is.EqualTo(0f), "the strap on the leg did not tie off the foot.");

            routing.TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Piercing", 20), attacker);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedTourniquetComponent>(leg), Is.False);
                Assert.That(bleeding.GetPartRate(foot), Is.GreaterThan(0f), "the foot stayed clamped under a strap that was gone.");
            });
        });
    }

    /// <summary>
    /// A hit that lands on a wound already under the strap can start that wound bleeding (the roll is one in a few).
    /// That bleed is tied off like any other; before, it ran free under a tourniquet that was still on.
    /// </summary>
    [Test]
    public async Task BleedStartingOnAnOldWoundUnderAStrapIsTiedOffTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid patient = default, attacker = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var routing = SEntMan.System<WoundDamageRoutingSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var wounds = SEntMan.System<WoundSystem>();
            var leg = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Right);
            routing.TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Slash", 40), attacker);
            routing.TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Blunt", 10), attacker);
            Assert.That(SEntMan.System<Content.Shared._Onyx.Medical.Tourniquet.TourniquetSystem>().Apply(patient, leg), Is.True);

            // The roll, forced: every wound on the leg that was not bleeding starts to.
            foreach (var wound in wounds.GetWounds(leg).ToArray())
            {
                if (SEntMan.HasComponent<WoundBleedingComponent>(wound))
                    continue;

                var started = SEntMan.AddComponent<WoundBleedingComponent>(wound);
                started.BleedingSeverity = wound.Comp.Severity;
                bleeding.RefreshWound((wound.Owner, started));
            }

            var bleeds = wounds.GetWounds(leg)
                .Select(wound => SEntMan.GetComponent<WoundBleedingComponent>(wound))
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(bleeds, Has.Length.GreaterThanOrEqualTo(2), "the bruise under the strap never bled.");
                Assert.That(bleeds.All(bleed => bleed.Treatment == BleedingTreatment.Clamped), Is.True,
                    "a bleed that started under the strap was not tied off.");
                Assert.That(bleeds.Count(bleed => bleed.BaseRate > 0f), Is.GreaterThanOrEqualTo(2));
                Assert.That(bleeding.GetPartRate(leg), Is.EqualTo(0f));
            });
        });
    }

    private EntityUid[] Straps() =>
        SEntMan.EntityQuery<WolfmedMakeshiftTourniquetComponent>().Select(strap => strap.Owner).ToArray();

    private EntityUid SpawnInHand(EntityUid user, string id, TestMapData map)
    {
        var item = SEntMan.SpawnEntity(id, map.GridCoords);
        Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, item), Is.True);
        return item;
    }

    private bool Apply(EntityUid user, EntityUid patient, string id, TargetBodyPart part, TestMapData map) =>
        Apply(user, patient, SpawnInHand(user, id, map), part, map);

    /// <summary>Aims the user at the part and clicks the patient with the strap, as a player does.</summary>
    private bool Apply(EntityUid user, EntityUid patient, EntityUid strap, TargetBodyPart part, TestMapData map)
    {
        SEntMan.GetComponent<TargetingComponent>(user).Target = part;
        var click = new AfterInteractEvent(user, strap, patient, map.GridCoords, canReach: true);
        SEntMan.EventBus.RaiseLocalEvent(strap, click);
        return click.Handled;
    }
}
