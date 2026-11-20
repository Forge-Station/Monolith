using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Lightning in a weather: far flashes with delayed thunder, and strikes that land near players.</summary>
[DataDefinition]
public sealed partial class WolfWeatherLightning
{
    /// <summary>Seconds between bolts, min and max.</summary>
    [DataField]
    public Vector2 Interval = new(10f, 30f);

    /// <summary>Share of bolts that land near a player. The rest are far flashes.</summary>
    [DataField]
    public float StrikeChance;

    /// <summary>Tiles from a player a strike lands, min and max.</summary>
    [DataField]
    public Vector2 StrikeRange = new(4f, 20f);

    /// <summary>Seconds from a far flash to its thunder, min and max.</summary>
    [DataField]
    public Vector2 FarDelay = new(1f, 4f);

    [DataField]
    public Color FlashColor = Color.FromHex("#cfe4ff");

    [DataField]
    public float FarFlash = 0.35f;

    [DataField]
    public float StrikeFlash = 0.8f;

    [DataField]
    public SoundSpecifier? NearSound;

    [DataField]
    public SoundSpecifier? FarSound;

    /// <summary>Volume change for far thunder heard indoors, in decibels.</summary>
    [DataField]
    public float IndoorVolume = -6f;

    /// <summary>Muffle for far thunder heard indoors, 0 to 1.</summary>
    [DataField]
    public float IndoorOcclusion = 0.5f;

    [DataField]
    public EntProtoId? Bolt = "WFThunderbolt";

    [DataField]
    public int StrikeDamage = 25;

    [DataField]
    public float StrikeStun = 3f;
}

/// <summary>Sent to a map's players when lightning flashes there.</summary>
[Serializable, NetSerializable]
public sealed class WolfLightningEvent : EntityEventArgs
{
    public NetEntity Map;
    public ProtoId<WolfWeatherPrototype> Weather;
    public bool Strike;
}
