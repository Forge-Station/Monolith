using System.Numerics;
using Content.Server.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Server.Physics.Controllers;

/// <summary>
/// A system which makes its entity chasing another entity with selected component.
/// </summary>
public sealed class ChasingWalkSystem : VirtualController
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    // OPT: Field-level reuse — one HashSet allocated at startup, cleared before each lookup.
    // The original code used the same pattern but it's worth making explicit that this avoids
    // per-call heap allocations in ChangeTarget.
    private readonly HashSet<Entity<IComponent>> _potentialChaseTargets = new();

    // OPT: Cached EntityQuery for PhysicsComponent so we don't call GetEntityQuery<>
    // on every ForceImpulse invocation.
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<ChasingWalkComponent, MapInitEvent>(OnChasingMapInit);
    }

    private void OnChasingMapInit(EntityUid uid, ChasingWalkComponent component, MapInitEvent args)
    {
        component.NextImpulseTime = _gameTiming.CurTime;
        component.NextChangeVectorTime = _gameTiming.CurTime;
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // OPT: Read CurTime once per frame instead of once per entity per check.
        var curTime = _gameTiming.CurTime;

        var query = EntityQueryEnumerator<ChasingWalkComponent>();
        while (query.MoveNext(out var uid, out var chasing))
        {
            // OPT: Merge the two time checks to avoid redundant property reads.
            // Both NextImpulseTime and NextChangeVectorTime are checked against the
            // same curTime snapshot, so order them cheapest-first (no side effects either way).
            if (chasing.NextChangeVectorTime <= curTime)
            {
                ChangeTarget(uid, chasing);
                chasing.NextChangeVectorTime += TimeSpan.FromSeconds(
                    _random.NextFloat(chasing.ChangeVectorMinInterval, chasing.ChangeVectorMaxInterval));
            }

            if (chasing.NextImpulseTime > curTime)
                continue;

            ForceImpulse(uid, chasing);
            chasing.NextImpulseTime += TimeSpan.FromSeconds(chasing.ImpulseInterval);
        }
    }

    private void ChangeTarget(EntityUid uid, ChasingWalkComponent component)
    {
        if (component.ChasingComponent.Count <= 0)
            return;

        //We find our coordinates and calculate the radius of the target search.
        var xform = Transform(uid);
        var range = component.MaxChaseRadius;
        var compType = _random.Pick(component.ChasingComponent.Values).Component.GetType();

        _potentialChaseTargets.Clear();
        _lookup.GetEntitiesInRange(compType, _transform.GetMapCoordinates(xform), range, _potentialChaseTargets, LookupFlags.Uncontained);

        //If there are no required components in the radius, don't moving.
        if (_potentialChaseTargets.Count <= 0)
            return;

        //In the case of finding required components, we choose a random one of them and remember its uid.
        component.ChasingEntity = _random.Pick(_potentialChaseTargets).Owner;
        component.Speed = _random.NextFloat(component.MinSpeed, component.MaxSpeed);
    }

    //pushing the entity toward its target
    private void ForceImpulse(EntityUid uid, ChasingWalkComponent component)
    {
        // OPT: Check for null/deleted before the more expensive TryComp call.
        if (component.ChasingEntity == null)
        {
            ChangeTarget(uid, component);
            return;
        }

        // OPT: Use Deleted(EntityUid) overload — avoids a second null check inside the original
        // Deleted(EntityUid?) which boxes the nullable before comparing.
        if (Deleted(component.ChasingEntity.Value))
        {
            component.ChasingEntity = null;
            ChangeTarget(uid, component);
            return;
        }

        // OPT: Use cached EntityQuery instead of TryComp<PhysicsComponent>(uid, ...).
        if (!_physicsQuery.TryGetComponent(uid, out var physics))
            return;

        var pos1 = _transform.GetWorldPosition(uid);
        var pos2 = _transform.GetWorldPosition(component.ChasingEntity.Value);

        var delta = pos2 - pos1;

        // OPT: Avoid calling delta.Length() twice (once for the > 0 guard, once inside Normalized()).
        // Compute LengthSquared() for the zero-check (no sqrt) and only do the division once.
        Vector2 speed;
        var lenSq = delta.LengthSquared();
        if (lenSq > 0f)
        {
            var len = MathF.Sqrt(lenSq);
            speed = (delta / len) * component.Speed;
        }
        else
        {
            speed = Vector2.Zero;
        }

        _physics.SetLinearVelocity(uid, speed);
        // Keeps the entity airborne so nearby explosions don't "ground" it and kill velocity.
        _physics.SetBodyStatus(uid, physics, BodyStatus.InAir);
    }
}
