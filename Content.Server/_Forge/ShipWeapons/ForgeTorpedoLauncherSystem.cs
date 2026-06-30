using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Forge.ShipWeapons;

public sealed class ForgeTorpedoLauncherSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TargetSeekingSystem _targetSeeking = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private List<Entity<MapGridComponent>> _targetGridScratch = new();
    private readonly Dictionary<EntityUid, EntityUid> _pendingTargets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnExamined(EntityUid uid, ForgeTorpedoLauncherComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("forge-torpedo-launcher-examine"));
    }

    private void OnShutdown(EntityUid uid, ForgeTorpedoLauncherComponent component, ComponentShutdown args)
    {
        _pendingTargets.Remove(uid);
    }

    private void OnAmmoShot(EntityUid uid, ForgeTorpedoLauncherComponent component, AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
        {
            return;
        }

        if (!_pendingTargets.Remove(uid, out var targetGrid))
        {
            if (!TryComp<GunComponent>(uid, out var gun) ||
                gun.ShootCoordinates is not { } aimCoordinates ||
                !TryFindTargetGrid(uid, aimCoordinates, component, Transform(uid), out targetGrid))
            {
                return;
            }
        }

        if (!Exists(targetGrid))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<TargetSeekingComponent>(projectile, out var seeking))
                continue;

            _targetSeeking.SetSeekerTarget((projectile, seeking), targetGrid, Transform(projectile));
        }
    }

    public Vector2 GetLaunchDirection(TransformComponent xform)
    {
        return _transform.GetWorldRotation(xform).ToWorldVec();
    }

    public bool TryPrepareLaunch(
        EntityUid uid,
        EntityCoordinates aimCoordinates,
        Vector2 launchDirection,
        ForgeTorpedoLauncherComponent? component,
        TransformComponent? xform,
        out EntityCoordinates launchCoordinates)
    {
        launchCoordinates = default;
        _pendingTargets.Remove(uid);

        if (!Resolve(uid, ref component, ref xform))
            return false;

        if (launchDirection.LengthSquared() <= 0.0001f)
            return false;

        launchDirection = Vector2.Normalize(launchDirection);

        if (!CanLaunch(uid, launchDirection, component, xform) ||
            !TryFindTargetGrid(uid, aimCoordinates, component, xform, out var targetGrid))
        {
            return false;
        }

        var launcherCoords = _transform.GetMapCoordinates(xform);
        var launchMapCoordinates = new MapCoordinates(
            launcherCoords.Position + launchDirection * component.LaunchAimDistance,
            launcherCoords.MapId);

        launchCoordinates = _transform.ToCoordinates((uid, xform), launchMapCoordinates);
        _pendingTargets[uid] = targetGrid;
        return true;
    }

    public bool CanLaunch(
        EntityUid uid,
        Vector2 worldDirection,
        ForgeTorpedoLauncherComponent? component = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref component, ref xform))
            return false;

        if (worldDirection.LengthSquared() <= 0.0001f)
            return false;

        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        worldDirection = Vector2.Normalize(worldDirection);

        return HasSpaceAhead(gridUid, grid, xform, worldDirection, component) &&
               HasClearMuzzleRay(uid, gridUid, xform, worldDirection, component);
    }

    public bool TryFindTargetGrid(
        EntityUid uid,
        EntityCoordinates aimCoordinates,
        ForgeTorpedoLauncherComponent? component,
        TransformComponent? xform,
        out EntityUid targetGrid)
    {
        targetGrid = default;

        if (!Resolve(uid, ref component, ref xform))
            return false;

        var aimMapCoordinates = _transform.ToMapCoordinates(aimCoordinates);
        if (aimMapCoordinates.MapId != xform.MapID)
            return false;

        var searchRadius = MathF.Max(0f, component.TargetSearchRadius);
        var searchExtents = new Vector2(searchRadius, searchRadius);
        var searchBox = new Box2(
            aimMapCoordinates.Position - searchExtents,
            aimMapCoordinates.Position + searchExtents);

        _targetGridScratch.Clear();
        _mapManager.FindGridsIntersecting(aimMapCoordinates.MapId, searchBox, ref _targetGridScratch, approx: true, includeMap: false);

        var sourceGrid = xform.GridUid;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < _targetGridScratch.Count; i++)
        {
            var candidate = _targetGridScratch[i];
            var candidateUid = candidate.Owner;

            if (candidateUid == sourceGrid ||
                TerminatingOrDeleted(candidateUid) ||
                !HasEnoughTiles(candidateUid, candidate.Comp, component.MinTargetTiles))
            {
                continue;
            }

            var candidateXform = Transform(candidateUid);
            if (candidateXform.MapID != aimMapCoordinates.MapId)
                continue;

            var bounds = _transform.GetWorldMatrix(candidateXform).TransformBox(candidate.Comp.LocalAABB);
            var distance = DistanceSquaredToBox(aimMapCoordinates.Position, bounds);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            targetGrid = candidateUid;
        }

        _targetGridScratch.Clear();
        return targetGrid != default;
    }

    private bool HasSpaceAhead(
        EntityUid gridUid,
        MapGridComponent grid,
        TransformComponent launcherXform,
        Vector2 worldDirection,
        ForgeTorpedoLauncherComponent component)
    {
        var launcherCoords = _transform.GetMapCoordinates(launcherXform);
        var probeCoords = launcherCoords.Offset(worldDirection * component.TileProbeDistance);
        var probeTile = _map.TileIndicesFor(gridUid, grid, probeCoords);

        if (!_map.TryGetTileRef(gridUid, grid, probeTile, out var tileRef))
            return true;

        return tileRef.Tile.IsEmpty ||
               _turf.GetContentTileDefinition(tileRef).ID == ContentTileDefinition.SpaceID;
    }

    private bool HasClearMuzzleRay(
        EntityUid uid,
        EntityUid gridUid,
        TransformComponent launcherXform,
        Vector2 worldDirection,
        ForgeTorpedoLauncherComponent component)
    {
        var launcherCoords = _transform.GetMapCoordinates(launcherXform);
        var ray = new CollisionRay(
            launcherCoords.Position,
            worldDirection,
            collisionMask: component.ClearanceCollisionMask);

        foreach (var _ in _physics.IntersectRayWithPredicate(
                     launcherXform.MapID,
                     ray,
                     uid,
                     IgnoreNonMuzzleBlockers,
                     component.ClearanceRayDistance,
                     returnOnFirstHit: true))
        {
            return false;
        }

        return true;

        bool IgnoreNonMuzzleBlockers(EntityUid entity, EntityUid source)
        {
            if (entity == source)
                return true;

            if (!TryComp<TransformComponent>(entity, out var otherXform))
                return false;

            return otherXform.GridUid != gridUid;
        }
    }

    private bool HasEnoughTiles(EntityUid gridUid, MapGridComponent grid, int minimumTiles)
    {
        if (minimumTiles <= 0)
            return true;

        var count = 0;
        var enumerator = _map.GetAllTilesEnumerator(gridUid, grid);
        while (enumerator.MoveNext(out _))
        {
            count++;
            if (count >= minimumTiles)
                return true;
        }

        return false;
    }

    private static float DistanceSquaredToBox(Vector2 point, Box2 box)
    {
        var dx = MathF.Max(MathF.Max(box.Left - point.X, 0f), point.X - box.Right);
        var dy = MathF.Max(MathF.Max(box.Bottom - point.Y, 0f), point.Y - box.Top);

        return dx * dx + dy * dy;
    }
}
