using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Automatic weather on a map and the state of its current roll.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WeatherScheduleComponent : Component
{
    [DataField]
    public ProtoId<WeatherSchedulePrototype>? Schedule;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextChange;

    [DataField]
    public ProtoId<WolfWeatherPrototype>? Current;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CurrentStart;

    [DataField]
    public bool Waiting;
}
