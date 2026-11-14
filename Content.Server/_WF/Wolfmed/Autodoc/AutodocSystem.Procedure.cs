using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Effects.Step;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared.Chat;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    /// <summary>The surgery whose requirement chain means the body gets opened, so anaesthesia is wanted.</summary>
    private static readonly EntProtoId OpenIncision = "SurgeryOpenIncision";

    /// <summary>Step families the step's own components and tools cannot tell apart.</summary>
    private static readonly Dictionary<string, AutodocVoiceEvent> StepVoiceOverrides = new()
    {
        ["SurgeryStepCloseIncision"] = AutodocVoiceEvent.StepClose,
        ["SurgeryStepSealWounds"] = AutodocVoiceEvent.StepClose,
        ["SurgeryStepSealTendWound"] = AutodocVoiceEvent.StepClose,
        ["WFSurgeryStepCloseEvisceration"] = AutodocVoiceEvent.StepEvisceration,
        ["WFSurgeryStepClampEvisceration"] = AutodocVoiceEvent.StepEvisceration,
        // Both are hemostat steps, which would otherwise announce themselves as clamping.
        ["WFSurgeryStepExtractEmbedded"] = AutodocVoiceEvent.StepEmbedded,
        ["WFSurgeryStepRelocateJoint"] = AutodocVoiceEvent.StepRelocate,
    };

    /// <summary>The surgery that puts a patient back together after an abandoned procedure.</summary>
    private static readonly EntProtoId CloseIncision = "SurgeryCloseIncision";

    /// <summary>The one procedure that may be planned on a part that still has something stuck in it.</summary>
    public static readonly EntProtoId RemoveEmbedded = "WFSurgeryRemoveEmbeddedObjects";

    /// <summary>The defibrillator refusal the pod can do something about on its own.</summary>
    private const string NoBlood = "wolfmed-defib-no-blood";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AutodocComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);
            TickVoice(ent);
            TickOxygen(ent);
            TickEmag(ent);
            TickAuto(ent);
            TickDefib(ent);
            TickAlarm(ent);
            TickIdleChatter(ent);
            TickBlood(ent); // Playtest 4, IV: the blood reservoir's steady transfusion
            TickAntibiotics(ent); // Playtest 5: the antibiotic course

            switch (comp.State)
            {
                case AutodocState.Preparing:
                    Prepare(ent);
                    break;
                case AutodocState.Step:
                    TickStep(ent, frameTime);
                    break;
                case AutodocState.Waiting:
                    TickWaiting(ent);
                    break;
            }

            TickPodWounds(ent);
            TickLeftovers(ent); // Playtest 5
            SweepTile(ent); // Playtest 5
        }
    }

    #region Library

    /// <summary>True when the pod's base library or the disk in its slot carries this surgery.</summary>
    public bool IsKnown(Entity<AutodocComponent> ent, EntProtoId surgery)
    {
        foreach (var program in ent.Comp.BasePrograms)
        {
            if (_protos.TryIndex(program, out var proto) && proto.Surgeries.Contains(surgery))
                return true;
        }

        return CurrentDisk(ent) is { } disk &&
               _protos.TryIndex(disk.Program, out var diskProto) &&
               diskProto.Surgeries.Contains(surgery);
    }

    public AutodocProgramDiskComponent? CurrentDisk(Entity<AutodocComponent> ent) =>
        _slots.GetItemOrNull(ent.Owner, AutodocComponent.DiskSlotId) is { } disk &&
        TryComp(disk, out AutodocProgramDiskComponent? comp)
            ? comp
            : null;

    public bool HasDefibModule(Entity<AutodocComponent> ent) =>
        _slots.GetItemOrNull(ent.Owner, AutodocComponent.ModuleSlotId) is { } module &&
        HasComp<AutodocDefibModuleComponent>(module);

    /// <summary>Every surgery the occupant's condition currently allows, part by part.</summary>
    public List<AutodocProcedureEntry> GetAvailable(Entity<AutodocComponent> ent, EntityUid body)
    {
        var result = new List<AutodocProcedureEntry>();
        foreach (var (partId, part) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(part.PartType, part.Symmetry) is not { } target)
                continue;

            foreach (var surgery in _surgery.AllSurgeries)
            {
                if (!_surgery.WolfmedSurgeryValid(body, partId, surgery))
                    continue;

                result.Add(new AutodocProcedureEntry(surgery, target, IsKnown(ent, surgery)));
            }
        }

        return result;
    }

    #endregion

    #region Queue

    /// <summary>Queues a surgery on a part if the pod knows it and the occupant's body allows it now.</summary>
    public bool TryQueue(Entity<AutodocComponent> ent, EntProtoId surgery, TargetBodyPart part)
    {
        if (GetOccupant(ent) is not { } body || ResolvePart(body, part) is not { } partEnt)
            return false;

        if (!IsKnown(ent, surgery))
        {
            Speak(ent, AutodocVoiceEvent.DiskMissing);
            return false;
        }

        if (!_surgery.WolfmedSurgeryValid(body, partEnt, surgery))
            return false;

        ent.Comp.Queue.Add(new AutodocQueued
        {
            Surgery = surgery,
            Part = part,
            Requirements = BuildRequirements(ent, surgery),
        });
        return true;
    }

    /// <summary>
    /// What the procedure will ask for, worked out once at queue time from the step components of the surgery
    /// and everything it requires.
    /// </summary>
    public List<AutodocRequirement> BuildRequirements(Entity<AutodocComponent> ent, EntProtoId surgery)
    {
        var result = new List<AutodocRequirement>();
        CollectRequirements(surgery, result, 0);

        var proc = _protos.TryIndex<AutodocProcedurePrototype>(surgery, out var procProto) ? procProto : null;
        var opens = OpensBody(surgery, 0);
        var anaesthetic = proc?.Anaesthetic ?? (opens ? ent.Comp.DefaultAnaesthetic : 0f);
        var antibiotic = proc?.Antibiotic ?? (opens ? ent.Comp.DefaultAntibiotic : 0f);

        if (anaesthetic > 0f)
            result.Insert(0, new AutodocRequirement { Kind = AutodocRequirementKind.Reagent, Reagent = nameof(AutodocReagentRole.Anaesthetic), Units = anaesthetic });

        if (antibiotic > 0f)
            result.Add(new AutodocRequirement { Kind = AutodocRequirementKind.Reagent, Reagent = nameof(AutodocReagentRole.Antibiotic), Units = antibiotic });

        return result;
    }

    private void CollectRequirements(EntProtoId surgery, List<AutodocRequirement> result, int depth)
    {
        if (depth > 8 ||
            _surgery.GetSingleton(surgery) is not { } surgeryEnt ||
            !TryComp(surgeryEnt, out SurgeryComponent? comp))
            return;

        if (comp.Requirement is { } requirement)
            CollectRequirements(requirement, result, depth + 1);

        foreach (var stepId in comp.Steps)
        {
            if (_surgery.GetSingleton(stepId) is not { } stepEnt)
                continue;

            if (HasComp<SurgeryAddPartStepComponent>(stepEnt) &&
                TryComp(surgeryEnt, out SurgeryPartRemovedConditionComponent? removed))
            {
                result.Add(new AutodocRequirement
                {
                    Kind = AutodocRequirementKind.Part,
                    PartType = removed.Part,
                    Symmetry = removed.Symmetry,
                });
            }
            else if (HasComp<SurgeryAddOrganStepComponent>(stepEnt) &&
                     TryComp(surgeryEnt, out SurgeryOrganConditionComponent? organ) &&
                     organ.Organ is { } registry &&
                     registry.Count > 0)
            {
                result.Add(new AutodocRequirement
                {
                    Kind = AutodocRequirementKind.Organ,
                    Component = registry.Keys.First(),
                });
            }
            else if (TryComp(stepEnt, out SurgeryStepCavityEffectComponent? cavity) && cavity.Action == "Insert")
            {
                result.Add(new AutodocRequirement { Kind = AutodocRequirementKind.Item });
            }
        }
    }

    /// <summary>True when the surgery opens the body, which is what buys the default anaesthetic dose.</summary>
    private bool OpensBody(EntProtoId surgery, int depth)
    {
        if (surgery == OpenIncision)
            return true;

        if (depth > 8 ||
            _surgery.GetSingleton(surgery) is not { } ent ||
            !TryComp(ent, out SurgeryComponent? comp) ||
            comp.Requirement is not { } requirement)
            return false;

        return OpensBody(requirement, depth + 1);
    }

    #endregion

    #region State machine

    /// <summary>Starts the queued run on the occupant, or resumes a paused one; false when it can't.</summary>
    public bool TryStart(Entity<AutodocComponent> ent, EntityUid? user)
    {
        if (ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete or AutodocState.Paused))
            return false;

        if (GetOccupant(ent) is not { } body)
        {
            Speak(ent, AutodocVoiceEvent.NoOccupant);
            return false;
        }

        if (ent.Comp.Queue.Count == 0)
        {
            Speak(ent, AutodocVoiceEvent.QueueEmpty);
            return false;
        }

        if (!IsPowered(ent))
            return false;

        if (ent.Comp.State == AutodocState.Paused)
        {
            ent.Comp.State = AutodocState.Step;
            ent.Comp.PauseRequested = false;
            Speak(ent, AutodocVoiceEvent.Resumed);
            BeginStep(ent);
            return true;
        }

        // Everything the occupant is carrying now is theirs; what turns up from here is the pod's.
        SnapshotWounds(ent, body);

        // A corpse or an arrested patient is operated on normally: repairing a brain and restarting a heart
        // are exactly the procedures that want one. Only a death that happens mid-run stops the pod.
        ent.Comp.OccupantWasDead = _mobState.IsDead(body) || _life.InArrest(body);
        if (ent.Comp.OccupantWasDead)
            Speak(ent, AutodocVoiceEvent.DeadProceeding);

        ent.Comp.Operator = user == body ? null : user;
        ent.Comp.Locked = true;
        ent.Comp.AnaestheticGiven = false;
        ent.Comp.SaidSedationLimit = false;
        ent.Comp.AbortRequested = false;
        ent.Comp.PauseRequested = false;
        ent.Comp.State = AutodocState.Preparing;
        Speak(ent, ent.Comp.SelfService ? AutodocVoiceEvent.SelfService : AutodocVoiceEvent.OperatorStart);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user)} started autodoc {ToPrettyString(ent.Owner)} on {ToPrettyString(body)} with {ent.Comp.Queue.Count} queued procedure(s)");

        UpdateAppearance(ent);
        UpdateUi(ent);
        return true;
    }

    /// <summary>Announce, push the anaesthetic if one is loaded, then start the first step.</summary>
    private void Prepare(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        // Playtest 3 SAM: a follow-up whose moment never came (the deep tend closed everything) goes quietly.
        if (DropStaleFollowUps(ent, body))
            return;

        WarnAboutJunkReagents(ent);

        // Blood first: the shock's own gate wants a bloodstream that can circulate, so a pod that shocked
        // before it transfused refused every patient who arrested from blood loss.
        TryTransfuse(ent, body);
        TryDefibrillateOccupant(ent, body);

        // A fresh procedure gets to announce each family of work once more, and starts the stall guard
        // from nothing: the step counts only mean anything within one procedure.
        ent.Comp.SpokenFamilies.Clear();
        ClearStall(ent);
        SnapshotWounds(ent, body);

        // M3: a closure the pod added to finish the last procedure is part of it; the patient is still under.
        if (ent.Comp.Queue.Count == 0 || !ent.Comp.Queue[0].Continuation)
            MaintainAnaesthesia(ent, body);

        ent.Comp.State = AutodocState.Step;
        BeginStep(ent);
    }

    /// <summary>Keeps one anaesthetic dose in the occupant for the whole queue, topped up as it runs out.</summary>
    // Never past the sedation cap: a full dose per procedure took a long plan to a stopped breath. While the
    // anaesthetic is in them they are asleep, since Shitmed's step emote is the scream and only reads
    // ForcedSleeping.
    private void MaintainAnaesthesia(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!ent.Comp.Anaesthesia)
            return;

        // Somebody already unconscious, arrested or dead is as under as anaesthetic could make them. M1a: a
        // pain faint is not: it ends by itself in seconds, and the patient would wake on the table.
        if (_mobState.IsCritical(body) && !_consciousness.InFaint(body) || _mobState.IsDead(body) ||
            _life.InArrest(body))
            return;

        var dose = QueueAnaestheticDose(ent);
        if (dose <= 0f)
            return;

        if (ent.Comp.AnaestheticGiven && !NeedsTopUp(ent, body))
        {
            Sedate(ent, body);
            return;
        }

        // M2 (plan §3.4): sedation aims for the units in the blood, so the pod pushes only what keeps that aim under
        // its cap. It tops up as the dose is used, the way an anaesthetist runs a drip.
        dose = CapAnaestheticDose(ent, body, dose);
        if (_relief.GetSedation(body) >= _sedationCap || dose < MinAnaestheticPush)
        {
            if (!ent.Comp.SaidSedationLimit)
            {
                ent.Comp.SaidSedationLimit = true;
                Speak(ent, AutodocVoiceEvent.SedationLimit);
            }

            Sedate(ent, body);
            return;
        }

        var pushed = PushReagent(ent, body, AutodocReagentRole.Anaesthetic, dose);
        if (!ent.Comp.AnaestheticGiven)
            Speak(ent, pushed ? AutodocVoiceEvent.Anaesthetic : AutodocVoiceEvent.NoAnaesthetic);

        ent.Comp.AnaestheticGiven = true;
        if (pushed)
            Sedate(ent, body);
    }

    /// <summary>M2: a push smaller than this is not worth making; the target is already at the cap.</summary>
    private const float MinAnaestheticPush = 0.1f;

    /// <summary>
    /// M2 (plan §3.4): the units that keep the occupant's sedation target under the pod's cap, at the strongest
    /// sedation per unit among the anaesthetics it may push. A dose that sedates nothing is not capped.
    /// </summary>
    private float CapAnaestheticDose(Entity<AutodocComponent> ent, EntityUid body, float dose)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return dose;

        var perUnit = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == AutodocReagentRole.Anaesthetic)
            .Select(entry => _relief.SedationPerUnit(entry.Reagent.Id))
            .DefaultIfEmpty(0f)
            .Max();

        if (perUnit <= 0f)
            return dose;

        var room = _sedationCap - _relief.GetSedationTarget(body);
        return MathF.Min(dose, MathF.Max(0f, room / perUnit));
    }

    /// <summary>The largest anaesthetic dose anything in the queue asks for. One dose covers the run.</summary>
    private float QueueAnaestheticDose(Entity<AutodocComponent> ent)
    {
        return ent.Comp.Queue
            .SelectMany(queued => queued.Requirements)
            .Where(r => r.Kind == AutodocRequirementKind.Reagent && r.Reagent == nameof(AutodocReagentRole.Anaesthetic))
            .Select(r => r.Units)
            .DefaultIfEmpty(0f)
            .Max();
    }

    /// <summary>True when the painkiller is about to lapse with the queue still running.</summary>
    private bool NeedsTopUp(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!TryComp(body, out WolfmedPainReliefComponent? relief) || relief.Ends is not { } ends)
            return true;

        return ends - _timing.CurTime < TimeSpan.FromSeconds(ent.Comp.AnaestheticTopUp);
    }

    /// <summary>Holds the occupant asleep for as long as the painkiller in them lasts.</summary>
    private void Sedate(Entity<AutodocComponent> ent, EntityUid body)
    {
        var left = CompOrNull<WolfmedPainReliefComponent>(body)?.Ends is { } ends
            ? ends - _timing.CurTime
            : TimeSpan.FromSeconds(ent.Comp.AnaestheticTopUp);

        if (left <= TimeSpan.Zero)
            return;

        if (_status.TryAddStatusEffect<ForcedSleepingComponent>(body, SleepKey, left, true))
            ent.Comp.Sedated = true;
    }

    /// <summary>Wakes an occupant the pod put under. Called wherever a run ends, however it ends.</summary>
    public void WakeOccupant(Entity<AutodocComponent> ent, EntityUid? occupant = null)
    {
        if (!ent.Comp.Sedated)
            return;

        ent.Comp.Sedated = false;
        if ((occupant ?? GetOccupant(ent)) is { } body && !TerminatingOrDeleted(body))
            _status.TryRemoveStatusEffect(body, SleepKey);
    }

    /// <summary>
    /// The pod counters its own respiratory depression when the reservoir holds something that can: past
    /// the sedation threshold a few units of a dexalin-class chem keeps the patient breathing. With nothing
    /// loaded the vital alarm is all the patient gets.
    /// </summary>
    private void TickOxygen(Entity<AutodocComponent> ent)
    {
        if (!IsRunning(ent) || !IsPowered(ent) || GetOccupant(ent) is not { } body)
            return;

        if (_timing.CurTime < ent.Comp.NextOxygen ||
            _relief.GetSedation(body) <= ent.Comp.OxygenSedation &&
            _relief.GetRespiratoryDepression(body) <= 0f)
            return;

        ent.Comp.NextOxygen = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(1f, ent.Comp.OxygenInterval));
        PushReagent(ent, body, AutodocReagentRole.Oxygen, ent.Comp.OxygenDose);
    }

    private void BeginStep(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        if (ent.Comp.AbortRequested)
        {
            Abort(ent);
            return;
        }

        if (ent.Comp.PauseRequested)
        {
            ent.Comp.State = AutodocState.Paused;
            Speak(ent, AutodocVoiceEvent.Paused);
            UpdateAppearance(ent);
            UpdateUi(ent);
            return;
        }

        if (ent.Comp.Queue.Count == 0)
        {
            FinishQueue(ent);
            return;
        }

        var queued = ent.Comp.Queue[0];
        if (ResolvePart(body, queued.Part) is not { } part ||
            _surgery.GetSingleton(queued.Surgery) is not { } surgeryEnt)
        {
            CompleteProcedure(ent, body);
            return;
        }

        // Pre-merge review: an entry that stopped applying before its first step (the same procedure queued twice, a
        // wound closed by hand) is dropped quietly. It used to fault the pod at that step, and FAULT only cleared on eject.
        if (queued.StepsDone == 0 && !queued.FollowUp && !_surgery.WolfmedSurgeryValid(body, part, queued.Surgery))
        {
            ent.Comp.Queue.RemoveAt(0);
            if (ent.Comp.Queue.Count == 0)
            {
                FinishQueue(ent);
                return;
            }

            BeginStep(ent);
            return;
        }

        if (_surgery.GetNextStep(body, part, surgeryEnt) is not { } next ||
            MetaData(next.Surgery.Owner).EntityPrototype?.ID is not { } owningSurgery)
        {
            CompleteProcedure(ent, body);
            return;
        }

        var stepId = next.Surgery.Comp.Steps[next.Step];
        if (_surgery.GetSingleton(stepId) is not { } stepEnt)
        {
            Fault(ent);
            return;
        }

        if (!_surgery.WolfmedCanPerformStep(ent.Owner, body, part, stepEnt, out var reason))
        {
            switch (reason)
            {
                case StepInvalidReason.MissingTool:
                    EnterWaiting(ent, queued);
                    return;

                // Clothing over the part. The pod has no hands to undress anybody with, so it asks and keeps
                // asking: a patient who climbed in dressed used to fault the machine outright.
                case StepInvalidReason.Armor:
                    EnterBlocked(ent, reason);
                    return;

                default:
                    Fault(ent);
                    return;
            }
        }

        ent.Comp.BlockedReason = null;
        ent.Comp.StepRuns[stepId.Id] = ent.Comp.StepRuns.GetValueOrDefault(stepId.Id) + 1;
        ent.Comp.CurrentStep = stepId;
        ent.Comp.CurrentSurgery = owningSurgery;
        ent.Comp.StepLength = MathF.Max(0.1f, _surgery.WolfmedStepDuration(stepEnt) * GetStepSpeed(ent));
        ent.Comp.StepRemaining = ent.Comp.StepLength;
        ent.Comp.State = AutodocState.Step;
        ent.Comp.Pending = null;

        PlayToolStart(ent, stepEnt);
        SpeakStep(ent, GetStepVoice(stepEnt, stepId));
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void TickStep(Entity<AutodocComponent> ent, float frameTime)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        if (!IsPowered(ent))
            return;

        if (WatchOccupant(ent, body))
            return;

        if (ent.Comp.CurrentStep == null)
        {
            BeginStep(ent);
            return;
        }

        ent.Comp.StepRemaining -= frameTime;
        if (ent.Comp.StepRemaining > 0f)
            return;

        FinishStep(ent, body);
    }

    private void FinishStep(Entity<AutodocComponent> ent, EntityUid body)
    {
        var queued = ent.Comp.Queue.Count > 0 ? ent.Comp.Queue[0] : null;
        if (queued == null || ResolvePart(body, queued.Part) is not { } part)
        {
            ent.Comp.CurrentStep = null;
            BeginStep(ent);
            return;
        }

        if (ent.Comp.CurrentStep is { } stepId && ent.Comp.CurrentSurgery is { } surgeryId)
        {
            RollMalfunction(ent, part);
            var before = PartSignature(part); // Playtest 3 SAM: the stall guard measures this pass, not the last one
            var performed = _surgery.WolfmedPerformStep(ent.Owner, body, part, surgeryId, stepId);
            if (performed)
                queued.StepsDone++;

            // Two ways a procedure ends. Its own last step reads as done - the closing step usually removes
            // the incision its requirement opened, so GetNextStep would otherwise send the pod back to
            // re-open it forever. Or the surgery stops being valid at all, which is what a surgeon sees as
            // the entry leaving the menu once the fracture is mended.
            if (surgeryId == queued.Surgery && IsFinalStep(queued.Surgery, stepId) &&
                _surgery.WolfmedIsStepComplete(body, part, stepId, surgeryId) ||
                !performed && queued.StepsDone > 0)
            {
                ent.Comp.CurrentStep = null;
                CloseTray(ent);

                // M3: a surgery whose problem went away before its closing step (the bleed stopped, the bone
                // mended) stops being valid and skips the seal, which left the pod's incision open. Close it next.
                if (!performed)
                    TryQueueClosure(ent, body, queued, 1);

                CompleteProcedure(ent, body);
                return;
            }

            if (!performed)
            {
                ent.Comp.CurrentStep = null;
                Fault(ent);
                return;
            }

            // The guard against a step that can never finish: its completion check still fails and the part
            // looks exactly as it did after the last run, so nothing the pod is doing is reaching the patient.
            if (NoteStall(ent, part, stepId, _surgery.WolfmedIsStepComplete(body, part, stepId, surgeryId), before))
            {
                ent.Comp.CurrentStep = null;
                CloseTray(ent);
                StallProcedure(ent, body, queued);
                return;
            }
        }

        ent.Comp.CurrentStep = null;
        CloseTray(ent);
        BeginStep(ent);
    }

    /// <summary>
    /// Counts runs of one step that changed nothing the completion check could be waiting for. Three of
    /// them in a row (wolfmed.autodoc_step_retries) and the procedure is hopeless, whatever it thinks it
    /// still wants. A completed step clears the count, so an ordinary repeatable step never trips it.
    /// </summary>
    /// <remarks>
    /// Playtest 3 SAM: a pass counts only when it ran to its end (the caller has it performed), its completion check
    /// said "not complete", and the part reads exactly as it did right before that pass. The first pass of a step used
    /// to count whatever it did, so two idle passes after it were enough.
    /// </remarks>
    private bool NoteStall(Entity<AutodocComponent> ent, EntityUid part, EntProtoId stepId, bool complete, string before)
    {
        if (complete)
        {
            ent.Comp.StallStep = null;
            ent.Comp.StallSignature = null;
            ent.Comp.StallCount = 0;
            return false;
        }

        var signature = PartSignature(part);
        if (signature != before)
        {
            ent.Comp.StallStep = stepId;
            ent.Comp.StallSignature = signature;
            ent.Comp.StallCount = 0;
            return false;
        }

        if (ent.Comp.StallStep == stepId && ent.Comp.StallSignature == signature)
        {
            ent.Comp.StallCount++;
        }
        else
        {
            ent.Comp.StallStep = stepId;
            ent.Comp.StallSignature = signature;
            ent.Comp.StallCount = 1;
        }

        return ent.Comp.StallCount >= _stepRetries;
    }

    private void ClearStall(Entity<AutodocComponent> ent)
    {
        ent.Comp.StallStep = null;
        ent.Comp.StallSignature = null;
        ent.Comp.StallCount = 0;
        ent.Comp.StepRuns.Clear();
        ent.Comp.BlockedReason = null;
    }

    /// <summary>A signature of what the pod can see of a part, used to tell whether a step made progress.</summary>
    // Covers wound prototypes, severities, states and embedded counts, each wound's bleeding severity, the fracture,
    // organ health and the part's own damage. Pain and bleed rates are left out because they drift every tick and
    // would hide a step achieving nothing. Bleeding severity doesn't drift, so a clamp closing a bleed reads as
    // progress.
    public string PartSignature(EntityUid part)
    {
        var signature = new StringBuilder();
        if (TryComp(part, out DamageableComponent? damageable))
            signature.Append(damageable.TotalDamage.Int()).Append(';');

        if (TryComp(part, out WoundableComponent? woundable))
        {
            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                signature.Append(wound.Comp.Prototype.Id).Append(':')
                    .Append(wound.Comp.Severity.Int()).Append(':')
                    .Append((int) wound.Comp.State).Append(':')
                    .Append(CompOrNull<WolfmedEmbeddedObjectComponent>(wound)?.Count ?? 0).Append(':')
                    .Append(CompOrNull<WoundBleedingComponent>(wound)?.BleedingSeverity.Int() ?? -1).Append(';');
            }
        }

        if (_fractures.GetFracture(part) is { } fracture)
            signature.Append((int) fracture.Comp2.Grade).Append(':').Append((int) fracture.Comp2.Treatment).Append(';');

        foreach (var (organ, comp) in _body.GetPartOrgans(part))
        {
            signature.Append(comp.SlotId).Append(':')
                .Append((CompOrNull<WolfmedOrganComponent>(organ)?.Health ?? FixedPoint2.Zero).Int()).Append(';');
        }

        return signature.ToString();
    }

    /// <summary>A signature of the occupant as the planner sees them: which wounds exist and their states.</summary>
    // Severity is left out because it drifts as a wound heals and would read as progress for ever, defeating the
    // re-plan bound. A new wound, a wound closing, a fracture being set or an object coming out all change this.
    public string BodySignature(EntityUid body)
    {
        var signature = new StringBuilder();
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp(part, out WoundableComponent? woundable))
            {
                foreach (var wound in _wounds.GetWounds((part, woundable)))
                {
                    // The pod's own incisions and sutures are not news to the planner.
                    if (HasComp<WolfmedPodWoundComponent>(wound))
                        continue;

                    signature.Append(wound.Comp.Prototype.Id).Append(':')
                        .Append((int) wound.Comp.State).Append(':')
                        .Append(CompOrNull<WolfmedEmbeddedObjectComponent>(wound)?.Count ?? 0).Append(';');
                }
            }

            if (_fractures.GetFracture(part) is { } fracture)
                signature.Append((int) fracture.Comp2.Grade).Append(':').Append((int) fracture.Comp2.Treatment).Append(';');

            foreach (var (organ, comp) in _body.GetPartOrgans(part))
            {
                signature.Append(comp.SlotId).Append(':')
                    .Append((CompOrNull<WolfmedOrganComponent>(organ)?.Health ?? FixedPoint2.Zero).Int() / 5).Append(';');
            }

            signature.Append('|');
        }

        return signature.ToString();
    }

    /// <summary>Every wound the occupant is carrying right now, whoever made it.</summary>
    private IEnumerable<EntityUid> OccupantWounds(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
                yield return wound.Owner;
        }
    }

    /// <summary>Marks the wounds the pod has just made as its own.</summary>
    // Runs every tick during a procedure and for a grace period after: a cautery's burn or an incision lands a tick or
    // two behind the step, and a single unmarked one is enough for the planner to find work and start again.
    private void TickPodWounds(Entity<AutodocComponent> ent)
    {
        if (IsRunning(ent))
            ent.Comp.PodWoundUntil = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0f, ent.Comp.PodWoundGrace));
        else if (_timing.CurTime >= ent.Comp.PodWoundUntil)
            return;

        if (GetOccupant(ent) is { } body)
            MarkPodWounds(ent, body);
    }

    /// <summary>
    /// Playtest 5: "the autodoc won't fix its own mistakes without being removed and reinserted". Everything a run
    /// makes on the occupant is marked the pod's own so the planner does not read a suture as a new problem, and the
    /// marks used to last the whole stay: a burn a cautery left, an incision a stalled procedure left open, stayed
    /// invisible to every plan until the patient was taken out and put back. Once the run is over and the grace
    /// window has passed, the marks come off and the next plan sees the leftovers as the patient's; a bounded number
    /// of times, since a run always leaves something behind.
    /// </summary>
    public void ScheduleLeftoverRelease(Entity<AutodocComponent> ent)
    {
        ent.Comp.LeftoverReleaseAt = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0f, ent.Comp.PodWoundGrace) + 0.25f);
    }

    private void TickLeftovers(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.LeftoverReleaseAt is not { } due || _timing.CurTime < due || IsRunning(ent))
            return;

        ent.Comp.LeftoverReleaseAt = null;
        if (GetOccupant(ent) is not { } body || ent.Comp.LeftoverPasses >= ent.Comp.LeftoverPassLimit)
            return;

        var released = 0;
        foreach (var wound in OccupantWounds(body).ToArray())
        {
            if (RemComp<WolfmedPodWoundComponent>(wound))
                released++;
        }

        if (released == 0)
            return;

        ent.Comp.LeftoverPasses++;
        // The module looks again at once; a hand-run pod shows the leftovers on the next PLAN.
        ent.Comp.AutoSignature = null;
        ent.Comp.AutoNextPlan = TimeSpan.Zero;
        UpdateUi(ent);
    }

    /// <summary>What the patient walked in with. Taken at the start of every procedure.</summary>
    private void SnapshotWounds(Entity<AutodocComponent> ent, EntityUid body)
    {
        ent.Comp.PreProcedureWounds.Clear();
        foreach (var wound in OccupantWounds(body))
            ent.Comp.PreProcedureWounds.Add(wound);
    }

    /// <summary>
    /// Marks everything the procedure added. Surgery cuts the patient open and sews them shut again, so a
    /// run always ends with wounds that were not there before; without this the planner reads them as a new
    /// problem, plans, operates, and leaves another set behind it.
    /// </summary>
    private void MarkPodWounds(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (TerminatingOrDeleted(body))
            return;

        foreach (var wound in OccupantWounds(body))
        {
            if (!ent.Comp.PreProcedureWounds.Contains(wound))
                EnsureComp<WolfmedPodWoundComponent>(wound);
        }

        SnapshotWounds(ent, body);
    }

    /// <summary>
    /// True when every wound on this entry's part is one the pod made. A triage step that treats wounds has
    /// nothing to do there, whatever the surgery menu still lists.
    /// </summary>
    public bool PodWoundsOnly(EntityUid body, TargetBodyPart target)
    {
        if (ResolvePart(body, target) is not { } part || !TryComp(part, out WoundableComponent? woundable))
            return false;

        var any = false;
        foreach (var wound in _wounds.GetWounds((part, woundable)))
        {
            if (!HasComp<WolfmedPodWoundComponent>(wound))
                return false;

            any = true;
        }

        return any;
    }

    /// <summary>
    /// Gives up on a procedure the pod cannot finish: it says so, drops it, remembers it for as long as this
    /// occupant is in the pod, closes them back up if it was the pod that opened them, and carries on with
    /// the next queued item rather than repeating the same step until somebody pulls the lid off.
    /// </summary>
    private void StallProcedure(Entity<AutodocComponent> ent, EntityUid body, AutodocQueued queued, bool speak = true)
    {
        MarkPodWounds(ent, body);
        if (speak) // Playtest 3 SAM: clothing it cannot take off has already had its line
            Speak(ent, AutodocVoiceEvent.Stall);
        ent.Comp.FailedProcedures.Add((queued.Surgery.Id, queued.Part));
        ent.Comp.Queue.Remove(queued);
        ClearStall(ent);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"autodoc {ToPrettyString(ent.Owner)} abandoned {queued.Surgery} on {ToPrettyString(body)}: {(speak ? $"no progress in {_stepRetries} attempts" : "clothing it could not take off")}");

        TryQueueClosure(ent, body, queued);

        if (ent.Comp.Queue.Count == 0)
        {
            FinishQueue(ent);
            return;
        }

        ent.Comp.State = AutodocState.Preparing;
        UpdateUi(ent);
    }

    /// <summary>Queues a closure after an abandoned or cut-short procedure if the patient is open.</summary>
    // Only when already open, since closing an incision lists on an intact body too. The index is 0 for an abandoned
    // procedure, which has already left the queue, and straight after the current one for one about to complete.
    private void TryQueueClosure(Entity<AutodocComponent> ent, EntityUid body, AutodocQueued abandoned, int index = 0)
    {
        if (abandoned.Surgery == CloseIncision ||
            ResolvePart(body, abandoned.Part) is not { } part ||
            !IsKnown(ent, CloseIncision) ||
            !_surgery.WolfmedSurgeryValid(body, part, CloseIncision) ||
            !IsProcedureStarted(body, part, CloseIncision))
            return;

        ent.Comp.Queue.Insert(Math.Min(index, ent.Comp.Queue.Count), new AutodocQueued
        {
            Surgery = CloseIncision,
            Part = abandoned.Part,
            Requirements = BuildRequirements(ent, CloseIncision),
            Continuation = true,
        });
    }

    private bool IsFinalStep(EntProtoId surgery, EntProtoId step)
    {
        return _surgery.GetSingleton(surgery) is { } ent &&
               TryComp(ent, out SurgeryComponent? comp) &&
               comp.Steps.Count > 0 &&
               comp.Steps[^1] == step;
    }

    private void CompleteProcedure(Entity<AutodocComponent> ent, EntityUid body)
    {
        MarkPodWounds(ent, body);
        if (ent.Comp.Queue.Count > 0)
        {
            var queued = ent.Comp.Queue[0];
            var antibiotic = queued.Requirements
                .Where(r => r.Kind == AutodocRequirementKind.Reagent && r.Reagent == nameof(AutodocReagentRole.Antibiotic))
                .Sum(r => r.Units);

            if (antibiotic > 0f)
                PushReagent(ent, body, AutodocReagentRole.Antibiotic, antibiotic);

            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"autodoc {ToPrettyString(ent.Owner)} finished {queued.Surgery} on {ToPrettyString(body)} for operator {ToPrettyString(ent.Comp.Operator)}");

            // AUTODOC5: the part gives up whatever damage the wounds this procedure closed used to carry.
            if (ResolvePart(body, queued.Part) is { } part)
                _damageSync.SyncPart(part);

            ent.Comp.Queue.RemoveAt(0);
        }

        // The queue's last procedure is announced by FinishQueue; the ones before it just run on.
        if (ent.Comp.Queue.Count == 0)
        {
            FinishQueue(ent);
            return;
        }

        ent.Comp.State = AutodocState.Preparing;
        UpdateUi(ent);
    }

    // The same set a hand defibrillator plays, so a pod shock sounds like one.
    private static readonly SoundPathSpecifier DefibChargeSound = new("/Audio/Items/Defib/defib_charge.ogg");
    private static readonly SoundPathSpecifier DefibZapSound = new("/Audio/Items/Defib/defib_zap.ogg");
    private static readonly SoundPathSpecifier DefibSuccessSound = new("/Audio/Items/Defib/defib_success.ogg");
    private static readonly SoundPathSpecifier DefibFailureSound = new("/Audio/Items/Defib/defib_failed.ogg");

    /// <summary>Shocks an arrested or dead occupant on the paddles' rules; true when the heart restarted.</summary>
    // Needs the cardiac module. Runs before the first procedure, between procedures and once the queue is done.
    public bool TryDefibrillateOccupant(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!_life.InArrest(body) && !_mobState.IsDead(body))
        {
            ent.Comp.DefibAttempt = 0;
            ent.Comp.DefibBlocked = null;
            return false;
        }

        if (!HasDefibModule(ent))
        {
            if (!ent.Comp.DefibWarned)
                Speak(ent, AutodocVoiceEvent.DefibMissing);
            ent.Comp.DefibWarned = true;
            return false;
        }

        ent.Comp.DefibNext = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0.1f, ent.Comp.DefibRetryDelay));

        // The one gate the pod can clear itself. Blood goes in and the refusal is asked again before
        // anything is said, so a patient who arrested from blood loss is transfused and then shocked.
        if (_revival.GetRefusal(body) == NoBlood && TryTransfuse(ent, body))
            ent.Comp.DefibBlocked = null;

        // A gate is not something another shock fixes, so the pod does not even charge: it says what is
        // wrong, once, and says it again only when the reason changes.
        if (_revival.GetRefusal(body) is { } refusal)
        {
            if (ent.Comp.DefibBlocked != refusal)
            {
                ent.Comp.DefibBlocked = refusal;
                Speak(ent, AutodocVoiceEvent.DefibBlocked);
                _chat.TrySendInGameICMessage(ent.Owner, _revival.LocalizeLine(body, refusal), InGameICChatType.Speak, false);
            }

            return false;
        }

        ent.Comp.DefibBlocked = null;
        if (ent.Comp.DefibAttempt >= _defibAttempts)
            return false;

        ent.Comp.DefibAttempt++;
        Speak(ent, AutodocVoiceEvent.DefibCharge);
        _audio.PlayPvs(DefibChargeSound, ent.Owner);
        var revived = _revival.TryDefibrillate(body, out _);
        _audio.PlayPvs(DefibZapSound, ent.Owner);
        _audio.PlayPvs(revived ? DefibSuccessSound : DefibFailureSound, ent.Owner);

        if (revived)
        {
            ent.Comp.DefibAttempt = 0;
            Speak(ent, AutodocVoiceEvent.DefibSuccess);

            // M1a: the ghost is offered the way back, as the hand defibrillator does (plan §5.4 item 6).
            _revival.OfferReturn(body);
            return true;
        }

        // "CHARGING AGAIN" is now true: TickDefib comes back in DefibRetryDelay seconds until the pod runs
        // out of attempts, and then it says so instead of promising a shock it will never give.
        Speak(ent, ent.Comp.DefibAttempt >= _defibAttempts
            ? AutodocVoiceEvent.DefibGaveUp
            : AutodocVoiceEvent.DefibFailure);
        return false;
    }

    /// <summary>The shocks between the procedures: one every DefibRetryDelay while the heart is stopped.</summary>
    private void TickDefib(Entity<AutodocComponent> ent)
    {
        if (!IsPowered(ent) || !HasDefibModule(ent) || _timing.CurTime < ent.Comp.DefibNext ||
            GetOccupant(ent) is not { } body)
            return;

        if (!_life.InArrest(body) && !_mobState.IsDead(body))
            return;

        if (ent.Comp.DefibAttempt >= _defibAttempts && _revival.GetRefusal(body) == null)
            return;

        TryDefibrillateOccupant(ent, body);
    }

    private void FinishQueue(Entity<AutodocComponent> ent)
    {
        // One run, one QUEUE COMPLETE. Every path into a finished queue used to say it again.
        if (ent.Comp.State == AutodocState.Complete)
            return;

        // BRAIN: a patient who arrested on the table is the last thing the pod does something about.
        if (GetOccupant(ent) is { } patient)
        {
            MarkPodWounds(ent, patient);
            TryTransfuse(ent, patient);
            TryDefibrillateOccupant(ent, patient);

            // AUTODOC5: nothing the pod did went through the damage path, so the parts still carry the
            // damage of every wound it closed. They give it up here, and the body total with it.
            _damageSync.SyncBody(patient);

            // Playtest 3 SAM: inside an AUTO run the queue is not finished until a fresh plan finds nothing.
            if (ContinueAutoRun(ent, patient))
                return;
        }

        EndAutoRun(ent);
        ScheduleLeftoverRelease(ent); // Playtest 5
        Speak(ent, AutodocVoiceEvent.QueueComplete);
        WakeOccupant(ent);
        ent.Comp.State = AutodocState.Complete;
        ent.Comp.Locked = IsEmagged(ent);
        ent.Comp.CurrentStep = null;
        CloseTray(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>Abandons the run: clears the queue, wakes the occupant and returns the pod to idle.</summary>
    public void Abort(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is { } aborted)
            MarkPodWounds(ent, aborted);

        ScheduleLeftoverRelease(ent); // Playtest 5
        Speak(ent, AutodocVoiceEvent.Aborted);
        ent.Comp.AutoSession = false; // Playtest 3 SAM: an abort ends the run
        WakeOccupant(ent);
        ent.Comp.Queue.Clear();
        ent.Comp.AbortRequested = false;
        ent.Comp.State = AutodocState.Idle;
        ent.Comp.CurrentStep = null;
        ent.Comp.Locked = IsEmagged(ent);
        CloseTray(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>Pauses and calls for help if the patient crashes under the knife; true when the pod stopped.</summary>
    // A body already dead or arrested at the start is not a crash: operating on a corpse is how a brain is repaired and
    // a heart restarted. The hold sets the flag too, so RESUME carries on instead of stopping on the same death.
    private bool WatchOccupant(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (_mobState.IsDead(body) && !ent.Comp.OccupantWasDead)
        {
            ent.Comp.OccupantWasDead = true;
            ent.Comp.State = AutodocState.Paused;
            ent.Comp.PauseRequested = true;
            Speak(ent, AutodocVoiceEvent.Critical);
            CallForOperator(ent);
            UpdateAppearance(ent);
            UpdateUi(ent);
            return true;
        }

        if (_mobState.IsCritical(body) && !ent.Comp.SaidUnconscious)
        {
            ent.Comp.SaidUnconscious = true;
            Speak(ent, AutodocVoiceEvent.Unconscious);
        }

        return false;
    }

    private void CallForOperator(Entity<AutodocComponent> ent)
    {
        var location = Transform(ent.Owner).GridUid is { } grid ? Name(grid) : Name(ent.Owner);
        _radio.SendRadioMessage(ent.Owner,
            Loc.GetString("wolfmed-autodoc-radio-critical", ("location", location)),
            "Medical",
            ent.Owner);
    }

    #endregion

    #region Requirements and the tray

    private void EnterWaiting(Entity<AutodocComponent> ent, AutodocQueued queued)
    {
        ent.Comp.State = AutodocState.Waiting;
        ent.Comp.CurrentStep = null;
        ent.Comp.Pending = queued.Requirements.FirstOrDefault(r => r.Kind != AutodocRequirementKind.Reagent && !r.Satisfied);
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, false);

        Speak(ent, ent.Comp.Pending?.Kind switch
        {
            AutodocRequirementKind.Part => AutodocVoiceEvent.RequirePart,
            AutodocRequirementKind.Organ => AutodocVoiceEvent.RequireOrgan,
            _ => AutodocVoiceEvent.RequireItem,
        }, ("item", DescribeRequirement(ent.Comp.Pending)));

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>
    /// Waiting on the patient rather than on the tray: clothing over the part. The pod says so once and
    /// looks again every tick, so it picks the procedure straight back up once the way is clear. Somebody
    /// awake is told to undress now or lose the clothing; somebody who cannot is told it will be cut. Either
    /// way the pod cuts once <see cref="AutodocComponent.ClothingCutDelay"/> is out (playtest 5).
    /// </summary>
    private void EnterBlocked(Entity<AutodocComponent> ent, StepInvalidReason reason)
    {
        var first = ent.Comp.BlockedReason == null;
        ent.Comp.State = AutodocState.Waiting;
        ent.Comp.CurrentStep = null;
        ent.Comp.Pending = null;
        ent.Comp.BlockedReason = reason;

        if (first)
        {
            ent.Comp.ClothingSince = _timing.CurTime;
            var body = GetOccupant(ent);

            // Playtest 3 SAM: the WAITING line names the first thing in the way. Something the pod already found it
            // cannot take off, holding the next procedure on the same part, is not announced a second time.
            var known = ent.Comp.GarmentStuck ? ent.Comp.BlockingGarment : null;
            ent.Comp.GarmentStuck = false;
            ent.Comp.BlockingGarment = null;
            ent.Comp.BlockingSlot = null;
            if (body != null)
            {
                foreach (var (slot, item) in Blockers(ent, body.Value))
                {
                    ent.Comp.BlockingGarment = item;
                    ent.Comp.BlockingSlot = slot.Name;
                    break;
                }
            }

            if (known != null && known == ent.Comp.BlockingGarment)
            {
                ent.Comp.GarmentStuck = true;
                UpdateAppearance(ent);
                UpdateUi(ent);
                return;
            }

            var helpless = body is { } patient && !CanUndress(patient);
            Speak(ent, helpless ? AutodocVoiceEvent.ClothingAuto : AutodocVoiceEvent.Clothing);

            if (body is { } awake && !helpless)
                _popup.PopupEntity(Loc.GetString("wolfmed-autodoc-popup-clothing"), ent.Owner, awake,
                    PopupType.MediumCaution);
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>
    /// The only slots the pod ever cuts. The armour check reads more than these (<see cref="ArmorSlots"/>); playtest 3
    /// SAM: the rest come off whole.
    /// </summary>
    private const SlotFlags CutSlots = SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING;

    /// <summary>
    /// Acts on a clothing block. A pressed CUT button cuts now; otherwise the pod cuts once it has waited its delay
    /// out, whoever the patient is and however the run was started. Playtest 5, "the autodoc should cut clothing
    /// automatically": it used to wait on an awake patient, and on any manual run, for ever.
    /// </summary>
    private void TickClothing(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body)
            return;

        if (ent.Comp.CutClothingRequested)
        {
            CutClothing(ent, body);
            return;
        }

        if (_timing.CurTime - ent.Comp.ClothingSince < TimeSpan.FromSeconds(MathF.Max(0f, ent.Comp.ClothingCutDelay)))
            return;

        // Playtest 3 SAM: waited the whole delay on something it cannot take off. The rest of the queue goes on.
        if (ent.Comp.GarmentStuck)
        {
            AbandonForClothing(ent, body);
            return;
        }

        CutClothing(ent, body);
    }

    /// <summary>True while the occupant could still take their own clothes off.</summary>
    private bool CanUndress(EntityUid body)
    {
        return !_mobState.IsIncapacitated(body) &&
               !HasComp<WolfmedDownedComponent>(body) &&
               !HasComp<ForcedSleepingComponent>(body);
    }

    /// <summary>
    /// Cuts the garments in the way off and destroys them: the outer layer and the jumpsuit, which is what
    /// the surgery access rules read. The ID, bag, shoes, gloves, mask and headset are left alone.
    /// </summary>
    public bool CutClothing(Entity<AutodocComponent> ent, EntityUid body)
    {
        ent.Comp.CutClothingRequested = false;

        // Playtest 3 SAM: whatever the armour check reads on the blocked part. The suit and the jumpsuit are still cut;
        // gloves, boots and a helmet come off whole. It used to cut those two whatever the part, so gloves or boots
        // held the pod on WAITING: CLOTHING for ever.
        var stuckBefore = ent.Comp.GarmentStuck ? ent.Comp.BlockingGarment : null;
        var (cut, removed) = Undress(ent, body);
        AnnounceUndress(ent, cut, removed);

        if (ent.Comp.GarmentStuck)
        {
            // The delay to giving the procedure up starts when the pod first finds it cannot take this off.
            if (stuckBefore != ent.Comp.BlockingGarment)
                ent.Comp.ClothingSince = _timing.CurTime;

            UpdateUi(ent);
            return false;
        }

        if (!cut && removed.Count == 0)
            return false;

        ent.Comp.BlockedReason = null;
        ent.Comp.State = AutodocState.Step;
        BeginStep(ent);
        return true;
    }

    private void TickWaiting(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) == null)
        {
            Reset(ent);
            return;
        }

        if (!IsPowered(ent))
            return;

        if (ent.Comp.AbortRequested)
        {
            Abort(ent);
            return;
        }

        if (ent.Comp.BlockedReason != null)
        {
            if (ent.Comp.BlockedReason == StepInvalidReason.Armor)
            {
                TickClothing(ent);
                if (ent.Comp.State != AutodocState.Waiting)
                    return;
            }

            ent.Comp.State = AutodocState.Step;
            BeginStep(ent);
            return;
        }

        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is not { } item)
            return;

        if (!Matches(ent.Comp.Pending, item))
        {
            Speak(ent, AutodocVoiceEvent.WrongItem, ("item", DescribeRequirement(ent.Comp.Pending)));
            _slots.TryEject(ent.Owner, AutodocComponent.TraySlotId, null, out _);
            return;
        }

        if (ent.Comp.Pending is { } pending)
            pending.Satisfied = true;

        Speak(ent, AutodocVoiceEvent.ItemAccepted);
        ent.Comp.State = AutodocState.Step;
        BeginStep(ent);
    }

    /// <summary>The same checks the add-part and add-organ steps make when they look through the tools.</summary>
    public bool Matches(AutodocRequirement? requirement, EntityUid item)
    {
        if (requirement == null)
            return false;

        switch (requirement.Kind)
        {
            case AutodocRequirementKind.Part:
                return TryComp(item, out BodyPartComponent? part) &&
                       part.PartType == requirement.PartType &&
                       (requirement.Symmetry == null || part.Symmetry == requirement.Symmetry);
            case AutodocRequirementKind.Organ:
                return requirement.Component != null &&
                       EntityManager.ComponentFactory.TryGetRegistration(requirement.Component, out var registration) &&
                       HasComp(item, registration.Type);
            case AutodocRequirementKind.Item:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The localized line for a requirement; a generic item when there is none.</summary>
    public string DescribeRequirement(AutodocRequirement? requirement)
    {
        if (requirement == null)
            return Loc.GetString("wolfmed-autodoc-requirement-item");

        return requirement.Kind switch
        {
            AutodocRequirementKind.Part => Loc.GetString("wolfmed-autodoc-requirement-part",
                ("side", Loc.GetString($"wolfmed-autodoc-symmetry-{(requirement.Symmetry?.ToString() ?? "None").ToLowerInvariant()}")),
                ("part", Loc.GetString($"wolfmed-autodoc-parttype-{requirement.PartType.ToString().ToLowerInvariant()}"))),
            AutodocRequirementKind.Organ => Loc.GetString("wolfmed-autodoc-requirement-organ",
                ("organ", Loc.GetString($"wolfmed-autodoc-organ-{requirement.Component?.ToLowerInvariant()}"))),
            AutodocRequirementKind.Reagent => Loc.GetString("wolfmed-autodoc-requirement-reagent",
                ("role", Loc.GetString($"wolfmed-autodoc-reagent-{(requirement.Reagent ?? "fluid").ToLowerInvariant()}")),
                ("units", requirement.Units)),
            _ => Loc.GetString("wolfmed-autodoc-requirement-item"),
        };
    }

    private void CloseTray(Entity<AutodocComponent> ent)
    {
        ent.Comp.Pending = null;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
    }

    #endregion

    #region Reagents

    /// <summary>
    /// Draws units of a role out of the loaded beakers and pushes them into the occupant's bloodstream.
    /// Anything not on the pod's reagent list is left in the beaker. Playtest 5: never past a reagent's
    /// <see cref="AutodocReagentEntry.SafeUnits"/> in the blood, one reagent of the role at a time in list order.
    /// </summary>
    public bool PushReagent(Entity<AutodocComponent> ent, EntityUid body, AutodocReagentRole role, float units)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return false;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == role)
            .ToArray();

        if (allowed.Length == 0)
            return false;

        _solutions.TryGetSolution(body, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var chemicals);
        var pushed = false;
        var left = FixedPoint2.New(units);
        foreach (var entry in allowed)
        {
            if (left <= FixedPoint2.Zero)
                break;

            var want = left;
            if (entry.SafeUnits > 0f)
            {
                var inBlood = chemicals?.GetTotalPrototypeQuantity(entry.Reagent.Id) ?? FixedPoint2.Zero;
                want = FixedPoint2.Min(want, FixedPoint2.New(entry.SafeUnits) - inBlood);
            }

            if (want <= FixedPoint2.Zero)
                continue;

            foreach (var slot in AutodocComponent.ReservoirSlotIds)
            {
                if (want <= FixedPoint2.Zero)
                    break;

                if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                    !TryGetReservoirSolution(beaker, out var soln, out _))
                    continue;

                var taken = _solutions.SplitSolutionPerReagentWithOnly(soln.Value, want, entry.Reagent.Id);
                if (taken.Volume <= FixedPoint2.Zero)
                    continue;

                want -= taken.Volume;
                left -= taken.Volume;
                _bloodstream.TryAddToChemicals(body, taken);
                pushed = true;
            }
        }

        return pushed;
    }

    /// <summary>
    /// The solution the pod draws out of whatever is in a reservoir slot. A beaker fits a dispenser; a
    /// chemistry bottle or a jug does not and only answers as a drainable container, which is why loading
    /// one used to do nothing at all.
    /// </summary>
    public bool TryGetReservoirSolution(EntityUid container,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        return _solutions.TryGetFitsInDispenser(container, out soln, out solution) ||
               _solutions.TryGetDrainableSolution(container, out soln, out solution);
    }

    /// <summary>One line the first time a beaker holds something the pod refuses to use.</summary>
    private void WarnAboutJunkReagents(Entity<AutodocComponent> ent)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable)
            .Select(entry => entry.Reagent.Id)
            .ToHashSet();

        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                !TryGetReservoirSolution(beaker, out _, out var solution))
                continue;

            if (solution.Contents.Any(reagent => !allowed.Contains(reagent.Reagent.Prototype)))
            {
                Speak(ent, AutodocVoiceEvent.ReagentIgnored);
                return;
            }
        }
    }

    #endregion

    #region Blood

    /// <summary>
    /// Tops the occupant's blood up out of the reservoir: ten units at a time until they are back over the
    /// pod's target or the beakers run dry. Their own blood reagent first; saline counts as volume, which is
    /// what the bloodstream already makes of it.
    /// </summary>
    public bool TryTransfuse(Entity<AutodocComponent> ent, EntityUid body)
    {
        ScheduleTransfusion(ent, body); // Playtest 4, IV: wherever the beakers are asked, the blood reservoir is too
        if (!TryComp(body, out BloodstreamComponent? blood))
            return false;

        if (_bloodstream.GetBloodLevelPercentage(body, blood) >= _transfuseBelow)
        {
            ent.Comp.Transfusing = false;
            return false;
        }

        var pushed = false;
        var target = Math.Clamp(ent.Comp.TransfuseTarget, 0f, 1f);
        // Bounded so an unreachable target cannot spin: twenty pushes is two whole bloodstreams.
        for (var i = 0; i < 20 && _bloodstream.GetBloodLevelPercentage(body, blood) < target; i++)
        {
            var taken = DrawFluid(ent, blood.BloodReagent, ent.Comp.TransfuseDose);
            if (taken <= FixedPoint2.Zero)
                break;

            _bloodstream.TryModifyBloodLevel(body, taken, blood);
            pushed = true;
        }

        if (pushed)
            Speak(ent, AutodocVoiceEvent.Transfusing);

        ent.Comp.Transfusing = _bloodstream.GetBloodLevelPercentage(body, blood) < _transfuseBelow;
        return pushed;
    }

    /// <summary>True while the occupant is low enough on blood for the pod to want to do something.</summary>
    public bool NeedsTransfusion(EntityUid body) =>
        TryComp(body, out BloodstreamComponent? blood) &&
        _bloodstream.GetBloodLevelPercentage(body, blood) < _transfuseBelow;

    /// <summary>
    /// Takes up to a dose of the patient's own blood reagent, then of any other loaded fluid. Playtest 3 IPC 2: a machine
    /// fluid goes only to a body that runs on it, and such a body takes nothing else, so a chassis is never topped up with
    /// saline or blood (which the bloodstream would have turned into its own fluid) and flesh never with hydraulic fluid.
    /// </summary>
    private FixedPoint2 DrawFluid(Entity<AutodocComponent> ent, string bloodReagent, float units)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return FixedPoint2.Zero;

        var entries = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == AutodocReagentRole.Fluid)
            .ToArray();

        var matching = entries.Where(entry => entry.Reagent.Id == bloodReagent).Select(entry => entry.Reagent.Id).ToArray();
        var fluids = entries.Any(entry => entry.Machine && entry.Reagent.Id == bloodReagent)
            ? Array.Empty<string>()
            : entries.Where(entry => !entry.Machine).Select(entry => entry.Reagent.Id).ToArray();

        foreach (var wanted in new[] { matching, fluids })
        {
            if (wanted.Length == 0)
                continue;

            foreach (var slot in AutodocComponent.ReservoirSlotIds)
            {
                if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                    !TryGetReservoirSolution(beaker, out var soln, out _))
                    continue;

                var taken = _solutions.SplitSolutionPerReagentWithOnly(soln.Value, FixedPoint2.New(units), wanted);
                if (taken.Volume > FixedPoint2.Zero)
                    return taken.Volume;
            }
        }

        return FixedPoint2.Zero;
    }

    #endregion

    #region Step detail

    /// <summary>Base 1.25x of a surgeon's step time, down to 0.9x at the best manipulator tier.</summary>
    private float GetStepSpeed(Entity<AutodocComponent> ent)
    {
        var normalised = Math.Clamp((ent.Comp.PartRating - 1f) / 3f, 0f, 1f);
        return ent.Comp.StepTime + (ent.Comp.BestStepTime - ent.Comp.StepTime) * normalised;
    }

    private void PlayToolStart(Entity<AutodocComponent> ent, EntityUid stepEnt)
    {
        if (!TryComp(stepEnt, out SurgeryStepComponent? step) || step.Tool == null ||
            !_containers.TryGetContainer(ent, AutodocComponent.ToolContainerId, out var container))
            return;

        foreach (var registration in step.Tool.Values)
        {
            foreach (var tool in container.ContainedEntities)
            {
                if (!HasComp(tool, registration.Component.GetType()) ||
                    !TryComp(tool, out Content.Shared._Shitmed.Medical.Surgery.Tools.SurgeryToolComponent? toolComp) ||
                    toolComp.StartSound == null)
                    continue;

                _audio.PlayPvs(toolComp.StartSound, ent.Owner, DuckedParams(ent, toolComp.StartSound.Params));
                return;
            }
        }
    }

    /// <summary>The voice line family for a step, read off the step's own effect components and tool.</summary>
    private AutodocVoiceEvent GetStepVoice(EntityUid stepEnt, EntProtoId stepId)
    {
        if (StepVoiceOverrides.TryGetValue(stepId.Id, out var over))
            return over;

        if (HasComp<SurgeryAddPartStepComponent>(stepEnt) || HasComp<SurgeryAffixPartStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepAttachPart;
        if (HasComp<SurgeryRemovePartStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepAmputate;
        if (HasComp<SurgeryAddOrganStepComponent>(stepEnt) || HasComp<SurgeryAffixOrganStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepInsertOrgan;
        if (HasComp<SurgeryRemoveOrganStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepRemoveOrgan;
        if (HasComp<SurgeryTendWoundsEffectComponent>(stepEnt))
            return AutodocVoiceEvent.StepTend;
        if (HasComp<SurgeryStepCavityEffectComponent>(stepEnt))
            return AutodocVoiceEvent.StepCavity;

        if (!TryComp(stepEnt, out SurgeryStepComponent? step) || step.Tool == null)
            return AutodocVoiceEvent.StepGeneric;

        foreach (var name in step.Tool.Keys)
        {
            switch (name)
            {
                case "Scalpel": return AutodocVoiceEvent.StepIncision;
                case "Retractor": return AutodocVoiceEvent.StepRetract;
                case "Hemostat": return AutodocVoiceEvent.StepClamp;
                case "Cautery": return AutodocVoiceEvent.StepCauterise;
                case "BoneSaw": return AutodocVoiceEvent.StepSaw;
                case "Drill": return AutodocVoiceEvent.StepDrill;
                case "BoneSetter": return AutodocVoiceEvent.StepSetBone;
                case "BoneGel": return AutodocVoiceEvent.StepBoneGel;
                case "Tending": return AutodocVoiceEvent.StepSuture;
                case "WolfmedSkinGraft": return AutodocVoiceEvent.StepTend;
                case "WolfmedServoKit": return AutodocVoiceEvent.StepWire;
                case "WolfmedHullPlate": return AutodocVoiceEvent.StepWrench;
                case "WolfmedHullWeld": return AutodocVoiceEvent.StepWeld;
            }
        }

        return AutodocVoiceEvent.StepGeneric;
    }

    /// <summary>A slip: one small bleeding wound on the part being worked on, and an apology. Never more.</summary>
    private void RollMalfunction(Entity<AutodocComponent> ent, EntityUid part)
    {
        var damage = TryComp(ent, out DamageableComponent? damageable)
            ? damageable.TotalDamage.Float() / MathF.Max(1f, ent.Comp.FaultDamage)
            : 0f;

        var chance = Math.Clamp(ent.Comp.MalfunctionChance * (1f + damage * 4f), 0f, 1f);
        if (!ent.Comp.ForceMalfunction && !_random.Prob(chance))
            return;

        ent.Comp.ForceMalfunction = false;
        _wounds.CreateOrMergeWound(part, ent.Comp.SlipWound, FixedPoint2.New(ent.Comp.SlipSeverity));
        Speak(ent, AutodocVoiceEvent.Slip);
        Speak(ent, AutodocVoiceEvent.SlipFix);
    }

    #endregion

    #region Emag

    /// <summary>An emagged pod keeps queueing amputations on whatever limb is still attached.</summary>
    private void TickEmag(Entity<AutodocComponent> ent)
    {
        if (!IsEmagged(ent) || !IsPowered(ent) || ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete))
            return;

        if (GetOccupant(ent) is not { } body)
            return;

        ent.Comp.Locked = true;
        var limbs = new List<TargetBodyPart>();
        foreach (var (partId, part) in _body.GetBodyChildren(body))
        {
            if (part.PartType is BodyPartType.Torso or BodyPartType.Head or BodyPartType.Other ||
                _body.GetTargetBodyPart(part.PartType, part.Symmetry) is not { } target ||
                !_surgery.WolfmedSurgeryValid(body, partId, "SurgeryRemovePart"))
                continue;

            limbs.Add(target);
        }

        if (limbs.Count == 0)
            return;

        ent.Comp.Queue.Clear();
        ent.Comp.Queue.Add(new AutodocQueued
        {
            Surgery = "SurgeryRemovePart",
            Part = _random.Pick(limbs),
            Requirements = new List<AutodocRequirement>(),
        });
        ent.Comp.Anaesthesia = false;
        ent.Comp.State = AutodocState.Preparing;
        Speak(ent, AutodocVoiceEvent.Emag);
        UpdateAppearance(ent);
    }

    #endregion
}
