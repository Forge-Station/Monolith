using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.VehicleShop;

/// <summary>
/// A vehicle in a box: used in hand, it is set down and counts down, then opens into the vehicle where it lies if there
/// is room. Priced off its catalogue's listing.
/// </summary>
[RegisterComponent]
public sealed partial class WFVehicleBeaconComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Vehicle;

    /// <summary>The catalogue whose listing for the vehicle prices the beacon.</summary>
    [DataField(required: true)]
    public ProtoId<WFVehicleCatalogPrototype> Catalog;

    /// <summary>The key handed over with a vehicle the vehicle locks don't key, such as an ATV.</summary>
    [DataField]
    public EntProtoId? Key;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>The share of the lower of the vehicle's value and its listing an unopened beacon sells for.</summary>
    [DataField]
    public float SellFraction = 0.75f;

    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier OpenSound = new SoundPathSpecifier("/Audio/Effects/box_deploy.ogg");

    /// <summary>When it opens; null while it isn't counting down.</summary>
    [ViewVariables]
    public TimeSpan? OpensAt;

    [ViewVariables]
    public TimeSpan NextBeep;

    /// <summary>Who set it off, handed the key if still about.</summary>
    [ViewVariables]
    public EntityUid? ArmedBy;
}

[Serializable, NetSerializable]
public enum WFVehicleBeaconVisuals : byte
{
    Armed,
}

/// <summary>A one-use kit that makes a whole vehicle whole: its frame and every fitted part.</summary>
[RegisterComponent]
public sealed partial class WFVehicleRepairKitComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(8);

    /// <summary>Vehicles appraised above this are more than the kit can fix.</summary>
    [DataField]
    public double MaxVehicleValue = 40000;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
}

[Serializable, NetSerializable]
public sealed partial class WFVehicleRepairKitDoAfterEvent : SimpleDoAfterEvent;
