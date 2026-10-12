using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>How near a stronghold's lead a player ship must stand by to count for the support pool.</summary>
    private const float SupportRange = 2500f;

    /// <summary>How long in all a ship must stand by an assault to share the support pool.</summary>
    private const float SupportTime = 90f;

    /// <summary>How long after the last hit on a stronghold standing by still counts as support.</summary>
    private static readonly TimeSpan SupportWindow = TimeSpan.FromSeconds(120);

    /// <summary>Times the player ships near a stronghold under attack that have never hit it.</summary>
    private void TrackSupport(WFReaverStrongholdComponent stronghold, WFEncounterComponent encounter, Vector2 lead,
        HashSet<EntityUid> ships, float elapsed)
    {
        foreach (var ship in ships)
        {
            if (encounter.ShipHits.Keys.Any(key => key.Attacker == ship)
                || Vector2.DistanceSquared(_transform.GetWorldPosition(ship), lead) > SupportRange * SupportRange)
                continue;

            stronghold.Support[ship] = stronghold.Support.GetValueOrDefault(ship) + elapsed;
        }
    }

    /// <summary>Pays a broken stronghold's bounty, at its higher tier and scaled by its level, to those who hit it or its reinforcements and the ships that stood by; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayStrongholdBounty(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state,
        Entity<WFReaverStrongholdComponent, WFEncounterComponent> stronghold)
    {
        var (uid, comp, encounter) = stronghold;
        var factor = LevelFactor(campaign, comp.Level);
        var bounty = (int) (ByTier(campaign.StrongholdBounty, Math.Max(comp.Tier, state.Tier), 0) * factor);
        var attackers = encounter.ShipHits.Keys.Select(key => key.Attacker).ToHashSet();
        var supporters = new List<EntityUid>();
        foreach (var (ship, seconds) in comp.Support)
        {
            if (seconds >= SupportTime && !attackers.Contains(ship) && !TerminatingOrDeleted(ship))
                supporters.Add(ship);
        }

        var (reward, extra) = Rewards(campaign, bounty, grant => (int) (grant.Stronghold * factor));
        var paid = _rewards.PayBounty(uid, reward, "breaking the Reaver stronghold", supporters, extra);
        return (bounty, paid);
    }

    /// <summary>Pays a destroyed patrol's bounty to those who hit it; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayPatrolBounty(WFReaverCampaignPrototype campaign, EntityUid patrol)
    {
        var (reward, extra) = Rewards(campaign, campaign.PatrolBounty, grant => grant.Patrol);
        var paid = _rewards.PayBounty(patrol, reward, "destroying the Reaver patrol", null, extra);
        return (campaign.PatrolBounty, paid);
    }

    /// <summary>Pays a destroyed reinforcement's bounty, the patrol bounty, to those who hit it; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayReinforcementBounty(WFReaverCampaignPrototype campaign, EntityUid reinforcement)
    {
        var (reward, extra) = Rewards(campaign, campaign.PatrolBounty, grant => grant.Patrol);
        var paid = _rewards.PayBounty(reinforcement, reward, "destroying the Reaver reinforcements", null, extra);
        return (campaign.PatrolBounty, paid);
    }

    /// <summary>Pays half the current tier's stronghold bounty, credits included, to those who hit a destroyed convoy; returns the bounty and how many were paid.</summary>
    private (int Bounty, int Paid) PayConvoyBounty(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, EntityUid convoy)
    {
        var bounty = ByTier(campaign.StrongholdBounty, state.Tier, 0) / 2;
        var (reward, extra) = Rewards(campaign, bounty, grant => grant.Stronghold / 2);
        var paid = _rewards.PayBounty(convoy, reward, "destroying the Reaver convoy", null, extra);
        return (bounty, paid);
    }

    /// <summary>A bounty as an encounter reward with the first navy's credits, and the other navies' credits as rewards of no spesos.</summary>
    private static (WFEncounterReward Reward, List<WFEncounterReward> Extra) Rewards(WFReaverCampaignPrototype campaign, int spesos,
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
