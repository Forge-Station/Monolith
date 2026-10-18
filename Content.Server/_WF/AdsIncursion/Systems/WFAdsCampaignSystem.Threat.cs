using Content.Server._WF.AdsIncursion.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>The highest tier.</summary>
    public const int MaxTier = 5;

    /// <summary>The sector's radius, against which a held cell's nearness to the origin is scored.</summary>
    private const float SectorRadius = 27000f;

    /// <summary>The most the nearness of held space to the origin adds to the score.</summary>
    private const float NearnessScore = 20f;

    private const float NodeScore = 10f;

    /// <summary>How long the score must stay under a tier before the tier falls.</summary>
    private static readonly TimeSpan FallDelay = TimeSpan.FromMinutes(10);

    /// <summary>The least time between a tier announcement and a fall.</summary>
    private static readonly TimeSpan AnnounceGap = TimeSpan.FromMinutes(10);

    /// <summary>Scores the campaign, moves the tier (a rise at once, a fall after a delay) and updates the legend.</summary>
    public void Recompute()
    {
        if (Campaign is not { } campaign || State(true) is not { } state)
            return;

        var now = _timing.CurTime;
        var held = _territory.Held(_territory.SectorMap, campaign.Faction);
        var nodes = Nodes().Count;
        var nearest = float.MaxValue;
        foreach (var cell in held)
        {
            nearest = MathF.Min(nearest, WFSectorHex.Centre(cell, _territory.CellSize).Length());
        }

        var nearness = held.Count == 0 ? 0f : Math.Clamp(NearnessScore * (1f - nearest / SectorRadius), 0f, NearnessScore);
        state.Score = nodes * NodeScore + held.Count + nearness + Dreadnoughts().Count * DreadnoughtScore;
        state.PeakCells = Math.Max(state.PeakCells, held.Count);

        var tier = TierFor(campaign, state.Score);
        if (tier > state.Tier)
        {
            ChangeTier(campaign, state, tier);
        }
        else if (tier < state.Tier)
        {
            state.BelowSince ??= now;
            if (now - state.BelowSince >= FallDelay && (state.TierAnnounced is not { } last || now - last >= AnnounceGap))
                ChangeTier(campaign, state, tier);
        }
        else
        {
            state.BelowSince = null;
        }

        SetLegend(campaign, state, nodes, held.Count);
    }

    /// <summary>Forces the tier, announcing the change. The score moves it again as usual.</summary>
    public void SetTier(int tier)
    {
        if (Campaign is not { } campaign || State(true) is not { } state)
            return;

        tier = Math.Clamp(tier, 0, MaxTier);
        if (tier != state.Tier)
            ChangeTier(campaign, state, tier);

        SetLegend(campaign, state, Nodes().Count, _territory.Held(_territory.SectorMap, campaign.Faction).Count);
    }

    /// <summary>The tier a score reaches: how many thresholds it meets.</summary>
    public static int TierFor(WFAdsCampaignPrototype campaign, float score)
    {
        var tier = 0;
        foreach (var threshold in campaign.TierThresholds)
        {
            if (score >= threshold)
                tier++;
        }

        return Math.Min(tier, MaxTier);
    }

    /// <summary>A tier's name.</summary>
    public string TierName(int tier)
    {
        return Loc.GetString($"wf-ads-tier-{Math.Clamp(tier, 0, MaxTier)}");
    }

    private void ChangeTier(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, int tier)
    {
        var rising = tier > state.Tier;
        state.Tier = tier;
        state.BelowSince = null;
        if (tier >= MaxTier)
            state.OverrunSince ??= _timing.CurTime;
        else
            state.OverrunSince = null;

        // A finished campaign never announces a rise again.
        if (rising && state.Finished)
            return;

        state.TierAnnounced = _timing.CurTime;
        Announce(campaign, Loc.GetString(rising ? "wf-ads-tier-rising" : "wf-ads-tier-falling",
            ("tier", TierName(tier)),
            ("nodes", Nodes().Count),
            ("cells", _territory.Held(_territory.SectorMap, campaign.Faction).Count),
            ("bounty", ByTier(campaign.NodeBounty, tier, 0))), rising);
    }

    /// <summary>Sends the campaign's legend line for the sector map.</summary>
    private void SetLegend(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, int nodes, int cells)
    {
        if (_legend is { } shown && shown != campaign.Faction)
            _sync.SetStatus(shown, null);

        // Nothing to show before the first founding.
        if (state.Founded == 0 && cells == 0 && !state.Finished)
        {
            ClearLegend();
            return;
        }

        var faction = FactionName(campaign.Faction);
        if (state.Finished)
        {
            _sync.SetStatus(campaign.Faction, Loc.GetString("wf-ads-legend-finished", ("faction", faction), ("nodes", nodes), ("cells", cells)));
            _legend = campaign.Faction;
            return;
        }

        if (state.Defeated && nodes == 0)
        {
            _sync.SetStatus(campaign.Faction, Loc.GetString("wf-ads-legend-dormant", ("faction", faction), ("cells", cells)));
            _legend = campaign.Faction;
            return;
        }

        _sync.SetStatus(campaign.Faction, Loc.GetString("wf-ads-legend",
            ("faction", faction),
            ("tier", TierName(state.Tier)),
            ("nodes", nodes),
            ("cells", cells),
            ("bounty", ByTier(campaign.NodeBounty, state.Tier, 0))));
        _legend = campaign.Faction;
    }

    private void ClearLegend()
    {
        if (_legend is not { } shown)
            return;

        _sync.SetStatus(shown, null);
        _legend = null;
    }
}
