using Content.Server._WF.Planets;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;

namespace Content.Server._FarHorizons.StarSystem;

public sealed partial class StarSystemMapSystem
{
    [Dependency] private WFPlanetRegistrySystem _wfPlanets = default!;

    /// <summary>Hands a freshly spawned sector body to the Wolfgate planet registry.</summary>
    private void WfPlanetSpawned(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet)
        => _wfPlanets.RegisterPlanet(map, planetEntity, planet);
}
