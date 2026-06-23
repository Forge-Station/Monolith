using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed partial class ToggleableEmbedEffectSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private static readonly ProtoId<StatusEffectPrototype> ChainsawShieldSlowedKey = "ChainsawShieldSlowed";

    // StatusEffectsSystem requires a duration; cleanup is still driven by detach/toggle/shutdown.
    private static readonly TimeSpan SlowStatusLifetime = TimeSpan.FromHours(6);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableEmbedEffectComponent, EmbedEvent>(OnEmbed);
        SubscribeLocalEvent<ToggleableEmbedEffectComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<ToggleableEmbedEffectComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<ToggleableEmbedEffectComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnEmbed(EntityUid uid, ToggleableEmbedEffectComponent component, ref EmbedEvent args)
    {
        if (IsActive(uid))
            ApplySlow(args.Embedded, component);
    }

    private void OnToggled(EntityUid uid, ToggleableEmbedEffectComponent component, ref ItemToggledEvent args)
    {
        if (!TryComp<EmbeddableProjectileComponent>(uid, out var embeddable) ||
            embeddable.EmbeddedIntoUid is not { } target)
            return;

        if (args.Activated)
            ApplySlow(target, component);
        else
            RemoveSlow(component);
    }

    private void OnParentChanged(EntityUid uid, ToggleableEmbedEffectComponent component, ref EntParentChangedMessage args)
    {
        if (component.SlowedTarget is { } target && args.Transform.ParentUid != target)
            RemoveSlow(component);
    }

    private void OnShutdown(EntityUid uid, ToggleableEmbedEffectComponent component, ComponentShutdown args)
    {
        RemoveSlow(component);
    }

    private void ApplySlow(EntityUid target, ToggleableEmbedEffectComponent component)
    {
        if (TerminatingOrDeleted(target))
            return;

        if (component.SlowedTarget is { } oldTarget && oldTarget != target)
            RemoveSlow(component);

        component.SlowedTarget = target;

        if (!TryRefreshTargetSlow(target))
        {
            component.SlowedTarget = null;
            return;
        }
    }

    private void RemoveSlow(ToggleableEmbedEffectComponent component)
    {
        if (component.SlowedTarget is not { } target)
            return;

        component.SlowedTarget = null;

        if (TerminatingOrDeleted(target))
            return;

        if (TryGetStrongestSlow(target, out var walk, out var sprint))
        {
            TryRefreshTargetSlow(target, walk, sprint);
            return;
        }

        _statusEffects.TryRemoveStatusEffect(target, ChainsawShieldSlowedKey);
    }

    private bool TryRefreshTargetSlow(EntityUid target)
    {
        if (TryGetStrongestSlow(target, out var walk, out var sprint))
            return TryRefreshTargetSlow(target, walk, sprint);

        return false;
    }

    private bool TryRefreshTargetSlow(EntityUid target, float walk, float sprint)
    {
        if (!_statusEffects.TryAddStatusEffect<ChainsawShieldSlowedComponent>(
                target,
                ChainsawShieldSlowedKey,
                SlowStatusLifetime,
                true))
        {
            return false;
        }

        if (!TryComp<ChainsawShieldSlowedComponent>(target, out var slowed))
            return false;

        slowed.WalkSpeedModifier = walk;
        slowed.SprintSpeedModifier = sprint;
        Dirty(target, slowed);
        _movement.RefreshMovementSpeedModifiers(target);
        return true;
    }

    private bool TryGetStrongestSlow(EntityUid target, out float walk, out float sprint)
    {
        walk = 1f;
        sprint = 1f;
        var found = false;

        var query = EntityQueryEnumerator<ToggleableEmbedEffectComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.SlowedTarget != target || !IsActive(uid))
                continue;

            walk = MathF.Min(walk, component.WalkSpeedModifier);
            sprint = MathF.Min(sprint, component.SprintSpeedModifier);
            found = true;
        }

        return found;
    }

    private bool IsActive(EntityUid uid)
    {
        return TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated;
    }
}
