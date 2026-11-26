using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;

namespace Content.Shared._WF.ShipShields;

/// <summary>Clips authoritative and predicted beams against the visible shield perimeter.</summary>
public sealed partial class WFShipShieldHitscanSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipShieldHitscanTraceEvent>(OnTrace);
    }

    private void OnTrace(ref WFShipShieldHitscanTraceEvent args)
    {
        var trace = args.Trace;
        var origin = _transform.ToMapCoordinates(trace.FromCoordinates);
        var sourceGrid = Transform(trace.FromCoordinates.EntityId).GridUid;
        EntityUid? nearest = null;
        var position = Vector2.Zero;
        var strength = 0f;
        var query = EntityQueryEnumerator<WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var xform))
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) || visuals.Grid == null ||
                TerminatingOrDeleted(visuals.Grid.Value) || xform.MapID != origin.MapId)
                continue;
            if (TryComp<PhysicsComponent>(uid, out var physics) && !physics.CanCollide)
                continue;
            if (!_net.IsClient && TryComp<ShipShieldComponent>(uid, out var shieldComponent) &&
                shieldComponent.Source is { } source &&
                (TerminatingOrDeleted(source) || !TryComp<ShipShieldEmitterComponent>(source, out var emitter) ||
                 emitter.Shield != uid))
                continue;
            var inverse = _transform.GetInvWorldMatrix(uid);
            var localOrigin = Vector2.Transform(origin.Position, inverse);
            if (visuals.Grid == sourceGrid && WFShipShieldHitscanMath.Contains(visuals.Contours, localOrigin))
                continue;
            var localDirection = Vector2.TransformNormal(trace.ShotDirection, inverse);
            if (!WFShipShieldHitscanMath.TryIntersect(visuals.Contours, localOrigin, localDirection,
                    trace.DistanceTried, CompOrNull<WFShipShieldShuntComponent>(uid),
                    out var distance, out var impact, out var multiplier))
                continue;
            var attempt = new WFShipShieldInterceptAttemptEvent(visuals.Grid.Value, args.Hitscan, trace.Gun, trace.Shooter);
            RaiseLocalEvent(ref attempt);
            if (attempt.Cancelled)
                continue;
            trace.DistanceTried = distance;
            nearest = uid;
            position = impact;
            strength = multiplier;
        }
        if (nearest is not { } shield)
            return;
        if (args.HitDistances == null)
            trace.HitEntities.Clear();
        else
        {
            var distances = args.HitDistances;
            var maximum = trace.DistanceTried;
            trace.HitEntities.RemoveWhere(uid => !distances.TryGetValue(uid, out var distance) || distance >= maximum);
        }
        trace.Canceled = _net.IsClient || trace.HitEntities.Count == 0;
        args.Trace = trace;
        if (!_net.IsClient && !args.ProbeOnly)
        {
            var impact = new WFShipShieldHitscanImpactEvent(args.Hitscan, position, strength, trace.Gun, trace.Shooter);
            RaiseLocalEvent(shield, ref impact);
        }
    }
}
