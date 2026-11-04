using System.Numerics;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.Vehicles.Core;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Client._WF.GroundCover;

/// <summary>
/// Tread marks in snow cover, laid on this client from the vehicles it sees move: two lines along the hull's path. They
/// fade with time, fill in under fresh snow, and go when the snow under them is pressed flat, shoveled or melted.
/// </summary>
public sealed class WFSnowTracksSystem : EntitySystem
{
    [Dependency] private WFGroundCoverSystem _cover = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Marks kept at most; the oldest go first.</summary>
    public const int MaxMarks = 800;

    /// <summary>Distance driven between marks.</summary>
    public const float StepSize = 0.6f;

    /// <summary>Longest step still taken for driving; a longer one was a jump.</summary>
    public const float MaxStep = 5f;

    /// <summary>How far in from the hull's side a tread runs.</summary>
    public const float TrackInset = 0.4f;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, List<WFSnowTread>>> _marks = new();
    private readonly Queue<WFSnowTread> _order = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2 Position)> _last = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<Vector2i> _bare = new();
    private int _seenVersion = -1;

    /// <summary>Marks laid and not yet gone.</summary>
    public int Count { get; private set; }

    /// <summary>Marks by grid and tile.</summary>
    public IReadOnlyDictionary<EntityUid, Dictionary<Vector2i, List<WFSnowTread>>> Marks => _marks;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new WFSnowTracksOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WFSnowTracksOverlay>();
        Clear();
    }

    /// <summary>Forgets every mark and vehicle.</summary>
    public void Clear()
    {
        _marks.Clear();
        _order.Clear();
        _last.Clear();
        Count = 0;
    }

    /// <summary>How visible a mark is now, from 1 fresh to 0 gone.</summary>
    public float Visibility(WFSnowTread mark)
    {
        var age = (float) ((_timing.CurTime - mark.Laid) / Lifetime);
        return Math.Clamp(1f - age, 0f, 1f) * (1f - mark.Filled);
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
        while (_order.TryPeek(out var oldest) && (oldest.Gone || now - oldest.Laid > Lifetime || Count > MaxMarks))
        {
            _order.Dequeue();
            Remove(oldest);
        }

        Drive();
    }

    /// <summary>Lays a pair of marks for every vehicle that moved a step on snow cover since it last did.</summary>
    private void Drive()
    {
        var query = EntityQueryEnumerator<WFVehicleEngineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || xform.ParentUid != grid || !_cover.HasSnow(grid))
            {
                _last.Remove(uid);
                continue;
            }

            var position = xform.LocalPosition;
            if (!_last.TryGetValue(uid, out var last) || last.Grid != grid)
            {
                _last[uid] = (grid, position);
                continue;
            }

            var delta = position - last.Position;
            var distance = delta.Length();
            if (distance < StepSize)
                continue;

            _last[uid] = (grid, position);
            if (distance > MaxStep || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var hull = HullBounds(uid);
            var along = delta / distance;
            var across = new Vector2(-along.Y, along.X);
            var rotation = xform.LocalRotation;
            var halfWidth = MathF.Abs(Vector2.Dot(across, rotation.RotateVec(Vector2.UnitX))) * hull.Width / 2f
                + MathF.Abs(Vector2.Dot(across, rotation.RotateVec(Vector2.UnitY))) * hull.Height / 2f;
            var middle = position + rotation.RotateVec(hull.Center);
            var side = across * MathF.Max(0.3f, halfWidth - TrackInset);
            var facing = delta.ToWorldAngle();
            foreach (var at in new[] { middle + side, middle - side })
            {
                var tile = _map.LocalToTile(grid, gridComp, new EntityCoordinates(grid, at));
                var depth = _cover.GetDepth(grid, tile);
                if (depth >= WFSnowCover.CoverFrom)
                    Lay(grid, tile, at, facing, depth);
            }
        }

        if (_last.Count == 0 || _timing.CurFrame % 600 != 0)
            return;

        _stale.Clear();
        foreach (var uid in _last.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _last.Remove(uid);
        }
    }

    /// <summary>Lays one tread mark on a tile.</summary>
    public WFSnowTread Lay(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, int depth)
    {
        if (!_marks.TryGetValue(grid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, List<WFSnowTread>>();
            _marks[grid] = tiles;
        }

        if (!tiles.TryGetValue(tile, out var list))
        {
            list = new List<WFSnowTread>();
            tiles[tile] = list;
        }

        var mark = new WFSnowTread(grid, tile, position, rotation, _timing.CurTime, depth);
        list.Add(mark);
        _order.Enqueue(mark);
        Count++;
        return mark;
    }

    /// <summary>The hull's hard fixtures in the vehicle's own frame, or a tile round its origin.</summary>
    private Box2 HullBounds(EntityUid uid)
    {
        if (!TryComp(uid, out FixturesComponent? fixtures))
            return Box2.CenteredAround(Vector2.Zero, Vector2.One);

        Box2? bounds = null;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var box = fixture.Shape.ComputeAABB(PhysicsTransform.Empty, i);
                bounds = bounds?.Union(box) ?? box;
            }
        }

        return bounds ?? Box2.CenteredAround(Vector2.Zero, Vector2.One);
    }

    /// <summary>Drops marks whose snow is gone or has been filled in, after a snow update.</summary>
    private void Recheck()
    {
        _stale.Clear();
        foreach (var (grid, tiles) in _marks)
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
                    var mark = list[i];
                    // The treads press the snow flat as they lay it: what it falls to is the level to fill in from.
                    mark.Depth = Math.Min(mark.Depth, depth);
                    mark.Filled = Math.Clamp((depth - mark.Depth) / (float) WFSnowPrintsSystem.FillUnits, 0f, 1f);
                    if (depth >= WFSnowCover.CoverFrom && mark.Filled < 1f)
                        continue;

                    mark.Gone = true;
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
            foreach (var list in _marks[grid].Values)
            {
                foreach (var mark in list)
                {
                    mark.Gone = true;
                    Count--;
                }
            }

            _marks.Remove(grid);
        }
    }

    private void Remove(WFSnowTread mark)
    {
        if (mark.Gone)
            return;

        mark.Gone = true;
        Count--;
        if (!_marks.TryGetValue(mark.Grid, out var tiles) || !tiles.TryGetValue(mark.Tile, out var list))
            return;

        list.Remove(mark);
        if (list.Count > 0)
            return;

        tiles.Remove(mark.Tile);
        if (tiles.Count == 0)
            _marks.Remove(mark.Grid);
    }
}

/// <summary>One tread mark in snow, in its grid's space.</summary>
public sealed class WFSnowTread(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, TimeSpan laid, int depth)
{
    public readonly EntityUid Grid = grid;
    public readonly Vector2i Tile = tile;
    public readonly Vector2 Position = position;
    public readonly Angle Rotation = rotation;
    public readonly TimeSpan Laid = laid;

    /// <summary>The lowest snow depth seen on its tile since it was laid.</summary>
    public int Depth = depth;

    /// <summary>How far fresh snow has filled it, 0 to 1.</summary>
    public float Filled;

    public bool Gone;
}
