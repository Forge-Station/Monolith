#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Challenges: a contest on a cell another faction holds, decided by presence and transferring the cell.</summary>
[TestOf(typeof(WFSectorTerritorySystem))]
public sealed class WFSectorChallengeTest : WFSectorTestBase
{
    private static readonly WFSectorCell Far = new(4, -2);
    private static readonly WFSectorCell Next = new(5, -2);
    private static readonly WFSectorCell Opposite = new(-4, 2);
    private static readonly WFSectorCell Empty = new(-5, 2);

    [TearDown]
    public async Task ClearTerritory()
    {
        await Server.WaitPost(() =>
        {
            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Server.System<WFSectorTestSystem>().Reset();
        });
    }

    private EntityUid SpawnSource()
    {
        return SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
    }

    /// <summary>Runs the server, reporting presence as drivers would, until no contest is left on the cells.</summary>
    private async Task RunOut(WFSectorCell[] rival, WFSectorCell[] holder, params WFSectorCell[] cells)
    {
        await WaitUntilServer(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            territory.SetPresence(Rival, MapData.MapId, rival);
            territory.SetPresence(Holder, MapData.MapId, holder);
            return cells.All(cell => territory.ContestOf(MapData.MapId, cell) == null);
        }, 900);
    }

    /// <summary>A challenge needs another faction's uncontested cell, a known faction and a live source; contest still refuses it.</summary>
    [Test]
    public async Task ChallengeRefusesWhatItCannotTake()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            var deadline = STiming.CurTime + TimeSpan.FromMinutes(5);
            recorder.Reset();

            Assert.That(territory.Challenge(map, Far, Rival, deadline, null), Is.False, "An unheld cell is contested, not challenged.");
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.Challenge(map, Far, Holder, deadline, null), Is.False, "Not the holder's own cell.");
            Assert.That(territory.Challenge(map, Far, "WFTestSectorNobody", deadline, null), Is.False, "Not an unknown faction.");
            var dead = SpawnSource();
            SEntMan.DeleteEntity(dead);
            Assert.That(territory.Challenge(map, Far, Rival, deadline, dead), Is.False, "Not with a dead source.");
            Assert.That(territory.Contest(map, Far, Rival, deadline), Is.False, "Contest still refuses another faction's cell.");

            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Next, Holder, deadline), Is.True);
            Assert.That(territory.Challenge(map, Next, Rival, deadline, null), Is.False, "Not a contested cell.");

            Assert.That(territory.Challenge(map, Far, Rival, deadline, null), Is.True);
            Assert.That(territory.Challenge(map, Far, Rival, deadline, null), Is.False, "Already challenged.");
            var contest = territory.ContestOf(map, Far)!;
            Assert.That(contest.Faction.Id, Is.EqualTo(Rival));
            Assert.That(contest.Holder?.Id, Is.EqualTo(Holder));
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder), "Challenged, it is still held.");
            var contested = recorder.Contested.Last();
            Assert.That((contested.Cell, contested.Faction.Id, contested.Holder?.Id), Is.EqualTo((Far, Rival, Holder)));
            Assert.That(recorder.Contested.Single(entry => entry.Cell == Next).Holder, Is.Null, "A plain contest has no holder.");
            Assert.That(territory.TryClaim(map, Far, Rival, null), Is.False, "Nor can the challenger claim it outright.");

            var later = deadline + TimeSpan.FromMinutes(1);
            Assert.That(territory.SetContestDeadline(map, Far, later), Is.True);
            Assert.That(recorder.Contested.Last().Holder?.Id, Is.EqualTo(Holder), "A moved deadline keeps the holder.");
            Assert.That(territory.DropContest(map, Far), Is.True);
            var dropped = recorder.Resolved.Single();
            Assert.That((dropped.Claimed, dropped.Holder?.Id), Is.EqualTo((false, Holder)));
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder));

            Server.ConsoleHost.ExecuteCommand($"wf_sector challenge {Rival} 4 -2 5");
            Assert.That(territory.ContestOf(map, Far)?.Holder?.Id, Is.EqualTo(Holder), "The admin command challenges.");
            Server.ConsoleHost.ExecuteCommand($"wf_sector challenge {Holder} 4 -2 5");
            Server.ConsoleHost.ExecuteCommand("wf_sector status");
        });
    }

    /// <summary>
    /// At the deadline the challenger takes the cell if its ships are there and the holder's are not, with no clearing;
    /// the holder's ships there, or the challenger's absence, drop it.
    /// </summary>
    [Test]
    public async Task ChallengeIsDecidedByPresence()
    {
        await StartRound();
        EntityUid source = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var deadline = STiming.CurTime + TimeSpan.FromSeconds(2);
            source = SpawnSource();
            foreach (var cell in new[] { Far, Next, Opposite })
            {
                Assert.That(territory.TryClaim(map, cell, Holder, SpawnSource()), Is.True);
            }

            Server.System<WFSectorTestSystem>().Reset();
            Assert.That(territory.Challenge(map, Far, Rival, deadline, source), Is.True);
            Assert.That(territory.Challenge(map, Next, Rival, deadline, null), Is.True);
            Assert.That(territory.Challenge(map, Opposite, Rival, deadline, null), Is.True);
        });

        // The challenger is in Far and Next, the holder in Next; nobody is in Opposite.
        await RunOut(new[] { Far, Next }, new[] { Next }, Far, Next, Opposite);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;

            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Rival), "Won: the cell transfers.");
            Assert.That(territory.Sources(map, Far), Is.EquivalentTo(new[] { source }), "The challenger's source supports it.");
            Assert.That(territory.ClearingOf(map, Far), Is.Null, "A transfer starts no clearing.");
            Assert.That(recorder.Clearing.Any(entry => entry.Cell == Far), Is.False);
            var changed = recorder.Changed.Single(entry => entry.Cell == Far);
            Assert.That((changed.Previous?.Id, changed.Owner?.Id), Is.EqualTo((Holder, Rival)));
            var won = recorder.Resolved.Single(entry => entry.Cell == Far);
            Assert.That((won.Faction.Id, won.Claimed, won.Holder?.Id), Is.EqualTo((Rival, true, Holder)));

            Assert.That(territory.Owner(map, Next)?.Id, Is.EqualTo(Holder), "The holder's ships keep it.");
            var held = recorder.Resolved.Single(entry => entry.Cell == Next);
            Assert.That((held.Claimed, held.Holder?.Id), Is.EqualTo((false, Holder)));

            Assert.That(territory.Owner(map, Opposite)?.Id, Is.EqualTo(Holder), "An empty cell is not taken by a challenge.");
            Assert.That(recorder.Resolved.Single(entry => entry.Cell == Opposite).Claimed, Is.False);
            Assert.That(recorder.Changed, Has.Count.EqualTo(1), "Only the won cell changed hands.");

            territory.SetPresence(Rival, map, Array.Empty<WFSectorCell>());
            territory.SetPresence(Holder, map, Array.Empty<WFSectorCell>());
        });
    }

    /// <summary>A challenge whose source dies, or whose holder loses the cell first, is dropped; a freed cell is not transferred.</summary>
    [Test]
    public async Task ChallengeIsDroppedWhenItsSourceOrTheHoldGoes()
    {
        await StartRound();
        EntityUid released = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Server.System<WFSectorTestSystem>().Reset();
            var deadline = STiming.CurTime + TimeSpan.FromMinutes(5);
            var source = SpawnSource();
            released = SpawnSource();

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, released), Is.True);
            Assert.That(territory.TryClaim(map, Opposite, Holder, null), Is.True);
            Assert.That(territory.Challenge(map, Far, Rival, deadline, source), Is.True);
            Assert.That(territory.Challenge(map, Next, Rival, deadline, null), Is.True);
            Assert.That(territory.Challenge(map, Opposite, Rival, deadline, null), Is.True);

            SEntMan.DeleteEntity(source);
            Assert.That(territory.Release(released), Is.EqualTo(1), "The holder's stronghold dies.");
            Assert.That(territory.Free(map, Opposite), Is.True);
            Assert.That(territory.Owner(map, Opposite), Is.Null, "Freeing a challenged cell does not transfer it.");
        });

        await RunOut(new[] { Far, Next, Opposite }, Array.Empty<WFSectorCell>(), Far, Next, Opposite);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;

            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder), "Its source died: dropped.");
            Assert.That(territory.Owner(map, Next), Is.Null, "The hold went first: dropped, not transferred.");
            Assert.That(territory.Owner(map, Opposite), Is.Null);
            foreach (var cell in new[] { Far, Next, Opposite })
            {
                var resolved = recorder.Resolved.Single(entry => entry.Cell == cell);
                Assert.That((resolved.Claimed, resolved.Holder?.Id), Is.EqualTo((false, Holder)), WFSectorHex.Callsign(cell));
            }

            Assert.That(recorder.Changed.Any(entry => entry.Owner?.Id == Rival), Is.False);
            territory.SetPresence(Rival, map, Array.Empty<WFSectorCell>());
        });
    }

    /// <summary>A vetoed or protected cell cannot be challenged.</summary>
    [Test]
    public async Task ProtectedCellsCannotBeChallenged()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var map = MapData.MapId;
            var deadline = STiming.CurTime + TimeSpan.FromMinutes(5);
            recorder.Reset();

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            recorder.Vetoed.Add(Far);
            Assert.That(territory.Challenge(map, Far, Rival, deadline, null), Is.False, "The claim-attempt veto applies.");
            Assert.That(recorder.Attempts.Last().Faction.Id, Is.EqualTo(Rival));
            recorder.Vetoed.Clear();

            SpawnStation(CentreOf(Empty), "Trade Post");
            Assert.That(territory.TryClaim(map, Empty, Holder, null), Is.True);
            config.SetCVar(SectorControlCVars.ProtectedStations, "Trade");
            try
            {
                Assert.That(territory.Challenge(map, Empty, Rival, deadline, null), Is.False, "A protected station's cell.");
                Assert.That(territory.ContestOf(map, Empty), Is.Null);
                Assert.That(territory.ContestOf(map, Far), Is.Null);
            }
            finally
            {
                config.SetCVar(SectorControlCVars.ProtectedStations, SectorControlCVars.ProtectedStations.DefaultValue);
            }
        });
    }

    /// <summary>The client gets a challenge as a contest on a held cell, keeps the holder's fill and names both factions.</summary>
    [Test]
    public async Task ClientShowsTheChallenge()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Next, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.True);
            Assert.That(territory.Challenge(map, Far, Rival, STiming.CurTime + TimeSpan.FromMinutes(5), null), Is.True);

            var ev = Server.System<WFSectorSyncSystem>().Build(map);
            Assert.That(ev.Claims.Select(entry => (entry.Cell, ev.Factions[entry.Faction])), Does.Contain((Far, Holder)));
            Assert.That(ev.Contests.Select(entry => (entry.Cell, ev.Factions[entry.Faction])), Does.Contain((Far, Rival)));
        });

        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out var view), Is.True);
            Assert.That(view.Names[view.Owners[Far]], Is.EqualTo("Faction id"), "The holder's fill stays.");
            Assert.That(view.Contests.Any(contest => contest.Cell == Far), Is.True, "With the challenger's countdown over it.");

            var map = new WFSectorMapControl();
            Assert.That(map.Readout(view, CentreOf(Far)), Does.Contain("held by Faction id, challenged by Subcommand, "));
            Assert.That(map.Readout(view, CentreOf(Next)), Does.Contain("contested by Faction id, "), "A holder's own contest reads as before.");
        });
    }
}
