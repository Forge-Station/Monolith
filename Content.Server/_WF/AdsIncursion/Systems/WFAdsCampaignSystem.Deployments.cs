using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>How far from its source node's lead a deployment, a sweep or an interdictor is spawned.</summary>
    private const float LaunchMin = 600f;
    private const float LaunchMax = 1000f;

    /// <summary>How near its target point a deployment's lead is sent, and how near counts as arrived.</summary>
    private const float DeployRange = 300f;
    private const float DeployArrival = 400f;

    /// <summary>A contest deadline off by less than this is left alone, so the map is not resent every tick.</summary>
    private static readonly TimeSpan DeadlineStep = TimeSpan.FromSeconds(30);

    /// <summary>Sends a deployment to the held cell the rules pick next, unless one is already under way.</summary>
    public bool TryLaunchDeployment(out EntityUid deployment)
    {
        deployment = default;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } || Deployments().Count > 0)
            return false;

        var standing = Nodes();
        return standing.Count > 0
            && TryLaterCell(campaign, Occupied(standing), out var target)
            && TryLaunchDeployment(target, out deployment);
    }

    /// <summary>Sends a node from the standing node nearest a cell to found itself there: contests the cell and announces it.</summary>
    public bool TryLaunchDeployment(WFSectorCell target, out EntityUid deployment)
    {
        deployment = default;
        var map = _territory.SectorMap;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } || HasStation(map, target)
            || !_prototypes.TryIndex(campaign.Deployment, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var centre = WFSectorHex.Centre(target, _territory.CellSize);
        foreach (var source in Nodes().OrderBy(entry => Vector2.DistanceSquared(Position(entry.Comp2, entry.Comp1.Lead), centre)))
        {
            if (!TryClearPoint(map, Position(source.Comp2, source.Comp1.Lead), LaunchMin, LaunchMax, SpawnClearance, out var start))
                continue;

            if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
                return false;

            var encounter = Comp<WFEncounterComponent>(uid);
            var lead = prototype.Ships[0].Key;
            var goal = ClearRoutePoint(map, centre) ?? centre;
            if (!_encounters.SetOrders(uid, lead, TravelOrders(goal, DeployRange)))
            {
                _encounters.End(uid);
                return false;
            }

            encounter.Leash = 0f;
            var comp = AddComp<WFAdsDeploymentComponent>(uid);
            comp.Target = target;
            comp.TargetPosition = goal;
            comp.Source = source.Owner;
            comp.Lead = lead;
            comp.Launched = _timing.CurTime;
            comp.Overdue = _timing.CurTime + FlightTime(start, goal) * 2 + FlightSlack;
            _territory.Contest(map, target, campaign.Faction, _timing.CurTime + FlightTime(start, goal) + FlightSlack, uid);

            var (station, bearing, range) = Place(map, goal);
            AnnounceQuiet(campaign, Loc.GetString("wf-ads-deployment-launched",
                ("callsign", WFSectorHex.Callsign(target)),
                ("station", station),
                ("bearing", bearing),
                ("range", range)), true);
            Log.Info($"ADS deployment {ToPrettyString(uid)} launched from {ToPrettyString(source.Owner)} for {WFSectorHex.Callsign(target)}.");
            deployment = uid;
            return true;
        }

        return false;
    }

    /// <summary>The orders that fly a lead to a point.</summary>
    private static List<WFCrewObjective> TravelOrders(Vector2 goal, float range)
    {
        return new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.GoTo, Position = goal, Range = range } };
    }

    /// <summary>Stops every deployment whose lead is out of the fight, founds those that have arrived and keeps their targets contested meanwhile.</summary>
    public void CheckDeployments()
    {
        if (State() is { } state)
            TendDeployments(state, true);
    }

    /// <summary>
    /// Stops deployments whose lead is out of the fight; with <paramref name="found"/>, founds those that have arrived,
    /// or are overdue in their target cell, and ends those overdue elsewhere; keeps each target contested meanwhile.
    /// </summary>
    private void TendDeployments(WFAdsCampaignComponent state, bool found)
    {
        var campaign = CampaignFor(state);
        foreach (var deployment in Deployments())
        {
            var (uid, comp, encounter) = deployment;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, WFEncounterResolution.Destroyed);
                continue;
            }

            if (campaign == null)
                continue;

            var at = _transform.GetWorldPosition(lead.Grid);
            if (found)
            {
                var inCell = _territory.CellOf(new MapCoordinates(at, Transform(uid).MapID)) == comp.Target;
                var overdue = _timing.CurTime >= comp.Overdue;
                if (Vector2.Distance(at, comp.TargetPosition) <= DeployArrival || inCell && (lead.Flown || overdue))
                {
                    CompleteDeployment(uid);
                    continue;
                }

                if (overdue)
                {
                    Log.Info($"ADS deployment {ToPrettyString(uid)} ran past its time short of {WFSectorHex.Callsign(comp.Target)} and was ended.");
                    _encounters.End(uid);
                    continue;
                }

                if (lead.Flown)
                    _encounters.SetOrders(uid, comp.Lead, TravelOrders(comp.TargetPosition, DeployRange));
            }

            HoldDeploymentContest(campaign, deployment, at);
        }
    }

    /// <summary>Contests a deployment's target again if the contest went, or moves its deadline to the reckoned arrival.</summary>
    private void HoldDeploymentContest(WFAdsCampaignPrototype campaign, Entity<WFAdsDeploymentComponent, WFEncounterComponent> deployment, Vector2 at)
    {
        var (uid, comp, _) = deployment;
        var map = Transform(uid).MapID;
        if (_territory.IsDisputed(map, comp.Target))
            return;

        var deadline = _timing.CurTime + FlightTime(at, comp.TargetPosition) + FlightSlack;
        if (_territory.ContestOf(map, comp.Target) is not { } contest)
            _territory.Contest(map, comp.Target, campaign.Faction, deadline, uid);
        else if (contest.Source == uid && (contest.Deadline - deadline).Duration() >= DeadlineStep)
            _territory.SetContestDeadline(map, comp.Target, deadline);
    }

    /// <summary>Makes a deployment the node it set out to be: drops the contest, claims the cell and ring, holds the lead where it stands and announces it.</summary>
    public bool CompleteDeployment(EntityUid deployment)
    {
        if (State(true) is not { } state || CampaignFor(state) is not { } campaign
            || !TryComp<WFAdsDeploymentComponent>(deployment, out var comp)
            || !TryComp<WFEncounterComponent>(deployment, out var encounter) || encounter.Resolution != null)
            return false;

        var map = Transform(deployment).MapID;
        var target = comp.Target;
        var lead = comp.Lead;
        var where = comp.TargetPosition;
        if (encounter.Ships.TryGetValue(lead, out var standing) && !TerminatingOrDeleted(standing.Grid))
        {
            var here = _transform.GetWorldPosition(standing.Grid);
            if (Vector2.Distance(here, where) > DeployArrival && _territory.CellOf(new MapCoordinates(here, map)) == target)
                where = here;
        }

        var at = new MapCoordinates(where, map);
        RemComp<WFAdsDeploymentComponent>(deployment);
        if (_territory.ContestOf(map, target) is { } contest && contest.Source == deployment)
            _territory.DropContest(map, target);

        // Its station is where it stopped: the node's leash holds it there from now on.
        encounter.Origin = at;
        encounter.Leash = _prototypes.TryIndex(campaign.Node, out var node) ? node.Leash : 0f;
        _encounters.SetOrders(deployment, lead, new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } });

        Establish(campaign, state, deployment, lead, target, at, "wf-ads-deployment-arrived");
        if (TryComp<WFAdsNodeComponent>(deployment, out var established))
        {
            // Hits taken on the way are not a fresh attack on the node.
            established.Hits = encounter.ShipHits.Values.Sum();
            established.LastHit = _timing.CurTime - SupportWindow - TimeSpan.FromSeconds(1);
        }

        Log.Info($"ADS deployment {ToPrettyString(deployment)} founded a node at {WFSectorHex.Callsign(target)}.");
        return true;
    }

    /// <summary>Withdraws a deployment's contest; destroyed, it pays half a node's bounty and is announced.</summary>
    private void DeploymentResolved(Entity<WFAdsDeploymentComponent> deployment, WFEncounterResolution resolution)
    {
        var (uid, comp) = deployment;
        var state = State();
        var campaign = CampaignFor(state);
        _territory.Release(uid);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null)
            return;

        var (bounty, paid) = PayDeploymentBounty(campaign, state, uid);
        state.DeploymentsStopped++;
        AnnounceQuiet(campaign, Loc.GetString("wf-ads-deployment-stopped",
            ("callsign", WFSectorHex.Callsign(comp.Target)),
            ("bounty", bounty),
            ("paid", paid)), false);
        AnnounceDefeat(campaign, state);
        Recompute();
    }
}
