// WOLFGATE (AUTODOC): the bodies of the three autodoc hooks in the upstream surgery system, plus the entry
// point the pod drives a step through. Kept here so SharedSurgerySystem.cs and .Steps.cs carry only the
// marked call sites.

using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Shared.Prototypes;

namespace Content.Shared._Shitmed.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    [Dependency] private SharedAutodocSystem _wolfmedAutodoc = default!; // WOLFGATE (AUTODOC)

    /// <summary>Hook body: a body lying in an autodoc is lying on an operating platform.</summary>
    private bool WolfmedOnOperatingPlatform(EntityUid body) => _wolfmedAutodoc.OnOperatingPlatform(body);

    /// <summary>
    /// Hook body: a performer with no hands answers with its own toolset. Returns null for everybody else,
    /// which leaves the held-items path exactly as it was.
    /// </summary>
    private List<EntityUid>? WolfmedInternalTools(EntityUid surgeon)
    {
        var ev = new WolfmedSurgeryToolsEvent();
        RaiseLocalEvent(surgeon, ref ev);
        return ev.Tools;
    }

    /// <summary>
    /// Performs one surgery step on behalf of a performer that cannot run a do-after (the autodoc). Every
    /// validity check the hand-driven path makes still runs, so the step has the same effect it would by hand.
    /// </summary>
    public bool WolfmedPerformStep(EntityUid user, EntityUid body, EntityUid targetPart, EntProtoId surgeryId, EntProtoId stepId)
    {
        if (!IsSurgeryValid(body, targetPart, surgeryId, stepId, user, out var surgery, out var part, out var step) ||
            !PreviousStepsComplete(body, part, surgery, stepId) ||
            !CanPerformStep(user, body, part, step, false))
            return false;

        var ev = new SurgeryStepEvent(user, body, part, GetTools(user), surgery);
        RaiseLocalEvent(step, ref ev);
        RefreshUI(body);
        return true;
    }

    /// <summary>Why a step cannot be performed right now, for the pod's Waiting state.</summary>
    public bool WolfmedCanPerformStep(EntityUid user, EntityUid body, EntityUid part, EntityUid step, out StepInvalidReason reason)
    {
        return CanPerformStep(user, body, part, step, false, out _, out reason, out _);
    }

    /// <summary>Whether one step of a named surgery reads as done on this part.</summary>
    public bool WolfmedIsStepComplete(EntityUid body, EntityUid part, EntProtoId stepId, EntProtoId surgeryId)
    {
        return GetSingleton(surgeryId) is { } surgeryEnt && IsStepComplete(body, part, stepId, surgeryEnt);
    }

    /// <summary>Base duration of a step, before the pod's own speed multiplier.</summary>
    public float WolfmedStepDuration(EntityUid step) =>
        TryComp(step, out SurgeryStepComponent? comp) ? comp.Duration : 2f;

    /// <summary>True when the surgery's conditions currently allow it on this part.</summary>
    public bool WolfmedSurgeryValid(EntityUid body, EntityUid part, EntProtoId surgeryId)
    {
        if (GetSingleton(surgeryId) is not { } surgeryEnt)
            return false;

        var ev = new SurgeryValidEvent(body, part);
        RaiseLocalEvent(surgeryEnt, ref ev);
        return !ev.Cancelled;
    }
}
