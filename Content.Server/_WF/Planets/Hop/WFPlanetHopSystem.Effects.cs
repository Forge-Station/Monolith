using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.FtlEffects;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Planets.Hop;

public sealed partial class WFPlanetHopSystem
{
    /// <summary>How far past the hull a player may stand and still be sent an arriving ship early.</summary>
    private const float PreloadRange = 48f;

    private const string VisualizerProto = "FtlVisualizerEntity";

    private readonly SoundSpecifier _startupSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_begin.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    private readonly SoundSpecifier _travelSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_progress.ogg")
    {
        Params = AudioParams.Default.WithVolume(-3f).WithLoop(true),
    };

    private readonly SoundSpecifier _arrivalSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_end.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    private readonly HashSet<EntityUid> _convoy = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<EntityUid> _knock = new();

    private EntityQuery<StatusEffectsComponent> _statusQuery;
    private EntityQuery<MovedByPressureComponent> _pressureQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;

    private void InitializeEffects()
    {
        _statusQuery = GetEntityQuery<StatusEffectsComponent>();
        _pressureQuery = GetEntityQuery<MovedByPressureComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
    }

    /// <summary>Spool-up sound and ripple, engines held to forward thrust, and the departing shield field dropped.</summary>
    private void BeginWarmEffects(Entity<ShuttleComponent> hull, WFPlanetHopComponent hop)
    {
        _thruster.DisableLinearThrusters(hull.Comp);
        _thruster.EnableLinearThrustDirection(hull.Comp, DirectionFlag.North);
        _thruster.SetAngularThrust(hull.Comp, false);

        var audio = _audio.PlayPvs(_startupSound, hull.Owner);
        _audio.SetGridAudio(audio);
        hop.StartupStream = audio?.Entity;

        _departure.Publish(hull.Owner, hop.Started, hop.PhaseEnd, false, false);

        _convoy.Clear();
        _shuttle.GetAllDockedShuttles(hull.Owner, _convoy);

        foreach (var grid in _convoy)
        {
            _shields.SuppressWolfgateShieldForFtl(grid);
        }
    }

    /// <summary>Keeps the ripple on whoever is docked now and takes it off a ship that cast off during the warm-up.</summary>
    private void KeepWarmConvoy(EntityUid hull, WFPlanetHopComponent hop)
    {
        _departure.Publish(hull, hop.Started, hop.PhaseEnd, false, false);

        _convoy.Clear();
        _shuttle.GetAllDockedShuttles(hull, _convoy);
        _stale.Clear();

        var effects = EntityQueryEnumerator<FtlDepartureComponent>();

        while (effects.MoveNext(out var uid, out var effect))
        {
            if (effect.Lead == hull && !_convoy.Contains(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            RemComp<FtlDepartureComponent>(uid);
        }
    }

    /// <summary>Records the hop's travel leg on the hull, for the approach overlay; skipped for bodies outside a star system.</summary>
    private void MarkApproach(EntityUid grid, WFPlanetHopComponent hop, Vector2 shipPosition)
    {
        if (hop.Body is not { } body
            || !TryComp<StarSystemMapComponent>(Transform(body).MapUid, out var starMap)
            || starMap.System is not { } system)
        {
            return;
        }

        var comp = EnsureComp<WFPlanetApproachComponent>(grid);
        comp.System = system;
        comp.PlanetPosition = _transform.GetWorldPosition(body);
        comp.ShipPosition = shipPosition;
        comp.Arriving = hop.Arriving;
        comp.Start = hop.PhaseEnd;
        comp.End = comp.Start + TimeSpan.FromSeconds(TravelTime);
        Dirty(grid, comp);
    }

    /// <summary>Gives a grid riding the hop the hull's approach mark, so its own crew sees the approach too.</summary>
    private void CopyApproach(EntityUid hull, EntityUid rider)
    {
        if (!TryComp<WFPlanetApproachComponent>(hull, out var from))
            return;

        var comp = EnsureComp<WFPlanetApproachComponent>(rider);
        comp.System = from.System;
        comp.PlanetPosition = from.PlanetPosition;
        comp.ShipPosition = from.ShipPosition;
        comp.Arriving = from.Arriving;
        comp.Start = from.Start;
        comp.End = from.End;
        Dirty(rider, comp);
    }

    /// <summary>Puts everyone unbuckled on the hull's deck on the floor, as a jump does.</summary>
    private void Knock(EntityUid grid)
    {
        _knock.Clear();
        var children = Transform(grid).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!_statusQuery.HasComp(child)
                || _pressureQuery.TryComp(child, out var moved) && !moved.Enabled
                || _buckleQuery.TryComp(child, out var buckle) && buckle.Buckled)
            {
                continue;
            }

            _knock.Add(child);
        }

        var time = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.HyperspaceKnockdownTime));

        foreach (var child in _knock)
        {
            if (!HasComp<FTLKnockdownImmuneComponent>(child))
                _stuns.TryKnockdown(child, time, true, _statusQuery.GetComponent(child));
        }
    }

    /// <summary>Leaves the tail of the spool-up sound where the hull was, so it doesn't cut off when the grid goes.</summary>
    private void LeaveStartupTail(EntityUid hull, WFPlanetHopComponent hop, EntityUid? fromMap)
    {
        if (fromMap == null || !TryComp(hop.StartupStream, out AudioComponent? startupAudio))
            return;

        var tail = _audio.PlayStatic(_startupSound, Filter.Broadcast(),
            new EntityCoordinates(fromMap.Value, _maps.GetGridPosition(hull)), true, startupAudio.Params);

        _audio.SetPlaybackPosition(tail, StartupTime);

        if (tail != null)
            tail.Value.Component.Flags |= AudioFlags.NoOcclusion;
    }

    /// <summary>The swirl at the arrival point that everyone near it sees while the hull is on its way.</summary>
    private void SpawnVisualizer(EntityUid hull, WFPlanetHopComponent hop)
    {
        if (!Exists(hop.TargetMap))
            return;

        var visualizer = SpawnAttachedTo(VisualizerProto, new EntityCoordinates(hop.TargetMap, hop.Target));
        var visuals = Comp<FtlVisualizerComponent>(visualizer);
        visuals.Grid = hull;
        Dirty(visualizer, visuals);
        _transform.SetLocalRotation(visualizer, hop.Angle);
        _pvs.AddGlobalOverride(visualizer);
        hop.Visualizer = visualizer;
    }

    /// <summary>Sends the arriving ships to players near the landing before they drop out, so none pop in.</summary>
    private void Preload(EntityUid hull, WFPlanetHopComponent hop)
    {
        if (!TryComp<MapGridComponent>(hull, out var grid) || !TryComp<MapComponent>(hop.TargetMap, out var map))
            return;

        var reach = PreloadRange + grid.LocalAABB.Center.Length() + grid.LocalAABB.Size.Length() / 2f;

        foreach (var session in _players.Sessions)
        {
            if (hop.Preloaded.Contains(session) || session.AttachedEntity is not { } player)
                continue;

            var xform = Transform(player);

            if (xform.MapID != map.MapId
                || (_transform.GetWorldPosition(xform) - hop.Target).LengthSquared() > reach * reach)
            {
                continue;
            }

            hop.Preloaded.Add(session);

            if (hop.PreloadedGrids.Count == 0)
            {
                hop.PreloadedGrids.Add(hull);

                foreach (var rider in hop.Riders)
                {
                    hop.PreloadedGrids.Add(rider.Grid);
                }
            }

            foreach (var member in hop.PreloadedGrids)
            {
                _pvs.AddSessionOverride(member, session);
            }
        }
    }

    private void Unpreload(WFPlanetHopComponent hop)
    {
        foreach (var session in hop.Preloaded)
        {
            foreach (var member in hop.PreloadedGrids)
            {
                if (Exists(member))
                    _pvs.RemoveSessionOverride(member, session);
            }
        }

        hop.Preloaded.Clear();
        hop.PreloadedGrids.Clear();
    }
}
