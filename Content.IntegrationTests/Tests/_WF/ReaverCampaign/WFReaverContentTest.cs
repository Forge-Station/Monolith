#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.GameRule;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Content.Shared.Roles;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The campaign content as shipped: a campaign for every vote option, sound encounter prototypes, and factions with real companies.</summary>
[TestFixture]
public sealed class WFReaverContentTest
{
    private static bool IsTest(string id)
    {
        return id.StartsWith("WFTest");
    }

    /// <summary>The lobby vote offers presets; each must run exactly one campaign.</summary>
    [Test]
    public async Task EveryVotablePresetHasExactlyOneCampaign()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var campaigns = prototypes.EnumeratePrototypes<WFReaverCampaignPrototype>().Where(campaign => !IsTest(campaign.ID)).ToList();
            var presets = prototypes.EnumeratePrototypes<WFEncounterPresetPrototype>().Where(preset => preset.Votable && !IsTest(preset.ID)).ToList();
            Assert.That(presets, Is.Not.Empty);
            foreach (var preset in presets)
            {
                Assert.That(campaigns.Count(campaign => campaign.Preset == preset.ID), Is.EqualTo(1), $"{preset.ID} runs exactly one campaign");
            }

            foreach (var campaign in campaigns)
            {
                Assert.That(prototypes.HasIndex(campaign.Preset), Is.True, $"{campaign.ID} names a real preset");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every faction's company is a real company, and every other id a faction or campaign names exists.</summary>
    [Test]
    public async Task EverySectorFactionCompanyExists()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var localization = pair.Server.ResolveDependency<ILocalizationManager>();
            var factions = prototypes.EnumeratePrototypes<WFSectorFactionPrototype>().ToList();
            Assert.That(factions, Is.Not.Empty);
            foreach (var faction in factions)
            {
                if (faction.Company is { } company)
                    Assert.That(prototypes.HasIndex(company), Is.True, $"{faction.ID}: company {company}");

                Assert.That(localization.TryGetString(faction.Name, out var name), Is.True, $"{faction.ID}: {faction.Name} is a string");
                Assert.That(name, Is.Not.Empty);
            }

            foreach (var campaign in prototypes.EnumeratePrototypes<WFReaverCampaignPrototype>())
            {
                Assert.That(prototypes.HasIndex(campaign.Faction), Is.True, $"{campaign.ID}: faction");
                Assert.That(prototypes.Index(campaign.Faction).Blockades, Is.True, $"{campaign.ID}: Reaver stations are blockaded");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Reavers leave the Underground alone: its company is real and hidden from the picker, its jobs and station fly it, and the faction names both.</summary>
    [Test]
    public async Task TheUndergroundIsNeutralToTheReavers()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var localization = pair.Server.ResolveDependency<ILocalizationManager>();
            var company = prototypes.Index<CompanyPrototype>("WFUnderground");
            Assert.That(company.Name, Is.EqualTo("Underground"));
            Assert.That(company.Disabled, Is.True, "Nobody picks it at character setup.");
            Assert.That(localization.TryGetString(company.Description, out _), Is.True, "Its description is a string.");

            var reavers = prototypes.Index<WFSectorFactionPrototype>("WFSectorFactionReavers");
            Assert.That(reavers.NeutralCompanies.Select(entry => entry.Id), Is.EquivalentTo(new[] { "WFUnderground" }));
            Assert.That(reavers.NeutralStations, Is.EquivalentTo(new[] { "Underground" }));
            foreach (var faction in prototypes.EnumeratePrototypes<WFSectorFactionPrototype>())
            {
                foreach (var neutral in faction.NeutralCompanies)
                {
                    Assert.That(prototypes.HasIndex(neutral), Is.True, $"{faction.ID}: neutral company {neutral}");
                }
            }

            foreach (var job in new[] { "Fugitive", "Godfather" })
            {
                Assert.That(prototypes.Index<JobPrototype>(job).AssignedCompany.Id, Is.EqualTo("WFUnderground"), $"{job} flies the Underground flag");
            }

            var station = prototypes.Index<PointOfInterestPrototype>("Underground");
            Assert.That(station.AddComponents.TryGetValue("Company", out var entry), Is.True, "The station flies it too.");
            Assert.That(((CompanyComponent) entry!.Component).CompanyName.Id, Is.EqualTo("WFUnderground"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The lists by tier have a tier each, the ladder climbs, and the encounters, sounds and credits all exist.</summary>
    [Test]
    public async Task EveryCampaignIsWellFormed()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var resources = pair.Server.ResolveDependency<IResourceManager>();
            var campaigns = prototypes.EnumeratePrototypes<WFReaverCampaignPrototype>().Where(campaign => !IsTest(campaign.ID)).ToList();
            Assert.That(campaigns, Is.Not.Empty);
            foreach (var campaign in campaigns)
            {
                var id = campaign.ID;
                Assert.That(campaign.Patrols, Has.Count.EqualTo(6), $"{id}: patrols by tier");
                Assert.That(campaign.ThreatScale, Has.Count.EqualTo(6), $"{id}: threat scale by tier");
                Assert.That(campaign.StrongholdBounty, Has.Count.EqualTo(6), $"{id}: bounty by tier");
                Assert.That(campaign.TierThresholds, Has.Count.EqualTo(5), $"{id}: a threshold for each of tiers 1 to 5");
                Assert.That(campaign.TierThresholds, Is.Ordered.Ascending, $"{id}: the ladder climbs");
                Assert.That(campaign.StrongholdBounty, Is.Ordered.Ascending, $"{id}: a bigger stronghold pays more");
                Assert.That(campaign.RaidInterval, Is.Not.Empty, $"{id}: raid intervals");
                Assert.That(campaign.RaidInterval.All(interval => interval > TimeSpan.Zero), Is.True, $"{id}: raid intervals are positive");
                Assert.That(campaign.RaidInterval, Is.Ordered.Descending, $"{id}: raids come faster at higher tiers");
                Assert.That(campaign.MaxThreatScale, Is.GreaterThanOrEqualTo(1f), $"{id}: a cap that does not weigh anything down");
                Assert.That(campaign.CellsPerStronghold, Is.GreaterThan(0), $"{id}");
                Assert.That(campaign.MaxStrongholds, Is.GreaterThan(0), $"{id}");
                Assert.That(campaign.FirstStrongholdDelay, Is.GreaterThan(TimeSpan.Zero), $"{id}");
                Assert.That(campaign.RegroupDelay, Is.GreaterThan(TimeSpan.Zero), $"{id}");

                foreach (var scaled in campaign.ScaledEncounters)
                {
                    Assert.That(prototypes.HasIndex(scaled), Is.True, $"{id}: scaled {scaled}");
                }

                foreach (var grant in campaign.Credits)
                {
                    Assert.That(prototypes.HasIndex(grant.Entity), Is.True, $"{id}: credit {grant.Entity}");
                    Assert.That(grant.Companies, Is.Not.Empty, $"{id}: {grant.Entity} goes to a company");
                    foreach (var company in grant.Companies)
                    {
                        Assert.That(prototypes.HasIndex<CompanyPrototype>(company), Is.True, $"{id}: credit company {company}");
                    }
                }

                var companies = campaign.Credits.SelectMany(grant => grant.Companies).ToList();
                Assert.That(companies, Is.Unique, $"{id}: a company is paid by one grant only");

                foreach (var sound in new[] { campaign.RisingSound, campaign.FallingSound })
                {
                    var path = ((Robust.Shared.Audio.SoundPathSpecifier) sound).Path;
                    Assert.That(resources.ContentFileExists(path), Is.True, $"{id}: {path}");
                }

                var strongholdId = campaign.Stronghold;
                var stronghold = prototypes.Index(strongholdId);
                Assert.That(stronghold.Start, Is.EqualTo(WFEncounterStart.Manual), $"{strongholdId}: only the campaign starts it");
                Assert.That(stronghold.Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent), $"{strongholdId}");
                Assert.That(stronghold.StartRadius, Is.Zero, $"{strongholdId}");
                Assert.That(stronghold.Ships, Has.Count.EqualTo(1), $"{strongholdId}: a hub with no guard ships");
                var hub = stronghold.Ships[0];
                Assert.That(hub.Key, Is.EqualTo("lead"), $"{strongholdId}: the first ship is the lead");
                Assert.That(hub.WarnRange, Is.GreaterThan(hub.AttackRange), $"{strongholdId}: the lead carries the zones");
                Assert.That(hub.Evades, Is.False, $"{strongholdId}: a hub that ran from an alert would undock everything on it");
                Assert.That(hub.Vessels, Is.Not.Empty, $"{strongholdId}");
                Assert.That(prototypes.TryIndex(campaign.HubShield, out var shield), Is.True, $"{id}: hub shield generator");
                Assert.That(shield!.Components.ContainsKey("ShipShieldEmitter"), Is.True, $"{id}: {campaign.HubShield} is a shield generator");

                Assert.That(campaign.MaxLevelByTier, Has.Count.EqualTo(6), $"{id}: level cap by tier");
                Assert.That(campaign.MaxLevelByTier, Is.Ordered.Ascending, $"{id}: higher tiers grow strongholds at least as far");
                Assert.That(campaign.MaxLevelByTier[0], Is.Zero, $"{id}: no reinforcements at tier 0");
                Assert.That(campaign.Reinforcements, Has.Count.GreaterThanOrEqualTo(campaign.MaxLevelByTier.Max()), $"{id}: a convoy for every level");
                Assert.That(campaign.ReinforcementInterval, Is.GreaterThan(TimeSpan.Zero), $"{id}");
                foreach (var reinforcementId in campaign.Reinforcements)
                {
                    var reinforcement = prototypes.Index(reinforcementId);
                    Assert.That(reinforcement.Start, Is.EqualTo(WFEncounterStart.Manual), $"{reinforcementId}");
                    Assert.That(reinforcement.Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent), $"{reinforcementId}: it stays with the hub");
                    Assert.That(reinforcement.StartRadius, Is.Zero, $"{reinforcementId}");
                    Assert.That(reinforcement.Leash, Is.Zero, $"{reinforcementId}: a leash would turn it back from the hub");
                    Assert.That(reinforcement.Ships[0].Key, Is.EqualTo("lead"), $"{reinforcementId}");
                    Assert.That(reinforcement.Ships[0].Evades, Is.False, $"{reinforcementId}: only the campaign sends it out of a dock");
                }

                var patrol = prototypes.Index(campaign.Patrol);
                Assert.That(patrol.Start, Is.EqualTo(WFEncounterStart.Manual), $"{campaign.Patrol}");
                Assert.That(patrol.Lifetime, Is.EqualTo(WFEncounterLifetime.Transient), $"{campaign.Patrol}: it jumps out when home");
                Assert.That(patrol.StartRadius, Is.Zero, $"{campaign.Patrol}");
                Assert.That(patrol.Leash, Is.Zero, $"{campaign.Patrol}: a leash would drag it home from its route");
                Assert.That(patrol.Ships[0].Key, Is.EqualTo("lead"));

                foreach (var raidId in new[] { campaign.RaidPack, campaign.RaidBoarders, campaign.RaidStation })
                {
                    var raid = prototypes.Index(raidId);
                    Assert.That(raid.Start, Is.EqualTo(WFEncounterStart.Manual), $"{raidId}");
                    Assert.That(raid.Lifetime, Is.EqualTo(WFEncounterLifetime.Transient), $"{raidId}");
                    Assert.That(raid.StartRadius, Is.Zero, $"{raidId}");
                    Assert.That(raid.MinPlayers, Is.GreaterThan(0), $"{raidId}: not on an empty server");
                    Assert.That(raid.Ships, Is.Not.Empty, $"{raidId}");
                }

                Assert.That(prototypes.Index(campaign.RaidPack).Ships[0].Key, Is.EqualTo("raider"));
                Assert.That(prototypes.Index(campaign.RaidStation).Ships[0].Key, Is.EqualTo("raider"));
                Assert.That(prototypes.Index(campaign.RaidBoarders).Ships.Any(ship => ship.Hunt), Is.True, "The boarders hunt.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
