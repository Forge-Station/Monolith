using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Falling;

/// <summary>Finding a landing, and what the fall does to the vehicle, its occupants and what lies under it.</summary>
public abstract partial class SharedVehicleFallSystem
{
    private static readonly ProtoId<DamageTypePrototype> FallDamageType = "Blunt";
    private static readonly EntProtoId DustEffect = "CEDustEffect";
    private static readonly TimeSpan MobKnockdown = TimeSpan.FromSeconds(3);

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private VehicleInteriorSystem _interior = default!;
    [Dependency] private GridVehicleMoverSystem _mover = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private VehicleWheelSystem _wheels = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixtureQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private readonly HashSet<EntityUid> _landingHits = new();
    private readonly List<EntityUid> _landedMobs = new();
    private readonly List<(EntityUid Target, VehicleSmashSettings Settings)> _landedSmashables = new();
    private readonly List<Vector2> _landingSamples = new();

    /// <summary>What a vehicle that comes down finds in its footprint.</summary>
    private enum LandingTarget : byte
    {
        /// <summary>Nothing it cares about: loose items, soft things, fixtures nothing collides with.</summary>
        Ignore,

        /// <summary>A mob, which is hit and knocked down.</summary>
        Mob,

        /// <summary>Something the vehicle smashes when it drives into it, which it smashes by landing on it.</summary>
        Smash,

        /// <summary>A wall or other hard thing that cannot be landed on.</summary>
        Block,
    }

    /// <summary>Where a vehicle comes down: the grid it ends up on and its pose in that grid's frame.</summary>
    private readonly record struct LandingSpot(EntityUid Grid, Vector2 LocalPosition, Angle LocalRotation, MapId MapId);

    /// <summary>Looks for room on the level below for the vehicle's footprint, at the matching position first, then near it.</summary>
    private bool TryFindLanding(
        EntityUid vehicle,
        FixturesComponent fixtures,
        PhysicsComponent body,
        VehicleWeightClass weight,
        EntityUid below,
        Vector2 centre,
        Angle worldRotation,
        out LandingSpot spot)
    {
        spot = default;
        LandingSearches++;

        if (!TryComp(below, out MapComponent? mapComp) ||
            !GridVehicleMoverSystem.TryGetFixtureAabb(fixtures, PhysicsTransform.Empty, out var local))
        {
            return false;
        }

        BuildSamples(local, _landingSamples);

        foreach (var offset in SearchOffsets)
        {
            var world = centre + offset;
            if (!_map.TryFindGridAt(mapComp.MapId, world, out var grid, out _))
                continue;

            var (gridPosition, gridRotation) = _transform.GetWorldPositionRotation(grid);
            var gridTx = new PhysicsTransform(gridPosition, gridRotation);
            var localRotation = SnapToCardinal(worldRotation - gridRotation);
            var localPosition = Vector2.Transform(world, _transform.GetInvWorldMatrix(grid));

            if (!IsLandingClear(vehicle, fixtures, body, weight, mapComp.MapId, gridTx, localPosition, localRotation, null, null))
                continue;

            spot = new LandingSpot(grid, localPosition, localRotation, mapComp.MapId);
            return true;
        }

        return false;
    }

    /// <summary>Whether a footprint at a pose has floor under all of it and nothing hard in it.</summary>
    private bool IsLandingClear(
        EntityUid vehicle,
        FixturesComponent fixtures,
        PhysicsComponent body,
        VehicleWeightClass weight,
        MapId mapId,
        PhysicsTransform gridTx,
        Vector2 localPosition,
        Angle localRotation,
        List<EntityUid>? mobs,
        List<(EntityUid Target, VehicleSmashSettings Settings)>? smashables)
    {
        var pose = new PhysicsTransform(localPosition, localRotation);

        foreach (var sample in _landingSamples)
        {
            if (!HasFloorAt(mapId, PhysicsTransform.Mul(gridTx, PhysicsTransform.Mul(pose, sample))))
                return false;
        }

        if (!GridVehicleMoverSystem.TryGetFixtureAabb(fixtures, pose, out var footprint))
            return false;

        var inset = footprint.Enlarged(-FootprintInset);
        if (inset.IsValid())
            footprint = inset;

        _landingHits.Clear();
        _lookup.GetEntitiesIntersecting(mapId, GridVehicleGeometry.ToWorldBounds(footprint, gridTx), _landingHits, LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var other in _landingHits)
        {
            switch (ClassifyLandingTarget(vehicle, fixtures, body, weight, other, gridTx, footprint, out var smash))
            {
                case LandingTarget.Block:
                    return false;
                case LandingTarget.Mob:
                    mobs?.Add(other);
                    break;
                case LandingTarget.Smash:
                    smashables?.Add((other, smash));
                    break;
            }
        }

        return true;
    }

    /// <summary>The mover's reading of what stands in a vehicle's way, applied to a footprint set down from above.</summary>
    private LandingTarget ClassifyLandingTarget(
        EntityUid vehicle,
        FixturesComponent vehicleFixtures,
        PhysicsComponent vehicleBody,
        VehicleWeightClass weight,
        EntityUid other,
        PhysicsTransform gridTx,
        Box2 footprint,
        out VehicleSmashSettings smash)
    {
        smash = default;

        // A grid is only its floor, which the support samples have answered for.
        if (other == vehicle || _gridQuery.HasComp(other))
            return LandingTarget.Ignore;

        var xform = Transform(other);
        if (!xform.Anchored && HasComp<ItemComponent>(other))
            return LandingTarget.Ignore;

        if (!_physicsQuery.TryComp(other, out var otherBody) ||
            !otherBody.CanCollide ||
            !_fixtureQuery.TryComp(other, out var otherFixtures))
        {
            return LandingTarget.Ignore;
        }

        var isMob = HasComp<MobStateComponent>(other);
        var isSmashable = _mover.TryGetSmashable(other, out smash);

        // Loose clutter that is not a mob, a vehicle or smashable is shoved aside by the solver, not landed on.
        if (!isMob &&
            !isSmashable &&
            !xform.Anchored &&
            otherBody.BodyType != BodyType.Static &&
            !HasComp<RMCVehicleComponent>(other))
        {
            return LandingTarget.Ignore;
        }

        var otherTx = PhysicsTransform.MulT(gridTx, _physics.GetPhysicsTransform(other, xform));
        if (!GridVehicleMoverSystem.TryGetFixtureAabb(otherFixtures, otherTx, out var otherAabb))
            return LandingTarget.Ignore;

        var overlap = footprint.Intersect(otherAabb);
        if (!(overlap.Width > 0f && overlap.Height > 0f))
            return LandingTarget.Ignore;

        if (isMob)
            return LandingTarget.Mob;

        if (isSmashable)
        {
            // A vehicle too light to smash it through cannot land on it either, and nor can one on a door that driving
            // at would be refused: a powered door a smash rule only breaks unpowered.
            if (smash.MinDestroyWeightClass is { } minimum && weight < minimum)
                return LandingTarget.Block;

            if (smash.RequiresDoorUnpowered && HasComp<DoorComponent>(other) &&
                !(_mover.TryGetDoorPowered(other, out var powered) && !powered))
            {
                return LandingTarget.Block;
            }

            return LandingTarget.Smash;
        }
        return VehicleHardCollision.IsHardCollidable((vehicle, vehicleFixtures, vehicleBody), (other, otherFixtures, otherBody))
            ? LandingTarget.Block
            : LandingTarget.Ignore;
    }

    /// <summary>Moves the vehicle to the level below and applies the landing.</summary>
    private void Drop(
        EntityUid vehicle,
        VehicleFallComponent fall,
        GridVehicleMoverComponent mover,
        FixturesComponent fixtures,
        PhysicsComponent body,
        TransformComponent xform,
        LandingSpot spot,
        EntityUid fromMap)
    {
        // Look at the landing while the vehicle is still on the old map, so that it cannot find its own parts there.
        var (gridPosition, gridRotation) = _transform.GetWorldPositionRotation(spot.Grid);
        _landedMobs.Clear();
        _landedSmashables.Clear();
        BuildSamples(GridVehicleMoverSystem.TryGetFixtureAabb(fixtures, PhysicsTransform.Empty, out var local) ? local : default, _landingSamples);
        IsLandingClear(
            vehicle,
            fixtures,
            body,
            mover.WeightClass,
            spot.MapId,
            new PhysicsTransform(gridPosition, gridRotation),
            spot.LocalPosition,
            spot.LocalRotation,
            _landedMobs,
            _landedSmashables);

        _transform.SetCoordinates(vehicle, xform, new EntityCoordinates(spot.Grid, spot.LocalPosition), spot.LocalRotation);

        // Whatever speed it had was spent in the landing; the driver can go on from a standstill.
        mover.CurrentSpeed = 0f;
        mover.IsMoving = false;
        mover.IsCommittedToMove = false;
        Dirty(vehicle, mover);

        _pending.Remove(vehicle);
        _retryAt.Remove(vehicle);

        var weight = mover.WeightClass;
        DamageHull(vehicle, fall, weight);

        // The damage may have wrecked it, and a vehicle being deleted has no landing left to take.
        if (TerminatingOrDeleted(vehicle))
            return;

        // The strongest jolt the cabin knows: a fall is not a bump.
        _interior.DoInteriorCrashEffect(vehicle, mover.MaxSpeed, mover.MaxSpeed);

        foreach (var mob in _landedMobs)
        {
            HitMob(vehicle, mob, fall.MobDamage ?? GetMobDamage(weight));
        }

        foreach (var (target, settings) in _landedSmashables)
        {
            CrushLandingTarget(vehicle, target, settings);
        }

        _landedMobs.Clear();
        _landedSmashables.Clear();

        var coordinates = Transform(vehicle).Coordinates;
        _popup.PopupEntity(Loc.GetString("wf-vehicles-fall-landed", ("vehicle", vehicle)), vehicle, PopupType.MediumCaution);

        if (_prototype.HasIndex(DustEffect))
            SpawnAtPosition(DustEffect, coordinates);

        var landed = new VehicleFellEvent(fromMap, Transform(vehicle).MapUid ?? fromMap);
        RaiseLocalEvent(vehicle, ref landed);
    }

    /// <summary>The frame loses a share of its integrity and every wheel a flat amount, by weight class.</summary>
    private void DamageHull(EntityUid vehicle, VehicleFallComponent fall, VehicleWeightClass weight)
    {
        var fraction = fall.FrameDamageFraction ?? weight switch
        {
            VehicleWeightClass.Weak => 0.10f,
            VehicleWeightClass.Light => 0.15f,
            VehicleWeightClass.Medium => 0.20f,
            _ => 0.25f,
        };

        if (TryComp(vehicle, out HardpointIntegrityComponent? frame))
        {
            _hardpoints.DamageHardpoint(vehicle, vehicle, frame.MaxIntegrity * fraction, frame);
        }
        else if (HasComp<DamageableComponent>(vehicle))
        {
            // No frame to hurt: the body takes it, whole, as a fall is not turned aside by armour.
            var damage = new DamageSpecifier { DamageDict = { [FallDamageType] = fall.BodyHealth * fraction } };
            _damageable.TryChangeDamage(vehicle, damage, ignoreResistances: true);
        }

        var wheels = fall.WheelDamage ?? weight switch
        {
            VehicleWeightClass.Weak => 50f,
            VehicleWeightClass.Light => 75f,
            VehicleWeightClass.Medium => 100f,
            _ => 125f,
        };

        _wheels.DamageWheels(vehicle, wheels);
    }

    private static float GetMobDamage(VehicleWeightClass weight)
    {
        return weight switch
        {
            VehicleWeightClass.Weak => 25f,
            VehicleWeightClass.Light => 35f,
            VehicleWeightClass.Medium => 50f,
            _ => 70f,
        };
    }

    /// <summary>A mob under the landing is run over: hurt, knocked down and held clear of the hull as the mover does it.</summary>
    private void HitMob(EntityUid vehicle, EntityUid mob, float damage)
    {
        if (TerminatingOrDeleted(mob) || _mobState.IsDead(mob))
            return;

        _damageable.TryChangeDamage(mob, new DamageSpecifier { DamageDict = { [FallDamageType] = damage } }, origin: vehicle);
        _stun.TryKnockdown(mob, MobKnockdown, true);

        var runover = EnsureComp<VehicleRunoverComponent>(mob);
        runover.Vehicle = vehicle;
        runover.Duration = MobKnockdown;
        runover.ExpiresAt = _timing.CurTime + runover.Duration + VehicleRunoverSystem.StandUpGrace;
        Dirty(mob, runover);

        if (_physicsQuery.TryComp(mob, out var mobBody))
        {
            _physics.SetLinearVelocity(mob, Vector2.Zero, body: mobBody);
            _physics.SetAngularVelocity(mob, 0f, body: mobBody);
        }
    }

    /// <summary>Smashes a thing the vehicle lands on, by the mover's own hit.</summary>
    private void CrushLandingTarget(EntityUid vehicle, EntityUid target, VehicleSmashSettings settings)
    {
        if (TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target))
            return;

        if (settings.SmashSound != null)
            _audio.PlayPvs(settings.SmashSound, Transform(target).Coordinates);

        _mover.SmashTarget(target, vehicle, settings);
    }
}
