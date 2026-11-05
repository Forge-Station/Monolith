#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Contracts;
using Content.Shared._WF.Contracts;
using Content.Shared._WF.Encounters;
using Content.Server.GameTicking.Presets;
using Content.Shared.Anomaly.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Repairable;
using Content.Shared.Stacks;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// Every contract template can be posted as written: its strings exist, its goods resolve and have a name, its bounty
/// targets are manual encounters with no warrant of their own, and its wrecks and cargo exist. Every contractor has work
/// and every category but Planetary is offered somewhere.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractPrototype))]
public sealed class ContractPrototypeTest
{
    [Test]
    public async Task EveryContractTemplateIsValid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var problems = new List<string>();
            void Check(bool ok, string id, string what)
            {
                if (!ok)
                    problems.Add($"{id}: {what}");
            }

            var contracts = protos.EnumeratePrototypes<WFContractPrototype>().ToList();
            Assert.That(contracts, Has.Count.GreaterThanOrEqualTo(40), "The board should have plenty of work.");

            foreach (var contractor in protos.EnumeratePrototypes<WFContractorPrototype>())
            {
                Check(loc.HasString(contractor.Name) && loc.HasString(contractor.Description) && loc.HasString(contractor.Employer),
                    contractor.ID, "missing strings");
                Check(contractor.Stations.Count > 0, contractor.ID, "no stations");
                Check(contracts.Any(c => c.Contractor == contractor.ID), contractor.ID, "no contracts");
                Check(contractor.Companies.All(c => protos.HasIndex(c)), contractor.ID, "unknown company");
                Check(contractor.Drift >= 0, contractor.ID, "negative drift");
                Check(contractor.Board == WFContractBoards.Central || protos.EnumeratePrototypes<EntityPrototype>().Any(e =>
                        e.TryComp<WFContractBoardComponent>(out var board, factory) && board.Board == contractor.Board),
                    contractor.ID, $"no board entity posts for board {contractor.Board}");
            }

            foreach (var faction in protos.EnumeratePrototypes<WFContractFactionPrototype>())
            {
                Check(faction.Companies.Count > 0 && faction.Companies.All(c => protos.HasIndex(c)), faction.ID, "unknown or no companies");
            }

            foreach (var category in System.Enum.GetValues<WFContractCategory>())
            {
                if (category != WFContractCategory.Planetary)
                    Check(contracts.Any(c => c.Category == category), category.ToString(), "nothing offered in this category");
            }

            foreach (var contract in contracts)
            {
                var id = contract.ID;
                Check(loc.HasString(contract.Title) && loc.HasString(contract.Briefing), id, "missing title or briefing");
                Check(contract.Tier is >= 1 and <= 5, id, "tier out of 1-5");
                Check(contract.Reward.X > 0 && contract.Reward.X <= contract.Reward.Y, id, "bad reward range");
                Check(contract.Minutes > 0 && contract.Deposit >= 0, id, "bad time or deposit");
                Check(contract.Tier > 1 || contract.Deposit <= 500, id, "a tier 1 contract should cost a new player little or nothing");
                Check(contract.Harms.Keys.All(h => protos.HasIndex(h) && h != contract.Contractor), id, "harms an unknown contractor or its own");
                Check(contract.Credits == 0 || contract.CreditEntity is { } credit && protos.HasIndex<EntityPrototype>(credit), id,
                    "credits without a credit entity");
                Check(contract.MinCrew is >= 1 and <= 8, id, "minimum crew out of 1-8");
                Check(contract.FollowUp is not { } followUp || protos.TryIndex(followUp, out var next) && next.Weight <= 0f, id,
                    "a follow-up must exist and have weight 0, so it is only posted as a follow-up");
                Check(contract.CrewBonus is >= 0f and <= 1f, id, "crew bonus out of 0-1");
                foreach (var required in contract.Requires)
                {
                    Check((required.Entity is { } entity ? protos.HasIndex<EntityPrototype>(entity) : required.Tag is { } tag
                            && protos.HasIndex(tag)) && loc.HasString(required.Name), id,
                        $"unknown required item {required.Entity}{required.Tag} or its name");
                }

                foreach (var preset in contract.ExcludePresets)
                {
                    Check(protos.HasIndex<GamePresetPrototype>(preset), id, $"unknown game preset {preset}");
                }

                void CheckWrecks(List<ResPath> wrecks)
                {
                    Check(wrecks.Count > 0, id, "no wrecks");
                    foreach (var wreck in wrecks)
                    {
                        Check(resources.ContentFileExists(wreck), id, $"missing wreck {wreck}");
                    }
                }

                void CheckInfestation(List<WFContractSpawn> infestation)
                {
                    foreach (var spawn in infestation)
                    {
                        Check(protos.HasIndex<EntityPrototype>(spawn.Entity), id, $"unknown infestation {spawn.Entity}");
                        Check(spawn.Count.X >= 0 && spawn.Count.X <= spawn.Count.Y, id, "bad infestation count");
                    }
                }

                switch (contract.Objective)
                {
                    case WFContractCourier courier:
                        Check(protos.HasIndex<EntityPrototype>(courier.Package), id, $"unknown package {courier.Package}");
                        Check(courier.Count.X >= 1 && courier.Count.X <= courier.Count.Y, id, "bad package count");
                        Check(courier.Destinations.All(d => protos.HasIndex(d)), id, "unknown destination contractor");
                        Check(courier.From != courier.To || courier.From == WFContractEnd.Other, id, "goes nowhere");
                        Check(contract.Category is WFContractCategory.Delivery or WFContractCategory.Hauling, id, "courier outside Delivery or Hauling");
                        break;
                    case WFContractSupply supply:
                        Check(supply.Goods.Count > 0 && supply.Pick <= supply.Goods.Count, id, "bad goods list");
                        Check(protos.HasIndex<EntityPrototype>(supply.Crate), id, "unknown crate");
                        foreach (var goods in supply.Goods)
                        {
                            var kinds = new[] { goods.Stack != null, goods.Entity != null, goods.Tag != null, goods.Component != null }.Count(k => k);
                            Check(kinds == 1, id, "a goods line needs exactly one of stack, entity, tag or component");
                            Check(goods.Count.X >= 1 && goods.Count.X <= goods.Count.Y, id, "bad goods count");
                            Check(goods.Stack == null || protos.HasIndex<StackPrototype>(goods.Stack.Value), id, $"unknown stack {goods.Stack}");
                            Check(goods.Entity == null || protos.HasIndex<EntityPrototype>(goods.Entity.Value), id, $"unknown entity {goods.Entity}");
                            Check(goods.Component == null || factory.TryGetRegistration(goods.Component, out _), id, $"unknown component {goods.Component}");
                            Check(goods.Stack != null || goods.Entity != null || goods.Name != null && goods.Value != null, id,
                                "a tag or component line needs a name and a value");
                            Check(goods.Name == null || loc.HasString(goods.Name.Value), id, "missing goods name string");
                        }
                        break;
                    case WFContractBounty bounty:
                        Check(bounty.Encounters.Count > 0, id, "no targets");
                        Check(bounty.MinDistance > 0 && bounty.MinDistance <= bounty.MaxDistance, id, "bad range");
                        foreach (var target in bounty.Encounters)
                        {
                            if (!protos.TryIndex(target, out var encounter))
                            {
                                Check(false, id, $"unknown target {target}");
                                continue;
                            }

                            Check(encounter.Start == WFEncounterStart.Manual, id, $"{target} could be scheduled on its own");
                            Check(encounter.Bounty == null, id, $"{target} posts a warrant and would pay twice");
                            Check(encounter.Duration >= contract.Minutes * 60f, id, $"{target} ends before the contract's clock");
                        }
                        break;
                    case WFContractEscort escort:
                        Check(escort.Encounters.Count > 0, id, "no escort runs");
                        Check(escort.Destinations.All(d => protos.HasIndex(d)), id, "unknown destination contractor");
                        foreach (var run in escort.Encounters)
                        {
                            if (!protos.TryIndex(run, out var encounter))
                            {
                                Check(false, id, $"unknown escort run {run}");
                                continue;
                            }

                            Check(encounter.Start == WFEncounterStart.Manual && encounter.Bounty == null, id, $"{run} must be manual with no warrant");
                            Check(encounter.Route != null && encounter.StartRadius > 0f, id, $"{run} needs a route and a rendezvous");
                            Check(encounter.Ships.Any(s => s.Key == escort.Ship && s.FlyRoute), id, $"{run} has no ship '{escort.Ship}' flying the route");
                            Check(encounter.Duration >= contract.Minutes * 60f, id, $"{run} ends before the contract's clock");
                        }
                        break;
                    case WFContractRecovery recovery:
                        Check(protos.HasIndex<EntityPrototype>(recovery.Item), id, "unknown item");
                        Check(recovery.To != WFContractEnd.Other, id, "a recovery goes to its contractor or the board");
                        CheckWrecks(recovery.Wrecks);
                        CheckInfestation(recovery.Infestation);
                        break;
                    case WFContractSurvey survey:
                        CheckWrecks(survey.Wrecks);
                        CheckInfestation(survey.Infestation);
                        Check(survey.Count.X >= 1 && survey.Count.X <= survey.Count.Y, id, "bad anomaly count");
                        Check(contract.Category == WFContractCategory.Science, id, "survey outside Science");
                        foreach (var anomaly in survey.Anomalies)
                        {
                            Check(protos.TryIndex(anomaly, out var anomalyProto) && anomalyProto.TryComp<AnomalyComponent>(out _, factory), id,
                                $"{anomaly} is not an anomaly");
                        }
                        break;
                    case WFContractTow tow:
                        CheckWrecks(tow.Wrecks);
                        CheckInfestation(tow.Infestation);
                        Check(tow.Range > 0f && tow.MinDistance <= tow.MaxDistance, id, "bad range");
                        Check(contract.Category == WFContractCategory.Salvage, id, "tow outside Salvage");
                        Check(contract.Requires.Count > 0, id, "a tow should require a tow kit");
                        break;
                    case WFContractMailRun mail:
                        Check(protos.HasIndex<EntityPrototype>(mail.Package), id, "unknown package");
                        Check(mail.Stops.X >= 2 && mail.Stops.X <= mail.Stops.Y, id, "a mail run needs two stops or more");
                        Check(contract.Category == WFContractCategory.Delivery, id, "mail run outside Delivery");
                        break;
                    case WFContractSweep sweep:
                        Check(sweep.Points.X >= 1 && sweep.Points.X <= sweep.Points.Y && sweep.Range > 0f, id, "bad points");
                        Check(sweep.MinDistance > 0 && sweep.MinDistance <= sweep.MaxDistance, id, "bad range");
                        break;
                    case WFContractClear clear:
                        CheckWrecks(clear.Wrecks);
                        CheckInfestation(clear.Infestation);
                        CheckInfestation(new List<WFContractSpawn> { clear.Targets });
                        Check(clear.Targets.Count.X >= 1, id, "nothing to clear");
                        break;
                    case WFContractRepair repair:
                        CheckWrecks(repair.Wrecks);
                        CheckInfestation(repair.Infestation);
                        Check(protos.TryIndex(repair.Device, out var device) && device.TryComp<ItemSlotsComponent>(out _, factory)
                              && device.TryComp<RepairableComponent>(out _, factory), id,
                            "the device needs item slots and to be repairable");
                        break;
                    case WFContractDownload download:
                        CheckWrecks(download.Wrecks);
                        CheckInfestation(download.Waves);
                        Check(protos.HasIndex<EntityPrototype>(download.Terminal) && download.Duration > System.TimeSpan.Zero, id, "bad terminal");
                        Check(download.Duration.TotalMinutes < contract.Minutes, id, "the download outlasts the contract");
                        break;
                    case WFContractInspect inspect:
                        CheckWrecks(inspect.Wrecks);
                        CheckInfestation(inspect.Crew);
                        Check(protos.HasIndex<EntityPrototype>(inspect.Cargo), id, "unknown cargo");
                        break;
                    case WFContractOperation operation:
                        CheckWrecks(operation.Wrecks);
                        CheckInfestation(operation.Infestation);
                        Check(protos.HasIndex<EntityPrototype>(operation.Item), id, "unknown item");
                        Check(contract.Requires.Count > 0, id, "an operation tows, so it should require a tow kit");
                        foreach (var anomaly in operation.Anomalies)
                        {
                            Check(protos.TryIndex(anomaly, out var anomalyProto) && anomalyProto.TryComp<AnomalyComponent>(out _, factory), id,
                                $"{anomaly} is not an anomaly");
                        }
                        break;
                    default:
                        Check(false, id, "objective the board cannot run");
                        break;
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crew's slots double the crew they need each, starting at one; standing drifts toward neutral.</summary>
    [Test]
    public void SlotsAndDrift()
    {
        Assert.That(new[] { 1, 2, 3, 4, 7, 8, 16 }.Select(WFContractSystem.SlotsFor), Is.EqualTo(new[] { 1, 2, 2, 3, 3, 4, 5 }));
        Assert.That(WFContractSystem.NextSlotAt(1), Is.EqualTo(2));
        Assert.That(WFContractSystem.NextSlotAt(5), Is.EqualTo(8));
        Assert.That(WFContractSystem.Drifted(5, 1), Is.EqualTo(4));
        Assert.That(WFContractSystem.Drifted(-3, 2), Is.EqualTo(-1));
        Assert.That(WFContractSystem.Drifted(1, 3), Is.EqualTo(0));
        Assert.That(WFContractSystem.RankOf(-6), Is.EqualTo(0), "At -6 a contractor should be hostile.");
        Assert.That(WFContractSystem.RankOf(-1), Is.EqualTo(1), "Below zero a contractor should distrust you.");
        Assert.That(WFContractSystem.RankForTier(1), Is.EqualTo(1), "Tier 1 should be open to the distrusted.");

        // Crew pay: the bonus counts crewmates past the minimum, at most four of them.
        var mining = new WFContractPrototype { MinCrew = 1, CrewBonus = 0.5f };
        Assert.That(WFContractSystem.CrewPool(10000, mining, 1), Is.EqualTo(10000));
        Assert.That(WFContractSystem.CrewPool(10000, mining, 3), Is.EqualTo(20000));
        Assert.That(WFContractSystem.CrewPool(10000, mining, 9), Is.EqualTo(30000), "The bonus should stop at four extra crewmates.");
        var bulk = new WFContractPrototype { MinCrew = 3, CrewBonus = 0.35f };
        Assert.That(WFContractSystem.CrewPool(40000, bulk, 3), Is.EqualTo(40000));
        Assert.That(WFContractSystem.CrewPool(40000, bulk, 4), Is.EqualTo(54000));
        Assert.That(WFContractSystem.CrewPool(10000, new WFContractPrototype(), 4), Is.EqualTo(10000), "No bonus, no growth.");
    }

    /// <summary>Standing is stored per character and contractor, and a second save updates the row.</summary>
    [Test]
    public async Task StandingIsStored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = pair.Server.ResolveDependency<Content.Server.Database.IServerDbManager>();
        var user = System.Guid.NewGuid();
        await db.SetContractStandingAsync(user, "Stored Contractor", "WFContractorMMC", 4, 7);
        await db.SetContractStandingAsync(user, "Stored Contractor", "WFContractorMMC", -2, 8);
        await db.SetContractStandingAsync(user, "Other Character", "WFContractorMMC", 9, 8);
        var stored = await db.GetContractStandingAsync(user, "Stored Contractor");
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.That(stored[0].Points, Is.EqualTo(-2));
        Assert.That(stored[0].LastRound, Is.EqualTo(8));
        await pair.CleanReturnAsync();
    }
}
