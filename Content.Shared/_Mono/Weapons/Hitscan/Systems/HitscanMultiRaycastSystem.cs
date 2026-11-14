using System.Linq;
using Content.Shared._Mono.Weapons.Hitscan.Components;
using Content.Shared._WF.ShipShields;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Mono.Weapons.Hitscan.Systems;

/// <summary>Resolves a piercing hitscan the same way the basic raycast does, then hands the hits to damage and shields.</summary>
public sealed partial class HitscanMultiRaycastSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ISharedAdminLogManager _log = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private readonly HashSet<EntityUid> _hitEntities = [];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HitscanMultiRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanMultiRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        var mapCoords = _transform.ToMapCoordinates(args.FromCoordinates);
        var ray = new CollisionRay(mapCoords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCoords.MapId, ray, ent.Comp.MaxDistance, shooter, false)
            .Where(hit => !HasComp<WFShipShieldVisualsComponent>(hit.HitEntity));

        var hitDistances = new Dictionary<EntityUid, float>();
        var hitCount = 0;
        var latestDistance = ent.Comp.MaxDistance;

        foreach (var result in rayCastResults)
        {
            if (!_physicsQuery.TryComp(result.HitEntity, out var phys))
                continue;

            _hitEntities.Add(result.HitEntity);
            hitDistances[result.HitEntity] = result.Distance;
            latestDistance = result.Distance;
            hitCount++;

            if (hitCount > ent.Comp.MaxPierce)
                break;

            if ((phys.CollisionLayer & (int) ent.Comp.PierceCollisionMask) != 0)
                break;
        }

        var trace = new HitscanRaycastFiredEvent
        {
            FromCoordinates = args.FromCoordinates,
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntities = _hitEntities,
            DistanceTried = latestDistance,
            Predicted = args.Predicted,
            // The client predicts the beam only; damage stays server-side.
            Canceled = _net.IsClient,
        };

        var shieldTrace = new WFShipShieldHitscanTraceEvent(ent.Owner, trace) { HitDistances = hitDistances };
        RaiseLocalEvent(ref shieldTrace);
        trace = shieldTrace.Trace;
        foreach (var hitEntity in trace.HitEntities)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(hitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }

        RaiseLocalEvent(ent, ref trace);
        _hitEntities.Clear();
    }
}
