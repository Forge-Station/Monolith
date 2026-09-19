using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Detection;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Timing;

namespace Content.Server._WF.TractorBeam;

/// <summary>
/// Server-authoritative, force-limited ship tethers. Each impulse has an equal reaction on the
/// emitter's grid. Existing thrusters, docking joints and damping remain responsible for braking.
/// </summary>
public sealed partial class TractorBeamSystem : VirtualController
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private DetectionSystem _detection = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MoverController _mover = default!;

    private TimeSpan _nextUiUpdate;
    private readonly HashSet<EntityUid> _docked = new();
    private readonly List<Entity<TractorBeamEmitterComponent>> _active = new();
    private readonly Dictionary<EntityUid, Vector2> _predictedCenters = new();
    private readonly Dictionary<EntityUid, float> _predictedAngles = new();
    private readonly List<EntityUid> _bodies = new();
    private readonly HashSet<EntityUid> _brakingSources = new();

    public override void Initialize()
    {
        // Read this tick's thruster forces, before the engine integrates them. This also makes
        // resistance measurable when a pinned target has no visible movement.
        UpdatesAfter.Add(typeof(MoverController));
        base.Initialize();
        SubscribeLocalEvent<TractorBeamConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<TractorBeamConsoleComponent, TractorBeamConsoleMessage>(OnCommand);
        SubscribeLocalEvent<TractorBeamEmitterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        var beams = EntityQueryEnumerator<TractorBeamEmitterComponent>();
        while (beams.MoveNext(out var uid, out var beam))
        {
            if (beam.SourceGrid == args.Grid || beam.Target == args.Grid)
                Release(uid, beam);
        }
    }

    private void OnUiOpened(EntityUid uid, TractorBeamConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp);
    }

    private void OnShutdown(EntityUid uid, TractorBeamEmitterComponent comp, ComponentShutdown args)
    {
        Release(uid, comp);
    }

    private bool ConsoleReady(EntityUid uid, out EntityUid grid)
    {
        grid = default;
        if (!TryComp(uid, out TransformComponent? xform) || !xform.Anchored ||
            xform.GridUid is not { } source || !IsMovableGrid(source) ||
            !TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered)
            return false;

        grid = source;
        return true;
    }

    private bool IsMovableGrid(EntityUid uid)
    {
        if (!IsFreeGrid(uid))
            return false;

        // A dynamic ship docked to a fixed station is not a free target either.
        _mobilityDocked.Clear();
        _shuttles.GetAllDockedShuttlesIgnoringFTLLock(uid, _mobilityDocked);
        foreach (var docked in _mobilityDocked)
        {
            if (!IsFreeGrid(docked))
                return false;
        }
        return true;
    }

    private readonly HashSet<EntityUid> _mobilityDocked = new();

    private bool IsFreeGrid(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && !Paused(uid) && HasComp<MapGridComponent>(uid) &&
               !Transform(uid).Anchored &&
               (!TryComp<ShuttleComponent>(uid, out var shuttle) || shuttle.Enabled) &&
               !HasComp<FTLComponent>(uid) && TryComp<PhysicsComponent>(uid, out var body) &&
               body.BodyType == BodyType.Dynamic && float.IsFinite(body.Mass) && body.Mass > 0;
    }

    private bool VisibleTarget(EntityUid console, EntityUid source, EntityUid target)
    {
        if (target == source || !IsMovableGrid(target) || Transform(target).MapID != Transform(source).MapID)
            return false;

        if (TryComp<IFFComponent>(target, out var iff))
        {
            if ((iff.Flags & IFFFlags.Hide) != 0)
                return false;
            if ((iff.Flags & IFFFlags.HideLabel) != 0 &&
                _detection.IsGridDetected(target, console) == DetectionLevel.Undetected)
                return false;
        }

        return true;
    }

    private Vector2 Center(EntityUid grid, PhysicsComponent? body = null)
    {
        body ??= Comp<PhysicsComponent>(grid);
        return TransformSystem.ToMapCoordinates(new EntityCoordinates(grid, body.LocalCenter)).Position;
    }

    private bool InRange(EntityUid emitter, TractorBeamEmitterComponent comp, EntityUid target)
    {
        return InOperatingCone(emitter, comp, Center(target));
    }

    private bool InOperatingCone(EntityUid emitter, TractorBeamEmitterComponent comp, Vector2 position)
    {
        var offset = position - TransformSystem.GetWorldPosition(emitter);
        var forward = TransformSystem.GetWorldRotation(emitter).RotateVec(Vector2.UnitY);
        return TractorBeamOperatingCone.Contains(offset, forward, comp.MaxRange, comp.ConeHalfAngle);
    }

    private bool SameDockedGroup(EntityUid source, EntityUid target)
    {
        _docked.Clear();
        _shuttles.GetAllDockedShuttlesIgnoringFTLLock(source, _docked);
        return _docked.Contains(target);
    }

    private void OnCommand(EntityUid uid, TractorBeamConsoleComponent console, TractorBeamConsoleMessage args)
    {
        // BUI checks actor access/range. Recheck all entity IDs and machine state here; never trust the radar list.
        if (!_ui.IsUiOpen(uid, TractorBeamUiKey.Key, args.Actor) ||
            !ConsoleReady(uid, out var source) ||
            !TryGetEntity(args.Emitter, out var emitterUid) || emitterUid is not { } emitter ||
            !TryComp<TractorBeamEmitterComponent>(emitter, out var beam) ||
            Transform(emitter).GridUid != source || !Transform(emitter).Anchored)
            return;

        // Release remains available during the lock-command cooldown.
        if (args.Target == null)
        {
            Release(emitter, beam);
            UpdateUi(uid, console);
            return;
        }

        if (_timing.CurTime < console.NextCommand)
            return;
        console.NextCommand = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        if (beam.CooldownRemaining > 0)
            return;

        if (!TryGetEntity(args.Target.Value, out var targetUid) || targetUid is not { } target ||
            !VisibleTarget(uid, source, target) || !InRange(emitter, beam, target) ||
            Vector2.Distance(Center(source), Center(target)) > console.Range ||
            SameDockedGroup(source, target) ||
            !TryComp<PowerConsumerComponent>(emitter, out var power) || power.ReceivedPower < beam.IdlePower)
        {
            _popup.PopupEntity(Loc.GetString("tractor-beam-lock-unavailable"), uid, args.Actor);
            return;
        }

        // Shields block acquisition only; an existing tether survives shields coming back up.
        if (beam.Target != target && HasComp<ShipShieldedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("tractor-beam-target-shielded"), uid, args.Actor);
            return;
        }

        if (args.Pulling || args.DesiredRange != null)
        {
            // Range commands modify an existing capture; they cannot acquire a new target or
            // reset its bearing, orientation, rest length, or startup power allowance.
            var minimum = MinimumGridSeparation(source, target, beam.CollectionStandOff);
            var desired = args.DesiredRange ?? minimum;
            var maximum = MathF.Min(beam.MaxRange, MathF.Min(beam.HoldDistance,
                Vector2.Distance(Center(source), Center(target))));
            if (args.LockInPlace || beam.Target != target || beam.SourceGrid != source || !beam.Active ||
                power.ReceivedPower < beam.HoldingPower || !float.IsFinite(desired) || desired <= 0 ||
                desired < minimum || desired > maximum)
            {
                _popup.PopupEntity(Loc.GetString("tractor-beam-range-unavailable"), uid, args.Actor);
                return;
            }
            beam.RequestedDistance = desired;
            beam.Pulling = beam.HoldDistance > desired;
            beam.LockedInPlace = false;
            beam.LockedSeparation = Vector2.Zero;
            beam.LockedAngle = 0;
            Dirty(emitter, beam);
            UpdateUi(uid, console);
            return;
        }

        if (args.LockInPlace && (beam.Target != target ||
            (!beam.LockedInPlace && !CanLockInPlace(emitter, beam))))
        {
            _popup.PopupEntity(Loc.GetString("tractor-beam-pin-unavailable"), uid, args.Actor);
            return;
        }

        // Repeated lock commands must not ratchet the rest length or renew a failing lock.
        if (beam.Target == target)
        {
            if (args.LockInPlace && !beam.LockedInPlace)
            {
                beam.LockedSeparation = Center(target) - Center(source);
                beam.LockedSourceAngle = (float) TransformSystem.GetWorldRotation(source).Theta;
                beam.LockedAngle = (float) (TransformSystem.GetWorldRotation(target).Theta -
                    TransformSystem.GetWorldRotation(source).Theta);
            }
            beam.LockedInPlace = args.LockInPlace;
            beam.Pulling = false;
            beam.RequestedDistance = null;
            Dirty(emitter, beam);
            UpdateUi(uid, console);
            return;
        }

        StopBeamAudio(emitter, beam);
        beam.SourceGrid = source;
        beam.Controller = uid;
        beam.Target = target;
        beam.TargetOffset = Comp<PhysicsComponent>(target).LocalCenter;
        beam.HoldDistance = Vector2.Distance(Center(source), Center(target));
        beam.HoldSourceAngle = (float) TransformSystem.GetWorldRotation(source).Theta;
        beam.HoldDirection = beam.HoldDistance > 0.001f
            ? (Center(target) - Center(source)) / beam.HoldDistance : Vector2.UnitY;
        beam.HoldAngle = (float) (TransformSystem.GetWorldRotation(target).Theta -
            TransformSystem.GetWorldRotation(source).Theta);
        beam.PowerGraceUntil = _timing.CurTime + TimeSpan.FromSeconds(1);
        beam.OverloadTime = 0;
        beam.RequiredForce = 0;
        beam.DistanceStrain = DistanceStrain(emitter, beam, target);
        beam.RequestedPower = TractorBeamPhysics.CalculatePower(0, beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
        beam.Strain = MathF.Round(beam.DistanceStrain * 100f) / 100f;
        beam.Active = false;
        beam.Pulling = false;
        beam.RequestedDistance = null;
        beam.LockedInPlace = false;
        beam.LockedSeparation = Vector2.Zero;
        beam.LockedAngle = 0;
        power.DrawRate = beam.RequestedPower;
        Dirty(emitter, beam);
        UpdateUi(uid, console);
    }

    public void Release(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        StopBeamAudio(uid, beam);
        DeleteVisual(beam);
        var changed = beam.Target != null || beam.Active || beam.Strain != 0;
        if (beam.Target != null || beam.Active)
            beam.CooldownRemaining = beam.RestartCooldown;
        beam.Target = null;
        beam.SourceGrid = null;
        beam.Controller = null;
        beam.Active = false;
        beam.Pulling = false;
        beam.RequestedDistance = null;
        beam.LockedInPlace = false;
        beam.LockedSeparation = Vector2.Zero;
        beam.LockedAngle = 0;
        beam.Strain = 0;
        beam.RequiredForce = 0;
        beam.DistanceStrain = 0;
        beam.OverloadTime = 0;
        beam.HoldDirection = Vector2.Zero;
        beam.HoldSourceAngle = null;
        beam.LockedSourceAngle = null;
        beam.HoldAngle = null;
        beam.RequestedPower = Transform(uid).Anchored ? beam.IdlePower : 0;
        if (TryComp<PowerConsumerComponent>(uid, out var power))
            power.DrawRate = beam.RequestedPower;
        if (changed && !TerminatingOrDeleted(uid))
            Dirty(uid, beam);
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        if (prediction || frameTime <= 0 || !float.IsFinite(frameTime))
            return;

        _active.Clear();
        _predictedCenters.Clear();
        _predictedAngles.Clear();
        _bodies.Clear();
        _brakingSources.Clear();
        _collectionTargets.Clear();
        var query = EntityQueryEnumerator<TractorBeamEmitterComponent, PowerConsumerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beam, out var power, out var xform))
        {
            if (Paused(uid))
                continue;
            beam.CooldownRemaining = MathF.Max(0, beam.CooldownRemaining - frameTime);
            if (beam.Target is not { } target || beam.SourceGrid is not { } source ||
                beam.Controller is not { } controller || !ConsoleReady(controller, out var consoleGrid) ||
                consoleGrid != source || xform.GridUid != source || !xform.Anchored ||
                !VisibleTarget(controller, source, target) || !InRange(uid, beam, target) ||
                SameDockedGroup(source, target) ||
                (power.ReceivedPower < beam.HoldingPower && _timing.CurTime >= beam.PowerGraceUntil))
            {
                Release(uid, beam);
                continue;
            }

            beam.RequiredForce = 0;
            beam.DistanceStrain = DistanceStrain(uid, beam, target);
            // Also initialize programmatically created locks once, never chase a moving target's bearing.
            if (beam.HoldDirection == Vector2.Zero)
            {
                var separation = Center(target) - Center(source);
                beam.HoldDirection = separation.LengthSquared() > 0.000001f
                    ? Vector2.Normalize(separation) : Vector2.UnitY;
            }
            _active.Add((uid, beam));
            if (power.ReceivedPower >= beam.HoldingPower)
                _brakingSources.Add(source);
            beam.HoldSourceAngle ??= (float) TransformSystem.GetWorldRotation(source).Theta;
            if (beam.LockedInPlace)
                beam.LockedSourceAngle ??= beam.HoldSourceAngle;
            beam.HoldAngle ??= (float) (TransformSystem.GetWorldRotation(target).Theta -
                TransformSystem.GetWorldRotation(source).Theta);
            if (_predictedCenters.TryAdd(source, Center(source)))
            {
                _bodies.Add(source);
            }
            _predictedAngles.TryAdd(source, (float) TransformSystem.GetWorldRotation(source).Theta);
            if (_predictedCenters.TryAdd(target, Center(target)))
            {
                _bodies.Add(target);
            }
            _predictedAngles.TryAdd(target, (float) TransformSystem.GetWorldRotation(target).Theta);
            FindCollectionTargets(uid, beam);
            if (beam.Pulling)
            {
                var destination = MathF.Max(MinimumGridSeparation(source, target, beam.CollectionStandOff),
                    beam.RequestedDistance ?? 0);
                beam.HoldDistance = MathF.Max(destination, beam.HoldDistance - MathF.Max(0, beam.ReelSpeed) * frameTime);
                if (beam.HoldDistance <= destination)
                {
                    beam.Pulling = false;
                    Dirty(uid, beam);
                }
            }
        }

        // Sequential impulses share updated velocities, so multiple dishes cooperate without
        // each independently cancelling the same target velocity. Small steps soften stiff links.
        const int substeps = 4;
        var dt = frameTime / substeps;
        for (var step = 0; step < substeps; step++)
        {
            foreach (var (uid, beam) in _active)
            {
                ApplyBeamForces(uid, beam, dt, (step + 1) * dt);
            }
            // Predict every participating body's position once per substep. The engine still
            // performs the actual movement/collisions; this keeps spring estimates consistent.
            foreach (var body in _bodies)
            {
                var physics = Comp<PhysicsComponent>(body);
                _predictedCenters[body] += PredictedVelocity(body, physics, (step + 1) * dt) * dt;
                if (_predictedAngles.ContainsKey(body))
                    _predictedAngles[body] += PredictedAngularVelocity(body, physics, (step + 1) * dt) * dt;
            }
        }

        foreach (var (uid, beam) in _active)
        {
            var power = Comp<PowerConsumerComponent>(uid);
            beam.RequestedPower = TractorBeamPhysics.CalculatePower(beam.RequiredForce, beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
            power.DrawRate = beam.RequestedPower;
            var active = power.ReceivedPower >= beam.HoldingPower;
            // Quantize the visual strain to avoid sending a component state every physics tick.
            var strain = MathF.Round(TractorBeamPhysics.CalculateStrain(beam.RequiredForce, beam.MaxForce, beam.DistanceStrain) * 100f) / 100f;
            beam.OverloadTime = strain >= 1f ? beam.OverloadTime + frameTime : 0;
            if (beam.OverloadTime >= beam.OverloadDuration)
            {
                Release(uid, beam);
                continue;
            }
            if (beam.Active != active || beam.Strain != strain)
            {
                beam.Active = active;
                beam.Strain = strain;
                Dirty(uid, beam);
            }
            UpdateVisual(uid, beam);
            UpdateBeamAudio(uid, beam);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUiUpdate)
            return;
        _nextUiUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var consoles = EntityQueryEnumerator<TractorBeamConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            if (_ui.IsUiOpen(uid, TractorBeamUiKey.Key))
                UpdateUi(uid, console);
        }
    }

    private void UpdateUi(EntityUid uid, TractorBeamConsoleComponent console)
    {
        var emitters = new List<TractorBeamEmitterEntry>();
        var targets = new List<TractorBeamTargetEntry>();
        var connected = ConsoleReady(uid, out var source);
        if (connected)
        {
            var center = Center(source);
            var rotation = TransformSystem.GetWorldRotation(source);
            var machines = EntityQueryEnumerator<TractorBeamEmitterComponent, PowerConsumerComponent, TransformComponent>();
            while (machines.MoveNext(out var emitter, out var beam, out var power, out var xform))
            {
                if (xform.GridUid != source || !xform.Anchored)
                    continue;
                var pinStatus = GetPinStatus(emitter, beam);
                emitters.Add(new TractorBeamEmitterEntry(GetNetEntity(emitter), Name(emitter),
                    beam.Target is { } target ? GetNetEntity(target) : null,
                    power.ReceivedPower >= (beam.Target == null ? beam.IdlePower : beam.HoldingPower),
                    TractorBeamPhysics.CalculateStrain(beam.RequiredForce, beam.MaxForce, beam.DistanceStrain),
                    beam.RequestedPower, power.ReceivedPower, beam.MaxRange, beam.Pulling,
                    beam.LockedInPlace, pinStatus == TractorBeamPinStatus.Ready,
                    (-rotation).RotateVec(TransformSystem.GetWorldPosition(emitter) - center),
                    (TransformSystem.GetWorldRotation(emitter) - rotation).RotateVec(Vector2.UnitY), beam.ConeHalfAngle,
                    beam.Target is { } captured && !TerminatingOrDeleted(captured) ? Vector2.Distance(center, Center(captured)) : 0,
                    beam.HoldDistance,
                    beam.Target is { } rangeTarget && !TerminatingOrDeleted(rangeTarget)
                        ? MinimumGridSeparation(source, rangeTarget, beam.CollectionStandOff) : 0,
                    beam.RequestedDistance, beam.Active, pinStatus, beam.CooldownRemaining));
            }

            _docked.Clear();
            _shuttles.GetAllDockedShuttlesIgnoringFTLLock(source, _docked);
            var grids = EntityQueryEnumerator<MapGridComponent, PhysicsComponent>();
            while (grids.MoveNext(out var target, out _, out var body))
            {
                if (!VisibleTarget(uid, source, target))
                    continue;
                var relative = Center(target, body) - center;
                if (relative.LengthSquared() > console.Range * console.Range || _docked.Contains(target))
                    continue;
                var label = TryComp<IFFComponent>(target, out var iff) && (iff.Flags & IFFFlags.HideLabel) != 0
                    ? Loc.GetString("shuttle-console-unknown") : Name(target);
                targets.Add(new TractorBeamTargetEntry(GetNetEntity(target), label, (-rotation).RotateVec(relative), body.Mass,
                    HasComp<ShipShieldedComponent>(target)));
            }
        }

        _ui.SetUiState(uid, TractorBeamUiKey.Key,
            new TractorBeamConsoleBoundUserInterfaceState(connected, console.Range, emitters.ToArray(), targets.ToArray()));
    }

    private bool CanLockInPlace(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        return GetPinStatus(uid, beam) == TractorBeamPinStatus.Ready;
    }

    private float DistanceStrain(EntityUid uid, TractorBeamEmitterComponent beam, EntityUid target)
    {
        return TractorBeamPhysics.CalculateDistanceStrain(
            Vector2.Distance(TransformSystem.GetWorldPosition(uid), Center(target)),
            beam.MaxRange, beam.RangeStrainAtMaxRange);
    }

    private TractorBeamPinStatus GetPinStatus(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        if (beam.Target is not { } target || beam.SourceGrid is not { } source ||
            !TryComp<PhysicsComponent>(source, out var sourceBody) || !TryComp<PhysicsComponent>(target, out var targetBody))
            return TractorBeamPinStatus.NoTarget;
        if (beam.LockedInPlace)
            return TractorBeamPinStatus.AlreadyPinned;
        if (!TryComp<PowerConsumerComponent>(uid, out var power) || power.ReceivedPower < beam.HoldingPower)
            return TractorBeamPinStatus.InsufficientPower;
        if (!beam.Active)
            return TractorBeamPinStatus.WaitingForBeam;
        if (!(PhysicsSystem.GetMapLinearVelocity(source, sourceBody).LengthSquared() <= 0.04f) ||
            !(MathF.Abs(sourceBody.AngularVelocity) <= 0.05f))
            return TractorBeamPinStatus.StopSource;
        if (!(PhysicsSystem.GetMapLinearVelocity(target, targetBody).LengthSquared() <= 0.04f) ||
            !(MathF.Abs(targetBody.AngularVelocity) <= 0.05f))
            return TractorBeamPinStatus.StopTarget;

        // Supply reflects the network's previous allocation while demand is recomputed by the
        // beam every physics tick. A sub-percent settling fluctuation must not keep a stopped
        // capture permanently unavailable. Real brownouts and the holding overhead still gate
        // this command; the solver continues to scale all restraint to actual received power.
        var maintenancePower = TractorBeamPhysics.CalculatePower(0, beam.MaxForce, beam.HoldingPower, beam.MaxPower, beam.DistanceStrain);
        var demand = MathF.Max(maintenancePower, beam.RequestedPower);
        var tolerance = MathF.Max(250f, demand * 0.01f);
        return float.IsFinite(demand) && power.ReceivedPower + tolerance >= demand
            ? TractorBeamPinStatus.Ready : TractorBeamPinStatus.InsufficientPower;
    }

    private Vector2 PredictedVelocity(EntityUid uid, PhysicsComponent body, float elapsed)
    {
        if (_brakingSources.Contains(uid) && TryComp<ShuttleComponent>(uid, out var shuttle))
            return _mover.PredictTractorBraking(uid, shuttle, body, elapsed).Linear;
        // The engine applies Force once after controllers finish. Include only the elapsed
        // substep portion here; actual impulses have already changed LinearVelocity.
        return PhysicsSystem.GetMapLinearVelocity(uid, body) + body.Force * body.InvMass * elapsed;
    }

    private float PredictedAngularVelocity(EntityUid uid, PhysicsComponent body, float elapsed)
    {
        if (_brakingSources.Contains(uid) && TryComp<ShuttleComponent>(uid, out var shuttle))
            return _mover.PredictTractorBraking(uid, shuttle, body, elapsed).Angular;
        return body.AngularVelocity + body.Torque * body.InvI * elapsed;
    }
}
