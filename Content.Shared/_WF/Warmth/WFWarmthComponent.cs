using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warmth;

/// <summary>
/// What a body feels of the temperature around it: the air on its own tile, how warm its body is, how wet it is and
/// whether the cold or heat is warning it yet. Server-written each second; its owner's client reads it for the HUD,
/// the screen effect and the slowdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFWarmthComponent : Component
{
    // Not owner-only: dirtying an owner-only component leaves every other viewer an empty entity state, which PVS
    // reports as an error. Only players' bodies are dirtied (WFWarmthSystem), so other mobs send nothing.

    /// <summary>The air temperature on the body's own tile in whole degrees Celsius; null in vacuum.</summary>
    [DataField, AutoNetworkedField]
    public int? HereCelsius;

    [DataField, AutoNetworkedField]
    public WFBodyFeel Feel = WFBodyFeel.Comfortable;

    /// <summary>Which warning signs are showing, if any.</summary>
    [DataField, AutoNetworkedField]
    public WFWarmthStrain Strain = WFWarmthStrain.None;

    /// <summary>How strong the warning signs are, 0 at the warning line and 1 at the line where harm starts.</summary>
    [DataField, AutoNetworkedField]
    public float Severity;

    /// <summary>How soaked the body and its clothes are, from 0 dry to 1 drenched.</summary>
    [DataField, AutoNetworkedField]
    public float Wetness;

    /// <summary>Where the body sits between the cold harm line (0) and the heat harm line (100), in 5% steps.</summary>
    [DataField, AutoNetworkedField]
    public byte Gauge = 50;

    /// <summary>Whether the body is warming or cooling, for the HUD gauge's arrow.</summary>
    [DataField, AutoNetworkedField]
    public WFWarmthTrend Trend;

    /// <summary>Whether a lit fire or another active radiant heater reaches the body.</summary>
    [DataField, AutoNetworkedField]
    public bool NearWarmth;

    /// <summary>The share of the way to harm from the cold, as the last body feel read it.</summary>
    [ViewVariables]
    public float ColdShare;

    /// <summary>The share of the way to harm from the heat, as the last body feel read it.</summary>
    [ViewVariables]
    public float HeatShare;

    /// <summary>The gauge's raw value last second and its smoothed change per second.</summary>
    [ViewVariables]
    public float LastGauge = float.NaN;

    [ViewVariables]
    public float GaugeRate;

    /// <summary>The extra heat the wind takes off the body on an open tile, as a multiplier; 1 out of the wind.</summary>
    [ViewVariables]
    public float WindChill = 1f;

    /// <summary>When the next shiver or sweat message may show.</summary>
    [ViewVariables]
    public TimeSpan NextSign;

    /// <summary>When the next visible shiver or sweat may show to others.</summary>
    [ViewVariables]
    public TimeSpan NextVisibleSign;
}

/// <summary>How warm a body feels, from its real body temperature.</summary>
[Serializable, NetSerializable]
public enum WFBodyFeel : byte
{
    Freezing,
    Cold,
    Chilly,
    Comfortable,
    Warm,
    Hot,
    Overheating,
}

/// <summary>Which way a body's temperature is heading.</summary>
[Serializable, NetSerializable]
public enum WFWarmthTrend : byte
{
    Steady,
    Warming,
    Cooling,
}

/// <summary>Which warning signs a body shows before the cold or heat harms it.</summary>
[Serializable, NetSerializable]
public enum WFWarmthStrain : byte
{
    None,
    Cold,
    Hot,
}
