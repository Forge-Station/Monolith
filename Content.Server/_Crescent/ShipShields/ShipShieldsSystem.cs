using Content.Server.Power.Components;
// Forge-Change: dropped Content.Server._Mono.FireControl + Content.Server.Shuttles.Systems imports;
// shield state replicates via the networked ShipShieldEmitterComponent instead of console refreshes.
using Content.Server.Station.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
// Forge-Change: Robust.Server.GameStates dropped along with PvsOverrideSystem.
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Timing; // Forge-Change
using System.Numerics;
using Content.Server._Crescent.ShipShields.Components;


namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem : EntitySystem
{
    private const string ShipShieldPrototype = "ShipShield";

    //private const float DeflectionSpread = 25f;

    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private FixtureSystem _fixtureSystem = default!;
    [Dependency] private PhysicsSystem _physicsSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsSys = default!;
    [Dependency] private StationSystem _station = default!; // Forge-Change
    [Dependency] private SharedAudioSystem _audio = default!; // Forge-Change
    [Dependency] private IGameTiming _timing = default!; // Forge-Change

    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<ShipWeaponProjectileComponent> _shipWeaponProjectileQuery;
    private EntityQuery<TransformComponent> _xformQuery; // Forge-Change
    private EntityQuery<ShipShieldEmitterComponent> _emitterQuery; // Forge-Change

    // Forge-Change-Start: per-emitter snapshot of last replicated state to gate Dirty calls.
    private const float HpDirtyThreshold = 0.02f;
    private static readonly TimeSpan RechargeDirtyThreshold = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<EntityUid, EmitterNetSnapshot> _lastNet = new();

    private struct EmitterNetSnapshot
    {
        public float HpPct;
        public bool Online;
        public bool Recharging;
        public TimeSpan? RechargeEndTime;
    }
    // Forge-Change-End
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
            }
            else if ((emitter.Recharging || emitter.OverloadAccumulator > 0) && emitter.Shield is not null || HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid))
            {
                UnshieldEntity(parent.Value);
                emitter.Shield = null;
                emitter.Shielded = null;
                if (!HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid))
                    _audio.PlayGlobal(emitter.PowerDownSound, filter, true, emitter.PowerUpSound.Params);
            }

            // Forge-Change-Start
            // Replicate emitter state via the networked component instead of pushing a full BUI refresh
            // to every shuttle/fire-control console on the grid each tick.
            var gridUid = Transform(uid).GridUid;
            UpdateReplicatedEmitterState(uid, emitter, gridUid);
            // Forge-Change-End
        }
    }

    // Forge-Change-Start: compute Online/RechargeEndTime/HP and Dirty only when something meaningfully changed.
    private void UpdateReplicatedEmitterState(EntityUid uid, ShipShieldEmitterComponent emitter, EntityUid? gridUid)
    {
        var limit = emitter.DamageLimit > 0f ? emitter.DamageLimit : 1f;
        var hpPct = Math.Clamp(1f - emitter.Damage / limit, 0f, 1f);
        var online = emitter.Shield != null;

        TimeSpan? endTime = null;
        if (emitter.OverloadAccumulator > 0f)
            endTime = _timing.CurTime + TimeSpan.FromSeconds(emitter.OverloadAccumulator);
        else if (emitter.Recharging && emitter.HealPerSecond > 0f)
        {
            var heal = emitter.HealPerSecond * emitter.UnpoweredBonus;
            if (heal > 0f)
                endTime = _timing.CurTime + TimeSpan.FromSeconds(emitter.Damage / heal);
        }

        if (!_lastNet.TryGetValue(uid, out var last))
        {
            last = new EmitterNetSnapshot { HpPct = float.NaN };
        }

        var hpChanged = float.IsNaN(last.HpPct) || MathF.Abs(hpPct - last.HpPct) > HpDirtyThreshold
            || (hpPct == 0f && last.HpPct != 0f) || (hpPct == 1f && last.HpPct != 1f);
        var onlineChanged = online != last.Online;
        var rechargingChanged = emitter.Recharging != last.Recharging;
        var endTimeChanged = !NullableTimeSpanCloseEnough(endTime, last.RechargeEndTime);

        if (!hpChanged && !onlineChanged && !rechargingChanged && !endTimeChanged
            && emitter.Online == online && emitter.RechargeEndTime == endTime)
        {
            // Forge-Change: still mirror to the grid — clients read grid state without PVS on the emitter.
            if (gridUid != null)
                SyncGridShieldState(gridUid.Value, emitter, online, endTime);
            return;
        }

        emitter.Online = online;
        emitter.RechargeEndTime = endTime;
        Dirty(uid, emitter);

        _lastNet[uid] = new EmitterNetSnapshot
        {
            HpPct = hpPct,
            Online = online,
            Recharging = emitter.Recharging,
            RechargeEndTime = endTime,
        };

        if (gridUid != null)
            SyncGridShieldState(gridUid.Value, emitter, online, endTime);
    }

    /// <summary>
    /// Mirrors emitter state onto the grid so clients can render HUD/radar without PVS on the emitter or bubble.
    /// </summary>
    private void SyncGridShieldState(
        EntityUid gridUid,
        ShipShieldEmitterComponent emitter,
        bool online,
        TimeSpan? rechargeEndTime)
    {
        var gridState = EnsureComp<ShipShieldGridStateComponent>(gridUid);

        var padding = 50f;
        if (emitter.Shield is { } shield && TryComp<ShipShieldVisualsComponent>(shield, out var visuals))
            padding = visuals.Padding;

        var changed = !gridState.HasEmitter
                      || gridState.Damage != emitter.Damage
                      || gridState.DamageLimit != emitter.DamageLimit
                      || gridState.Recharging != emitter.Recharging
                      || gridState.Online != online
                      || gridState.RechargeEndTime != rechargeEndTime
                      || gridState.ShieldColor != emitter.ShieldColor
                      || MathF.Abs(gridState.Padding - padding) > 0.01f;

        if (!changed)
            return;

        gridState.HasEmitter = true;
        gridState.Damage = emitter.Damage;
        gridState.DamageLimit = emitter.DamageLimit;
        gridState.Recharging = emitter.Recharging;
        gridState.Online = online;
        gridState.RechargeEndTime = rechargeEndTime;
        gridState.ShieldColor = emitter.ShieldColor;
        gridState.Padding = padding;
        Dirty(gridUid, gridState);
    }

    private void SyncGridShieldState(EntityUid gridUid, ShipShieldEmitterComponent emitter)
    {
        SyncGridShieldState(gridUid, emitter, emitter.Online, emitter.RechargeEndTime);
    }

    private void RefreshGridShieldState(EntityUid gridUid)
    {
        if (TryGetCanonicalEmitter(gridUid, out _, out var emitter))
        {
            SyncGridShieldState(gridUid, emitter);
            return;
        }

        if (!TryComp<ShipShieldGridStateComponent>(gridUid, out var gridState) || !gridState.HasEmitter)
            return;

        gridState.HasEmitter = false;
        Dirty(gridUid, gridState);
    }

    private bool TryGetCanonicalEmitter(EntityUid gridUid, out EntityUid emitterUid, out ShipShieldEmitterComponent emitter)
    {
        emitterUid = default;
        emitter = default!;

        if (TryComp<ShipShieldedComponent>(gridUid, out var shielded)
            && shielded.Source is { } src
            && _emitterQuery.TryGetComponent(src, out var canonical))
        {
            emitterUid = src;
            emitter = canonical;
            return true;
        }

        EntityUid? best = null;
        ShipShieldEmitterComponent? bestEmitter = null;
        var query = AllEntityQuery<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var ec, out var xform))
        {
            if (xform.GridUid != gridUid || !xform.Anchored)
                continue;

            if (best == null || uid.CompareTo(best.Value) < 0)
            {
                best = uid;
                bestEmitter = ec;
            }
        }

        if (best == null || bestEmitter == null)
            return false;

        emitterUid = best.Value;
        emitter = bestEmitter;
        return true;
    }

    private static bool NullableTimeSpanCloseEnough(TimeSpan? a, TimeSpan? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        var diff = a.Value - b.Value;
        if (diff.Ticks < 0)
            diff = -diff;
        return diff < RechargeDirtyThreshold;
    }
    // Forge-Change-End
    public override void Initialize()
    {
        base.Initialize();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _shipWeaponProjectileQuery = GetEntityQuery<ShipWeaponProjectileComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>(); // Forge-Change
        _emitterQuery = GetEntityQuery<ShipShieldEmitterComponent>(); // Forge-Change

        SubscribeLocalEvent<ShipShieldComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentShutdown>(OnEmitterShutdown); // Mono
        // Forge-Change: keep grid snapshot in sync when emitters are added or re-anchored.
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentStartup>(OnEmitterStartup);
        SubscribeLocalEvent<ShipShieldEmitterComponent, AnchorStateChangedEvent>(OnEmitterAnchorChanged);

        InitializeCommands();
        InitializeEmitters();
    }

    // Forge-Change-Start
    private void OnEmitterStartup(EntityUid uid, ShipShieldEmitterComponent _, ComponentStartup args)
    {
        var gridUid = Transform(uid).GridUid;
        if (gridUid != null)
            RefreshGridShieldState(gridUid.Value);
    }

    private void OnEmitterAnchorChanged(EntityUid uid, ShipShieldEmitterComponent _, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            return;

        var gridUid = Transform(uid).GridUid;
        if (gridUid != null)
            RefreshGridShieldState(gridUid.Value);
    }
    // Forge-Change-End

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
        if (emitter.Shielded != null)
        {
            UnshieldEntity(emitter.Shielded.Value);
            emitter.Shield = null;
            emitter.Shielded = null;
        }

        // Forge-Change: drop replicated-state snapshot; component removal already propagates to clients.
        _lastNet.Remove(uid);

        var gridUid = Transform(uid).GridUid;
        if (gridUid != null)
            RefreshGridShieldState(gridUid.Value);
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

        if (!Resolve(entity, ref mapGrid, false) || HasComp<ShipShieldDisabledGridComponent>(Transform(entity).GridUid))
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

        // Forge-Change: removed _pvsSys.AddGlobalOverride(shield); the bubble is a regular grid-anchored
        // entity and PVS already streams it to nearby clients. Distant clients do not need its physics fixtures.

        var shieldedComp = EnsureComp<ShipShieldedComponent>(entity);
        shieldedComp.Shield = shield;
        shieldedComp.Source = source;

        // Forge-Change: push shield outline/HUD state to the grid immediately when the bubble spawns.
        if (source != null && TryComp<ShipShieldEmitterComponent>(source.Value, out var srcEmitter))
            SyncGridShieldState(entity, srcEmitter, online: true, srcEmitter.RechargeEndTime);

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
