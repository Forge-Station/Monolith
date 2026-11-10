using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Holds a mob a vehicle ran over down, and keeps the hull from pushing it, until it has been clear of the hull for a moment.</summary>
public sealed partial class VehicleRunoverSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;

    public static readonly TimeSpan StandUpGrace = TimeSpan.FromSeconds(0.6);

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleRunoverComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<VehicleRunoverComponent, StoodEvent>(OnRunoverStood);
    }

    private void OnPreventCollide(Entity<VehicleRunoverComponent> ent, ref PreventCollideEvent args)
    {
        if (args.OtherEntity == ent.Comp.Vehicle)
            args.Cancelled = true;
    }

    private void OnRunoverStood(Entity<VehicleRunoverComponent> ent, ref StoodEvent args)
    {
        if (_net.IsClient)
            return;

        if (TerminatingOrDeleted(ent.Comp.Vehicle))
        {
            RemCompDeferred<VehicleRunoverComponent>(ent);
            return;
        }

        var time = _timing.CurTime;
        var graceUntil = time + StandUpGrace;
        if (ent.Comp.ExpiresAt < graceUntil)
        {
            ent.Comp.ExpiresAt = graceUntil;
            Dirty(ent);
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<VehicleRunoverComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (TerminatingOrDeleted(comp.Vehicle))
            {
                RemCompDeferred<VehicleRunoverComponent>(uid);
                continue;
            }

            if (IsOverlapping(uid, comp.Vehicle))
            {
                if (comp.Duration == TimeSpan.Zero)
                    comp.Duration = TimeSpan.FromSeconds(1.5);

                comp.ExpiresAt = time + comp.Duration + StandUpGrace;
                _stun.TryKnockdown(uid, comp.Duration, true);
                continue;
            }

            if (comp.ExpiresAt <= time)
                RemCompDeferred<VehicleRunoverComponent>(uid);
        }
    }

    /// <summary>Whether the mob's hard fixtures overlap the vehicle's, measured in the vehicle's own frame so that a turned deck does not widen the hull.</summary>
    private bool IsOverlapping(EntityUid mob, EntityUid vehicle)
    {
        var mobXform = Transform(mob);
        var vehicleXform = Transform(vehicle);

        if (mobXform.MapID != vehicleXform.MapID ||
            !TryComp(vehicle, out FixturesComponent? vehicleFixtures) ||
            !TryComp(mob, out FixturesComponent? mobFixtures))
        {
            return false;
        }

        var vehicleTx = _physics.GetPhysicsTransform(vehicle, vehicleXform);
        var mobInVehicleFrame = PhysicsTransform.MulT(vehicleTx, _physics.GetPhysicsTransform(mob, mobXform));

        return GridVehicleMoverSystem.TryGetFixtureAabb(vehicleFixtures, PhysicsTransform.Empty, out var hull) &&
               GridVehicleMoverSystem.TryGetFixtureAabb(mobFixtures, mobInVehicleFrame, out var body) &&
               hull.Intersects(body);
    }
}
