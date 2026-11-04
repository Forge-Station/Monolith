using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.FtlEffects;
using Content.Shared._WF.Planets;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Planets.Hop;

public sealed partial class WFPlanetHopSystem
{
    /// <summary>Warm-up over: the hull and whatever is docked to it now leave for the hold map.</summary>
    private void EnterTransit(EntityUid hull, WFPlanetHopComponent hop)
    {
        var now = _timing.CurTime;
        var group = new HashSet<EntityUid>();
        _shuttle.GetAllDockedShuttles(hull, group);
        group.Add(hull);
        UndockOutsiders(group);

        var riders = Offsets(hull, group);
        var xform = Transform(hull);
        var fromMap = xform.MapUid;
        var shuttle = Comp<ShuttleComponent>(hull);

        Knock(hull);
        LeaveStartupTail(hull, hop, fromMap);

        hop.Phase = WFPlanetHopPhase.Transit;
        hop.PhaseEnd = now + TimeSpan.FromSeconds(TravelTime);
        hop.Riders = riders;

        var holdMap = _maps.CreateMap(out var holdId);
        _meta.SetEntityName(holdMap, "Hop");
        AddComp<WFPlanetHopMapComponent>(holdMap);
        _maps.SetAmbientLight(holdId, Color.MediumPurple);
        hop.HoldMap = holdMap;

        var grid = Comp<MapGridComponent>(hull);
        var oldMatrix = _transform.GetWorldMatrix(xform);

        _transform.SetCoordinates(hull, new EntityCoordinates(holdMap, -grid.LocalAABB.Center));
        _transform.SetWorldRotation(hull, Angle.Zero);
        _shuttle.LeaveNoFTLBehind((hull, xform), oldMatrix, fromMap);

        var mainPos = _transform.GetWorldPosition(hull);

        foreach (var rider in riders)
        {
            var riderXform = Transform(rider.Grid);
            var riderOldMap = riderXform.MapUid;
            var riderOldMatrix = _transform.GetWorldMatrix(riderXform);

            _transform.SetParent(rider.Grid, riderXform, holdMap);
            _transform.SetWorldRotationNoLerp(rider.Grid, rider.Rotation);
            _transform.SetWorldPosition(rider.Grid, mainPos + rider.Offset);
            _shuttle.LeaveNoFTLBehind((rider.Grid, riderXform), riderOldMatrix, riderOldMap);

            var member = EnsureComp<WFPlanetHopComponent>(rider.Grid);
            member.Hull = hull;

            CopyApproach(hull, rider.Grid);
            Settle(rider.Grid);
            _docking.RedockDocks(rider.Grid);
            _console.RefreshShuttleConsoles(rider.Grid);
        }

        _thruster.DisableLinearThrusters(shuttle);
        Settle(hull);
        _docking.SetDockBolts(hull, true);

        var audio = _audio.PlayPvs(_travelSound, hull);
        _audio.SetGridAudio(audio);
        hop.TravelStream = audio?.Entity;

        SpawnVisualizer(hull, hop);
        _departure.Publish(hull, hop.Started, now, true, false);

        var started = new WFPlanetHopStartedEvent(hull, new EntityCoordinates(hop.TargetMap, hop.Target));
        RaiseLocalEvent(ref started);

        _console.RefreshShuttleConsoles(hull);
    }

    /// <summary>The hold map is over: the group comes out on the target map, inside the ring and clear of other grids.</summary>
    private void Land(EntityUid hull, WFPlanetHopComponent hop)
    {
        if (LandingMap(hop) is not { } landing)
        {
            Log.Error($"Hop of {ToPrettyString(hull)} has nowhere to land; leaving it on its hold map.");
            Release(hull, hop, keepMap: true);
            return;
        }

        var riders = hop.Riders.FindAll(rider => !TerminatingOrDeleted(rider.Grid));

        // The ring and the clear check measure the hull at the pose it will land in.
        _transform.SetWorldRotation(hull, hop.Angle);
        var spot = FindSpot(hull, riders, landing, landing == hop.TargetMap ? hop.Target : hop.Origin, hop.Angle);

        Place(hull, riders, landing, spot, hop.Angle);

        _bounds.ClampHull(hull);

        if (HasComp<MapGridComponent>(hull))
            _shuttle.Smimsh(hull);

        Knock(hull);

        var arrival = _audio.PlayPvs(_arrivalSound, hull);
        _audio.SetGridAudio(arrival);

        var now = _timing.CurTime;
        Release(hull, hop);

        _departure.Publish(hull, now, now, true, true);

        var done = new WFPlanetHopCompletedEvent(hull, landing);
        RaiseLocalEvent(ref done);

        _console.RefreshShuttleConsoles(hull);
    }

    /// <summary>The hull is gone: the grids that rode with it come out where the hull would have, and the hold map goes.</summary>
    private void Abort(EntityUid hull, WFPlanetHopComponent hop)
    {
        if (hop.Phase == WFPlanetHopPhase.Transit && LandingMap(hop) is { } landing)
        {
            var riders = hop.Riders.FindAll(rider => !TerminatingOrDeleted(rider.Grid));

            // The hull's own place is empty; the riders hold their offsets from it.
            Place(null, riders, landing, hop.Target, hop.Angle);
        }

        Release(hull, hop);
    }

    /// <summary>The map the hop lands on: its target, or where it left if that is gone.</summary>
    private EntityUid? LandingMap(WFPlanetHopComponent hop)
    {
        if (Exists(hop.TargetMap) && !TerminatingOrDeleted(hop.TargetMap))
            return hop.TargetMap;

        return Exists(hop.OriginMap) && !TerminatingOrDeleted(hop.OriginMap) ? hop.OriginMap : null;
    }

    /// <summary>Puts the hull and its riders on a map at a pose, at rest, and rebuilds their docks.</summary>
    private void Place(EntityUid? hull, List<WFHopRider> riders, EntityUid map, Vector2 spot, Angle angle)
    {
        if (hull is { } lead)
        {
            _transform.SetCoordinates(lead, Transform(lead), new EntityCoordinates(map, spot), rotation: angle.Reduced());
            Settle(lead);
        }

        foreach (var rider in riders)
        {
            var xform = Transform(rider.Grid);

            _transform.SetParent(rider.Grid, xform, map);
            _transform.SetWorldRotationNoLerp(rider.Grid, angle + rider.Rotation);
            _transform.SetWorldPosition(rider.Grid, spot + angle.RotateVec(rider.Offset));
            Settle(rider.Grid);
            _docking.RedockDocks(rider.Grid);
        }

        if (hull is { } head)
            _docking.RedockDocks(head);
    }

    /// <summary>At rest, thrusters off, physics back on.</summary>
    private void Settle(EntityUid grid)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return;

        _physics.SetLinearVelocity(grid, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(grid, 0f, body: body);

        if (TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            _thruster.DisableLinearThrusters(shuttle);
            _shuttle.Enable(grid, component: body, shuttle: shuttle);
        }
    }

    /// <summary>Ends a hop: marks, effects, sounds, the hold map and the plan itself all go, on the tick the grids move.</summary>
    private void Release(EntityUid hull, WFPlanetHopComponent hop, bool keepMap = false)
    {
        Unpreload(hop);

        hop.StartupStream = _audio.Stop(hop.StartupStream);
        hop.TravelStream = _audio.Stop(hop.TravelStream);

        if (hop.Visualizer is { } visualizer)
            QueueDel(visualizer);

        hop.Visualizer = null;

        var grids = new List<EntityUid> { hull };

        foreach (var rider in hop.Riders)
        {
            grids.Add(rider.Grid);
        }

        var landed = HasComp<MapGridComponent>(hull) && !TerminatingOrDeleted(hull);

        foreach (var grid in grids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            RemComp<WFPlanetApproachComponent>(grid);

            if (grid != hull && !landed)
                RemComp<FtlDepartureComponent>(grid);

            if (HasComp<ForceAnchorPostFTLComponent>(grid))
            {
                _shuttle.Disable(grid, force: true);
                EnsureComp<PreventGridAnchorChangesComponent>(grid);
            }

            if (grid != hull)
                RemComp<WFPlanetHopComponent>(grid);
        }

        if (!TerminatingOrDeleted(hull))
        {
            _docking.SetDockBolts(hull, false);
            RemComp<WFPlanetHopComponent>(hull);
        }

        if (!keepMap && hop.HoldMap is { } holdMap && TryComp<MapComponent>(holdMap, out var hold))
            _maps.DeleteMap(hold.MapId);

        hop.HoldMap = null;
    }
}
