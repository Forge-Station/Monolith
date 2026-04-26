using Content.Server.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics;
using System.Numerics;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Utility;

namespace Content.Server.Physics.Controllers;

/// <summary>
/// A component which makes its entity periodically chaotic jumps arounds
/// </summary>
public sealed class ChaoticJumpSystem : VirtualController
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ChaoticJumpComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ChaoticJumpComponent> chaotic, ref MapInitEvent args)
    {
        // So the entity doesn't teleport instantly.
        chaotic.Comp.NextJumpTime = _gameTiming.CurTime
            + TimeSpan.FromSeconds(_random.NextFloat(chaotic.Comp.JumpMinInterval, chaotic.Comp.JumpMaxInterval));
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // OPT: Cache CurTime once outside the loop instead of reading the property
        // on every iteration. IGameTiming.CurTime may trigger a property getter each call.
        var curTime = _gameTiming.CurTime;

        var query = EntityQueryEnumerator<ChaoticJumpComponent>();
        while (query.MoveNext(out var uid, out var chaotic))
        {
            if (chaotic.NextJumpTime > curTime)
                continue;

            Jump(uid, chaotic);
            chaotic.NextJumpTime += TimeSpan.FromSeconds(
                _random.NextFloat(chaotic.JumpMinInterval, chaotic.JumpMaxInterval));
        }
    }

    private void Jump(EntityUid uid, ChaoticJumpComponent component)
    {
        var transform = Transform(uid);
        var startPos = _transform.GetWorldPosition(uid);

        var direction = _random.NextAngle();
        var range = _random.NextFloat(component.RangeMin, component.RangeMax);

        // OPT: Compute sin/cos once and reuse — Math.Cos/Sin are expensive trig calls.
        // In the original code they were computed twice (for offset and for fallback position).
        var dirVec = direction.ToVec(); // already a unit vector from Angle.ToVec()
        var cosD = dirVec.X;
        var sinD = dirVec.Y;

        var ray = new CollisionRay(startPos, dirVec, component.CollisionMask);
        var rayCastResults = _physics.IntersectRay(transform.MapID, ray, range, uid, returnOnFirstHit: false).FirstOrNull();

        Vector2 targetPos;
        if (rayCastResults != null)
        {
            var hitPos = rayCastResults.Value.HitPos;
            // Offset so the teleport does not land directly inside the hit target
            targetPos = new Vector2(hitPos.X - cosD, hitPos.Y - sinD);
        }
        else
        {
            targetPos = new Vector2(startPos.X + range * cosD, startPos.Y + range * sinD);
        }

        Spawn(component.Effect, transform.Coordinates);
        _transform.SetWorldPosition(uid, targetPos);
    }
}
