using System.Linq;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Surgery;

/// <summary>Server half of the wound surgeries: the step effects that actually change wound state.</summary>
/// <remarks>
/// SurgeryStepEvent handlers have to be server-side because WoundBleedingSystem and OrganHealthSystem are
/// server-assembly classes (D13); the listing conditions and completion checks stay shared
/// (WolfmedSurgeryConditionSystem). One component in both systems is legal because the events differ.
/// </remarks>
public sealed class WolfmedWoundSurgerySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private OrganHealthSystem _organHealth = default!;
    [Dependency] private Life.WolfmedLifeSystem _life = default!; // BRAIN
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private WolfmedSurgeryConditionSystem _conditions = default!;
    [Dependency] private Content.Shared._WF.Wolfmed.Wounds.WolfmedDislocationSystem _dislocation = default!;
    [Dependency] private Content.Shared._WF.Wolfmed.Wounds.WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private Wounds.WolfmedEmbeddedRemovalSystem _removal = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundScarSystem _scars = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WolfmedSurgeryClampBleedingEffectComponent, SurgeryStepEvent>(OnClampBleeding);
        SubscribeLocalEvent<WolfmedSurgeryTreatWoundEffectComponent, SurgeryStepEvent>(OnTreatWound);
        SubscribeLocalEvent<WolfmedSurgeryMendFractureEffectComponent, SurgeryStepEvent>(OnMendFracture);
        SubscribeLocalEvent<WolfmedSurgeryOrganHealEffectComponent, SurgeryStepEvent>(OnHealOrgan);
        SubscribeLocalEvent<WolfmedSurgeryBrainRepairEffectComponent, SurgeryStepEvent>(OnRepairBrain);
        SubscribeLocalEvent<WolfmedSurgeryPainEffectComponent, SurgeryStepEvent>(OnSurgeryPain);
        SubscribeLocalEvent<WolfmedSurgeryIncisionWoundEffectComponent, SurgeryStepEvent>(OnOpenIncision);
        SubscribeLocalEvent<WolfmedSurgeryIncisionTreatmentEffectComponent, SurgeryStepEvent>(OnTreatIncision);
        SubscribeLocalEvent<WolfmedSurgeryExtractEmbeddedEffectComponent, SurgeryStepEvent>(OnExtractEmbedded);
        SubscribeLocalEvent<WolfmedSurgeryRelocateJointEffectComponent, SurgeryStepEvent>(OnRelocateJoint);
    }

    /// <summary>
    /// AUTODOC4: one step empties the part, one object at a time through the hand path's own removal, so
    /// what the patient is left with is what a hemostat would have left. A machine with no hands could not
    /// use the do-after version, and a lodged round refuses every other treatment on the part until it is out.
    /// </summary>
    private void OnExtractEmbedded(Entity<WolfmedSurgeryExtractEmbeddedEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp(args.Body, out WoundHostComponent? host))
            return;

        for (var taken = 0; taken < ent.Comp.MaxPerStep; taken++)
        {
            if (_embedded.GetEmbeddedWound(args.Part) is not { } wound ||
                _removal.TryRemoveOne((args.Body, host), wound.Owner, args.User, ent.Comp.Clean) == null)
                return;
        }
    }

    /// <summary>AUTODOC4: the relocate verb's own code, without the do-after and without a hand.</summary>
    private void OnRelocateJoint(Entity<WolfmedSurgeryRelocateJointEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_conditions.FindWound(args.Part, ent.Comp.Wound) is { } wound)
            _dislocation.TryRelocate(args.Body, wound.Owner, args.User);
    }

    private void OnClampBleeding(Entity<WolfmedSurgeryClampBleedingEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_conditions.FindWound(args.Part, ent.Comp.WoundPrototype, bleeding: true) is { } wound)
            _bleeding.ReduceBleeding(wound.Owner, ent.Comp.Amount);
    }

    private void OnTreatWound(Entity<WolfmedSurgeryTreatWoundEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_conditions.FindWound(args.Part, ent.Comp.WoundPrototype,
                internalBleeding: ent.Comp.InternalBleeding) is { } wound)
            _wounds.TreatWound(wound.Owner, ent.Comp.Amount);
    }

    private void OnMendFracture(Entity<WolfmedSurgeryMendFractureEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_fractures.GetFracture(args.Part) is not { } fracture)
            return;

        // TryMend also honours profile.RemoveWoundWhenMended, which the organic profile sets.
        if (ent.Comp.Treatment == FractureTreatment.Reduced)
            _fractures.TryReduce(fracture.Owner);
        else
            _fractures.TryMend(fracture.Owner);
    }

    private void OnHealOrgan(Entity<WolfmedSurgeryOrganHealEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_conditions.TryFindOrgan(args.Part, ent.Comp.Slot, out var organ))
            _organHealth.ChangeHealth(organ, ent.Comp.Amount);
    }

    /// <summary>BRAIN: the whole organ back at once, and the trauma that comes with having been dead.</summary>
    private void OnRepairBrain(Entity<WolfmedSurgeryBrainRepairEffectComponent> ent, ref SurgeryStepEvent args)
    {
        _life.RepairBrain(args.Body);
    }

    private void OnSurgeryPain(Entity<WolfmedSurgeryPainEffectComponent> ent, ref SurgeryStepEvent args)
    {
        // PainComponent lives on the part, not the body; ChangePain redirects a positive delta to the body itself.
        _pain.ChangePain(args.Part, ent.Comp.Amount);
    }

    private void OnOpenIncision(Entity<WolfmedSurgeryIncisionWoundEffectComponent> ent, ref SurgeryStepEvent args)
    {
        // The host gate is what keeps the incision-wound step D2-safe: a non-wound-host keeps only the flat
        // SurgeryDamageChangeEffect the step already carried.
        if (HasComp<WoundHostComponent>(args.Body))
            _wounds.CreateOrMergeWound(args.Part, ent.Comp.Wound, ent.Comp.Severity);
    }

    private void OnTreatIncision(Entity<WolfmedSurgeryIncisionTreatmentEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (ent.Comp.Treatment == WolfmedIncisionTreatment.Clamp)
        {
            _bleeding.TreatPart(args.Part, BleedingTreatment.Clamped, ent.Comp.Wound);
            return;
        }

        if (!TryComp(args.Part, out WoundableComponent? woundable))
            return;

        var chance = Math.Clamp(_cfg.GetCVar(CCVars.SurgeryScarChance), 0f, 1f);
        foreach (var wound in _wounds.GetWounds((args.Part, woundable)).ToArray())
        {
            if (wound.Comp.Prototype != ent.Comp.Wound ||
                wound.Comp.State is not WoundState.Open and not WoundState.Stabilized)
                continue;

            _bleeding.SetTreatment(wound.Owner, BleedingTreatment.Cauterized);
            _wounds.CloseWound(wound.Owner);
            if (chance > 0f && _random.Prob(chance))
                _scars.CreateScar(wound.Owner);
            _wounds.RemoveWound(wound.Owner);
        }
    }
}
