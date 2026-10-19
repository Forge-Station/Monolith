using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Projectiles;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCProjectileSystem))]
public sealed partial class DeleteOnCollideComponent : Component;
