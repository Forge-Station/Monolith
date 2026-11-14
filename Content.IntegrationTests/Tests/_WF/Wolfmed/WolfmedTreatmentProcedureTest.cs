#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// UI4: the procedure prototypes behind the analyzer's treatment window. The window draws whatever the
/// prototype says, so a missing procedure is an empty window, a mistyped tool is an error sprite, and a
/// mistyped locale key is a raw key on screen. All three are caught here rather than in game.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedTreatmentProcedurePrototype))]
public sealed class WolfmedTreatmentProcedureTest : GameTest
{
    /// <summary>
    /// Every wound prototype the game ships has a procedure. It iterates the prototypes rather than a list,
    /// so a wound added in a later phase fails here until somebody writes what to do about it.
    /// </summary>
    [Test]
    public async Task EveryWoundHasAProcedureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>().OrderBy(wound => wound.ID))
                {
                    var id = WolfmedTreatmentAdvice.ProcedureId(wound.ID, false);
                    Assert.That(prototypes.HasIndex<WolfmedTreatmentProcedurePrototype>(id), Is.True,
                        $"{wound.ID} has no treatment procedure ({id}).");
                }
            });
        });
    }

    /// <summary>
    /// Every part-level and body-level finding the wounds tab draws is clickable, so every one of them
    /// needs a procedure too, including the chassis variants of the three that read differently.
    /// </summary>
    [Test]
    public async Task EveryConditionHasAProcedureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var required = new List<string>();
            foreach (var condition in WolfmedTreatmentAdvice.Conditions)
                required.Add(WolfmedTreatmentAdvice.ConditionProcedureId(condition, false));

            foreach (var condition in WolfmedTreatmentAdvice.MechanicalConditions)
                required.Add(WolfmedTreatmentAdvice.ConditionProcedureId(condition, true));

            Assert.Multiple(() =>
            {
                foreach (var id in required)
                {
                    Assert.That(prototypes.HasIndex<WolfmedTreatmentProcedurePrototype>(id), Is.True,
                        $"condition procedure {id} is missing.");
                }
            });
        });
    }

    /// <summary>
    /// Every row of every procedure: the text and the summary resolve, the tool is a real entity, the
    /// reagent is a real reagent, and no procedure is empty.
    /// </summary>
    [Test]
    public async Task ProcedureRowsResolveTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var procedure in prototypes
                             .EnumeratePrototypes<WolfmedTreatmentProcedurePrototype>()
                             .OrderBy(procedure => procedure.ID))
                {
                    Assert.That(locale.HasString(procedure.Summary), Is.True,
                        $"{procedure.ID} summary {procedure.Summary} does not resolve.");
                    Assert.That(procedure.Steps, Is.Not.Empty, $"{procedure.ID} has no steps.");

                    foreach (var line in procedure.Avoid)
                    {
                        Assert.That(locale.HasString(line), Is.True,
                            $"{procedure.ID} avoid line {line} does not resolve.");
                    }

                    foreach (var step in procedure.Steps)
                    {
                        Assert.That(locale.HasString(step.Text), Is.True,
                            $"{procedure.ID} step {step.Text} does not resolve.");

                        if (step.Tool is { } tool)
                        {
                            Assert.That(prototypes.HasIndex<EntityPrototype>(tool), Is.True,
                                $"{procedure.ID} step {step.Text} names a tool that does not exist ({tool}).");
                        }

                        if (step.Reagent is { } reagent)
                        {
                            Assert.That(prototypes.HasIndex<ReagentPrototype>(reagent), Is.True,
                                $"{procedure.ID} step {step.Text} names a reagent that does not exist ({reagent}).");
                        }

                        Assert.That(step.Tool == null || step.Reagent == null, Is.True,
                            $"{procedure.ID} step {step.Text} names both a tool and a reagent; only one is drawn.");
                    }
                }
            });
        });
    }

    /// <summary>
    /// The wounds both flesh and a chassis can carry need both procedures. The dual-carrier set is
    /// re-derived from the body part profiles rather than trusted, exactly as the tooltip test does it, so
    /// a profile change fails here instead of leaving an IPC told to apply ointment.
    /// </summary>
    [Test]
    public async Task DualCarrierWoundsHaveBothProceduresTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
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
                    "the wounds both flesh and a chassis carry have changed; update MechanicalWounds and the procedures.");

                foreach (var id in dual)
                {
                    foreach (var mechanical in new[] { false, true })
                    {
                        var procedure = WolfmedTreatmentAdvice.ProcedureId(id, mechanical);
                        Assert.That(prototypes.HasIndex<WolfmedTreatmentProcedurePrototype>(procedure), Is.True,
                            $"{id} is carried by both, but {procedure} is missing.");
                    }
                }
            });
        });
    }

    /// <summary>
    /// The derivation the panel and the prototypes have to agree on. A change to either naming scheme
    /// silently stops the window finding anything, so it is pinned rather than inferred.
    /// </summary>
    [Test]
    public void ProcedureIdsAreDerivedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WolfmedTreatmentAdvice.ProcedureId("WFWolfmedGunshotWound", false),
                Is.EqualTo("WFWolfmedGunshotWound"));
            Assert.That(WolfmedTreatmentAdvice.ProcedureId("ElectricalWound", true),
                Is.EqualTo("ElectricalWoundMechanical"));
            Assert.That(WolfmedTreatmentAdvice.ConditionProcedureId("internal-bleeding", false),
                Is.EqualTo("CondInternalBleeding"));
            Assert.That(WolfmedTreatmentAdvice.ConditionProcedureId("bleeding", true),
                Is.EqualTo("CondBleedingMechanical"));
            Assert.That(WolfmedTreatmentAdvice.ConditionProcedureId("infection-septic", false),
                Is.EqualTo("CondInfectionSeptic"));
        });
    }
}
