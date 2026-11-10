using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>The gun shoots from its own position instead of its user's, e.g. a vehicle gun fired by a crewman inside.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunUseGunOriginComponent : Component
{
}
