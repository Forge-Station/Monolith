#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// UI3: the analyzer's treatment advice. The tooltips and the procedure window are locale data keyed off
/// prototype ids and condition names, so a wound shipped without advice is a silent gap in the UI unless
/// something iterates every prototype and fails. That is what this does.
/// </summary>
/// <remarks>
/// It does not check that the advice is correct - no test can - only that it exists, that it is a real
/// procedure rather than a stub, and that the payload carries the id the keys are built from.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedTreatmentAdvice))]
public sealed class WolfmedTreatmentAdviceTest : GameTest
{
    /// <summary>Fixed strings the panel and the procedure window build without a prototype behind them.</summary>
    private static readonly string[] Chrome =
    [
        "wolfmed-treatment-guidebook-button",
        "health-analyzer-wound-targeted-tag",
        "health-analyzer-wound-target-part-hint",
        "health-analyzer-wound-banner-sepsis",
        "health-analyzer-wound-banner-blood-low",
        // UI4: the procedure window's own chrome.
        "wolfmed-treatment-resolved",
        "wolfmed-treatment-avoid-heading",
        "wolfmed-treatment-step-done",
        "wolfmed-treatment-step-surgery",
        "wolfmed-treatment-title",
    ];

    /// <summary>
    /// Every wound prototype in the game has a tooltip line. The procedure behind it is a prototype and is
    /// checked in <see cref="WolfmedTreatmentProcedureTest"/>.
    /// </summary>
    [Test]
    public async Task EveryWoundPrototypeHasAdviceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>().OrderBy(wound => wound.ID))
                {
                    var shortKey = WolfmedTreatmentAdvice.ShortKey(wound.ID);

                    Assert.That(locale.HasString(shortKey), Is.True,
                        $"{wound.ID} has no treatment tooltip ({shortKey}).");
                }
            });
        });
    }

    /// <summary>
    /// The wounds both flesh and a chassis can carry need the chassis procedure too: a medic reading
    /// "apply ointment" off an IPC has been told to do something the game refuses.
    /// </summary>
    [Test]
    public async Task DualCarrierWoundsHaveChassisAdviceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            // The list is only meaningful while it matches the profiles, so it is re-derived here rather
            // than trusted: every wound carried by both a Biological and a Mechanical profile needs both.
            var organic = new HashSet<string>();
            var chassis = new HashSet<string>();
            foreach (var profile in prototypes.EnumeratePrototypes<BodyPartProfilePrototype>())
            {
                var set = profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological)
                    ? organic
                    : chassis;
                foreach (var wound in profile.SupportedWounds)
                    set.Add(wound.Id);
            }

            var dual = organic.Intersect(chassis).OrderBy(id => id).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(dual, Is.EquivalentTo(WolfmedTreatmentAdvice.MechanicalWounds),
                    "the wounds both flesh and a chassis carry have changed; update MechanicalWounds and the advice.");

                foreach (var id in dual)
                {
                    var mechanical = WolfmedTreatmentAdvice.ShortKey(id) +
                                     WolfmedTreatmentAdvice.MechanicalSuffix;
                    Assert.That(locale.HasString(mechanical), Is.True,
                        $"{id} is carried by a chassis but has no chassis tooltip ({mechanical}).");

                    // UI4: and the chassis procedure the window opens from that row.
                    var procedure = WolfmedTreatmentAdvice.ProcedureId(id, true);
                    Assert.That(prototypes.HasIndex<WolfmedTreatmentProcedurePrototype>(procedure), Is.True,
                        $"{id} is carried by a chassis but has no chassis procedure ({procedure}).");
                }
            });
        });
    }

    /// <summary>
    /// Every part-level and body-level finding the panel draws, every category chip, and the panel's own
    /// fixed strings.
    /// </summary>
    [Test]
    public async Task EveryConditionAndCategoryHasAdviceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var required = new List<string>(Chrome);

            foreach (var condition in WolfmedTreatmentAdvice.Conditions)
            {
                required.Add(WolfmedTreatmentAdvice.ConditionShortKey(condition));
            }

            foreach (var condition in WolfmedTreatmentAdvice.MechanicalConditions)
            {
                required.Add(WolfmedTreatmentAdvice.ConditionShortKey(condition) +
                             WolfmedTreatmentAdvice.MechanicalSuffix);
            }

            foreach (var category in WolfmedWoundCategories.All)
                required.Add(WolfmedTreatmentAdvice.CategoryShortKey(category));

            Assert.Multiple(() =>
            {
                foreach (var key in required)
                    Assert.That(locale.HasString(key), Is.True, $"missing treatment advice: {key}");

                // The enum members the panel turns into condition names have to land on keys that exist,
                // which is the half a hardcoded list cannot catch on its own.
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.FunctionalityCondition(BodyPartFunctionalityState.Impaired)));
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.FunctionalityCondition(BodyPartFunctionalityState.Disabled)));
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.FunctionalityCondition(BodyPartFunctionalityState.Unavailable)));
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.InfectionCondition(WolfmedInfectionStage.Local)));
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.InfectionCondition(WolfmedInfectionStage.Spreading)));
                Assert.That(WolfmedTreatmentAdvice.Conditions,
                    Does.Contain(WolfmedTreatmentAdvice.InfectionCondition(WolfmedInfectionStage.Septic)));
            });
        });
    }

    /// <summary>
    /// The payload half. The panel can only look advice up if the row names the prototype it came from,
    /// so the analyzer builder has to stamp it.
    /// </summary>
    [Test]
    public async Task VisibleWoundsCarryTheirPrototypeIdTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = entities.System<SharedBodySystem>().GetBodyChildren(body).Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;

            Assert.That(entities.System<WoundSystem>()
                .CreateOrMergeWound(arm, "WFWolfmedGunshotWound", 20), Is.Not.Null);

            var diagnostics = entities.System<HealthAnalyzerSystem>().BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);

            var row = diagnostics!.Parts[TargetBodyPart.LeftArm].VisibleWounds.Single();
            Assert.Multiple(() =>
            {
                Assert.That(row.Prototype, Is.EqualTo("WFWolfmedGunshotWound"),
                    "the row has to name its prototype or the panel cannot find its advice.");
                Assert.That(WolfmedTreatmentAdvice.ShortKey(row.Prototype),
                    Is.EqualTo("wolfmed-treatment-short-wolfmed-gunshot-wound"));
                Assert.That(WolfmedTreatmentAdvice.ProcedureId(row.Prototype, false),
                    Is.EqualTo("WFWolfmedGunshotWound"));
            });
        });
    }
}
