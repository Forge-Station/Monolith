using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>Marks the private map a hull waits on during an orbit hop, which is where its crew sees the planet approach.</summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent]
public sealed partial class WFPlanetHopMapComponent : Component;
