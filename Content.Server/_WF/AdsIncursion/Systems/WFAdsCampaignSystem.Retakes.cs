using Content.Server._WF.SectorControl;
using Content.Server.GameTicking;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>Keeps a cell with a standing node in it from being retaken: it is freed when the node is broken.</summary>
    private void OnRetakeAttempt(ref WFSectorRetakeAttemptEvent args)
    {
        if (Campaign is not { } campaign || args.Faction != campaign.Faction)
            return;

        foreach (var (_, node, _) in Nodes())
        {
            if (node.Cell != args.Cell)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    /// <summary>The ADS cells freed since the last tick, with when each stops clearing, announced together.</summary>
    private readonly List<(WFSectorCell Cell, TimeSpan Until)> _clearingPending = new();

    /// <summary>Remembers a freed ADS cell so the cells freed in the same tick are announced as one line.</summary>
    private void OnClearing(ref WFSectorCellClearingEvent args)
    {
        if (CampaignFor(State()) is { } campaign && args.Previous == campaign.Faction && args.Map == _territory.SectorMap)
            _clearingPending.Add((args.Cell, args.Until));
    }

    /// <summary>Announces the pending freed cells in one line on the radio.</summary>
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
        AnnounceRadio(Loc.GetString("wf-ads-clearing", ("cells", string.Join(", ", callsigns)), ("minutes", minutes)));
    }

    /// <summary>Pays the ships that held a retaken cell its bounty, announces it on the radio, and rescores the threat.</summary>
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
            paid = _rewards.PayCrews(Loc.GetString("wf-ads-retake-name", ("callsign", callsign)), $"ADS cell {callsign}", bounty, ships,
                "retaking");
        }

        if (state != null)
            state.Retaken++;

        AnnounceRadio(Loc.GetString("wf-ads-retaken", ("callsign", callsign), ("bounty", bounty), ("paid", paid)));
        Recompute();
    }
}
