using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Shots from this gun pass through whatever it is mounted in; see <see cref="GunIgnoreContainerOwnerCollisionSystem"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunIgnoreContainerOwnerCollisionComponent : Component
{
}
