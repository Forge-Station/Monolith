using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using ClientPhysicsSystem = Robust.Client.Physics.PhysicsSystem;
using SharedMover = Content.Shared._WF.Vehicles.Grid.GridVehicleMoverSystem;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Client.Physics;

namespace Content.Client._WF.Vehicles.Grid;

/// <summary>Client side of the grid mover: marks the driven vehicle as predicted and owns the debug overlays.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private ClientPhysicsSystem _physics = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public static readonly List<Vector2> DebugCollisionPositions = new();

    private GridVehicleMoverOverlay? _overlay;
    private EntityUid? _lastPredictedVehicle;

    public override void Initialize()
    {
        _overlay = new GridVehicleMoverOverlay(EntityManager);

        SubscribeLocalEvent<GridVehicleMoverComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
    }

    /// <summary>Toggles the overlay of fixtures, facing and tested tiles; returns whether it is on.</summary>
    public bool ToggleDebugOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.DebugEnabled = !_overlay.DebugEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.DebugEnabled;
    }

    /// <summary>Toggles the overlay of probes and what blocked them; returns whether it is on.</summary>
    public bool ToggleCollisionOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.CollisionsEnabled = !_overlay.CollisionsEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.CollisionsEnabled;
    }

    /// <summary>Toggles the overlay of how each drive step was decided; returns whether it is on.</summary>
    public bool ToggleMovementOverlay()
    {
        if (_overlay == null)
            return false;

        _overlay.MovementEnabled = !_overlay.MovementEnabled;
        RefreshSharedDebugFlags();
        RefreshVehicleDebugOverlay();
        return _overlay.MovementEnabled;
    }

    public override void Shutdown()
    {
        if (_overlay == null)
            return;

        _overlayManager.RemoveOverlay(_overlay);

        // The flags and lists live on the shared mover, which outlasts this system (a reconnect builds a new one).
        _overlay.DebugEnabled = false;
        _overlay.CollisionsEnabled = false;
        _overlay.MovementEnabled = false;
        RefreshSharedDebugFlags();
        SharedMover.DebugTestedTiles.Clear();
        SharedMover.DebugCollisionProbes.Clear();
        SharedMover.DebugCollisions.Clear();
        SharedMover.DebugMovementDecisions.Clear();
        DebugCollisionPositions.Clear();
    }

    private void RefreshSharedDebugFlags()
    {
        SharedMover.CollisionDebugEnabled =
            _overlay is { DebugEnabled: true } or { CollisionsEnabled: true };
        SharedMover.MovementDebugEnabled =
            _overlay is { MovementEnabled: true };
    }

    private void RefreshVehicleDebugOverlay()
    {
        if (_overlay == null)
            return;

        var enabled = _overlay.DebugEnabled || _overlay.CollisionsEnabled || _overlay.MovementEnabled;
        var hasOverlay = _overlayManager.HasOverlay<GridVehicleMoverOverlay>();
        if (enabled && !hasOverlay)
        {
            _overlayManager.AddOverlay(_overlay);
        }
        else if (!enabled && hasOverlay)
        {
            _overlayManager.RemoveOverlay(_overlay);
        }
    }

    private void OnUpdateIsPredicted(Entity<GridVehicleMoverComponent> ent, ref UpdateIsPredictedEvent args)
    {
        if (_playerManager.LocalEntity is not { } local)
            return;

        if (!TryComp(ent.Owner, out RMCVehicleComponent? vehicle))
            return;

        if (vehicle.Operator == local)
            args.IsPredicted = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_playerManager.LocalEntity is not { } local)
        {
            if (_lastPredictedVehicle is { } oldVehicle)
                _physics.UpdateIsPredicted(oldVehicle);
            _lastPredictedVehicle = null;
            return;
        }

        if (TryComp(local, out RMCVehicleOperatorComponent? op) && op.Vehicle is { } vehicle)
        {
            if (_lastPredictedVehicle != vehicle)
            {
                if (_lastPredictedVehicle is { } oldVehicle)
                    _physics.UpdateIsPredicted(oldVehicle);

                _lastPredictedVehicle = vehicle;
                _physics.UpdateIsPredicted(vehicle);
            }
            return;
        }

        if (_lastPredictedVehicle is { } oldPredicted)
        {
            _physics.UpdateIsPredicted(oldPredicted);
            _lastPredictedVehicle = null;
        }
    }
}
