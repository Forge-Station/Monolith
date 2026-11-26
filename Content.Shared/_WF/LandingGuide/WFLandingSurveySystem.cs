using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Doors.Components;
using Content.Shared.Mining.Components;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LandingGuide;

/// <summary>
/// Reads the ground under a hull flying a planet's air the way a gentle set-down does. The server's set-down refusal
/// and the pilot's landing guide both ask this, so the guide shows exactly what would hold a hull off.
/// </summary>
public sealed partial class WFLandingSurveySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> DiagonalTag = "Diagonal";

    /// <summary>Half the side, in tiles, of the box round an obstacle that counts as under a hull: just inside its own tile.</summary>
    public const float ObstacleHalfExtent = 0.45f;

    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<FTLSmashImmuneComponent> _immuneQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private List<Entity<MapGridComponent>> _otherGrids = new();

    public override void Initialize()
    {
        base.Initialize();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _immuneQuery = GetEntityQuery<FTLSmashImmuneComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
    }

    /// <summary>Whether any of an entity's fixtures is hard and impassable: what a hull can't rest on.</summary>
    public static bool IsSolid(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a solid thing is one a landing hull flattens, as an FTL arrival does: trees, rocks and rock walls,
    /// furniture, machines, tables and grilles. Built walls, windows, doors and what is immune to the smash are not.
    /// </summary>
    public bool IsCrushable(EntityUid uid)
    {
        return !_immuneQuery.HasComp(uid) && !_doorQuery.HasComp(uid)
               && (HasComp<MiningScannerViewableComponent>(uid) || !_tags.HasAnyTag(uid, WallTag, WindowTag, DiagonalTag));
    }

    /// <summary>Whether an entity is still there, solid and not something a landing hull crushes: it holds a hull off.</summary>
    public bool IsBlockingObstacle(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && _fixturesQuery.TryComp(uid, out var fixtures) && IsSolid(fixtures)
               && !IsCrushable(uid);
    }

    /// <summary>Whether a map is a planet's ground layer.</summary>
    public bool IsGroundLayer(EntityUid map)
    {
        return HasComp<CEZGroundLayerComponent>(map) || TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0;
    }

    /// <summary>
    /// The ground a hull would set down on while it flies a planet's air: on an air layer, or in the gap between two
    /// layers above the ground. False in space, in orbit, on the ground and below it.
    /// </summary>
    public bool TryGetLandingGround(EntityUid hull, out EntityUid ground)
    {
        ground = EntityUid.Invalid;

        if (Transform(hull).MapUid is not { } map)
            return false;

        var level = map;
        if (TryComp<CEZTransitMapComponent>(map, out var transit))
        {
            if (transit.LowerMap is not { } lower)
                return false;
            level = lower;
        }
        else if (IsGroundLayer(map))
        {
            return false;
        }

        if (!HasComp<WFPlanetLayerComponent>(level) || HasComp<WFOrbitLayerComponent>(level)
            || HasComp<WFCavernLayerComponent>(level))
            return false;

        for (var i = 0; i <= CESharedZLevelsSystem.MaxZLevelsBelowRendering; i++)
        {
            if (IsGroundLayer(level))
            {
                ground = level;
                return _gridQuery.HasComp(level);
            }

            if (!_zLevels.TryMapOffset(level, -1, out var below))
                return false;

            level = below;
        }

        return false;
    }

    /// <summary>Whether anything hard and anchored on the ground stands under one of the hull's real tiles.</summary>
    public bool ObstacleUnder(Entity<MapGridComponent> hull, Entity<MapGridComponent> ground)
    {
        var bounds = _transform.GetWorldMatrix(hull).TransformBox(hull.Comp.LocalAABB);
        foreach (var obstacle in _map.GetAnchoredEntities(ground, ground.Comp, new Box2Rotated(bounds, Angle.Zero)))
        {
            if (!IsBlockingObstacle(obstacle))
                continue;

            if (UnderHull(hull, _transform.GetWorldPosition(obstacle), ground.Comp.TileSize))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The ground tiles under a hull's real tiles: every tile an obstacle anchored on would hold the hull off from,
    /// found the way <see cref="ObstacleUnder"/> looks, so the two always agree.
    /// </summary>
    public void GetFootprint(Entity<MapGridComponent> hull, Entity<MapGridComponent> ground, HashSet<Vector2i> footprint)
    {
        footprint.Clear();

        var bounds = _transform.GetWorldMatrix(hull).TransformBox(hull.Comp.LocalAABB);
        var tiles = _map.GetTilesIntersecting(ground, ground.Comp, new Box2Rotated(bounds, Angle.Zero), ignoreEmpty: false);
        while (tiles.MoveNext(out var tile))
        {
            if (UnderHull(hull, _map.GridTileToWorldPos(ground, ground.Comp, tile.GridIndices), ground.Comp.TileSize))
                footprint.Add(tile.GridIndices);
        }
    }

    /// <summary>What one ground tile holds for a hull setting down on it.</summary>
    public WFLandingTile Classify(Entity<MapGridComponent> ground, Vector2i index)
    {
        var centre = _map.GridTileToWorldPos(ground, ground.Comp, index);
        var half = new Vector2(ground.Comp.TileSize * ObstacleHalfExtent);

        _otherGrids.Clear();
        _map.FindGridsIntersecting(ground, new Box2(centre - half, centre + half), ref _otherGrids, approx: false,
            includeMap: false);
        if (_otherGrids.Count > 0)
            return WFLandingTile.Grid;

        var anchored = _map.GetAnchoredEntities(ground, ground.Comp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (IsBlockingObstacle(uid.Value))
                return WFLandingTile.Blocked;
        }

        if (!_map.TryGetTileRef(ground, ground.Comp, index, out var tile) || tile.Tile.IsEmpty)
            return WFLandingTile.NoGround;

        return WFLandingTile.Clear;
    }

    /// <summary>Whether the obstacle box round a point on the ground reaches one of the hull's real tiles.</summary>
    private bool UnderHull(Entity<MapGridComponent> hull, Vector2 position, float groundTileSize)
    {
        var half = new Vector2(groundTileSize * ObstacleHalfExtent);
        foreach (var tile in _map.GetTilesIntersecting(hull, hull.Comp, new Box2(position - half, position + half)))
        {
            if (!tile.Tile.IsEmpty)
                return true;
        }

        return false;
    }
}
