using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W2
using Content.Shared._WF.Wolfmed.Reagents; // WOLFGATE(Wolfmed): CONSC
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): Onyx's OrganGotInserted/RemovedEvent are compat shims here.

namespace Content.Shared._Onyx.Wounds;

public sealed partial class FractureEffectSystem : EntitySystem
{
    private enum EffectKind : byte
    {
        Mobility,
        Manipulation,
    }

    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private WoundStatusEffectSystem _statusEffects = default!;
    [Dependency] private FractureAlertSystem _fractureAlerts = default!;
    [Dependency] private WolfmedWoundTraitSystem _wolfmed = default!; // WOLFGATE(Wolfmed): W2: limb penalties from wounds
    [Dependency] private WolfmedPainReliefSystem _wolfmedPainRelief = default!; // WOLFGATE(Wolfmed): CONSC

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WoundHostComponent, GetManipulationDurationMultiplierEvent>(OnGetMultiplier);
        SubscribeLocalEvent<WoundFractureComponent, FractureGradeChangedEvent>(OnChanged);
        SubscribeLocalEvent<WoundFractureComponent, FractureTreatmentChangedEvent>(OnChanged);
        SubscribeLocalEvent<WoundFractureComponent, WoundRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<WoundableComponent, OrganGotInsertedEvent>(OnPartChanged);
        SubscribeLocalEvent<WoundableComponent, OrganGotRemovedEvent>(OnPartChanged);
        SubscribeLocalEvent<WoundableComponent, BodyPartFunctionalityChangedEvent>(OnFunctionalityChanged);
    }
    private void OnChanged(Entity<WoundFractureComponent> wound, ref FractureGradeChangedEvent args)
    {
        Refresh(args.Body);
        _fractureAlerts.Refresh(args.Body);
        if (args.Body is { } body)
            _functionality.Refresh(body);
    }
    private void OnChanged(Entity<WoundFractureComponent> wound, ref FractureTreatmentChangedEvent args)
    {
        Refresh(args.Body);
        _fractureAlerts.Refresh(args.Body);
        if (args.Body is { } body)
            _functionality.Refresh(body);
    }
    private void OnRemoved(Entity<WoundFractureComponent> wound, ref WoundRemovedEvent args)
    {
        RefreshPart(args.Part);
        _fractureAlerts.Refresh(CompOrNull<BodyPartComponent>(args.Part)?.Body);
        if (CompOrNull<BodyPartComponent>(args.Part)?.Body is { } body)
            _functionality.Refresh(body);
    }
    private void OnPartChanged(Entity<WoundableComponent> part, ref OrganGotInsertedEvent args)
    {
        Refresh(args.Target);
        _fractureAlerts.Refresh(args.Target);
        _functionality.Refresh(args.Target);
        _statusEffects.HandlePartInserted(part.Owner);
    }
    private void OnPartChanged(Entity<WoundableComponent> part, ref OrganGotRemovedEvent args)
    {
        Refresh(args.Target);
        _fractureAlerts.Refresh(args.Target);
        _functionality.Refresh(args.Target);
        _statusEffects.HandlePartRemoved(part.Owner, args.Target);
    }

    private void OnFunctionalityChanged(Entity<WoundableComponent> part, ref BodyPartFunctionalityChangedEvent args)
    {
        Refresh(args.Body);
    }

    private void RefreshPart(EntityUid part) => Refresh(CompOrNull<BodyPartComponent>(part)?.Body);

    public void RefreshTransferredPart(EntityUid part)
    {
        var body = CompOrNull<BodyPartComponent>(part)?.Body;
        Refresh(body);
        _fractureAlerts.Refresh(body);
        if (body is { } uid)
            _functionality.RefreshPart(uid, part);
    }

    private void Refresh(EntityUid? body)
    {
        if (body is { } uid)
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshSpeed(Entity<WoundHostComponent> body, ref RefreshMovementSpeedModifiersEvent args)
    {
        // WOLFGATE(Wolfmed): CONSC: a strong painkiller masks the wound slowdowns, so a player walks on a broken leg
        // while it keeps worsening underneath.
        if (_wolfmedPainRelief.MasksSlowdown(body))
            return;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (!body.Comp.MobilityParts.Contains(bodyPart.PartType) ||
                !TryComp(part, out WoundableComponent? woundable))
                continue;

            var (modifier, partScale, treatmentScale) = GetEffect((part, woundable), body.Comp, bodyPart, EffectKind.Mobility);
            args.ModifySpeed(1f - (1f - modifier) * partScale * treatmentScale);
        }
    }

    private void OnGetMultiplier(Entity<WoundHostComponent> body, ref GetManipulationDurationMultiplierEvent args)
    {
        if (!TryGetUsedHandSymmetry(body, args.Used, out var symmetry))
            return;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (!body.Comp.ManipulationParts.Contains(bodyPart.PartType) ||
                bodyPart.Symmetry != symmetry ||
                !TryComp(part, out WoundableComponent? woundable))
                continue;

            var (modifier, partScale, treatmentScale) = GetEffect((part, woundable), body.Comp, bodyPart, EffectKind.Manipulation);
            args.Multiplier *= 1f + (modifier - 1f) * partScale * treatmentScale;
        }
    }

    // WOLFGATE(Wolfmed): Wolfgate's hands API hands back Hand objects, not hand-id strings (GetActiveHand returns
    // Hand?, IsHolding's out param is Hand?), Hand is a class so there is no .Value, and HandLocation has no
    // Functional* members. Same rewrite as WoundDamageRoutingSystem.TryGetActiveHandPart (:606-633).
    private bool TryGetUsedHandSymmetry(EntityUid body, EntityUid? used, out BodyPartSymmetry symmetry)
    {
        symmetry = BodyPartSymmetry.None;
        if (!TryComp(body, out HandsComponent? hands))
            return false;

        Hand? hand;
        if (used is { } item)
        {
            if (!_hands.IsHolding(body, item, out hand, hands))
                return false;
        }
        else
            hand = _hands.GetActiveHand((body, hands));

        if (hand is null)
            return false;

        symmetry = hand.Location switch
        {
            HandLocation.Left => BodyPartSymmetry.Left,
            HandLocation.Right => BodyPartSymmetry.Right,
            _ => BodyPartSymmetry.None,
        };
        return symmetry != BodyPartSymmetry.None;
    }

    private (float Modifier, float PartScale, float TreatmentScale) GetEffect(
        Entity<WoundableComponent?> part,
        WoundHostComponent host,
        BodyPartComponent bodyPart,
        EffectKind kind)
    {
        var partScale = host.PartEffectScales.GetValueOrDefault(bodyPart.PartType, 1f);

        if (_fractures.GetFracture(part) is { } fracture &&
            fracture.Comp2.Treatment != FractureTreatment.Mended &&
            _fractures.TryGetProfile(part, out var profile))
        {
            var modifier = kind switch
            {
                EffectKind.Mobility => GetGradeSettings(profile, fracture.Comp2.Grade).MovementModifier,
                _ => GetGradeSettings(profile, fracture.Comp2.Grade).ManipulationModifier,
            };
            return (modifier, partScale, GetTreatmentScale(profile, fracture.Comp2.Treatment));
        }

        // WOLFGATE(Wolfmed): W2: Wolfmed's mechanical wounds (a severed tendon) answer here rather than through
        // the functionality fallback below, which is inert while wounds.body_part_functionality_enabled
        // stays false (P2-3). A fracture shadows them, as it already shadows the fallback.
        if (_wolfmed.TryGetLimbPenalty(part, kind == EffectKind.Mobility, out var wolfmed))
            return (wolfmed, partScale, 1f);

        var state = _functionality.GetState(part);
        var fallback = kind switch
        {
            EffectKind.Mobility => state switch
            {
                BodyPartFunctionalityState.Disabled => 0f,
                BodyPartFunctionalityState.Impaired => 0.6f,
                _ => 1f,
            },
            _ => state switch
            {
                BodyPartFunctionalityState.Disabled => 2.5f,
                BodyPartFunctionalityState.Impaired => 1.25f,
                _ => 1f,
            },
        };
        return (fallback, partScale, 1f);
    }

    public float GetDurationMultiplier(EntityUid body, EntityUid? used = null)
    {
        var ev = new GetManipulationDurationMultiplierEvent(used);
        RaiseLocalEvent(body, ref ev);
        return ev.Multiplier;
    }

    private static FractureGradeSettings GetGradeSettings(FractureProfilePrototype profile, FractureGrade grade) =>
        profile.Grades.GetValueOrDefault(grade, new FractureGradeSettings());

    private static float GetTreatmentScale(FractureProfilePrototype profile, FractureTreatment treatment) =>
        profile.TreatmentEffectScales.GetValueOrDefault(treatment, 1f);
}
