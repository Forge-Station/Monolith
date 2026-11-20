using Content.Shared.Body.Part;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE(Wolfmed): D8 keeps Onyx's part fields on WolfmedBodyPartComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade.
using Content.Shared.Damage; // WOLFGATE(Wolfmed): DamageableSystem/DamageableComponent live here, not in .Components/.Systems.
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundFractureSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private WolfmedDamageableSystem _damage = default!; // WOLFGATE(Wolfmed): D12, Onyx-shaped damage API; see _WF/Wolfmed/Compat
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE(Wolfmed): D8, Onyx's extra part fields.

    public override void Initialize() =>
        SubscribeLocalEvent<WoundFractureComponent, WoundChangedEvent>(OnWoundChanged);

    // WOLFGATE(Wolfmed): D13 puts OrganDamageSystem (the sole caller) in Content.Server, so internal no longer reaches it.
    public void HandlePartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        if (!_net.IsServer || !TryGetProfile(part.Owner, out var profile) ||
            profile.SeverityMultiplier <= 0f ||
            !args.Damage.DamageDict.TryGetValue(profile.DamageType, out var damage) || damage <= FixedPoint2.Zero)
            return;

        var existing = GetFracture(part.Owner);

        if (existing is { } current)
        {
            if (damage < FixedPoint2.Max(FixedPoint2.Zero, profile.WorsenMinimumDamage))
                return;

            if (profile.ResetTreatmentOnDamage)
                ResetTreatment(current);
            _wounds.ChangeSeverity(current.Owner, damage * profile.SeverityMultiplier);
            return;
        }

        if (damage < FixedPoint2.Max(FixedPoint2.Zero, profile.MinimumHitDamage))
            return;

        var effectiveTrauma = GetEffectiveTrauma(part.Owner, profile, damage);
        var hitGrade = GetGrade(profile, effectiveTrauma);
        if (hitGrade == FractureGrade.None ||
            !profile.Grades.TryGetValue(hitGrade, out var gradeSettings) ||
            !_random.Prob(Math.Clamp(gradeSettings.CreationChance, 0f, 1f)))
            return;

        // WOLFGATE(Wolfmed): M6: P30, the fracture starts at the trauma that graded it. Created at the hit alone, a fracture
        // made from accumulated damage sat under its own lowest grade: no grade, no penalty, no treatment.
        if (_wounds.CreateOrMergeWound(part.Owner, profile.Wound, effectiveTrauma * profile.SeverityMultiplier) is not { } wound ||
            !TryComp(wound, out WoundComponent? core))
            return;

        var component = AddComp<WoundFractureComponent>(wound);
        SetGrade((wound, core, component), GetGrade(profile, core.Severity));
    }

    private FixedPoint2 GetEffectiveTrauma(EntityUid part, FractureProfilePrototype profile, FixedPoint2 hit)
    {
        if (profile.AccumulationMultiplier <= 0f || !TryComp(part, out DamageableComponent? damageable))
            return hit;

        var current = _damage.GetPositiveDamage((part, damageable)).DamageDict.GetValueOrDefault(profile.DamageType);
        var previous = FixedPoint2.Max(FixedPoint2.Zero, current - hit);
        return hit + previous * profile.AccumulationMultiplier;
    }

    private void OnWoundChanged(Entity<WoundFractureComponent> wound, ref WoundChangedEvent args)
    {
        if (!_net.IsServer || !TryComp(wound, out WoundComponent? core) ||
            !TryGetProfile(core.HoldingPart, out var profile))
            return;

        var grade = GetGrade(profile, args.Severity);
        if (grade == FractureGrade.None)
            _wounds.RemoveWound((wound.Owner, core));
        else
            SetGrade((wound, core, wound.Comp), grade);
    }

    public Entity<WoundComponent, WoundFractureComponent>? GetFracture(Entity<WoundableComponent?> part)
    {
        if (!Resolve(part, ref part.Comp, false))
            return null;

        foreach (var wound in _wounds.GetWounds(part))
            if (TryComp(wound, out WoundFractureComponent? fracture))
                return (wound, wound.Comp, fracture);
        return null;
    }

    public bool TryReduce(Entity<WoundComponent?> wound) => TrySetTreatment(wound, FractureTreatment.Reduced);
    public bool TryMend(Entity<WoundComponent?> wound)
    {
        if (!Resolve(wound, ref wound.Comp, false) ||
            !TryGetProfile(wound.Comp.HoldingPart, out var profile) ||
            !TrySetTreatment(wound, FractureTreatment.Mended))
            return false;

        return !profile.RemoveWoundWhenMended ||
               _wounds.RemoveWound(wound);
    }

    public bool TrySetTreatment(Entity<WoundComponent?> wound, FractureTreatment treatment)
    {
        if (!_net.IsServer || !Resolve(wound, ref wound.Comp, false) ||
            !TryComp(wound, out WoundFractureComponent? fracture) || fracture.Grade == FractureGrade.None ||
            !TryGetProfile(wound.Comp.HoldingPart, out var profile) || !CanTreat(fracture, profile, treatment))
            return false;

        var old = fracture.Treatment;
        fracture.Treatment = treatment;
        Dirty(wound.Owner, fracture);
        var body = CompOrNull<BodyPartComponent>(wound.Comp.HoldingPart)?.Body;
        var changed = new FractureTreatmentChangedEvent(body, wound.Comp.HoldingPart, wound, old, treatment);
        RaiseLocalEvent(wound.Comp.HoldingPart, ref changed);
        RaiseLocalEvent(wound, ref changed);
        return true;
    }

    public static FractureGrade GetGrade(FractureProfilePrototype profile, FixedPoint2 damage)
    {
        var result = FractureGrade.None;
        var threshold = FixedPoint2.Zero;
        foreach (var (grade, settings) in profile.Grades)
        {
            if (grade == FractureGrade.None || settings.Threshold > damage || settings.Threshold < threshold ||
                settings.Threshold == threshold && grade <= result)
                continue;

            threshold = settings.Threshold;
            result = grade;
        }

        return result;
    }

    public bool TryGetProfile(Entity<WoundableComponent?> part, out FractureProfilePrototype profile)
    {
        profile = default!;
        if (!Resolve(part, ref part.Comp, false) || !TryComp(part, out BodyPartComponent? bodyPart))
            return false;

        var profileId = _wfPart.Get(part).FractureProfile; // WOLFGATE(Wolfmed): D8, FractureProfile lives on WolfmedBodyPartComponent.
        if (profileId is not { } id || !_prototypes.TryIndex(id, out var indexed))
            return false;
        profile = indexed;
        return true;
    }

    private void SetGrade(Entity<WoundComponent, WoundFractureComponent> wound, FractureGrade grade)
    {
        if (wound.Comp2.Grade == grade)
            return;

        var oldGrade = wound.Comp2.Grade;
        wound.Comp2.Grade = grade;
        Dirty(wound.Owner, wound.Comp2);
        var body = CompOrNull<BodyPartComponent>(wound.Comp1.HoldingPart)?.Body;
        var changed = new FractureGradeChangedEvent(body, wound.Comp1.HoldingPart, wound, oldGrade, grade);
        RaiseLocalEvent(wound.Comp1.HoldingPart, ref changed);
        RaiseLocalEvent(wound, ref changed);
    }

    private void ResetTreatment(Entity<WoundComponent, WoundFractureComponent> wound)
    {
        if (wound.Comp2.Treatment == FractureTreatment.None)
            return;

        var old = wound.Comp2.Treatment;
        wound.Comp2.Treatment = FractureTreatment.None;
        Dirty(wound.Owner, wound.Comp2);
        var body = CompOrNull<BodyPartComponent>(wound.Comp1.HoldingPart)?.Body;
        var changed = new FractureTreatmentChangedEvent(body, wound.Comp1.HoldingPart, wound, old, FractureTreatment.None);
        RaiseLocalEvent(wound.Comp1.HoldingPart, ref changed);
        RaiseLocalEvent(wound, ref changed);
    }

    private static bool CanTreat(WoundFractureComponent fracture, FractureProfilePrototype profile,
        FractureTreatment treatment) => treatment switch
    {
        FractureTreatment.Reduced => fracture.Grade >= profile.ReductionMinimumGrade &&
                                     fracture.Treatment == FractureTreatment.None,
        FractureTreatment.Mended => fracture.Treatment != FractureTreatment.Mended,
        _ => false,
    };
}
