using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How near the point it was sent to an intercepting patrol closes before it attacks.</summary>
    private const float InterceptApproach = 600f;

    /// <summary>How many cells from its target a patrol may be to be sent after it, or to keep after it.</summary>
    public const int InterceptReach = 3;

    /// <summary>How long a target may be out of held space, or out of reach, before its patrol gives it up.</summary>
    public static readonly TimeSpan InterceptLose = TimeSpan.FromSeconds(20);

    /// <summary>How soon a ship no patrol could be sent after is tried again.</summary>
    private static readonly TimeSpan InterceptRetry = TimeSpan.FromSeconds(30);

    /// <summary>How long a ship that fought the Reavers stays marked.</summary>
    public static readonly TimeSpan MarkTime = TimeSpan.FromMinutes(30);

    /// <summary>How many times heavier a marked ship weighs as a raid target.</summary>
    private const float MarkWeight = 3f;

    /// <summary>One pass of intercepts, as the tick runs it: ends those that are over, then sends patrols after ships that stayed in held space.</summary>
    public void CheckIntercepts()
    {
        if (State() is not { } state)
            return;

        TendIntercepts(state);
        if (Campaign is { } campaign)
            TickIntercepts(campaign, state, Crewed());
    }

    /// <summary>Times each crewed ship in held space and sends a patrol after each one past the dwell and off cooldown, within the tier's cap.</summary>
    private void TickIntercepts(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, HashSet<EntityUid> ships)
    {
        var now = _timing.CurTime;
        var held = _territory.Held(_territory.SectorMap, campaign.Faction).ToHashSet();
        var inside = ships.Where(ship => held.Contains(_territory.GridCell(ship)) && !_scheduler.IsSecretPlace(ship)).ToHashSet();
        foreach (var ship in state.InsideSince.Keys.ToList())
        {
            if (!inside.Contains(ship))
                state.InsideSince.Remove(ship);
        }

        foreach (var ship in inside)
        {
            state.InsideSince.TryAdd(ship, now);
        }

        foreach (var (ship, until) in state.InterceptCooldowns.ToList())
        {
            if (now >= until || TerminatingOrDeleted(ship))
                state.InterceptCooldowns.Remove(ship);
        }

        var patrols = Patrols();
        var active = patrols.Count(patrol => patrol.Comp1.Intercepting != null);
        var cap = ByTier(campaign.Intercepts, state.Tier, 0);
        foreach (var (ship, since) in state.InsideSince.OrderBy(entry => entry.Value).ToList())
        {
            if (active >= cap)
                return;

            if (now - since < campaign.InterceptDwell || state.InterceptCooldowns.ContainsKey(ship)
                || patrols.Any(patrol => patrol.Comp1.Intercepting == ship))
                continue;

            if (TryIntercept(ship))
                active++;
            else
                state.InterceptCooldowns[ship] = now + InterceptRetry;
        }
    }

    /// <summary>
    /// Sends the nearest free patrol within three cells after a ship now, or else a fresh one from the nearest
    /// stronghold if the patrol caps allow, whatever the dwell, cooldown and intercept cap.
    /// </summary>
    public bool TryIntercept(EntityUid target)
    {
        var map = _territory.SectorMap;
        if (Campaign is not { } campaign || State(true) is not { } state || TerminatingOrDeleted(target)
            || !HasComp<MapGridComponent>(target) || HasComp<WFEncounterGridComponent>(target) || Transform(target).MapID != map
            || _scheduler.IsSecretPlace(target) || Neutral(target))
            return false;

        var patrols = Patrols();
        if (patrols.Any(patrol => patrol.Comp1.Intercepting == target))
            return false;

        var at = _transform.GetWorldPosition(target);
        var cell = WFSectorHex.CellOf(at, _territory.CellSize);
        Entity<WFReaverPatrolComponent, WFEncounterComponent>? nearest = null;
        var best = float.MaxValue;
        foreach (var patrol in patrols)
        {
            if (patrol.Comp1.Intercepting != null || patrol.Comp1.Recalled
                || !patrol.Comp2.Ships.TryGetValue(patrol.Comp1.Lead, out var lead) || TerminatingOrDeleted(lead.Grid))
                continue;

            var position = _transform.GetWorldPosition(lead.Grid);
            var distance = Vector2.DistanceSquared(position, at);
            if (distance >= best || WFSectorHex.Distance(WFSectorHex.CellOf(position, _territory.CellSize), cell) > InterceptReach)
                continue;

            best = distance;
            nearest = patrol;
        }

        if (nearest is { } sent)
            return StartIntercept(campaign, sent, target, at, RemainingRoute(sent));

        var standing = Strongholds();
        if (standing.Count == 0)
            return false;

        var from = standing.MinBy(stronghold => Vector2.DistanceSquared(Position(stronghold.Comp2, stronghold.Comp1.Lead), at));
        if (!PatrolRoom(campaign, state, from.Owner, patrols) || !TryLaunchPatrol(from.Owner, out var uid)
            || !TryComp<WFReaverPatrolComponent>(uid, out var comp) || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return false;

        return StartIntercept(campaign, (uid, comp, encounter), target, at, comp.Route.ToList());
    }

    /// <summary>Turns a patrol on a ship: close on where it is and attack it, then fly the rest of the route and home. Warns those aboard and the sector.</summary>
    private bool StartIntercept(WFReaverCampaignPrototype campaign, Entity<WFReaverPatrolComponent, WFEncounterComponent> patrol,
        EntityUid target, Vector2 at, List<Vector2> resume)
    {
        var (uid, comp, encounter) = patrol;
        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = at, Range = InterceptApproach },
            new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(target) },
        };
        // The route stays queued behind the attack, so a patrol whose target is disabled flies on rather than going idle.
        queue.AddRange(RouteOrders(comp, resume));
        if (!_encounters.SetOrders(uid, comp.Lead, queue))
            return false;

        comp.Intercepting = target;
        comp.InterceptSince = _timing.CurTime;
        comp.Resume = resume;
        comp.TargetOutsideSince = null;
        comp.TargetFarSince = null;
        Log.Info($"Reaver patrol {ToPrettyString(uid)} turning to intercept {ToPrettyString(target)}.");

        var from = encounter.Ships.TryGetValue(comp.Lead, out var lead) && !TerminatingOrDeleted(lead.Grid)
            ? _transform.GetWorldPosition(lead.Grid)
            : comp.LastSeen;
        WarnShip(campaign, Transform(target).MapID, target, from, "wf-reaver-intercept-warning", "wf-reaver-intercept-warning");
        return true;
    }

    /// <summary>Ends each intercept whose target is disabled or gone, has been out of held space or out of reach for twenty seconds, or has drawn a sortie pack too far from its stronghold.</summary>
    private void TendIntercepts(WFReaverCampaignComponent state)
    {
        if (CampaignFor(state) is not { } campaign)
            return;

        var now = _timing.CurTime;
        var map = _territory.SectorMap;
        foreach (var patrol in Patrols())
        {
            var (_, comp, encounter) = patrol;
            // A patrol whose lead is gone resolves, which drops the intercept.
            if (comp.Intercepting is not { } target || !encounter.Ships.TryGetValue(comp.Lead, out var lead)
                || TerminatingOrDeleted(lead.Grid))
                continue;

            if (TerminatingOrDeleted(target) || Transform(target).MapID != map || _status.IsDisabled(target))
            {
                EndIntercept(patrol);
                continue;
            }

            if (comp.Sortie && StrongholdLead(comp.Stronghold) is { } hub
                && Vector2.Distance(_transform.GetWorldPosition(lead.Grid), _transform.GetWorldPosition(hub)) > SortieLeash)
            {
                EndIntercept(patrol);
                continue;
            }

            var cell = _territory.GridCell(target);
            if (_territory.Owner(map, cell) == campaign.Faction)
                comp.TargetOutsideSince = null;
            else
                comp.TargetOutsideSince ??= now;

            if (WFSectorHex.Distance(_territory.GridCell(lead.Grid), cell) <= InterceptReach)
                comp.TargetFarSince = null;
            else
                comp.TargetFarSince ??= now;

            if (now - comp.TargetOutsideSince >= InterceptLose || now - comp.TargetFarSince >= InterceptLose)
                EndIntercept(patrol);
        }
    }

    /// <summary>Gives an intercepting patrol back the rest of its route and home, and puts its target on cooldown.</summary>
    private void EndIntercept(Entity<WFReaverPatrolComponent, WFEncounterComponent> patrol)
    {
        var (uid, comp, _) = patrol;
        if (comp.Intercepting is not { } target)
            return;

        _encounters.SetOrders(uid, comp.Lead, RouteOrders(comp, comp.Resume));
        Log.Info($"Reaver patrol {ToPrettyString(uid)} gives up intercepting {ToPrettyString(target)}.");
        DropIntercept(comp);
    }

    /// <summary>Clears a patrol's intercept without new orders and puts its target on cooldown.</summary>
    private void DropIntercept(WFReaverPatrolComponent patrol)
    {
        if (patrol.Intercepting is not { } target)
            return;

        patrol.Intercepting = null;
        patrol.Resume = new List<Vector2>();
        patrol.TargetOutsideSince = null;
        patrol.TargetFarSince = null;
        if (State() is { } state && CampaignFor(state) is { } campaign)
            state.InterceptCooldowns[target] = _timing.CurTime + campaign.InterceptCooldown;
    }

    /// <summary>Orders to call at route points, then home.</summary>
    private static List<WFCrewObjective> RouteOrders(WFReaverPatrolComponent patrol, List<Vector2> route)
    {
        var orders = route.Select(point => new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = point, Range = RouteRange })
            .ToList();
        orders.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = patrol.Home, Range = RouteRange });
        return orders;
    }

    /// <summary>The route points a patrol has still to call at, read from its lead's orders.</summary>
    private List<Vector2> RemainingRoute(Entity<WFReaverPatrolComponent, WFEncounterComponent> patrol)
    {
        var (_, comp, encounter) = patrol;
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

    /// <summary>Whether a ship carries a live mark.</summary>
    public bool IsMarked(EntityUid ship)
    {
        return TryComp<WFReaverMarkComponent>(ship, out var mark) && _timing.CurTime < mark.Until;
    }

    /// <summary>Marks player ships that fought the Reavers for <see cref="MarkTime"/>, or refreshes their mark.</summary>
    private void Mark(IEnumerable<EntityUid> ships)
    {
        var until = _timing.CurTime + MarkTime;
        foreach (var ship in ships)
        {
            if (TerminatingOrDeleted(ship) || HasComp<WFEncounterGridComponent>(ship) || Neutral(ship))
                continue;

            EnsureComp<WFReaverMarkComponent>(ship).Until = until;
        }
    }

    /// <summary>Marks the player ships that have hit a stronghold since it was last looked at, and returns them.</summary>
    private List<EntityUid> MarkAttackers(WFReaverStrongholdComponent stronghold, WFEncounterComponent encounter)
    {
        var totals = new Dictionary<EntityUid, int>();
        foreach (var ((attacker, _), hits) in encounter.ShipHits)
        {
            totals[attacker] = totals.GetValueOrDefault(attacker) + hits;
        }

        var fresh = new List<EntityUid>();
        foreach (var (attacker, hits) in totals)
        {
            if (hits <= stronghold.AttackerHits.GetValueOrDefault(attacker))
                continue;

            stronghold.AttackerHits[attacker] = hits;
            fresh.Add(attacker);
        }

        Mark(fresh);
        return fresh;
    }

    /// <summary>Takes off the marks that have run out.</summary>
    private void TendMarks()
    {
        var now = _timing.CurTime;
        var expired = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFReaverMarkComponent>();
        while (query.MoveNext(out var uid, out var mark))
        {
            if (now >= mark.Until)
                expired.Add(uid);
        }

        foreach (var uid in expired)
        {
            RemComp<WFReaverMarkComponent>(uid);
        }
    }
}
