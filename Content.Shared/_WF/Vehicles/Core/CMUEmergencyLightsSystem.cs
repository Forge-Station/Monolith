using System;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Gives a vehicle's operator an action that cycles its emergency lightbar and spawns the lightbar for the chosen mode.</summary>
public sealed partial class CMUEmergencyLightsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CMUEmergencyLightsComponent, VehicleOperatorSetEvent>(OnOperatorSet);
        SubscribeLocalEvent<CMUEmergencyLightsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RMCVehicleOperatorComponent, CMUEmergencyLightsActionEvent>(OnAction);
    }

    private void OnOperatorSet(Entity<CMUEmergencyLightsComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (_net.IsClient)
            return;

        RemoveAction(ent, args.OldOperator);

        if (args.NewOperator is not { } driver)
            return;

        EntityUid? action = null;
        if (!_actions.AddAction(driver, ref action, ent.Comp.Action))
            return;

        ent.Comp.DriverAction = action;
        _actions.SetToggled(action, ent.Comp.Mode > 0);
    }

    private void OnShutdown(Entity<CMUEmergencyLightsComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsClient)
            return;

        RemoveAction(ent, null);

        if (ent.Comp.Lightbar is { } lightbar)
            QueueDel(lightbar);

        ent.Comp.Lightbar = null;
    }

    private void OnAction(Entity<RMCVehicleOperatorComponent> ent, ref CMUEmergencyLightsActionEvent args)
    {
        if (args.Handled ||
            ent.Comp.Vehicle is not { } vehicle ||
            !TryComp(vehicle, out CMUEmergencyLightsComponent? lights) ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp) ||
            vehicleComp.Operator != args.Performer)
        {
            return;
        }

        args.Handled = true;
        if (_net.IsClient)
            return;

        if (TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
            return;

        var mode = (lights.Mode + 1) % (lights.LightPrototypes.Count + 1);
        SetMode((vehicle, lights), mode);
        _popup.PopupEntity(Loc.GetString("cmu-emergency-lights-mode", ("mode", mode)), args.Performer, args.Performer);
    }

    /// <summary>Switches the lightbar to the mode, spawning or removing it; the mode is clamped to what the vehicle has.</summary>
    public void SetMode(Entity<CMUEmergencyLightsComponent> ent, int mode)
    {
        if (_net.IsClient)
            return;

        mode = Math.Clamp(mode, 0, ent.Comp.LightPrototypes.Count);
        if (ent.Comp.Mode == mode)
            return;

        ent.Comp.Mode = mode;
        Dirty(ent);

        if (ent.Comp.DriverAction is { } action)
            _actions.SetToggled(action, mode > 0);

        if (ent.Comp.Lightbar is { } lightbar)
            QueueDel(lightbar);

        ent.Comp.Lightbar = null;

        if (mode > 0)
            ent.Comp.Lightbar = SpawnAttachedTo(ent.Comp.LightPrototypes[mode - 1], new EntityCoordinates(ent, ent.Comp.Offset));
    }

    /// <summary>Takes the action back from its holder and deletes it.</summary>
    private void RemoveAction(Entity<CMUEmergencyLightsComponent> ent, EntityUid? holder)
    {
        if (ent.Comp.DriverAction is not { } action)
            return;

        ent.Comp.DriverAction = null;
        if (TerminatingOrDeleted(action))
            return;

        if (holder is { } user && !TerminatingOrDeleted(user))
            _actions.RemoveAction(user, action);
        else
            _actions.RemoveAction(action);

        QueueDel(action);
    }
}
