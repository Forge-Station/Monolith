using Robust.Shared.Map;

namespace Content.Shared._WF.TradeGoods;

/// <summary>
/// Where a trade crate was made, so a chute can refuse one that was not hauled. Server-only state: not networked, not saved.
/// </summary>
[RegisterComponent]
public sealed partial class WFTradeOriginComponent : Component
{
    /// <summary>True until the crate is first seen on a market map.</summary>
    [ViewVariables]
    public bool Pending = true;

    /// <summary>The grid the crate was on when stamped.</summary>
    [ViewVariables]
    public EntityUid? Grid;

    /// <summary>
    /// Grids docked to <see cref="Grid"/> when stamped, or null for none. A chute on one of them counts the crate as made
    /// there, so riding docked to a moving market doesn't haul it.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid>? Docked;

    /// <summary>Where the crate was when stamped.</summary>
    [ViewVariables]
    public MapCoordinates Point = MapCoordinates.Nullspace;

    /// <summary>The last player ship the crate rode on, named when it is sold or taken.</summary>
    [ViewVariables]
    public EntityUid? Carrier;

    /// <summary>Name of the NPC freight run the crate was loaded on, if any.</summary>
    [ViewVariables]
    public string? Run;

    /// <summary>The station the freight run was carrying the crate to, if any.</summary>
    [ViewVariables]
    public EntityUid? Destination;
}
