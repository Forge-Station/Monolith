using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.TractorBeam;

public sealed partial class TractorBeamSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly HashSet<EntityUid> _coneEntities = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _collectionTargets = new();
    private readonly List<(EntityUid Target, Vector2 Force, Vector2 SourceOffset)> _pullRequests = new();

    /// <summary>Uses the target fan, limited by the dish's forward operating sector.</summary>
    private void FindCollectionTargets(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        var source = beam.SourceGrid!.Value;
        var target = beam.Target!.Value;
        var start = TransformSystem.GetWorldPosition(uid);
        var end = TransformSystem.ToMapCoordinates(new Robust.Shared.Map.EntityCoordinates(target, beam.TargetOffset)).Position;
        var width = GetBeamHalfWidth(target, start, end);
        var bounds = new Box2(Vector2.Min(start, end) - new Vector2(width), Vector2.Max(start, end) + new Vector2(width));
        var map = Transform(uid).MapID;
        _coneEntities.Clear();
        _lookup.GetEntitiesIntersecting(map, bounds, _coneEntities, LookupFlags.Dynamic);

        // Grids have their own broadphase trees and are not returned as ordinary loose entities.
        var grids = EntityQueryEnumerator<MapGridComponent, PhysicsComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out _, out _, out var gridTransform))
        {
            if (gridTransform.MapID == map && grid != source && grid != target && IsMovableGrid(grid) &&
                TractorBeamGeometry.IntersectsBox(start, end, _lookup.GetWorldAABB(grid, gridTransform), width))
                _coneEntities.Add(grid);
        }

        var targets = new List<EntityUid>();
        foreach (var entity in _coneEntities)
        {
            if (entity == source || entity == target || TerminatingOrDeleted(entity) || Paused(entity) ||
                !TryComp<PhysicsComponent>(entity, out var body) ||
                body.BodyType is not (BodyType.Dynamic or BodyType.KinematicController) ||
                !body.CanCollide || !float.IsFinite(body.Mass) || body.Mass <= 0)
                continue;

            var xform = Transform(entity);
            if (xform.MapID != map || xform.Anchored || !InOperatingCone(uid, beam, Center(entity, body)))
                continue;

            if (HasComp<MapGridComponent>(entity))
            {
                if (!IsMovableGrid(entity) || SameDockedGroup(source, entity) ||
                    !TractorBeamGeometry.IntersectsBox(start, end, _lookup.GetWorldAABB(entity, xform), width))
                    continue;
            }
            else
            {
                // Only free-floating objects: never pull cargo through a hull, objects from hands,
                // or items out of containers. Their containing grid is the physical target instead.
                if (xform.GridUid != null || xform.ParentUid != xform.MapUid ||
                    !TractorBeamGeometry.ContainsPoint(start, end, Center(entity, body), width))
                    continue;
            }

            targets.Add(entity);
            if (_predictedCenters.TryAdd(entity, Center(entity, body)))
                _bodies.Add(entity);
        }
        _collectionTargets[uid] = targets;
    }

    private float MinimumGridSeparation(EntityUid source, EntityUid target, float clearance)
    {
        return GridRadius(source) + GridRadius(target) + MathF.Max(0, clearance);
    }

    private float GridRadius(EntityUid grid)
    {
        var bounds = _lookup.GetWorldAABB(grid);
        var center = Center(grid);
        // A radius enclosing the hull about its mass center avoids commanding hulls through each other.
        var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft - center), Vector2.Abs(bounds.TopRight - center));
        return extent.Length();
    }

    private void ApplyBeamForces(EntityUid uid, TractorBeamEmitterComponent beam, float dt, float elapsed)
    {
        var source = beam.SourceGrid!.Value;
        var target = beam.Target!.Value;
        var sourceBody = Comp<PhysicsComponent>(source);
        var targetBody = Comp<PhysicsComponent>(target);
        if (!PhysicsSystem.WakeBody(source, body: sourceBody))
            return;

        _pullRequests.Clear();
        var reducedMass = 1f / (1f / sourceBody.Mass + 1f / targetBody.Mass);
        var separationToTarget = _predictedCenters[target] - _predictedCenters[source];
        var relativeVelocity = PredictedVelocity(target, targetBody, elapsed) - PredictedVelocity(source, sourceBody, elapsed);
        var sourceAngularVelocity = PredictedAngularVelocity(source, sourceBody, elapsed);
        // The target's desired velocity includes the tangential motion of the source's arm.
        relativeVelocity -= new Vector2(-separationToTarget.Y, separationToTarget.X) * sourceAngularVelocity;
        var lateralMass = 1f / (sourceBody.InvMass + targetBody.InvMass +
            separationToTarget.LengthSquared() * sourceBody.InvI);
        var direction = new Angle(_predictedAngles[source] - beam.HoldSourceAngle!.Value).RotateVec(beam.HoldDirection);
        var torqueArm = MathF.Max(1f, GridRadius(target));
        var torque = 0f;
        Vector2 force;
        if (beam.LockedInPlace)
        {
            var heldSeparation = new Angle(_predictedAngles[source] - beam.LockedSourceAngle!.Value)
                .RotateVec(beam.LockedSeparation);
            var error = separationToTarget - heldSeparation;
            // Restrain the relative pose: common translation is free, while real engines
            // must counter the pair's shared momentum and the arm's reaction torque.
            var sourceInvMass = sourceBody.InvMass;
            var sourceInvI = sourceBody.InvI;
            if (_brakingSources.Contains(source) && TryComp<ShuttleComponent>(source, out var shuttle))
            {
                // Only treat a source axis as supported after checking its actual engines
                // can absorb the full proposed reaction. Manual steering stays unconstrained.
                var candidate = new Vector2(
                    TractorBeamPhysics.CalculateLockForce(error.X, relativeVelocity.X, targetBody.Mass, dt),
                    TractorBeamPhysics.CalculateLockForce(error.Y, relativeVelocity.Y, targetBody.Mass, dt));
                var angularError = _predictedAngles[target] - _predictedAngles[source] - beam.LockedAngle;
                angularError = MathF.Atan2(MathF.Sin(angularError), MathF.Cos(angularError));
                var candidateTorque = targetBody.InvI > 0 ? TractorBeamPhysics.CalculateLockForce(angularError,
                    PredictedAngularVelocity(target, targetBody, elapsed) - sourceAngularVelocity, 1f / targetBody.InvI, dt) : 0;
                var reactionTorque = separationToTarget.X * candidate.Y - separationToTarget.Y * candidate.X + candidateTorque;
                var support = _mover.PredictTractorBraking(source, shuttle, sourceBody, elapsed, candidate * dt, reactionTorque * dt);
                if (support.LinearHeld)
                    sourceInvMass = 0;
                if (support.AngularHeld)
                    sourceInvI = 0;
            }
            var desiredAcceleration = new Vector2(
                TractorBeamPhysics.CalculateLockForce(error.X, relativeVelocity.X, 1f, dt),
                TractorBeamPhysics.CalculateLockForce(error.Y, relativeVelocity.Y, 1f, dt));
            var angleError = _predictedAngles[target] - _predictedAngles[source] - beam.LockedAngle;
            angleError = MathF.Atan2(MathF.Sin(angleError), MathF.Cos(angleError));
            var angularVelocity = PredictedAngularVelocity(target, targetBody, elapsed) - sourceAngularVelocity;
            var desiredAngularAcceleration = TractorBeamPhysics.CalculateLockForce(angleError, angularVelocity, 1f, dt);
            (force, torque) = TractorBeamPhysics.CalculateArmLock(desiredAcceleration, desiredAngularAcceleration,
                separationToTarget, sourceInvMass + targetBody.InvMass, sourceInvI, targetBody.InvI);
        }
        else
        {
            force = TractorBeamPhysics.CalculateForce(separationToTarget, relativeVelocity,
                beam.HoldDistance, reducedMass, beam.Frequency, beam.DampingRatio, beam.MaxForce, dt, direction, lateralMass);
            var inverseInertia = sourceBody.InvI + targetBody.InvI;
            if (inverseInertia > 0)
            {
                var angleError = _predictedAngles[target] - _predictedAngles[source] - beam.HoldAngle!.Value;
                var relativeAngularVelocity = PredictedAngularVelocity(target, targetBody, elapsed) - sourceAngularVelocity;
                torque = TractorBeamPhysics.CalculateTorque(angleError, relativeAngularVelocity, 1f / inverseInertia,
                    beam.Frequency, beam.DampingRatio, beam.MaxForce * torqueArm, dt);
            }
        }
        if (PhysicsSystem.WakeBody(target, body: targetBody))
            _pullRequests.Add((target, force, Center(target, targetBody) - Center(source, sourceBody)));

        var dish = TransformSystem.GetWorldPosition(uid) + _predictedCenters[source] - Center(source, sourceBody);
        foreach (var entity in _collectionTargets[uid])
        {
            var body = Comp<PhysicsComponent>(entity);
            if (!PhysicsSystem.WakeBody(entity, body: body))
                continue;

            var separation = _predictedCenters[entity] - dish;
            var distance = separation.Length();
            if (!float.IsFinite(distance) || distance <= 0.001f)
                continue;

            // Secondary captures deliberately keep accelerating into the dish. They have no
            // speed governor or arrival brake: using heavy debris against an arrestor is possible.
            // Apply recoil at the dish, including its lever arm, to conserve angular momentum.
            var collectionDirection = separation / distance;
            var acceleration = float.IsFinite(beam.CollectionAcceleration) ? MathF.Max(0, beam.CollectionAcceleration) : 0f;
            if (!HasComp<MapGridComponent>(entity))
                acceleration *= float.IsFinite(beam.LooseCollectionMultiplier) ? MathF.Max(0, beam.LooseCollectionMultiplier) : 0f;
            var mass = 1f / (1f / sourceBody.Mass + 1f / body.Mass);
            _pullRequests.Add((entity, collectionDirection * MathF.Min(beam.MaxForce, mass * acceleration),
                dish - _predictedCenters[source]));
        }

        // A dish has ONE budget. Extra bodies share its force and power instead of receiving
        // a fresh maximum-force beam each. Sum magnitudes, so opposing loads cannot cancel cost.
        // Turning a captured hull costs force at its hull radius. Translation, rotation and
        // debris all consume the same dish budget, including stationary thrust/gyro resistance.
        var totalForce = MathF.Abs(torque) / torqueArm;
        foreach (var request in _pullRequests)
            totalForce += request.Force.Length();
        var demand = MathF.Min(totalForce, beam.MaxForce);
        beam.RequiredForce = MathF.Max(beam.RequiredForce, demand);
        var requestedPower = TractorBeamPhysics.CalculatePower(demand, beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
        var power = Comp<PowerConsumerComponent>(uid);
        // Pay for extending the field before buying mechanical authority. Distance consumes
        // electricity, never a fictitious force or recoil; all bodies still share this budget.
        var maintenancePower = TractorBeamPhysics.CalculatePower(0, beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
        var fraction = TractorBeamPhysics.AvailableForceFraction(power.ReceivedPower, maintenancePower, requestedPower);
        var scale = totalForce > 0 ? MathF.Min(1f, beam.MaxForce / totalForce) * fraction * dt : 0;
        foreach (var request in _pullRequests)
        {
            var impulse = request.Force * scale;
            PhysicsSystem.ApplyLinearImpulse(source, impulse, body: sourceBody);
            var recoilTorque = request.SourceOffset.X * impulse.Y - request.SourceOffset.Y * impulse.X;
            if (recoilTorque != 0)
                PhysicsSystem.ApplyAngularImpulse(source, recoilTorque, body: sourceBody);
            PhysicsSystem.ApplyLinearImpulse(request.Target, -impulse);
        }
        if (torque != 0)
        {
            PhysicsSystem.ApplyAngularImpulse(source, torque * scale, body: sourceBody);
            PhysicsSystem.ApplyAngularImpulse(target, -torque * scale, body: targetBody);
        }
    }
}
