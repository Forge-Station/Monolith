using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>Shared half of the wound surgeries: listing conditions and step completion checks.</summary>
/// <remarks>
/// Every SurgeryValidEvent and SurgeryStepCompleteCheckEvent handler has to be shared - the surgery BUI runs
/// GetNextStep/IsStepComplete and CanPerformStep client-side, so a server-only check highlights the wrong row.
/// The mutating SurgeryStepEvent half lives in Content.Server (WolfmedWoundSurgerySystem) because
/// WoundBleedingSystem and OrganHealthSystem are server-assembly classes (D13).
/// </remarks>
public sealed class WolfmedSurgeryConditionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!; // Playtest 3 IPC 2

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WolfmedSurgeryWoundConditionComponent, SurgeryValidEvent>(OnWoundValid);
        SubscribeLocalEvent<WolfmedSurgeryFractureConditionComponent, SurgeryValidEvent>(OnFractureValid);
        SubscribeLocalEvent<WolfmedSurgeryOrganDamagedConditionComponent, SurgeryValidEvent>(OnOrganValid);
        SubscribeLocalEvent<WolfmedSurgeryClampBleedingEffectComponent, SurgeryStepCompleteCheckEvent>(OnClampCheck);
        SubscribeLocalEvent<WolfmedSurgeryTreatWoundEffectComponent, SurgeryStepCompleteCheckEvent>(OnTreatCheck);
        SubscribeLocalEvent<WolfmedSurgeryMendFractureEffectComponent, SurgeryStepCompleteCheckEvent>(OnFractureCheck);
        SubscribeLocalEvent<WolfmedSurgeryOrganHealEffectComponent, SurgeryStepCompleteCheckEvent>(OnOrganCheck);
        SubscribeLocalEvent<WolfmedSurgeryBrainRepairEffectComponent, SurgeryStepCompleteCheckEvent>(OnBrainRepairCheck);
        SubscribeLocalEvent<WolfmedSurgeryIncisionTreatmentEffectComponent, SurgeryStepCompleteCheckEvent>(OnIncisionCheck);
        SubscribeLocalEvent<WolfmedSurgeryCloseEviscerationEffectComponent, SurgeryStepCompleteCheckEvent>(OnCloseEviscerationCheck);
        SubscribeLocalEvent<WolfmedSurgeryEmbeddedConditionComponent, SurgeryValidEvent>(OnEmbeddedValid);
        SubscribeLocalEvent<WolfmedSurgeryExtractEmbeddedEffectComponent, SurgeryStepCompleteCheckEvent>(OnExtractCheck);
        SubscribeLocalEvent<WolfmedSurgeryRelocateJointEffectComponent, SurgeryStepCompleteCheckEvent>(OnRelocateCheck);
        SubscribeLocalEvent<WolfmedSurgeryWeldChassisEffectComponent, SurgeryStepCompleteCheckEvent>(OnWeldChassisCheck);
        SubscribeLocalEvent<WolfmedSurgeryRewireChassisEffectComponent, SurgeryStepCompleteCheckEvent>(OnRewireChassisCheck);
        SubscribeLocalEvent<IncisionOpenComponent, ComponentRemove>(OnIncisionClosed);
    }

    /// <summary>
    /// Playtest 5, "surgery fails at the graft step": a surgeon's step on a Wolfmed procedure keeps it open on the part
    /// until its last step runs. The treatment step removes the wound, fracture, object or organ damage the procedure
    /// was listed for, and its closing step used to be refused, leaving the patient open. Server only; the part
    /// carries the list to the client, whose surgery window reads the same conditions.
    /// </summary>
    public void RecordStep(EntityUid part, Entity<SurgeryComponent> surgery, EntProtoId step)
    {
        if (!_net.IsServer || MetaData(surgery).EntityPrototype is not { } prototype || !HasWolfmedCondition(surgery))
            return;

        var last = surgery.Comp.Steps.Count > 0 && surgery.Comp.Steps[^1] == step;
        if (last)
        {
            if (TryComp(part, out WolfmedSurgeryProgressComponent? progress) && progress.Surgeries.Remove(prototype.ID))
                Dirty(part, progress);
            return;
        }

        var open = EnsureComp<WolfmedSurgeryProgressComponent>(part);
        if (open.Surgeries.Contains(prototype.ID))
            return;

        open.Surgeries.Add(prototype.ID);
        Dirty(part, open);
    }

    /// <summary>An incision closed any other way (Close Incision) ends every procedure begun under it.</summary>
    private void OnIncisionClosed(Entity<IncisionOpenComponent> part, ref ComponentRemove args)
    {
        if (!_net.IsServer || TerminatingOrDeleted(part) ||
            !TryComp(part, out WolfmedSurgeryProgressComponent? progress) || progress.Surgeries.Count == 0)
            return;

        progress.Surgeries.Clear();
        Dirty(part.Owner, progress);
    }

    /// <summary>True when a surgeon began this procedure on the part and has not closed it.</summary>
    public bool InProgress(EntityUid part, EntityUid surgery) =>
        TryComp(part, out WolfmedSurgeryProgressComponent? progress) &&
        MetaData(surgery).EntityPrototype is { } prototype &&
        progress.Surgeries.Contains(prototype.ID);

    private bool HasWolfmedCondition(EntityUid surgery) =>
        HasComp<SurgeryWoundedConditionComponent>(surgery) || // Pre-merge review: the tend surgeries stay open to their closing step too
        HasComp<WolfmedSurgeryWoundConditionComponent>(surgery) ||
        HasComp<WolfmedSurgeryFractureConditionComponent>(surgery) ||
        HasComp<WolfmedSurgeryOrganDamagedConditionComponent>(surgery) ||
        HasComp<WolfmedSurgeryEmbeddedConditionComponent>(surgery);

    /// <summary>Playtest 3 IPC 2: the weld repeats until the part carries none of its wounds.</summary>
    private void OnWeldChassisCheck(Entity<WolfmedSurgeryWeldChassisEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasAnyWound(args.Part, ent.Comp.Wounds))
            args.Cancelled = true;
    }

    /// <summary>Playtest 3 IPC 2: the rewire repeats until the part carries none of its wounds.</summary>
    private void OnRewireChassisCheck(Entity<WolfmedSurgeryRewireChassisEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasAnyWound(args.Part, ent.Comp.Wounds))
            args.Cancelled = true;
    }

    /// <summary>True while the part carries any one of the named wounds, scars aside.</summary>
    public bool HasAnyWound(EntityUid part, IEnumerable<ProtoId<WoundPrototype>> prototypes) =>
        prototypes.Any(prototype => FindWound(part, prototype) != null);

    private void OnEmbeddedValid(Entity<WolfmedSurgeryEmbeddedConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (InProgress(args.Part, ent.Owner))
            return;

        if (_embedded.GetPartCount(args.Part) > 0 == ent.Comp.Inverse)
            args.Cancelled = true;
    }

    /// <summary>Done when the part is empty, whatever is left of the wounds the objects were in.</summary>
    private void OnExtractCheck(Entity<WolfmedSurgeryExtractEmbeddedEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (_embedded.GetPartCount(args.Part) > 0)
            args.Cancelled = true;
    }

    private void OnRelocateCheck(Entity<WolfmedSurgeryRelocateJointEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.Wound) != null)
            args.Cancelled = true;
    }

    private void OnWoundValid(Entity<WolfmedSurgeryWoundConditionComponent> ent, ref SurgeryValidEvent args)
    {
        var part = args.Part;
        if (InProgress(part, ent.Owner))
            return;

        var found = FindWound(part, ent.Comp.WoundPrototype, ent.Comp.State, ent.Comp.Visibility,
            ent.Comp.Bleeding, ent.Comp.InternalBleeding) != null;

        // Playtest 3 IPC 2: a list of wounds, any one of which lists the surgery. The single field still works.
        if (ent.Comp.WoundPrototypes is { Count: > 0 } prototypes)
        {
            found = ent.Comp.WoundPrototype != null && found ||
                    prototypes.Any(prototype => FindWound(part, prototype, ent.Comp.State, ent.Comp.Visibility,
                        ent.Comp.Bleeding, ent.Comp.InternalBleeding) != null);
        }

        if (ent.Comp.Mechanical && !_traits.IsMechanical(part))
            found = false;

        if (found == ent.Comp.Inverse)
            args.Cancelled = true;
    }

    private void OnFractureValid(Entity<WolfmedSurgeryFractureConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (InProgress(args.Part, ent.Owner))
            return;

        if (_fractures.GetFracture(args.Part) is not { } fracture ||
            (ent.Comp.Grade is { } grade ? fracture.Comp2.Grade != grade : fracture.Comp2.Grade < ent.Comp.MinGrade) ||
            ent.Comp.Treatment is { } treatment && fracture.Comp2.Treatment != treatment)
            args.Cancelled = true;
    }

    private void OnOrganValid(Entity<WolfmedSurgeryOrganDamagedConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (InProgress(args.Part, ent.Owner))
            return;

        // Damaged but still alive: OrganHealthSystem.Update destroys any organ at Health <= 0 on the next tick,
        // so a dead organ is an insert job, not a heal job (P4-D24). The brain is the exception, because
        // OrganHealthSystem kills the mob instead of destroying it, and BRAIN's repair surgery wants it.
        var found = TryFindOrgan(args.Part, ent.Comp.Slot, out var organ);
        var treatable = ent.Comp.AnyDamage
            ? found && organ.Comp.Health < organ.Comp.MaxHealth
            : ent.Comp.Destroyed
                ? found && organ.Comp.Health <= FixedPoint2.Zero
                : found && organ.Comp.Health > FixedPoint2.Zero && organ.Comp.Health < organ.Comp.MaxHealth;

        // M6: a repair that has already fixed the organ stays listed until its closing step has run.
        var part = args.Part;
        if (!treatable && !ent.Comp.Inverse && ent.Comp.ValidWhile is { } open &&
            open.Values.Any(entry => HasComp(part, entry.Component.GetType())))
            return;

        if (treatable == ent.Comp.Inverse)
            args.Cancelled = true;
    }

    private void OnClampCheck(Entity<WolfmedSurgeryClampBleedingEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.WoundPrototype, bleeding: true) != null)
            args.Cancelled = true;
    }

    private void OnTreatCheck(Entity<WolfmedSurgeryTreatWoundEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.WoundPrototype, internalBleeding: ent.Comp.InternalBleeding) != null)
            args.Cancelled = true;
    }

    private void OnFractureCheck(Entity<WolfmedSurgeryMendFractureEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        // Complete when the fracture is gone, already at or past the target treatment, or when the target can
        // never be reached: CanTreat gates Reduced on Grade >= profile.ReductionMinimumGrade (Simple on the
        // organic profile), so a Hairline fracture can never be Reduced and a "wait for Reduced" check would
        // stall the surgery forever (P4-D20).
        if (_fractures.GetFracture(args.Part) is not { } fracture)
            return;

        if (fracture.Comp2.Treatment >= ent.Comp.Treatment || !CanEverReach(fracture, ent.Comp.Treatment))
            return;

        args.Cancelled = true;
    }

    private void OnOrganCheck(Entity<WolfmedSurgeryOrganHealEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryFindOrgan(args.Part, ent.Comp.Slot, out var organ) || organ.Comp.Health < organ.Comp.MaxHealth)
            args.Cancelled = true;
    }

    /// <summary>BRAIN: the repair is done once the brain organ is whole again.</summary>
    private void OnBrainRepairCheck(Entity<WolfmedSurgeryBrainRepairEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryFindOrgan(args.Part, ent.Comp.Slot, out var organ) || organ.Comp.Health < organ.Comp.MaxHealth) // M2: slot
            args.Cancelled = true;
    }

    /// <summary>EVISC: the closing step is done when the tear is gone, whatever replaced it.</summary>
    private void OnCloseEviscerationCheck(
        Entity<WolfmedSurgeryCloseEviscerationEffectComponent> ent,
        ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.WoundPrototype) != null)
            args.Cancelled = true;
    }

    private void OnIncisionCheck(Entity<WolfmedSurgeryIncisionTreatmentEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Part, out WoundableComponent? woundable))
            return;

        foreach (var wound in _wounds.GetWounds((args.Part, woundable)))
        {
            if (wound.Comp.Prototype != ent.Comp.Wound)
                continue;

            var pending = ent.Comp.Treatment switch
            {
                WolfmedIncisionTreatment.Clamp => TryComp(wound, out WoundBleedingComponent? bleeding) &&
                                                  bleeding.Treatment < BleedingTreatment.Clamped,
                _ => wound.Comp.State is WoundState.Open or WoundState.Stabilized,
            };

            if (!pending)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    /// <summary>Re-states WoundFractureSystem.CanTreat's reachability rule without touching the private original.</summary>
    private bool CanEverReach(Entity<WoundComponent, WoundFractureComponent> fracture, FractureTreatment treatment)
    {
        if (fracture.Comp2.Grade == FractureGrade.None)
            return false;

        if (treatment != FractureTreatment.Reduced)
            return treatment == FractureTreatment.Mended;

        return _fractures.TryGetProfile(fracture.Comp1.HoldingPart, out var profile) &&
               fracture.Comp2.Grade >= profile.ReductionMinimumGrade;
    }

    /// <summary>The highest-severity wound on a part matching a condition's filters, or null.</summary>
    public Entity<WoundComponent>? FindWound(
        Entity<WoundableComponent?> part,
        ProtoId<WoundPrototype>? prototypeId = null,
        WoundState? state = null,
        WoundVisibility? visibility = null,
        bool bleeding = false,
        bool internalBleeding = false)
    {
        if (!Resolve(part, ref part.Comp, false))
            return null;

        Entity<WoundComponent>? selected = null;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (HasComp<WoundScarComponent>(wound) ||
                prototypeId is { } id && wound.Comp.Prototype != id ||
                state is { } woundState && wound.Comp.State != woundState)
                continue;

            if (!_prototypes.TryIndex(wound.Comp.Prototype, out WoundPrototype? prototype) ||
                visibility is { } woundVisibility && prototype.Visibility != woundVisibility)
                continue;

            if (bleeding && (!TryComp(wound, out WoundBleedingComponent? bleedingComp) ||
                    wound.Comp.State != WoundState.Open || bleedingComp.CurrentRate <= 0f))
                continue;

            if (internalBleeding && (!TryComp(wound, out WoundInternalBleedingComponent? internalComp) ||
                    wound.Comp.State != WoundState.Open || internalComp.Severity <= FixedPoint2.Zero))
                continue;

            if (selected is { } current && current.Comp.Severity >= wound.Comp.Severity)
                continue;

            selected = wound;
        }

        return selected;
    }

    /// <summary>Sum of non-scar wound severity on a part whose prototype damages any type in the group.</summary>
    public FixedPoint2 GetGroupSeverity(Entity<WoundableComponent?> part, ProtoId<DamageGroupPrototype> group)
    {
        if (!Resolve(part, ref part.Comp, false) || !_prototypes.TryIndex(group, out var groupPrototype))
            return FixedPoint2.Zero;

        var types = groupPrototype.DamageTypes.ToHashSet();
        var severity = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (HasComp<WoundScarComponent>(wound) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out WoundPrototype? prototype) ||
                // Wolfgate's DamageGroupPrototype.DamageTypes is List<string>, Onyx's is List<ProtoId<...>>.
                !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                continue;

            severity += wound.Comp.Severity;
        }

        return severity;
    }

    /// <summary>
    /// Severity on a part a tend step could still close: wounds damaging the group whose prototype lets a
    /// treatment reach them at all, and which are not refusing treatment this second. A lodged round, a
    /// charred patch and a cut tendon are all left out, so a tend step never waits on one.
    /// </summary>
    public FixedPoint2 GetTreatableGroupSeverity(Entity<WoundableComponent?> part, ProtoId<DamageGroupPrototype> group)
    {
        if (!Resolve(part, ref part.Comp, false) || !_prototypes.TryIndex(group, out var groupPrototype))
            return FixedPoint2.Zero;

        var types = groupPrototype.DamageTypes.ToHashSet();
        var severity = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (HasComp<WoundScarComponent>(wound) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out WoundPrototype? prototype) ||
                prototype.HealingMultiplier <= 0f ||
                !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                continue;

            var attempt = new WoundTreatmentAttemptEvent(part.Owner, wound.Owner, wound.Comp.Severity);
            RaiseLocalEvent(part.Owner, ref attempt);
            RaiseLocalEvent(wound.Owner, ref attempt);
            if (attempt.Cancelled)
                continue;

            severity += wound.Comp.Severity;
        }

        return severity;
    }

    /// <summary>The named organ slot on a part, if it carries Wolfmed health data.</summary>
    public bool TryFindOrgan(EntityUid part, string slot, out Entity<WolfmedOrganComponent> organ)
    {
        foreach (var (id, comp) in _body.GetPartOrgans(part))
        {
            if (comp.SlotId != slot || !TryComp(id, out WolfmedOrganComponent? health))
                continue;

            organ = (id, health);
            return true;
        }

        organ = default;
        return false;
    }
}
