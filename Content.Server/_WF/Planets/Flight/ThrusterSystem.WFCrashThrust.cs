using System.Numerics;
using System.Linq;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem
{
    [Dependency] private SharedPhysicsSystem _wfCrashPhysics = default!;
    [Dependency] private SharedTransformSystem _wfCrashTransform = default!;

    /// <summary>Remembers which linear engines were firing at impact, for one second.</summary>
    public void WfCaptureCrashThrust(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
            if (TryComp<ThrusterComponent>(uid, out var engine) &&
                engine.Type == ThrusterType.Linear && engine.Enabled && engine.IsOn && engine.Firing)
                EnsureComp<WFCrashThrustComponent>(uid).ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(1);
    }

    /// <summary>Rebinds engines after a breakup; engines cut off from every console keep their last command.</summary>
    public void WfDetachCrashThrust(EntityUid original, EntityUid[] fragments)
    {
        var pieces = new HashSet<EntityUid>(fragments) { original };
        var consoles = WfConsoleGrids();
        // Engines moved to fragments must leave the original's banks so it can't steer the wreckage.
        if (TryComp<ShuttleComponent>(original, out var oldShuttle))
        {
            for (var direction = 0; direction < oldShuttle.LinearThrusters.Length; direction++)
            {
                var bank = oldShuttle.LinearThrusters[direction];
                foreach (var uid in bank.ToArray())
                    if (Transform(uid).GridUid != original && TryComp<ThrusterComponent>(uid, out var engine))
                    {
                        bank.Remove(uid);
                        oldShuttle.LinearThrust[direction] -= engine.Thrust;
                        oldShuttle.BaseLinearThrust[direction] -= engine.BaseThrust;
                    }
                var force = 0f;
                var centre = Vector2.Zero;
                foreach (var uid in bank)
                    if (TryComp<ThrusterComponent>(uid, out var engine))
                    {
                        force += engine.Thrust;
                        centre += Transform(uid).LocalPosition * engine.Thrust;
                    }
                oldShuttle.CenterOfThrust[direction] = force > 0f ? centre / force : Vector2.Zero;
            }
            foreach (var uid in oldShuttle.AngularThrusters.ToArray())
                if (Transform(uid).GridUid != original && TryComp<ThrusterComponent>(uid, out var engine))
                {
                    oldShuttle.AngularThrusters.Remove(uid);
                    oldShuttle.AngularThrust -= engine.Thrust;
                }
        }
        foreach (var grid in fragments)
        {
            if (!consoles.Contains(grid) || HasComp<ShuttleComponent>(grid))
                continue;
            EnsureComp<ShuttleComponent>(grid);
            var children = Transform(grid).ChildEnumerator;
            while (children.MoveNext(out var uid))
                if (TryComp<ThrusterComponent>(uid, out var engine))
                {
                    engine.IsOn = false; // No bank existed on this fragment before now.
                    if (CanEnable(uid, engine)) EnableThruster(uid, engine);
                }
        }
        var query = EntityQueryEnumerator<WFCrashThrustComponent>();
        while (query.MoveNext(out var uid, out var command))
            if (Transform(uid).GridUid is { } grid && pieces.Contains(grid) && !consoles.Contains(grid))
                command.Detached = true;
    }

    private HashSet<EntityUid> WfConsoleGrids()
    {
        var grids = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
            if (xform.Anchored && xform.GridUid is { } grid)
                grids.Add(grid);
        return grids;
    }

    private void WfUpdateCrashThrust()
    {
        var commands = EntityQueryEnumerator<WFCrashThrustComponent, ThrusterComponent>();
        var forces = new Dictionary<EntityUid, Vector2>();
        HashSet<EntityUid>? consoles = null;
        while (commands.MoveNext(out var uid, out var command, out var engine))
        {
            if (!command.Detached)
            {
                if (_timing.CurTime >= command.ExpiresAt)
                    RemCompDeferred<WFCrashThrustComponent>(uid);
                continue;
            }
            consoles ??= WfConsoleGrids();
            var xform = Transform(uid);
            if (!engine.Enabled || !xform.Anchored || xform.GridUid is not { } grid || consoles.Contains(grid))
            {
                engine.Firing = false;
                _appearance.SetData(uid, ThrusterVisualState.Thrusting, false);
                RemCompDeferred<WFCrashThrustComponent>(uid);
                continue;
            }
            // Normal power/disable/nozzle checks remain authoritative. A blackout retains the command, not force.
            var firing = engine.IsOn && CanEnable(uid, engine);
            engine.Firing = firing;
            _appearance.SetData(uid, ThrusterVisualState.State, firing);
            _appearance.SetData(uid, ThrusterVisualState.Thrusting, firing);
            if (!firing || !float.IsFinite(engine.Thrust))
                continue;
            var force = _wfCrashTransform.GetWorldRotation(uid).ToWorldVec() * engine.Thrust;
            forces[grid] = forces.GetValueOrDefault(grid) + force;
        }
        foreach (var (grid, requested) in forces)
        {
            if (!_wfFlightLevels.WfHasSkidGround(grid) ||
                !TryComp<PhysicsComponent>(grid, out var body) || body.BodyType != BodyType.Dynamic ||
                !float.IsFinite(body.LinearVelocity.LengthSquared()) || !float.IsFinite(requested.LengthSquared()))
                continue;
            var magnitude = requested.Length();
            if (magnitude < 0.01f || body.Mass <= 0f) continue;
            var heading = requested / magnitude;
            // Wrecks crawl and skid; never accelerate tiny fragments into streaming-speed projectiles.
            if (Vector2.Dot(body.LinearVelocity, heading) < 12f)
                _wfCrashPhysics.ApplyForce(grid, heading * Math.Min(magnitude, body.Mass * 2f), body: body);
            EnsureComp<WFSkidComponent>(grid).Debris = true;
        }
    }
}
