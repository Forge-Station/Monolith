using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How far from its source stronghold's lead a convoy is spawned.</summary>
    private const float ConvoySpawnMin = 600f;
    private const float ConvoySpawnMax = 1000f;

    /// <summary>How near its target point the lead is sent, and how near counts as arrived.</summary>
    private const float ConvoyRange = 300f;
    private const float ConvoyArrival = 400f;

    /// <summary>The speed a convoy's arrival is reckoned at, in metres a second.</summary>
    private const float ConvoySpeed = 30f;

    /// <summary>Time added to a convoy's reckoned arrival for the deadline of the contest on its target.</summary>
    private static readonly TimeSpan ConvoySlack = TimeSpan.FromMinutes(3);

    /// <summary>A contest deadline off by less than this is left alone, so the map is not resent every tick.</summary>
    private static readonly TimeSpan ConvoyDeadlineStep = TimeSpan.FromSeconds(30);

    /// <summary>The convoys under way on the sector map.</summary>
    public List<Entity<WFReaverConvoyComponent, WFEncounterComponent>> Convoys()
    {
        var map = _territory.SectorMap;
        var convoys = new List<Entity<WFReaverConvoyComponent, WFEncounterComponent>>();
        var query = EntityQueryEnumerator<WFReaverConvoyComponent, WFEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var convoy, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                convoys.Add((uid, convoy, encounter));
        }

        return convoys;
    }

    /// <summary>Sends a convoy to the held cell the rules pick next, unless one is already under way.</summary>
    public bool TryLaunchConvoy(out EntityUid convoy)
    {
        convoy = default;
        if (Campaign is not { } campaign || State(true) == null || Convoys().Count > 0)
            return false;

        var standing = Strongholds();
        return standing.Count > 0
            && TryLaterCell(campaign, Occupied(standing), out var target)
            && TryLaunchConvoy(target, out convoy);
    }

    /// <summary>Sends a hub from the standing stronghold nearest a cell to found itself there: contests the cell and announces it.</summary>
    public bool TryLaunchConvoy(WFSectorCell target, out EntityUid convoy)
    {
        convoy = default;
        if (Campaign is not { } campaign || State(true) is not { } state
            || !_prototypes.TryIndex(campaign.Stronghold, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = _territory.SectorMap;
        var centre = WFSectorHex.Centre(target, _territory.CellSize);
        foreach (var source in Strongholds().OrderBy(entry => Vector2.DistanceSquared(Position(entry.Comp2, entry.Comp1.Lead), centre)))
        {
            if (!TryClearPoint(map, Position(source.Comp2, source.Comp1.Lead), ConvoySpawnMin, ConvoySpawnMax, SpawnClearance, out var start))
                continue;

            if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
                return false;

            var encounter = Comp<WFEncounterComponent>(uid);
            var lead = prototype.Ships[0].Key;
            ShieldHub(campaign, uid, lead);
            var goal = ClearRoutePoint(map, centre) ?? centre;
            if (!_encounters.SetOrders(uid, lead, TravelOrders(goal)))
            {
                _encounters.End(uid);
                return false;
            }

            // The leash holds a stronghold to its station; under way it would turn the convoy back.
            encounter.Leash = 0f;

            var comp = AddComp<WFReaverConvoyComponent>(uid);
            comp.Target = target;
            comp.TargetPosition = goal;
            comp.Source = source.Owner;
            comp.Lead = lead;
            comp.Launched = _timing.CurTime;
            comp.Overdue = _timing.CurTime + ConvoyEta(start, goal) * 2 + ConvoySlack;
            _territory.Contest(map, target, campaign.Faction, _timing.CurTime + ConvoyEta(start, goal), uid);

            var (station, bearing, range) = Place(map, goal);
            Announce(campaign, Loc.GetString("wf-reaver-convoy-launched",
                ("callsign", WFSectorHex.Callsign(target)),
                ("station", station),
                ("bearing", bearing),
                ("range", range)), true, chime: false);
            Log.Info($"Reaver convoy {ToPrettyString(uid)} launched from {ToPrettyString(source.Owner)} for {WFSectorHex.Callsign(target)}.");
            convoy = uid;
            return true;
        }

        return false;
    }

    /// <summary>The orders that fly a convoy's lead to its target point.</summary>
    private static List<WFCrewObjective> TravelOrders(Vector2 goal)
    {
        return new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.GoTo, Position = goal, Range = ConvoyRange } };
    }

    /// <summary>Stops every convoy whose lead is out of the fight, founds those that have arrived and keeps their targets contested meanwhile.</summary>
    public void CheckConvoys()
    {
        if (State() is { } state)
            TendConvoys(state, true);
    }

    /// <summary>
    /// Stops convoys whose lead is out of the fight; with <paramref name="found"/>, founds those that have arrived,
    /// or are overdue in their target cell, and ends those overdue elsewhere; keeps each target contested meanwhile.
    /// </summary>
    private void TendConvoys(WFReaverCampaignComponent state, bool found)
    {
        var campaign = CampaignFor(state);
        foreach (var convoy in Convoys())
        {
            var (uid, comp, encounter) = convoy;
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
                if (Vector2.Distance(at, comp.TargetPosition) <= ConvoyArrival || inCell && (lead.Flown || overdue))
                {
                    CompleteFounding(uid);
                    continue;
                }

                // Adrift or stuck short of its cell it never arrives; ending it lifts the one-at-a-time gate.
                if (overdue)
                {
                    Log.Info($"Reaver convoy {ToPrettyString(uid)} ran past its time short of {WFSectorHex.Callsign(comp.Target)} and was ended.");
                    _encounters.End(uid);
                    continue;
                }

                // Its crew gave up the flight; it tries again until the time runs out.
                if (lead.Flown)
                    _encounters.SetOrders(uid, comp.Lead, TravelOrders(comp.TargetPosition));
            }

            HoldContest(campaign, convoy, at);
        }
    }

    /// <summary>Contests a convoy's target again if the contest went, or moves its deadline to the convoy's reckoned arrival.</summary>
    private void HoldContest(WFReaverCampaignPrototype campaign, Entity<WFReaverConvoyComponent, WFEncounterComponent> convoy, Vector2 at)
    {
        var (uid, comp, _) = convoy;
        var map = Transform(uid).MapID;
        if (_territory.IsDisputed(map, comp.Target))
            return;

        var deadline = _timing.CurTime + ConvoyEta(at, comp.TargetPosition);
        if (_territory.ContestOf(map, comp.Target) is not { } contest)
            _territory.Contest(map, comp.Target, campaign.Faction, deadline, uid);
        else if (contest.Source == uid && (contest.Deadline - deadline).Duration() >= ConvoyDeadlineStep)
            _territory.SetContestDeadline(map, comp.Target, deadline);
    }

    /// <summary>How long a convoy should take between two points, with slack.</summary>
    private static TimeSpan ConvoyEta(Vector2 from, Vector2 to)
    {
        return TimeSpan.FromSeconds(Vector2.Distance(from, to) / ConvoySpeed) + ConvoySlack;
    }

    /// <summary>
    /// Makes a convoy the stronghold it set out to be: drops the contest, claims the cell and ring, holds the lead
    /// where it stands as a level 0 hub, and announces the founding.
    /// </summary>
    public bool CompleteFounding(EntityUid convoy)
    {
        if (State(true) is not { } state || CampaignFor(state) is not { } campaign
            || !TryComp<WFReaverConvoyComponent>(convoy, out var comp)
            || !TryComp<WFEncounterComponent>(convoy, out var encounter) || encounter.Resolution != null)
            return false;

        var map = Transform(convoy).MapID;
        var target = comp.Target;
        var lead = comp.Lead;
        var where = comp.TargetPosition;
        if (encounter.Ships.TryGetValue(lead, out var standing) && !TerminatingOrDeleted(standing.Grid))
        {
            // A lead that stopped short of its point, but in the cell, founds where it stands.
            var here = _transform.GetWorldPosition(standing.Grid);
            if (Vector2.Distance(here, where) > ConvoyArrival && _territory.CellOf(new MapCoordinates(here, map)) == target)
                where = here;
        }

        var at = new MapCoordinates(where, map);
        RemComp<WFReaverConvoyComponent>(convoy);
        if (_territory.ContestOf(map, target) is { } contest && contest.Source == convoy)
            _territory.DropContest(map, target);

        // Its station is where it stopped: the leash holds it there from now on.
        encounter.Origin = at;
        encounter.Leash = _prototypes.TryIndex(encounter.Prototype, out var prototype) ? prototype.Leash : 0f;
        _encounters.SetOrders(convoy, lead, new List<WFCrewObjective> { new() { Kind = WFCrewObjectiveKind.Hold } });

        Establish(campaign, state, convoy, lead, target, at, false);
        SeedHits(convoy, encounter);
        Log.Info($"Reaver convoy {ToPrettyString(convoy)} founded a stronghold at {WFSectorHex.Callsign(target)}.");
        return true;
    }

    /// <summary>
    /// Carries the hits a convoy took on its way into the stronghold it became, so they are not seen as a fresh attack:
    /// the ships that landed them are marked once and no patrol is pulled off its route for them.
    /// </summary>
    private void SeedHits(EntityUid uid, WFEncounterComponent encounter)
    {
        if (!TryComp<WFReaverStrongholdComponent>(uid, out var stronghold))
            return;

        stronghold.Hits = encounter.ShipHits.Values.Sum();
        if (stronghold.Hits == 0)
            return;

        foreach (var ((attacker, _), hits) in encounter.ShipHits)
        {
            stronghold.AttackerHits[attacker] = stronghold.AttackerHits.GetValueOrDefault(attacker) + hits;
        }

        stronghold.Recalled = true;
        stronghold.LastHit = _timing.CurTime - SupportWindow - TimeSpan.FromSeconds(1);
        Mark(stronghold.AttackerHits.Keys);
    }

    /// <summary>Withdraws a convoy's contest; destroyed, it pays half a stronghold's bounty and is announced.</summary>
    private void ConvoyResolved(Entity<WFReaverConvoyComponent> convoy, WFEncounterResolution resolution)
    {
        var (uid, comp) = convoy;
        var state = State();
        var campaign = CampaignFor(state);
        _territory.Release(uid);
        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return;

        Mark(encounter.ShipHits.Keys.Select(key => key.Attacker));
        var (bounty, paid) = PayConvoyBounty(campaign, state, uid);
        state.ConvoysDestroyed++;
        Announce(campaign, Loc.GetString("wf-reaver-convoy-destroyed",
            ("callsign", WFSectorHex.Callsign(comp.Target)),
            ("bounty", bounty),
            ("paid", paid)), false, chime: false);
        Recompute();
    }
}
