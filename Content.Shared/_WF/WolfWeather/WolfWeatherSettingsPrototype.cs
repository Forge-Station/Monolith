using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Shared tuning for wind, indoor sound falloff and wind push.</summary>
[Prototype]
public sealed partial class WolfWeatherSettingsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float CalmWindResponse = 6f;

    [DataField]
    public int IndoorSearch = 16;

    [DataField]
    public int IndoorNear = 2;

    [DataField]
    public float IndoorNearLevel = 0.4f;

    /// <summary>Weather sound volume deep indoors, as a share of outdoors.</summary>
    [DataField]
    public float IndoorVolume = 0.5f;

    /// <summary>How muffled weather sound is deep indoors, as engine audio occlusion.</summary>
    [DataField]
    public float IndoorMuffle = 1.5f;

    /// <summary>Whether strong wind pushes loose items at all.</summary>
    [DataField]
    public bool PushEnabled = true;

    [DataField]
    public float PushInterval = 0.5f;

    [DataField]
    public float PushThreshold = 3f;

    [DataField]
    public float PushRadius = 12f;

    [DataField]
    public int PushMaxWeight = 2;

    [DataField]
    public float PushChance = 0.35f;

    [DataField]
    public float PushStrength = 0.3f;

    /// <summary>Most items one push pass may move, across every map.</summary>
    [DataField]
    public int PushMaxPerTick = 64;
}
