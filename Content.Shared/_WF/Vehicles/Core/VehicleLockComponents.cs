using System;
using System.Collections.Generic;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Tools;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>A vehicle's door lock: locked or not, broken open, and the key id its keys must match.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleLockSystem), typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleLockComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Locked;

    [DataField, AutoNetworkedField]
    public bool Broken;

    [DataField, AutoNetworkedField]
    public string? KeyId;

    [DataField]
    public ProtoId<ToolQualityPrototype> BreakToolQuality = "Cutting";

    [DataField]
    public TimeSpan BreakDelay = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan BreakAlarmInterval = TimeSpan.FromSeconds(2.5);

    [DataField]
    public TimeSpan BreakAlarmFlashDuration = TimeSpan.FromSeconds(0.6);

    [DataField]
    public ProtoId<ToolQualityPrototype> RepairToolQuality = "Screwing";

    [DataField]
    public TimeSpan RepairDelay = TimeSpan.FromSeconds(10);

    public int AlarmToken;
}

/// <summary>On a driver: the lock action they were given and the vehicle it works on.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleLockSystem))]
public sealed partial class VehicleLockActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId ActionId = "ActionVehicleLock";

    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;

    [DataField]
    public HashSet<EntityUid> Sources = new();
}

/// <summary>The lock action: toggles the vehicle's lock from the driver seat.</summary>
public sealed partial class VehicleLockActionEvent : InstantActionEvent;

/// <summary>Ends the do-after of cutting a vehicle's lock.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleLockBreakDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Ends the do-after of repairing a cut lock.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleLockRepairDoAfterEvent : SimpleDoAfterEvent;

/// <summary>A key: bound to one vehicle's key id, or blank and waiting to be imprinted, or a duplicator.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleLockSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleKeyComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? KeyId;

    [DataField, AutoNetworkedField]
    public VehicleKeyMode Mode = VehicleKeyMode.Normal;
}

/// <summary>A key is bound to a vehicle, blank until imprinted, or a duplicator.</summary>
[Serializable, NetSerializable]
public enum VehicleKeyMode : byte
{
    Normal,
    Blank,
    Duplicator,
}
