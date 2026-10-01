using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Hud;

/// <summary>
/// The whole synthetic diagnostics readout for one mechanical body, pushed by the server whenever it
/// changes. The client HUD draws nothing else: it never walks the parts itself, because wound entities are
/// server-only.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedSyntheticHudComponent : Component
{
    /// <summary>Most fault lines the readout will ever carry. Past this the oldest are dropped.</summary>
    public const int MaxFaults = 12;

    /// <summary>Active faults, newest first.</summary>
    [AutoNetworkedField]
    public List<WolfmedSyntheticFault> Faults = new();

    /// <summary>Chassis integrity, 1 intact to 0 scrap. Averaged over the parts against their amputation thresholds.</summary>
    [AutoNetworkedField]
    public float Integrity = 1f;

    /// <summary>Hydraulic fluid left, 1 to 0. The mechanical blood analogue: bloodstream volume.</summary>
    [AutoNetworkedField]
    public float Fluid = 1f;

    /// <summary>Cell charge, 1 to 0, or -1 when the chassis carries no readable cell.</summary>
    [AutoNetworkedField]
    public float Power = -1f;

    /// <summary>Share of the limbs still driving, 1 to 0. Disabled and missing parts both count against it.</summary>
    [AutoNetworkedField]
    public float Servos = 1f;

    /// <summary>The chassis has no power or no pump; the readout goes to standby.</summary>
    [AutoNetworkedField]
    public bool Shutdown;

    /// <summary>The positronic brain is gone. The readout panics and then shows the core-offline banner.</summary>
    [AutoNetworkedField]
    public bool CoreOffline;

    /// <summary>Advice line for the worst active fault, or empty while nothing is wrong.</summary>
    [AutoNetworkedField]
    public string Advice = string.Empty;

    /// <summary>
    /// M1a: the banner line for whatever holds the chassis down, from its cause (plan §5.6), in place of the
    /// blanket STANDBY. Takes the cause's sub-source as <c>$source</c>. Empty while the chassis is up.
    /// </summary>
    [AutoNetworkedField]
    public string CauseLine = string.Empty;

    /// <summary>M1a D: damage-sensor load, the chassis's pain against its soft cap, 0 to 1 (plan §5.6).</summary>
    [AutoNetworkedField]
    public float Sensors;

    /// <summary>M1a D: chassis temperature in kelvin, or -1 when it has none (plan §5.6; the M4 core-heat input).</summary>
    [AutoNetworkedField]
    public float CoreTemperature = -1f;
}

/// <summary>One line of the DIAGNOSTICS block: which part, what it says, and how loud.</summary>
[Serializable, NetSerializable]
public readonly record struct WolfmedSyntheticFault(
    TargetBodyPart Part,
    string Line,
    WolfmedSyntheticSeverity Severity,
    string Advice);

/// <summary>The tag a fault line carries, in the order the readout sorts them.</summary>
[Serializable, NetSerializable]
public enum WolfmedSyntheticSeverity : byte
{
    /// <summary>Noted, not acted on.</summary>
    Info,

    /// <summary>Degraded, still working.</summary>
    Warn,

    /// <summary>Failing now.</summary>
    Crit,

    /// <summary>Gone.</summary>
    Fail,
}

/// <summary>Body- and part-level findings that are not wounds but still want a line.</summary>
[Serializable, NetSerializable]
public enum WolfmedSyntheticCondition : byte
{
    /// <summary>Not a condition line; the mapping is keyed on a wound instead.</summary>
    None,

    /// <summary>Hydraulic fluid below the reserve mark.</summary>
    LowFluid,

    /// <summary>No power or no pump.</summary>
    Shutdown,

    /// <summary>The positronic brain is damaged but still there.</summary>
    BrainDamage,

    /// <summary>The positronic brain is destroyed.</summary>
    BrainDestroyed,

    /// <summary>The part is attached but does nothing.</summary>
    PartDisabled,

    /// <summary>The part is off the chassis.</summary>
    PartMissing,

    /// <summary>Something is still lodged in the part.</summary>
    Embedded,

    /// <summary>A mechanical wound with no line of its own.</summary>
    Fallback,

    /// <summary>M2 (OD10): the core was repaired a while ago. Cosmetic: no trauma on a machine.</summary>
    CoreRestored,

    /// <summary>M4 (plan §3.11): core or chassis past the warning line, before thermal shutdown.</summary>
    CoreTempCritical,
}
