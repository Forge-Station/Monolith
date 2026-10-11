using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>
/// Sent by the console's whole-ship view while it is on screen. The server only gathers hull telemetry
/// for consoles that ask for it, since the sweep isn't free.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipStatusRequestMessage : BoundUserInterfaceMessage
{
    public bool Active;

    /// <summary>
    /// Which overlays the console is actually drawing. Readings for the rest are still counted for the
    /// summary but aren't sent per-tile, so a permanently unpressurised ship doesn't stream pressure
    /// data nobody is looking at.
    /// </summary>
    public ShipOverlays Overlays;

    /// <summary>Requests the permanent cockpit hull and fuel gauges while the detailed SHP page is hidden.</summary>
    public bool HullOnly;

    public ShipStatusRequestMessage(bool active, ShipOverlays overlays, bool hullOnly = false)
    {
        Active = active;
        Overlays = overlays;
        HullOnly = hullOnly;
    }
}

/// <summary>
/// The overlays a console can draw, matching <see cref="ShipTileFlags"/>.
/// </summary>
[Flags]
[Serializable, NetSerializable]
public enum ShipOverlays : byte
{
    None = 0,
    Damage = 1 << 0,
    Fire = 1 << 1,
    Power = 1 << 2,
    Pressure = 1 << 3,
    All = Damage | Fire | Power | Pressure,
}

/// <summary>
/// Per-tile hull telemetry for the whole-ship view. Only tiles with something worth reporting are
/// included, so an undamaged ship costs almost nothing to send.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipStatusMessage : BoundUserInterfaceMessage
{
    public NetEntity? Grid;
    public List<ShipTileStatus> Tiles;
    public ShipStatusSummary Summary;

    public ShipStatusMessage(NetEntity? grid, List<ShipTileStatus> tiles, ShipStatusSummary summary)
    {
        Grid = grid;
        Tiles = tiles;
        Summary = summary;
    }
}

/// <summary>
/// One tile's readings. Integrity and pressure are quantised to a byte to keep the payload small.
/// </summary>
[Serializable, NetSerializable]
public struct ShipTileStatus
{
    public Vector2i Index;

    /// <summary>
    /// Condition of the worst anchored structure on the tile, 0 being about to break.
    /// Only meaningful with <see cref="ShipTileFlags.Damaged"/>.
    /// </summary>
    public byte Integrity;

    /// <summary>
    /// Tile pressure in half-kPa steps, so it tops out at 510 kPa.
    /// Only meaningful with <see cref="ShipTileFlags.Pressure"/>.
    /// </summary>
    public byte Pressure;

    public ShipTileFlags Flags;
}

/// <summary>Hazards flagged on one tile of the whole-ship view.</summary>
[Flags]
[Serializable, NetSerializable]
public enum ShipTileFlags : byte
{
    None = 0,

    /// <summary>An anchored structure here is below full condition.</summary>
    Damaged = 1 << 0,

    /// <summary>The tile is on fire.</summary>
    Fire = 1 << 1,

    /// <summary>An anchored device here wants power and isn't getting any.</summary>
    Unpowered = 1 << 2,

    /// <summary>Pressure is outside the liveable band, so <see cref="ShipTileStatus.Pressure"/> is set.</summary>
    Pressure = 1 << 3,
}

/// <summary>
/// Totals for the console's readout, so the side panel doesn't have to re-count every frame.
/// </summary>
[Serializable, NetSerializable]
public struct ShipStatusSummary
{
    public int DamagedTiles;
    public int FireTiles;
    public int UnpoweredTiles;

    /// <summary>Tiles holding meaningfully less than a breathable atmosphere.</summary>
    public int VentedTiles;

    /// <summary>Average condition of surveyed hull locations, including destroyed or detached sections, 0-1.</summary>
    public float HullIntegrity;

    /// <summary>Installed fuel reserves, also present in lightweight cockpit snapshots.</summary>
    public ShipFuelSummary Fuel;

    /// <summary>Condition of the worst surviving structure on the ship, 0-1.</summary>
    public float WorstIntegrity;

    /// <summary>Set when the sweep hit its tile cap and the readings are partial.</summary>
    public bool Truncated;
}
