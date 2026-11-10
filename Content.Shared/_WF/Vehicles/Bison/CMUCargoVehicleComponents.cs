using Content.Shared.FixedPoint;
using Content.Shared.StatusIcon;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Bison;

/// <summary>How a Bison's self-destruct was armed; it cannot be disarmed.</summary>
[Serializable, NetSerializable]
public enum CMUCargoVehicleArmingMode : byte
{
    None,
    Manual,
    Automatic,
}

/// <summary>Appearance keys of the Bison's sprite.</summary>
[Serializable, NetSerializable]
public enum CMUCargoVehicleVisuals : byte
{
    BayOpen,
}

/// <summary>A Bison remote cargo carrier: one crate in a cargo bay, driven by a mind visit from a paired controller, with a self-destruct.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUCargoVehicleComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool BayOpen;

    [DataField, AutoNetworkedField]
    public EntityUid? Controller;

    [DataField, AutoNetworkedField]
    public CMUCargoVehicleArmingMode ArmingMode;

    [DataField]
    public string CargoContainerId = "cmu-cargo-crate";

    /// <summary>An entity storage is loadable when its prototype is one of these or inherits from one.</summary>
    [DataField]
    public List<EntProtoId> CrateParents = new() { "CrateGeneric" };

    [DataField]
    public FixedPoint2 AutomaticArmDamage = FixedPoint2.New(300);

    [DataField]
    public TimeSpan CargoDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public float DetonationDelay = 5f;

    [DataField]
    public float BeepInterval = 1f;

    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/_WF/Vehicles/effects/reset_key_shortbeep.ogg");

    [DataField]
    public SoundSpecifier RampSound = new SoundPathSpecifier("/Audio/_WF/Vehicles/effects/hydraulics_1.ogg");

    /// <summary>Played when the carrier hits something hard at <see cref="CollisionSoundMinSpeed"/> or more.</summary>
    [DataField]
    public SoundSpecifier? CollisionSound;

    [DataField]
    public float CollisionSoundCooldown = 1.5f;

    [DataField]
    public float CollisionSoundMinSpeed = 4f;

    [DataField]
    public EntProtoId WreckPrototype = "CMUCargoCarrierWreck";

    [DataField]
    public List<EntProtoId> DebrisPrototypes = new()
    {
        "CMUCargoCarrierDebris",
        "CMUCargoCarrierDebrisSide",
        "CMUCargoCarrierDebrisHood",
        "CMUCargoCarrierDebrisBumper",
    };

    /// <summary>Tiles from the carrier, counted as a diamond, that catch fire when it blows up.</summary>
    [DataField]
    public int FireRange = 1;

    /// <summary>The stage of the tile fire the blast leaves.</summary>
    [DataField]
    public int FireStage = 2;

    [DataField]
    public int MinimumDebris = 4;

    [DataField]
    public int MaximumDebris = 7;

    public ContainerSlot? CargoContainer;

    public TimeSpan NextCollisionSound;
}

/// <summary>The handheld controller; used on a carrier it pairs with it, used in hand it starts or ends remote control.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUCargoVehicleControllerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? LinkedVehicle;
}

/// <summary>On the body of whoever is driving a carrier, left behind while their mind visits it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUCargoVehicleRemotePilotComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Vehicle;

    [DataField, AutoNetworkedField]
    public EntityUid Controller;

    [DataField, AutoNetworkedField]
    public EntityUid MindId;

    public bool HadSsdIndicator;

    public ProtoId<SsdIconPrototype> SsdIndicatorIcon = "SSDIcon";

    /// <summary>The map the body was on when control began; control ends when it is on another.</summary>
    public MapId Map;
}

/// <summary>On a carrier while someone drives it: who, with which controller, and the actions the visit grants.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUCargoVehicleControlSessionComponent : Component
{
    public bool Ending;

    [DataField, AutoNetworkedField]
    public EntityUid Operator;

    [DataField, AutoNetworkedField]
    public EntityUid Controller;

    [DataField, AutoNetworkedField]
    public EntityUid MindId;

    [DataField, AutoNetworkedField]
    public EntityUid? ReturnAction;

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleBayAction;

    [DataField, AutoNetworkedField]
    public EntityUid? SelfDestructAction;

    /// <summary>The map the carrier was on when control began; control ends when it is on another.</summary>
    public MapId Map;
}
