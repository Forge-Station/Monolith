using System.Linq;
using System.Numerics;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>How near a node's lead a player ship must stand by to count for the support pool.</summary>
    private const float SupportRange = 2500f;

    /// <summary>How long in all a ship must stand by an assault to share the support pool.</summary>
    private const float SupportTime = 90f;

    /// <summary>How long after the last hit on a node standing by still counts as support.</summary>
    private static readonly TimeSpan SupportWindow = TimeSpan.FromSeconds(120);

    /// <summary>What each sentinel alive adds to a node's bounty, as a fraction of the base.</summary>
    private const float SentinelBountyScale = 0.5f;

    /// <summary>Times the player ships near a node under attack that have never hit it.</summary>
    private void TrackSupport(WFAdsNodeComponent node, WFEncounterComponent encounter, Vector2 lead, HashSet<EntityUid> ships, float elapsed)
    {
        foreach (var ship in ships)
        {
            if (encounter.ShipHits.Keys.Any(key => key.Attacker == ship)
                || Vector2.DistanceSquared(_transform.GetWorldPosition(ship), lead) > SupportRange * SupportRange)
                continue;

            node.Support[ship] = node.Support.GetValueOrDefault(ship) + elapsed;
        }
    }

    /// <summary>Pays a broken node's bounty, at its higher tier and scaled by the sentinels alive, to those who hit it or its sentinels and the ships that stood by; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayNodeBounty(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state,
        Entity<WFAdsNodeComponent, WFEncounterComponent> node, int sentinels)
    {
        var (uid, comp, encounter) = node;
        var factor = 1f + SentinelBountyScale * sentinels;
        var bounty = (int) (ByTier(campaign.NodeBounty, Math.Max(comp.Tier, state.Tier), 0) * factor);
        var attackers = encounter.ShipHits.Keys.Select(key => key.Attacker).ToHashSet();
        var supporters = new List<EntityUid>();
        foreach (var (ship, seconds) in comp.Support)
        {
            if (seconds >= SupportTime && !attackers.Contains(ship) && !TerminatingOrDeleted(ship))
                supporters.Add(ship);
        }

        var (reward, extra) = Rewards(campaign, bounty, grant => (int) (grant.Stronghold * factor));
        var paid = _rewards.PayBounty(uid, reward, "breaking the ADS node", supporters, extra);
        return (bounty, paid);
    }

    /// <summary>Pays the sweep bounty to those who hit a destroyed sweep or sentinel; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PaySweepBounty(WFAdsCampaignPrototype campaign, EntityUid uid, string deed)
    {
        var (reward, extra) = Rewards(campaign, campaign.SweepBounty, grant => grant.Patrol);
        var paid = _rewards.PayBounty(uid, reward, deed, null, extra);
        return (campaign.SweepBounty, paid);
    }

    /// <summary>Pays the interdictor bounty to those who hit a destroyed interdictor or picket; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayInterdictorBounty(WFAdsCampaignPrototype campaign, EntityUid uid, string deed)
    {
        var (reward, extra) = Rewards(campaign, campaign.InterdictorBounty, grant => grant.Patrol);
        var paid = _rewards.PayBounty(uid, reward, deed, null, extra);
        return (campaign.InterdictorBounty, paid);
    }

    /// <summary>Pays half the current tier's node bounty, credits included, to those who hit a destroyed deployment; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayDeploymentBounty(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, EntityUid deployment)
    {
        var bounty = ByTier(campaign.NodeBounty, state.Tier, 0) / 2;
        var (reward, extra) = Rewards(campaign, bounty, grant => grant.Stronghold / 2);
        var paid = _rewards.PayBounty(deployment, reward, "stopping the ADS deployment", null, extra);
        return (bounty, paid);
    }

    /// <summary>A bounty as an encounter reward with the first navy's credits, and the other navies' credits as rewards of no spesos.</summary>
    private static (WFEncounterReward Reward, List<WFEncounterReward> Extra) Rewards(WFAdsCampaignPrototype campaign, int spesos,
        Func<WFReaverCreditGrant, int> amount)
    {
        var extra = campaign.Credits.Select(grant => new WFEncounterReward
        {
            Credits = amount(grant),
            CreditEntity = grant.Entity,
            CreditCompanies = new List<string>(grant.Companies),
        }).ToList();

        if (extra.Count == 0)
            return (new WFEncounterReward { Spesos = spesos }, extra);

        var reward = extra[0];
        reward.Spesos = spesos;
        extra.RemoveAt(0);
        return (reward, extra);
    }
}
