using Content.Shared.Atmos;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.GroundCover;

/// <summary>Standing water that freezes over in cold air into walkable ice.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFFreezableWaterComponent : Component
{
    public const float DefaultFreezeAt = Atmospherics.T0C - 2f;

    public const float DefaultThawAt = Atmospherics.T0C;

    [DataField, AutoNetworkedField]
    public bool Frozen;

    /// <summary>Kelvin at or below which the water starts to freeze, once the air has held there a while. Zero never freezes.</summary>
    [DataField]
    public float FreezeAt = DefaultFreezeAt;

    /// <summary>Kelvin above which its ice starts to thaw, once the air has held there a while.</summary>
    [DataField]
    public float ThawAt = DefaultThawAt;

    /// <summary>Wading slowdown restored on a thaw.</summary>
    [DataField]
    public float WalkSpeed = 0.5f;

    [DataField]
    public float SprintSpeed = 0.5f;

    /// <summary>Friction share on the ice, so it's a little slippery.</summary>
    [DataField]
    public float IceFriction = 0.35f;

    [DataField]
    public float IceAcceleration = 0.5f;

    /// <summary>Seconds to break a hole at a tool speed of 1.</summary>
    [DataField]
    public float BreakSeconds = 4f;

    /// <summary>The tick of the last freeze or thaw, which the planet unloader doesn't count as a change.</summary>
    [ViewVariables]
    public GameTick IceTick;
}

/// <summary>Raised on frozen water when breaking a hole in it ends.</summary>
[Serializable, NetSerializable]
public sealed partial class WFIceBreakDoAfterEvent : SimpleDoAfterEvent;
