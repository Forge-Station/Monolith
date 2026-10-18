#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server.GameTicking.Presets;
using Content.Shared._Mono.Company;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>The ADS campaign content as shipped: one campaign for every vote option, sound ladders and encounters, and the rule in the Contested Sector preset.</summary>
[TestFixture]
public sealed class WFAdsContentTest
{
    private static bool IsTest(string id)
    {
        return id.StartsWith("WFTest");
    }

    /// <summary>The lobby vote offers presets; each must run exactly one ADS campaign.</summary>
    [Test]
    public async Task EveryVotablePresetHasExactlyOneCampaign()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var campaigns = prototypes.EnumeratePrototypes<WFAdsCampaignPrototype>().Where(campaign => !IsTest(campaign.ID)).ToList();
            var presets = prototypes.EnumeratePrototypes<WFEncounterPresetPrototype>().Where(preset => preset.Votable && !IsTest(preset.ID)).ToList();
            Assert.That(presets, Is.Not.Empty);
            foreach (var preset in presets)
            {
                Assert.That(campaigns.Count(campaign => campaign.Preset == preset.ID), Is.EqualTo(1), $"{preset.ID} runs exactly one ADS campaign");
            }

            foreach (var campaign in campaigns)
            {
                Assert.That(prototypes.HasIndex(campaign.Preset), Is.True, $"{campaign.ID} names a real preset");
            }

            var ashfall = prototypes.Index<GamePresetPrototype>("WFContestedSector");
            Assert.That(ashfall.Rules, Does.Contain("WFAdsCampaignRule"), "The Contested Sector preset runs the ADS beside the Reavers.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The lists by tier have a tier each, the ladder climbs, and the faction, encounters, sounds and credits all exist.</summary>
    [Test]
    public async Task EveryCampaignIsWellFormed()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var resources = pair.Server.ResolveDependency<IResourceManager>();
            var campaigns = prototypes.EnumeratePrototypes<WFAdsCampaignPrototype>().Where(campaign => !IsTest(campaign.ID)).ToList();
            Assert.That(campaigns, Is.Not.Empty);
            foreach (var campaign in campaigns)
            {
                var id = campaign.ID;
                var faction = prototypes.Index(campaign.Faction);
                Assert.That(faction.Blockades, Is.False, $"{id}: the ADS never blockades a station");
                foreach (var list in new[] { campaign.Sweeps, campaign.Interdicts, campaign.MaxSentinelsByTier, campaign.NodeBounty, campaign.RetakeBounty })
                {
                    Assert.That(list, Has.Count.EqualTo(6), $"{id}: a value for each tier");
                    Assert.That(list, Is.Ordered.Ascending, $"{id}: higher tiers never get less");
                }

                Assert.That(campaign.TierThresholds, Has.Count.EqualTo(5), $"{id}: a threshold for each of tiers 1 to 5");
                Assert.That(campaign.TierThresholds, Is.Ordered.Ascending, $"{id}: the ladder climbs");
                Assert.That(campaign.ChallengeTier, Is.InRange(1, 5), $"{id}");
                Assert.That(campaign.MaxChallenges, Is.GreaterThan(0), $"{id}");
                Assert.That(campaign.MaxNodes, Is.GreaterThan(0), $"{id}");
                Assert.That(campaign.CellsPerNode, Is.GreaterThan(0), $"{id}");

                foreach (var grant in campaign.Credits)
                {
                    Assert.That(prototypes.HasIndex(grant.Entity), Is.True, $"{id}: credit {grant.Entity}");
                    foreach (var company in grant.Companies)
                    {
                        Assert.That(prototypes.HasIndex<CompanyPrototype>(company), Is.True, $"{id}: credit company {company}");
                    }
                }

                foreach (var sound in new[] { campaign.RisingSound, campaign.FallingSound })
                {
                    var path = ((Robust.Shared.Audio.SoundPathSpecifier) sound).Path;
                    Assert.That(resources.ContentFileExists(path), Is.True, $"{id}: {path}");
                }

                foreach (var encounterId in new[] { campaign.Node, campaign.Deployment, campaign.Sentinel, campaign.Sweep, campaign.Interdictor })
                {
                    var encounter = prototypes.Index(encounterId);
                    Assert.That(encounter.Start, Is.EqualTo(WFEncounterStart.Manual), $"{encounterId}: only the campaign starts it");
                    Assert.That(encounter.Ships, Is.Not.Empty, $"{encounterId}");
                    Assert.That(encounter.Ships[0].Key, Is.EqualTo("lead"), $"{encounterId}: the first ship is the lead");
                }

                Assert.That(prototypes.Index(campaign.Sweep).Lifetime, Is.EqualTo(WFEncounterLifetime.Transient), "A sweep jumps out when home.");
                Assert.That(prototypes.Index(campaign.Node).Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent));
                Assert.That(prototypes.Index(campaign.Interdictor).Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent));
            }
        });

        await pair.CleanReturnAsync();
    }
}
