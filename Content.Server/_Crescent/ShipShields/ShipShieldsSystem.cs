using Content.Server.Power.Components;
using Content.Server._Mono.FireControl;
using Content.Server.Station.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Audio.Systems;
using System.Numerics;


namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem : EntitySystem
{
    private const string ShipShieldPrototype = "ShipShield";

    //private const float DeflectionSpread = 25f;

    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly FixtureSystem _fixtureSystem = default!;
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsSys = default!;
    [Dependency] private readonly ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private readonly FireControlSystem _fireControl = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<ShipWeaponProjectileComponent> _shipWeaponProjectileQuery;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var power))
        {
            var interval = emitter.EmitterUpdateInterval > 0f ? emitter.EmitterUpdateInterval : 1.5f;

            emitter.Accumulator += frameTime;

            if (emitter.Accumulator < interval)
                continue;

            if (ShipShieldEmitterMath.CalculateAdditionalLoad(emitter) >= emitter.MaxDraw)
                emitter.Recharging = true;
            if (!power.Powered)
                emitter.Recharging = true;

            emitter.Accumulator -= interval;
            if (emitter.OverloadAccumulator > 0)
            {
                emitter.OverloadAccumulator -= interval;
            }

            float healed = emitter.HealPerSecond * interval;

            if (emitter.Recharging)
                healed *= emitter.UnpoweredBonus;

            if (emitter.HealScalesWithPowerReceived && power.Powered)
            {
                var ratio = Math.Clamp(power.PowerReceived / Math.Max(power.Load, 1f), 0f, 1f);
                healed *= ratio;
            }

            emitter.Damage -= healed;

            if (emitter.Damage < 0)
            {
                emitter.Damage = 0;
                if (power.Powered)
                    emitter.Recharging = false;
            }

            emitter.Damage += emitter.PassiveShieldDamagePerSecond * interval;

            AdjustEmitterLoad(uid, emitter, power);

            var parent = Transform(uid).GridUid;

            if (parent == null)
                continue;

            var filter = _station.GetInOwningStation(uid);

            if (emitter.Damage > emitter.DamageLimit)
            {
                var scale = 1f + emitter.OverloadPunishmentScale * (emitter.Damage - emitter.DamageLimit) / Math.Max(emitter.DamageLimit, 1f);
                var pun = emitter.DamageOverloadTimePunishment * scale;
                if (emitter.OverloadPunishmentMax > 0f)
                    pun = Math.Min(pun, emitter.OverloadPunishmentMax);
                emitter.OverloadAccumulator = pun;
            }

            if (!emitter.Recharging && emitter.Shield is null && emitter.OverloadAccumulator < 1)
            {
                var shield = ShieldEntity(parent.Value, uid);
                if (shield != EntityUid.Invalid)
                {
                    emitter.Shield = shield;
                    emitter.Shielded = parent.Value;
                }
                _audio.PlayGlobal(emitter.PowerUpSound, filter, true, emitter.PowerUpSound.Params);
            }
            else if ((emitter.Recharging || emitter.OverloadAccumulator > 0) && emitter.Shield is not null)
            {
                UnshieldEntity(parent.Value);
                emitter.Shield = null;
                emitter.Shielded = null;
                _audio.PlayGlobal(emitter.PowerDownSound, filter, true, emitter.PowerUpSound.Params);
            }

            // Forge-Change-Start
            // Push fresh shield state to any consoles on this grid so HP %/recharge timer stays current.
            _shuttleConsole.RefreshShuttleConsoles(parent.Value);
            _fireControl.RefreshConsolesOnGrid(parent.Value);
            // Forge-Change-End
        }
    }
    public override void Initialize()
    {
        base.Initialize();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _shipWeaponProjectileQuery = GetEntityQuery<ShipWeaponProjectileComponent>();

        SubscribeLocalEvent<ShipShieldComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentShutdown>(OnEmitterShutdown); // Mono

        InitializeCommands();
        InitializeEmitters();
    }

    private void OnPreventCollide(EntityUid uid, ShipShieldComponent component, ref PreventCollideEvent args)
    {
        // only handle ship weapons for now. engine update introduced physics regressions. Let's polish everything else and circle back yeah?
        // Ensuring projectiles coming froms same grid don't hit shield is handled by ProjectileGridPhaseComponent
        if (!_shipWeaponProjectileQuery.HasComponent(args.OtherEntity) ||
        !_projectileQuery.TryGetComponent(args.OtherEntity, out var projectile) ||
        projectile.ProjectileSpent)
        {
            args.Cancelled = true;
            return;
        }

        //if (TryComp<TimedDespawnComponent>(args.OtherEntity, out var despawn))
        //    despawn.Lifetime += despawn.Lifetime;

        // I originally tried reflection but the math is too hard with the fucked coordinate system in this game (WorldRotation can be negative. Vector to Angle conversion loses information. Etc etc.)
        // Might try again at some point using just vector math with this (https://math.stackexchange.com/questions/13261/how-to-get-a-reflection-vector)
        //var deflectionVector = Transform(args.OtherEntity).WorldPosition - Transform(uid).WorldPosition;
        //var angle = _random.NextFloat(DeflectionSpread);

        //if (_random.Prob(0.5f))
        //    angle = -angle;

        //deflectionVector = new Vector2((float) (Math.Cos(angle) * deflectionVector.X - Math.Sin(angle) * deflectionVector.Y), (float) (Math.Sin(angle) * deflectionVector.X - Math.Cos(angle) * deflectionVector.Y));

        // instead of reflecting the projectile, just delete it. this works better for gameplay and intuiting what is going on in a fight.
        // why shoot the projectile again when you can just 180 its physics, tho?
        //_gun.ShootProjectile(args.OtherEntity, deflectionVector, _physicsSystem.GetMapLinearVelocity(uid), uid, null, velocity.Length());

        if (component.Source is not { } source || !TryComp<ShipShieldEmitterComponent>(source, out var emitter))
            return;

        TryHandleShipWeaponShieldHit(uid, source, emitter, args.OtherEntity, projectile, ref args);
    }

    private void OnEmitterShutdown(EntityUid uid, ShipShieldEmitterComponent emitter, ComponentShutdown args) // Mono
    {
        var parent = Transform(uid).GridUid; // Forge-Change

        if (emitter.Shielded != null)
        {
            UnshieldEntity(emitter.Shielded.Value);
            emitter.Shield = null;
            emitter.Shielded = null;
        }

        // Forge-Change-Start
        // Refresh consoles so the shield HP bar disappears when the emitter is removed from the grid.
        if (parent != null)
        {
            _shuttleConsole.RefreshShuttleConsoles(parent.Value);
            _fireControl.RefreshConsolesOnGrid(parent.Value);
        }
        // Forge-Change-End
    }

    /// <summary>
    /// Produces a shield around a grid entity, if it doesn't already exist.
    /// </summary>
    /// <param name="entity">The entity being shielded.</param>
    /// <param name="mapGrid">The map grid component of the entity being shielded.</param>
    /// <param name="source">A shield generator or similar providing the shield for the entity</param>
    /// <returns>The shield entity.</returns>
    private EntityUid ShieldEntity(EntityUid entity, EntityUid? source = null, MapGridComponent? mapGrid = null)
    {
        if (TryComp<ShipShieldedComponent>(entity, out var existingShielded))
            return existingShielded.Shield;

        if (!Resolve(entity, ref mapGrid, false))
            return EntityUid.Invalid;

        var prototype = ShipShieldPrototype;

        var shield = Spawn(prototype, Transform(entity).Coordinates);
        var shieldPhysics = EnsureComp<PhysicsComponent>(shield);
        var shieldComp = EnsureComp<ShipShieldComponent>(shield);
        shieldComp.Shielded = entity;
        shieldComp.Source = source;

        // Copy shield color from the generator to the shield visuals
        var shieldVisuals = EnsureComp<ShipShieldVisualsComponent>(shield);
        if (source != null && TryComp<ShipShieldEmitterComponent>(source.Value, out var emitter))
        {
            shieldVisuals.ShieldColor = emitter.ShieldColor;
            Dirty(shield, shieldVisuals);
        }

        var gridCenter = new EntityCoordinates(entity, mapGrid.LocalAABB.Center);
        _transformSystem.SetCoordinates(shield, gridCenter);
        _transformSystem.SetWorldRotation(shield, _transformSystem.GetWorldRotation(entity));

        var chain = GenerateOvalFixture(shield, "shield", shieldPhysics, mapGrid, shieldVisuals.Padding);

        List<Vector2> roughPoly = new();

        var interval = chain.Count / PhysicsConstants.MaxPolygonVertices;

        int i = 0;

        while (i < PhysicsConstants.MaxPolygonVertices)
        {
            roughPoly.Add(chain.Vertices[i * interval]);
            i++;
        }

        var internalPoly = new PolygonShape();
        internalPoly.Set(roughPoly);

        _fixtureSystem.TryCreateFixture(shield, internalPoly, "internalShield",
            hard: true,
            collisionLayer: (int)CollisionGroup.BulletImpassable, // Mono - Only try to block bullets
            body: shieldPhysics);

        _physicsSystem.WakeBody(shield, body: shieldPhysics);
        _physicsSystem.SetSleepingAllowed(shield, shieldPhysics, false);

        _pvsSys.AddGlobalOverride(shield);

        var shieldedComp = EnsureComp<ShipShieldedComponent>(entity);
        shieldedComp.Shield = shield;
        shieldedComp.Source = source;

        return shield;
    }

    private bool UnshieldEntity(EntityUid uid, ShipShieldedComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        TryQueueDel(component.Shield);
        RemComp<ShipShieldedComponent>(uid);
        return true;
    }

    private ChainShape GenerateOvalFixture(EntityUid uid, string name, PhysicsComponent physics, MapGridComponent mapGrid, float padding)
    {
        float radius;
        float scale;
        var scaleX = true;

        var height = mapGrid.LocalAABB.Height + padding;
        var width = mapGrid.LocalAABB.Width + padding;

        if (width > height)
        {
            radius = 0.5f * height;
            scale = width / height;
        }
        else
        {
            radius = 0.5f * width;
            scale = height / width;
            scaleX = false;
        }

        var chain = new ChainShape();

        chain.CreateLoop(Vector2.Zero, radius);

        for (int i = 0; i < chain.Vertices.Length; i++)
        {
            if (scaleX)
            {
                chain.Vertices[i].X *= scale;
            }
            else
            {
                chain.Vertices[i].Y *= scale;
            }
        }

        _fixtureSystem.TryCreateFixture(uid, chain, name,
            hard: false,
            collisionLayer: (int)CollisionGroup.BulletImpassable, // Mono - Only blocks bullets
            body: physics);

        return chain;
    }

}
