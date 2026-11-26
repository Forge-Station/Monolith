#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Retaking: a crewed player ship sitting in a held cell with none of the holder's ships frees it after a delay.</summary>
[TestOf(typeof(WFSectorTerritorySystem))]
public sealed class WFSectorRetakeTest : WFSectorTestBase
{
    private static readonly WFSectorCell Far = new(4, -2);
    private static readonly WFSectorCell Next = new(5, -2);

    private IConfigurationManager ServerConfig => Server.ResolveDependency<IConfigurationManager>();

    /// <summary>Puts the retake delay, in minutes.</summary>
    private async Task SetDelay(float minutes)
    {
        await Server.WaitPost(() => ServerConfig.SetCVar(SectorControlCVars.RetakeMinutes, minutes));
    }

    [TearDown]
    public async Task ResetRetakes()
    {
        await Server.WaitPost(() =>
        {
            ServerConfig.SetCVar(SectorControlCVars.RetakeMinutes, SectorControlCVars.RetakeMinutes.DefaultValue);
            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Server.System<WFSectorTestSystem>().Reset();
            Disembark();
        });
    }

    /// <summary>The countdown starts when a crewed ship is in the cell and ends with the cell freed, an event after, and the cell marked active so spread does not take it back.</summary>
    [Test]
    public async Task HeldCellIsFreedAfterTheDelay()
    {
        await StartRound();
        await SetDelay(0.05f);
        EntityUid hauler = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);

            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "No player ship there yet.");

            hauler = SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler");
            Board(hauler);
            territory.CheckRetakes(map);
            var deadline = territory.RetakeOf(map, Far);
            Assert.That(deadline, Is.EqualTo(STiming.CurTime + TimeSpan.FromSeconds(3)));
            Assert.That(territory.RetakeOf(map, Next), Is.Null, "Only the cell with a ship in it.");
            Assert.That(recorder.Retaking.Single(), Is.EqualTo(new WFSectorCellRetakingEvent(map, Far, Holder, deadline)));

            territory.CheckRetakes(map);
            Assert.That(recorder.Retaking, Has.Count.EqualTo(1), "Only a change raises the event.");
            Assert.That(territory.Owner(map, Far), Is.Not.Null, "Still held before the deadline.");
        });

        await RunTicks(200);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            Assert.That(territory.Owner(map, Far), Is.Null, "Freed.");
            Assert.That(territory.Owner(map, Next), Is.Not.Null);
            Assert.That(territory.RetakeOf(map, Far), Is.Null);
            var retaken = recorder.Retaken.Single();
            Assert.That((retaken.Map, retaken.Cell, retaken.Faction), Is.EqualTo((map, Far, Holder)));
            Assert.That(retaken.Ships, Is.EqualTo(new[] { hauler }), "The ship that sat in the cell is named.");
            Assert.That(Server.System<WFSectorActivitySystem>().IsQuiet(map, Far, STiming.CurTime - TimeSpan.FromMinutes(1)), Is.False,
                "Marked active, so a faction spreading into quiet cells waits.");
        });
    }

    /// <summary>The holder's ships in the cell, the ship leaving, a contest and a veto each end the countdown; they start it again when they lapse.</summary>
    [Test]
    public async Task PresenceDepartureContestAndVetoEndTheCountdown()
    {
        await StartRound();
        await SetDelay(10f);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            var ship = SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler");
            Board(ship);

            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "The holder has a ship there.");
            Assert.That(recorder.Retaking, Is.Empty);

            territory.SetPresence(Holder, map, new[] { Next });
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Not.Null, "Its ships are elsewhere.");

            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "Its ship came back: the countdown resets.");
            Assert.That(recorder.Retaking.Last(), Is.EqualTo(new WFSectorCellRetakingEvent(map, Far, Holder, null)));

            territory.SetPresence(Holder, map, new[] { Next });
            recorder.RetakeVetoed.Add(Far);
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "A driver vetoed it.");
            Assert.That(recorder.RetakeAttempts.Last(), Is.EqualTo(new WFSectorRetakeAttemptEvent(map, Far, Holder)));

            recorder.RetakeVetoed.Clear();
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Not.Null);

            Disembark();
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "The ship left.");

            Board(ship);
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Not.Null);
            Assert.That(territory.Free(map, Far), Is.True);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "Freeing the cell drops its countdown.");
            Assert.That(recorder.Retaken, Is.Empty, "Freed by something else is not a retake.");

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.Contest(map, Far, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.True);
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "A contested cell is not counted down.");
        });
    }

    /// <summary>A report that has gone stale counts as the holder being gone; a delay of zero turns retaking off.</summary>
    [Test]
    public async Task StalePresenceCountsAsGoneAndZeroTurnsItOff()
    {
        await StartRound();
        await SetDelay(10f);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Board(SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler"));
            territory.SetPresence(Holder, map, new[] { Far });
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null);
        });

        await RunTicks(240);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.RetakeOf(MapData.MapId, Far), Is.Not.Null, "Nobody reported for longer than the timeout.");
        });

        await SetDelay(0f);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            territory.CheckRetakes(MapData.MapId);
            Assert.That(territory.RetakeOf(MapData.MapId, Far), Is.Null, "Off: the countdown is dropped.");
            Assert.That(Server.System<WFSectorTestSystem>().Retaking.Last().Deadline, Is.Null);
        });

        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFSectorTerritorySystem>().Owner(MapData.MapId, Far), Is.Not.Null, "Still held with retaking off.");
        });
    }

    /// <summary>The territory event carries the countdown, and the client shows it as a retaking cell with a hover line, in place of the callsign.</summary>
    [Test]
    public async Task SyncCarriesTheRetakeToTheClient()
    {
        await StartRound();
        await SetDelay(10f);
        TimeSpan deadline = default;
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            Assert.That(Server.System<WFSectorSyncSystem>().Build(map).Retakes, Is.Empty);

            Board(SpawnGrid(CentreOf(Far) + new System.Numerics.Vector2(500f, 0f), "Test Hauler"));
            territory.CheckRetakes(map);
            var entry = Server.System<WFSectorSyncSystem>().Build(map).Retakes.Single();
            Assert.That(entry.Cell, Is.EqualTo(Far));
            deadline = entry.Deadline;
            Assert.That(deadline, Is.EqualTo(territory.RetakeOf(map, Far)));
        });

        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True);
            var retake = view.Retakes.Single();
            Assert.That(retake.Cell, Is.EqualTo(Far));
            Assert.That(retake.Deadline, Is.EqualTo(deadline));
            Assert.That(retake.Fill, Has.Length.EqualTo(18));
            Assert.That(retake.Outline, Has.Length.EqualTo(12));
            Assert.That(retake.Centre, Is.EqualTo(CentreOf(Far)));
            Assert.That(view.Labels.Select(label => label.Text), Does.Not.Contain(WFSectorHex.Callsign(Far)), "The countdown replaces the callsign.");
            Assert.That(view.Labels.Select(label => label.Text), Does.Contain(WFSectorHex.Callsign(Next)));
            Assert.That(view.CellLabels.Select(label => label.Text), Does.Not.Contain(WFSectorHex.Callsign(Far)));

            var map = new WFSectorMapControl();
            var hover = map.Readout(view, CentreOf(Far));
            Assert.That(hover, Does.Contain("being retaken"));
            Assert.That(hover, Does.Contain(WFSectorHex.Callsign(Far)));
            Assert.That(map.Readout(view, CentreOf(Next)), Does.Not.Contain("retaken"));
        });

        await Server.WaitAssertion(() =>
        {
            Disembark();
            Server.System<WFSectorTerritorySystem>().CheckRetakes(MapData.MapId);
        });
        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out var view), Is.True);
            Assert.That(view.Retakes, Is.Empty, "The ship left and the client dropped the countdown.");
            Assert.That(view.Labels.Select(label => label.Text), Does.Contain(WFSectorHex.Callsign(Far)));
        });
    }
}
