using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Tether.Harpoon;

/// <summary>
/// An anchored, powered harpoon launcher a crew member buckles into. While manned the operator's shoot input
/// drives this entity's <c>Gun</c>, and the harpoon it fires drags a tow cable back to this hardpoint.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipHarpoonTurretComponent : Component
{
    /// <summary>Rope type the embedded harpoon is tied off with. Stage 2A owns the prototype.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RopeTypePrototype> RopeType = "RopeTowCable";

    /// <summary>Total width of the firing cone, centred on <see cref="MountRotation"/>.</summary>
    [DataField, AutoNetworkedField]
    public Angle Arc = Angle.FromDegrees(120);

    /// <summary>The rotation the hardpoint was bolted down at, in its grid's frame. The arc is measured from it.</summary>
    [DataField, AutoNetworkedField]
    public Angle MountRotation;

    /// <summary>Metres of cable taken in or paid out per second while reeling.</summary>
    [DataField]
    public float ReelRate = 1.5f;

    /// <summary>The winch stalls above this tension, in newtons.</summary>
    [DataField]
    public float ReelMaxTension = 120000f;

    /// <summary>Slack left over the straight-line distance when the cable is tied off.</summary>
    [DataField]
    public float Slack = 1.05f;

    [DataField]
    public SoundSpecifier ReelSound = new SoundPathSpecifier("/Audio/Weapons/reel.ogg");

    [DataField]
    public SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");

    /// <summary>The crew member currently buckled in, if any.</summary>
    [AutoNetworkedField]
    public NetEntity? Operator;

    /// <summary>The harpoon this turret has in the air or sunk into something.</summary>
    [AutoNetworkedField]
    public NetEntity? Harpoon;

    /// <summary>Server only: the real tow cable, once the harpoon is set.</summary>
    public EntityUid? Rope;

    /// <summary>Server only: the visual-only rope that pays out while the harpoon flies.</summary>
    public EntityUid? FlightRope;

    /// <summary>Server only: 1 while reeling in, -1 while paying out, 0 otherwise.</summary>
    public int Reeling;

    /// <summary>Server only: the looping winch sound.</summary>
    public EntityUid? ReelStream;

    /// <summary>Earliest time the turret may nag the operator again.</summary>
    public TimeSpan NextPopup;
}
