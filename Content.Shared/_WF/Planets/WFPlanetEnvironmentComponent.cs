using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>Small map-scoped snapshot shared by the worn watch and day/night soundscape.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
public sealed partial class WFPlanetEnvironmentComponent : Component
{
    [DataField, AutoNetworkedField] public string PlanetName = string.Empty;
    [DataField, AutoNetworkedField] public int MinuteOfDay;
    [DataField, AutoNetworkedField] public string Weather = string.Empty;

    /// <summary>Local night runs from 18:00 until 06:00; daylight follows the same planetary clock.</summary>
    public bool IsNight => MinuteOfDay < 6 * 60 || MinuteOfDay >= 18 * 60;
}
