using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server._WF.Planets;

/// <summary>Marks a hull and the grids that ride its hop for the planet approach their crews' clients draw over the hop into or out of orbit.</summary>
public sealed partial class WFOrbitEntrySystem
{
    /// <summary>Last resort for a mark whose hull is stuck in transit.</summary>
    private static readonly TimeSpan ApproachTimeout = TimeSpan.FromMinutes(1);

    private readonly List<EntityUid> _approachDone = new();

    /// <summary>Records the hop's travel leg on the hull; skipped for bodies outside a star system.</summary>
    public void MarkApproach(EntityUid grid, EntityUid body, bool arriving)
    {
        if (!TryComp<StarSystemMapComponent>(Transform(body).MapUid, out var starMap) || starMap.System is not { } system)
            return;

        var comp = EnsureComp<WFPlanetApproachComponent>(grid);
        comp.Hull = grid;
        comp.System = system;
        comp.PlanetPosition = _transform.GetWorldPosition(body);
        comp.ShipPosition = _transform.GetWorldPosition(grid);
        comp.Arriving = arriving;
        comp.Start = _timing.CurTime + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitStartupTime);
        comp.End = comp.Start + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitTravelTime);
        Dirty(grid, comp);
    }

    /// <summary>Copies the hull's mark to the grids the drive linked to it on the FTL map, which are exactly the ones riding the hop.</summary>
    private void OnApproachStarted(Entity<WFPlanetApproachComponent> ent, ref FTLStartedEvent args)
    {
        // The overlay reads the grid the local player stands on, so docked crews need the mark too.
        var query = EntityQueryEnumerator<FTLComponent>();

        while (query.MoveNext(out var uid, out var ftl))
        {
            if (ftl.LinkedShuttle != ent.Owner)
                continue;

            var comp = EnsureComp<WFPlanetApproachComponent>(uid);
            comp.Hull = ent.Comp.Hull;
            comp.System = ent.Comp.System;
            comp.PlanetPosition = ent.Comp.PlanetPosition;
            comp.ShipPosition = ent.Comp.ShipPosition;
            comp.Arriving = ent.Comp.Arriving;
            comp.Start = ent.Comp.Start;
            comp.End = ent.Comp.End;
            Dirty(uid, comp);
        }
    }

    /// <summary>Drops the hop's marks on the arrival tick, so clients get the move off the FTL map and the removal in one state.</summary>
    private void OnApproachCompleted(Entity<WFPlanetApproachComponent> ent, ref FTLCompletedEvent args)
    {
        _approachDone.Clear();
        _approachDone.Add(ent.Owner);

        var query = EntityQueryEnumerator<WFPlanetApproachComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Hull == ent.Owner && uid != ent.Owner)
                _approachDone.Add(uid);
        }

        foreach (var uid in _approachDone)
        {
            RemComp<WFPlanetApproachComponent>(uid);
        }
    }

    /// <summary>Fallback for a hop that never arrives: drops marks whose hull is gone or no longer in transit.</summary>
    private void SweepApproaches()
    {
        _approachDone.Clear();

        var query = EntityQueryEnumerator<WFPlanetApproachComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!InTransit(comp.Hull) || _timing.CurTime > comp.End + ApproachTimeout)
                _approachDone.Add(uid);
        }

        foreach (var uid in _approachDone)
        {
            RemComp<WFPlanetApproachComponent>(uid);
        }
    }

    /// <summary>True while the hull's FTL is spooling up, travelling or arriving.</summary>
    private bool InTransit(EntityUid hull)
    {
        return TryComp<FTLComponent>(hull, out var ftl)
            && (ftl.State & (FTLState.Starting | FTLState.Travelling | FTLState.Arriving)) != 0;
    }
}
