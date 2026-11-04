using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Falling;

/// <summary>Lets a vehicle with <see cref="VehicleFallComponent"/> drive off an edge where a z-level lies below and drop to it.</summary>
public abstract partial class SharedVehicleFallSystem : EntitySystem
{
    private const float SampleSpacing = 0.4f;
    private const float SampleInset = 0.05f;
    private const float SearchRadius = 2f;
    private const float SearchStep = 0.5f;
    private const float FootprintInset = 0.05f;
    private const int LandingCacheLimit = 512;

    /// <summary>The side, in tiles, of the squares of the map in which a changed tile is remembered.</summary>
    private const int ChangeCellSize = 8;

    /// <summary>How far past a vehicle a changed tile still makes the floor under it worth measuring again.</summary>
    private const float ChangeMargin = 1f;

    private const int ChangeCellLimit = 65536;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LandingCacheLife = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan SupportedRecheck = TimeSpan.FromSeconds(2);
    private static readonly Vector2[] SearchOffsets = BuildSearchOffsets();

    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<CEZMapComponent> _zMapQuery = default!;

    private readonly Dictionary<(EntityUid Vehicle, EntityUid Map, int X, int Y), (bool Landing, TimeSpan Until)> _landingCache = new();
    private readonly Dictionary<(EntityUid Vehicle, int X, int Y), (bool Landing, TimeSpan Until)> _handOverCache = new();
    private readonly Dictionary<(MapId Map, int X, int Y), int> _changedAt = new();
    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _pendingScratch = new();
    private readonly List<EntityUid> _candidates = new();
    private readonly Dictionary<EntityUid, TimeSpan> _retryAt = new();
    private readonly List<Vector2> _supportSamples = new();
    private readonly Dictionary<EntityUid, SupportedPose> _supportedPoses = new();
    private TimeSpan _nextSweep;
    private int _floorVersion;
    private int _changesForgotten;

    /// <summary>How many times a whole footprint has been sampled for floor; the sweep skips a vehicle it knows is supported.</summary>
    public int FootprintSamplings { get; private set; }

    /// <summary>How many times the level below has been searched for room to land.</summary>
    public int LandingSearches { get; private set; }

    /// <summary>Where a vehicle stood, and how many tile changes there had been, when every sample of it had floor under it.</summary>
    private readonly record struct SupportedPose(Vector2 Position, Angle Rotation, int FloorVersion, TimeSpan At);

    /// <summary>What the footprint check says about a vehicle.</summary>
    private enum FallResult : byte
    {
        /// <summary>Not a vehicle that can fall now: disabled, no level below, or not standing free.</summary>
        Invalid,

        /// <summary>Every sample of the footprint has floor under it.</summary>
        Supported,

        /// <summary>Part of the footprint hangs over nothing, but less than the vehicle tolerates.</summary>
        Overhanging,

        /// <summary>The vehicle should drop and the level below has no room for it.</summary>
        NoLanding,

        Fell,
    }

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(GridVehicleMoverSystem));

        SubscribeLocalEvent<VehicleFallComponent, VehicleOpenTileAttemptEvent>(OnOpenTile);
        SubscribeLocalEvent<VehicleFallComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var version = ++_floorVersion;
        var xform = Transform(args.Entity);
        if (xform.MapID == MapId.Nullspace)
            return;

        // Only the squares of the map where a tile changed are remembered, so a change far from a vehicle, like terrain
        // streaming in on a planet, leaves it alone.
        if (_changedAt.Count >= ChangeCellLimit)
        {
            _changedAt.Clear();
            _changesForgotten = version;
        }

        var matrix = _transform.GetWorldMatrix(xform);
        var size = (float) args.Entity.Comp.TileSize;

        foreach (var change in args.Changes)
        {
            var world = Vector2.Transform(
                new Vector2((change.GridIndices.X + 0.5f) * size, (change.GridIndices.Y + 0.5f) * size),
                matrix);
            _changedAt[(xform.MapID, ChangeCellOf(world.X), ChangeCellOf(world.Y))] = version;
        }
    }

    private static int ChangeCellOf(float coordinate)
    {
        return (int) MathF.Floor(coordinate / ChangeCellSize);
    }

    /// <summary>Whether a tile changed near the vehicle after the floor was last measured as holding it up, at tile change count <paramref name="since"/>.</summary>
    private bool FloorChangedNear(EntityUid uid, TransformComponent xform, int since)
    {
        if (since < _changesForgotten)
            return true;

        var area = _lookup.GetWorldAABB(uid, xform).Enlarged(ChangeMargin);
        var maxX = ChangeCellOf(area.Right);
        var maxY = ChangeCellOf(area.Top);

        for (var x = ChangeCellOf(area.Left); x <= maxX; x++)
        {
            for (var y = ChangeCellOf(area.Bottom); y <= maxY; y++)
            {
                if (_changedAt.TryGetValue((xform.MapID, x, y), out var at) && at > since)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether an empty tile of planet terrain is only unloaded ground, which holds a vehicle up.</summary>
    protected virtual bool IsUnloadedGround(EntityUid grid, Vector2i tile)
    {
        return false;
    }

    private void OnShutdown(Entity<VehicleFallComponent> ent, ref ComponentShutdown args)
    {
        _pending.Remove(ent.Owner);
        _retryAt.Remove(ent.Owner);
        _supportedPoses.Remove(ent.Owner);
    }

    /// <summary>Whether the vehicle may cover an empty tile: when the level below has room for it near that tile.</summary>
    private void OnOpenTile(Entity<VehicleFallComponent> ent, ref VehicleOpenTileAttemptEvent args)
    {
        if (args.Allow || !ent.Comp.Enabled)
            return;

        if (!TryComp(ent, out FixturesComponent? fixtures) ||
            !TryComp(ent, out PhysicsComponent? body) ||
            !TryComp(ent, out GridVehicleMoverComponent? mover) ||
            !TryComp(args.Grid, out MapGridComponent? gridComp) ||
            !TryGetLevelBelow(Transform(args.Grid).MapUid, out var below))
        {
            return;
        }

        // Ground nobody has loaded yet is no edge; the mover keeps stopping short of it until the terrain streams in.
        if (IsUnloadedGround(args.Grid, args.Tile))
            return;

        if (HasComp<WFParachutedComponent>(ent.Owner) && HasComp<CEZPhysicsComponent>(ent.Owner))
        {
            args.Allow = true;
            return;
        }

        // Past the edge the vehicle is on the map and not driven, so the drop that follows must find room from where its
        // origin is now, which is where Evaluate looks. Room that only a search from the tile ahead finds would leave it
        // stranded on the map; it stays on the deck, and can drive back, until the origin has room.
        if (args.HandOver)
        {
            // Asked again at every step that moves a vehicle held on its deck, so the answer is kept as the other one is.
            var origin = _transform.GetWorldPosition(ent);
            var handNow = _timing.CurTime;
            var handKey = (ent.Owner, (int) MathF.Floor(origin.X * 2f), (int) MathF.Floor(origin.Y * 2f));

            if (!_handOverCache.TryGetValue(handKey, out var handCached) || handCached.Until <= handNow)
            {
                if (_handOverCache.Count >= LandingCacheLimit)
                    _handOverCache.Clear();

                handCached = (TryFindLanding(ent, fixtures, body, mover.WeightClass, below, origin, _transform.GetWorldRotation(ent), out _), handNow + LandingCacheLife);
                _handOverCache[handKey] = handCached;
            }

            if (!handCached.Landing)
                return;

            args.Allow = true;

            if (!_net.IsClient)
                _pending.Add(ent.Owner);

            return;
        }

        var centre = _map.GridTileToWorld(args.Grid, gridComp, args.Tile).Position;
        var now = _timing.CurTime;
        var key = (ent.Owner, below.Owner, (int) MathF.Floor(centre.X * 2f), (int) MathF.Floor(centre.Y * 2f));

        if (!_landingCache.TryGetValue(key, out var cached) || cached.Until <= now)
        {
            if (_landingCache.Count >= LandingCacheLimit)
                _landingCache.Clear();

            var rotation = _transform.GetWorldRotation(ent);
            cached = (TryFindLanding(ent, fixtures, body, mover.WeightClass, below, centre, rotation, out _), now + LandingCacheLife);
            _landingCache[key] = cached;
        }

        if (!cached.Landing)
            return;

        args.Allow = true;

        // The mover may now carry it over the edge; the fall is decided as soon as it has gone far enough.
        if (!_net.IsClient)
            _pending.Add(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        if (_pending.Count > 0)
        {
            _pendingScratch.Clear();
            _pendingScratch.AddRange(_pending);

            foreach (var uid in _pendingScratch)
            {
                if (!TryComp(uid, out VehicleFallComponent? fall) || !TryComp(uid, out TransformComponent? xform))
                {
                    _pending.Remove(uid);
                    continue;
                }

                if (Evaluate(uid, fall, xform) != FallResult.Overhanging)
                    _pending.Remove(uid);
            }
        }

        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        // A parked vehicle whose floor was taken away, or one set down over a gap, is found here.
        _candidates.Clear();
        var query = EntityQueryEnumerator<VehicleFallComponent, GridVehicleMoverComponent>();
        while (query.MoveNext(out var uid, out var fall, out _))
        {
            if (fall.Enabled && (!_retryAt.TryGetValue(uid, out var retry) || now >= retry))
                _candidates.Add(uid);
        }

        foreach (var uid in _candidates)
        {
            if (TryComp(uid, out VehicleFallComponent? fall) && TryComp(uid, out TransformComponent? xform))
                Evaluate(uid, fall, xform);
        }
    }

    /// <summary>Drops the vehicle when too much of its footprint hangs over nothing and the level below can take it.</summary>
    private FallResult Evaluate(EntityUid uid, VehicleFallComponent fall, TransformComponent xform)
    {
        if (!fall.Enabled || TerminatingOrDeleted(uid) ||
            HasComp<WFParachutedComponent>(uid) && HasComp<CEZPhysicsComponent>(uid))
            return FallResult.Invalid;

        // Only a vehicle standing on a grid or the map, not one inside something, and only where a level lies below.
        if (xform.MapUid is not { } map ||
            xform.ParentUid != map && xform.ParentUid != xform.GridUid ||
            !TryGetLevelBelow(map, out var below) ||
            !TryComp(uid, out FixturesComponent? fixtures) ||
            !TryComp(uid, out PhysicsComponent? body) ||
            !TryComp(uid, out GridVehicleMoverComponent? mover))
        {
            return FallResult.Invalid;
        }

        // A vehicle that was fully supported where it stands, with no tile changed near it since, is still supported; the
        // check is not made again for a while even then, in case a grid under it has moved away.
        var (worldPosition, worldRotation) = _transform.GetWorldPositionRotation(xform);
        var now = _timing.CurTime;
        if (_supportedPoses.TryGetValue(uid, out var known) &&
            now < known.At + SupportedRecheck &&
            (known.Position - worldPosition).LengthSquared() < 0.0001f &&
            known.Rotation.EqualsApprox(worldRotation) &&
            !FloorChangedNear(uid, xform, known.FloorVersion))
        {
            return FallResult.Supported;
        }

        var unsupported = GetUnsupportedFraction(uid, fixtures, xform.MapID, out var onUnloadedGround);
        if (unsupported <= 0f)
        {
            // Ground that is only unloaded can be pinned as a hole without a tile changing, so a vehicle over any is checked every sweep.
            if (!onUnloadedGround)
                _supportedPoses[uid] = new SupportedPose(worldPosition, worldRotation, _floorVersion, now);

            return FallResult.Supported;
        }

        _supportedPoses.Remove(uid);

        // The mover hands a vehicle to the map when its origin crosses an edge, and a vehicle on no grid is not driven. A hull
        // whose origin is near one end can get there with little of it over the edge, so there any gap drops it.
        var tolerated = xform.GridUid is null ? 0f : fall.UnsupportedFraction;
        if (unsupported < tolerated)
            return FallResult.Overhanging;

        if (!TryFindLanding(uid, fixtures, body, mover.WeightClass, below, worldPosition, worldRotation, out var spot))
        {
            _retryAt[uid] = now + RetryInterval;
            return FallResult.NoLanding;
        }

        Drop(uid, fall, mover, fixtures, body, xform, spot, map);
        return FallResult.Fell;
    }

    private bool TryGetLevelBelow(EntityUid? mapUid, out Entity<CEZMapComponent> below)
    {
        below = default;

        if (mapUid is not { } map || !_zMapQuery.TryComp(map, out var zMap))
            return false;

        return _zLevels.TryMapDown((map, zMap), out below);
    }

    /// <summary>The share of the footprint, sampled across its area, with no floor under it.</summary>
    private float GetUnsupportedFraction(EntityUid uid, FixturesComponent fixtures, MapId mapId, out bool onUnloadedGround)
    {
        onUnloadedGround = false;

        if (!GridVehicleMoverSystem.TryGetFixtureAabb(fixtures, PhysicsTransform.Empty, out var local))
            return 0f;

        FootprintSamplings++;
        BuildSamples(local, _supportSamples);
        var matrix = _transform.GetWorldMatrix(uid);
        var unsupported = 0;

        foreach (var sample in _supportSamples)
        {
            if (!IsSupportedAt(mapId, Vector2.Transform(sample, matrix), ref onUnloadedGround))
                unsupported++;
        }

        return (float) unsupported / _supportSamples.Count;
    }

    /// <summary>Floor on any grid at a point, or planet ground that is only unloaded.</summary>
    private bool IsSupportedAt(MapId mapId, Vector2 world, ref bool onUnloadedGround)
    {
        if (!_map.TryFindGridAt(mapId, world, out var grid, out var gridComp))
            return false;

        var tile = _map.WorldToTile(grid, gridComp, world);
        if (_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) && !tileRef.Tile.IsEmpty)
            return true;

        if (!IsUnloadedGround(grid, tile))
            return false;

        onUnloadedGround = true;
        return true;
    }

    /// <summary>Floor on any grid at a point.</summary>
    private bool HasFloorAt(MapId mapId, Vector2 world)
    {
        if (!_map.TryFindGridAt(mapId, world, out var grid, out var gridComp))
            return false;

        var tile = _map.WorldToTile(grid, gridComp, world);
        return _map.TryGetTileRef(grid, gridComp, tile, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    /// <summary>Sample points across a box in the vehicle's own frame, spaced no further apart than <see cref="SampleSpacing"/>.</summary>
    private static void BuildSamples(Box2 local, List<Vector2> samples)
    {
        samples.Clear();

        var inset = MathF.Min(SampleInset, MathF.Min(local.Width, local.Height) * 0.25f);
        var min = local.BottomLeft + new Vector2(inset);
        var max = local.TopRight - new Vector2(inset);
        var countX = Math.Max(2, (int) MathF.Ceiling((max.X - min.X) / SampleSpacing) + 1);
        var countY = Math.Max(2, (int) MathF.Ceiling((max.Y - min.Y) / SampleSpacing) + 1);

        for (var x = 0; x < countX; x++)
        {
            for (var y = 0; y < countY; y++)
            {
                samples.Add(new Vector2(
                    min.X + (max.X - min.X) * x / (countX - 1),
                    min.Y + (max.Y - min.Y) * y / (countY - 1)));
            }
        }
    }

    /// <summary>Offsets to try for a landing, nearest first, in a fixed order so the client and the server pick alike.</summary>
    private static Vector2[] BuildSearchOffsets()
    {
        var steps = (int) MathF.Round(SearchRadius / SearchStep);
        var offsets = new List<Vector2>();

        for (var x = -steps; x <= steps; x++)
        {
            for (var y = -steps; y <= steps; y++)
            {
                var offset = new Vector2(x, y) * SearchStep;
                if (offset.Length() <= SearchRadius + 0.001f)
                    offsets.Add(offset);
            }
        }

        offsets.Sort(static (a, b) =>
        {
            var byLength = a.LengthSquared().CompareTo(b.LengthSquared());
            if (byLength != 0)
                return byLength;

            var byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Y.CompareTo(b.Y);
        });

        return offsets.ToArray();
    }

    /// <summary>The nearest quarter turn to a rotation, so a landed vehicle is square to the tiles of the grid it lands on.</summary>
    private static Angle SnapToCardinal(Angle rotation)
    {
        var quarter = MathF.Round((float) (rotation.Theta / (Math.PI / 2)));
        return new Angle(quarter * (Math.PI / 2));
    }
}
