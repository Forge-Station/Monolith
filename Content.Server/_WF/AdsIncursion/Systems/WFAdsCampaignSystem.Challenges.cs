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
    /// <summary>How near its target point an interdictor is sent, and how near counts as arrived.</summary>
    private const float ChallengeRange = 300f;
    private const float ChallengeArrival = 400f;

    /// <summary>How far from ADS space a challenged cell may lie when none is adjacent to it.</summary>
    private const int ChallengeReach = 4;

    /// <summary>Starts a challenge every interval from the challenge tier, while fewer than the cap are out.</summary>
    private void TickChallenges(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        if (state.NextChallenge is not { } next)
        {
            state.NextChallenge = now + campaign.ChallengeInterval;
            return;
        }

        if (now < next)
            return;

        state.NextChallenge = now + campaign.ChallengeInterval;
        if (state.Tier < campaign.ChallengeTier || Interdictors().Count >= campaign.MaxChallenges)
            return;

        TryChallenge(out _);
    }

    /// <summary>Challenges the cell the rules pick: held by another faction, adjacent to ADS space if one is, else the nearest within four cells of it.</summary>
    public bool TryChallenge(out EntityUid interdictor)
    {
        interdictor = default;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } || Nodes().Count == 0)
            return false;

        foreach (var cell in ChallengeCells(campaign))
        {
            if (TryChallenge(cell, out interdictor))
                return true;
        }

        return false;
    }

    /// <summary>The cells the ADS may challenge, best first.</summary>
    private List<WFSectorCell> ChallengeCells(WFAdsCampaignPrototype campaign)
    {
        var map = _territory.SectorMap;
        var held = HeldSet(campaign);
        if (held.Count == 0 || _territory.Territory(map) is not { } territory)
            return new List<WFSectorCell>();

        var nodes = Nodes().Select(node => node.Comp1.Cell).ToList();
        var adjacent = new List<(WFSectorCell Cell, int Distance)>();
        var near = new List<(WFSectorCell Cell, int Distance)>();
        foreach (var (cell, claim) in territory.Claims)
        {
            if (claim.Faction == campaign.Faction || !CanChallenge(campaign, map, cell))
                continue;

            var reach = held.Min(own => WFSectorHex.Distance(own, cell));
            if (reach > ChallengeReach)
                continue;

            if (reach == 1)
                adjacent.Add((cell, nodes.Count == 0 ? 0 : nodes.Min(node => WFSectorHex.Distance(node, cell))));
            else
                near.Add((cell, reach));
        }

        _random.Shuffle(adjacent);
        _random.Shuffle(near);
        return adjacent.OrderBy(entry => entry.Distance).Select(entry => entry.Cell)
            .Concat(near.OrderBy(entry => entry.Distance).Select(entry => entry.Cell))
            .ToList();
    }

    /// <summary>
    /// Whether the ADS may challenge a cell: held by another faction, no station in it, not contested or disputed,
    /// none of its claim's sources standing in it, and no protection rule against it.
    /// </summary>
    public bool CanChallenge(WFAdsCampaignPrototype campaign, MapId map, WFSectorCell cell)
    {
        if (_territory.Owner(map, cell) is not { } owner || owner == campaign.Faction
            || _territory.ContestOf(map, cell) != null || _territory.IsDisputed(map, cell) || HasStation(map, cell))
            return false;

        foreach (var source in _territory.Sources(map, cell))
        {
            if (StandsIn(source, cell))
                return false;
        }

        var attempt = new WFSectorClaimAttemptEvent(map, cell, campaign.Faction, null);
        RaiseLocalEvent(ref attempt);
        return !attempt.Cancelled;
    }

    /// <summary>Whether a claim's source stands in a cell: any of an encounter's hulls, or the entity itself.</summary>
    private bool StandsIn(EntityUid source, WFSectorCell cell)
    {
        if (TerminatingOrDeleted(source))
            return false;

        if (!TryComp<WFEncounterComponent>(source, out var encounter))
            return _territory.CellOf(_transform.GetMapCoordinates(source)) == cell;

        foreach (var ship in encounter.Ships.Values)
        {
            if (!TerminatingOrDeleted(ship.Grid) && _territory.GridCell(ship.Grid) == cell)
                return true;
        }

        return false;
    }

    /// <summary>Sends an interdictor from the node nearest a cell to challenge it, and announces it.</summary>
    public bool TryChallenge(WFSectorCell target, out EntityUid interdictor)
    {
        interdictor = default;
        var map = _territory.SectorMap;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } state || !CanChallenge(campaign, map, target)
            || _territory.Owner(map, target) is not { } holder
            || !_prototypes.TryIndex(campaign.Interdictor, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var centre = WFSectorHex.Centre(target, _territory.CellSize);
        foreach (var node in Nodes().OrderBy(entry => Vector2.DistanceSquared(Position(entry.Comp2, entry.Comp1.Lead), centre)))
        {
            if (!TryClearPoint(map, Position(node.Comp2, node.Comp1.Lead), LaunchMin, LaunchMax, SpawnClearance, out var start))
                continue;

            if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
                return false;

            var lead = prototype.Ships[0].Key;
            var goal = ClearRoutePoint(map, centre) ?? centre;
            var trip = FlightTime(start, goal);
            if (!_encounters.SetOrders(uid, lead, TravelOrders(goal, ChallengeRange))
                || !_territory.Challenge(map, target, campaign.Faction, _timing.CurTime + trip + campaign.ChallengeDeadline, uid))
            {
                _encounters.End(uid);
                return false;
            }

            var comp = AddComp<WFAdsInterdictorComponent>(uid);
            comp.Target = target;
            comp.TargetPosition = goal;
            comp.Holder = holder;
            comp.Node = node.Owner;
            comp.Lead = lead;
            comp.Launched = _timing.CurTime;
            comp.Overdue = _timing.CurTime + trip * 2 + FlightSlack;
            state.Challenges++;

            var (station, bearing, range) = Place(map, goal);
            AnnounceQuiet(campaign, Loc.GetString("wf-ads-challenge-launched",
                ("callsign", WFSectorHex.Callsign(target)),
                ("holder", FactionName(holder)),
                ("station", station),
                ("bearing", bearing),
                ("range", range)), true);
            Log.Info($"ADS interdictor {ToPrettyString(uid)} launched from {ToPrettyString(node.Owner)} to challenge {WFSectorHex.Callsign(target)}.");
            interdictor = uid;
            return true;
        }

        return false;
    }

    /// <summary>A faction's display name.</summary>
    private string FactionName(ProtoId<WFSectorFactionPrototype> faction)
    {
        return _prototypes.TryIndex(faction, out var prototype) ? Loc.GetString(prototype.Name) : faction.Id;
    }

    /// <summary>Runs <see cref="TendChallenges"/> and <see cref="TendPickets"/> once, as a tick does.</summary>
    public void CheckChallenges()
    {
        if (State() is not { } state)
            return;

        TendChallenges(state, true);
        TendPickets();
    }

    /// <summary>
    /// Breaks interdictors whose lead is out of the fight; with <paramref name="act"/>, holds those that have arrived
    /// and ends those overdue short of their cell; moves each challenge's deadline to its interdictor's reckoned arrival.
    /// </summary>
    private void TendChallenges(WFAdsCampaignComponent state, bool act)
    {
        var campaign = CampaignFor(state);
        foreach (var interdictor in Interdictors())
        {
            var (uid, comp, encounter) = interdictor;
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, LostResolution(encounter, comp.Lead));
                continue;
            }

            if (campaign == null || comp.Arrived)
                continue;

            var map = Transform(uid).MapID;
            var at = _transform.GetWorldPosition(lead.Grid);
            if (act)
            {
                var inCell = _territory.CellOf(new MapCoordinates(at, map)) == comp.Target;
                var overdue = _timing.CurTime >= comp.Overdue;
                if (Vector2.Distance(at, comp.TargetPosition) <= ChallengeArrival || inCell && (lead.Flown || overdue))
                {
                    comp.Arrived = true;
                    _encounters.SetOrders(uid, comp.Lead, new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } });
                    Log.Info($"ADS interdictor {ToPrettyString(uid)} reached {WFSectorHex.Callsign(comp.Target)} and holds there.");
                    continue;
                }

                if (overdue)
                {
                    Log.Info($"ADS interdictor {ToPrettyString(uid)} ran past its time short of {WFSectorHex.Callsign(comp.Target)} and was ended.");
                    // Ending it releases it, which drops the challenge.
                    _encounters.End(uid);
                    AnnounceRadio(Loc.GetString("wf-ads-challenge-withdrawn", ("callsign", WFSectorHex.Callsign(comp.Target))));
                    continue;
                }

                if (lead.Flown)
                    _encounters.SetOrders(uid, comp.Lead, TravelOrders(comp.TargetPosition, ChallengeRange));
            }

            if (_territory.IsDisputed(map, comp.Target) || _territory.ContestOf(map, comp.Target) is not { } contest || contest.Source != uid)
                continue;

            var deadline = _timing.CurTime + FlightTime(at, comp.TargetPosition) + campaign.ChallengeDeadline;
            if ((contest.Deadline - deadline).Duration() >= DeadlineStep)
                _territory.SetContestDeadline(map, comp.Target, deadline);
        }
    }

    /// <summary>Breaks pickets whose lead is out of the fight.</summary>
    private void TendPickets()
    {
        foreach (var (uid, comp, encounter) in Pickets())
        {
            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
                _encounters.Resolve(uid, LostResolution(encounter, comp.Lead));
        }
    }

    /// <summary>Settles an ADS challenge: won, its interdictor stays as a picket; lost or dropped, it withdraws.</summary>
    private void OnContestResolved(ref WFSectorContestResolvedEvent args)
    {
        if (args.Holder == null || CampaignFor(State()) is not { } campaign || args.Faction != campaign.Faction
            || args.Map != _territory.SectorMap)
            return;

        foreach (var interdictor in Interdictors())
        {
            if (interdictor.Comp1.Target != args.Cell)
                continue;

            if (args.Claimed)
                MakePicket(campaign, interdictor);
            else
                Withdraw(campaign, interdictor);

            return;
        }
    }

    /// <summary>Turns an interdictor that won its cell into the picket holding it.</summary>
    private void MakePicket(WFAdsCampaignPrototype campaign, Entity<WFAdsInterdictorComponent, WFEncounterComponent> interdictor)
    {
        var (uid, comp, _) = interdictor;
        var cell = comp.Target;
        var picket = EnsureComp<WFAdsPicketComponent>(uid);
        picket.Cell = cell;
        picket.Lead = comp.Lead;
        picket.Holder = comp.Holder;
        RemComp<WFAdsInterdictorComponent>(uid);
        _encounters.SetOrders(uid, picket.Lead, new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } });
        if (State() is { } state)
            state.ChallengesWon++;

        AnnounceRadio(Loc.GetString("wf-ads-challenge-won", ("callsign", WFSectorHex.Callsign(cell))));
        Log.Info($"ADS interdictor {ToPrettyString(uid)} took {WFSectorHex.Callsign(cell)} and holds it as a picket.");
        Recompute();
    }

    /// <summary>Withdraws an interdictor whose challenge was decided against it: it is resolved completed and jumps out.</summary>
    private void Withdraw(WFAdsCampaignPrototype campaign, Entity<WFAdsInterdictorComponent, WFEncounterComponent> interdictor)
    {
        var (uid, comp, _) = interdictor;
        AnnounceRadio(Loc.GetString("wf-ads-challenge-lost", ("callsign", WFSectorHex.Callsign(comp.Target))));
        Log.Info($"ADS interdictor {ToPrettyString(uid)} lost its challenge for {WFSectorHex.Callsign(comp.Target)} and withdraws.");
        _encounters.Resolve(uid, WFEncounterResolution.Completed);
        _encounters.End(uid);
    }

    /// <summary>Withdraws an interdictor's challenge and counts it lost; destroyed, it pays its bounty and is announced.</summary>
    private void InterdictorResolved(Entity<WFAdsInterdictorComponent> interdictor, WFEncounterResolution resolution)
    {
        var (uid, comp) = interdictor;
        var state = State();
        var campaign = CampaignFor(state);
        _territory.Release(uid);
        if (state != null)
            state.ChallengesLost++;

        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null)
            return;

        var (bounty, paid) = PayInterdictorBounty(campaign, uid, "destroying the ADS interdictor");
        AnnounceRadio(Loc.GetString("wf-ads-challenge-destroyed",
            ("callsign", WFSectorHex.Callsign(comp.Target)),
            ("bounty", bounty),
            ("paid", paid)));
    }

    /// <summary>Frees a picket's cell; destroyed, it pays the interdictor bounty and is announced.</summary>
    private void PicketResolved(Entity<WFAdsPicketComponent> picket, WFEncounterResolution resolution)
    {
        var (uid, comp) = picket;
        var state = State();
        var campaign = CampaignFor(state);
        _territory.Release(uid);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null)
        {
            if (campaign != null)
                Recompute();
            return;
        }

        var (bounty, paid) = PayInterdictorBounty(campaign, uid, "destroying the ADS picket");
        AnnounceRadio(Loc.GetString("wf-ads-picket-destroyed",
            ("callsign", WFSectorHex.Callsign(comp.Cell)),
            ("bounty", bounty),
            ("paid", paid)));
        Recompute();
    }
}
