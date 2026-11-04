using System.Numerics;
using Content.Shared._WF.GroundCover;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.GroundCover;

/// <summary>
/// Footprints in snow cover, laid on this client from the mobs it sees walk, so nothing is networked. They fade with
/// time, with the snow under them as it thins, fill in as fresh snow lands on them, and go when their tile is shoveled
/// or melts bare.
/// </summary>
public sealed class WFSnowPrintsSystem : EntitySystem
{
    [Dependency] private WFGroundCoverSystem _cover = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Prints kept at most; the oldest go first.</summary>
    public const int MaxPrints = 1500;

    /// <summary>Prints one tile keeps; a new one replaces its oldest.</summary>
    public const int MaxPerTile = 6;

    /// <summary>Distance walked between prints.</summary>
    public const float StepSize = 0.7f;

    /// <summary>Depth units of fresh snow that fill a print in.</summary>
    public const int FillUnits = WFSnowCover.UnitsPerLevel / 3;

    /// <summary>Depth a print needs to show in full: under it, a print fades as the snow thins, to nothing when bare.</summary>
    public const int FullDepth = WFSnowCover.UnitsPerLevel;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(4);

    private static readonly Vector2 SideOffset = new(0.1f, 0f);
    private static readonly Angle PrintRotation = Angle.FromDegrees(180);
    private const string ShoesSlot = "shoes";

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, List<WFSnowPrint>>> _prints = new();
    private readonly Queue<WFSnowPrint> _order = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2 Position, bool Right)> _walkers = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<Vector2i> _bare = new();
    private int _seenVersion = -1;

    /// <summary>Prints laid and not yet gone.</summary>
    public int Count { get; private set; }

    /// <summary>Prints by grid and tile.</summary>
    public IReadOnlyDictionary<EntityUid, Dictionary<Vector2i, List<WFSnowPrint>>> Prints => _prints;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new WFSnowPrintsOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WFSnowPrintsOverlay>();
        Clear();
    }

    /// <summary>Forgets every print and walker.</summary>
    public void Clear()
    {
        _prints.Clear();
        _order.Clear();
        _walkers.Clear();
        Count = 0;
    }

    /// <summary>How visible a print is now, from 1 fresh to 0 gone.</summary>
    public float Visibility(WFSnowPrint print)
    {
        var age = (float) ((_timing.CurTime - print.Laid) / Lifetime);
        return Math.Clamp(1f - age, 0f, 1f) * (1f - print.Filled) * print.Thickness;
    }

    /// <summary>How much of a print's snow is left, 1 while the snow is as deep as it was or past <see cref="FullDepth"/>, 0 when bare.</summary>
    public static float Thickness(int laid, int depth)
    {
        return Math.Clamp(depth / (float) Math.Min(laid, FullDepth), 0f, 1f);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_cover.Version != _seenVersion)
        {
            _seenVersion = _cover.Version;
            Recheck();
        }

        var now = _timing.CurTime;
        while (_order.TryPeek(out var oldest) && (oldest.Gone || now - oldest.Laid > Lifetime || Count > MaxPrints))
        {
            _order.Dequeue();
            Remove(oldest);
        }

        Walk();
    }

    /// <summary>Lays a print for every mob that walked a step on snow cover since last frame.</summary>
    private void Walk()
    {
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || xform.ParentUid != grid || !_cover.HasSnow(grid))
            {
                _walkers.Remove(uid);
                continue;
            }

            var position = xform.LocalPosition;
            if (!_walkers.TryGetValue(uid, out var last) || last.Grid != grid)
            {
                _walkers[uid] = (grid, position, false);
                continue;
            }

            var delta = position - last.Position;
            if (delta.LengthSquared() < StepSize * StepSize)
                continue;

            var right = !last.Right;
            _walkers[uid] = (grid, position, right);
            if (_gravity.IsWeightless(uid) || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var tile = _map.LocalToTile(grid, gridComp, xform.Coordinates);
            var depth = _cover.GetDepth(grid, tile);
            if (depth < WFSnowCover.CoverFrom)
                continue;

            var facing = delta.ToWorldAngle();
            var side = right ? facing + PrintRotation : facing;
            var kind = _inventory.TryGetSlotEntity(uid, ShoesSlot, out _)
                ? WFSnowPrintKind.Shoes
                : right ? WFSnowPrintKind.BareRight : WFSnowPrintKind.BareLeft;
            Lay(grid, tile, position + side.RotateVec(SideOffset), facing + PrintRotation, kind, depth);
        }

        if (_walkers.Count == 0 || _timing.CurFrame % 600 != 0)
            return;

        _stale.Clear();
        foreach (var uid in _walkers.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _walkers.Remove(uid);
        }
    }

    /// <summary>Lays one print on a tile, making room on it if it's full.</summary>
    public WFSnowPrint Lay(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, WFSnowPrintKind kind, int depth)
    {
        if (!_prints.TryGetValue(grid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, List<WFSnowPrint>>();
            _prints[grid] = tiles;
        }

        if (!tiles.TryGetValue(tile, out var list))
        {
            list = new List<WFSnowPrint>();
            tiles[tile] = list;
        }

        if (list.Count >= MaxPerTile)
        {
            list[0].Gone = true;
            list.RemoveAt(0);
            Count--;
        }

        var print = new WFSnowPrint(grid, tile, position, rotation, kind, _timing.CurTime, depth);
        list.Add(print);
        _order.Enqueue(print);
        Count++;
        return print;
    }

    /// <summary>Drops prints whose snow is gone or has been filled in, after a snow update.</summary>
    private void Recheck()
    {
        _stale.Clear();
        foreach (var (grid, tiles) in _prints)
        {
            if (TerminatingOrDeleted(grid))
            {
                _stale.Add(grid);
                continue;
            }

            _bare.Clear();
            foreach (var (tile, list) in tiles)
            {
                var depth = _cover.GetDepth(grid, tile);
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var print = list[i];
                    print.Filled = Math.Clamp((depth - print.Depth) / (float) FillUnits, 0f, 1f);
                    print.Thickness = Thickness(print.Depth, depth);
                    if (depth > 0 && print.Filled < 1f)
                        continue;

                    print.Gone = true;
                    list.RemoveAt(i);
                    Count--;
                }

                if (list.Count == 0)
                    _bare.Add(tile);
            }

            foreach (var tile in _bare)
            {
                tiles.Remove(tile);
            }
        }

        foreach (var grid in _stale)
        {
            foreach (var list in _prints[grid].Values)
            {
                foreach (var print in list)
                {
                    print.Gone = true;
                    Count--;
                }
            }

            _prints.Remove(grid);
        }
    }

    private void Remove(WFSnowPrint print)
    {
        if (print.Gone)
            return;

        print.Gone = true;
        Count--;
        if (!_prints.TryGetValue(print.Grid, out var tiles) || !tiles.TryGetValue(print.Tile, out var list))
            return;

        list.Remove(print);
        if (list.Count > 0)
            return;

        tiles.Remove(print.Tile);
        if (tiles.Count == 0)
            _prints.Remove(print.Grid);
    }
}

/// <summary>Which footprint sprite a print uses.</summary>
public enum WFSnowPrintKind : byte
{
    BareLeft,
    BareRight,
    Shoes,
}

/// <summary>One footprint in snow, in its grid's space.</summary>
public sealed class WFSnowPrint(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, WFSnowPrintKind kind, TimeSpan laid, int depth)
{
    public readonly EntityUid Grid = grid;
    public readonly Vector2i Tile = tile;
    public readonly Vector2 Position = position;
    public readonly Angle Rotation = rotation;
    public readonly WFSnowPrintKind Kind = kind;
    public readonly TimeSpan Laid = laid;

    /// <summary>Snow depth on its tile when it was laid.</summary>
    public readonly int Depth = depth;

    /// <summary>How far fresh snow has filled it, 0 to 1.</summary>
    public float Filled;

    /// <summary>How much of its snow is left, 1 to 0, as melt thins the cover.</summary>
    public float Thickness = 1f;

    public bool Gone;
}
