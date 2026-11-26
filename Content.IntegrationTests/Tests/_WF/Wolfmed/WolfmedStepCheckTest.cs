#nullable enable
using System;
using System.Collections.Generic;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.FixedPoint;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// UI4: the truth table for the procedure window's completion checks. The evaluator is pure, so this needs
/// no server: it is the half that decides whether a medic sees a step greyed out, and getting it wrong
/// means telling somebody a wound is dressed when it is not.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedStepChecks))]
public sealed class WolfmedStepCheckTest
{
    /// <summary>A part on which no check holds: bleeding, broken, infected, hot, dying and untouched.</summary>
    private static HealthAnalyzerWoundDiagnostic Bad() => Clean() with
    {
        Fracture = FractureGrade.Displaced,
        FractureTreatment = FractureTreatment.None,
        BleedingRate = 5f,
        InternalBleedingRate = 3f,
        EmbeddedObjects = 2,
        Infection = WolfmedInfectionStage.Spreading,
        NecrosisRisk = true,
        Overheating = true,
        Functionality = BodyPartFunctionalityState.Disabled,
        Treatments = WolfmedPartTreatments.None,
        MissingOrgans = 2,
    };

    /// <summary>A part on which every part-level check holds.</summary>
    private static HealthAnalyzerWoundDiagnostic Good() => Clean() with
    {
        Treatments = WolfmedPartTreatments.Bandaged | WolfmedPartTreatments.Tourniqueted |
                     WolfmedPartTreatments.Sutured | WolfmedPartTreatments.Cauterized |
                     WolfmedPartTreatments.Cleaned,
    };

    private static HealthAnalyzerWoundDiagnostic Clean() => new(
        FractureGrade.None,
        FractureTreatment.None,
        0f,
        BleedingTreatment.None,
        0,
        FixedPoint2.Zero,
        new List<HealthAnalyzerVisibleWound>(),
        BodyPartFunctionalityState.Functional,
        0f,
        HealthAnalyzerClottingPhase.NotApplicable);

    /// <summary>
    /// Every member of the enum, both ways round. A check added without a case in the evaluator returns
    /// false for everything and fails the "done" half here.
    /// </summary>
    [Test]
    public void EveryCheckSeparatesDoneFromNotDoneTest()
    {
        // BRAIN adds the two body-level vitals: a stopped heart with a dead brain on the pending side.
        var pending = new WolfmedProcedureState(Bad(), false, true, 40f, 0.3f, true, 0f);
        var done = new WolfmedProcedureState(Good(), true, false, 0f, 1f, false, 1f);

        Assert.Multiple(() =>
        {
            foreach (WolfmedStepCheck check in Enum.GetValues(typeof(WolfmedStepCheck)))
            {
                Assert.That(WolfmedStepChecks.IsDone(check, pending), Is.False,
                    $"{check} reads done on a patient nothing has been done to.");
                Assert.That(WolfmedStepChecks.IsDone(check, done), Is.True,
                    $"{check} never reads done, so its step could never grey.");
            }
        });
    }

    /// <summary>The cases where "done" does not mean an item was used, only that there is nothing left to do.</summary>
    [Test]
    public void NothingLeftToDoCountsAsDoneTest()
    {
        Assert.Multiple(() =>
        {
            // Gauze leaves a quarter of the flow, so "dressed" cannot be read off the rate alone; a part
            // that has stopped bleeding on its own still has nothing to dress.
            Assert.That(IsDone(WolfmedStepCheck.Dressed, Clean()), Is.True);
            Assert.That(IsDone(WolfmedStepCheck.Dressed, Clean() with { BleedingRate = 2f }), Is.False);
            Assert.That(IsDone(WolfmedStepCheck.Dressed,
                    Clean() with { BleedingRate = 2f, Treatments = WolfmedPartTreatments.Bandaged }),
                Is.True);

            // A splint sets the bone without mending it; a part with no break at all needs neither.
            var splinted = Clean() with
            {
                Fracture = FractureGrade.Simple,
                FractureTreatment = FractureTreatment.Reduced,
            };
            Assert.That(IsDone(WolfmedStepCheck.FractureReduced, splinted), Is.True);
            Assert.That(IsDone(WolfmedStepCheck.FractureMended, splinted), Is.False);
            Assert.That(IsDone(WolfmedStepCheck.FractureReduced, Clean()), Is.True);

            // A cautery closes a wound just as sutures do, and dead tissue is past risk, not clear of it.
            Assert.That(IsDone(WolfmedStepCheck.Sutured,
                Clean() with { Treatments = WolfmedPartTreatments.Cauterized }), Is.True);
            Assert.That(IsDone(WolfmedStepCheck.Sutured,
                Clean() with { Treatments = WolfmedPartTreatments.Bandaged }), Is.False);
            Assert.That(IsDone(WolfmedStepCheck.NecrosisRiskCleared, Clean() with { Necrotic = true }), Is.False);
        });
    }

    /// <summary>
    /// The two banner procedures have no part card behind them, so every part-level check has to read false
    /// rather than grey a row on nothing.
    /// </summary>
    [Test]
    public void BodyLevelChecksWorkWithoutAPartTest()
    {
        var state = new WolfmedProcedureState(null, false, true, 0f, 1f, false, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.SepsisCleared, state), Is.True);
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.BloodRestored, state), Is.True);
            // BRAIN: the arrest and brain-death procedures are banner-level too.
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.PulseRestored, state), Is.True);
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.BrainRepaired, state), Is.True);
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.BleedingStopped, state), Is.False);
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.FractureMended, state), Is.False);

            // A body with no bloodstream reports NaN; that is not a reason to nag about a bloodpack.
            Assert.That(WolfmedStepChecks.IsDone(WolfmedStepCheck.BloodRestored,
                new WolfmedProcedureState(null, false, true, 0f, float.NaN)), Is.True);
        });
    }

    /// <summary>A step greys only when all of its checks hold, and a step with none never greys alone.</summary>
    [Test]
    public void AllChecksMustHoldTest()
    {
        var state = new WolfmedProcedureState(Clean(), true, true, 0f, 1f);
        var both = new List<WolfmedStepCheck>
        {
            WolfmedStepCheck.PartTargeted,
            WolfmedStepCheck.BleedingStopped,
        };
        var mixed = new List<WolfmedStepCheck>
        {
            WolfmedStepCheck.PartTargeted,
            WolfmedStepCheck.SubjectGone,
        };

        Assert.Multiple(() =>
        {
            Assert.That(WolfmedStepChecks.IsDone(both, state), Is.True);
            Assert.That(WolfmedStepChecks.IsDone(mixed, state), Is.False);
            Assert.That(WolfmedStepChecks.IsDone(new List<WolfmedStepCheck>(), state), Is.False);
        });
    }

    /// <summary>Every bleeding treatment maps to its own flag, and the untreated case to none.</summary>
    [Test]
    public void BleedingTreatmentsMapToFlagsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WolfmedStepChecks.Flag(BleedingTreatment.None),
                Is.EqualTo(WolfmedPartTreatments.None));
            Assert.That(WolfmedStepChecks.Flag(BleedingTreatment.Bandaged),
                Is.EqualTo(WolfmedPartTreatments.Bandaged));
            Assert.That(WolfmedStepChecks.Flag(BleedingTreatment.Clamped),
                Is.EqualTo(WolfmedPartTreatments.Tourniqueted));
            Assert.That(WolfmedStepChecks.Flag(BleedingTreatment.Sutured),
                Is.EqualTo(WolfmedPartTreatments.Sutured));
            Assert.That(WolfmedStepChecks.Flag(BleedingTreatment.Cauterized),
                Is.EqualTo(WolfmedPartTreatments.Cauterized));
        });
    }

    private static bool IsDone(WolfmedStepCheck check, HealthAnalyzerWoundDiagnostic part) =>
        WolfmedStepChecks.IsDone(check, new WolfmedProcedureState(part, false, true, 0f, 1f));
}
