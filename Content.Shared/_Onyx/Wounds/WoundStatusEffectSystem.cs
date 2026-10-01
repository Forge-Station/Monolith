// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Wounds;

/// <summary>
/// Applies status effects granted by wound behaviors (<see cref="WoundStatusEffectBehavior"/>)
/// while the wound is active and removes them once it heals or drops below the threshold.
/// Effects are attached to the part's body (or the part itself) and only removed when no
/// other active wound targeting the same entity carries the same effect.
/// </summary>
public sealed partial class WoundStatusEffectSystem : EntitySystem
{
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, WoundCreatedEvent>(OnWoundCreated);
        SubscribeLocalEvent<WoundableComponent, WoundChangedEvent>(OnWoundChanged);
        SubscribeLocalEvent<WoundableComponent, WoundStateChangedEvent>(OnWoundStateChanged);
        SubscribeLocalEvent<WoundableComponent, WoundRemovedEvent>(OnWoundRemoved);
    }

    private void OnWoundStateChanged(Entity<WoundableComponent> part, ref WoundStateChangedEvent args)
    {
        _pain.RefreshWoundPain((part.Owner, (WoundableComponent?) part.Comp));
        RefreshFunctionality(part.Owner);
        if (!_net.IsServer || !TryComp(args.Wound, out WoundComponent? wound) ||
            !TryGetActiveBehavior(wound.Prototype, wound.Severity, out var behavior))
            return;

        if (args.State is WoundState.Closed or WoundState.Healed or WoundState.Scarred)
            RemoveEffect(part.Owner, args.Wound, behavior);
        else if (args.OldState is WoundState.Closed or WoundState.Healed or WoundState.Scarred)
            ApplyEffect(part.Owner, behavior);
    }

    private void OnWoundCreated(Entity<WoundableComponent> part, ref WoundCreatedEvent args)
    {
        _pain.RefreshWoundPain((part.Owner, (WoundableComponent?) part.Comp));
        _pain.ApplyOneTimePain(part.Owner, args.Wound);
        RefreshFunctionality(part.Owner);
        if (!_net.IsServer)
            return;

        var severity = CompOrNull<WoundComponent>(args.Wound)?.Severity ?? FixedPoint2.Zero;
        if (!TryGetActiveBehavior(args.Prototype, severity, out var behavior))
            return;

        ApplyEffect(part.Owner, behavior);
    }

    private void OnWoundChanged(Entity<WoundableComponent> part, ref WoundChangedEvent args)
    {
        _pain.RefreshWoundPain((part.Owner, (WoundableComponent?) part.Comp));
        if (args.Severity > args.OldSeverity)
            _pain.ApplyOneTimePain(part.Owner, args.Wound, args.Severity - args.OldSeverity);
        RefreshFunctionality(part.Owner);
        if (!_net.IsServer || !TryComp(args.Wound, out WoundComponent? wound) ||
            !_prototypes.TryIndex(wound.Prototype, out var prototype))
            return;

        var oldBehavior = prototype.TryGetBehavior(args.OldSeverity, out WoundStatusEffectBehavior oldEffect) &&
                          (oldEffect.MinSeverity is not { } oldMinimum || args.OldSeverity >= oldMinimum)
            ? oldEffect
            : null;
        var newBehavior = prototype.TryGetBehavior(args.Severity, out WoundStatusEffectBehavior newEffect) &&
                          (newEffect.MinSeverity is not { } minimum || args.Severity >= minimum)
            ? newEffect
            : null;

        if (oldBehavior?.StatusEffect == newBehavior?.StatusEffect &&
            oldBehavior?.ApplyToPart == newBehavior?.ApplyToPart)
            return;

        if (oldBehavior is { } removed)
            RemoveEffect(part.Owner, args.Wound, removed);
        if (newBehavior is { } applied)
            ApplyEffect(part.Owner, applied);
    }

    private void OnWoundRemoved(Entity<WoundableComponent> part, ref WoundRemovedEvent args)
    {
        _pain.RefreshWoundPain((part.Owner, (WoundableComponent?) part.Comp));
        RefreshFunctionality(part.Owner);
        if (!_net.IsServer)
            return;

        var severity = CompOrNull<WoundComponent>(args.Wound)?.Severity ?? FixedPoint2.Zero;
        if (!TryGetActiveBehavior(args.Prototype, severity, out var behavior))
            return;

        RemoveEffect(part.Owner, args.Wound, behavior);
    }

    public void HandlePartRemoved(EntityUid part, EntityUid body)
    {
        if (!_net.IsServer)
            return;

        foreach (var wound in _wounds.GetWounds(part))
            if (TryGetActiveBehavior(wound.Comp.Prototype, wound.Comp.Severity, out var behavior))
                RemoveEffect(part, wound.Owner, behavior, body);
    }

    public void HandlePartInserted(EntityUid part)
    {
        if (!_net.IsServer)
            return;

        foreach (var wound in _wounds.GetWounds(part))
            if (TryGetActiveBehavior(wound.Comp.Prototype, wound.Comp.Severity, out var behavior))
                ApplyEffect(part, behavior);
    }

    public void RefreshPartWounds(EntityUid part)
    {
        _pain.RefreshWoundPain(part);
        RefreshFunctionality(part);
        HandlePartInserted(part);
    }

    private bool TryGetBehavior(ProtoId<WoundPrototype> prototypeId, FixedPoint2 severity,
        out WoundStatusEffectBehavior behavior)
    {
        behavior = null!;
        return _prototypes.TryIndex(prototypeId, out var prototype) &&
               prototype.TryGetBehavior(severity, out behavior);
    }

    private void RefreshFunctionality(EntityUid part)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _functionality.RefreshPart(body, part);
    }

    private bool TryGetActiveBehavior(ProtoId<WoundPrototype> prototypeId, FixedPoint2 severity,
        out WoundStatusEffectBehavior behavior)
    {
        return TryGetBehavior(prototypeId, severity, out behavior) &&
               (behavior.MinSeverity is not { } minimum || severity >= minimum);
    }

    private EntityUid? GetTarget(EntityUid part, WoundStatusEffectBehavior behavior)
    {
        if (behavior.ApplyToPart)
            return part;

        return CompOrNull<BodyPartComponent>(part)?.Body;
    }

    private void ApplyEffect(EntityUid part, WoundStatusEffectBehavior behavior)
    {
        if (GetTarget(part, behavior) is not { } target)
            return;

        if (behavior.Duration is { } duration)
            _statusEffects.TryUpdateStatusEffectDuration(target, behavior.StatusEffect, duration);
        else if (!_statusEffects.HasStatusEffect(target, behavior.StatusEffect))
            _statusEffects.TrySetStatusEffectDuration(target, behavior.StatusEffect);
    }

    private void RemoveEffect(EntityUid part, EntityUid removedWound, WoundStatusEffectBehavior behavior,
        EntityUid? detachedBody = null)
    {
        var target = behavior.ApplyToPart ? part : detachedBody ?? GetTarget(part, behavior);
        if (target is not { } uid)
            return;

        if (behavior.ApplyToPart)
        {
            if (HasOtherOwner(part, uid, removedWound, behavior.StatusEffect))
                return;
        }
        else
        {
            foreach (var (otherPart, _) in _body.GetBodyChildren(uid))
                if (HasOtherOwner(otherPart, uid, removedWound, behavior.StatusEffect))
                    return;
        }

        _statusEffects.TryRemoveStatusEffect(uid, behavior.StatusEffect);
    }

    private bool HasOtherOwner(EntityUid part, EntityUid target, EntityUid removedWound,
        EntProtoId<StatusEffectComponent> statusEffect)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Owner == removedWound ||
                !TryGetActiveBehavior(wound.Comp.Prototype, wound.Comp.Severity, out var behavior) ||
                behavior.StatusEffect != statusEffect || GetTarget(part, behavior) != target)
                continue;

            return true;
        }

        return false;
    }
}
