using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Server._WF.TractorBeam;

/// <summary>
/// Arrestor ships counter beam recoil with their existing powered thrusters and gyros.
/// Braking is evaluated after beam recoil, using the shuttle movement controller's
/// powered engine limits and retaining independent manual steering priority.
/// </summary>
public sealed class TractorBeamStationKeepingSystem : VirtualController
{
    private readonly HashSet<EntityUid> _brakingShuttles = new();
    [Dependency] private MoverController _mover = default!;

    public override void Initialize()
    {
        // The normal mover queues pilot forces first, then beams apply reaction impulses.
        // Brake against both in the same step so sufficient thrust leaves no residual drift.
        UpdatesAfter.Add(typeof(TractorBeamSystem));
        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        _brakingShuttles.Clear();
        if (prediction || frameTime <= 0 || !float.IsFinite(frameTime))
            return;

        var query = EntityQueryEnumerator<TractorBeamEmitterComponent, PowerConsumerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beam, out var power, out var xform))
        {
            if (Paused(uid) || !beam.Active || beam.Target == null || beam.SourceGrid is not { } source ||
                !xform.Anchored || xform.GridUid != source || power.ReceivedPower < beam.HoldingPower ||
                !TryComp<ShuttleComponent>(source, out var shuttle) || !shuttle.Enabled ||
                HasComp<FTLComponent>(source) || !TryComp<PhysicsComponent>(source, out var body) ||
                body.BodyType != BodyType.Dynamic)
                continue;

            if (!_brakingShuttles.Add(source))
                continue;
            // A ship can arrest another vessel with the tractor operator at their console
            // and nobody at the helm. Empty pilot lists are already supported by the mover.
            EnsureComp<PilotedShuttleComponent>(source);
            _mover.ApplyStationKeeping(source, frameTime, shuttle, body);
        }
    }
}
