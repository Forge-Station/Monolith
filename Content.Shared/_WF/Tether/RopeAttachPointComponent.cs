using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Tether;

/// <summary>
/// Something a rope can be tied to. The physical body used for the joint is the entity's grid
/// while it is anchored (or parented to a grid without a dynamic body of its own), otherwise its
/// own body, so loose crates can be towed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RopeAttachPointComponent : Component
{
    [DataField, AutoNetworkedField] public int MaxRopes = 1;

    /// <summary>Where the rope leaves the entity, in its local frame.</summary>
    [DataField, AutoNetworkedField] public Vector2 LocalOffset;

    /// <summary>Ropes currently tied here. Maintained by the server.</summary>
    [AutoNetworkedField] public List<NetEntity> Ropes = new();
}
