using Content.Shared._WF.VehicleShop;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MilitaryVehicles;

/// <summary>
/// An orbital drop beacon: set off on a planet's open ground, it counts down and the vehicle drops from orbit onto it.
/// Its catalogue lists the beacon itself; an unused one resells for no more than that listing.
/// </summary>
[RegisterComponent]
public sealed partial class WFOrbitalDropBeaconComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Vehicle;

    /// <summary>The catalogue that sells the beacon, whose listing caps what it resells for.</summary>
    [DataField(required: true)]
    public ProtoId<WFVehicleCatalogPrototype> Catalog;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>How long before the drop the inbound rumble starts.</summary>
    [DataField]
    public TimeSpan RumbleLead = TimeSpan.FromSeconds(6);

    /// <summary>The share of the lower of the vehicle's value and its listing an unused beacon sells for.</summary>
    [DataField]
    public float SellFraction = 0.75f;

    [DataField]
    public SoundSpecifier BeepSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier RumbleSound = new SoundPathSpecifier("/Audio/_WF/Planets/Flight/fall_rumble.ogg");

    [DataField]
    public SoundSpecifier LandSound = new SoundPathSpecifier("/Audio/_WF/Planets/Flight/hard_landing.ogg");

    /// <summary>Thrown up round the vehicle where it lands.</summary>
    [DataField]
    public EntProtoId LandEffect = "CEDustEffect";

    /// <summary>When the vehicle lands; null while it isn't counting down.</summary>
    [ViewVariables]
    public TimeSpan? LandsAt;

    [ViewVariables]
    public TimeSpan NextBeep;

    [ViewVariables]
    public bool Rumbled;

    /// <summary>Who set it off, handed the key if still about.</summary>
    [ViewVariables]
    public EntityUid? ArmedBy;
}

/// <summary>
/// A one-use military repair kit: makes a vehicle of any size whole, frame and parts. Used on an NPC crew's vehicle
/// with no living crew aboard, it also claims it: re-keyed to the user and marked salvaged.
/// </summary>
[RegisterComponent]
public sealed partial class WFMilitaryRepairKitComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(15);

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
}

[Serializable, NetSerializable]
public sealed partial class WFMilitaryRepairKitDoAfterEvent : SimpleDoAfterEvent;
