using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Weighted weathers a map rolls between, with gaps of no weather.</summary>
[Prototype]
public sealed partial class WeatherSchedulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public ScheduleTimeRange Gap = new() { Min = TimeSpan.FromMinutes(6), Max = TimeSpan.FromMinutes(15) };

    [DataField(required: true)]
    public List<WeatherScheduleEntry> Weathers = new();
}

/// <summary>One weather a schedule can roll, with its weight and duration.</summary>
[DataDefinition]
public sealed partial class WeatherScheduleEntry
{
    [DataField(required: true)]
    public ProtoId<WolfWeatherPrototype> Weather;

    [DataField]
    public float Weight = 1f;

    [DataField(required: true)]
    public ScheduleTimeRange Duration;

    /// <summary>Overrides the weather's wind direction with a random angle in degrees, keeping its strength.</summary>
    [DataField]
    public ScheduleFloatRange? WindAngle;
}

/// <summary>A random time between two bounds.</summary>
[DataDefinition]
public partial record struct ScheduleTimeRange
{
    [DataField(required: true)]
    public TimeSpan Min;

    [DataField(required: true)]
    public TimeSpan Max;
}

/// <summary>A random number between two bounds.</summary>
[DataDefinition]
public partial record struct ScheduleFloatRange
{
    [DataField(required: true)]
    public float Min;

    [DataField(required: true)]
    public float Max;
}
