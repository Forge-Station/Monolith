using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Hands;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Timing;

namespace Content.Shared._Forge.Weapons;

/// <summary>
/// Jump ability on items with <see cref="ItemToggleComponent"/> only works while toggled on.
/// Also disables the jump action in the UI while the item is off.
/// Shared cooldown is stored on the user so it persists when swapping or storing swords.
/// </summary>
public sealed class ForgeToggleJumpAbilitySystem : EntitySystem
{
    private const string SharedJumpDelayId = "ForgeToggleJump";

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpAbilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<JumpAbilityComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<InstantActionComponent, ActionAttemptEvent>(OnActionAttempt);
        SubscribeLocalEvent<InstantActionComponent, ActionPerformedEvent>(OnJumpActionPerformed);
        SubscribeLocalEvent<DidEquipHandEvent>(OnDidEquipHand, after: [typeof(SharedActionsSystem)]);
    }

    private void OnMapInit(Entity<JumpAbilityComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<ItemToggleComponent>(ent))
            return;

        UpdateJumpActionEnabled(ent, Comp<ItemToggleComponent>(ent).Activated);
    }

    private void OnToggled(Entity<JumpAbilityComponent> ent, ref ItemToggledEvent args)
    {
        UpdateJumpActionEnabled(ent, args.Activated);
    }

    private void OnDidEquipHand(DidEquipHandEvent args)
    {
        if (!HasComp<JumpAbilityComponent>(args.Equipped) || !HasComp<ItemToggleComponent>(args.Equipped))
            return;

        SyncJumpActionCooldown(args.User);
    }

    private void OnActionAttempt(Entity<InstantActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (ent.Comp.Event is not GravityJumpEvent)
            return;

        if (ent.Comp.Container is not { } container)
            return;

        if (!HasComp<JumpAbilityComponent>(container) || !TryComp<ItemToggleComponent>(container, out var toggle))
            return;

        if (TryComp<UseDelayComponent>(args.User, out var useDelay)
            && _useDelay.IsDelayed((args.User, useDelay), SharedJumpDelayId))
        {
            args.Cancelled = true;
            return;
        }

        if (toggle.Activated)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("forge-toggle-jump-requires-active"), args.User, args.User);
    }

    private void OnJumpActionPerformed(Entity<InstantActionComponent> ent, ref ActionPerformedEvent args)
    {
        if (ent.Comp.Event is not GravityJumpEvent)
            return;

        if (ent.Comp.Container is not { } container
            || !HasComp<JumpAbilityComponent>(container)
            || !HasComp<ItemToggleComponent>(container))
            return;

        var delay = ent.Comp.UseDelay ?? TimeSpan.FromSeconds(8);
        _useDelay.SetLength((args.Performer, null), delay, SharedJumpDelayId);

        if (TryComp<UseDelayComponent>(args.Performer, out var useDelay))
            _useDelay.TryResetDelay((args.Performer, useDelay), id: SharedJumpDelayId);

        SyncJumpActionCooldown(args.Performer);
    }

    private void SyncJumpActionCooldown(EntityUid user)
    {
        if (!TryComp<UseDelayComponent>(user, out var useDelay)
            || !_useDelay.IsDelayed((user, useDelay), SharedJumpDelayId)
            || !_useDelay.TryGetDelayInfo((user, useDelay), out var info, SharedJumpDelayId))
            return;

        foreach (var (actionId, action) in _actions.GetActions(user))
        {
            if (action is not InstantActionComponent instant || instant.Event is not GravityJumpEvent)
                continue;

            if (action.Container is not { } container
                || !HasComp<JumpAbilityComponent>(container)
                || !HasComp<ItemToggleComponent>(container))
                continue;

            _actions.SetCooldown(actionId, info.StartTime, info.EndTime);
            _actions.UpdateAction(actionId, action);
        }
    }

    private void UpdateJumpActionEnabled(EntityUid uid, bool enabled)
    {
        if (!TryComp<ActionGrantComponent>(uid, out var grant))
            return;

        foreach (var actionEnt in grant.ActionEntities)
        {
            _actions.SetEnabled(actionEnt, enabled);
        }
    }
}
