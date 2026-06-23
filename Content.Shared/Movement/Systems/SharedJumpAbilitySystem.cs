using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Content.Shared.Actions; // Forge-Change
using Content.Shared.Actions.Events; // Forge-Change
using Content.Shared.Hands; // Forge-Change
using Content.Shared.Item.ItemToggle; // Forge-Change
using Content.Shared.Item.ItemToggle.Components; // Forge-Change
using Content.Shared.Popups; // Forge-Change
using Content.Shared.Timing; // Forge-Change

namespace Content.Shared.Movement.Systems;

public sealed partial class SharedJumpAbilitySystem : EntitySystem
{
    // Forge-Change: shared cooldown id for toggleable jump abilities (persists when item is stored).
    private const string ToggleJumpDelayId = "ToggleJumpAbility";

    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedActionsSystem _actions = default!; // Forge-Change
    [Dependency] private SharedPopupSystem _popup = default!; // Forge-Change
    [Dependency] private UseDelaySystem _useDelay = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpAbilityComponent, GravityJumpEvent>(OnGravityJump);

        // Forge-Change-start: toggle-gated jump with user-side cooldown.
        SubscribeLocalEvent<JumpAbilityComponent, MapInitEvent>(OnToggleJumpMapInit);
        SubscribeLocalEvent<JumpAbilityComponent, ItemToggledEvent>(OnToggleJumpToggled);
        SubscribeLocalEvent<InstantActionComponent, ActionAttemptEvent>(OnToggleJumpActionAttempt);
        SubscribeLocalEvent<InstantActionComponent, ActionPerformedEvent>(OnToggleJumpActionPerformed);
        SubscribeLocalEvent<DidEquipHandEvent>(OnToggleJumpDidEquipHand, after: [typeof(SharedActionsSystem)]);
        // Forge-Change-end
    }

    private void OnGravityJump(Entity<JumpAbilityComponent> entity, ref GravityJumpEvent args)
    {
        if (_gravity.IsWeightless(args.Performer))
            return;

        var xform = Transform(args.Performer);
        var throwing = xform.LocalRotation.ToWorldVec() * entity.Comp.JumpDistance;
        var direction = xform.Coordinates.Offset(throwing); // to make the character jump in the direction he's looking


        _throwing.TryThrow(args.Performer, direction, entity.Comp.JumpThrowSpeed);

        _audio.PlayPredicted(entity.Comp.JumpSound, args.Performer, args.Performer);
        args.Handled = true;
    }

    // Forge-Change-start
    private void OnToggleJumpMapInit(Entity<JumpAbilityComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<ItemToggleComponent>(ent))
            return;

        UpdateToggleJumpActionEnabled(ent, Comp<ItemToggleComponent>(ent).Activated);
    }

    private void OnToggleJumpToggled(Entity<JumpAbilityComponent> ent, ref ItemToggledEvent args)
    {
        UpdateToggleJumpActionEnabled(ent, args.Activated);
    }

    private void OnToggleJumpDidEquipHand(DidEquipHandEvent args)
    {
        if (!HasComp<JumpAbilityComponent>(args.Equipped) || !HasComp<ItemToggleComponent>(args.Equipped))
            return;

        SyncToggleJumpActionCooldown(args.User);
    }

    private void OnToggleJumpActionAttempt(Entity<InstantActionComponent> ent, ref ActionAttemptEvent args)
    {
        if (ent.Comp.Event is not GravityJumpEvent)
            return;

        if (ent.Comp.Container is not { } container)
            return;

        if (!HasComp<JumpAbilityComponent>(container) || !TryComp<ItemToggleComponent>(container, out var toggle))
            return;

        if (TryComp<UseDelayComponent>(args.User, out var useDelay)
            && _useDelay.IsDelayed((args.User, useDelay), ToggleJumpDelayId))
        {
            args.Cancelled = true;
            return;
        }

        if (toggle.Activated)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("jump-ability-requires-active"), args.User, args.User);
    }

    private void OnToggleJumpActionPerformed(Entity<InstantActionComponent> ent, ref ActionPerformedEvent args)
    {
        if (ent.Comp.Event is not GravityJumpEvent)
            return;

        if (ent.Comp.Container is not { } container
            || !HasComp<JumpAbilityComponent>(container)
            || !HasComp<ItemToggleComponent>(container))
            return;

        var delay = ent.Comp.UseDelay ?? TimeSpan.FromSeconds(8);
        _useDelay.SetLength((args.Performer, null), delay, ToggleJumpDelayId);

        if (TryComp<UseDelayComponent>(args.Performer, out var useDelay))
            _useDelay.TryResetDelay((args.Performer, useDelay), id: ToggleJumpDelayId);

        SyncToggleJumpActionCooldown(args.Performer);
    }

    private void SyncToggleJumpActionCooldown(EntityUid user)
    {
        if (!TryComp<UseDelayComponent>(user, out var useDelay)
            || !_useDelay.IsDelayed((user, useDelay), ToggleJumpDelayId)
            || !_useDelay.TryGetDelayInfo((user, useDelay), out var info, ToggleJumpDelayId))
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

    private void UpdateToggleJumpActionEnabled(EntityUid uid, bool enabled)
    {
        if (!TryComp<ActionGrantComponent>(uid, out var grant))
            return;

        foreach (var actionEnt in grant.ActionEntities)
        {
            _actions.SetEnabled(actionEnt, enabled);
        }
    }
    // Forge-Change-end
}
