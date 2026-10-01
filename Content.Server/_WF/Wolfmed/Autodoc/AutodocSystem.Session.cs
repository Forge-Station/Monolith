using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>
/// Playtest 3 SAM: one AUTO run is one run. The module plans once and says so once; when the queue drains it plans
/// again straight away, silently, and only a plan that comes back empty ends the run with QUEUE COMPLETE. Where the
/// planner can already see what the pod's own work will leave behind, it queues that too, as a follow-up.
/// </summary>
public sealed partial class AutodocSystem
{
    /// <summary>
    /// Called where a queue would finish. Inside an AUTO run the pod plans again instead; true when there was more
    /// to do and the run goes on, false at the real end (or when the run can no longer go on), which the caller
    /// announces as usual.
    /// </summary>
    private bool ContinueAutoRun(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!ent.Comp.AutoSession)
            return false;

        if (!ent.Comp.Auto || !HasAutofixModule(ent) || IsEmagged(ent) || !IsPowered(ent) ||
            ent.Comp.AbortRequested || AutoPlan(ent, body) == 0)
        {
            EndAutoRun(ent);
            return false;
        }

        ent.Comp.CurrentStep = null;
        ent.Comp.State = AutodocState.Preparing;
        CloseTray(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
        return true;
    }

    /// <summary>
    /// The run is over. QUEUE COMPLETE is its last word, so the module's next empty look stays quiet: "NOTHING MORE I
    /// CAN DO" is for a patient it never found anything on.
    /// </summary>
    private void EndAutoRun(Entity<AutodocComponent> ent)
    {
        if (!ent.Comp.AutoSession)
            return;

        ent.Comp.AutoSession = false;
        ent.Comp.AutoSaidNothing = true;
    }

    /// <summary>A follow-up goes in without the validity check: it is queued for a state the part is not in yet.</summary>
    private void QueueFollowUp(Entity<AutodocComponent> ent, EntProtoId surgery, TargetBodyPart part, bool ignorePodWounds)
    {
        if (GetOccupant(ent) is not { } body || ResolvePart(body, part) == null || !IsKnown(ent, surgery))
            return;

        ent.Comp.Queue.Add(new AutodocQueued
        {
            Surgery = surgery,
            Part = part,
            Requirements = BuildRequirements(ent, surgery),
            FollowUp = true,
            IgnorePodWounds = ignorePodWounds,
        });
    }

    /// <summary>
    /// Tending only lowers a wound. When a tend of one severity window is planned on a part, the tend of the window
    /// below it on the same group is where that part goes next: queued now, not found by the next plan.
    /// </summary>
    private void PlanTendFollowUps(Entity<AutodocComponent> ent,
        EntProtoId surgery,
        bool ignorePodWounds,
        List<AutodocProcedureEntry> result,
        HashSet<(string, TargetBodyPart)> taken,
        Dictionary<(string Surgery, TargetBodyPart Part), bool>? followUps)
    {
        if (TendWindow(surgery) is not { MaxWoundSeverity: { } max } lower)
            return;

        foreach (var planned in result.ToArray())
        {
            if (TendWindow(planned.Surgery) is not { MinWoundSeverity: { } min } upper ||
                upper.WoundGroup != lower.WoundGroup || max >= min ||
                !IsKnown(ent, surgery) ||
                ent.Comp.FailedProcedures.Contains((surgery.Id, planned.Part)) ||
                !CanPlanWithoutHelp(ent, surgery) ||
                !taken.Add((surgery.Id, planned.Part)))
                continue;

            result.Add(new AutodocProcedureEntry(surgery, planned.Part, true));
            if (followUps != null)
                followUps[(surgery.Id, planned.Part)] = ignorePodWounds;
        }
    }

    /// <summary>The wound-severity window a tend surgery lists in, or null for anything else.</summary>
    private SurgeryWoundedConditionComponent? TendWindow(EntProtoId surgery)
    {
        return _surgery.GetSingleton(surgery) is { } surgeryEnt &&
               TryComp(surgeryEnt, out SurgeryWoundedConditionComponent? window)
            ? window
            : null;
    }

    /// <summary>Drops stale follow-ups at the head of the queue; true when that emptied it and ended the run.</summary>
    // Stale: the surgery no longer lists, the pod has given up on it, the part still has something lodged in it, or
    // (for a step that skips the pod's own handiwork) every wound left on the part is the pod's. No antibiotic and no
    // line, since nothing was done.
    private bool DropStaleFollowUps(Entity<AutodocComponent> ent, EntityUid body)
    {
        var dropped = false;
        while (ent.Comp.Queue.Count > 0 && ent.Comp.Queue[0] is { FollowUp: true } queued && !StillPlannable(ent, body, queued))
        {
            ent.Comp.Queue.RemoveAt(0);
            dropped = true;
        }

        if (!dropped || ent.Comp.Queue.Count > 0)
            return false;

        FinishQueue(ent);
        return true;
    }

    private bool StillPlannable(Entity<AutodocComponent> ent, EntityUid body, AutodocQueued queued)
    {
        return ResolvePart(body, queued.Part) is { } part &&
               _surgery.WolfmedSurgeryValid(body, part, queued.Surgery) &&
               !ent.Comp.FailedProcedures.Contains((queued.Surgery.Id, queued.Part)) &&
               !(queued.IgnorePodWounds && PodWoundsOnly(body, queued.Part)) &&
               !IsBlockedByEmbedded(ent, body, new AutodocProcedureEntry(queued.Surgery, queued.Part, true));
    }

    /// <summary>
    /// A round the pod pulled out leaves its wound as a new entity. Without this the pod marked that wound its own,
    /// the triage's ignorePodWounds steps skipped the part, and the run ended with the gunshot untreated.
    /// </summary>
    private void OnWoundReplaced(Content.Server._WF.Wolfmed.Wounds.WolfmedWoundReplacedEvent args)
    {
        var query = EntityQueryEnumerator<AutodocComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (!comp.PreProcedureWounds.Remove(args.Old))
                continue;

            if (args.PodMade)
                EnsureComp<WolfmedPodWoundComponent>(args.Replacement);
            else
                comp.PreProcedureWounds.Add(args.Replacement);
        }
    }

    /// <summary>Every voice event raised for this occupant, for the tests: spoken, queued or dropped alike.</summary>
    private void CountVoice(Entity<AutodocComponent> ent, AutodocVoiceEvent voiceEvent)
    {
        ent.Comp.VoiceEvents[voiceEvent] = ent.Comp.VoiceEvents.GetValueOrDefault(voiceEvent) + 1;
    }
}
