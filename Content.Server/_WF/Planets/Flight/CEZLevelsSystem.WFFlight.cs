using System.Numerics;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Destructible;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>Atmospheric flight hooks: thruster lift over a planet, sinking without it, and touchdown costs.</summary>
public sealed partial class CEZLevelsSystem
{
    /// <summary>Crashed hulls stop paying hover power while grounded; an ascent command restores full demand.</summary>
    public bool WfWreckResting(EntityUid grid) =>
        HasComp<WFCrashImpactComponent>(grid) && WfHasSkidGround(grid) &&
        !HasComp<WFLiftoffComponent>(grid) && _pilotVerticalInput.GetValueOrDefault(grid) <= 0f;

    [Dependency] private SharedDestructibleSystem _wfDestructible = default!;
    [Dependency] private WFFlightSystem _wfFlight = default!;
    [Dependency] private ThrusterSystem _wfThrusters = default!;

    /// <summary>Lift ratio at or above which a hull flies normally; below it the hull is lift-lost.</summary>
    public const float WFFullLiftRatio = 1f;

    /// <summary>Lift ratio below which partial lift stops scaling the fall by (1 - ratio).</summary>
    public const float WFPartialLiftRatio = 0.5f;

    /// <summary>Fraction of the free-fall landing speed at or above which a touchdown is a crash.</summary>
    // Above the partial-lift band's landing speed (about 0.74 at 0.5 lift), so that band lands hard instead.
    public const float WFHardLandingFraction = 0.8f;

    // Matches the server tick CE's own fall runs on.
    private const float WFFreeFallStep = 1f / 60f;

    // Seconds; stops an unreachable gap from hanging the integration loop.
    private const float WFFreeFallCeiling = 600f;

    private static readonly TimeSpan WFOrbitRefusalCooldown = TimeSpan.FromSeconds(4);

    // m/s lost per anchored obstacle ploughed through.
    private const float WFPloughSpeedCost = 1.5f;

    private readonly Dictionary<EntityUid, TimeSpan> _wfNextOrbitRefusal = new();

    private readonly HashSet<EntityUid> _wfPloughed = new();

    private readonly Dictionary<(float Gravity, float Terminal), float> _wfFreeFallSpeeds = new();

    /// <summary>Surface gravity of the planet a grid is over; a transit gap uses the layer below it.</summary>
    public bool WfTryGetPlanetGravity(EntityUid grid, out float gravity)
    {
        gravity = 1f;

        if (Transform(grid).MapUid is not { } mapUid)
            return false;

        if (TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
        {
            gravity = layer.Gravity;
            return true;
        }

        if (!TryComp<CEZTransitMapComponent>(mapUid, out var transit))
            return false;

        var anchor = transit.LowerMap ?? transit.UpperMap;

        if (anchor is not { } anchorMap || !TryComp<WFPlanetLayerComponent>(anchorMap, out var anchorLayer))
            return false;

        gravity = anchorLayer.Gravity;
        return true;
    }

    /// <summary>True when this grid is flying over a planet, where thrusters replace the gravgen as lift.</summary>
    public bool WfIsPlanetFlight(EntityUid grid) => WfTryGetPlanetGravity(grid, out _);

    /// <summary>Current lift from one grid's landing thrusters, before dividing by planet gravity.</summary>
    public float WfGetLandingThrust(EntityUid grid)
    {
        var lift = 0f;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (TryComp<ThrusterComponent>(uid, out var thruster))
                lift += _wfThrusters.WfAtmosphericForce(uid, thruster) / ThrusterSystem.WfStandardGravity;
        }
        return lift;
    }

    /// <summary>Fraction of thrust left for manoeuvres after supporting the ship's weight.</summary>
    public float WfManeuveringFactor(EntityUid grid)
    {
        if (!_wfThrusters.WfInAtmosphere(grid) || !WfTryGetLiftRatio(grid, out var ratio))
            return 1f;
        return ratio <= 1f ? 0f : Math.Clamp(1f - 1f / ratio, 0f, 1f);
    }

    /// <summary>Hover power demand for the grid's rigid set, and whether it is short of power.</summary>
    public void WfGetAtmospherePower(EntityUid grid, out float demand, out bool deficit)
    {
        _wfThrusters.WfAtmospherePower(CollectRigidSet(grid), out demand, out deficit);
    }

    /// <summary>Pooled lift over pooled weight for a grid's whole rigid set; one is level flight.</summary>
    /// <param name="grid">Any member of the rigid body.</param>
    /// <param name="ratio">Lift over weight, or positive infinity for a weightless or force-anchored set.</param>
    public bool WfTryGetLiftRatio(EntityUid grid, out float ratio)
    {
        ratio = 0f;

        if (!WfTryGetPlanetGravity(grid, out var gravity) || gravity <= 0f)
            return false;

        var lift = 0f;
        var mass = 0f;

        foreach (var member in CollectRigidSet(grid))
        {
            if (HasComp<CEZMappingAnchorGridComponent>(member))
            {
                ratio = float.PositiveInfinity;
                return true;
            }

            lift += WfGetLandingThrust(member);

            if (_physQuery.TryComp(member, out var body))
                mass += body.FixturesMass;

            mass += GetWFVirtualMass(member);
        }

        ratio = mass <= 0f ? float.PositiveInfinity : lift / (mass * gravity);
        return true;
    }

    /// <summary>True when a gravgen's hull is over a planet, where the gravgen gives no lift.</summary>
    private bool WfGravgenIsOnPlanet(EntityUid gridUid)
    {
        return _mapGridQuery.HasComp(gridUid) && WfIsPlanetFlight(gridUid);
    }

    /// <summary>Adds landing-thruster lift, divided by planet gravity, to CE's pooled capacity.</summary>
    private void WfAddLandingThrusterCapacity(Dictionary<EntityUid, float> capacity)
    {
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            var grid = xform.ParentUid;
            if (!_mapGridQuery.HasComp(grid) || !WfTryGetPlanetGravity(grid, out var gravity) || gravity <= 0f)
                continue;
            capacity[grid] = capacity.GetValueOrDefault(grid)
                + _wfThrusters.WfAtmosphericForce(uid, thruster) / (ThrusterSystem.WfStandardGravity * gravity);
        }
    }

    /// <summary>Sink acceleration over a planet, scaled by partial lift; marks the set lift-lost.</summary>
    private float WfSinkGravity(EntityUid grid, HashSet<EntityUid> set, float gravity)
    {
        if (!WfTryGetLiftRatio(grid, out var ratio))
            return gravity;

        // A set falls as one, so every member takes the lead grid's state.
        foreach (var member in set)
        {
            _wfFlight.EnterLiftLost(member, ratio);
        }

        if (ratio >= WFFullLiftRatio)
            return gravity;

        return ratio >= WFPartialLiftRatio ? gravity * (1f - ratio) : gravity;
    }

    /// <summary>True when vertical input on an orbit layer is dropped; descent uses the console button.</summary>
    // Climb is refused too: CE's climb falls back to the gap below when there is none above.
    private bool WfRefusesOrbitInput(EntityUid grid, float input)
    {
        if (input == 0f || Transform(grid).MapUid is not { } mapUid || !WfIsOrbitLayer(mapUid))
            return false;

        // Only a descent has a button to point the pilot at.
        if (input < 0f && _timing.CurTime >= _wfNextOrbitRefusal.GetValueOrDefault(grid))
        {
            _wfNextOrbitRefusal[grid] = _timing.CurTime + WFOrbitRefusalCooldown;

            var pilots = EntityQueryEnumerator<PilotComponent>();
            while (pilots.MoveNext(out var pilotUid, out var pilot))
            {
                if (pilot.Console is { } console && !TerminatingOrDeleted(console) && Transform(console).GridUid == grid)
                    _popup.PopupEntity(Loc.GetString("wf-flight-descend-use-button"), console, pilotUid);
            }
        }

        return true;
    }

    /// <summary>True when landing thrusters give full lift; CE's gravity check only knows gravgens.</summary>
    private bool WfHasVerticalLift(EntityUid grid)
    {
        return WfTryGetLiftRatio(grid, out var ratio) && ratio >= WFFullLiftRatio;
    }

    /// <summary>A skid only damages a hull while its footprint touches planetary terrain.</summary>
    public bool WfHasSkidGround(EntityUid grid)
    {
        return _mapGridQuery.TryComp(grid, out var gridComp)
               && Transform(grid).MapUid is { } map
               && HasComp<WFPlanetLayerComponent>(map)
               && _mapGridQuery.HasComp(map)
               && HasGroundUnderFootprint((grid, gridComp), map);
    }

    /// <summary>Touchdown speed (levels/s) after falling one full gap; CE resets fall speed at each layer.</summary>
    public float WfGetFreeFallSpeed(CEZGridFallerComponent faller)
    {
        return WfGetFreeFallSpeed(faller.GridGravity, faller.GridTerminalVelocity);
    }

    /// <inheritdoc cref="WfGetFreeFallSpeed(CEZGridFallerComponent)"/>
    public float WfGetFreeFallSpeed(float gravity, float terminal)
    {
        if (gravity <= 0f || terminal <= 0f)
            return 0f;

        var key = (gravity, terminal);

        if (_wfFreeFallSpeeds.TryGetValue(key, out var cached))
            return cached;

        // Same integrator as the fall itself, over one level.
        var velocity = 0f;
        var fallen = 0f;

        for (var elapsed = 0f; fallen < 1f && elapsed < WFFreeFallCeiling; elapsed += WFFreeFallStep)
        {
            velocity = ApproachTerminal(velocity, gravity, terminal, WFFreeFallStep);
            fallen += velocity * WFFreeFallStep;
        }

        _wfFreeFallSpeeds[key] = velocity;
        return velocity;
    }

    /// <summary>Hard-lands a lift-lost hull arriving under the crash threshold instead of crashing it.</summary>
    public bool WfTryHardLanding(Entity<MapGridComponent, CEZGridFallerComponent> ent, float impact)
    {
        if (!HasComp<WFLiftLostComponent>(ent.Owner))
            return false;

        var reference = WfGetFreeFallSpeed(ent.Comp2);

        if (reference <= 0f || impact >= reference * WFHardLandingFraction)
            return false;

        _wfFlight.HardLanding((ent.Owner, ent.Comp1), impact / reference);
        WfRefreshOrbitParking(ent.Owner, Transform(ent.Owner).MapUid);
        return true;
    }

    /// <summary>Starts a skid after a lift-lost hull crashes with planar speed.</summary>
    public void WfSkidAfterCrash(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !_mapGridQuery.HasComp(grid) || !HasComp<WFLiftLostComponent>(grid))
            return;

        if (!_physQuery.TryComp(grid, out var body))
            return;

        var crashSpeed = body.LinearVelocity.Length();

        // NaN fails every comparison, so it is rejected explicitly.
        if (!float.IsFinite(crashSpeed) || crashSpeed <= WFFlightSystem.SkidStopSpeed)
            return;

        // No thud; the crash already made its sound.
        _wfFlight.BeginSkid(grid, thud: false);
    }

    /// <summary>Planetary ship crashes use structural breakup; detached terrain keeps the plain crash.</summary>
    public bool WfTryStructuralCrash(Entity<MapGridComponent, CEZGridFallerComponent> ent, float impact)
    {
        if (!WfHasSkidGround(ent.Owner)
            || HasComp<WFDetachedTerrainComponent>(ent.Owner))
            return false;
        var reference = WfGetFreeFallSpeed(ent.Comp2);
        _wfFlight.StructuralCrash((ent.Owner, ent.Comp1), reference > 0f ? impact / reference : 1f);
        return true;
    }

    /// <summary>Sliding wrecks shed momentum over distance; stopped ships retain the normal static ground grip.</summary>
    public float WfCrashSkidFriction(EntityUid grid)
    {
        return HasComp<WFSkidComponent>(grid) ? 0.0125f : 1f;
    }

    /// <summary>Re-arms a grid's ordinary z-gravity after it has been taken off an orbit layer by hand.</summary>
    public void WfRearmZGravity(EntityUid grid)
    {
        WfRefreshOrbitParking(grid, Transform(grid).MapUid);
    }

    /// <summary>A skidding hull breaks anchored obstacles it hits, losing speed, instead of bouncing.</summary>
    private bool WfPloughThroughWalls(EntityUid grid, PhysicsComponent body)
    {
        if (!TryComp<WFSkidComponent>(grid, out var skid) || !WfHasSkidGround(grid))
            return false;

        // Rate-limited to avoid a destruction sound per obstacle per tick; between bites the hull passes through.
        if (_timing.CurTime < skid.NextPlough)
            return true;

        skid.NextPlough = _timing.CurTime + WFFlightSystem.SkidBiteInterval;

        _wfPloughed.Clear();

        foreach (var contact in _wallContacts)
        {
            // Only obstacles CE found touching hull tiles, once each.
            var ent = contact.Wall;
            if (!TerminatingOrDeleted(ent) && _physQuery.TryComp(ent, out var obstacle) && obstacle.CanCollide && obstacle.Hard)
                _wfPloughed.Add(ent);
        }

        if (_wfPloughed.Count == 0)
            return true;

        foreach (var ent in _wfPloughed)
        {
            _wfFlight.GroundObstacleImpact(grid, _transform.GetWorldPosition(ent));
            _wfDestructible.BreakEntity(ent);

            if (!TerminatingOrDeleted(ent))
                QueueDel(ent);
        }

        var velocity = body.LinearVelocity;
        var speed = velocity.Length();
        var cost = MathF.Min(WFPloughSpeedCost * _wfPloughed.Count, speed * 0.03125f);

        // Guard non-finite speed so no NaN velocity reaches the hull.
        _physics.SetLinearVelocity(grid,
            !float.IsFinite(speed) || speed <= cost ? Vector2.Zero : velocity / speed * (speed - cost),
            body: body);

        return true;
    }
}
