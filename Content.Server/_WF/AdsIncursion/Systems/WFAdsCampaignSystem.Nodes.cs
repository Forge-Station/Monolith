using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.SectorControl;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>How far from the origin the first node and a regrouped one are founded.</summary>
    private const float FirstMinDistance = 16000f;
    private const float FirstMaxDistance = 26000f;

    /// <summary>How far from any living player the first node and a regrouped one are founded.</summary>
    private const float PlayerClearance = 8000f;

    /// <summary>Space free of grids around a node's centre.</summary>
    private const float GridClearance = 600f;

    /// <summary>Distance kept from stations by the first node and by later ones.</summary>
    private const float FirstStationClearance = 6000f;
    private const float LaterStationClearance = 3000f;

    /// <summary>How many cells from any cell another faction holds the first node and a regrouped one keep.</summary>
    private const int RivalClearance = 3;

    /// <summary>How long after its founding a node may send its first sweep.</summary>
    private static readonly TimeSpan FirstSweepDelay = TimeSpan.FromMinutes(2);

    /// <summary>How long a node may sit out of power unattacked before it is broken anyway.</summary>
    public static readonly TimeSpan StarvedGrace = TimeSpan.FromMinutes(20);

    /// <summary>Founds the first node or a regrouped one far out, or deploys a later one from a standing node; one founding at a time.</summary>
    private void TickFounding(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        var standing = Nodes();
        var deployments = Deployments().Count;
        if (standing.Count == 0)
        {
            if (state.Founded > 0)
                state.NoNodeSince ??= now;

            if (deployments == 0 && FoundingDue(campaign, state) is { } due && now >= due && now >= _nextFoundTry && !TryFound(out _))
                _nextFoundTry = now + FoundRetry;
            return;
        }

        state.NoNodeSince = null;
        if (deployments > 0
            || standing.Count + deployments >= campaign.MaxNodes
            || _territory.Held(_territory.SectorMap, campaign.Faction).Count < campaign.CellsPerNode * standing.Count
            || state.LastFounded is { } last && now < last + campaign.DeployInterval
            || now < _nextFoundTry)
            return;

        if (!TryLaunchDeployment(out _))
            _nextFoundTry = now + FoundRetry;
    }

    /// <summary>When a founding is due with none standing: the first after the stations generate, a regroup after the last node fell; null if that clock has not started.</summary>
    public TimeSpan? FoundingDue(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent? state)
    {
        if (state is { Finished: true })
            return null;

        if (state == null || state.Founded == 0)
            return _stationsAt is { } at ? at + campaign.FirstNodeDelay : null;

        return state.NoNodeSince is { } since ? since + campaign.RegroupDelay : null;
    }

    /// <summary>Founds a node at once where the rules put it: a quiet cell far out with none standing, otherwise a held frontier cell. The campaign deploys to the latter.</summary>
    public bool TryFound(out EntityUid node)
    {
        node = default;
        if (Campaign is not { } campaign || State(true) is not { Finished: false })
            return false;

        var standing = Nodes();
        if (standing.Count == 0)
        {
            return TryFirstCell(campaign, out var first)
                && TryFoundAt(_territory.Centre(_territory.SectorMap, first), out node);
        }

        return TryLaterCell(campaign, Occupied(standing), out var later)
            && TryFoundAt(_territory.Centre(_territory.SectorMap, later), out node);
    }

    /// <summary>Founds a node at a point, claims its cell and ring and announces it.</summary>
    public bool TryFoundAt(MapCoordinates at, out EntityUid node)
    {
        node = default;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } state
            || !_prototypes.TryIndex(campaign.Node, out var prototype) || prototype.Ships.Count == 0
            || HasStation(at.MapId, _territory.CellOf(at)))
            return false;

        var regroup = state.Founded > 0 && Nodes().Count == 0;
        if (!SpawnEncounter(prototype, at, out var uid))
            return false;

        Establish(campaign, state, uid, prototype.Ships[0].Key, _territory.CellOf(at), at,
            regroup ? "wf-ads-node-regrouped" : "wf-ads-node-founded");
        node = uid;
        return true;
    }

    /// <summary>Makes an encounter a node in a cell: claims the cell and ring, counts the founding and announces it at a point with a string.</summary>
    private void Establish(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, EntityUid uid, string lead,
        WFSectorCell cell, MapCoordinates at, string announcement)
    {
        var now = _timing.CurTime;
        var comp = EnsureComp<WFAdsNodeComponent>(uid);
        comp.Cell = cell;
        comp.Tier = state.Tier;
        comp.Lead = lead;
        comp.NextSweep = now + FirstSweepDelay;
        comp.NextSentinel = now + campaign.SentinelInterval;

        ClaimCells(campaign, at.MapId, cell, uid);
        state.Founded++;
        state.LastFounded = now;
        state.NoNodeSince = null;
        state.Defeated = false;
        Recompute();
        AnnouncePlace(campaign, announcement, true, at.MapId, at.Position,
            ("callsign", WFSectorHex.Callsign(cell)),
            ("bounty", ByTier(campaign.NodeBounty, state.Tier, 0)));
    }

    /// <summary>Claims a node's cell and ring for it; a cell with a station in it is skipped.</summary>
    private void ClaimCells(WFAdsCampaignPrototype campaign, MapId map, WFSectorCell cell, EntityUid node)
    {
        var cells = new List<WFSectorCell> { cell };
        cells.AddRange(WFSectorHex.Ring(cell, 1));
        foreach (var claimed in cells)
        {
            if (!HasStation(map, claimed))
                _territory.TryClaim(map, claimed, campaign.Faction, node);
        }
    }

    /// <summary>Claims each standing node's cells again after sector control was switched back on or resized, and re-aims the deployments and interdictors.</summary>
    private void OnSectorReset(ref WFSectorResetEvent args)
    {
        if (Campaign is not { } campaign)
            return;

        var map = _territory.SectorMap;
        foreach (var (uid, comp, encounter) in Nodes())
        {
            comp.Cell = _territory.CellOf(new MapCoordinates(Position(encounter, comp.Lead), map));
            ClaimCells(campaign, map, comp.Cell, uid);
        }

        foreach (var (_, comp, _) in Deployments())
        {
            comp.Target = _territory.CellOf(new MapCoordinates(comp.TargetPosition, map));
        }

        // A reset wiped the challenged cells' holders, so there is nothing left to challenge; a picket takes its cell back.
        foreach (var (uid, _, _) in Interdictors())
        {
            _encounters.End(uid);
        }

        foreach (var (uid, comp, encounter) in Pickets())
        {
            comp.Cell = _territory.CellOf(new MapCoordinates(Position(encounter, comp.Lead), map));
            if (!HasStation(map, comp.Cell))
                _territory.TryClaim(map, comp.Cell, campaign.Faction, uid);
        }
    }

    /// <summary>
    /// A random quiet, claimable cell 16 to 26 km out with no station in it, its centre clear, 8 km from any living
    /// player and three cells from any cell another faction holds.
    /// </summary>
    private bool TryFirstCell(WFAdsCampaignPrototype campaign, out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var quietSince = _timing.CurTime - TimeSpan.FromMinutes(campaign.QuietMinutes);
        var players = PlayerPositions(map);
        var rivals = new List<WFSectorCell>();
        if (_territory.Territory(map) is { } territory)
        {
            foreach (var (held, claim) in territory.Claims)
            {
                if (claim.Faction != campaign.Faction)
                    rivals.Add(held);
            }
        }

        var rings = (int) (FirstMaxDistance / (_territory.CellSize * 1.5f)) + 2;
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in WFSectorHex.Within(WFSectorCell.Origin, rings))
        {
            var centre = WFSectorHex.Centre(candidate, _territory.CellSize);
            var distance = centre.Length();
            if (distance < FirstMinDistance || distance > FirstMaxDistance
                || _territory.Owner(map, candidate) != null || _territory.ContestOf(map, candidate) != null
                || !_activity.IsQuiet(map, candidate, quietSince)
                || rivals.Any(rival => WFSectorHex.Distance(rival, candidate) < RivalClearance)
                || players.Any(player => Vector2.DistanceSquared(player, centre) < PlayerClearance * PlayerClearance)
                || !_territory.CanClaim(map, candidate, campaign.Faction))
                continue;

            candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates)
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, FirstStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>The cells of the standing nodes and of the deployments' targets, which later nodes keep two cells from.</summary>
    private List<WFSectorCell> Occupied(List<Entity<WFAdsNodeComponent, WFEncounterComponent>> standing)
    {
        var cells = standing.Select(node => node.Comp1.Cell).ToList();
        cells.AddRange(Deployments().Select(deployment => deployment.Comp1.Target));
        return cells;
    }

    /// <summary>A held cell on the edge of held space with no station, two cells from every occupied one, with a clear centre; nearest the core first.</summary>
    private bool TryLaterCell(WFAdsCampaignPrototype campaign, List<WFSectorCell> occupied, out WFSectorCell cell)
    {
        cell = default;
        var map = _territory.SectorMap;
        var held = HeldSet(campaign);
        var candidates = new List<WFSectorCell>();
        foreach (var candidate in held)
        {
            var edge = false;
            for (var direction = 0; direction < 6; direction++)
            {
                if (!held.Contains(candidate.Neighbour(direction)))
                    edge = true;
            }

            if (edge && occupied.All(other => WFSectorHex.Distance(other, candidate) >= 2) && !HasStation(map, candidate))
                candidates.Add(candidate);
        }

        _random.Shuffle(candidates);
        foreach (var candidate in candidates.OrderBy(entry => WFSectorHex.Distance(entry, WFSectorCell.Origin)))
        {
            if (!_scheduler.IsClearSpace(map, WFSectorHex.Centre(candidate, _territory.CellSize), GridClearance, LaterStationClearance))
                continue;

            cell = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Runs <see cref="TendNodes"/> once, as a tick does.</summary>
    public void CheckNodes()
    {
        if (State() is { } state)
            TendNodes(state, Crewed(), 1f);
    }

    /// <summary>Breaks nodes whose lead is out of the fight, counts hits, sends the sentinels after a fresh attacker and brings them back, and times support.</summary>
    private void TendNodes(WFAdsCampaignComponent state, HashSet<EntityUid> ships, float elapsed)
    {
        foreach (var node in Nodes())
        {
            var (uid, comp, encounter) = node;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                // A hull dark only from lost power, with nobody shooting at it, is not beaten until the grace runs out.
                if (lead != null && encounter.ShipHits.Count == 0 && _encounters.OutForWantOfPower(lead))
                {
                    if (!comp.Starved)
                    {
                        Log.Info($"ADS node {ToPrettyString(uid)} is out of power with no attack on it and is left standing.");
                        comp.StarvedSince = _timing.CurTime;
                    }

                    comp.Starved = true;
                    if (_timing.CurTime - comp.StarvedSince < StarvedGrace)
                        continue;

                    Log.Info($"ADS node {ToPrettyString(uid)} has been out of power past the grace and is broken.");
                }

                _encounters.Resolve(uid, LostResolution(encounter, comp.Lead));
                continue;
            }

            comp.Starved = false;
            var hits = encounter.ShipHits.Values.Sum();
            if (hits > comp.Hits)
            {
                comp.LastHit = _timing.CurTime;
                ScatterSentinels(uid);
            }

            comp.Hits = hits;
            if (comp.Hits == 0)
                continue;

            if (comp.Scattered)
                RegroupSentinels(uid, false);

            if (_timing.CurTime - comp.LastHit <= SupportWindow)
                TrackSupport(comp, encounter, _transform.GetWorldPosition(lead.Grid), ships, elapsed);
        }
    }

    /// <summary>
    /// How an encounter whose lead is out of the fight ends: destroyed if the lead was wrecked, lost its crew or took
    /// hits from a player ship, else ended, so a pilot stall frees the cap but does not free ground or announce a kill.
    /// </summary>
    private WFEncounterResolution LostResolution(WFEncounterComponent encounter, string lead)
    {
        return encounter.ShipHits.Count == 0 && encounter.Ships.TryGetValue(lead, out var ship) && _encounters.OutForWantOfPilot(ship)
            ? WFEncounterResolution.Ended
            : WFEncounterResolution.Destroyed;
    }

    /// <summary>Ends an encounter whose lead is out of the fight: the rest of its ships turn for where it set out, and it resolves.</summary>
    private void BreakLead(EntityUid uid, WFEncounterComponent encounter, string lead)
    {
        foreach (var key in encounter.Ships.Keys)
        {
            if (key == lead)
                continue;

            _encounters.SetOrders(uid, key, new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.GoTo, Position = encounter.Origin.Position, Range = RouteRange },
            });
        }

        Log.Info($"ADS encounter {ToPrettyString(uid)} lost its lead and is broken.");
        _encounters.Resolve(uid, LostResolution(encounter, lead));
    }

    /// <summary>Frees a node's cells and ends its sentinels; broken, it pays its bounty and is announced.</summary>
    private void NodeResolved(Entity<WFAdsNodeComponent> node, WFEncounterResolution resolution)
    {
        var (uid, comp) = node;
        var state = State();
        var campaign = CampaignFor(state);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
        {
            SweepSentinels(node, false);
            _territory.Release(uid);
            AfterNodeGone(state);
            return;
        }

        var sentinels = AliveSentinels(comp);
        SweepSentinels(node, true);
        var (bounty, paid) = PayNodeBounty(campaign, state, (uid, comp, encounter), sentinels);
        var freed = _territory.Release(uid);
        state.Broken++;
        AfterNodeGone(state);
        AnnouncePlace(campaign, "wf-ads-node-broken", false, Transform(uid).MapID, Position(encounter, comp.Lead),
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid),
            ("cells", freed));
        AnnounceDefeat(campaign, state);
    }

    /// <summary>Announces the ADS gone dormant when the last node has broken with no deployment out to found another; a finished campaign has had its ending.</summary>
    private void AnnounceDefeat(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state)
    {
        if (state.Finished || state.Defeated || Nodes().Count > 0 || Deployments().Count > 0)
            return;

        state.Defeated = true;
        state.Defeats++;
        Announce(campaign, Loc.GetString("wf-ads-dormant"), false);
        Recompute();
    }

    /// <summary>Starts the regroup clock when the last node goes, and rescores.</summary>
    private void AfterNodeGone(WFAdsCampaignComponent? state)
    {
        if (state == null)
            return;

        if (Nodes().Count == 0 && state.Founded > 0)
            state.NoNodeSince ??= _timing.CurTime;

        if (Campaign != null)
            Recompute();
    }
}
