using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>Small map-scoped snapshot shared by the worn watch and day/night soundscape.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
public sealed partial class WFPlanetEnvironmentComponent : Component
{
    [DataField, AutoNetworkedField] public string PlanetName = string.Empty;
    [DataField, AutoNetworkedField] public int MinuteOfDay;
    [DataField, AutoNetworkedField] public string Weather = string.Empty;

    /// <summary>The season's name, a Fluent id; null on a world without seasons.</summary>
    [DataField, AutoNetworkedField] public LocId? Season;
    [DataField, AutoNetworkedField] public Color SeasonColor = Color.White;

    /// <summary>The outdoor air temperature in whole degrees Celsius; null where there is no outdoor air.</summary>
    [DataField, AutoNetworkedField] public int? TemperatureCelsius;

    /// <summary>Local night runs from 18:00 until 06:00; daylight follows the same planetary clock.</summary>
    public bool IsNight => MinuteOfDay < 6 * 60 || MinuteOfDay >= 18 * 60;
}
