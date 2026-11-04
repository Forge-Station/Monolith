namespace Content.Server._WF.PlanetCities;

/// <summary>On a world's ground: the stations of the fixed cities built on it, removed with the world.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCitiesComponent : Component
{
    [DataField]
    public List<EntityUid> Stations = new();
}
