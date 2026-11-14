using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>How near the point it was sent to an interdicting sweep closes before it holds and queries.</summary>
    private const float InterdictApproach = 600f;

    /// <summary>How long an interdicting sweep holds beside its target, so its zone puts the question, before it flies on.</summary>
    private const float InterdictHold = 180f;

    /// <summary>How many cells from its target a sweep may be to be sent after it, or to keep after it.</summary>
    public const int InterdictReach = 3;

    /// <summary>How long a target may be out of held space, or out of reach, before its sweep gives it up.</summary>
    public static readonly TimeSpan InterdictLose = TimeSpan.FromSeconds(20);

    /// <summary>How soon a ship no sweep could be sent after is tried again.</summary>
    private static readonly TimeSpan InterdictRetry = TimeSpan.FromSeconds(30);

    /// <summary>One pass of interdictions, as the tick runs it: ends those that are over, then sends sweeps after ships that stayed in held space.</summary>
    public void CheckInterdicts()
    {
        if (State() is not { } state)
            return;

        TendInterdicts(state);
        if (Campaign is { } campaign)
            TickInterdicts(campaign, state, Crewed());
    }

    /// <summary>Times each crewed ship in held space and sends a sweep after each one past the dwell and off cooldown, within the tier's cap.</summary>
    private void TickInterdicts(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, HashSet<EntityUid> ships)
    {
        var now = _timing.CurTime;
        var held = HeldSet(campaign);
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

        foreach (var (ship, until) in state.InterdictCooldowns.ToList())
        {
            if (now >= until || TerminatingOrDeleted(ship))
                state.InterdictCooldowns.Remove(ship);
        }

        var sweeps = Sweeps();
        var active = sweeps.Count(sweep => sweep.Comp1.Interdicting != null);
        var cap = ByTier(campaign.Interdicts, state.Tier, 0);
        foreach (var (ship, since) in state.InsideSince.OrderBy(entry => entry.Value).ToList())
        {
            if (active >= cap)
                return;

            if (now - since < campaign.InterdictDwell || state.InterdictCooldowns.ContainsKey(ship)
                || sweeps.Any(sweep => sweep.Comp1.Interdicting == ship))
                continue;

            if (TryInterdict(ship))
                active++;
            else
                state.InterdictCooldowns[ship] = now + InterdictRetry;
        }
    }

    /// <summary>
    /// Sends the nearest free sweep within three cells after a ship now, or else a fresh one from the nearest node if
    /// the sweep caps allow, whatever the dwell, cooldown and interdiction cap.
    /// </summary>
    public bool TryInterdict(EntityUid target)
    {
        var map = _territory.SectorMap;
        if (Campaign is not { } campaign || State(true) is not { Finished: false } state || TerminatingOrDeleted(target)
            || !HasComp<MapGridComponent>(target) || HasComp<WFEncounterGridComponent>(target) || Transform(target).MapID != map
            || _scheduler.IsSecretPlace(target) || _activity.IsNeutralTo(campaign.Faction, target) || _encounters.DockedAtStation(target))
            return false;

        var sweeps = Sweeps();
        if (sweeps.Any(sweep => sweep.Comp1.Interdicting == target))
            return false;

        var at = _transform.GetWorldPosition(target);
        var cell = WFSectorHex.CellOf(at, _territory.CellSize);
        Entity<WFAdsSweepComponent, WFEncounterComponent>? nearest = null;
        var best = float.MaxValue;
        foreach (var sweep in sweeps)
        {
            if (sweep.Comp1.Interdicting != null || LeadGrid(sweep.Owner, sweep.Comp1.Lead) is not { } lead)
                continue;

            var position = _transform.GetWorldPosition(lead);
            var distance = Vector2.DistanceSquared(position, at);
            if (distance >= best || WFSectorHex.Distance(WFSectorHex.CellOf(position, _territory.CellSize), cell) > InterdictReach)
                continue;

            best = distance;
            nearest = sweep;
        }

        if (nearest is { } sent)
            return StartInterdict(campaign, sent, target, at, RemainingRoute(sent));

        var standing = Nodes();
        if (standing.Count == 0)
            return false;

        var from = standing.MinBy(node => Vector2.DistanceSquared(Position(node.Comp2, node.Comp1.Lead), at));
        if (!SweepRoom(campaign, state, from.Owner, sweeps) || !TryLaunchSweep(from.Owner, out var uid)
            || !TryComp<WFAdsSweepComponent>(uid, out var comp) || !TryComp<WFEncounterComponent>(uid, out var encounter))
            return false;

        return StartInterdict(campaign, (uid, comp, encounter), target, at, comp.Route.ToList());
    }

    /// <summary>
    /// Turns a sweep on a ship: close on where it is and hold there, so its zone asks the ship whether it is a threat,
    /// then fly the rest of the route and home. It fires only through its zone, on a declared threat or an attacker.
    /// Warns those aboard and the sector.
    /// </summary>
    private bool StartInterdict(WFAdsCampaignPrototype campaign, Entity<WFAdsSweepComponent, WFEncounterComponent> sweep,
        EntityUid target, Vector2 at, List<Vector2> resume)
    {
        var (uid, comp, _) = sweep;
        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = at, Range = InterdictApproach },
            new() { Kind = WFCrewObjectiveKind.Hold, Duration = InterdictHold },
        };
        // The route stays queued behind the hold, so the sweep flies on rather than going idle.
        queue.AddRange(RouteOrders(comp, resume));
        if (!_encounters.SetOrders(uid, comp.Lead, queue))
            return false;

        comp.Interdicting = target;
        comp.InterdictSince = _timing.CurTime;
        comp.Resume = resume;
        comp.TargetOutsideSince = null;
        comp.TargetFarSince = null;
        Log.Info($"ADS sweep {ToPrettyString(uid)} turning to interdict {ToPrettyString(target)}.");

        var from = LeadGrid(uid, comp.Lead) is { } lead ? _transform.GetWorldPosition(lead) : comp.LastSeen;
        WarnShip(campaign, Transform(target).MapID, target, from, "wf-ads-interdict-warning");
        return true;
    }

    /// <summary>Ends each interdiction whose target is disabled or gone, or has been out of held space or out of reach for twenty seconds.</summary>
    private void TendInterdicts(WFAdsCampaignComponent state)
    {
        if (CampaignFor(state) is not { } campaign)
            return;

        var now = _timing.CurTime;
        var map = _territory.SectorMap;
        foreach (var sweep in Sweeps())
        {
            var (_, comp, _) = sweep;
            if (comp.Interdicting is not { } target || LeadGrid(sweep.Owner, comp.Lead) is not { } lead)
                continue;

            if (TerminatingOrDeleted(target) || Transform(target).MapID != map || _status.IsDisabled(target)
                || _encounters.DockedAtStation(target))
            {
                EndInterdict(sweep);
                continue;
            }

            var cell = _territory.GridCell(target);
            if (_territory.Owner(map, cell) == campaign.Faction)
                comp.TargetOutsideSince = null;
            else
                comp.TargetOutsideSince ??= now;

            if (WFSectorHex.Distance(_territory.GridCell(lead), cell) <= InterdictReach)
                comp.TargetFarSince = null;
            else
                comp.TargetFarSince ??= now;

            if (now - comp.TargetOutsideSince >= InterdictLose || now - comp.TargetFarSince >= InterdictLose)
                EndInterdict(sweep);
        }
    }

    /// <summary>Gives an interdicting sweep back the rest of its route and home, and puts its target on cooldown.</summary>
    private void EndInterdict(Entity<WFAdsSweepComponent, WFEncounterComponent> sweep)
    {
        var (uid, comp, _) = sweep;
        if (comp.Interdicting is not { } target)
            return;

        _encounters.SetOrders(uid, comp.Lead, RouteOrders(comp, comp.Resume));
        Log.Info($"ADS sweep {ToPrettyString(uid)} gives up interdicting {ToPrettyString(target)}.");
        DropInterdict(comp);
    }

    /// <summary>Clears a sweep's interdiction without new orders and puts its target on cooldown.</summary>
    private void DropInterdict(WFAdsSweepComponent sweep)
    {
        if (sweep.Interdicting is not { } target)
            return;

        sweep.Interdicting = null;
        sweep.Resume = new List<Vector2>();
        sweep.TargetOutsideSince = null;
        sweep.TargetFarSince = null;
        if (State() is { } state && CampaignFor(state) is { } campaign)
            state.InterdictCooldowns[target] = _timing.CurTime + campaign.InterdictCooldown;
    }
}
