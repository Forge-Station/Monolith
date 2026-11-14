using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCities;

/// <summary>
/// A fixed city's level standing on a flight or underground layer: terrain and buildings, not a vessel. It casts no
/// ship's shadow, keeps the planet's perspective off for anyone under its roofs and is rained on by its own roofs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFPlanetCityGridComponent : Component
{
    [DataField]
    public ProtoId<WFFixedCityPrototype> City;
}
