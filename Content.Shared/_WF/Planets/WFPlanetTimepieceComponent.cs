using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>Marks a wearable that reports the containing planet's local time and current weather.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFPlanetTimepieceComponent : Component
{
}
