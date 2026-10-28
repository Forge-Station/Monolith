using System.Linq;
using Content.Shared._Mono.Weapons.Hitscan.Components; // WOLFGATE(Weapons)
using Content.Shared._WF.ShipShields; // WOLFGATE(ShipShields)
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Containers;
using Robust.Shared.Network; // WOLFGATE(Weapons)
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ISharedAdminLogManager _log = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!; // WOLFGATE(Weapons)

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, shooter, false);

        // WOLFGATE(ShipShields): resolve shield crossings against their visible perimeter
        rayCastResults = rayCastResults.Where(hit => !HasComp<WFShipShieldVisualsComponent>(hit.HitEntity));
        // WOLFGATE(Weapons) START: a chained beam passes through its shooter and the targets it already hit
        if (TryComp<HitscanJumpComponent>(ent, out var jump) && jump.IgnoredEntities.Count > 0)
            rayCastResults = rayCastResults.Where(hit => !jump.IgnoredEntities.Contains(hit.HitEntity));
        // WOLFGATE END

        var target = args.Target;
        // If you are in a container, use the raycast result
        // Otherwise:
        //  1.) Hit the first entity that you targeted.
        //  2.) Hit the first entity that doesn't require you to aim at it specifically to be hit.
        var result = _container.IsEntityOrParentInContainer(shooter)
            ? rayCastResults.FirstOrNull()
            : rayCastResults.FirstOrNull(hit => hit.HitEntity == target
                                                || CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active != true);

        var trace = new HitscanRaycastFiredEvent
        {
            FromCoordinates = args.FromCoordinates,
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntities = [], // Mono
            DistanceTried = result?.Distance ?? ent.Comp.MaxDistance,
            Predicted = args.Predicted, // WOLFGATE(Weapons)
            // WOLFGATE(Weapons): the client predicts the beam only; damage, stun and reflections stay server-side
            Canceled = _net.IsClient,
        };

        if (result?.HitEntity != null) // Mono
        {
            trace.HitEntities.Add(result.Value.HitEntity);

            // WOLFGATE(ShipShields) START: log only the target remaining after shield interception
            // _log.Add(LogType.HitScanHit,
            //     $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.Value.HitEntity):target}"
            //     + $" using {ToPrettyString(args.Gun):entity}.");
            // WOLFGATE END
        }

        // WOLFGATE(ShipShields) START: intercept beams before their visuals and damage
        var shieldTrace = new WFShipShieldHitscanTraceEvent(ent.Owner, trace);
        RaiseLocalEvent(ref shieldTrace);
        trace = shieldTrace.Trace;
        foreach (var hitEntity in trace.HitEntities)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(hitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }
        // WOLFGATE END

        // WOLFGATE(Xenoarch) START: let struck entities react, such as artifact laser triggers
        if (!trace.Canceled)
        {
            foreach (var hitEntity in trace.HitEntities)
            {
                var strike = new HitscanRaycastStrikeEvent(ent.Owner, trace.Gun, trace.Shooter);
                RaiseLocalEvent(hitEntity, ref strike);
            }
        }
        // WOLFGATE END

        RaiseLocalEvent(ent, ref trace);
    }
}
