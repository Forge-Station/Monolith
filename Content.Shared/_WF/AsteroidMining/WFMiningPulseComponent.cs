using Robust.Shared.GameStates;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>A mining beam's hitscan pulse: it heats the asteroid it hits instead of damaging it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFMiningPulseComponent : Component;
