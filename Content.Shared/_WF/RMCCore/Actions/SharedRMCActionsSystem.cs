using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Interaction;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._WF.RMCCore.Actions;

public abstract partial class SharedRMCActionsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<ActionSharedCooldownComponent> _actionSharedCooldownQuery;

    public override void Initialize()
    {
        _actionSharedCooldownQuery = GetEntityQuery<ActionSharedCooldownComponent>();

        SubscribeAllEvent<RMCMissedTargetActionEvent>(OnMissedTargetAction);

        SubscribeLocalEvent<ActionSharedCooldownComponent, ActionPerformedEvent>(OnSharedCooldownPerformed);

        SubscribeLocalEvent<ActionCooldownComponent, RMCActionUseEvent>(OnCooldownUse);

        SubscribeLocalEvent<ActionInRangeUnobstructedComponent, RMCActionUseAttemptEvent>(OnInRangeUnobstructedUseAttempt);

        SubscribeLocalEvent<ActionReducedUseDelayComponent, ActionReducedUseDelayEvent>(OnReducedUseDelayEvent);

        SubscribeLocalEvent<ActionReducedUseDelayComponent, StartUseDelayEvent>(OnReducedStartUseDelay);
    }

    private void OnMissedTargetAction(RMCMissedTargetActionEvent args)
    {
        var action = GetEntity(args.Action);

        if (!TryComp(action, out RMCCooldownOnMissComponent? cooldown))
            return;

        _actions.SetIfBiggerCooldown(action, cooldown.MissCooldown);
    }

    private void OnSharedCooldownPerformed(Entity<ActionSharedCooldownComponent> ent, ref ActionPerformedEvent args)
    {
        if (ent.Comp.OnPerform)
            ActivateSharedCooldown((ent, ent), args.Performer);
    }

    public void ActivateSharedCooldown(Entity<ActionSharedCooldownComponent?> action, EntityUid performer)
    {
        if (!Resolve(action, ref action.Comp, false))
            return;

        if (action.Comp.Cooldown == TimeSpan.Zero)
            return;

        foreach (var (actionId, _) in _actions.GetActions(performer))
        {
            if (!_actionSharedCooldownQuery.TryComp(actionId, out var shared))
                continue;

            // Same ID or primary ID found in subset of other action's ids
            if ((shared.Id != null && shared.Id == action.Comp.Id) || (action.Comp.Id != null && shared.Ids.Contains(action.Comp.Id.Value)))
                _actions.SetIfBiggerCooldown(actionId, action.Comp.Cooldown);
        }
    }

    /// <summary>
    /// Enable all events that have a shared cooldown with the provided action
    /// </summary>
    public void EnableSharedCooldownEvents(Entity<ActionSharedCooldownComponent?> action, EntityUid performer)
    {
        SetStatusOfSharedCooldownEvents(action, performer, true);
    }

    /// <summary>
    /// Disable all events that have a shared cooldown with the provided action
    /// </summary>
    public void DisableSharedCooldownEvents(Entity<ActionSharedCooldownComponent?> action, EntityUid performer)
    {
        SetStatusOfSharedCooldownEvents(action, performer, false);
    }

    /// <summary>
    /// Sets the enabled status of all events that have a shared cooldown with the provided action
    /// </summary>
    private void SetStatusOfSharedCooldownEvents(Entity<ActionSharedCooldownComponent?> action, EntityUid performer, bool newStatus)
    {
        if (!Resolve(action, ref action.Comp, false))
            return;

        if (action.Comp.Cooldown == TimeSpan.Zero)
            return;

        foreach (var (actionId, comp) in _actions.GetActions(performer))
        {
            if (!_actionSharedCooldownQuery.TryComp(actionId, out var shared))
                continue;

            // Same ID or primary ID found in subset of other action's ids
            if (!(shared.Id != null && shared.Id == action.Comp.Id || action.Comp.Id != null &&
                  (shared.Ids.Contains(action.Comp.Id.Value) || shared.ActiveIds.Contains(action.Comp.Id.Value))))
            {
                continue;
            }

            _actions.SetEnabled(actionId, newStatus);
        }
    }

    private void OnReducedUseDelayEvent(Entity<ActionReducedUseDelayComponent> ent, ref ActionReducedUseDelayEvent args)
    {
        if (args.Amount < 0 || args.Amount > 1 || args.Amount == ent.Comp.UseDelayReduction)
            return;

        ent.Comp.UseDelayReduction = args.Amount;

        Dirty(ent, ent.Comp);
    }

    private void OnCooldownUse(Entity<ActionCooldownComponent> ent, ref RMCActionUseEvent args)
    {
        _actions.SetIfBiggerCooldown(ent.Owner, ent.Comp.Cooldown);
    }

    private void OnInRangeUnobstructedUseAttempt(Entity<ActionInRangeUnobstructedComponent> ent, ref RMCActionUseAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.Target is not { } target)
            return;

        if (!_interaction.InRangeUnobstructed(args.User, target, ent.Comp.Range))
            args.Cancelled = true;
    }

    private void OnReducedStartUseDelay(Entity<ActionReducedUseDelayComponent> ent, ref StartUseDelayEvent args)
    {
        var reductionAmount = args.Delay * ent.Comp.UseDelayReduction.Float();
        args.Start -= reductionAmount;
        args.End -= reductionAmount;
    }

    public bool CanUseActionPopup(EntityUid user, EntityUid action, EntityUid? target = null)
    {
        var ev = new RMCActionUseAttemptEvent(user, target);
        RaiseLocalEvent(action, ref ev);

        return !ev.Cancelled;
    }

    private void ActionUsed(EntityUid user, EntityUid action)
    {
        var ev = new RMCActionUseEvent(user);
        RaiseLocalEvent(action, ref ev);
    }

    public bool TryUseAction(EntityUid user, EntityUid action, EntityUid target)
    {
        if (!CanUseActionPopup(user, action, target))
            return false;

        ActionUsed(user, action);
        return true;
    }

    public bool TryUseAction(InstantActionEvent action)
    {
        if (!CanUseActionPopup(action.Performer, action.Action))
            return false;

        ActionUsed(action.Performer, action.Action);
        return true;
    }

    public bool TryUseAction(EntityTargetActionEvent action)
    {
        if (!CanUseActionPopup(action.Performer, action.Action, action.Target))
            return false;

        ActionUsed(action.Performer, action.Action);
        return true;
    }

    public bool TryUseAction(WorldTargetActionEvent action)
    {
        if (!CanUseActionPopup(action.Performer, action.Action))
            return false;

        ActionUsed(action.Performer, action.Action);
        return true;
    }

    public bool TryUseAction(EntityWorldTargetActionEvent action)
    {
        if (!CanUseActionPopup(action.Performer, action.Action, action.Entity))
            return false;

        ActionUsed(action.Performer, action.Action);
        return true;
    }

    public IEnumerable<Entity<BaseActionComponent>> GetActionsWithEvent<T>(EntityUid user) where T : BaseActionEvent
    {
        foreach (var (id, comp) in _actions.GetActions(user))
        {
            if (comp.BaseEvent is T)
                yield return (id, comp);
        }
    }

    public IEnumerable<Entity<BaseActionComponent, T>> GetActionsWithComp<T>(EntityUid user) where T : IComponent
    {
        foreach (var (id, comp) in _actions.GetActions(user))
        {
            if (TryComp(id, out T? other))
                yield return (id, comp, other);
        }
    }

    // WOLFGATE START: helpers with RMC-14's names over Wolfgate's older actions API (BaseActionComponent per action type)

    /// <summary>The user's actions as entities, RMC's <c>GetActions</c> shape.</summary>
    public IEnumerable<Entity<BaseActionComponent>> GetActionEntities(EntityUid user)
    {
        foreach (var (id, comp) in _actions.GetActions(user))
        {
            yield return (id, comp);
        }
    }

    /// <summary>The action's data component, or null if the entity isn't an action.</summary>
    public Entity<BaseActionComponent>? GetAction(EntityUid? uid, bool logError = true)
    {
        if (!_actions.TryGetActionData(uid, out var comp, logError))
            return null;

        return (uid.Value, comp);
    }

    /// <summary>The event the action raises when performed.</summary>
    public BaseActionEvent? GetEvent(EntityUid? uid)
    {
        return GetAction(uid, false)?.Comp.BaseEvent;
    }

    /// <summary>Replaces the action's event with a copy of <paramref name="ev"/>; false if the event type doesn't fit the action type.</summary>
    public bool SetEvent(EntityUid? uid, BaseActionEvent ev)
    {
        if (GetAction(uid) is not { } action)
            return false;

        var copy = _serialization.CreateCopy(ev, notNullableOverride: true);
        switch (action.Comp)
        {
            case InstantActionComponent instant when copy is InstantActionEvent instantEv:
                instant.Event = instantEv;
                break;
            case EntityTargetActionComponent entity when copy is EntityTargetActionEvent entityEv:
                entity.Event = entityEv;
                break;
            case WorldTargetActionComponent world when copy is WorldTargetActionEvent worldEv:
                world.Event = worldEv;
                break;
            case EntityWorldTargetActionComponent entityWorld when copy is EntityWorldTargetActionEvent entityWorldEv:
                entityWorld.Event = entityWorldEv;
                break;
            default:
                Log.Error($"Event {ev.GetType()} doesn't fit action {ToPrettyString(action)}");
                return false;
        }

        Dirty(action);
        return true;
    }

    /// <summary>Sets the target of an entity-targeted action's event.</summary>
    public void SetEventTarget(EntityUid? uid, EntityUid target)
    {
        if (GetAction(uid) is not { Comp: EntityTargetActionComponent { Event: { } ev } } action)
            return;

        ev.Target = target;
        Dirty(action);
    }

    public void SetIcon(EntityUid? uid, SpriteSpecifier? icon)
    {
        if (GetAction(uid) is not { } action)
            return;

        action.Comp.Icon = icon;
        Dirty(action);
        _actions.UpdateAction(action, action.Comp);
    }

    public void SetIconOn(EntityUid? uid, SpriteSpecifier? icon)
    {
        if (GetAction(uid) is not { } action)
            return;

        action.Comp.IconOn = icon;
        Dirty(action);
        _actions.UpdateAction(action, action.Comp);
    }

    public bool IsCooldownActive(EntityUid? uid, TimeSpan? curTime = null)
    {
        return GetAction(uid, false) is { } action && IsCooldownActive(action.Comp, curTime);
    }

    public bool IsCooldownActive(BaseActionComponent action, TimeSpan? curTime = null)
    {
        curTime ??= _timing.CurTime;
        return action.Cooldown is { } cooldown && cooldown.End > curTime;
    }

    /// <summary>Clears the cooldown entirely, unlike <see cref="SharedActionsSystem.ClearCooldown"/> which ends it now.</summary>
    public void RemoveCooldown(EntityUid? uid)
    {
        if (GetAction(uid) is not { } action || action.Comp.Cooldown == null)
            return;

        action.Comp.Cooldown = null;
        Dirty(action);
    }

    /// <summary>Moves the shared action state onto another action component of the same entity, for swapping action types.</summary>
    public void CopyBaseActionData(BaseActionComponent from, BaseActionComponent to)
    {
        to.Icon = from.Icon;
        to.IconOn = from.IconOn;
        to.BackgroundOn = from.BackgroundOn;
        to.IconColor = from.IconColor;
        to.OriginalIconColor = from.OriginalIconColor;
        to.DisabledIconColor = from.DisabledIconColor;
        to.Keywords = new HashSet<string>(from.Keywords);
        to.Enabled = from.Enabled;
        to.Toggled = from.Toggled;
        to.Cooldown = from.Cooldown;
        to.StartDelay = from.StartDelay;
        to.UseDelay = from.UseDelay;
        to.Charges = from.Charges;
        to.MaxCharges = from.MaxCharges;
        to.RenewCharges = from.RenewCharges;
#pragma warning disable RA0002 // Container is written by the actions systems only; a type swap has to carry it over.
        to.Container = from.Container;
#pragma warning restore RA0002
        to.EntIcon = from.EntIcon;
        to.CheckCanInteract = from.CheckCanInteract;
        to.CheckConsciousness = from.CheckConsciousness;
        to.ClientExclusive = from.ClientExclusive;
        to.Priority = from.Priority;
        to.AttachedEntity = from.AttachedEntity;
        to.RaiseOnUser = from.RaiseOnUser;
        to.RaiseOnAction = from.RaiseOnAction;
        to.AutoPopulate = from.AutoPopulate;
        to.Temporary = from.Temporary;
        to.ItemIconStyle = from.ItemIconStyle;
        to.Sound = from.Sound;
    }

    // WOLFGATE END
}

[Serializable, NetSerializable]
public sealed partial class RMCMissedTargetActionEvent : EntityEventArgs
{
    public readonly NetEntity Action;
    public RMCMissedTargetActionEvent(NetEntity actionId)
    {
        Action = actionId;
    }
}
