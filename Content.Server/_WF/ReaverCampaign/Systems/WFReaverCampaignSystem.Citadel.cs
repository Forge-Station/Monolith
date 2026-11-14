using System.Linq;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>When the next citadel launch may be tried after one found no cell or no clear point.</summary>
    private TimeSpan _nextCitadelTry;

    /// <summary>Sends the citadel out once the campaign has stood at tier 5 for the delay, with none standing or under way.</summary>
    private void TickCitadel(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (campaign.Citadel == null)
            return;

        var now = _timing.CurTime;
        if (state.Tier < MaxTier)
        {
            state.OverrunSince = null;
            return;
        }

        var since = state.OverrunSince ??= now;
        if (now < since + campaign.CitadelDelay || now < _nextCitadelTry || state.Finished
            || CitadelStanding() != null || CitadelConvoy() != null)
            return;

        if (!TryLaunchCitadel(out _))
            _nextCitadelTry = now + FoundRetry;
    }

    /// <summary>Sends the citadel convoy now from the highest-level stronghold to the next held edge cell, whatever the tier; one citadel at a time, none once finished.</summary>
    public bool TryLaunchCitadel(out EntityUid convoy)
    {
        convoy = default;
        if (Campaign is not { Citadel: not null } campaign || State(true) is not { } state || state.Finished
            || CitadelStanding() != null || CitadelConvoy() != null)
            return false;

        var standing = Strongholds();
        return standing.Count > 0
            && TryLaterCell(campaign, Occupied(standing), out var target)
            && TryLaunchConvoy(target, out convoy, true);
    }

    /// <summary>The citadel standing on the sector map, if any.</summary>
    public EntityUid? CitadelStanding()
    {
        foreach (var (uid, comp, _) in Strongholds())
        {
            if (comp.Citadel)
                return uid;
        }

        return null;
    }

    /// <summary>The citadel convoy under way, if any.</summary>
    public EntityUid? CitadelConvoy()
    {
        foreach (var (uid, comp, _) in Convoys())
        {
            if (comp.Citadel)
                return uid;
        }

        return null;
    }

    /// <summary>The convoys under way that go to found an ordinary stronghold.</summary>
    private int OrdinaryConvoys()
    {
        return Convoys().Count(convoy => !convoy.Comp1.Citadel);
    }

    /// <summary>Whether the citadel has fallen this round, which finishes the Reavers.</summary>
    public bool IsFinished(WFReaverCampaignComponent? state)
    {
        return state is { Finished: true };
    }

    /// <summary>The highest level a stronghold may reach at the current tier: the citadel's lifted by its extra levels.</summary>
    private int LevelCap(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, WFReaverStrongholdComponent stronghold)
    {
        if (!stronghold.Citadel)
            return LevelCap(campaign, state);

        return campaign.Reinforcements.Count == 0 ? 0 : ByTier(campaign.MaxLevelByTier, state.Tier, 0) + campaign.CitadelExtraLevels;
    }

    /// <summary>The factor a stronghold's bounty and credits are scaled by: its level, and the citadel's scale.</summary>
    private static float BountyFactor(WFReaverCampaignPrototype campaign, WFReaverStrongholdComponent stronghold)
    {
        return LevelFactor(campaign, stronghold.Level) * (stronghold.Citadel ? campaign.CitadelBountyScale : 1f);
    }

    /// <summary>The legend's citadel part: its cell while one stands, else nothing.</summary>
    private string CitadelText()
    {
        foreach (var (_, comp, _) in Strongholds())
        {
            if (comp.Citadel)
                return Loc.GetString("wf-reaver-legend-citadel", ("callsign", WFSectorHex.Callsign(comp.Cell)));
        }

        return string.Empty;
    }
}
