using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>The driver's switch between free steering and the four-way drive, kept on the vehicle.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    public static readonly EntProtoId SteeringActionId = "WFActionVehicleSteeringMode";

    private void InitializeSteeringMode()
    {
        SubscribeLocalEvent<GridVehicleMoverComponent, VehicleOperatorSetEvent>(OnSteeringOperatorSet);
        SubscribeLocalEvent<RMCVehicleOperatorComponent, VehicleSteeringModeActionEvent>(OnSteeringModeAction);
    }

    private void OnSteeringOperatorSet(Entity<GridVehicleMoverComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (_net.IsClient)
            return;

        RemoveSteeringAction(ent.Comp, args.OldOperator);

        if (args.NewOperator is not { } user || TerminatingOrDeleted(user))
            return;

        EntityUid? action = null;
        if (!_actions.AddAction(user, ref action, SteeringActionId))
            return;

        ent.Comp.SteeringAction = action;
        _actions.SetToggled(action, ent.Comp.FreeSteering);
    }

    private void RemoveSteeringAction(GridVehicleMoverComponent mover, EntityUid? user)
    {
        if (mover.SteeringAction is not { } action)
            return;

        mover.SteeringAction = null;
        if (TerminatingOrDeleted(action))
            return;

        if (user is { } holder)
            _actions.RemoveAction(holder, action);
        else
            _actions.RemoveAction(action);

        QueueDel(action);
    }

    private void OnSteeringModeAction(Entity<RMCVehicleOperatorComponent> ent, ref VehicleSteeringModeActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Vehicle is not { } vehicle ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp) ||
            vehicleComp.Operator != ent.Owner ||
            !TryComp(vehicle, out GridVehicleMoverComponent? mover))
        {
            return;
        }

        if (TrySetFreeSteering((vehicle, mover), !mover.FreeSteering, ent.Owner))
            args.Handled = true;
    }

    /// <summary>
    /// Switches a vehicle between free steering and the four-way drive. Going to the four-way drive squares the hull to
    /// the nearest of the four directions, which needs room; without it nothing changes and the driver is told.
    /// </summary>
    public bool TrySetFreeSteering(Entity<GridVehicleMoverComponent> ent, bool free, EntityUid? user = null)
    {
        var mover = ent.Comp;
        if (mover.FreeSteering == free)
            return true;

        if (free)
        {
            if (TryGetLocalFacing(mover, out var facing))
                mover.Heading = facing.Reduced();
        }
        else if (mover.CurrentDirection != Vector2i.Zero)
        {
            var xform = Transform(ent);
            if (xform.GridUid is not { } grid || !gridQ.TryComp(grid, out var gridComp))
                return false;

            var direction = GridVehicleGeometry.CardinalFromRotation(mover.Heading);
            var squared = DirectionToVehicleRotation(direction);
            if (!TryFindTurnPosition(ent, mover, grid, gridComp, squared, out var position) ||
                !CanOccupyTransform(ent, mover, grid, position, squared, Clearance, applyEffects: false))
            {
                if (user is { } driver)
                    _popup.PopupClient(Loc.GetString("wf-vehicle-steering-no-room"), driver, driver, PopupType.SmallCaution);

                return false;
            }

            mover.Position = position;
            mover.CurrentTile = GetTile(grid, gridComp, position);
            mover.CurrentDirection = direction;
            _transform.SetLocalRotation(ent, squared, xform);
        }

        mover.FreeSteering = free;
        mover.AngularVelocityDegrees = 0f;
        Dirty(ent, mover);

        if (mover.SteeringAction is { } action)
            _actions.SetToggled(action, free);

        if (user is { } operatorUid)
        {
            _popup.PopupClient(
                Loc.GetString(free ? "wf-vehicle-steering-free" : "wf-vehicle-steering-fixed"),
                operatorUid,
                operatorUid);
        }

        return true;
    }
}

/// <summary>Raised on the driver when they use the steering switch.</summary>
public sealed partial class VehicleSteeringModeActionEvent : InstantActionEvent;
