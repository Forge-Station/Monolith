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
    /// <summary>How far from the origin a sentinel appears, on the side of the sector nearest its node.</summary>
    private const float EdgeMin = 25000f;
    private const float EdgeMax = 28000f;

    /// <summary>How far, in radians, an edge point may stray from the line from the origin through the node.</summary>
    private const float EdgeSpread = 0.35f;

    /// <summary>How far from the node a sentinel appears if the sector edge has no clear point.</summary>
    private const float NearMin = 3000f;
    private const float NearMax = 5000f;

    /// <summary>How near the node an inbound sentinel closes, how near counts as joined, and the radius it circles at.</summary>
    private const float SentinelApproach = 500f;
    private const float SentinelArrival = 900f;
    private const float SentinelCircle = 400f;

    /// <summary>How long one circling order lasts before it is given again.</summary>
    private const float SentinelCircleTime = 3600f;

    /// <summary>The speed a flight is reckoned at, in metres a second.</summary>
    private const float FlightSpeed = 30f;

    /// <summary>Time added to a reckoned flight before the ship is overdue.</summary>
    private static readonly TimeSpan FlightSlack = TimeSpan.FromMinutes(3);

    /// <summary>How soon a node tries again after a sentinel that could not be sent.</summary>
    private static readonly TimeSpan SentinelRetry = TimeSpan.FromMinutes(1);

    /// <summary>How long after the last hit, with no attacker near, sentinels that broke away come back.</summary>
    private static readonly TimeSpan ReturnDelay = TimeSpan.FromMinutes(3);

    private const float ReturnClearance = 2500f;

    /// <summary>How far from the node a ship may be and still be picked as the one attacking it.</summary>
    private const float AttackerLeash = 5000f;

    /// <summary>Sends a sentinel from each node that has stood its interval and is under its tier's cap.</summary>
    private void TickSentinels(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state)
    {
        var now = _timing.CurTime;
        var cap = ByTier(campaign.MaxSentinelsByTier, state.Tier, 0);
        foreach (var node in Nodes())
        {
            var comp = node.Comp1;
            if (comp.Inbound != null || comp.Scattered || now < comp.NextSentinel || AliveSentinels(comp) >= cap)
                continue;

            if (!TrySentinel(node.Owner))
                comp.NextSentinel = now + SentinelRetry;
        }
    }

    /// <summary>The joined sentinels of a node still out.</summary>
    private int AliveSentinels(WFAdsNodeComponent node)
    {
        return node.Sentinels.Count(uid => TryComp<WFEncounterComponent>(uid, out var encounter) && encounter.Resolution == null);
    }

    /// <summary>Sends a node a sentinel now, whatever the caps, unless one is on its way: a gunship from the sector edge that flies to the node and circles it.</summary>
    public bool TrySentinel(EntityUid node)
    {
        if (Campaign is not { } campaign || State(true) is not { Finished: false }
            || !TryComp<WFAdsNodeComponent>(node, out var comp)
            || !TryComp<WFEncounterComponent>(node, out var encounter) || encounter.Resolution != null
            || comp.Inbound != null || LeadGrid(node, comp.Lead) is not { } hub
            || !_prototypes.TryIndex(campaign.Sentinel, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = Transform(node).MapID;
        var hubAt = _transform.GetWorldPosition(hub);
        if (!TryEdgePoint(map, hubAt, out var start) && !TryClearPoint(map, hubAt, NearMin, NearMax, SpawnClearance, out start))
            return false;

        if (!SpawnEncounter(prototype, new MapCoordinates(start, map), out var uid, offBudget: true))
            return false;

        var lead = prototype.Ships[0].Key;
        if (!_encounters.SetOrders(uid, lead, CircleOrders(hub, hubAt)))
        {
            _encounters.End(uid);
            return false;
        }

        var sentinel = AddComp<WFAdsSentinelComponent>(uid);
        sentinel.Node = node;
        sentinel.Lead = lead;
        sentinel.Phase = WFAdsSentinelPhase.Inbound;
        sentinel.Launched = _timing.CurTime;
        sentinel.Overdue = _timing.CurTime + FlightTime(start, hubAt) * 2 + FlightSlack;
        comp.Inbound = uid;
        Log.Info($"ADS sentinel {ToPrettyString(uid)} launched for node {ToPrettyString(node)}.");
        return true;
    }

    /// <summary>The orders that fly a sentinel to its node and circle it.</summary>
    private List<WFCrewObjective> CircleOrders(EntityUid hub, Vector2 hubAt)
    {
        return new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = hubAt, Range = SentinelApproach },
            new() { Kind = WFCrewObjectiveKind.Circle, Target = GetNetEntity(hub), Range = SentinelCircle, Duration = SentinelCircleTime },
        };
    }

    /// <summary>How long a flight between two points is reckoned to take.</summary>
    private static TimeSpan FlightTime(Vector2 from, Vector2 to)
    {
        return TimeSpan.FromSeconds(Vector2.Distance(from, to) / FlightSpeed);
    }

    /// <summary>A clear point 25 to 28 km from the origin on the side the node is on.</summary>
    private bool TryEdgePoint(MapId map, Vector2 hub, out Vector2 point)
    {
        var bearing = hub.LengthSquared() < 1000f * 1000f ? _random.NextFloat(MathF.Tau) : MathF.Atan2(hub.Y, hub.X);
        for (var i = 0; i < 8; i++)
        {
            var angle = bearing + _random.NextFloat(-EdgeSpread, EdgeSpread);
            point = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * _random.NextFloat(EdgeMin, EdgeMax);
            if (_scheduler.IsClearSpace(map, point, SpawnClearance, 0f))
                return true;
        }

        point = default;
        return false;
    }

    /// <summary>Runs <see cref="TendSentinels"/> once, as a tick does.</summary>
    public void CheckSentinels()
    {
        if (State() != null)
            TendSentinels();
    }

    /// <summary>
    /// Breaks sentinels whose lead is out of the fight, joins inbound ones that reached their node, ends those overdue
    /// short of it, keeps the joined circling and calls back any that chased out of held space.
    /// </summary>
    private void TendSentinels()
    {
        var now = _timing.CurTime;
        var held = CampaignFor(State()) is { } campaign ? HeldSet(campaign) : new HashSet<WFSectorCell>();
        foreach (var sentinel in Sentinels())
        {
            var (uid, comp, encounter) = sentinel;
            if (!TryComp<WFAdsNodeComponent>(comp.Node, out var node)
                || !TryComp<WFEncounterComponent>(comp.Node, out var nodeEncounter) || nodeEncounter.Resolution != null)
            {
                _encounters.End(uid);
                continue;
            }

            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                BreakLead(uid, encounter, comp.Lead);
                continue;
            }

            if (LeadGrid(comp.Node, node.Lead) is not { } hub)
                continue;

            var at = _transform.GetWorldPosition(lead.Grid);
            var hubAt = _transform.GetWorldPosition(hub);
            switch (comp.Phase)
            {
                case WFAdsSentinelPhase.Inbound:
                    if (Vector2.Distance(at, hubAt) <= SentinelArrival)
                    {
                        Join(sentinel);
                        break;
                    }

                    if (now >= comp.Overdue)
                    {
                        Log.Info($"ADS sentinel {ToPrettyString(uid)} ran past its time short of its node and was ended.");
                        _encounters.End(uid);
                        break;
                    }

                    if (lead.Flown)
                        _encounters.SetOrders(uid, comp.Lead, CircleOrders(hub, hubAt));
                    break;
                case WFAdsSentinelPhase.Circling:
                    if (lead.Flown)
                        _encounters.SetOrders(uid, comp.Lead, CircleOrders(hub, hubAt));
                    break;
                case WFAdsSentinelPhase.Attacking:
                    if (held.Count == 0 || InOrBeside(held, _territory.GridCell(lead.Grid)))
                    {
                        comp.OutsideSince = null;
                        if (lead.Flown || comp.Target is not { } target || TerminatingOrDeleted(target) || _status.IsDisabled(target))
                            ReturnToCircle(sentinel, hub);
                        break;
                    }

                    comp.OutsideSince ??= now;
                    if (now - comp.OutsideSince >= LeashDelay)
                        ReturnToCircle(sentinel, hub);
                    break;
            }
        }
    }

    /// <summary>Joins an inbound sentinel to its node: it circles, counts toward the node's bounty and the clock restarts.</summary>
    public bool Join(Entity<WFAdsSentinelComponent, WFEncounterComponent> sentinel)
    {
        var (uid, comp, _) = sentinel;
        if (comp.Phase != WFAdsSentinelPhase.Inbound || CampaignFor(State()) is not { } campaign
            || !TryComp<WFAdsNodeComponent>(comp.Node, out var node) || LeadGrid(comp.Node, node.Lead) is not { } hub)
            return false;

        comp.Phase = WFAdsSentinelPhase.Circling;
        if (node.Inbound == uid)
            node.Inbound = null;

        if (!node.Sentinels.Contains(uid))
            node.Sentinels.Add(uid);

        node.NextSentinel = _timing.CurTime + campaign.SentinelInterval;
        _encounters.SetOrders(uid, comp.Lead, CircleOrders(hub, _transform.GetWorldPosition(hub)));
        AnnounceRadio(Loc.GetString("wf-ads-sentinel-joined",
            ("callsign", WFSectorHex.Callsign(node.Cell)),
            ("count", AliveSentinels(node))));
        Log.Info($"ADS sentinel {ToPrettyString(uid)} joined node {ToPrettyString(comp.Node)}.");

        // Arriving mid-fight, it goes straight out.
        if (node.Scattered && node.Attacker is { } attacker && !TerminatingOrDeleted(attacker))
            Attack(sentinel, attacker);

        return true;
    }

    /// <summary>Sets a sentinel on a ship.</summary>
    private void Attack(Entity<WFAdsSentinelComponent, WFEncounterComponent> sentinel, EntityUid attacker)
    {
        var (uid, comp, _) = sentinel;
        comp.Phase = WFAdsSentinelPhase.Attacking;
        comp.Target = attacker;
        comp.OutsideSince = null;
        _encounters.SetOrders(uid, comp.Lead, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(attacker) },
        });
        Log.Info($"ADS sentinel {ToPrettyString(uid)} breaks away to attack {ToPrettyString(attacker)}.");
    }

    /// <summary>Sends a sentinel back to circle its node.</summary>
    private void ReturnToCircle(Entity<WFAdsSentinelComponent, WFEncounterComponent> sentinel, EntityUid hub)
    {
        var (uid, comp, _) = sentinel;
        comp.Phase = WFAdsSentinelPhase.Circling;
        comp.Target = null;
        comp.OutsideSince = null;
        _encounters.SetOrders(uid, comp.Lead, CircleOrders(hub, _transform.GetWorldPosition(hub)));
        Log.Info($"ADS sentinel {ToPrettyString(uid)} returns to circle its node.");
    }

    /// <summary>The player ship that has hit a node most, of those near it; else the top attacker anywhere.</summary>
    private EntityUid? TopAttacker(WFEncounterComponent encounter, EntityUid hub)
    {
        var hits = new Dictionary<EntityUid, int>();
        foreach (var ((attacker, _), count) in encounter.ShipHits)
        {
            if (!TerminatingOrDeleted(attacker))
                hits[attacker] = hits.GetValueOrDefault(attacker) + count;
        }

        var hubAt = _transform.GetWorldPosition(hub);
        var near = hits.Where(pair => Vector2.Distance(_transform.GetWorldPosition(pair.Key), hubAt) <= AttackerLeash).ToList();
        var pool = near.Count > 0 ? near : hits.ToList();
        return pool.Count == 0 ? null : pool.OrderByDescending(pair => pair.Value).First().Key;
    }

    /// <summary>Sends the joined sentinels of a node under attack after its top attacker.</summary>
    public void ScatterSentinels(EntityUid node)
    {
        if (!TryComp<WFAdsNodeComponent>(node, out var comp) || !TryComp<WFEncounterComponent>(node, out var encounter)
            || LeadGrid(node, comp.Lead) is not { } hub || TopAttacker(encounter, hub) is not { } attacker)
            return;

        comp.Attacker = attacker;
        foreach (var uid in comp.Sentinels.ToList())
        {
            if (!TryComp<WFAdsSentinelComponent>(uid, out var sentinel) || !TryComp<WFEncounterComponent>(uid, out var sentinelEncounter)
                || sentinelEncounter.Resolution != null)
                continue;

            if (sentinel.Phase == WFAdsSentinelPhase.Attacking && sentinel.Target == attacker)
                continue;

            Attack((uid, sentinel, sentinelEncounter), attacker);
            comp.Scattered = true;
        }
    }

    /// <summary>
    /// Brings back the sentinels that broke away once the node has gone three minutes with no hit and no ship that
    /// hit it within 2.5 km; with <paramref name="force"/>, at once.
    /// </summary>
    public void RegroupSentinels(EntityUid node, bool force)
    {
        if (!TryComp<WFAdsNodeComponent>(node, out var comp) || !comp.Scattered
            || !TryComp<WFEncounterComponent>(node, out var encounter) || LeadGrid(node, comp.Lead) is not { } hub)
            return;

        if (!force)
        {
            var hubAt = _transform.GetWorldPosition(hub);
            if (_timing.CurTime - comp.LastHit < ReturnDelay
                || encounter.ShipHits.Keys.Select(key => key.Attacker).Distinct().Any(attacker => !TerminatingOrDeleted(attacker)
                    && Vector2.Distance(_transform.GetWorldPosition(attacker), hubAt) <= ReturnClearance))
                return;
        }

        comp.Scattered = false;
        foreach (var uid in comp.Sentinels.ToList())
        {
            if (TryComp<WFAdsSentinelComponent>(uid, out var sentinel) && TryComp<WFEncounterComponent>(uid, out var sentinelEncounter)
                && sentinelEncounter.Resolution == null && sentinel.Phase == WFAdsSentinelPhase.Attacking)
                ReturnToCircle((uid, sentinel, sentinelEncounter), hub);
        }
    }

    /// <summary>Ends the sentinels of a node that is gone; with <paramref name="fold"/>, the hits on them count toward the node's bounty.</summary>
    private void SweepSentinels(Entity<WFAdsNodeComponent> node, bool fold)
    {
        var (uid, comp) = node;
        foreach (var sentinel in Sentinels().Where(entry => entry.Comp1.Node == uid))
        {
            if (fold && TryComp<WFEncounterComponent>(uid, out var hub))
                FoldHits(hub, sentinel.Comp2);

            _encounters.End(sentinel.Owner);
        }

        comp.Sentinels.Clear();
        comp.Inbound = null;
    }

    /// <summary>Adds the hits a sentinel took to a node's, so those who shot at either are paid for the node.</summary>
    private static void FoldHits(WFEncounterComponent hub, WFEncounterComponent sentinel)
    {
        foreach (var (user, bySide) in sentinel.Hits)
        {
            if (!hub.Hits.TryGetValue(user, out var into))
                hub.Hits[user] = into = new Dictionary<string, int>();

            foreach (var (side, hits) in bySide)
            {
                into[side] = into.GetValueOrDefault(side) + hits;
            }
        }

        foreach (var (key, hits) in sentinel.ShipHits)
        {
            hub.ShipHits[key] = hub.ShipHits.GetValueOrDefault(key) + hits;
        }
    }

    /// <summary>Drops a sentinel from its node, which tries again after the interval; destroyed, it pays the sweep bounty.</summary>
    private void SentinelResolved(Entity<WFAdsSentinelComponent> sentinel, WFEncounterResolution resolution)
    {
        var (uid, comp) = sentinel;
        var state = State();
        var campaign = CampaignFor(state);
        WFAdsNodeComponent? node = null;
        if (TryComp(comp.Node, out node))
        {
            node.Sentinels.Remove(uid);
            if (node.Inbound == uid)
                node.Inbound = null;

            node.NextSentinel = _timing.CurTime + (campaign?.SentinelInterval ?? SentinelRetry);
        }

        if (resolution != WFEncounterResolution.Destroyed || campaign == null || state == null || node == null)
            return;

        var (bounty, paid) = PaySweepBounty(campaign, uid, "destroying the ADS sentinel");
        state.SweepsDestroyed++;
        AnnounceRadio(Loc.GetString("wf-ads-sentinel-lost",
            ("callsign", WFSectorHex.Callsign(node.Cell)),
            ("bounty", bounty),
            ("paid", paid)));
    }
}
