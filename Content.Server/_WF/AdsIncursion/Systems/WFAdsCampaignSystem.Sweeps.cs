using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>Space free of grids around a spawn point and a route point.</summary>
    private const float SpawnClearance = 200f;
    private const float RouteClearance = 300f;

    /// <summary>How near a route point a sweep must come.</summary>
    private const float RouteRange = 200f;

    /// <summary>How far a route point that is not clear is moved to find clear space.</summary>
    private const float NudgeMin = 500f;
    private const float NudgeMax = 1500f;

    /// <summary>How long a lead may be outside held space and the ring round it before it is called back.</summary>
    private static readonly TimeSpan LeashDelay = TimeSpan.FromSeconds(10);

    /// <summary>One sweep per this many players, campaign-wide, within the cap.</summary>
    private const int PlayersPerSweep = 6;
    private const int MaxSweeps = 4;

    /// <summary>How soon a node tries again after a sweep that could not be sent.</summary>
    private static readonly TimeSpan SweepRetry = TimeSpan.FromMinutes(1);

    /// <summary>Sends sweeps from nodes below their tier's count, within the campaign-wide cap.</summary>
    private void TickSweeps(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        var sweeps = Sweeps();
        var cap = SweepCap();
        var each = ByTier(campaign.Sweeps, state.Tier, 0);
        var active = sweeps.Count;
        foreach (var node in Nodes().OrderBy(entry => entry.Comp1.NextSweep))
        {
            if (active >= cap)
                return;

            if (now < node.Comp1.NextSweep || sweeps.Count(sweep => sweep.Comp1.Node == node.Owner) >= each)
                continue;

            if (TryLaunchSweep(node.Owner))
                active++;
            else
                node.Comp1.NextSweep = now + SweepRetry;
        }
    }

    /// <summary>The campaign-wide sweep cap for the players on.</summary>
    private int SweepCap()
    {
        return Math.Clamp(Players() / PlayersPerSweep, 1, MaxSweeps);
    }

    /// <summary>Whether a node may send another sweep under its tier's count and the campaign-wide cap.</summary>
    private bool SweepRoom(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, EntityUid node,
        List<Entity<WFAdsSweepComponent, WFEncounterComponent>> sweeps)
    {
        return sweeps.Count < SweepCap() && sweeps.Count(sweep => sweep.Comp1.Node == node) < ByTier(campaign.Sweeps, state.Tier, 0);
    }

    /// <summary>Sends a sweep from a node now, whatever the caps, through held cells and home.</summary>
    public bool TryLaunchSweep(EntityUid node)
    {
        return TryLaunchSweep(node, out _);
    }

    /// <summary>Sends a sweep from a node now, whatever the caps, through held cells and home; gives its encounter.</summary>
    private bool TryLaunchSweep(EntityUid node, out EntityUid uid)
    {
        uid = default;
        if (Campaign is not { } campaign || State() is { Finished: true }
            || !TryComp<WFAdsNodeComponent>(node, out var comp)
            || !TryComp<WFEncounterComponent>(node, out var encounter) || encounter.Resolution != null
            || !_prototypes.TryIndex(campaign.Sweep, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = Transform(node).MapID;
        if (!TryClearPoint(map, Position(encounter, comp.Lead), LaunchMin, LaunchMax, SpawnClearance, out var home))
            return false;

        var route = BuildRoute(campaign, map, comp.Cell);
        if (route.Count == 0)
            return false;

        if (!SpawnEncounter(prototype, new MapCoordinates(home, map), out uid, offBudget: true))
            return false;

        var lead = prototype.Ships[0].Key;
        var sweep = new WFAdsSweepComponent { Node = node, Lead = lead, Route = route, Home = home, LastSeen = home };
        if (!_encounters.SetOrders(uid, lead, RouteOrders(sweep, route)))
        {
            _encounters.End(uid);
            return false;
        }

        var added = AddComp<WFAdsSweepComponent>(uid);
        added.Node = node;
        added.Lead = lead;
        added.Route = route;
        added.Home = home;
        added.LastSeen = home;
        comp.NextSweep = _timing.CurTime + campaign.SweepInterval;
        return true;
    }

    /// <summary>A random walk of three to five held cells from a start cell, each centre clear or nudged clear, else skipped.</summary>
    private List<Vector2> BuildRoute(WFAdsCampaignPrototype campaign, MapId map, WFSectorCell start)
    {
        var route = new List<Vector2>();
        var visited = new HashSet<WFSectorCell> { start };
        var current = start;
        var steps = _random.Next(3, 6);
        var next = new List<WFSectorCell>();
        for (var i = 0; i < steps; i++)
        {
            next.Clear();
            for (var direction = 0; direction < 6; direction++)
            {
                var neighbour = current.Neighbour(direction);
                if (_territory.Owner(map, neighbour) == campaign.Faction)
                    next.Add(neighbour);
            }

            if (next.Count == 0)
                break;

            var fresh = next.Where(cell => !visited.Contains(cell)).ToList();
            current = _random.Pick(fresh.Count > 0 ? fresh : next);
            visited.Add(current);
            if (ClearRoutePoint(map, WFSectorHex.Centre(current, _territory.CellSize)) is { } point)
                route.Add(point);
        }

        return route;
    }

    /// <summary>A route point, or a clear one near it, or null.</summary>
    private Vector2? ClearRoutePoint(MapId map, Vector2 point)
    {
        if (_scheduler.IsClearSpace(map, point, RouteClearance, 0f))
            return point;

        for (var i = 0; i < 4; i++)
        {
            var nudged = point + _random.NextAngle().ToVec() * _random.NextFloat(NudgeMin, NudgeMax);
            if (_scheduler.IsClearSpace(map, nudged, RouteClearance, 0f))
                return nudged;
        }

        return null;
    }

    /// <summary>Orders to call at route points, then home.</summary>
    private static List<WFCrewObjective> RouteOrders(WFAdsSweepComponent sweep, List<Vector2> route)
    {
        var orders = route.Select(point => new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = point, Range = RouteRange })
            .ToList();
        orders.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = sweep.Home, Range = RouteRange });
        return orders;
    }

    /// <summary>Runs <see cref="TendSweeps"/> and <see cref="TendInterdicts"/> once, as a tick does.</summary>
    public void CheckSweeps()
    {
        if (State() is not { } state)
            return;

        TendSweeps(state);
        TendInterdicts(state);
    }

    /// <summary>
    /// Breaks sweeps whose lead is out of the fight, notes where each lead is, for the cell it dies in, and calls a
    /// sweep whose lead has been outside held space and the ring round it for ten seconds back to its route.
    /// </summary>
    private void TendSweeps(WFAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        var held = CampaignFor(state) is { } campaign ? HeldSet(campaign) : new HashSet<WFSectorCell>();
        foreach (var sweep in Sweeps())
        {
            var (uid, comp, encounter) = sweep;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                BreakLead(uid, encounter, comp.Lead);
                continue;
            }

            comp.LastSeen = _transform.GetWorldPosition(lead.Grid);
            if (held.Count == 0 || InOrBeside(held, _territory.GridCell(lead.Grid)))
            {
                comp.OutsideSince = null;
                continue;
            }

            comp.OutsideSince ??= now;
            if (now - comp.OutsideSince < LeashDelay)
                continue;

            comp.OutsideSince = null;
            var orders = RouteOrders(comp, comp.Interdicting != null ? comp.Resume.ToList() : RemainingRoute(sweep));
            if (!_encounters.SetOrders(uid, comp.Lead, orders))
                continue;

            Log.Info($"ADS sweep {ToPrettyString(uid)} strayed from held space and was called back.");
            DropInterdict(comp);
        }
    }

    /// <summary>The route points a sweep has still to call at, read from its lead's orders.</summary>
    private List<Vector2> RemainingRoute(Entity<WFAdsSweepComponent, WFEncounterComponent> sweep)
    {
        var (_, comp, encounter) = sweep;
        if (!encounter.Ships.TryGetValue(comp.Lead, out var lead))
            return new List<Vector2>();

        var grid = GetNetEntity(lead.Grid);
        var row = _objectives.Snapshot().FirstOrDefault(crew => crew.Grid == grid && crew.Group == lead.Group);
        if (row == null)
            return new List<Vector2>();

        return row.Objectives
            .Where(order => order.Kind == WFCrewObjectiveKind.GoTo && Vector2.DistanceSquared(order.Position, comp.Home) > 1f)
            .Select(order => order.Position)
            .ToList();
    }

    /// <summary>Lets its node send another; destroyed, it frees the cell its lead died in, unless a node stands there, and pays its bounty.</summary>
    private void SweepResolved(Entity<WFAdsSweepComponent> sweep, WFEncounterResolution resolution)
    {
        var (uid, comp) = sweep;
        var state = State();
        var campaign = CampaignFor(state);
        var now = _timing.CurTime;
        if (campaign != null && TryComp<WFAdsNodeComponent>(comp.Node, out var home))
            home.NextSweep = home.NextSweep > now + campaign.SweepInterval ? home.NextSweep : now + campaign.SweepInterval;

        DropInterdict(comp);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return;

        var map = Transform(uid).MapID;
        var position = encounter.Ships.TryGetValue(comp.Lead, out var lead) && !TerminatingOrDeleted(lead.Grid)
            ? _transform.GetWorldPosition(lead.Grid)
            : comp.LastSeen;
        var cell = WFSectorHex.CellOf(position, _territory.CellSize);
        var guarded = Nodes().Any(node => node.Comp1.Cell == cell) || Pickets().Any(picket => picket.Comp1.Cell == cell);
        var freed = !guarded && _territory.Owner(map, cell) == campaign.Faction && _territory.Free(map, cell);
        var (bounty, paid) = PaySweepBounty(campaign, uid, "destroying the ADS sweep");
        state.SweepsDestroyed++;
        AnnounceRadio(Loc.GetString(freed ? "wf-ads-sweep-destroyed" : "wf-ads-sweep-destroyed-held",
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", bounty),
            ("paid", paid)));
        Recompute();
    }
}
