using System;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Actions;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Gives the operator an overcharge action while they drive and starts the burst when it is used.</summary>
public sealed partial class VehicleOverchargeSystem : EntitySystem
{
    public static readonly EntProtoId ActionId = "WFActionVehicleOvercharge";

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleOverchargeComponent, VehicleOperatorSetEvent>(OnOperatorSet);
        SubscribeLocalEvent<VehicleOverchargeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RMCVehicleOperatorComponent, VehicleOverchargeActionEvent>(OnOvercharge);
    }

    private void OnOperatorSet(Entity<VehicleOverchargeComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.OldOperator is { } oldOperator)
            RemoveAction(ent, oldOperator);

        if (args.NewOperator is { } newOperator)
            GrantAction(ent, newOperator);
    }

    private void OnShutdown(Entity<VehicleOverchargeComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient || ent.Comp.Action is not { } action)
            return;

        ent.Comp.Action = null;
        DeleteAction(action, null);
    }

    private void GrantAction(Entity<VehicleOverchargeComponent> ent, EntityUid user)
    {
        if (TerminatingOrDeleted(user))
            return;

        EntityUid? action = null;
        if (!_actions.AddAction(user, ref action, ActionId))
            return;

        ent.Comp.Action = action;
        _actions.SetUseDelay(action, TimeSpan.FromSeconds(ent.Comp.Cooldown));

        // The cooldown belongs to the vehicle, so a new driver sees what is left of it.
        var now = _timing.CurTime;
        if (ent.Comp.CooldownUntil > now)
            _actions.SetCooldown(action, now, ent.Comp.CooldownUntil);
    }

    private void RemoveAction(Entity<VehicleOverchargeComponent> ent, EntityUid user)
    {
        if (ent.Comp.Action is not { } action)
            return;

        ent.Comp.Action = null;
        DeleteAction(action, user);
    }

    /// <summary>Takes the action back and deletes it.</summary>
    private void DeleteAction(EntityUid action, EntityUid? user)
    {
        if (TerminatingOrDeleted(action))
            return;

        if (user is { } holder)
            _actions.RemoveAction(holder, action);
        else
            _actions.RemoveAction(action);

        QueueDel(action);
    }

    private void OnOvercharge(Entity<RMCVehicleOperatorComponent> ent, ref VehicleOverchargeActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Vehicle is not { } vehicle ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp) ||
            vehicleComp.Operator != ent.Owner)
        {
            return;
        }

        if (TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Running)
            return;

        if (!TryComp(vehicle, out VehicleOverchargeComponent? overcharge))
            return;

        var now = _timing.CurTime;
        if (overcharge.CooldownUntil > now || overcharge.ActiveUntil > now)
            return;

        overcharge.ActiveUntil = now + TimeSpan.FromSeconds(overcharge.Duration);
        overcharge.CooldownUntil = now + TimeSpan.FromSeconds(overcharge.Cooldown);

        if (overcharge.OverchargeSound != null)
            _audio.PlayPredicted(overcharge.OverchargeSound, vehicle, ent.Owner);

        Dirty(vehicle, overcharge);
        args.Handled = true;
    }
}

/// <summary>Raised on the operator when they use the overcharge action.</summary>
public sealed partial class VehicleOverchargeActionEvent : InstantActionEvent;
