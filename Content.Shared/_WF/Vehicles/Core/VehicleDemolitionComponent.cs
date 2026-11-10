using System;
using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Lets anyone rig a vehicle whose frame is destroyed with a charge; after a delay the wreck explodes and is deleted.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehicleInteriorSystem), Other = AccessPermissions.Read)]
public sealed partial class VehicleDemolitionComponent : Component
{
    [DataField]
    public EntityWhitelist Whitelist = new()
    {
        Components = new[] { "RMCVehicleDemolitionCharge" },
    };

    /// <summary>Seconds it takes to plant the charge.</summary>
    [DataField]
    public float DoAfter = 45f;

    /// <summary>Seconds between planting and the blast.</summary>
    [DataField]
    public float TimerDelay = 10f;

    [DataField]
    public float BeepInterval = 1f;

    [DataField]
    public SoundSpecifier? BeepSound = new SoundPathSpecifier("/Audio/Effects/beep1.ogg", AudioParams.Default.WithVolume(4));

    [DataField]
    public EntProtoId Explosion = "WFVehicleWreckExplosion";

    [DataField]
    public bool DeleteUsedItem = true;

    [DataField]
    public SoundSpecifier? PlantSound = new SoundPathSpecifier("/Audio/Effects/beep1.ogg");

    /// <summary>Whether someone is planting a charge; lapses with <see cref="RiggingUser"/> or <see cref="RiggingUntil"/>.</summary>
    [ViewVariables]
    public bool Rigging;

    [ViewVariables]
    public EntityUid RiggingUser;

    [ViewVariables]
    public TimeSpan RiggingUntil;

    [ViewVariables]
    public bool Armed;

    [ViewVariables]
    public TimeSpan DetonateAt;

    [ViewVariables]
    public TimeSpan NextBeepAt;
}

/// <summary>Ends the do-after of planting a demolition charge on a wrecked vehicle.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleDemolitionDoAfterEvent : SimpleDoAfterEvent;
