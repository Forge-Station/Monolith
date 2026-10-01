using System.Linq;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Medical;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;

namespace Content.Server._WF.Wolfmed.Autodoc;

// The blood reservoir's dependencies, CVars and tick live here; AutodocSystem.cs, .Procedure.cs, .Triage.cs and .Ui.cs
// carry one-line hooks into it.

/// <summary>
/// Playtest 4, IV: the pod's blood reservoir. One slot takes a Bloodpack stack; the planner's transfusion step (and
/// every point the pod already tops blood up from its beakers) starts a steady transfusion of the occupant's own blood
/// out of it, the way an IV drip does, until they are back up or the stack is gone. With nothing that could help
/// loaded, the pod says NO BLOOD LOADED and the readout shows it.
/// </summary>
public sealed partial class AutodocSystem
{
    [Dependency] private WolfmedIvDripSystem _iv = default!;
    [Dependency] private SharedStackSystem _stacks = default!;

    private float _bloodBelow = 0.85f;
    private float _bloodRate = 5f;
    private float _bloodTo = 0.95f;

    /// <summary>Seconds between the reservoir's pushes.</summary>
    private const float BloodInterval = 1f;

    private void InitializeBlood()
    {
        Subs.CVar(_cfg, WolfmedCVars.PodTransfuseBelow, value => _bloodBelow = Math.Clamp(value, 0f, 1f), true);
        Subs.CVar(_cfg, WolfmedCVars.PodTransfuseRate, value => _bloodRate = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.PodTransfuseTo, value => _bloodTo = Math.Clamp(value, 0f, 1f), true);
    }

    /// <summary>The stack in the blood slot, if it has a pack left.</summary>
    public EntityUid? GetBloodPack(Entity<AutodocComponent> ent)
    {
        return _slots.GetItemOrNull(ent.Owner, WolfmedAutodocBloodComponent.SlotId) is { } pack &&
               !TerminatingOrDeleted(pack) &&
               TryComp(pack, out StackComponent? stack) && stack.Count > 0
            ? pack
            : null;
    }

    /// <summary>A reservoir transfusion is running for the occupant now in the pod.</summary>
    public bool BloodRunning(Entity<AutodocComponent> ent) =>
        TryComp(ent, out WolfmedAutodocBloodComponent? blood) && blood.Active && blood.Patient == GetOccupant(ent);

    /// <summary>The occupant needs blood and nothing loaded can give it: NO BLOOD LOADED.</summary>
    public bool BloodFault(Entity<AutodocComponent> ent) =>
        TryComp(ent, out WolfmedAutodocBloodComponent? blood) && blood.NoBlood && blood.Patient == GetOccupant(ent);

    /// <summary>
    /// The planner's transfusion step: under <c>wolfmed.pod_transfuse_below</c> the reservoir starts, or, with no pack
    /// and no fluid in the beakers either, the fault is raised and said once. A body blood packs cannot help (a chassis)
    /// is left to the beakers as before.
    /// </summary>
    public void ScheduleTransfusion(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!TryComp(ent, out WolfmedAutodocBloodComponent? blood))
            return;

        SyncBloodPatient(blood, body);
        if (!TryComp(body, out BloodstreamComponent? stream) ||
            _bloodstream.GetBloodLevelPercentage(body, stream) >= _bloodBelow)
        {
            if (blood.NoBlood)
            {
                blood.NoBlood = false;
                UpdateUi(ent);
            }

            return;
        }

        var pack = GetBloodPack(ent);
        if (!_iv.PacksCanTreat(body, pack))
            return;

        // Playtest 5: an opened pack in the pod counts as blood loaded.
        if (pack == null && blood.PackOpened <= 0f)
        {
            var fault = !HasReservoirFluid(ent, stream.BloodReagent.Id);
            if (fault && !blood.NoBloodSaid)
            {
                blood.NoBloodSaid = true;
                Speak(ent, AutodocVoiceEvent.NoBlood);
            }

            if (fault != blood.NoBlood)
            {
                blood.NoBlood = fault;
                UpdateUi(ent);
            }

            return;
        }

        blood.NoBlood = false;
        if (blood.Active)
            return;

        blood.Active = true;
        blood.NextUpdate = _timing.CurTime;
        Speak(ent, AutodocVoiceEvent.Transfusing);
        UpdateUi(ent);
    }

    /// <summary>A new occupant starts with no transfusion and no fault.</summary>
    private static void SyncBloodPatient(WolfmedAutodocBloodComponent blood, EntityUid? body)
    {
        if (blood.Patient == body)
            return;

        blood.Patient = body;
        blood.Active = false;
        blood.NoBlood = false;
        blood.NoBloodSaid = false;
    }

    /// <summary>
    /// The reservoir's steady push, alongside whatever else the pod is doing. The autofix module watches the blood
    /// itself; without it the planner and the pod's own top-ups start it. Unpowered or emagged, it holds.
    /// </summary>
    private void TickBlood(Entity<AutodocComponent> ent)
    {
        if (!TryComp(ent, out WolfmedAutodocBloodComponent? blood))
            return;

        var body = GetOccupant(ent);
        SyncBloodPatient(blood, body);
        if (body is not { } patient || !IsPowered(ent) || IsEmagged(ent) || _timing.CurTime < blood.NextUpdate)
            return;

        blood.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(BloodInterval);

        // A pack arriving clears the fault's "said once", so an empty slot is reported again after it runs dry.
        var loaded = GetBloodPack(ent);
        if (loaded != null)
            blood.NoBloodSaid = false;

        // A raised fault is the pod waiting for blood, as it waits for material: it starts once some is loaded. The
        // autofix module watches the blood on its own.
        if (blood.NoBlood || ent.Comp.Auto && HasAutofixModule(ent) && !blood.Active)
            ScheduleTransfusion(ent, patient);

        if (!blood.Active)
            return;

        // Playtest 5: the opened pack runs on with the slot empty; only a fresh pack needs the stack.
        if (loaded == null && blood.PackOpened <= 0f)
        {
            blood.Active = false;
            ScheduleTransfusion(ent, patient);
            UpdateUi(ent);
            return;
        }

        // Never past the target: the last push is cut to what is left of the way there.
        var level = _bloodstream.GetBloodLevelPercentage(patient);
        FixedPoint2 pool = TryComp(patient, out BloodstreamComponent? stream) ? stream.BloodMaxVolume : FixedPoint2.Zero;
        var toTarget = (_bloodTo - level) * pool.Float();
        var given = toTarget > 0.005f
            ? _iv.TransfuseFromPack(loaded, ref blood.PackOpened, patient, MathF.Min(_bloodRate * BloodInterval, toTarget))
            : 0f;

        if (given <= 0f || _bloodstream.GetBloodLevelPercentage(patient) >= _bloodTo - 0.001f)
            blood.Active = false;

        if (!blood.Active || _ui.IsUiOpen(ent.Owner, AutodocUiKey.Key))
            UpdateUi(ent);
    }

    /// <summary>
    /// Whether a beaker slot holds fluid the old top-up would give this body (its own blood reagent, or saline to a
    /// body that does not run on a machine fluid), the same filter <c>DrawFluid</c> applies, read without drawing.
    /// </summary>
    private bool HasReservoirFluid(Entity<AutodocComponent> ent, string bloodReagent)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return false;

        var entries = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == AutodocReagentRole.Fluid)
            .ToArray();
        var machine = entries.Any(entry => entry.Machine && entry.Reagent.Id == bloodReagent);
        var wanted = entries
            .Where(entry => entry.Reagent.Id == bloodReagent || !machine && !entry.Machine)
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

    /// <summary>The fault on the status line, after the state.</summary>
    private string BloodStatus(Entity<AutodocComponent> ent) =>
        BloodFault(ent) ? "  " + Loc.GetString("wolfmed-autodoc-status-no-blood") : string.Empty;

    /// <summary>The blood slot as a fourth reservoir row, named for what it holds.</summary>
    private void AddBloodReservoir(Entity<AutodocComponent> ent, List<AutodocReservoirEntry> rows)
    {
        if (!HasComp<WolfmedAutodocBloodComponent>(ent))
            return;

        var opened = TryComp(ent, out WolfmedAutodocBloodComponent? blood) ? blood.PackOpened : 0f;
        var pack = GetBloodPack(ent);
        if (pack == null && opened <= 0f)
        {
            rows.Add(new AutodocReservoirEntry(Loc.GetString("wolfmed-autodoc-reservoir-blood-empty"), 0f, 1f, false));
            return;
        }

        // Playtest 5: with the stack gone the row is the opened pack alone.
        var left = _iv.PackUnitsLeft(pack, opened);
        var max = MathF.Max(left, (pack is { } stack ? _stacks.GetMaxCount(stack) : 1) * _iv.UnitsPerPack);
        var name = pack is { } loaded
            ? Loc.GetString("wolfmed-autodoc-reservoir-blood", ("name", Name(loaded)), ("count", _stacks.GetCount(loaded)))
            : Loc.GetString("wolfmed-autodoc-reservoir-blood-opened");
        rows.Add(new AutodocReservoirEntry(name, left, max, true));
    }
}
