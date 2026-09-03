using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Power.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.ShipWeapons;

public sealed class ForgeTorpedoLauncherSystem : EntitySystem
{
    private static readonly TimeSpan ShotResultTimeout = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TargetSeekingSystem _targetSeeking = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private readonly List<Entity<MapGridComponent>> _targetGridScratch = new();
    private readonly Dictionary<EntityUid, PendingLaunch> _pendingLaunches = new();
    private readonly List<EntityUid> _pendingLaunchScratch = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<ForgeTorpedoLauncherComponent, OnEmptyGunShotEvent>(OnEmptyShot);
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
        CancelPreparedLaunch(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _pendingLaunchScratch.Clear();
        _pendingLaunchScratch.AddRange(_pendingLaunches.Keys);

        foreach (var weapon in _pendingLaunchScratch)
        {
            if (!_pendingLaunches.TryGetValue(weapon, out var pending))
                continue;

            if (pending.Fired)
            {
                if (pending.ExpireTime <= _timing.CurTime)
                    _pendingLaunches.Remove(weapon);

                continue;
            }

            if (pending.FireTime > _timing.CurTime)
                continue;

            if (!TryGetLaunchState(pending, out var component, out var xform, out var gun) ||
                TerminatingOrDeleted(pending.TargetGrid) ||
                !CanFireNow(pending.Weapon, gun, pending.NoServer) ||
                !CanLaunch(pending.Weapon, GetLaunchDirection(xform), component, xform))
            {
                _pendingLaunches.Remove(weapon);
                continue;
            }

            _pendingLaunches[weapon] = pending with
            {
                Fired = true,
                ExpireTime = _timing.CurTime + ShotResultTimeout,
            };

            _gun.AttemptShots(pending.User, pending.Weapon, gun, pending.FireCoordinates, TimeSpan.FromSeconds(0.2));
        }

        _pendingLaunchScratch.Clear();
    }

    private void OnAmmoShot(EntityUid uid, ForgeTorpedoLauncherComponent component, AmmoShotEvent args)
    {
        if (!_pendingLaunches.Remove(uid, out var pending) ||
            !pending.Fired ||
            args.FiredProjectiles.Count == 0 ||
            TerminatingOrDeleted(pending.TargetGrid))
        {
            return;
        }

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<TargetSeekingComponent>(projectile, out var seeking))
                continue;

            _targetSeeking.SetSeekerTarget((projectile, seeking), pending.TargetGrid, Transform(projectile));
        }
    }

    private void OnEmptyShot(EntityUid uid, ForgeTorpedoLauncherComponent component, ref OnEmptyGunShotEvent args)
    {
        CancelPreparedLaunch(uid);
    }

    public bool HasPendingLaunch(EntityUid uid)
    {
        return _pendingLaunches.ContainsKey(uid);
    }

    public void CancelPreparedLaunch(EntityUid uid)
    {
        _pendingLaunches.Remove(uid);
    }

    public Vector2 GetLaunchDirection(TransformComponent xform)
    {
        return _transform.GetWorldRotation(xform).ToWorldVec();
    }

    public bool TryQueueLaunch(
        EntityUid uid,
        EntityUid user,
        EntityCoordinates aimCoordinates,
        Vector2 launchDirection,
        ForgeTorpedoLauncherComponent component,
        TransformComponent xform,
        bool noServer)
    {
        if (_pendingLaunches.ContainsKey(uid) ||
            !TryComp<GunComponent>(uid, out var gun) ||
            !CanFireNow(uid, gun, noServer) ||
            !TryBuildPendingLaunch(uid, user, aimCoordinates, launchDirection, component, xform, noServer, out var pending))
        {
            return false;
        }

        _pendingLaunches[uid] = pending;
        return true;
    }

    private bool TryBuildPendingLaunch(
        EntityUid uid,
        EntityUid user,
        EntityCoordinates aimCoordinates,
        Vector2 launchDirection,
        ForgeTorpedoLauncherComponent component,
        TransformComponent xform,
        bool noServer,
        out PendingLaunch pending)
    {
        pending = default;

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

        var minDelay = MathF.Max(0f, component.LaunchDelayMin);
        var maxDelay = MathF.Max(0f, component.LaunchDelayMax);
        if (maxDelay < minDelay)
            (minDelay, maxDelay) = (maxDelay, minDelay);

        var delay = maxDelay > minDelay
            ? _random.NextFloat(minDelay, maxDelay)
            : minDelay;

        pending = new PendingLaunch(
            uid,
            user,
            _transform.ToCoordinates((uid, xform), launchMapCoordinates),
            targetGrid,
            _timing.CurTime + TimeSpan.FromSeconds(delay),
            noServer);

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
               HasClearMuzzleRay(uid, xform, worldDirection, component);
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

    private bool TryGetLaunchState(
        PendingLaunch pending,
        out ForgeTorpedoLauncherComponent component,
        out TransformComponent xform,
        out GunComponent gun)
    {
        component = default!;
        xform = default!;
        gun = default!;

        return !TerminatingOrDeleted(pending.Weapon) &&
               !TerminatingOrDeleted(pending.User) &&
               TryComp(pending.Weapon, out component) &&
               TryComp(pending.Weapon, out xform) &&
               TryComp(pending.Weapon, out gun);
    }

    private bool CanFireNow(EntityUid weapon, GunComponent gun, bool noServer)
    {
        if (!_power.IsPowered(weapon) || !_gun.CanShoot(gun))
            return false;

        return noServer ||
               TryComp<FireControllableComponent>(weapon, out var fireControllable) &&
               fireControllable.ControllingServer != null;
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
                     (entity, source) => entity == source,
                     component.ClearanceRayDistance,
                     returnOnFirstHit: true))
        {
            return false;
        }

        return true;
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

    private readonly record struct PendingLaunch(
        EntityUid Weapon,
        EntityUid User,
        EntityCoordinates FireCoordinates,
        EntityUid TargetGrid,
        TimeSpan FireTime,
        bool NoServer,
        bool Fired = false,
        TimeSpan ExpireTime = default);
}
