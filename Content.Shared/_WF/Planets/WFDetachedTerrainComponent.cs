using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>On a grid cut loose from a planet's ground: terrain, not a hull, so flight, drag, orbit decay, infestation, biomass and ambience treat it as ground.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFDetachedTerrainComponent : Component;
