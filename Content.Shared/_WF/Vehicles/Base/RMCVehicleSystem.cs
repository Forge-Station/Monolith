using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Whitelist;
using JetBrains.Annotations;

namespace Content.Shared._WF.Vehicles.Base;

/// <summary>Tracks who operates an RMC-style vehicle and wires the grid mover to that operator.</summary>
public sealed partial class RMCVehicleSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private EntityWhitelistSystem _entityWhitelist = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCVehicleComponent, UpdateCanMoveEvent>(OnVehicleUpdateCanMove);
        SubscribeLocalEvent<RMCVehicleComponent, ComponentShutdown>(OnVehicleShutdown);
        SubscribeLocalEvent<RMCVehicleComponent, GetAdditionalAccessEvent>(OnVehicleGetAdditionalAccess);

        SubscribeLocalEvent<RMCVehicleOperatorComponent, ComponentShutdown>(OnOperatorShutdown);
    }

    private void OnVehicleUpdateCanMove(Entity<RMCVehicleComponent> ent, ref UpdateCanMoveEvent args)
    {
        var ev = new VehicleCanRunEvent(ent);
        RaiseLocalEvent(ent, ref ev);
        if (!ev.CanRun)
            args.Cancel();
    }

    private void OnVehicleShutdown(Entity<RMCVehicleComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator(ent);
    }

    private void OnVehicleGetAdditionalAccess(Entity<RMCVehicleComponent> ent, ref GetAdditionalAccessEvent args)
    {
        // A vehicle that is its own operator (the Bison) adds nothing: the access search recurses into everything added, and
        // would recurse into the vehicle forever.
        if (ent.Comp.Operator is { } operatorUid && operatorUid != ent.Owner)
            args.Entities.Add(operatorUid);
    }

    private void OnOperatorShutdown(Entity<RMCVehicleOperatorComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator((ent, ent));
    }

    /// <summary>Makes an entity the vehicle's operator, replacing the current one unless told not to.</summary>
    public bool TrySetOperator(Entity<RMCVehicleComponent> entity, EntityUid? uid, bool removeExisting = true)
    {
        if (entity.Comp.Operator == null && uid is null)
            return false;

        if (TryComp<RMCVehicleOperatorComponent>(uid, out var eOperator))
        {
            if (eOperator.Vehicle == entity.Owner)
                return true;

            RemComp<RMCVehicleOperatorComponent>(uid!.Value);
        }

        if (!removeExisting && entity.Comp.Operator is not null)
            return false;

        if (uid != null && !CanOperate(entity.AsNullable(), uid.Value))
            return false;

        var oldOperator = entity.Comp.Operator;

        if (entity.Comp.Operator is { } currentOperator && TryComp<RMCVehicleOperatorComponent>(currentOperator, out var currentOperatorComponent))
        {
            var exitEvent = new OnVehicleExitedEvent(entity, currentOperator);
            RaiseLocalEvent(currentOperator, ref exitEvent);

            currentOperatorComponent.Vehicle = null;
            RemCompDeferred<RMCVehicleOperatorComponent>(currentOperator);
            RemCompDeferred<RelayInputMoverComponent>(currentOperator);
            RemCompDeferred<GridVehicleOperatorComponent>(currentOperator);
            RemCompDeferred<VehicleWatchingComponent>(currentOperator);
        }

        entity.Comp.Operator = uid;

        if (uid != null)
        {
            var vehicleOperator = AddComp<RMCVehicleOperatorComponent>(uid.Value);
            vehicleOperator.Vehicle = entity.Owner;
            Dirty(uid.Value, vehicleOperator);

            if (entity.Comp.MovementKind == VehicleMovementKind.Grid)
            {
                EnsureComp<GridVehicleMoverComponent>(entity.Owner);
                EnsureComp<GridVehicleOperatorComponent>(uid.Value);
                RemCompDeferred<RelayInputMoverComponent>(uid.Value);
                RemCompDeferred<MovementRelayTargetComponent>(entity);

                var watching = EnsureComp<VehicleWatchingComponent>(uid.Value);
                watching.Watching = entity.Owner;
                Dirty(uid.Value, watching);
            }

            var enterEvent = new OnVehicleEnteredEvent(entity, uid.Value);
            RaiseLocalEvent(uid.Value, ref enterEvent);
        }
        else
        {
            RemCompDeferred<MovementRelayTargetComponent>(entity);
        }

        RefreshCanRun((entity, entity.Comp));

        var setEvent = new VehicleOperatorSetEvent(uid, oldOperator);
        RaiseLocalEvent(entity, ref setEvent);

        Dirty(entity);
        return true;
    }

    /// <summary>Clears the vehicle's operator.</summary>
    [PublicAPI]
    public bool TryRemoveOperator(Entity<RMCVehicleComponent> entity)
    {
        return TrySetOperator(entity, null, removeExisting: true);
    }

    /// <summary>Stops an operator operating its vehicle.</summary>
    [PublicAPI]
    public bool TryRemoveOperator(Entity<RMCVehicleOperatorComponent?> operatorEntity)
    {
        if (!Resolve(operatorEntity, ref operatorEntity.Comp, false))
            return true;

        if (!TryComp<RMCVehicleComponent>(operatorEntity.Comp.Vehicle, out var vehicle))
            return true;

        return TrySetOperator((operatorEntity.Comp.Vehicle.Value, vehicle), null, removeExisting: true);
    }

    /// <summary>Gets the vehicle's operator entity.</summary>
    [PublicAPI]
    public bool TryGetOperator(Entity<RMCVehicleComponent?> entity, [NotNullWhen(true)] out Entity<RMCVehicleOperatorComponent>? operatorEnt)
    {
        operatorEnt = null;
        if (!Resolve(entity, ref entity.Comp))
            return false;

        if (entity.Comp.Operator is not { } operatorUid)
            return false;

        if (!TryComp<RMCVehicleOperatorComponent>(operatorUid, out var operatorComponent))
            return false;

        operatorEnt = (operatorUid, operatorComponent);
        return true;
    }

    /// <summary>The vehicle's operator, or null.</summary>
    public EntityUid? GetOperatorOrNull(Entity<RMCVehicleComponent?> entity)
    {
        TryGetOperator(entity, out var operatorEnt);
        return operatorEnt;
    }

    /// <summary>Whether the vehicle has an operator.</summary>
    [PublicAPI]
    public bool HasOperator(Entity<RMCVehicleComponent?> entity)
    {
        return TryGetOperator(entity, out _);
    }

    /// <summary>Whether an entity may operate the vehicle: it passes the operator whitelist and can act.</summary>
    public bool CanOperate(Entity<RMCVehicleComponent?> entity, EntityUid uid)
    {
        if (!Resolve(entity, ref entity.Comp))
            return false;

        if (_entityWhitelist.IsWhitelistFail(entity.Comp.OperatorWhitelist, uid))
            return false;

        return _actionBlocker.CanConsciouslyPerformAction(uid);
    }

    /// <summary>Asks for the vehicle's movement permission to be checked again.</summary>
    public void RefreshCanRun(Entity<RMCVehicleComponent?> entity)
    {
        if (TerminatingOrDeleted(entity))
            return;

        if (!Resolve(entity, ref entity.Comp))
            return;

        _actionBlocker.UpdateCanMove(entity);
        UpdateAppearance((entity, entity.Comp));
    }

    private void UpdateAppearance(Entity<RMCVehicleComponent> entity)
    {
        if (!TryComp<AppearanceComponent>(entity, out var appearance))
            return;

        if (TryComp<InputMoverComponent>(entity, out var inputMover))
            _appearance.SetData(entity, RMCVehicleVisuals.CanRun, inputMover.CanMove, appearance);

        _appearance.SetData(entity, RMCVehicleVisuals.HasOperator, entity.Comp.Operator is not null, appearance);
    }
}
