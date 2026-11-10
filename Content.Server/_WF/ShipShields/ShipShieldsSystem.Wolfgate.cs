using System.Numerics;
using Content.Server._WF.ShipShields;
using System.Linq;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Content.Shared.Projectiles;
using Content.Shared._Mono.Weapons.Ranged.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // The surface covers one tick of travel below the default projectile raycast cutoff.
    private const float ShieldSurfaceRadius = 0.65f;

    [Dependency] private SharedMapSystem _wfShieldMap = default!;
    private readonly HashSet<EntityUid> _wfChangedShieldHulls = new();
    private float _wfHullAccumulator;

    /// <summary>Tracks hull changes without rebuilding every projectile tick.</summary>
    private void InitializeWolfgateShields()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnWolfgateShieldTileChanged);
        SubscribeLocalEvent<ShipShieldComponent, ProjectileReflectAttemptEvent>(OnWolfgateShieldContact);
        SubscribeLocalEvent<ShipShieldComponent, WFShipShieldProjectileRayHitEvent>(OnWolfgateShieldRayHit);
        SubscribeLocalEvent<ShipShieldedComponent, MoveEvent>(OnWolfgateShieldGridMove);
        SubscribeLocalEvent<ShipShieldedComponent, EntityTerminatingEvent>(OnWolfgateShieldGridTerminating);
    }

    private void OnWolfgateShieldGridMove(EntityUid uid, ShipShieldedComponent component, ref MoveEvent args)
    {
        if (!TerminatingOrDeleted(uid) && !TerminatingOrDeleted(component.Shield))
            FollowWolfgateShieldGrid(component.Shield, uid);
    }

    private void OnWolfgateShieldGridTerminating(EntityUid uid, ShipShieldedComponent component, ref EntityTerminatingEvent args)
    {
        _wfChangedShieldHulls.Remove(uid);
        TryQueueDel(component.Shield);
    }
    private void OnWolfgateShieldTileChanged(ref TileChangedEvent args)
    {
        if (HasComp<ShipShieldedComponent>(args.Entity) && args.Changes.Any(c => c.OldTile.IsEmpty != c.NewTile.IsEmpty))
            _wfChangedShieldHulls.Add(args.Entity);
    }

    /// <summary>Refreshes changing hulls and emitter health.</summary>
    private void UpdateWolfgateShields(float frameTime)
    {
        UpdateWolfgateFtlShields();
        UpdateWolfgateShieldTails();
        UpdateWolfgateShieldPvs(frameTime);
        var query = EntityQueryEnumerator<WFShipShieldVisualsComponent, ShipShieldComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var shield))
            UpdateWolfgateShieldHealth(uid, visuals, shield.Source);

        _wfHullAccumulator += frameTime;
        if (_wfHullAccumulator < 0.5f)
            return;
        _wfHullAccumulator = 0f;
        foreach (var grid in _wfChangedShieldHulls)
        {
            if (TryComp<ShipShieldedComponent>(grid, out var shielded) &&
                TryComp<MapGridComponent>(grid, out var mapGrid) &&
                TryComp<PhysicsComponent>(shielded.Shield, out var physics))
                CreateWolfgateShieldHull(shielded.Shield, grid, mapGrid, physics);
        }
        _wfChangedShieldHulls.Clear();
    }

    /// <summary>Keeps the perimeter in the map broadphase beyond the grid tile bounds.</summary>
    private void FollowWolfgateShieldGrid(EntityUid shield, EntityUid grid)
    {
        if (TerminatingOrDeleted(shield) || TerminatingOrDeleted(grid) ||
            !TryComp(grid, out TransformComponent? gridTransform))
            return;
        if (gridTransform.MapUid == null || gridTransform.MapID == MapId.Nullspace)
        {
            _transformSystem.DetachEntity(shield);
            return;
        }
        var origin = _transformSystem.ToMapCoordinates(new EntityCoordinates(grid, Vector2.Zero));
        // One call, so a ship's move costs the shield one move event and one lookup update rather than two.
        var coordinates = _transformSystem.ToCoordinates(origin);
        var rotation = _transformSystem.GetWorldRotation(grid) - _transformSystem.GetWorldRotation(coordinates.EntityId);
        _transformSystem.SetCoordinates(shield, Transform(shield), coordinates, rotation);
    }

    /// <summary>Passes shots from the protected ship and other protected autonomous fire.</summary>
    private bool IsWolfgateShieldFriendlyProjectile(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<ShipShieldComponent>(shield, out var shieldComp))
            return false;
        if (TryComp<ProjectileGridPhaseComponent>(projectile, out var phase) && phase.SourceGrid == shieldComp.Shielded)
            return true;
        if (!_shipWeaponProjectileQuery.HasComponent(projectile) || !_projectileQuery.TryGetComponent(projectile, out var shot))
            return false;
        var attempt = new WFShipShieldInterceptAttemptEvent(shieldComp.Shielded, projectile, shot.Weapon, shot.Shooter);
        RaiseLocalEvent(ref attempt);
        return attempt.Cancelled;
    }
    /// <summary>Uses the padded hull perimeter for both collisions and rendering.</summary>
    private void CreateWolfgateShieldHull(EntityUid shield, EntityUid grid, MapGridComponent mapGrid, PhysicsComponent physics, Vector2[][]? existingContours = null)
    {
        FollowWolfgateShieldGrid(shield, grid);
        var visuals = EnsureComp<WFShipShieldVisualsComponent>(shield);
        var contours = existingContours ?? WFShipShieldGeometry.CreateContours(_wfShieldMap.GetAllTiles(grid, mapGrid).Select(t => t.GridIndices), mapGrid.TileSize);
        // A tile loss inside the same bounding fit leaves the perimeter unchanged: keep the fixtures and skip the resend.
        if (existingContours == null && TryComp<WFShipShieldShuntComponent>(shield, out var previous) &&
            previous.Center == mapGrid.LocalAABB.Center && ContoursMatch(visuals.Contours, contours) &&
            TryComp<FixturesComponent>(shield, out var current) &&
            current.Fixtures.Keys.Any(n => n.StartsWith("wfShield", StringComparison.Ordinal)))
            return;
        if (TryComp<FixturesComponent>(shield, out var fixtures))
        {
            foreach (var name in fixtures.Fixtures.Keys.Where(n => n.StartsWith("wfShield", StringComparison.Ordinal)).ToArray())
                _fixtureSystem.DestroyFixture(shield, name, updates: false, body: physics);
        }
        var allocation = SyncWolfgateShieldShunt(shield, grid, mapGrid);
        var index = 0;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var start = contour[i];
            var end = contour[(i + 1) % contour.Length];
            foreach (var segment in WFShipShieldShuntMath.ProtectedSegments(start, end, allocation.Center,
                         allocation.DirectionRadians, allocation.Concentration, allocation.ArcRadians))
            {
                var edge = new EdgeShape();
                edge.SetOneSided(contour[(i + contour.Length - 1) % contour.Length], segment.Start,
                    segment.End, contour[(i + 2) % contour.Length]);
                var name = $"wfShield{index++}";
                _fixtureSystem.TryCreateFixture(shield, edge, name, hard: true,
                    collisionLayer: (int) CollisionGroup.BulletImpassable, updates: false, body: physics);
                if (_fixtureSystem.GetFixtureOrNull(shield, name) is { } fixture)
                    _physicsSystem.SetRadius(shield, name, fixture, edge, ShieldSurfaceRadius * mapGrid.TileSize, body: physics);
            }
        }
        // Removing the last fixture normally disables collision; restore it after the whole rebuild.
        _fixtureSystem.FixtureUpdate(shield, body: physics);
        _physicsSystem.SetCanCollide(shield, index > 0, body: physics);
        _physicsSystem.WakeBody(shield, body: physics);
        visuals.Grid = grid;
        visuals.Contours = contours;
        UpdateWolfgateShieldHealth(shield, visuals, Comp<ShipShieldComponent>(shield).Source);
        if (existingContours == null)
        {
            DirtyFields(shield, visuals, null,
                nameof(WFShipShieldVisualsComponent.Contours),
                nameof(WFShipShieldVisualsComponent.Grid),
                nameof(WFShipShieldVisualsComponent.Health));
        }
    }

    /// <summary>Compares contour sets vertex by vertex within a small tolerance.</summary>
    private static bool ContoursMatch(Vector2[][] a, Vector2[][] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Length != b[i].Length)
                return false;
            for (var j = 0; j < a[i].Length; j++)
            {
                if (Math.Abs(a[i][j].X - b[i][j].X) > 1e-3f || Math.Abs(a[i][j].Y - b[i][j].Y) > 1e-3f)
                    return false;
            }
        }
        return true;
    }

    private void UpdateWolfgateShieldHealth(EntityUid uid, WFShipShieldVisualsComponent visuals, EntityUid? source)
    {
        var health = source is { } emitterUid && TryComp<ShipShieldEmitterComponent>(emitterUid, out var emitter)
            ? GetWolfgateShieldHealth(emitter) : 1f;
        if (Math.Abs(visuals.Health - health) < 0.001f)
            return;
        visuals.Health = health;
        DirtyField(uid, visuals, nameof(WFShipShieldVisualsComponent.Health));
    }

    /// <summary>Returns remaining capacity against damage and additional power limits.</summary>
    public static float GetWolfgateShieldHealth(ShipShieldEmitterComponent emitter)
    {
        var damageFraction = emitter.Damage / Math.Max(emitter.DamageLimit, 1f);
        var loadFraction = CalculateLoadDamage(emitter) / Math.Max(emitter.MaxDraw, 1f);
        return Math.Clamp(1f - Math.Max(damageFraction, loadFraction), 0f, 1f);
    }

    /// <summary>Consumes a confirmed ray hit before its one-sided edge can miss physical contact.</summary>
    private void OnWolfgateShieldRayHit(EntityUid uid, ShipShieldComponent component, ref WFShipShieldProjectileRayHitEvent args)
    {
        if (IsWolfgateShieldFriendlyProjectile(uid, args.Projectile) ||
            !_shipWeaponProjectileQuery.HasComponent(args.Projectile) || args.Component.ProjectileSpent)
            return;
        _transformSystem.SetCoordinates(args.Projectile, _transformSystem.ToCoordinates(args.Position));
        var contact = new ProjectileReflectAttemptEvent(args.Projectile, args.Component, false);
        OnWolfgateShieldContact(uid, component, ref contact);
        args.Handled = args.Component.ProjectileSpent;
    }

    /// <summary>Consumes ship weapon projectiles after a real shield contact.</summary>
    private void OnWolfgateShieldContact(EntityUid uid, ShipShieldComponent component, ref ProjectileReflectAttemptEvent args)
    {
        if (IsWolfgateShieldFriendlyProjectile(uid, args.ProjUid) || !_shipWeaponProjectileQuery.HasComponent(args.ProjUid) ||
            !_projectileQuery.TryGetComponent(args.ProjUid, out var projectile) || projectile.ProjectileSpent)
            return;
        args.Cancelled = true;
        var strength = WolfgateShieldStrength(uid, args.ProjUid);
        if (strength <= 0f)
            return;
        DisarmWolfgateAbsorbedProjectile(args.ProjUid);
        var impactPosition = WolfgateShieldImpactPosition(uid, args.ProjUid);
        var launchGrid = CompOrNull<ProjectileGridPhaseComponent>(args.ProjUid)?.SourceGrid;
        if (component.Source is { } source)
        {
            var emitter = Comp<ShipShieldEmitterComponent>(source);
            var previousDamage = emitter.Damage;
            var ev = new ShieldDeflectedEvent(args.ProjUid, projectile);
            RaiseLocalEvent(source, ref ev);
            emitter.Damage = previousDamage + (emitter.Damage - previousDamage) / strength;
            ReportWolfgateShieldAttack(component, emitter.Damage - previousDamage, launchGrid, projectile.Weapon, projectile.Shooter);
            var impactStrength = WFShipShieldEffects.ImpactStrength(emitter.Damage - previousDamage,
                WFShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp));
            WolfgateShieldImpact(uid, impactPosition, impactStrength);
        }
        else
        {
            WolfgateShieldImpact(uid, impactPosition, projectile.Damage.GetTotal() > 0 ? 1f : 0f);
            projectile.ProjectileSpent = true;
            QueueDel(args.ProjUid);
            ReportWolfgateShieldAttack(component, (float) projectile.Damage.GetTotal(), launchGrid, projectile.Weapon, projectile.Shooter);
        }
    }
    /// <summary>Captures the contact before projectile triggers can remove it.</summary>
    private Vector2 WolfgateShieldImpactPosition(EntityUid shield, EntityUid projectile)
    {
        var local = Vector2.Transform(_transformSystem.GetWorldPosition(projectile), _transformSystem.GetInvWorldMatrix(shield));
        return TryComp<WFShipShieldVisualsComponent>(shield, out var visuals)
            ? WFShipShieldGeometry.ClosestPoint(visuals.Contours, local)
            : local;
    }

    /// <summary>Reports a damage-scaled impact on the visible perimeter.</summary>
    private void WolfgateShieldImpact(EntityUid shield, Vector2 position, float strength)
    {
        if (strength <= 0f || !HasComp<WFShipShieldVisualsComponent>(shield))
            return;
        PlayWolfgateShieldImpact(shield, position);
        // Shield entities are visible on their whole map, including edges outside their origin's PVS.
        RaiseNetworkEvent(new WFShipShieldImpactEvent(GetNetEntity(shield), position, strength), Filter.BroadcastMap(Transform(shield).MapID));
    }
}
