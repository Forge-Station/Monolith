using System.Linq;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Server.Body.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>
/// Playtest 5: "won't fix infections even with antibiotics". The pod only ever pushed an antibiotic as the dose after a
/// surgery; an infected patient with Spaceacillin in the reservoir got none. The course runs beside the queue the way
/// the blood reservoir does: the planner's step starts it, the autofix module watches it, and with an infected occupant
/// and nothing usable loaded the pod says NO ANTIBIOTIC LOADED once.
/// </summary>
public sealed partial class AutodocSystem
{
    [Dependency] private WolfmedInfectionSystem _infection = default!;

    private float _antibioticDose = 5f;
    private float _antibioticInterval = 30f;
    private float _antibioticCourse = 40f;

    /// <summary>Seconds between the course's looks at the patient.</summary>
    private const float AntibioticCheck = 1f;

    private void InitializeAntibiotics()
    {
        Subs.CVar(_cfg, WolfmedCVars.PodAntibioticDose, value => _antibioticDose = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.PodAntibioticInterval, value => _antibioticInterval = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.PodAntibioticCourse, value => _antibioticCourse = MathF.Max(0f, value), true);
    }

    /// <summary>A course is running on the occupant.</summary>
    public bool AntibioticsRunning(Entity<AutodocComponent> ent) =>
        TryComp(ent, out WolfmedAutodocAntibioticComponent? course) && course.Active && course.Patient == GetOccupant(ent);

    /// <summary>The occupant is infected and nothing usable is loaded.</summary>
    public bool AntibioticFault(Entity<AutodocComponent> ent) =>
        TryComp(ent, out WolfmedAutodocAntibioticComponent? course) && course.NoAntibiotic && course.Patient == GetOccupant(ent);

    /// <summary>
    /// The planner's antibiotic step: an infected occupant starts the course, or, with nothing usable loaded, the fault
    /// is raised and said once. A course already spent on this occupant is not started again.
    /// </summary>
    public void ScheduleAntibiotics(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!TryComp(ent, out WolfmedAutodocAntibioticComponent? course))
            return;

        SyncAntibioticPatient(course, body);
        if (!_infection.HasInfection(body) || course.Given >= _antibioticCourse)
        {
            if (course.NoAntibiotic)
            {
                course.NoAntibiotic = false;
                UpdateUi(ent);
            }

            return;
        }

        if (!HasReservoirAntibiotic(ent))
        {
            if (!course.NoAntibioticSaid)
            {
                course.NoAntibioticSaid = true;
                Speak(ent, AutodocVoiceEvent.NoAntibiotic);
            }

            if (!course.NoAntibiotic)
            {
                course.NoAntibiotic = true;
                UpdateUi(ent);
            }

            return;
        }

        course.NoAntibiotic = false;
        if (course.Active)
            return;

        course.Active = true;
        course.NextUpdate = _timing.CurTime;
        course.NextDose = _timing.CurTime;
        Speak(ent, AutodocVoiceEvent.Antibiotics);
        UpdateUi(ent);
    }

    /// <summary>A new occupant starts with no course and no fault.</summary>
    private static void SyncAntibioticPatient(WolfmedAutodocAntibioticComponent course, EntityUid? body)
    {
        if (course.Patient == body)
            return;

        course.Patient = body;
        course.Active = false;
        course.NoAntibiotic = false;
        course.NoAntibioticSaid = false;
        course.Given = 0f;
        course.NextDose = TimeSpan.Zero;
    }

    /// <summary>
    /// The course's steady look, alongside whatever else the pod is doing: a dose every interval while the occupant
    /// is still infected and the course is not spent. Unpowered or emagged, it holds.
    /// </summary>
    private void TickAntibiotics(Entity<AutodocComponent> ent)
    {
        if (!TryComp(ent, out WolfmedAutodocAntibioticComponent? course))
            return;

        var body = GetOccupant(ent);
        SyncAntibioticPatient(course, body);
        if (body is not { } patient || !IsPowered(ent) || IsEmagged(ent) || _timing.CurTime < course.NextUpdate)
            return;

        course.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(AntibioticCheck);

        // A beaker arriving clears the fault's "said once", so an empty reservoir is reported again after it runs dry.
        if (HasReservoirAntibiotic(ent))
            course.NoAntibioticSaid = false;

        // A raised fault is the pod waiting for antibiotic, as it waits for blood; the autofix module watches on its own.
        if (course.NoAntibiotic || ent.Comp.Auto && HasAutofixModule(ent) && !course.Active)
            ScheduleAntibiotics(ent, patient);

        if (!course.Active)
            return;

        if (!_infection.HasInfection(patient) || course.Given >= _antibioticCourse)
        {
            course.Active = false;
            UpdateUi(ent);
            return;
        }

        if (_timing.CurTime < course.NextDose)
            return;

        // Pre-merge review: at the safe line the dose waits for the liver with the course still running. Treating a
        // refused push as an empty reservoir re-armed the course every tick, with its line in chat each time.
        if (!HasAntibioticHeadroom(ent, patient))
        {
            course.NextDose = _timing.CurTime + TimeSpan.FromSeconds(_antibioticInterval);
            return;
        }

        var dose = MathF.Min(_antibioticDose, _antibioticCourse - course.Given);
        if (dose <= 0f || !PushReagent(ent, patient, AutodocReagentRole.Antibiotic, dose))
        {
            // The reservoir ran dry mid-course: the fault, and the course again once something is loaded.
            course.Active = false;
            ScheduleAntibiotics(ent, patient);
            UpdateUi(ent);
            return;
        }

        course.Given += dose;
        course.NextDose = _timing.CurTime + TimeSpan.FromSeconds(_antibioticInterval);
        UpdateUi(ent);
    }

    /// <summary>Whether the patient's blood still has room under the safe line for any antibiotic on the pod's list.</summary>
    private bool HasAntibioticHeadroom(Entity<AutodocComponent> ent, EntityUid patient)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return false;

        _solutions.TryGetSolution(patient, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var chemicals);
        foreach (var entry in list.Reagents)
        {
            if (!entry.AutodocAdministrable || entry.Role != AutodocReagentRole.Antibiotic)
                continue;

            if (entry.SafeUnits <= 0f)
                return true;

            var inBlood = chemicals?.GetTotalPrototypeQuantity(entry.Reagent.Id) ?? FixedPoint2.Zero;
            if (FixedPoint2.New(entry.SafeUnits) - inBlood > FixedPoint2.Zero)
                return true;
        }

        return false;
    }

    /// <summary>Whether a beaker slot holds a reagent the pod's list gives the antibiotic role.</summary>
    private bool HasReservoirAntibiotic(Entity<AutodocComponent> ent)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return false;

        var wanted = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == AutodocReagentRole.Antibiotic)
            .Select(entry => entry.Reagent.Id)
            .ToHashSet();

        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (_slots.GetItemOrNull(ent.Owner, slot) is { } beaker &&
                TryGetReservoirSolution(beaker, out _, out var solution) &&
                solution.Contents.Any(reagent => wanted.Contains(reagent.Reagent.Prototype)))
                return true;
        }

        return false;
    }

    /// <summary>The course or its fault on the status line, after the state.</summary>
    private string AntibioticStatus(Entity<AutodocComponent> ent)
    {
        if (AntibioticFault(ent))
            return "  " + Loc.GetString("wolfmed-autodoc-status-no-antibiotic");

        return AntibioticsRunning(ent) ? "  " + Loc.GetString("wolfmed-autodoc-status-antibiotics") : string.Empty;
    }
}
