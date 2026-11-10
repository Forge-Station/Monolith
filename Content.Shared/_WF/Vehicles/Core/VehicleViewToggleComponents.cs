using System.Collections.Generic;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>On whoever can swap their view between a vehicle's outside and inside, with the one action that does it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleViewToggleSystem))]
public sealed partial class VehicleViewToggleComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId ActionId = "ActionVehicleToggleView";

    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField, AutoNetworkedField]
    public EntityUid? OutsideTarget;

    [DataField, AutoNetworkedField]
    public EntityUid? InsideTarget;

    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField, AutoNetworkedField]
    public bool IsOutside;

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> Sources = new();
}

/// <summary>The view action: swaps the eye between the vehicle's outside and the previous target.</summary>
public sealed partial class VehicleToggleViewActionEvent : InstantActionEvent;

/// <summary>Raised on a user when their view changed between the vehicle's outside and the inside.</summary>
[ByRefEvent]
public readonly record struct VehicleViewToggledEvent(bool IsOutside);
