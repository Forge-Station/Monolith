using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server.Construction;
using Content.Shared.Construction;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    private void InitializeUi()
    {
        SubscribeLocalEvent<AutodocComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<AutodocComponent, AutodocPryDoAfterEvent>(OnPried);
        SubscribeLocalEvent<AutodocComponent, InteractHandEvent>(OnInteractHand);

        Subs.BuiEvents<AutodocComponent>(AutodocUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<AutodocQueueAddMessage>(OnQueueAdd);
            subs.Event<AutodocQueueRemoveMessage>(OnQueueRemove);
            subs.Event<AutodocQueueMoveMessage>(OnQueueMove);
            subs.Event<AutodocControlMessage>(OnControl);
            subs.Event<AutodocAnaesthesiaMessage>(OnAnaesthesia);
        });
    }

    private void OnRefreshParts(EntityUid uid, AutodocComponent comp, RefreshPartsEvent args)
    {
        if (args.PartRatings.TryGetValue(comp.MachinePartManipulator, out var manipulator))
            comp.PartRating = manipulator;

        if (args.PartRatings.TryGetValue(comp.MachinePartMatterBin, out var bin))
            comp.ReservoirSize = comp.BaseReservoirSize * bin;
    }

    /// <summary>A pod with no power answers a hand with a stutter and nothing else.</summary>
    private void OnInteractHand(Entity<AutodocComponent> ent, ref InteractHandEvent args)
    {
        if (!IsPowered(ent))
            Speak(ent, AutodocVoiceEvent.Offline);
    }

    private void OnPried(Entity<AutodocComponent> ent, ref AutodocPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        ent.Comp.Locked = false;
        Speak(ent, AutodocVoiceEvent.LidForced);
        Abort(ent);
        TryEject(ent, force: true);
    }

    private void OnUiOpened(Entity<AutodocComponent> ent, ref BoundUIOpenedEvent args)
    {
        NoteTerminalUser(ent, args.Actor);
        UpdateUi(ent);
    }

    /// <summary>
    /// Playtest 5: self-service is the occupant choosing for themselves, one procedure at a time. Anybody else at the
    /// terminal is an operator with a queue, whoever put the patient in; a patient who climbed in by themselves used
    /// to leave the pod in self-service for the medic who then came to the console, and every ADD replaced the queue.
    /// </summary>
    private void NoteTerminalUser(Entity<AutodocComponent> ent, EntityUid? actor)
    {
        if (actor != null && ent.Comp.SelfService && actor != GetOccupant(ent))
        {
            ent.Comp.SelfService = false;
            UpdateUi(ent);
        }
    }

    private void OnQueueAdd(Entity<AutodocComponent> ent, ref AutodocQueueAddMessage args)
    {
        QueueByHand(ent, args.Actor, args.Surgery, args.Part);
    }

    /// <summary>
    /// One press of ADD. Public so a test drives the same path a player does. Playtest 5: a procedure the subject's
    /// condition no longer allows is refused out loud rather than silently.
    /// </summary>
    public bool QueueByHand(Entity<AutodocComponent> ent, EntityUid? actor, EntProtoId surgery, TargetBodyPart part)
    {
        NoteTerminalUser(ent, actor);

        // Self-service picks exactly one procedure and has no queue at all.
        if (ent.Comp.SelfService && ent.Comp.Queue.Count > 0)
            ent.Comp.Queue.Clear();

        var queued = TryQueue(ent, surgery, part);
        if (!queued && IsKnown(ent, surgery))
            Speak(ent, AutodocVoiceEvent.QueueRefused);

        UpdateUi(ent);
        return queued;
    }

    private void OnQueueRemove(Entity<AutodocComponent> ent, ref AutodocQueueRemoveMessage args)
    {
        // Anything not under the knife can go, even mid-run; the running one needs Abort.
        var first = AutodocQueueRules.FirstMovable(ent.Comp.State);
        if (args.Index >= first && args.Index < ent.Comp.Queue.Count)
        {
            ent.Comp.Queue.RemoveAt(args.Index);
            // An operator editing the queue by hand has taken over from the planner.
            SetAuto(ent, false);
        }

        UpdateUi(ent);
    }

    private void OnQueueMove(Entity<AutodocComponent> ent, ref AutodocQueueMoveMessage args)
    {
        TryMoveQueued(ent, args.Index, args.Up);
    }

    /// <summary>
    /// One press of ^ or v. Public so a test drives the same path a player does. Returns whether the queue
    /// actually changed; the entry under the knife cannot move and neither can anything past the ends.
    /// </summary>
    public bool TryMoveQueued(Entity<AutodocComponent> ent, int index, bool up)
    {
        var target = up ? index - 1 : index + 1;
        var first = AutodocQueueRules.FirstMovable(ent.Comp.State);
        var moved = index >= first && index < ent.Comp.Queue.Count &&
                    target >= first && target < ent.Comp.Queue.Count;

        if (moved)
        {
            (ent.Comp.Queue[index], ent.Comp.Queue[target]) = (ent.Comp.Queue[target], ent.Comp.Queue[index]);
            // An operator editing the queue by hand has taken over from the planner.
            SetAuto(ent, false);
        }

        UpdateUi(ent);
        return moved;
    }

    private void OnControl(Entity<AutodocComponent> ent, ref AutodocControlMessage args)
    {
        NoteTerminalUser(ent, args.Actor);
        Control(ent, args.Control, args.Actor);
    }

    /// <summary>One press of one button on the terminal. Public so a test drives the same path a player does.</summary>
    public void Control(Entity<AutodocComponent> ent, AutodocControl control, EntityUid? actor)
    {
        switch (control)
        {
            case AutodocControl.Start:
                TryStart(ent, actor);
                break;
            case AutodocControl.Pause:
                // Only at a step boundary: the pod never stops with a scalpel in the wound.
                ent.Comp.PauseRequested = ent.Comp.State != AutodocState.Paused;
                if (ent.Comp.State == AutodocState.Paused)
                    TryStart(ent, actor);
                break;
            case AutodocControl.Abort:
                ent.Comp.AbortRequested = true;
                if (ent.Comp.State is AutodocState.Paused or AutodocState.Waiting or AutodocState.Complete or AutodocState.Faulted) // Pre-merge review: ABORT clears a fault too
                    Abort(ent);
                break;
            case AutodocControl.Eject:
                // An eject with a patient open on the table is an emergency; one from an idle or finished
                // pod is somebody getting out, and gets a door held for them instead of an alarm.
                if (IsRunning(ent))
                {
                    Speak(ent, AutodocVoiceEvent.EmergencyEject);
                    Abort(ent);
                }
                else
                {
                    Speak(ent, AutodocVoiceEvent.Goodbye);
                }

                TryEject(ent, force: !ent.Comp.EmagRevealed);
                break;
            case AutodocControl.Plan:
                // Self-service has no queue to look at, so PLAN and START are one button there.
                if (TryPlan(ent) > 0)
                {
                    Speak(ent, AutodocVoiceEvent.Plan);
                    if (ent.Comp.SelfService)
                        TryStart(ent, actor);
                }
                else if (!BloodRunning(ent) && !BloodFault(ent) && !AntibioticsRunning(ent) && !AntibioticFault(ent)) // Playtest 4, IV / 5: transfusing or dosing is something to do
                {
                    Speak(ent, AutodocVoiceEvent.AutoNothing);
                }

                break;
            case AutodocControl.Auto:
                SetAuto(ent, !ent.Comp.Auto);
                break;
            case AutodocControl.CutClothing:
                // Acted on now when the pod is already stuck on clothing, and remembered if it is not yet.
                ent.Comp.CutClothingRequested = true;
                if (ent.Comp.BlockedReason == StepInvalidReason.Armor && GetOccupant(ent) is { } patient)
                    CutClothing(ent, patient);

                break;
        }

        UpdateUi(ent);
    }

    private void OnAnaesthesia(Entity<AutodocComponent> ent, ref AutodocAnaesthesiaMessage args)
    {
        ent.Comp.Anaesthesia = args.Enabled;
        UpdateUi(ent);
    }

    /// <summary>Builds and sends the window state, if the pod has a UI.</summary>
    public void UpdateUi(Entity<AutodocComponent> ent)
    {
        if (!_ui.HasUi(ent.Owner, AutodocUiKey.Key))
            return;

        var occupant = GetOccupant(ent);
        // Playtest 4, IV: not TRANSFUSING beside NO BLOOD LOADED, and TRANSFUSING while the blood reservoir runs.
        ent.Comp.Transfusing = occupant is { } bleeding && NeedsTransfusion(bleeding) && !BloodFault(ent) || BloodRunning(ent);
        var state = new AutodocBuiState
        {
            State = ent.Comp.State,
            Status = StatusLine(ent) + BloodStatus(ent) + AntibioticStatus(ent) + AnaesthesiaStatus(occupant), // Playtest 4, IV: NO BLOOD LOADED; playtest 5: the course
            ClothingBlocked = ent.Comp.BlockedReason == StepInvalidReason.Armor,
            Transfusing = ent.Comp.Transfusing,
            CurrentStep = ent.Comp.CurrentStep?.Id,
            Progress = ent.Comp.StepLength > 0f
                ? Math.Clamp(1f - ent.Comp.StepRemaining / ent.Comp.StepLength, 0f, 1f)
                : 0f,
            StepEnds = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, ent.Comp.StepRemaining)),
            StepLength = ent.Comp.StepLength,
            SelfService = ent.Comp.SelfService,
            Anaesthesia = ent.Comp.Anaesthesia,
            Occupied = occupant != null,
            DefibModule = HasDefibModule(ent),
            AutofixModule = HasAutofixModule(ent),
            Auto = ent.Comp.Auto,
            DiskProgram = CurrentDisk(ent) is { } disk && _protos.TryIndex(disk.Program, out var program)
                ? Loc.GetString(program.Name)
                : null,
            TrayItem = _slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is { } tray ? Name(tray) : null,
            Reservoir = BuildReservoir(ent),
            ReservoirAccepted = ReservoirAccepted(ent), // Playtest 5: what NOT MEDICINE means
            LastLine = ent.Comp.LastLine,
            Seal = GetSeal(ent), // Playtest 3: the pod's own air
        };
        AddBloodReservoir(ent, state.Reservoir); // Playtest 4, IV: the blood slot's row

        if (occupant is { } body)
        {
            state.Diagnostics = _analyzer.WolfmedBuildScanMessage(body);
            state.Available = GetAvailable(ent, body);
        }

        foreach (var queued in ent.Comp.Queue)
        {
            var lines = queued.Requirements
                .Select(req => Loc.GetString("wolfmed-autodoc-requirement-line",
                    ("what", DescribeRequirement(req)),
                    ("status", Loc.GetString(req.Satisfied
                        ? "wolfmed-autodoc-requirement-loaded"
                        : "wolfmed-autodoc-requirement-waiting"))))
                .ToList();

            state.Queue.Add(new AutodocQueueEntry(queued.Surgery, queued.Part, lines));
        }

        _ui.SetUiState(ent.Owner, AutodocUiKey.Key, state);
    }

    /// <summary>
    /// The big readout. Waiting names what it is waiting for: "material" was a lie for a dressed patient,
    /// who needs a button pressed rather than something put in the tray.
    /// </summary>
    private string StatusLine(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.State == AutodocState.Waiting && ent.Comp.BlockedReason == StepInvalidReason.Armor)
            return BlockingStatus(ent) ?? Loc.GetString("wolfmed-autodoc-status-waiting-clothing"); // Playtest 3 SAM

        var status = Loc.GetString($"wolfmed-autodoc-status-{ent.Comp.State.ToString().ToLowerInvariant()}");
        return ent.Comp.Transfusing ? status + "  " + Loc.GetString("wolfmed-autodoc-status-transfusing") : status;
    }

    /// <summary>
    /// What the patient is under, on the same line as the state: how long the painkiller has left and how
    /// sedated it has left them, which is the number the pod's own cap is measured against.
    /// </summary>
    private string AnaesthesiaStatus(EntityUid? occupant)
    {
        if (occupant is not { } body || !TryComp(body, out WolfmedPainReliefComponent? relief))
            return string.Empty;

        var seconds = relief.Ends is { } ends
            ? MathF.Max(0f, (float) (ends - _timing.CurTime).TotalSeconds)
            : 0f;

        if (seconds <= 0f && relief.Sedation <= 0f)
            return string.Empty;

        return "  " + Loc.GetString("wolfmed-autodoc-status-anaesthesia",
            ("seconds", MathF.Round(seconds)),
            ("percent", MathF.Round(relief.Sedation * 100f)));
    }

    /// <summary>Playtest 5: the reagents the pod draws on, by name, for the row a beaker of anything else gets.</summary>
    private string ReservoirAccepted(Entity<AutodocComponent> ent)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return string.Empty;

        return string.Join(", ", list.Reagents
            .Where(entry => entry.AutodocAdministrable)
            .Select(entry => _protos.TryIndex(entry.Reagent, out var reagent) ? reagent.LocalizedName : entry.Reagent.Id)
            .Distinct());
    }

    private List<AutodocReservoirEntry> BuildReservoir(Entity<AutodocComponent> ent)
    {
        var result = new List<AutodocReservoirEntry>();
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return result;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable)
            .Select(entry => entry.Reagent.Id)
            .ToHashSet();

        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                !TryGetReservoirSolution(beaker, out _, out var solution))
            {
                result.Add(new AutodocReservoirEntry(Loc.GetString("wolfmed-autodoc-reservoir-empty"), 0f, ent.Comp.ReservoirSize, false));
                continue;
            }

            var usable = solution.Contents.Any(reagent => allowed.Contains(reagent.Reagent.Prototype));
            result.Add(new AutodocReservoirEntry(Name(beaker), solution.Volume.Float(), ent.Comp.ReservoirSize, usable));
        }

        return result;
    }
}
