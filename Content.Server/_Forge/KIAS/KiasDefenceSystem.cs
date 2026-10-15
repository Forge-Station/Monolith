using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Shared._Forge.KIAS;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Content.Shared.Destructible;
using Content.Shared.Verbs;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Content.Shared.Examine;

namespace Content.Server._Forge.KIAS;

public sealed class KiasDefenceSystem : EntitySystem
{
    public void SetFireLock(EntityUid server, bool enabled)
    {
        if (EntityManager.System<KiasSystem>().IsOnline(server) && TryComp<KiasDefenceComponent>(server, out var component))
            component.FireLock = enabled;
    }
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasNavigationSystem _navigation = default!;
    [Dependency] private FireControlSystem _fire = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    private readonly HashSet<EntityUid> _projectiles = new();
    private readonly HashSet<EntityUid> _enabledGrids = new();
    private readonly HashSet<EntityUid> _reserved = new();
    private readonly Dictionary<(MapId Map, Vector2i Cell), List<EntityUid>> _buckets = new();
    private readonly Stack<List<EntityUid>> _bucketPool = new();
    private TimeSpan _nextScan;
    private readonly KiasPeriodicScheduler _scans = new(0.1);
    private readonly Dictionary<EntityUid, (EntityUid Target, EntityUid Grid, Vector2 Previous)> _interceptors = new();
    public event Action<EntityUid, EntityUid, EntityUid>? ProjectileIntercepted;

    public override void Initialize()
    {
        SubscribeLocalEvent<FireControllableComponent, ShotAttemptedEvent>(OnLockedShot);
        SubscribeLocalEvent<FireControllableComponent, ExaminedEvent>(OnFireLockExamine);
        SubscribeLocalEvent<ShipWeaponProjectileComponent, ComponentStartup>(OnProjectileStartup);
        SubscribeLocalEvent<ShipWeaponProjectileComponent, ComponentShutdown>(OnProjectileShutdown);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<KiasDefenceComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<KiasPdcWeaponComponent, ShotAttemptedEvent>(OnShot);
        SubscribeLocalEvent<KiasPdcWeaponComponent, ComponentShutdown>(OnWeaponShutdown);
        SubscribeLocalEvent<KiasPdcWeaponComponent, AmmoShotEvent>(OnAmmo);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    private void OnProjectileStartup(Entity<ShipWeaponProjectileComponent> ent, ref ComponentStartup args)
    {
        if (_enabledGrids.Count > 0)
            _projectiles.Add(ent);
    }
    private void OnProjectileShutdown(Entity<ShipWeaponProjectileComponent> ent, ref ComponentShutdown args) => _projectiles.Remove(ent);
    private void OnWeaponShutdown(Entity<KiasPdcWeaponComponent> ent, ref ComponentShutdown args) => Release(ent, ent.Comp);

    private void OnGridRemoved(GridRemovalEvent args)
    {
        _enabledGrids.Remove(args.EntityUid);
        _scans.Remove(args.EntityUid);
        ClearIdleIndex();
        foreach (var uid in _reserved.ToArray())
        {
            if (TryComp<KiasPdcWeaponComponent>(uid, out var weapon) && weapon.AutomaticGrid == args.EntityUid)
                Release(uid, weapon);
        }
        foreach (var uid in _interceptors.Where(p => p.Value.Grid == args.EntityUid).Select(p => p.Key).ToArray())
            _interceptors.Remove(uid);
    }

    private void OnTopology(ref KiasTopologyChangedEvent args) => Refresh(args.Grid);
    private void OnAvailability(ref KiasAvailabilityChangedEvent args) => Refresh(args.Grid);

    private void Refresh(EntityUid grid)
    {
        var enabled = false;
        if (_kias.ActiveGrids.Contains(grid) && TryComp<KiasGridComponent>(grid, out var runtime))
        {
            foreach (var uid in runtime.Online)
                enabled |= _kias.IsOnline(uid) && TryComp<KiasDefenceComponent>(uid, out var defence) && defence.PdcEnabled;
        }
        if (enabled)
        {
            var first = _enabledGrids.Count == 0;
            _enabledGrids.Add(grid);
            _scans.Add(grid, _timing.CurTime);
            if (first)
            {
                var query = EntityQueryEnumerator<ShipWeaponProjectileComponent>();
                while (query.MoveNext(out var uid, out _))
                    _projectiles.Add(uid);
            }
        }
        else
        {
            _enabledGrids.Remove(grid);
            _scans.Remove(grid);
        }
        ClearIdleIndex();
        foreach (var uid in _reserved.ToArray())
        {
            if (!TryComp<KiasPdcWeaponComponent>(uid, out var weapon))
                _reserved.Remove(uid);
            else if (weapon.AutomaticGrid == grid && (!enabled || !_kias.IsOnline(uid)))
                Release(uid, weapon);
        }
        if (!enabled)
        {
            foreach (var uid in _interceptors.Where(p => p.Value.Grid == grid).Select(p => p.Key).ToArray())
                _interceptors.Remove(uid);
        }
    }

    private void ClearIdleIndex()
    {
        if (_enabledGrids.Count != 0)
            return;
        _projectiles.Clear();
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
            _bucketPool.Push(bucket);
        }
        _buckets.Clear();
    }

    public bool SetEnabled(EntityUid server, EntityUid user, bool enabled)
    {
        if (!_kias.IsOnline(server) || Transform(server).GridUid is not { } grid || !_kias.CanConfigure(grid, user)
            || !TryComp<KiasDefenceComponent>(server, out var comp))
            return false;
        comp.PdcEnabled = enabled;
        Refresh(grid);
        return true;
    }

    public void SetAutomatic(EntityUid server, bool enabled)
    {
        if (!_kias.IsOnline(server) || Transform(server).GridUid is not { } grid || !TryComp<KiasDefenceComponent>(server, out var comp))
            return;
        comp.PdcEnabled = enabled;
        Refresh(grid);
    }

    private void OnVerbs(Entity<KiasDefenceComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User))
            return;
        var uid = ent.Owner;
        var user = args.User;
        var enabled = !ent.Comp.PdcEnabled;
        args.Verbs.Add(new AlternativeVerb { Text = Loc.GetString(enabled ? "kias-pdc-enable" : "kias-pdc-disable"), Act = () => SetEnabled(uid, user, enabled) });
    }

    public bool AuthorizeFire(EntityUid uid, bool automatic, EntityUid? user = null)
    {
        if (!automatic && IsFireLocked(uid) && !HasComp<KiasDecoyLauncherComponent>(uid))
        {
            if (user is { } actor) _popup.PopupEntity(Loc.GetString("kias-fire-locked"), uid, actor);
            return false;
        }
        if (!TryComp<KiasPdcWeaponComponent>(uid, out var weapon))
            return !automatic;
        if (!automatic)
        {
            if (weapon.AutomaticGrid is { } reservedGrid && _enabledGrids.Contains(reservedGrid)
                && weapon.AutomaticUntil > _timing.CurTime && _kias.IsOnline(uid))
            {
                if (user is { } actor && !TerminatingOrDeleted(actor))
                    _popup.PopupEntity(Loc.GetString("kias-pdc-manual-priority"), uid, actor);
                return false;
            }
            Release(uid, weapon);
            weapon.ManualUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
            return true;
        }
        return weapon.ManualUntil <= _timing.CurTime && weapon.AutomaticGrid is { } grid
            && _enabledGrids.Contains(grid) && _kias.IsOnline(uid) && _fire.CanFireWeapons(grid);
    }

    public bool IsFireLocked(EntityUid weapon) => Transform(weapon).GridUid is { } grid
        && _kias.HasRole(grid, KiasDeviceRole.Defence) && Comp<KiasGridComponent>(grid).Online
            .Any(uid => _kias.IsOnline(uid) && TryComp<KiasDefenceComponent>(uid, out var defence) && defence.FireLock);

    private void OnLockedShot(Entity<FireControllableComponent> ent, ref ShotAttemptedEvent args)
    {
        var automatic = TryComp<KiasPdcWeaponComponent>(ent, out var pdc) && pdc.AutomaticGrid != null && args.User == ent.Owner;
        if (!AuthorizeFire(ent, automatic, args.User)) args.Cancel();
    }

    private void OnFireLockExamine(Entity<FireControllableComponent> ent, ref ExaminedEvent args)
    {
        if (IsFireLocked(ent)) args.PushMarkup(Loc.GetString("kias-fire-locked"));
    }

    private void OnShot(Entity<KiasPdcWeaponComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.AutomaticGrid == null)
            return;
        if (args.User != ent.Owner)
        {
            if (!AuthorizeFire(ent, false, args.User))
                args.Cancel();
            return;
        }
        if (!AuthorizeFire(ent, true))
        {
            args.Cancel();
            Release(ent, ent.Comp);
        }
    }

    private void Release(EntityUid uid, KiasPdcWeaponComponent weapon)
    {
        if (weapon.AutomaticGrid != null)
            _guns.CancelShots(uid, weapon.PreviousAim);
        weapon.AutomaticGrid = null;
        weapon.PreviousAim = null;
        weapon.Target = null;
        _reserved.Remove(uid);
    }

    private void OnAmmo(Entity<KiasPdcWeaponComponent> ent, ref AmmoShotEvent args)
    {
        if (ent.Comp.AutomaticGrid is not { } grid || ent.Comp.Target is not { } target || TerminatingOrDeleted(target))
            return;
        var targetPosition = _transform.GetMapCoordinates(target);
        foreach (var projectile in args.FiredProjectiles)
        {
            if (TerminatingOrDeleted(projectile) || !HasComp<ProjectileComponent>(projectile))
                continue;
            var position = _transform.GetMapCoordinates(projectile);
            if (position.MapId == targetPosition.MapId)
                _interceptors[projectile] = (target, grid, position.Position - targetPosition.Position);
        }
    }

    private void CheckInterceptions()
    {
        foreach (var (uid, track) in _interceptors.ToArray())
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) || TerminatingOrDeleted(track.Target)
                || EntityManager.IsQueuedForDeletion(track.Target) || !_enabledGrids.Contains(track.Grid))
            {
                _interceptors.Remove(uid);
                continue;
            }
            var position = _transform.GetMapCoordinates(uid);
            var target = _transform.GetMapCoordinates(track.Target);
            if (position.MapId != target.MapId)
            {
                _interceptors.Remove(uid);
                continue;
            }
            var relative = position.Position - target.Position;
            if (KiasThreatMath.SweptHit(track.Previous, relative, 0.4f))
            {
                ProjectileIntercepted?.Invoke(track.Grid, track.Target, uid);
                _destructible.DestroyEntity(track.Target);
                QueueDel(uid);
                _interceptors.Remove(uid);
            }
            else
                _interceptors[uid] = (track.Target, track.Grid, relative);
        }
    }

    private static Vector2i Cell(Vector2 position) => new((int) MathF.Floor(position.X / 500), (int) MathF.Floor(position.Y / 500));

    public override void Update(float frameTime)
    {
        using var measurement = new KiasUpdateMeasurement(_kias);
        if (_enabledGrids.Count == 0)
            return;
        if (_timing.CurTime >= _nextScan)
            RebuildProjectileIndex();
        const int gridBudget = 40;
        for (var i = 0; i < gridBudget && _scans.TryDue(_timing.CurTime, out var grid); i++) Scan(grid);
    }

    private void RebuildProjectileIndex()
    {
        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(0.1);
        CheckInterceptions();
        foreach (var uid in _reserved.ToArray())
        {
            if (TryComp<KiasPdcWeaponComponent>(uid, out var weapon) && weapon.AutomaticUntil <= _timing.CurTime)
                Release(uid, weapon);
        }
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
            _bucketPool.Push(bucket);
        }
        _buckets.Clear();
        foreach (var uid in _projectiles)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<ProjectileComponent>(uid, out var projectile) || projectile.ProjectileSpent
                || projectile.OnlyCollideWhenShot && projectile.Weapon == null)
                continue;
            var position = _transform.GetMapCoordinates(uid);
            var key = (position.MapId, Cell(position.Position));
            if (!_buckets.TryGetValue(key, out var bucket))
                _buckets.Add(key, bucket = _bucketPool.TryPop(out var cached) ? cached : new());
            bucket.Add(uid);
        }
    }

    private void Scan(EntityUid grid)
    {
        if (!TryComp<KiasGridComponent>(grid, out var runtime) || !TryComp<MapGridComponent>(grid, out var map)
            || !_fire.CanFireWeapons(grid))
            return;
        var center = _transform.ToMapCoordinates(new EntityCoordinates(grid, map.LocalAABB.Center));
        var radius = map.LocalAABB.Size.Length() * 0.5f + 4;
        var shipVelocity = _physics.GetMapLinearVelocity(grid);
        var threats = new Dictionary<EntityUid, float>();
        Dictionary<EntityUid, bool>? friendlySources = null;
        foreach (var radar in runtime.Online)
        {
            if (!TryComp<KiasPdcRadarComponent>(radar, out var sensor) || !_kias.IsOnline(radar))
                continue;
            var position = _transform.GetMapCoordinates(radar);
            var cell = Cell(position.Position);
            var range = Math.Clamp(sensor.Range, 0, 500);
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (!_buckets.TryGetValue((position.MapId, cell + new Vector2i(x, y)), out var bucket))
                    continue;
                foreach (var projectile in bucket)
                {
                    if (TerminatingOrDeleted(projectile) || !TryComp<ProjectileComponent>(projectile, out var shot) || shot.ProjectileSpent) continue;
                    var sourceGrid = shot.Shooter is { } shooter && !Deleted(shooter) ? Transform(shooter).GridUid : null;
                    if (sourceGrid == null && shot.Weapon is { } sourceWeapon && !Deleted(sourceWeapon))
                        sourceGrid = Transform(sourceWeapon).GridUid;
                    if (sourceGrid is { } source)
                    {
                        if (source == grid) continue;
                        friendlySources ??= new();
                        if (!friendlySources.TryGetValue(source, out var friendly))
                            friendlySources[source] = friendly = _navigation.Classify(grid, source) == KiasContactDisposition.Friendly;
                        if (friendly) continue;
                    }
                    var target = _transform.GetMapCoordinates(projectile).Position;
                    if (Vector2.DistanceSquared(position.Position, target) > range * range)
                        continue;
                    if (KiasThreatMath.Approaches(target - center.Position, _physics.GetMapLinearVelocity(projectile) - shipVelocity, radius, out var time))
                        threats[projectile] = time;
                }
            }
        }
        if (threats.Count == 0)
            return;
        var ordered = threats.OrderBy(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).ToArray();
        var firedByServer = new Dictionary<EntityUid, int>();
        foreach (var uid in runtime.Online.ToArray())
        {
            if (!TryComp<KiasPdcWeaponComponent>(uid, out var weapon) || !_kias.IsOnline(uid)
                || weapon.ManualUntil > _timing.CurTime || !TryComp<GunComponent>(uid, out var gun))
                continue;
            if (TryComp<AutoShootGunComponent>(uid, out var auto) && auto.Enabled && auto.CanFire && weapon.AutomaticGrid == null)
                continue;
            if (!TryComp<FireControllableComponent>(uid, out var controllable) || controllable.NextFire > _timing.CurTime
                || controllable.ControllingServer is not { } server || !TryComp<FireControlServerComponent>(server, out var gcs)
                || firedByServer.GetValueOrDefault(server) >= gcs.MaxWeapons)
                continue;
            var position = _transform.GetMapCoordinates(uid);
            foreach (var target in ordered)
            {
                var targetPosition = _transform.GetMapCoordinates(target).Position;
                var velocity = _physics.GetMapLinearVelocity(target) - _physics.GetMapLinearVelocity(uid);
                var speed = gun.ProjectileSpeedModified > 0 ? gun.ProjectileSpeedModified : gun.ProjectileSpeed;
                if (!KiasThreatMath.Intercept(targetPosition - position.Position, velocity, speed, out var time))
                    continue;
                if (weapon.AutomaticGrid == null)
                    weapon.PreviousAim = gun.ShootCoordinates;
                weapon.AutomaticGrid = grid;
                weapon.Target = target;
                weapon.AutomaticUntil = _timing.CurTime + TimeSpan.FromSeconds(0.25);
                _reserved.Add(uid);
                var aim = targetPosition + velocity * time;
                if (_fire.AttemptFire(uid, uid, _transform.ToCoordinates(new MapCoordinates(aim, position.MapId)), kiasAutomatic: true))
                {
                    firedByServer[server] = firedByServer.GetValueOrDefault(server) + 1;
                    break;
                }
                Release(uid, weapon);
            }
        }
    }
}
