using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
[TestOf(typeof(WoundBleedingSystem))]
public sealed class WolfmedBleedingLifecycleTest : GameTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task HealingBelowBleedingThresholdUpdatesBloodstreamTest(bool otherBleedingWound)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var analyzer = entities.System<HealthAnalyzerSystem>();
            var torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var bloodstream = entities.GetComponent<BloodstreamComponent>(body);
            if (otherBleedingWound)
                Assert.That(wounds.CreateOrMergeWound(torso, "PiercingWound", 10), Is.Not.Null);
            var remainingRate = bloodstream.BleedAmount;
            var wound = wounds.CreateOrMergeWound(torso, "SlashWound", 10)!.Value;
            Assert.That(bloodstream.BleedAmount, Is.GreaterThan(remainingRate));

            // Below severity 9, SlashWound loses its bleeding component while the wound remains.
            Assert.That(wounds.ChangeSeverity(wound, -2), Is.True);
            Assert.That(entities.HasComponent<WoundBleedingComponent>(wound), Is.False);
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(remainingRate).Within(0.0001f),
                "Removing a bleeding effect must remove its contribution from the body immediately.");
            var diagnostics = analyzer.BuildWoundDiagnostics(body)!;
            Assert.That(diagnostics.Parts.Values.Sum(part => part.BleedingRate),
                Is.EqualTo(bloodstream.BleedAmount).Within(0.0001f));

            // Removing the now non-bleeding wound must not leave an invisible bleed behind.
            wounds.ClearWounds(torso);
            Assert.That(analyzer.BuildWoundDiagnostics(body)!.Parts.Values.Sum(part => part.BleedingRate),
                Is.Zero);
            Assert.That(bloodstream.BleedAmount, Is.Zero);
        });
    }

    /// <summary>
    /// A bleed that gauze stopped stays stopped. The bleeding component used to be removed at zero, and the
    /// next severity change of any kind (a bruise pack healing the wound, a state change) re-rolled the bleed
    /// at full strength: gauze held for seconds, healing a wound made it bleed, and no dressing ever showed.
    /// </summary>
    [Test]
    public async Task DressedBleedStaysStoppedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            // Dressed: the component stays, at zero, marked Bandaged.
            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 20)!.Value;
            Assert.That(bleeding.GetPartRate(torso), Is.GreaterThan(0f));
            Assert.That(bleeding.ReducePartBleeding(torso, 1000, dressing: true));
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(torso), Is.Zero);
                Assert.That(entities.GetComponent<WoundBleedingComponent>(cut).Treatment,
                    Is.EqualTo(BleedingTreatment.Bandaged), "the gauze is what the limb overlay shows.");
            });

            // Healing the wound under the dressing does not reopen it.
            Assert.That(wounds.ChangeSeverity(cut, -5));
            wounds.RefreshRuntimeComponents(cut);
            Assert.That(bleeding.GetPartRate(torso), Is.Zero, "treating a wound must never make it bleed.");

            // Stopped without a dressing (clotting, a drug): the component goes, and healing still does not re-roll it.
            var head = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var second = wounds.CreateOrMergeWound(head, "SlashWound", 20)!.Value;
            Assert.That(bleeding.ReduceBleeding(second, 1000));
            Assert.That(entities.HasComponent<WoundBleedingComponent>(second), Is.False);
            Assert.That(wounds.ChangeSeverity(second, -5));
            Assert.That(entities.HasComponent<WoundBleedingComponent>(second), Is.False,
                "a wound that shrank is not a new injury.");

            // A fresh hit on it is, and bleeds again.
            Assert.That(wounds.CreateOrMergeWound(head, "SlashWound", 10), Is.EqualTo(second));
            Assert.That(bleeding.GetPartRate(head), Is.GreaterThan(0f));
        });
    }

    /// <summary>
    /// A dressed arterial bleed still bleeds, slowly, and no topical can lower it further. "Is the part bleeding"
    /// was the wrong question for whether gauze has work left: it made gauze apply itself to a shot chest until
    /// the stack was gone.
    /// </summary>
    [Test]
    public async Task DressingAnArteryIsDoneAfterOneTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            Assert.That(wounds.CreateOrMergeWound(torso, "WFWolfmedArterialBleedWound", 30), Is.Not.Null);
            Assert.That(bleeding.CanDressBleeding(torso), Is.True, "an undressed artery is work for gauze.");

            Assert.That(bleeding.BandageArterialBleeds(torso), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(torso), Is.GreaterThan(0f), "the dressing only slows it.");
                Assert.That(bleeding.CanDressBleeding(torso), Is.False, "and a second dressing would do nothing.");
                Assert.That(bleeding.HasUndressableBleed(torso), Is.True, "which is what the medic is told.");
            });
        });
    }

    /// <summary>
    /// A bandage stays on the limb after the wound under it has closed, and a tourniquet knows whether it is still
    /// holding a bleed. Both are what the medic reads to know what is done and what is safe to take off.
    /// </summary>
    [Test]
    public async Task DressingLingersAndTourniquetKnowsItsJobTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var visuals = entities.System<Content.Server._WF.Wolfmed.Damage.WolfmedTreatmentVisualsSystem>();
            var necrosis = entities.System<Content.Server._WF.Wolfmed.Wounds.WolfmedNecrosisSystem>();
            var profile = server.ProtoMan.Index<Content.Shared._WF.Wolfmed.Damage.WolfmedTreatmentOverlayProfilePrototype>(
                "WFWolfmedTreatmentOverlayDefault");
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(body)
                .First(part => part.Component.PartType == BodyPartType.Arm).Id;

            var cut = wounds.CreateOrMergeWound(arm, "SlashWound", 20)!.Value;
            Assert.That(bleeding.ReducePartBleeding(arm, 1000, dressing: true));
            Assert.That(visuals.GetTreatment(arm, profile),
                Is.EqualTo(Content.Shared._WF.Wolfmed.Damage.WolfmedPartTreatment.Gauze));

            // The wound closes and goes. The bandage does not go with it.
            Assert.That(wounds.RemoveWound(cut));
            Assert.That(visuals.GetTreatment(arm, profile),
                Is.EqualTo(Content.Shared._WF.Wolfmed.Damage.WolfmedPartTreatment.Gauze),
                "a dressing that vanished when the treatment worked told the medic nothing had been done.");

            // A tourniquet over an open bleed is holding it; over nothing, it is safe to take off.
            var second = wounds.CreateOrMergeWound(arm, "SlashWound", 20)!.Value;
            Assert.That(bleeding.SetTreatment(second, BleedingTreatment.Clamped));
            Assert.That(necrosis.IsHoldingABleed(arm), Is.True);
            Assert.That(bleeding.ReduceBleeding(second, 1000));
            Assert.That(necrosis.IsHoldingABleed(arm), Is.False);
        });
    }
}
