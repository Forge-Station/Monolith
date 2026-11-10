using System.Numerics;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Sends the rounds of a <see cref="ShootAtFixedPointComponent"/> gun to the aimed point and stops them there or at the first wall.</summary>
/// <remarks>
/// The ammo event is raised on the server only, and the shooter's client does not predict these rounds (their prototypes
/// carry <c>IgnorePredictionHide</c>), so the stop is the server's; the networked component lets the client stop its copy
/// of the server's round at the same time. The time stop is checked once a tick, so a round comes to rest up to a tick of
/// travel (a gun's speed over the tick rate, under a tile at the vehicle guns' speeds) past the aimed point.
/// The walls are found by a sweep (see <c>ShootAtFixedPointSystem.Sweep.cs</c>) of each tick's travel, ahead of the physics step,
/// in the map's frame, so a stop never depends on a contact event or on how any grid is turned.
/// </remarks>
public sealed partial class ShootAtFixedPointSystem : EntitySystem
{
    /// <summary>What stops a round on contact, the layers of RMC's item mask: walls, doors, windows, hulls and large machines.</summary>
    private const int BlockCollisionGroup = (int) (CollisionGroup.HighImpassable | CollisionGroup.Impassable);

    /// <summary>The furthest any fixed-distance round flies, whatever the gun and the client ask for, in tiles.</summary>
    public const float AbsoluteMaxFixedRange = 64f;

    [Dependency] private SharedBroadphaseSystem _broadphase = default!;
    [Dependency] private GunAimGuardSystem _aimGuard = default!;
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedProjectileSystem _projectiles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        // The sweep looks at the travel of the coming step, so it has to run before the physics step takes it.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));

        SubscribeLocalEvent<ShootAtFixedPointComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<ProjectileFixedDistanceComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ProjectileFixedDistanceComponent, PhysicsSleepEvent>(OnSleep);
    }

    private void OnShot(Entity<ShootAtFixedPointComponent> ent, ref AmmoShotEvent args)
    {
        if (!TryComp(ent, out GunComponent? gun))
            return;

        var origin = _transform.GetMapCoordinates(ent);
        var from = args.FiredProjectiles.Count > 0 ? _transform.GetMapCoordinates(args.FiredProjectiles[0]) : origin;

        var speed = gun.ProjectileSpeedModified;
        if (!float.IsFinite(speed) || speed <= 0f)
            return;

        // An aim that is on another map or not a number was refused when the shot was asked for; if one got here anyway, the
        // rounds it sent off on a course nobody checked are not allowed to fly.
        if (gun.ShootCoordinates is { } target && !_aimGuard.IsUsableAim(from, target))
        {
            foreach (var projectile in args.FiredProjectiles)
            {
                QueueDel(projectile);
            }

            return;
        }

        var maxRange = ent.Comp.MaxFixedRange is { } gunCap && gunCap > 0f
            ? Math.Min(gunCap, AbsoluteMaxFixedRange)
            : AbsoluteMaxFixedRange;

        // The aim comes from the client: work in doubles so a huge point cannot overflow, and keep the shot's own course
        // for a point that is not a usable distance away.
        var aimed = false;
        var baseDistance = maxRange;
        var normalized = Vector2.Zero;
        if (gun.ShootCoordinates is { } aim)
        {
            var to = _transform.ToMapCoordinates(aim).Position;
            var dx = (double) to.X - from.Position.X;
            var dy = (double) to.Y - from.Position.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            aimed = double.IsFinite(length) && length > 0.0001;

            if (aimed)
            {
                baseDistance = (float) Math.Min(length, maxRange);
                normalized = new Vector2((float) (dx / length), (float) (dy / length));
            }
        }

        if (aimed && ent.Comp.AutoAimClosestObstacle)
        {
            var ray = new CollisionRay(from.Position, normalized, (int) CollisionGroup.Impassable);
            foreach (var hit in _physics.IntersectRay(from.MapId, ray, baseDistance, returnOnFirstHit: true))
            {
                baseDistance = hit.Distance;
                break;
            }
        }

        var time = _timing.CurTime;
        var tick = (float) _timing.TickPeriod.TotalSeconds;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!_physicsQuery.TryComp(projectile, out var physics))
                continue;

            // Send the round at the point from where it is now, with none of the spread the shot came out with.
            if (aimed)
            {
                _physics.SetLinearVelocity(projectile, Vector2.Zero, body: physics);
                _physics.ApplyLinearImpulse(projectile, normalized * speed * physics.Mass, body: physics);
            }
            else if (!float.IsFinite(physics.LinearVelocity.X) || !float.IsFinite(physics.LinearVelocity.Y))
            {
                // A point the gun could not aim at left the round with no course.
                QueueDel(projectile);
                continue;
            }

            _physics.SetBodyStatus(projectile, physics, BodyStatus.InAir);

            var comp = EnsureComp<ProjectileFixedDistanceComponent>(projectile);
            comp.ArcProj = ent.Comp.ShootArcProj;

            var distance = baseDistance;
            if (TryComp(projectile, out ProjectileMaxFixedRangeComponent? cap) && cap.MaxFixedRange > 0)
                distance = distance > 0 ? Math.Min(cap.MaxFixedRange, distance) : cap.MaxFixedRange;

            comp.FlyEndTime = time + TimeSpan.FromSeconds(distance / speed);
            Dirty(projectile, comp);

            // The muzzle may be through a wall already, and the first step may carry the round through one.
            if (_net.IsServer)
                SweepSpawn((projectile, comp), origin, tick);
        }
    }

    /// <summary>An arcing round ignores everything that is not impassable.</summary>
    private void OnPreventCollide(Entity<ProjectileFixedDistanceComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.ArcProj && (args.OtherFixture.CollisionLayer & BlockCollisionGroup) == 0)
            args.Cancelled = true;
    }

    private void OnSleep(Entity<ProjectileFixedDistanceComponent> ent, ref PhysicsSleepEvent args)
    {
        StopProjectile(ent);
    }

    /// <summary>Brings a round to rest. It is spent from then on, so a mob walking onto it is not hit by it.</summary>
    private void StopProjectile(EntityUid projectile)
    {
        if (!_physicsQuery.TryComp(projectile, out var physics))
            return;

        _physics.SetLinearVelocity(projectile, Vector2.Zero, body: physics);
        _physics.SetBodyStatus(projectile, physics, BodyStatus.OnGround);

        if (_net.IsServer && _projectileQuery.TryComp(projectile, out var projectileComp) && !projectileComp.ProjectileSpent)
        {
            projectileComp.ProjectileSpent = true;
            Dirty(projectile, projectileComp);
        }

        if (physics.Awake)
            _broadphase.RegenerateContacts((projectile, physics, null, null));
    }

    /// <summary>Ends a round's flight where it is: it comes to rest and tells whatever waits for that.</summary>
    private void Finish(EntityUid projectile)
    {
        StopProjectile(projectile);
        RemCompDeferred<ProjectileFixedDistanceComponent>(projectile);

        var ev = new ProjectileFixedDistanceStopEvent();
        RaiseLocalEvent(projectile, ref ev);
    }

    public override void Update(float frameTime)
    {
        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<ProjectileFixedDistanceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // A round that was ended this tick, or is on its way out, has nothing left to stop.
            if (!comp.Running || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            if (time >= comp.FlyEndTime)
            {
                Finish(uid);
                continue;
            }

            if (_net.IsServer)
                SweepAhead((uid, comp), frameTime);
        }
    }
}
