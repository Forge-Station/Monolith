using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Weathers on a map, with wind and fade timing.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWolfWeatherSystem))]
public sealed partial class WolfWeatherMapComponent : Component
{
    /// <summary>Active weathers, newest last. A replaced weather stays until its fade-out ends.</summary>
    [DataField, AutoNetworkedField]
    public List<WolfWeatherEntry> Weathers = new();

    [DataField, AutoNetworkedField]
    public Vector2 Wind;

    [DataField, AutoNetworkedField]
    public ProtoId<WolfWeatherSettingsPrototype> Settings = "WFDefault";
}

/// <summary>One weather on a map, with when it started, when it ends and its random seed.</summary>
[DataDefinition, Serializable, NetSerializable]
public partial record struct WolfWeatherEntry
{
    [DataField(required: true)]
    public ProtoId<WolfWeatherPrototype> Weather;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan Start;

    /// <summary>When the weather is gone. Null means it lasts until replaced.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? End;

    [DataField]
    public int Seed;
}
