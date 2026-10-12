using Content.Server._WF.SectorControl;
using Content.Server.GameTicking;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Keeps a cell with a standing stronghold in it from being retaken: it is freed when the hub is broken.</summary>
    private void OnRetakeAttempt(ref WFSectorRetakeAttemptEvent args)
    {
        if (Campaign is not { } campaign || args.Faction != campaign.Faction)
            return;

        foreach (var (_, stronghold, _) in Strongholds())
        {
            if (stronghold.Cell != args.Cell)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    /// <summary>The Reaver cells freed since the last tick, with when each stops clearing, announced together.</summary>
    private readonly List<(WFSectorCell Cell, TimeSpan Until)> _clearingPending = new();

    /// <summary>Remembers a freed Reaver cell so the cells freed in the same tick are announced as one line.</summary>
    private void OnClearing(ref WFSectorCellClearingEvent args)
    {
        if (CampaignFor(State()) is { } campaign && args.Previous == campaign.Faction && args.Map == _territory.SectorMap)
            _clearingPending.Add((args.Cell, args.Until));
    }

    /// <summary>Announces the pending freed cells in one line, with no chime: they are clearing and drones return after the grace period.</summary>
    private void AnnounceClearing()
    {
        var pending = new List<(WFSectorCell Cell, TimeSpan Until)>(_clearingPending);
        _clearingPending.Clear();
        if (CampaignFor(State()) is not { } campaign || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var callsigns = new List<string>();
        foreach (var (cell, _) in pending)
        {
            callsigns.Add(WFSectorHex.Callsign(cell));
        }

        callsigns.Sort(StringComparer.Ordinal);
        var minutes = Math.Max(1, (int) Math.Round((pending[0].Until - _timing.CurTime).TotalMinutes));
        Announce(campaign, Loc.GetString("wf-reaver-clearing", ("cells", string.Join(", ", callsigns)), ("minutes", minutes)), false, chime: false);
    }

    /// <summary>Pays the ships that held a retaken cell its bounty, announces it with no chime, and rescores the threat.</summary>
    private void OnRetaken(ref WFSectorCellRetakenEvent args)
    {
        var state = State();
        if (CampaignFor(state) is not { } campaign || args.Faction != campaign.Faction)
            return;

        var callsign = WFSectorHex.Callsign(args.Cell);
        var bounty = ByTier(campaign.RetakeBounty, state?.Tier ?? 0, 0);
        var paid = 0;
        if (bounty > 0 && args.Ships is { Count: > 0 } ships)
        {
            paid = _rewards.PayCrews(Loc.GetString("wf-reaver-retake-name", ("callsign", callsign)), $"Reaver cell {callsign}", bounty, ships,
                "retaking");
        }

        if (state != null)
            state.Retaken++;

        Announce(campaign, Loc.GetString("wf-reaver-retaken", ("callsign", callsign), ("bounty", bounty), ("paid", paid)), false, chime: false);
        Recompute();
    }
}
