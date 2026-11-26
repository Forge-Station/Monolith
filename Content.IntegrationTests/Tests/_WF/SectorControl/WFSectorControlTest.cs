#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Sector control: claims and their sources, the protection veto, blockaded stations, contests and the admin command.</summary>
[TestOf(typeof(WFSectorTerritorySystem))]
public sealed class WFSectorControlTest : WFSectorTestBase
{
    private static readonly WFSectorCell Far = new(4, -2);
    private static readonly WFSectorCell Next = new(5, -2);
    private static readonly WFSectorCell Opposite = new(-4, 2);

    /// <summary>A claim lasts while a source does, a claim with none until freed; a faction's own cell takes more sources.</summary>
    [Test]
    public async Task ClaimsLastWhileTheirSourcesDo()
    {
        await StartRound();
        EntityUid first = default, second = default, third = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var at = new EntityCoordinates(MapData.MapUid, Vector2.Zero);
            first = SEntMan.SpawnEntity(null, at);
            second = SEntMan.SpawnEntity(null, at);
            third = SEntMan.SpawnEntity(null, at);

            Assert.That(territory.Owner(map, Far), Is.Null);
            Assert.That(territory.CanClaim(map, Far, Holder, first), Is.True);
            Assert.That(territory.TryClaim(map, Far, Holder, first), Is.True);
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder));
            Assert.That(territory.Sources(map, Far), Is.EquivalentTo(new[] { first }));
            Assert.That(territory.TryClaim(map, Far, Holder, second), Is.True, "A faction adds sources to its own cell.");
            Assert.That(territory.Sources(map, Far), Has.Count.EqualTo(2));
            Assert.That(territory.TryClaim(map, Far, Rival, null), Is.False, "Another faction's cell cannot be claimed.");
            Assert.That(territory.TryClaim(map, Next, Rival, null), Is.True);

            Assert.That(territory.Release(first), Is.Zero, "The cell stays while a source remains.");
            Assert.That(territory.Owner(map, Far), Is.Not.Null);
            Assert.That(territory.Release(second), Is.EqualTo(1));
            Assert.That(territory.Owner(map, Far), Is.Null, "Losing the last source frees the cell.");
            Assert.That(territory.Owner(map, Next)?.Id, Is.EqualTo(Rival), "Other cells are not touched.");

            // A claim whose source is deleted is freed by the next check; one made with none stays.
            Assert.That(territory.TryClaim(map, Far, Holder, third), Is.True);
            Assert.That(territory.TryClaim(map, Opposite, Holder, null), Is.True);
            SEntMan.DeleteEntity(third);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.Owner(MapData.MapId, Far), Is.Null, "A claim does not outlive its source.");
            Assert.That(territory.Owner(MapData.MapId, Opposite)?.Id, Is.EqualTo(Holder), "A claim with no source stays until freed.");
            Assert.That(territory.TryClaim(MapData.MapId, Opposite, Holder, third), Is.False, "A deleted source claims nothing.");
        });
    }

    /// <summary>Held, frontier, free and clear work on cells and factions.</summary>
    [Test]
    public async Task HeldFrontierFreeAndClear()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.CellOf(new MapCoordinates(CentreOf(Far), map)), Is.EqualTo(Far));
            Assert.That(territory.Centre(map, Far).Position, Is.EqualTo(CentreOf(Far)));

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Opposite, Rival, null), Is.True);
            Assert.That(territory.Held(map, Holder), Is.EquivalentTo(new[] { Far, Next }));
            Assert.That(territory.Held(map, Rival), Is.EquivalentTo(new[] { Opposite }));

            // Two neighbouring cells have eight unheld neighbours between them.
            var frontier = territory.Frontier(map, Holder);
            Assert.That(frontier, Has.Count.EqualTo(8));
            Assert.That(frontier, Does.Not.Contain(Far).And.Not.Contain(Next));

            Assert.That(territory.Free(map, Far), Is.True);
            Assert.That(territory.Free(map, Far), Is.False, "Freeing a free cell does nothing.");
            Assert.That(territory.Clear(map, Rival), Is.EqualTo(1));
            Assert.That(territory.Held(map, Holder), Is.EquivalentTo(new[] { Next }), "Clearing a faction leaves the others.");
            Assert.That(territory.Clear(map), Is.EqualTo(1));
            Assert.That(territory.Held(map, Holder), Is.Empty);
        });
    }

    /// <summary>With sector control off nothing can be claimed and what was held is gone.</summary>
    [Test]
    public async Task SwitchingItOffClearsTheTerritory()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Next, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.True);

            config.SetCVar(SectorControlCVars.Enabled, false);
            Assert.That(territory.Enabled, Is.False);
            Assert.That(territory.Held(map, Holder), Is.Empty);
            Assert.That(territory.ContestOf(map, Next), Is.Null);
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.False);
            Assert.That(territory.CanClaim(map, Far, Holder), Is.False);

            config.SetCVar(SectorControlCVars.Enabled, true);
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);

            // A new cell size makes the old cells meaningless.
            config.SetCVar(SectorControlCVars.CellSize, 2000f);
            Assert.That(territory.CellSize, Is.EqualTo(2000f));
            Assert.That(territory.Held(map, Holder), Is.Empty);
            config.SetCVar(SectorControlCVars.CellSize, SectorControlCVars.CellSize.DefaultValue);
        });
    }

    /// <summary>A veto stops claims and contests, for every driver; the core and a named station are vetoed by the framework.</summary>
    [Test]
    public async Task ProtectedCellsRefuseEveryClaim()
    {
        await StartRound();
        var named = new WFSectorCell(3, -1);
        var plain = new WFSectorCell(-3, 1);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var protection = Server.System<WFSectorProtectionSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var map = MapData.MapId;

            foreach (var cell in WFSectorHex.Within(WFSectorCell.Origin, WFSectorProtectionSystem.CoreRadius))
            {
                Assert.That(protection.IsProtected(map, cell), Is.True, $"{WFSectorHex.Callsign(cell)} is core");
                Assert.That(territory.CanClaim(map, cell, Holder), Is.False);
                Assert.That(territory.TryClaim(map, cell, Holder, null), Is.False);
                Assert.That(territory.Contest(map, cell, Holder, STiming.CurTime + TimeSpan.FromMinutes(1)), Is.False);
            }

            var outside = new WFSectorCell(WFSectorProtectionSystem.CoreRadius + 1, 0);
            Assert.That(protection.IsProtected(map, outside), Is.False);

            // A station named in the cvar protects its cell.
            SpawnStation(CentreOf(named), "Halcyon Fleet Command");
            SpawnStation(CentreOf(plain), "Trade Post");
            Assert.That(protection.IsProtected(map, named), Is.True);
            Assert.That(protection.IsProtected(map, plain), Is.False);
            Assert.That(territory.TryClaim(map, named, Holder, null), Is.False);
            Assert.That(territory.TryClaim(map, plain, Holder, null), Is.True);
            Assert.That(territory.Free(map, plain), Is.True);

            config.SetCVar(SectorControlCVars.ProtectedStations, "Trade");
            Assert.That(protection.IsProtected(map, plain), Is.True);
            Assert.That(protection.IsProtected(map, named), Is.False);
            config.SetCVar(SectorControlCVars.ProtectedStations, SectorControlCVars.ProtectedStations.DefaultValue);
        });
    }

    /// <summary>Any system may veto a claim or contest through the attempt event; the cell stays free, and nothing is raised for it.</summary>
    [Test]
    public async Task AnyDriverCanVetoAClaim()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();

            recorder.Vetoed.Add(Far);
            Assert.That(territory.CanClaim(map, Far, Holder), Is.False);
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.False);
            Assert.That(territory.Contest(map, Far, Holder, STiming.CurTime + TimeSpan.FromMinutes(1)), Is.False);
            Assert.That(territory.Owner(map, Far), Is.Null);
            Assert.That(territory.ContestOf(map, Far), Is.Null);
            Assert.That(recorder.Changed, Is.Empty);
            Assert.That(recorder.Attempts.Count(attempt => attempt.Cell == Far), Is.EqualTo(3));

            recorder.Vetoed.Clear();
            var source = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Assert.That(territory.TryClaim(map, Far, Holder, source), Is.True);
            var changed = recorder.Changed.Single();
            Assert.That(changed.Cell, Is.EqualTo(Far));
            Assert.That(changed.Previous, Is.Null);
            Assert.That(changed.Owner?.Id, Is.EqualTo(Holder));
            Assert.That(recorder.Attempts.Last().Source, Is.EqualTo(source));

            Assert.That(territory.Free(map, Far), Is.True);
            Assert.That(recorder.Changed.Last().Previous?.Id, Is.EqualTo(Holder));
            Assert.That(recorder.Changed.Last().Owner, Is.Null);
            recorder.Reset();
        });
    }

    /// <summary>A station in a cell a blockading faction holds leaves the storyteller's list, but not the sector's.</summary>
    [Test]
    public async Task BlockadedStationsLeaveTheStorytellersList()
    {
        await StartRound();
        EntityUid station = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            var map = MapData.MapId;
            station = SpawnStation(CentreOf(Far), "Outpost Kappa");

            Assert.That(scheduler.Stations(map).Select(entry => entry.Owner), Does.Contain(station));
            Assert.That(territory.StationsIn(map, Far).Select(entry => entry.Owner), Is.EquivalentTo(new[] { station }));
            Assert.That(territory.StationsIn(map, Next), Is.Empty);

            // A faction that does not blockade leaves it be.
            Assert.That(territory.TryClaim(map, Far, Rival, null), Is.True);
            Assert.That(scheduler.Stations(map).Select(entry => entry.Owner), Does.Contain(station));
            Assert.That(territory.Free(map, Far), Is.True);

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(scheduler.Stations(map).Select(entry => entry.Owner), Does.Not.Contain(station), "Blockaded.");
            Assert.That(territory.Stations(map).Select(entry => entry.Owner), Does.Contain(station), "Still a station to the sector.");
            Assert.That(territory.StationsIn(map, Far).Select(entry => entry.Owner), Is.EquivalentTo(new[] { station }));

            Assert.That(territory.Free(map, Far), Is.True);
            Assert.That(scheduler.Stations(map).Select(entry => entry.Owner), Does.Contain(station), "Freed, it is back.");
        });
    }

    /// <summary>A contest is claimed at its deadline if the cell was quiet, and dropped if it was flown or its source is gone.</summary>
    [Test]
    public async Task ContestIsDecidedAtItsDeadline()
    {
        await StartRound();
        var quiet = new WFSectorCell(4, -2);
        var flown = new WFSectorCell(6, -3);
        var orphan = new WFSectorCell(-4, 2);
        var dropped = new WFSectorCell(-6, 3);
        EntityUid source = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var activity = Server.System<WFSectorActivitySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            var deadline = STiming.CurTime + TimeSpan.FromSeconds(4);
            source = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, Vector2.Zero));

            Assert.That(territory.Contest(map, quiet, Holder, deadline), Is.True);
            Assert.That(territory.Contest(map, flown, Holder, deadline), Is.True);
            Assert.That(territory.Contest(map, orphan, Holder, deadline, source), Is.True);
            Assert.That(territory.Contest(map, dropped, Holder, deadline), Is.True);
            Assert.That(territory.Contest(map, quiet, Holder, deadline), Is.False, "Already contested.");
            Assert.That(territory.ContestOf(map, quiet)!.Deadline, Is.EqualTo(deadline));
            Assert.That(territory.TryClaim(map, quiet, Rival, null), Is.False, "Nobody else may take a contested cell.");
            Assert.That(recorder.Contested.Select(entry => entry.Cell), Is.EquivalentTo(new[] { quiet, flown, orphan, dropped }));
            Assert.That(territory.Owner(map, quiet), Is.Null, "Contested is not yet held.");

            activity.Stamp(map, flown);
            SEntMan.DeleteEntity(source);
            Assert.That(territory.DropContest(map, dropped), Is.True);
            Assert.That(territory.DropContest(map, dropped), Is.False);
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            Assert.That(territory.Owner(map, quiet)?.Id, Is.EqualTo(Holder), "A quiet cell is taken at the deadline.");
            Assert.That(territory.Owner(map, flown), Is.Null, "A flown cell is not.");
            Assert.That(territory.Owner(map, orphan), Is.Null, "Nor is one whose source is gone.");
            foreach (var cell in new[] { quiet, flown, orphan, dropped })
            {
                Assert.That(territory.ContestOf(map, cell), Is.Null, $"{WFSectorHex.Callsign(cell)} is decided");
            }

            Assert.That(recorder.Resolved.Single(entry => entry.Cell == quiet).Claimed, Is.True);
            Assert.That(recorder.Resolved.Single(entry => entry.Cell == flown).Claimed, Is.False);
            Assert.That(recorder.Resolved.Single(entry => entry.Cell == orphan).Claimed, Is.False);
            Assert.That(recorder.Resolved.Single(entry => entry.Cell == dropped).Claimed, Is.False, "A dropped contest is resolved unclaimed.");
            recorder.Reset();
        });
    }

    /// <summary>A faction may contest a cell it holds, which stays held; another faction may not, and the deadline can be moved.</summary>
    [Test]
    public async Task FactionMayContestItsOwnCell()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            var deadline = STiming.CurTime + TimeSpan.FromMinutes(5);
            var later = deadline + TimeSpan.FromMinutes(2);

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Far, Rival, deadline), Is.False, "Not another faction's cell.");
            Assert.That(territory.Contest(map, Far, Holder, deadline), Is.True);
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder), "Contested, it is still held.");

            Assert.That(territory.SetContestDeadline(map, Far, later), Is.True);
            Assert.That(territory.ContestOf(map, Far)!.Deadline, Is.EqualTo(later));
            Assert.That(recorder.Contested.Select(entry => entry.Deadline), Is.EqualTo(new[] { deadline, later }), "The move is sent like a contest.");
            Assert.That(territory.SetContestDeadline(map, Next, later), Is.False, "No contest there to move.");

            Assert.That(territory.DropContest(map, Far), Is.True);
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder), "Dropping the contest leaves the hold.");
            territory.Clear(map);
            recorder.Reset();
        });
    }

    /// <summary>The admin command claims, frees, contests and clears cells, and refuses what the framework refuses.</summary>
    [Test]
    public async Task CommandDrivesTheTerritory()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var console = Server.ConsoleHost;
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;

            console.ExecuteCommand($"wf_sector claim {Holder} 4 -2");
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder));
            console.ExecuteCommand($"wf_sector claim {Rival} 4 -2");
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder), "Another faction's cell is not taken.");
            console.ExecuteCommand($"wf_sector claim {Holder} 0 0");
            Assert.That(territory.Owner(map, WFSectorCell.Origin), Is.Null, "The core is refused.");
            console.ExecuteCommand("wf_sector claim Nonsense 5 -2");
            console.ExecuteCommand($"wf_sector claim {Holder} five -2");
            Assert.That(territory.Owner(map, Next), Is.Null);

            console.ExecuteCommand("wf_sector free 4 -2");
            Assert.That(territory.Owner(map, Far), Is.Null);

            console.ExecuteCommand($"wf_sector contest {Holder} 5 -2 5");
            Assert.That(territory.ContestOf(map, Next)?.Faction.Id, Is.EqualTo(Holder));
            var due = territory.ContestOf(map, Next)!.Deadline - STiming.CurTime;
            Assert.That(due.TotalSeconds, Is.InRange(290, 301), "Five minutes.");
            console.ExecuteCommand($"wf_sector contest {Holder} -4 2 soon");
            Assert.That(territory.ContestOf(map, Opposite), Is.Null);
            console.ExecuteCommand("wf_sector free 5 -2");
            Assert.That(territory.ContestOf(map, Next), Is.Null, "Freeing a contested cell drops the contest.");

            console.ExecuteCommand($"wf_sector claim {Holder} 4 -2");
            console.ExecuteCommand($"wf_sector claim {Rival} -4 2");
            console.ExecuteCommand($"wf_sector clear {Rival}");
            Assert.That(territory.Held(map, Rival), Is.Empty);
            Assert.That(territory.Held(map, Holder), Is.EquivalentTo(new[] { Far }));
            console.ExecuteCommand("wf_sector status");
            console.ExecuteCommand("wf_sector callsign 0 0");
            console.ExecuteCommand("wf_sector callsign x y");
            console.ExecuteCommand("wf_sector clear");
            Assert.That(territory.Held(map, Holder), Is.Empty);
        });
    }

    /// <summary>The free command clears a held cell and the contest on it in one go.</summary>
    [Test]
    public async Task FreeClearsAHeldCellAndItsContest()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var console = Server.ConsoleHost;
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Far, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.True, "A faction may contest its own cell.");

            console.ExecuteCommand("wf_sector free 4 -2");
            Assert.That(territory.Owner(map, Far), Is.Null);
            Assert.That(territory.ContestOf(map, Far), Is.Null, "The contest goes with the claim.");
        });
    }
}
