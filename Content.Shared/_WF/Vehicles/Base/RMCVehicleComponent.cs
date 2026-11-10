using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Base;

/// <summary>A driveable RMC-style vehicle; kept apart from Wolfgate's ride-on <c>Vehicle</c> component.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCVehicleSystem))]
public sealed partial class RMCVehicleComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? OperatorWhitelist;

    [DataField, AutoNetworkedField]
    public VehicleMovementKind MovementKind = VehicleMovementKind.Standard;
}

/// <summary>Appearance keys a vehicle publishes for its sprite.</summary>
[Serializable, NetSerializable]
public enum RMCVehicleVisuals : byte
{
    HasOperator,
    CanRun
}

/// <summary>How a vehicle moves: <c>Grid</c> is driven by the grid mover; <c>Standard</c> has an operator and no movement.</summary>
[Serializable, NetSerializable]
public enum VehicleMovementKind : byte
{
    Standard,
    Grid
}

/// <summary>Raised on a vehicle when an operator takes the wheel.</summary>
[ByRefEvent, UsedImplicitly]
public readonly record struct OnVehicleEnteredEvent(Entity<RMCVehicleComponent> Vehicle, EntityUid Operator);

/// <summary>Raised on a vehicle when its operator lets go.</summary>
[ByRefEvent, UsedImplicitly]
public readonly record struct OnVehicleExitedEvent(Entity<RMCVehicleComponent> Vehicle, EntityUid Operator);

/// <summary>Raised on a vehicle after its operator changed, with the new and the old one.</summary>
[ByRefEvent, UsedImplicitly]
public readonly record struct VehicleOperatorSetEvent(EntityUid? NewOperator, EntityUid? OldOperator);

/// <summary>Asked of a vehicle whenever it is checked for running; a handler clears <c>CanRun</c> to stop it (an empty required hardpoint slot, say).</summary>
[ByRefEvent, UsedImplicitly]
public record struct VehicleCanRunEvent(Entity<RMCVehicleComponent> Vehicle, bool CanRun = true);
