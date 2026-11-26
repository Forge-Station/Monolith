#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EntityEffects.Effects;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// "Medicines don't do what they're advertised to, tranexamic acid in particular": a reagent's ModifyBleedAmount on a
/// wound host was a bloodstream write GUARD E3 refuses. It now treats the wounds: a coagulant stops an ordinary bleed,
/// slows an artery without stopping it, and keeps every dressing; a bleed-worsening dose opens a systemic bleed.
/// </summary>
[TestFixture]
[TestOf(typeof(WoundBleedingSystem))]
public sealed class WolfmedReagentBleedingTest : GameTest
{
    /// <summary>Tranexamic acid's own effect stops an ordinary cut, at its dose times the bleed-rate knob per tick.</summary>
    [Test]
    public async Task CoagulantStopsAnOrdinaryBleedTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(body);
            var txa = Effect<ModifyBleedAmount>("TranexamicAcid");
            var perTick = -txa.Amount * Server.CfgMan.GetCVar(WolfmedCVars.BleedRate);

            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 30)!.Value;
            var open = bloodstream.BleedAmount;
            Assert.That(open, Is.GreaterThan(perTick), "the cut must bleed faster than one tick of the drug takes off.");

            txa.Effect(Args(body));
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(open - perTick).Within(0.02f),
                "one tick takes its dose off the bleed rate, in the wounds' own units.");

            for (var tick = 0; tick < 25; tick++)
                txa.Effect(Args(body));

            Assert.Multiple(() =>
            {
                Assert.That(bloodstream.BleedAmount, Is.Zero, "a coagulant stops an ordinary bleed.");
                Assert.That(SEntMan.HasComponent<WoundBleedingComponent>(cut), Is.False);
                Assert.That(SEntMan.EntityExists(cut), Is.True, "it stops the bleeding; it does not close the cut.");
            });
        });
    }

    /// <summary>
    /// An artery slows under a coagulant, down to its floor, and keeps pumping: still refusing treatment, still a job
    /// for a tourniquet or the table. A dressing on it stays on.
    /// </summary>
    [Test]
    public async Task CoagulantSlowsAnArteryButNeverStopsItTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var txa = Effect<ModifyBleedAmount>("TranexamicAcid");

            var artery = wounds.CreateOrMergeWound(torso, "WFWolfmedArterialBleedWound", 30)!.Value;
            var bleed = SEntMan.GetComponent<WoundBleedingComponent>(artery);
            var open = bleed.CurrentRate;
            Assert.That(open, Is.GreaterThan(0f));

            for (var tick = 0; tick < 100; tick++)
                txa.Effect(Args(body));

            Assert.Multiple(() =>
            {
                Assert.That(bleed.CurrentRate, Is.EqualTo(open * 0.5f).Within(0.02f),
                    "the artery clots down to its coagulant floor, half its severity, and no further.");
                Assert.That(bleed.BleedingSeverity, Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(wounds.TreatWound(artery, FixedPoint2.New(5)), Is.False,
                    "still pumping, so still refusing every treatment.");
            });

            // Dressed on top of the drug: the dressing stays and multiplies what is left.
            Assert.That(bleeding.BandageArterialBleeds(torso), Is.True);
            var dressed = bleed.CurrentRate;
            txa.Effect(Args(body));
            Assert.Multiple(() =>
            {
                Assert.That(bleed.Treatment, Is.EqualTo(BleedingTreatment.Bandaged), "a drug never takes a dressing off.");
                Assert.That(bleed.CurrentRate, Is.EqualTo(dressed).Within(0.001f));
                Assert.That(dressed, Is.EqualTo(open * 0.5f * 0.6f).Within(0.02f));
            });
        });
    }

    /// <summary>
    /// A coagulant never undoes a treatment: a dressed cut that it finishes keeps its dressing (the vanilla projection
    /// would have stripped it and doubled the rate), and a tourniqueted limb's wounds stay clamped at full severity.
    /// </summary>
    [Test]
    public async Task CoagulantKeepsEveryTreatmentTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var leg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var txa = Effect<ModifyBleedAmount>("TranexamicAcid");

            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 30)!.Value;
            Assert.That(bleeding.ReduceBleeding(cut, FixedPoint2.New(5), dressing: true));
            var dressedCut = SEntMan.GetComponent<WoundBleedingComponent>(cut);
            var before = dressedCut.CurrentRate;
            Assert.That(before, Is.GreaterThan(0f));

            SEntMan.System<DamageableSystem>().TryChangeDamage(body, Spec("Slash", 25), targetPart: TargetBodyPart.LeftLeg);
            Assert.That(SEntMan.System<TourniquetSystem>().Apply(body, leg), Is.True);
            var legBleeds = wounds.GetWounds(leg)
                .Where(wound => SEntMan.HasComponent<WoundBleedingComponent>(wound))
                .Select(wound => (wound.Owner, SEntMan.GetComponent<WoundBleedingComponent>(wound).BleedingSeverity))
                .ToList();
            Assert.That(legBleeds, Is.Not.Empty);

            txa.Effect(Args(body));
            Assert.Multiple(() =>
            {
                Assert.That(dressedCut.Treatment, Is.EqualTo(BleedingTreatment.Bandaged));
                Assert.That(dressedCut.CurrentRate, Is.LessThan(before), "a coagulant never makes a dressed wound bleed faster.");
            });

            for (var tick = 0; tick < 25; tick++)
                txa.Effect(Args(body));

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WoundBleedingComponent>(cut), Is.True, "the dressing stays on the stopped cut.");
                Assert.That(dressedCut.CurrentRate, Is.Zero);
                Assert.That(dressedCut.Treatment, Is.EqualTo(BleedingTreatment.Bandaged));
                foreach (var (wound, severity) in legBleeds)
                {
                    var comp = SEntMan.GetComponent<WoundBleedingComponent>(wound);
                    Assert.That(comp.Treatment, Is.EqualTo(BleedingTreatment.Clamped), "the tourniquet still holds.");
                    Assert.That(comp.BleedingSeverity, Is.EqualTo(severity), "a clamped bleed is not the drug's to change.");
                }
            });
        });
    }

    /// <summary>
    /// A dose that worsens bleeding (ketorolac's overdose) opens a systemic bleed on a wound host, as Onyx's
    /// ModifyBleed did; a body that is not a wound host keeps the bloodstream's own figure.
    /// </summary>
    [Test]
    public async Task WorseningDoseOpensASystemicBleedTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var ketorolac = Effect<ModifyBleedAmount>("Ketorolac");
            Assert.That(ketorolac.Amount, Is.GreaterThan(0f));

            for (var tick = 0; tick < 10; tick++)
                ketorolac.Effect(Args(body));

            var systemic = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .SelectMany(part => wounds.GetWounds(part.Id))
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("SystemicBleedingWound"))
                .ToList();
            Assert.Multiple(() =>
            {
                Assert.That(systemic, Has.Count.EqualTo(1));
                Assert.That(systemic[0].Comp.Severity.Float(), Is.EqualTo(10 * ketorolac.Amount).Within(0.02f));
                Assert.That(SEntMan.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.GreaterThan(0f));
            });

            // A mouse is no wound host: the upstream bloodstream write, unchanged.
            var mouse = SEntMan.SpawnEntity("MobMouse", map.GridCoords);
            Assert.That(SEntMan.HasComponent<WoundHostComponent>(mouse), Is.False);
            var blood = SEntMan.GetComponent<BloodstreamComponent>(mouse);
            Assert.That(SEntMan.System<BloodstreamSystem>().TryModifyBleedAmount(mouse, 5f, blood));
            Effect<ModifyBleedAmount>("TranexamicAcid").Effect(Args(mouse));
            Assert.That(blood.BleedAmount, Is.EqualTo(3.5f).Within(0.001f));
        });
    }

    private T Effect<T>(string reagent) where T : EntityEffect =>
        SProtoMan.Index<ReagentPrototype>(reagent).Metabolisms!["Medicine"].Effects.OfType<T>().Single();

    private EntityEffectReagentArgs Args(EntityUid body) =>
        new(body, SEntMan, null, null, FixedPoint2.New(1), null, null, FixedPoint2.New(1));

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry).Id;

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
