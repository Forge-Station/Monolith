using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Explosion;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedRMCExplosionSystem))]
public sealed partial class RMCWallExplosionDeletableComponent : Component;
