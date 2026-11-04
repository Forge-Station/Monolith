using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Network;

namespace Content.Shared._WF.Vehicles.Scanner;

/// <summary>Gives whoever sits in a scanner seat the action that opens it, and points the scanner out from the vehicle.</summary>
public sealed class WFVehicleScannerSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private VehicleInteriorSystem _interiors = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFVehicleScannerComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WFVehicleScannerComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<RadarConsoleComponent, WFRadarOriginEvent>(OnRadarOrigin);
        SubscribeLocalEvent<BuckleComponent, WFVehicleScannerActionEvent>(OnScannerAction);
    }

    private void OnStrapped(Entity<WFVehicleScannerComponent> ent, ref StrappedEvent args)
    {
        if (_net.IsClient)
            return;

        _actions.AddAction(args.Buckle.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action, ent.Owner);
    }

    private void OnUnstrapped(Entity<WFVehicleScannerComponent> ent, ref UnstrappedEvent args)
    {
        _ui.CloseUi(ent.Owner, RadarConsoleUiKey.Key, args.Buckle.Owner);

        // The action stays in the seat for the next driver.
        if (!_net.IsClient)
            _actions.RemoveAction(args.Buckle.Owner, ent.Comp.ActionEntity);
    }

    private void OnScannerAction(Entity<BuckleComponent> ent, ref WFVehicleScannerActionEvent args)
    {
        if (args.Handled || ent.Comp.BuckledTo is not { } seat || !HasComp<WFVehicleScannerComponent>(seat))
            return;

        if (_interiors.TryGetVehicleFromInterior(seat, out var vehicle) &&
            TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
            return;

        _ui.TryToggleUi(seat, RadarConsoleUiKey.Key, ent.Owner);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;
        var query = EntityQueryEnumerator<WFVehicleScannerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_interiors.TryGetVehicleFromInterior(uid, out var vehicle) &&
                TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
                _ui.CloseUi(uid, RadarConsoleUiKey.Key);
        }
    }

    /// <summary>Any scanner inside a vehicle, seat or handheld, looks out from the vehicle.</summary>
    private void OnRadarOrigin(Entity<RadarConsoleComponent> ent, ref WFRadarOriginEvent args)
    {
        if (_interiors.TryGetVehicleFromInterior(ent.Owner, out var vehicle))
            args.Origin = vehicle;
    }
}
