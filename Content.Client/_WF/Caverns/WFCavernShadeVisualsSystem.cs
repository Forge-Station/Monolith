using System.Numerics;
using Content.Shared._WF.Caverns;
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Caverns;

/// <summary>
/// Draws each hole's crumbling edge on the dual grid: one piece per tile corner, centred on it, so the lip can overhang
/// the hole and round its corners while the hole's middle stays clear for the cavern below to show through.
/// </summary>
// Each corner piece is drawn once, by the lowest pit tile around it, as a layer offset from that tile's shade. A corner
// with pit all round has no edge to draw.
public sealed partial class WFCavernShadeVisualsSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>How far a pit change reaches: the owners of its corners are its neighbours.</summary>
    private const int RefreshReach = 1;

    /// <summary>The mask of a corner with pit all round, which draws nothing.</summary>
    private const int FullMask = 15;

    /// <summary>How many labels a tile edge the rim crosses can take: shallow or deep.</summary>
    private const int Labels = 2;

    /// <summary>A tile's corners: the layer key and the corner as an offset from the tile.</summary>
    private static readonly (string Key, Vector2i Offset)[] TileCorners =
    {
        ("ne", new Vector2i(1, 1)),
        ("nw", new Vector2i(0, 1)),
        ("se", new Vector2i(1, 0)),
        ("sw", new Vector2i(0, 0)),
    };

    /// <summary>The tiles around a corner as offsets from it, in mask-bit order: NW, NE, SW, SE.</summary>
    private static readonly Vector2i[] Block = { new(-1, 0), new(0, 0), new(-1, -1), new(0, -1) };

    /// <summary>The order that picks a corner's owner, lowest tile first: SW, SE, NW, NE, as indices into <see cref="Block"/>.</summary>
    private static readonly int[] OwnerOrder = { 2, 3, 0, 1 };

    // Shades are unanchored, so icon smoothing can't see them: the pits are tracked here by grid tile.
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, EntityUid>> _pits = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2i Tile)> _placed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernShaftComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFCavernShaftComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WFCavernShaftComponent> ent, ref ComponentStartup args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var tile = _map.CoordinatesToTile(grid, gridComp, xform.Coordinates);
        if (!_pits.TryGetValue(grid, out var pits))
            _pits[grid] = pits = new Dictionary<Vector2i, EntityUid>();

        pits[tile] = ent.Owner;
        _placed[ent.Owner] = (grid, tile);
        RefreshAround(grid, tile);
    }

    private void OnShutdown(Entity<WFCavernShaftComponent> ent, ref ComponentShutdown args)
    {
        if (!_placed.Remove(ent.Owner, out var placed) || !_pits.TryGetValue(placed.Grid, out var pits))
            return;

        if (pits.TryGetValue(placed.Tile, out var there) && there == ent.Owner)
            pits.Remove(placed.Tile);

        if (pits.Count == 0)
            _pits.Remove(placed.Grid);
        else
            RefreshAround(placed.Grid, placed.Tile);
    }

    private void RefreshAround(EntityUid grid, Vector2i centre)
    {
        if (!_pits.TryGetValue(grid, out var pits))
            return;

        for (var dx = -RefreshReach; dx <= RefreshReach; dx++)
        {
            for (var dy = -RefreshReach; dy <= RefreshReach; dy++)
            {
                var tile = centre + new Vector2i(dx, dy);
                if (pits.TryGetValue(tile, out var pit))
                    Refresh(pit, tile, pits);
            }
        }
    }

    private void Refresh(EntityUid uid, Vector2i tile, Dictionary<Vector2i, EntityUid> pits)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        Entity<SpriteComponent?> ent = (uid, sprite);

        // The prototype's whole-tile layer is only the placement icon.
        _sprite.LayerSetVisible(ent, 0, false);

        foreach (var (key, offset) in TileCorners)
        {
            var corner = tile + offset;
            var piece = _sprite.LayerMapReserve(ent, $"wf-pit-{key}");
            var mask = Mask(pits, corner);

            if (mask == FullMask || Owner(pits, corner) != tile)
            {
                _sprite.LayerSetVisible(ent, piece, false);
                continue;
            }

            _sprite.LayerSetRsiState(ent, piece, PieceState(corner, mask));
            _sprite.LayerSetOffset(ent, piece, new Vector2(offset.X - 0.5f, offset.Y - 0.5f));
            _sprite.LayerSetVisible(ent, piece, true);
        }
    }

    /// <summary>Which of the four tiles around a corner are pit: 1 NW, 2 NE, 4 SW, 8 SE.</summary>
    private static int Mask(Dictionary<Vector2i, EntityUid> pits, Vector2i corner)
    {
        var mask = 0;
        for (var i = 0; i < Block.Length; i++)
        {
            if (pits.ContainsKey(corner + Block[i]))
                mask |= 1 << i;
        }

        return mask;
    }

    /// <summary>The pit tile that draws a corner's piece: the lowest of the pit tiles around it, then the leftmost.</summary>
    private static Vector2i? Owner(Dictionary<Vector2i, EntityUid> pits, Vector2i corner)
    {
        foreach (var index in OwnerOrder)
        {
            if (pits.ContainsKey(corner + Block[index]))
                return corner + Block[index];
        }

        return null;
    }

    /// <summary>The piece state for a corner: its mask, the labels of the tile edges the rim crosses and a drawing.</summary>
    public static string PieceState(Vector2i corner, int mask)
    {
        bool Pit(int bit) => (mask & bit) != 0;

        // Each piece border crosses one tile edge; both pieces on it see the same edge, so the rim meets itself there.
        var labels = 0;
        if (Pit(1) != Pit(2) && Label(corner, false))
            labels |= 1;
        if (Pit(2) != Pit(8) && Label(corner, true))
            labels |= 2;
        if (Pit(4) != Pit(8) && Label(corner - new Vector2i(0, 1), false))
            labels |= 4;
        if (Pit(1) != Pit(4) && Label(corner - new Vector2i(1, 0), true))
            labels |= 8;

        return $"v{mask}_{labels}_{Hash(corner, 0) % (uint) Variants(mask)}";
    }

    /// <summary>How many drawings a mask's pieces have: a diagonal pair one, the rest two.</summary>
    public static int Variants(int mask)
    {
        return mask is 6 or 9 ? 1 : 2;
    }

    /// <summary>Every state a pit RSI must hold for this system.</summary>
    public static IEnumerable<string> AllStates()
    {
        yield return "pit";

        for (var mask = 1; mask < FullMask; mask++)
        {
            var crossed = 0;
            if (((mask & 1) != 0) != ((mask & 2) != 0))
                crossed |= 1;
            if (((mask & 2) != 0) != ((mask & 8) != 0))
                crossed |= 2;
            if (((mask & 4) != 0) != ((mask & 8) != 0))
                crossed |= 4;
            if (((mask & 1) != 0) != ((mask & 4) != 0))
                crossed |= 8;

            for (var labels = 0; labels < 16; labels++)
            {
                if ((labels & ~crossed) != 0)
                    continue;

                for (var variant = 0; variant < Variants(mask); variant++)
                {
                    yield return $"v{mask}_{labels}_{variant}";
                }
            }
        }
    }

    /// <summary>Whether a grid has a pit tile inside a world box.</summary>
    // Pits only lie on a planet's ground, whose grid is its own map, so its tiles are world tiles.
    public bool AnyPitWithin(EntityUid grid, Box2 worldBox)
    {
        if (!_pits.TryGetValue(grid, out var pits))
            return false;

        foreach (var tile in pits.Keys)
        {
            if (worldBox.Intersects(new Box2(tile, tile + Vector2i.One)))
                return true;
        }

        return false;
    }

    /// <summary>A tile edge's label: a vertical edge runs up from a point, a horizontal one right.</summary>
    private static bool Label(Vector2i start, bool horizontal)
    {
        return Hash(start, horizontal ? 2 : 1) % Labels == 1;
    }

    /// <summary>A stable hash of a grid point: plain arithmetic, so it never changes between sessions.</summary>
    public static uint Hash(Vector2i point, int salt)
    {
        var hash = unchecked((uint) (point.X * 374761393 + point.Y * 668265263 + salt * 1274126177));
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
        return hash ^ (hash >> 16);
    }
}
