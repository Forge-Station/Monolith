using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.GroundCover;

/// <summary>A digging tool that clears snow cover off a tile.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSnowShovelComponent : Component
{
    /// <summary>Seconds to clear a tile at a tool speed of 1.</summary>
    [DataField]
    public float Seconds = 0.75f;
}

/// <summary>Raised on a shovel when clearing snow off a tile ends.</summary>
[Serializable, NetSerializable]
public sealed partial class WFSnowShovelDoAfterEvent : DoAfterEvent
{
    public NetEntity Grid;

    public Vector2i Tile;

    public WFSnowShovelDoAfterEvent(NetEntity grid, Vector2i tile)
    {
        Grid = grid;
        Tile = tile;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is WFSnowShovelDoAfterEvent dig && Grid == dig.Grid && Tile == dig.Tile;
    }
}
