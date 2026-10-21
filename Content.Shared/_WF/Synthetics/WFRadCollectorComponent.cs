using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// A chassis organ that soaks up the radiation reaching a synthetic. While it has room nothing else is touched; full,
/// the overflow strains it and leaks into the core; missing or burnt out, all of it corrupts the core.
/// Pulled out, it vents its charge; a welder restores its integrity (its organ health) until it burns out at zero.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFRadCollectorComponent : Component
{
    /// <summary>Rads held.</summary>
    [DataField, AutoNetworkedField]
    public float Charge;

    /// <summary>Rads it can hold before it overflows.</summary>
    [DataField]
    public float Capacity = 300f;

    /// <summary>Rads a second it sheds while out of a body.</summary>
    [DataField]
    public float VentPerSecond = 5f;

    /// <summary>Integrity lost per rad of overflow while it is full.</summary>
    [DataField]
    public float StrainPerRad = 0.03f;

    /// <summary>Core health lost per rad with no working collector; a full collector leaks half of this.</summary>
    [DataField]
    public float CorruptionPerRad = 0.02f;

    /// <summary>Integrity one weld restores.</summary>
    [DataField]
    public float WeldRepair = 10f;

    /// <summary>Seconds one weld takes.</summary>
    [DataField]
    public float WeldTime = 3f;
}

/// <summary>A weld on a pulled collector has finished.</summary>
[Serializable, NetSerializable]
public sealed partial class WFRadCollectorWeldDoAfterEvent : SimpleDoAfterEvent;
