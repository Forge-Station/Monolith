using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Whether the campaign's clocks stand still: it is paused, or the event runner is.</summary>
    public bool Held => Paused || _scheduler.Paused;

    /// <summary>Whether the event runner (the Encounters scheduler) is paused, which holds the campaign too.</summary>
    public bool RunnerPaused => _scheduler.Paused;

    /// <summary>
    /// Pushes every campaign clock forward by a span the campaign sat held or switched off, so nothing falls due at
    /// once on resuming. Clocks are absolute times, so each is moved rather than frozen.
    /// </summary>
    private void HoldClocks(TimeSpan span, WFReaverCampaignComponent? state)
    {
        if (span <= TimeSpan.Zero)
            return;

        if (_nextFoundTry != TimeSpan.Zero)
            _nextFoundTry += span;

        // The first founding is timed from the stations; later ones from the campaign state.
        if (_stationsAt != null && state is not { Founded: > 0 })
            _stationsAt += span;

        if (state != null)
            HoldState(state, span);

        foreach (var (_, comp, _) in Strongholds())
        {
            comp.NextPatrol += span;
            comp.NextReinforcement += span;
            if (comp.NextSortie != TimeSpan.Zero)
                comp.NextSortie += span;
            if (comp.Hits > 0)
                comp.LastHit += span;
        }

        foreach (var (_, comp, _) in Reinforcements())
        {
            comp.Overdue += span;
            comp.NextOrders += span;
        }

        foreach (var (_, comp, _) in Convoys())
        {
            comp.Overdue += span;
        }

        var marks = EntityQueryEnumerator<WFReaverMarkComponent>();
        while (marks.MoveNext(out _, out var mark))
        {
            mark.Until += span;
        }

        // The event runner holds every encounter's own timers when it is paused; otherwise the campaign holds its own.
        if (!_scheduler.Paused)
        {
            var encounters = EntityQueryEnumerator<WFEncounterComponent>();
            while (encounters.MoveNext(out var uid, out var encounter))
            {
                if (IsCampaignEncounter(uid))
                    _encounters.Hold((uid, encounter), span);
            }
        }

        HoldContests(span);
    }

    /// <summary>Moves the state's own clocks.</summary>
    private static void HoldState(WFReaverCampaignComponent state, TimeSpan span)
    {
        state.BelowSince += span;
        state.TierAnnounced += span;
        state.LastFounded += span;
        state.NoStrongholdSince += span;
        state.NextSpread += span;
        state.NextRaid += span;
        Shift(state.Raided, span);
        Shift(state.InsideSince, span);
        Shift(state.InterceptCooldowns, span);
    }

    /// <summary>Moves every time in a table forward.</summary>
    private static void Shift(Dictionary<EntityUid, TimeSpan> times, TimeSpan span)
    {
        foreach (var key in times.Keys.ToList())
        {
            times[key] += span;
        }
    }

    /// <summary>Moves the deadlines of the faction's contests, which would otherwise claim their cells while the campaign is held.</summary>
    private void HoldContests(TimeSpan span)
    {
        var map = _territory.SectorMap;
        if (CampaignFor(State()) is not { } campaign || _territory.Territory(map) is not { } territory)
            return;

        foreach (var (cell, contest) in territory.Contests.ToList())
        {
            // A disputed cell's deadline is already pushed by the territory system.
            if (contest.Faction == campaign.Faction && !_territory.IsDisputed(map, cell))
                _territory.SetContestDeadline(map, cell, contest.Deadline + span);
        }
    }
}
