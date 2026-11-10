using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>A doorway on the outside of a vehicle: where it is in the vehicle's frame and how close a user must stand.</summary>
[DataDefinition]
public sealed partial class VehicleEntryPoint
{
    [DataField(required: true)]
    public Vector2 Offset;

    [DataField]
    public float Radius = 0.6f;

    [DataField]
    public Vector2? InteriorCoords;
}

/// <summary>Makes a vehicle enterable: the interior map it loads on first use, how many can be inside and its doorways.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleEnterComponent : Component
{
    [DataField(required: true)]
    public ResPath InteriorPath;

    /// <summary>The most living occupants inside at once.</summary>
    [DataField]
    public int MaxPassengers = 0;

    [DataField]
    public List<VehicleEntryPoint> EntryPoints = new();

    [DataField]
    public float EnterDoAfter = 0f;

    [DataField]
    public float ExitDoAfter = 0f;

    /// <summary>Where an exit lands, in the vehicle's frame, when the exit marker names no doorway.</summary>
    [DataField]
    public Vector2 ExitOffset = Vector2.Zero;
}

/// <summary>The marker inside an interior that leads back out; <see cref="EntryIndex"/> picks the doorway it exits through.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleExitComponent : Component
{
    [DataField, AutoNetworkedField]
    public int EntryIndex;

    /// <summary>Whether someone is partway through leaving by this marker; lapses with <see cref="PendingExitUser"/> or <see cref="PendingExitUntil"/>.</summary>
    [NonSerialized]
    public bool PendingExit;

    [NonSerialized]
    public EntityUid PendingExitUser;

    [NonSerialized]
    public TimeSpan PendingExitUntil;
}

/// <summary>Ends the do-after of walking into a vehicle through a doorway.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleEnterDoAfterEvent : SimpleDoAfterEvent
{
    [DataField(required: true)]
    public int EntryIndex;

    public override DoAfterEvent Clone()
    {
        return new VehicleEnterDoAfterEvent
        {
            EntryIndex = EntryIndex,
        };
    }
}

/// <summary>Ends the do-after of leaving a vehicle through an exit marker.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleExitDoAfterEvent : SimpleDoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return new VehicleExitDoAfterEvent();
    }
}

/// <summary>A seat inside an interior whose occupant drives the vehicle.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehicleInteriorSystem))]
public sealed partial class VehicleDriverSeatComponent : Component;
