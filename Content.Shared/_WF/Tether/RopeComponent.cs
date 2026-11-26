using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Tether;

/// <summary>
/// A single rope, living on its own lightweight entity with a global PVS override so both ends
/// can draw it. Only the fields the client renders from are networked.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RopeComponent : Component
{
    /// <summary>Attach point A. For a carried rope this is the anchored end.</summary>
    [AutoNetworkedField] public NetEntity EndA;

    /// <summary>Attach point B, or the carrying player while the rope is carried.</summary>
    [AutoNetworkedField] public NetEntity EndB;

    [AutoNetworkedField] public ProtoId<RopeTypePrototype> RopeType;

    /// <summary>Rest length in metres.</summary>
    [AutoNetworkedField] public float Length = 1f;

    /// <summary>Extension over the hard limit's travel, 0..2, quantised for the network.</summary>
    [AutoNetworkedField] public float Strain;

    /// <summary>A loose end held by a player: drawn, but never physical.</summary>
    [AutoNetworkedField] public bool Carried;

    /// <summary>
    /// Last known map positions of both ends, refreshed coarsely by the server. The client falls
    /// back to these when an end is outside its PVS range so the rope keeps drawing.
    /// </summary>
    [AutoNetworkedField] public Vector2 WorldA;
    [AutoNetworkedField] public Vector2 WorldB;

    /// <summary>Server only: when the fallback positions were last sent.</summary>
    public TimeSpan NextWorldSync;

    /// <summary>Server only: joint id on the two physical bodies, null while the rope has no joint.</summary>
    public string? JointId;

    /// <summary>Server only: bodies the joint currently connects. Recreated when either changes.</summary>
    public EntityUid? BodyA;
    public EntityUid? BodyB;

    /// <summary>Server only: last computed tension in newtons.</summary>
    public float Tension;

    /// <summary>Server only: seconds spent continuously above the break force.</summary>
    public float OverloadTime;

    /// <summary>Server only: stack units spent on this rope, refunded exactly on untie.</summary>
    public int Units;

    /// <summary>Server only: false for rope that was never paid out of a coil, such as a harpoon's cable.</summary>
    public bool Refundable = true;

    /// <summary>Server only: metres of rope per stack unit, from the coil that paid it out.</summary>
    public float MetresPerUnit = 1f;
}
