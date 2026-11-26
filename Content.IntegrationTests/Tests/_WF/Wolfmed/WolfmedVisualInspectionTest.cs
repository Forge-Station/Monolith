#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WF.Wolfmed.Examine;
using Content.Client._WF.Wolfmed.Medical;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// LOOK: the health examine read as a visual inspection. What shows is what an examiner could see on the
/// body in front of them: bare skin reveals everything, clothing hides all but gross findings, and the
/// patient knows their own body whatever they are wearing.
/// </summary>
/// <remarks>
/// Appearance is data (<c>_WF/Wolfmed/Wounds/look.yml</c>), so the last two tests are the ones that keep
/// it honest: every wound prototype declares what it looks like, and every key it names resolves.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedVisualInspectionSystem))]
public sealed class WolfmedVisualInspectionTest : GameTest
{
    /// <summary>Keys the system builds itself rather than reading off a look prototype.</summary>
    private static readonly string[] BuiltKeys =
    [
        "wolfmed-look-title-self", "wolfmed-look-title-other", "wolfmed-look-part-self",
        "wolfmed-look-part-other", "wolfmed-look-none-self", "wolfmed-look-none-other",
        "wolfmed-look-covered", "wolfmed-look-hidden", "wolfmed-look-distant", "wolfmed-look-sepsis-self",
        "wolfmed-look-sepsis-other",
        // INFECTION: septic shock replaces the flush.
        "wolfmed-look-septic-shock-self", "wolfmed-look-septic-shock-other",
        // M1a: the chest and the pulse.
        "wolfmed-look-gasping-self", "wolfmed-look-gasping-other", "wolfmed-look-breathing-slow-self",
        "wolfmed-look-breathing-slow-other", "wolfmed-look-pale-self", "wolfmed-look-pale-other",
        "wolfmed-look-pulse-weak-self", "wolfmed-look-pulse-weak-other", "wolfmed-look-pulse-faint-self",
        "wolfmed-look-pulse-faint-other", "wolfmed-look-not-breathing-self", "wolfmed-look-not-breathing-other",
        "wolfmed-look-no-pulse-self", "wolfmed-look-no-pulse-other",
        "wolfmed-look-part-name-head", "wolfmed-look-part-name-torso", "wolfmed-look-part-name-groin",
        "wolfmed-look-part-name-left-arm", "wolfmed-look-part-name-left-hand",
        "wolfmed-look-part-name-right-arm", "wolfmed-look-part-name-right-hand",
        "wolfmed-look-part-name-left-leg", "wolfmed-look-part-name-left-foot",
        "wolfmed-look-part-name-right-leg", "wolfmed-look-part-name-right-foot",
    ];

    /// <summary>A cut is there for anyone to see, until a sleeve goes over it. The owner still knows.</summary>
    [Test]
    public async Task BareSkinShowsWhatClothingHidesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Wound(entities, arm, "SlashWound", 30);

            var cut = Text(locale, "wolfmed-look-cut-moderate");
            var partName = Text(locale, "wolfmed-look-part-name-left-arm");

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(cut).And.Contain(partName),
                    "a cut on a bare arm is there to be seen.");
                Assert.That(Look(entities, body, body), Does.Contain(cut));
            });

            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Not.Contain(cut),
                    "a sleeve hides it from everyone else,");
                Assert.That(Look(entities, body, medic), Does.Contain(Text(locale, "wolfmed-look-covered")),
                    "and says so once rather than part by part.");
                Assert.That(Look(entities, body, body), Does.Contain(cut),
                    "but you can look under your own sleeve.");
            });
        });
    }

    /// <summary>Enough blood and the clothing stops hiding anything.</summary>
    [Test]
    public async Task HeavyBleedingSoaksThroughClothingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Bleed(entities, Wound(entities, arm, "SlashWound", 30), 2f);

            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(Text(locale, "wolfmed-look-soaking")));
                Assert.That(Look(entities, body, body), Does.Contain(Text(locale, "wolfmed-look-bleed-flowing")),
                    "the owner sees the wound itself rather than the stain.");
            });
        });
    }

    /// <summary>A bone out of line shows through a jumpsuit. A crack in one shows to nobody.</summary>
    [Test]
    public async Task OnlyDeformityShowsThroughClothingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, uniform, "jumpsuit", true, true), Is.True);

            // Hairline: stage 12 in BoneFractureWound's own table.
            Wound(entities, arm, "BoneFractureWound", 12);
            var ache = Text(locale, "wolfmed-look-fracture-ache");
            var bent = Text(locale, "wolfmed-look-fracture-displaced");

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Not.Contain(ache).And.Not.Contain(bent),
                    "a hairline fracture is a finding for the analyzer, not for the eye.");
                Assert.That(Look(entities, body, body), Does.Contain(ache),
                    "the patient gets an ache, not a diagnosis.");
                Assert.That(Look(entities, body, body), Does.Not.Contain(bent));
            });

            // 12 + 20 merges to 32, which is Displaced.
            Wound(entities, arm, "BoneFractureWound", 20);

            Assert.Multiple(() =>
            {
                Assert.That(Look(entities, body, medic), Does.Contain(bent),
                    "a limb bent the wrong way shows through a sleeve,");
                Assert.That(Look(entities, body, medic, false), Does.Contain(bent),
                    "and from across the room.");
            });
        });
    }

    /// <summary>What is under the skin stays under it, however bad it is.</summary>
    [Test]
    public async Task InternalFindingsNeverShowTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Wound(entities, torso, "InternalBleedingWound", 30);
            Wound(entities, torso, "WFWolfmedOrganContusionWound", 20);
            Wound(entities, head, "WFWolfmedConcussionWound", 5);
            Wound(entities, arm, "WFWolfmedTendonCutWound", 20);

            var seen = Look(entities, body, medic);
            var known = Look(entities, body, body);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(
                        Text(locale, "wolfmed-look-none-other", ("target", Identity.Entity(body, entities)))),
                    "a torso full of internal bleeding looks like an unhurt torso.");
                Assert.That(known, Does.Contain(Text(locale, "wolfmed-look-tendon-self")),
                    "the patient feels the tendon,");
                Assert.That(known, Does.Contain(Text(locale, "wolfmed-look-concussion-self")),
                    "and the concussion,");
                Assert.That(known, Does.Not.Contain(Text(locale, "wolfmed-look-concussion-severe")),
                    "without the unfocused eyes a bad one would show.");
            });
        });
    }

    /// <summary>A dressing, a splint and a tourniquet are as visible as the injury under them.</summary>
    [Test]
    public async Task TreatmentIsVisibleTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            var stitched = Bleed(entities, Wound(entities, arm, "SlashWound", 30), 6f);
            entities.GetComponent<WoundBleedingComponent>(stitched).Treatment = BleedingTreatment.Sutured;
            // What suturing really does to the rate (the treatment multiplier is zero). The look reads the rate,
            // not the label, so a dressing that is still losing can say so.
            entities.GetComponent<WoundBleedingComponent>(stitched).CurrentRate = 0f;
            entities.EnsureComponent<WolfmedTourniquetComponent>(arm);

            // Comminuted at 60 Blunt, which is what the splint needs before it will go on.
            entities.System<DamageableSystem>()
                .TryChangeDamage(body, Blunt(60), origin: null, targetPart: TargetBodyPart.LeftLeg);
            var splint = entities.SpawnEntity("WFWolfmedSplint", map.GridCoords);
            Assert.That(entities.System<WolfmedSplintSystem>()
                .TryApply((splint, entities.GetComponent<WolfmedSplintComponent>(splint)), body, leg, medic), Is.True);

            var treated = Line(entities, locale, body, medic, "left-arm");

            Assert.Multiple(() =>
            {
                Assert.That(treated, Does.Contain(Text(locale, "wolfmed-look-treatment-sutured")));
                Assert.That(treated, Does.Not.Contain(Text(locale, "wolfmed-look-bleed-spurting")),
                    "a closed wound does not read as still bleeding.");
                Assert.That(treated, Does.Contain(Text(locale, "wolfmed-look-tourniquet")));
                Assert.That(Line(entities, locale, body, medic, "left-leg"),
                    Does.Contain(Text(locale, "wolfmed-look-splint-splint")));
            });
        });
    }

    /// <summary>A round still in the wound is a hole. Shrapnel is metal you can see.</summary>
    [Test]
    public async Task EmbeddedObjectsReadDifferentlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Wound(entities, Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left),
                "WFWolfmedLodgedRoundWound", 10);
            Wound(entities, Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Right),
                "WFWolfmedShrapnelWound", 10);

            var seen = Look(entities, body, medic);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-lodged-round")),
                    "a lodged round reads as a hole with nothing coming back out of it.");
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-shrapnel-minor")),
                    "shrapnel stands out of the skin.");
            });
        });
    }

    /// <summary>
    /// Gauze over a wound it cannot hold reads as dressed AND bleeding. The look used to drop every dressed wound
    /// from the bleeding total, so a bandaged artery looked finished.
    /// </summary>
    [Test]
    public async Task LeakingDressingStillReadsAsBleedingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var wound = Bleed(entities, Wound(entities, arm, "SlashWound", 30), 2f);
            entities.GetComponent<WoundBleedingComponent>(wound).Treatment = BleedingTreatment.Bandaged;

            var line = Line(entities, locale, body, body, "left-arm");
            Assert.Multiple(() =>
            {
                Assert.That(line, Does.Contain(Text(locale, "wolfmed-look-treatment-bandaged")));
                Assert.That(line, Does.Contain(Text(locale, "wolfmed-look-bleed-flowing")),
                    "blood getting past the dressing is the thing the medic most needs to see.");
            });
        });
    }

    /// <summary>A chassis dents, breaches and leaks. It never bruises or bleeds.</summary>
    [Test]
    public async Task ChassisUsesMechanicalWordingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, machine, BodyPartType.Arm, BodyPartSymmetry.Left);
            Bleed(entities, Wound(entities, arm, "WFWolfmedBreachWound", 10), 2f);

            var seen = Look(entities, machine, medic);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-breach-minor")));
                Assert.That(seen, Does.Contain(Text(locale, "wolfmed-look-bleed-flowing-mechanical")),
                    "a chassis leaks fluid,");
                Assert.That(seen, Does.Not.Contain(Text(locale, "wolfmed-look-bleed-flowing")),
                    "and never bleeds.");
            });
        });
    }

    /// <summary>From across the room only the gross things carry.</summary>
    [Test]
    public async Task DistanceHidesDetailTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Wound(entities, Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left), "SlashWound", 30);
            Wound(entities, Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Right),
                "WFWolfmedDislocationWound", 10);

            var far = Look(entities, body, medic, false);

            Assert.Multiple(() =>
            {
                Assert.That(far, Does.Contain(Text(locale, "wolfmed-look-dislocation")),
                    "a joint out of its socket is visible from a distance,");
                Assert.That(far, Does.Not.Contain(Text(locale, "wolfmed-look-cut-moderate")),
                    "a cut is not,");
                Assert.That(far, Does.Contain(Text(locale, "wolfmed-look-distant")),
                    "and the examiner is told why.");
                Assert.That(Look(entities, body, body, false),
                    Does.Contain(Text(locale, "wolfmed-look-cut-moderate")),
                    "your own body is never far away.");
            });
        });
    }

    /// <summary>
    /// M1a (plan §4.5, §5.5): the chest and the pulse come from the networked vitals the life tick sets.
    /// Sedation past its depression line reads as slow, shallow breathing, never as "not breathing"; the blood
    /// bands read pale, then pale and clammy with a weak pulse, then barely palpable; a hand on the neck needs
    /// to be close; a chassis has neither.
    /// </summary>
    [Test]
    public async Task BreathingAndPulseReadTheNetworkedVitalsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.BloodBandPale, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(entities);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var sedated = entities.SpawnEntity("MobHuman", map.GridCoords);
            var pale = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weak = entities.SpawnEntity("MobHuman", map.GridCoords);
            var faint = entities.SpawnEntity("MobHuman", map.GridCoords);
            var machine = entities.SpawnEntity("MobIPC", map.GridCoords);

            Assert.That(Look(entities, sedated, medic), Does.Not.Contain("breathing").And.Not.Contain("pulse"),
                "a healthy patient already read as short of breath or pulse.");

            var relief = entities.EnsureComponent<WolfmedPainReliefComponent>(sedated);
            relief.Sedation = 0.8f;
            s.SetBlood(pale, 0.7f);
            s.SetBlood(weak, 0.45f);
            s.SetBlood(faint, 0.33f);
            s.SetBlood(machine, 0.4f);
            foreach (var body in new[] { sedated, pale, weak, faint, machine })
                s.Life.UpdateVitalSigns(body);

            Assert.That(s.Vitals(sedated).Breathing, Is.EqualTo(WolfmedBreathing.Depressed));
            var slowed = Look(entities, sedated, medic);
            Assert.Multiple(() =>
            {
                Assert.That(slowed, Does.Contain("breathing slowly and shallowly"));
                Assert.That(slowed, Does.Not.Contain("is not breathing"), "sedation read as a stopped chest.");
                Assert.That(Look(entities, pale, medic), Does.Contain("looks pale"));
                Assert.That(Look(entities, weak, medic), Does.Contain("pale and clammy, with a weak, rapid pulse"));
                Assert.That(Look(entities, faint, medic), Does.Contain("barely palpable pulse"));
                Assert.That(Look(entities, weak, medic, false), Does.Not.Contain("pulse"),
                    "a pulse was felt from across the room.");
                Assert.That(Look(entities, weak, weak), Does.Contain("your pulse is weak and fast"));
                Assert.That(Look(entities, machine, medic), Does.Not.Contain("pulse").And.Not.Contain("pale"),
                    "a chassis read as flesh.");
            });
        });
    }

    /// <summary>Every wound in the game declares what it looks like, or it is invisible by accident.</summary>
    [Test]
    public async Task EveryWoundDeclaresALookTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var looks = entities.System<WolfmedVisualInspectionSystem>();
            var claimed = new Dictionary<string, string>();

            foreach (var look in prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            {
                Assert.That(prototypes.HasIndex(look.Wound), Is.True,
                    $"{look.ID} describes a wound that does not exist.");
                Assert.That(claimed.TryAdd(look.Wound.Id, look.ID), Is.True,
                    $"{look.ID} and {claimed.GetValueOrDefault(look.Wound.Id)} both describe {look.Wound.Id}.");
            }

            foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>())
            {
                Assert.That(looks.TryGetLook(wound.ID, out _), Is.True,
                    $"{wound.ID} has no wolfmedWoundLook: nobody could ever see it.");
            }
        });
    }

    /// <summary>Every string the inspection can print resolves, including every stage of every wound.</summary>
    [Test]
    public async Task EveryLookStringResolvesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var keys = new List<string>(BuiltKeys);

            // LOOK2: every finding the system builds itself needs the sentence and the row label beside it.
            foreach (var key in FindingKeys())
            {
                keys.Add(key);
                keys.Add(key + "-short");
            }

            foreach (var look in prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
            {
                Collect(keys, look.Description, look.Label);
                Collect(keys, look.SelfHint, look.HintLabel);
                foreach (var stage in look.Stages.Values)
                {
                    Collect(keys, stage.Description, stage.Label);
                    Collect(keys, stage.SelfHint, stage.HintLabel);
                }

                // A stage the wound itself does not declare would never be reached.
                var wound = prototypes.Index(look.Wound);
                foreach (var stage in look.Stages.Keys)
                {
                    Assert.That(wound.Stages.ContainsKey(stage), Is.True,
                        $"{look.ID} describes stage {stage}, which {wound.ID} does not have.");
                }
            }

            foreach (var key in keys)
                Assert.That(locale.HasString(key), Is.True, $"{key} has no string.");
        });
    }

    /// <summary>
    /// LOOK2: the rows are drawn from data, so the data has to be drawable. Every glyph is a state the RSI
    /// really has, every colour is one the client's palette answers to, and every description has the short
    /// label its row needs.
    /// </summary>
    [Test]
    public async Task EveryLookFindingIsDrawableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var meta = resources.ContentFileReadAllText(IconRsi / "meta.json");
            var profile = prototypes.Index<WolfmedLookProfilePrototype>(WolfmedLookProfilePrototype.Default);

            Assert.Multiple(() =>
            {
                // The client holds the colours, the shared list holds the keys; they have to be one palette.
                Assert.That(WolfmedWoundStyle.LookKeys.OrderBy(key => key),
                    Is.EqualTo(WolfmedLookPalette.All.OrderBy(key => key)),
                    "the client palette and the keys the data may name have drifted apart.");

                foreach (var cls in WolfmedLookClasses.All)
                {
                    Assert.That(profile.Glyph(cls), Is.Not.Null, $"{profile.ID} declares no glyph for {cls}.");
                    if (profile.Glyph(cls) is not { } glyph)
                        continue;

                    AssertDrawable(resources, meta, glyph.Icon, glyph.Colour, cls);
                }

                foreach (var key in profile.AccentPriority)
                {
                    Assert.That(WolfmedLookPalette.Knows(key), Is.True,
                        $"{profile.ID} orders {key}, which is not a palette colour.");
                }

                // The nine analyzer categories are what a wound draws with when its look names nothing.
                foreach (var category in WolfmedWoundCategories.All)
                {
                    var state = WolfmedWoundCategories.IconState(category);
                    AssertDrawable(resources, meta, state, state, category.ToString());
                }

                foreach (var look in prototypes.EnumeratePrototypes<WolfmedWoundLookPrototype>())
                {
                    AssertLabelled(look.ID, "itself", look.Description, look.Label, look.SelfHint, look.HintLabel);
                    AssertGlyph(resources, meta, look.ID, look.Icon, look.Colour);

                    foreach (var (name, stage) in look.Stages)
                    {
                        AssertLabelled(look.ID, name, stage.Description ?? look.Description, stage.Label ?? look.Label,
                            stage.SelfHint ?? look.SelfHint, stage.HintLabel ?? look.HintLabel);
                        AssertGlyph(resources, meta, look.ID, stage.Icon, stage.Colour);
                    }
                }
            });
        });
    }

    /// <summary>
    /// LOOK2: what the server writes is what the client reads back. A mauled patient goes through the markup
    /// and comes out as the same parts, in the same order, with the same findings on them.
    /// </summary>
    [Test]
    public async Task InspectionMarkupRoundTripsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            Bleed(entities, Wound(entities, arm, "SlashWound", 40), 2f);
            Wound(entities, arm, "WFWolfmedShrapnelWound", 10);
            Wound(entities, Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Right),
                "WFWolfmedDislocationWound", 10);
            entities.EnsureComponent<WolfmedTourniquetComponent>(arm);

            var report = Report(entities, body, medic);
            var message = Markup(entities, body, medic);
            var read = ReadBack(message);

            Assert.That(report.Parts, Has.Count.GreaterThan(1), "the patient needs more than one hurt part.");
            Assert.That(read, Has.Count.EqualTo(report.Parts.Count), "every part must survive the markup.");

            Assert.Multiple(() =>
            {
                for (var index = 0; index < report.Parts.Count; index++)
                {
                    var written = report.Parts[index];
                    var (name, accent, findings) = read[index];
                    Assert.That(name, Is.EqualTo(written.Name));
                    Assert.That(accent, Is.EqualTo(written.Accent));
                    Assert.That(findings.Select(finding => finding.Icon),
                        Is.EqualTo(written.Findings.Select(finding => finding.Icon)));
                    Assert.That(findings.Select(finding => finding.Colour),
                        Is.EqualTo(written.Findings.Select(finding => finding.Colour)));
                    Assert.That(findings.Select(finding => finding.Label),
                        Is.EqualTo(written.Findings.Select(finding => finding.Label)));
                    Assert.That(findings.Select(finding => finding.Text),
                        Is.EqualTo(written.Findings.Select(finding => finding.Text)));
                }

                // The rows are for the tooltip; everything else reading the message gets plain sentences.
                var plain = message.ToString();
                Assert.That(plain, Does.Not.Contain(WolfmedLookTag.Part), "no tag soup in the chat copy.");
                foreach (var part in report.Parts)
                {
                    Assert.That(plain, Does.Contain(part.Name));
                    foreach (var finding in part.Findings)
                        Assert.That(plain, Does.Contain(finding.Label));
                }
            });
        });
    }

    /// <summary>
    /// LOOK2: the client turns that markup into one row per hurt part, with a chip per finding carrying the
    /// full sentence on hover. The mouse filter is the half that makes the tooltip work inside the examine
    /// popup, so it is asserted too.
    /// </summary>
    [Test]
    public async Task RowsAreBuiltForDamagedPartsOnlyTest()
    {
        var client = Pair.Client;
        await client.WaitIdleAsync();
        var entities = client.ResolveDependency<IEntityManager>();

        var message = new FormattedMessage();
        message.AddMarkupOrThrow("[color=DarkGray]You look them over.[/color]");
        var parts = new List<WolfmedLookPart>
        {
            SamplePart("left arm", "bleeding", ("cut", "cut", "deep cut", "a deep cut"),
                ("bleeding", "bleeding", "bleeding freely", "bleeding freely from it")),
            SamplePart("right leg", "fracture", ("fracture", "fracture", "bent wrong", "bent at a wrong angle")),
        };

        foreach (var part in parts)
        {
            WolfmedLookTag.WritePart(message, part);
            message.PushNewline();
            message.AddMarkupOrThrow(part.Line);
            WolfmedLookTag.WriteEnd(message);
        }

        message.PushNewline();
        message.AddMarkupOrThrow("[color=DarkGray]The rest is covered by clothing.[/color]");

        BoxContainer root = default!;
        var built = false;

        await client.WaitPost(() =>
        {
            root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            built = entities.System<Content.Client.Examine.ExamineSystem>()
                .TryAddWolfmedLookMessage(root, message);
            root.Measure(Vector2Helpers.Infinity);
            root.Arrange(UIBox2.FromDimensions(Vector2.Zero, root.DesiredSize));
        });

        await client.WaitAssertion(() =>
        {
            Assert.That(built, Is.True, "a message carrying the tag must be drawn as rows.");

            var rows = Descendants(root).OfType<WolfmedLookRow>().ToList();
            var chips = Descendants(root).OfType<WolfmedLookChip>().ToList();

            Assert.Multiple(() =>
            {
                Assert.That(rows, Has.Count.EqualTo(parts.Count), "one row per part with something to show.");
                Assert.That(chips, Has.Count.EqualTo(3), "one chip per finding.");
                Assert.That(rows.Select(row => row.PartLabel.Text), Is.EqualTo(parts.Select(part => part.Name)));

                foreach (var chip in chips)
                {
                    Assert.That(chip.ToolTip, Is.Not.Null.And.Not.Empty, "a chip with no tooltip says nothing.");
                    Assert.That(chip.MouseFilter, Is.Not.EqualTo(Control.MouseFilterMode.Ignore),
                        "a chip the mouse cannot find never shows its tooltip.");
                }

                // The name columns line up, and the whole-body lines stay as text.
                // One shared name column, and one that can never collapse: the rows are measured before the popup
                // has a font, and a column fixed to that measurement hid every part name in the real client.
                Assert.That(rows.Select(row => row.PartLabel.MinWidth).Distinct().Count(), Is.EqualTo(1));
                Assert.That(rows[0].PartLabel.MinWidth, Is.GreaterThanOrEqualTo(80f));
                Assert.That(rows.All(row => float.IsNaN(row.PartLabel.SetWidth)), Is.True,
                    "a fixed width is what clipped the names away.");
                Assert.That(Descendants(root).OfType<RichTextLabel>().Count(), Is.EqualTo(2),
                    "the title and the clothing notice are labels, not rows.");
            });

            root.Dispose();
        });
    }

    private static readonly ResPath IconRsi = new("/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi");

    private static WolfmedLookPart SamplePart(
        string name,
        string accent,
        params (string Icon, string Colour, string Label, string Text)[] findings)
    {
        var part = new WolfmedLookPart { Name = name, Accent = accent };
        foreach (var (icon, colour, label, text) in findings)
            part.Findings.Add(new WolfmedLookObservation(icon, colour, label, text));

        part.Line = name + ": " + string.Join(", ", part.Findings.Select(finding => finding.Label));
        return part;
    }

    /// <summary>Parses an examine message back into the parts and findings it was written from.</summary>
    private static List<(string Name, string Accent, List<WolfmedLookObservation> Findings)> ReadBack(
        FormattedMessage message)
    {
        var parts = new List<(string, string, List<WolfmedLookObservation>)>();
        List<WolfmedLookObservation>? findings = null;

        foreach (var node in message.Nodes)
        {
            if (WolfmedLookTag.TryReadPart(node, out var name, out var accent))
            {
                findings = new List<WolfmedLookObservation>();
                parts.Add((name, accent, findings));
                continue;
            }

            if (findings != null && WolfmedLookTag.TryReadFinding(node, out var finding))
                findings.Add(finding);
        }

        return parts;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    /// <summary>The locale keys of every finding the system builds rather than reads off a wound.</summary>
    private static List<string> FindingKeys()
    {
        var keys = new List<string>
        {
            "wolfmed-look-tourniquet", "wolfmed-look-scars", "wolfmed-look-numb",
            "wolfmed-look-soaking", "wolfmed-look-soaking-mechanical",
        };

        foreach (var treatment in new[] { "bandaged", "clamped", "sutured", "cauterized" })
            keys.Add("wolfmed-look-treatment-" + treatment);

        foreach (var overlay in new[] { "gauze", "splint", "splintimprovised", "splinttribal" })
            keys.Add("wolfmed-look-splint-" + overlay);

        foreach (var stage in new[] { "local", "spreading" })
            keys.Add("wolfmed-look-infection-" + stage);

        foreach (var band in new[] { "oozing", "flowing", "spurting" })
        {
            keys.Add("wolfmed-look-bleed-" + band);
            keys.Add("wolfmed-look-bleed-" + band + "-mechanical");
        }

        return keys;
    }

    private static void AssertLabelled(
        string id,
        string stage,
        LocId? description,
        LocId? label,
        LocId? hint,
        LocId? hintLabel)
    {
        if (description != null)
            Assert.That(label, Is.Not.Null, $"{id} ({stage}) describes a finding with no row label.");

        if (hint != null)
            Assert.That(hintLabel, Is.Not.Null, $"{id} ({stage}) hints at a finding with no row label.");
    }

    private static void AssertGlyph(
        IResourceManager resources,
        string meta,
        string id,
        string? icon,
        string? colour)
    {
        if (icon != null)
            AssertIcon(resources, meta, icon, id);

        if (colour != null)
            Assert.That(WolfmedLookPalette.Knows(colour), Is.True, $"{id} names colour {colour}, which is not one.");
    }

    private static void AssertDrawable(
        IResourceManager resources,
        string meta,
        string icon,
        string colour,
        string owner)
    {
        AssertIcon(resources, meta, icon, owner);
        Assert.That(WolfmedLookPalette.Knows(colour), Is.True, $"{owner} names colour {colour}, which is not one.");
    }

    private static void AssertIcon(IResourceManager resources, string meta, string icon, string owner)
    {
        Assert.That(resources.ContentFileExists(IconRsi / (icon + ".png")), Is.True,
            $"{owner} draws with {icon}, which analyzer_icons.rsi has no png for.");
        Assert.That(meta, Does.Contain($"\"name\": \"{icon}\""),
            $"analyzer_icons.rsi meta.json does not list {icon}.");
    }

    private static void Collect(List<string> keys, LocId? description, LocId? hint)
    {
        if (description is { } value)
            keys.Add(value.Id);

        if (hint is { } self)
            keys.Add(self.Id);
    }

    /// <summary>LOOK2: the structured inspection, which is what the system builds and the markup carries.</summary>
    private static WolfmedLookReport Report(
        IEntityManager entities,
        EntityUid examined,
        EntityUid examiner,
        bool detailed = true)
    {
        var report = entities.System<WolfmedVisualInspectionSystem>().GetLook(examined, examiner, detailed);
        Assert.That(report, Is.Not.Null, "the look profile is missing.");
        return report!;
    }

    /// <summary>
    /// Everything one inspection would put on screen, a part per line: the name, every row label and every
    /// tooltip, then the whole-body notes. What the assertions above search.
    /// </summary>
    private static string Look(IEntityManager entities, EntityUid examined, EntityUid examiner, bool detailed = true)
    {
        var report = Report(entities, examined, examiner, detailed);
        var text = new StringBuilder();
        text.AppendLine(FormattedMessage.RemoveMarkupPermissive(report.Title));

        foreach (var part in report.Parts)
        {
            text.Append(part.Name).Append(" [").Append(part.Accent).Append(']');
            foreach (var finding in part.Findings)
                text.Append(": ").Append(finding.Label).Append(" (").Append(finding.Text).Append(')');

            text.AppendLine();
        }

        foreach (var note in report.Notes)
            text.AppendLine(FormattedMessage.RemoveMarkupPermissive(note));

        return text.ToString();
    }

    /// <summary>The examine message the client is actually sent.</summary>
    private static FormattedMessage Markup(
        IEntityManager entities,
        EntityUid examined,
        EntityUid examiner,
        bool detailed = true)
    {
        var message = new FormattedMessage();
        entities.System<WolfmedVisualInspectionSystem>().AddLookMarkup(examined, examiner, message, detailed);
        return message;
    }

    /// <summary>The one line the inspection prints for a part, so a finding elsewhere cannot answer for it.</summary>
    private static string Line(
        IEntityManager entities,
        ILocalizationManager locale,
        EntityUid examined,
        EntityUid examiner,
        string part)
    {
        var name = Text(locale, "wolfmed-look-part-name-" + part);
        foreach (var line in Look(entities, examined, examiner).Split('\n'))
        {
            if (line.Contains(name))
                return line;
        }

        return string.Empty;
    }

    private static string Text(ILocalizationManager locale, string key, params (string, object)[] args) =>
        FormattedMessage.RemoveMarkupPermissive(locale.GetString(key, args));

    private static EntityUid Wound(IEntityManager entities, EntityUid part, string prototype, int severity)
    {
        var wound = entities.System<WoundSystem>().CreateOrMergeWound(part, prototype, FixedPoint2.New(severity));
        Assert.That(wound, Is.Not.Null, $"{prototype} could not be created on that part.");
        return wound!.Value;
    }

    /// <summary>Pins a bleeding rate rather than waiting on the server's own roll and clotting tick.</summary>
    private static EntityUid Bleed(IEntityManager entities, EntityUid wound, float rate)
    {
        entities.EnsureComponent<WoundBleedingComponent>(wound).CurrentRate = rate;
        return wound;
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Blunt(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };
}
