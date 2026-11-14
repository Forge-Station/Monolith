#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Disputed cells: a faction's ship and a crewed player ship in one cell hold its timers and mark it on the map.</summary>
[TestOf(typeof(WFSectorTerritorySystem))]
public sealed class WFSectorDisputeTest : WFSectorTestBase
{
    private static readonly WFSectorCell Far = new(4, -2);
    private static readonly WFSectorCell Next = new(5, -2);

    /// <summary>Presence with a player ship in the cell disputes it and raises the event; the ship leaving, or the report going stale, ends it.</summary>
    [Test]
    public async Task PresenceAndPlayerShipMarkACellDisputed()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);

            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.False, "No player ship there yet.");
            Assert.That(recorder.Disputed, Is.Empty);

            var ship = SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.True);
            Assert.That(territory.IsDisputed(map, Next), Is.False);
            Assert.That(recorder.Disputed.Single(), Is.EqualTo(new WFSectorCellDisputedEvent(map, Far, Holder, true)));

            territory.CheckDisputes(map);
            Assert.That(recorder.Disputed, Has.Count.EqualTo(1), "Only a change raises the event.");

            Disembark();
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.False, "The player ship left.");
            Assert.That(recorder.Disputed.Last().Disputed, Is.False);

            Board(ship);
            territory.SetPresence(Holder, map, new[] { Next });
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.False, "The faction's ships are elsewhere.");
            territory.Clear(map);
            recorder.Reset();
        });
    }

    /// <summary>A contest's deadline stands still while its cell is disputed, and is decided as usual once the dispute ends.</summary>
    [Test]
    public async Task ContestDeadlineHoldsWhileDisputed()
    {
        await StartRound();
        TimeSpan deadline = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Server.System<WFSectorTestSystem>().Reset();
            var map = MapData.MapId;
            deadline = STiming.CurTime + TimeSpan.FromSeconds(4);
            Assert.That(territory.Contest(map, Far, Holder, deadline), Is.True);
            var ship = SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckDisputes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.True);
        });

        for (var second = 0; second < 8; second++)
        {
            await Server.WaitPost(() => Server.System<WFSectorTerritorySystem>().SetPresence(Holder, MapData.MapId, new[] { Far }));
            await RunTicks(30);
        }

        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var contest = territory.ContestOf(MapData.MapId, Far);
            Assert.That(contest, Is.Not.Null, "Past its deadline, the contest still stands while disputed.");
            Assert.That(contest!.Deadline - STiming.CurTime, Is.GreaterThan(TimeSpan.FromSeconds(2)), "The countdown held.");
            Assert.That(contest.Deadline, Is.GreaterThan(deadline + TimeSpan.FromSeconds(4)));
            Disembark();
        });

        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.IsDisputed(MapData.MapId, Far), Is.False);
            Assert.That(territory.ContestOf(MapData.MapId, Far), Is.Null, "With the dispute over the deadline runs out and the contest is decided.");
            Assert.That(Server.System<WFSectorTestSystem>().Resolved.Any(entry => entry.Cell == Far), Is.True);
            Server.System<WFSectorTestSystem>().Reset();
        });
    }

    /// <summary>The territory event lists the disputed cell with its faction, and the client view and hover card show it.</summary>
    [Test]
    public async Task SyncCarriesTheDisputedCellToTheClient()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            var ship = SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler");
            Board(ship);
            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckDisputes(map);

            var ev = Server.System<WFSectorSyncSystem>().Build(map);
            var entry = ev.Disputed.Single();
            Assert.That(entry.Cell, Is.EqualTo(Far));
            Assert.That(ev.Factions[entry.Faction], Is.EqualTo(Holder));
        });

        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True);
            Assert.That(view.Disputes.Keys, Is.EquivalentTo(new[] { Far }));
            var dispute = view.Disputed.Single();
            Assert.That(dispute.Cell, Is.EqualTo(Far));
            Assert.That(dispute.Hatch, Is.Not.Empty, "The cell is hatched.");
            Assert.That(dispute.Hatch.Length % 2, Is.Zero);
            Assert.That(dispute.Outline, Has.Length.EqualTo(12));
            Assert.That(view.Disputes.ContainsKey(Next), Is.False);
        });

        await Server.WaitAssertion(() =>
        {
            Disembark();
            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Server.System<WFSectorTerritorySystem>().CheckDisputes(MapData.MapId);
        });
        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out var view), Is.True);
            Assert.That(view.Disputes, Is.Empty, "The dispute ended and the client dropped it.");
        });
    }
}
