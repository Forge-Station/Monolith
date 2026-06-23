using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Gatherable;

/// <summary>
/// Mining hitscan that gathers rock and teleports asteroid drops (ore, artifact fragments) to the collector.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DrakeHitscanGatheringComponent : Component
{
    /// <summary>
    /// When true, ore is teleported to the firing gun (ship turrets). Otherwise to the shooter.
    /// </summary>
    [DataField]
    public bool CollectToGun;

    /// <summary>
    /// When false, ore spawns at the mined rock instead of being teleported.
    /// </summary>
    [DataField]
    public bool TeleportOre = true;

    /// <summary>
    /// Local offset from the collector origin where ore appears (e.g. below the turret sprite).
    /// </summary>
    [DataField]
    public Vector2 TeleportOffset;
}
