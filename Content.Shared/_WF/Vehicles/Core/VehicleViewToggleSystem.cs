using System.Linq;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Gives a driver or peeker one action that swaps their eye between the vehicle's outside and its inside.</summary>
public sealed partial class VehicleViewToggleSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleViewToggleComponent, VehicleToggleViewActionEvent>(OnToggleViewAction);
        SubscribeLocalEvent<VehicleViewToggleComponent, ComponentShutdown>(OnToggleViewShutdown);
    }

    /// <summary>Gives a user the view action for a source, such as a driver seat or a viewport.</summary>
    public void EnableViewToggle(
        EntityUid user,
        EntityUid outsideTarget,
        EntityUid source,
        EntityUid? insideTarget,
        bool isOutside)
    {
        if (TerminatingOrDeleted(user))
            return;

        var toggle = EnsureComp<VehicleViewToggleComponent>(user);
        toggle.Sources.Add(source);
        toggle.Source = source;
        toggle.OutsideTarget = outsideTarget;
        toggle.InsideTarget = insideTarget;
        toggle.IsOutside = isOutside;

        EnsureSingleToggleAction(user, toggle);

        if (_actions.TryGetActionData(toggle.Action, out var actionComp, logError: false))
        {
            actionComp.ItemIconStyle = ItemActionIconStyle.BigAction;
            actionComp.EntIcon = null;
            actionComp.Temporary = false;
            Dirty(toggle.Action!.Value, actionComp);
        }

        UpdateActionState(toggle);
        Dirty(user, toggle);
    }

    /// <summary>Takes a source's view action from a user; the last source removes it.</summary>
    public void DisableViewToggle(EntityUid user, EntityUid source)
    {
        if (!TryComp(user, out VehicleViewToggleComponent? toggle))
            return;

        toggle.Sources.Remove(source);

        // A user that is going away keeps no action: the component's shutdown takes it back.
        if (TerminatingOrDeleted(user))
            return;

        if (toggle.Sources.Count > 0)
        {
            foreach (var remaining in toggle.Sources)
            {
                toggle.Source = remaining;
                break;
            }

            EnsureSingleToggleAction(user, toggle);
            UpdateActionState(toggle);
            Dirty(user, toggle);
            return;
        }

        if (toggle.Action is { } action)
            RemoveAndDeleteToggleAction(action);

        toggle.Action = null;
        toggle.Source = null;
        toggle.OutsideTarget = null;
        toggle.InsideTarget = null;
        toggle.IsOutside = false;

        RemComp<VehicleViewToggleComponent>(user);
    }

    private void OnToggleViewShutdown(Entity<VehicleViewToggleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Action is not { } action)
            return;

        RemoveAndDeleteToggleAction(action);
    }

    private void OnToggleViewAction(Entity<VehicleViewToggleComponent> ent, ref VehicleToggleViewActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner)
            return;

        args.Handled = true;

        if (ent.Comp.OutsideTarget == null || !TryComp(ent.Owner, out EyeComponent? eye))
            return;

        var outside = ent.Comp.OutsideTarget.Value;
        if (eye.Target == outside)
        {
            _eye.SetTarget(ent.Owner, ent.Comp.InsideTarget, eye);
            ent.Comp.IsOutside = false;
        }
        else
        {
            ent.Comp.InsideTarget = eye.Target;
            _eye.SetTarget(ent.Owner, outside, eye);
            ent.Comp.IsOutside = true;
        }

        EnsureSingleToggleAction(ent.Owner, ent.Comp);
        UpdateActionState(ent.Comp);
        Dirty(ent.Owner, ent.Comp);
        var toggledEv = new VehicleViewToggledEvent(ent.Comp.IsOutside);
        RaiseLocalEvent(ent.Owner, ref toggledEv);
    }

    private void UpdateActionState(VehicleViewToggleComponent toggle)
    {
        if (toggle.Action == null)
            return;

        _actions.SetToggled(toggle.Action, toggle.IsOutside);
    }

    private void EnsureSingleToggleAction(EntityUid user, VehicleViewToggleComponent toggle)
    {
        if (TerminatingOrDeleted(user))
            return;

        bool IsToggleActionPrototype(EntityUid actionUid)
        {
            if (TerminatingOrDeleted(actionUid) ||
                !TryComp(actionUid, out MetaDataComponent? metaData))
            {
                return false;
            }

            return metaData.EntityPrototype?.ID == toggle.ActionId.ToString();
        }

        bool IsLiveToggleAction(EntityUid actionUid)
        {
            if (!IsToggleActionPrototype(actionUid))
                return false;

            return _actions.TryGetActionData(actionUid, out var actionComp, logError: false) &&
                   actionComp.AttachedEntity == user;
        }

        if (TryComp(user, out ActionsContainerComponent? containerComp))
        {
            foreach (var action in containerComp.Container.ContainedEntities.ToArray())
            {
                if (!IsToggleActionPrototype(action))
                    continue;

                if (_actions.TryGetActionData(action, out var actionComp, logError: false) &&
                    actionComp.AttachedEntity == user)
                {
                    continue;
                }

                RemoveAndDeleteToggleAction(action);
            }
        }

        EntityUid? primaryAction = null;

        if (toggle.Action is { } existing &&
            IsLiveToggleAction(existing))
        {
            primaryAction = existing;
        }

        if (TryComp(user, out ActionsComponent? actionsComp))
        {
            foreach (var action in actionsComp.Actions.ToArray())
            {
                if (!IsLiveToggleAction(action))
                {
                    continue;
                }

                if (primaryAction == null)
                {
                    primaryAction = action;
                    continue;
                }

                if (action == primaryAction.Value)
                    continue;

                RemoveAndDeleteToggleAction(action);
            }
        }

        if (primaryAction == null)
            primaryAction = _actions.AddAction(user, toggle.ActionId);

        toggle.Action = primaryAction;

        if (toggle.Action is { } ensuredAction)
            _actions.SetEnabled(ensuredAction, true);
    }

    private void RemoveAndDeleteToggleAction(EntityUid action)
    {
        if (TerminatingOrDeleted(action))
            return;

        _actions.RemoveAction(action);

        // The action entity is networked; client-side queued deletion causes prediction errors.
        if (_net.IsClient)
            return;

        if (Exists(action))
            QueueDel(action);
    }
}
