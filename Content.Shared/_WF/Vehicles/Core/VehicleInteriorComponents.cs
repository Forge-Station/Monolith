using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Who holds a doorway, exit or charge while their do-after runs; lapses with its user or at <see cref="Until"/>.</summary>
public readonly record struct VehicleBusyHold(EntityUid User, TimeSpan Until);

/// <summary>Server-side state of a vehicle's loaded interior: its map and grid, where entrants land and who is inside.</summary>
[RegisterComponent]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleInteriorComponent : Component
{
    public EntityUid Map = EntityUid.Invalid;
    public MapId MapId = MapId.Nullspace;
    public EntityCoordinates Entry;
    public EntityUid EntryParent = EntityUid.Invalid;
    public EntityUid Grid = EntityUid.Invalid;
    public Dictionary<int, VehicleBusyHold> EntryLocks = new();
    public HashSet<EntityUid> Passengers = new();
}

/// <summary>On an interior map: the vehicle it belongs to.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleInteriorLinkComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle = EntityUid.Invalid;
}

/// <summary>
/// On someone strapped into a driver seat who could not take the wheel because they could not act (out cold, asleep):
/// they become the operator once they can. Removed when they take the wheel or leave the seat.
/// </summary>
[RegisterComponent]
[Access(typeof(VehicleInteriorSystem))]
public sealed partial class VehicleDriverRecoveryComponent : Component;

/// <summary>On whoever is inside a vehicle: which one. Removed when they leave the interior's map.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleInteriorOccupantComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Vehicle = EntityUid.Invalid;
}
