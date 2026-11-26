#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.ShipPa;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Notices: a crewed ship ten seconds inside held space is told so once, in chat to everyone aboard, and over its PA when it has one.</summary>
[TestOf(typeof(WFSectorNoticeSystem))]
public sealed class WFSectorNoticeTest : WFSectorTestBase
{
    private static readonly WFSectorCell Cell = new(4, -2);

    /// <summary>An open cell two steps from <see cref="Cell"/>.</summary>
    private static readonly WFSectorCell Open = new(6, -2);

    /// <summary>The default line, with the holder's name and the cell.</summary>
    private static bool IsEntered(string message)
    {
        return message.Contains("WARNING: entering Faction id controlled space") && message.Contains(WFSectorHex.Callsign(Cell));
    }

    /// <summary>The default leaving line, with the open cell the ship is now in.</summary>
    private static bool IsLeft(string message)
    {
        return message.Contains("Leaving Faction id controlled space") && message.Contains(WFSectorHex.Callsign(Open));
    }

    /// <summary>A ship that leaves held space is told so after the leaving dwell, even inside the cooldown its entered notice began.</summary>
    [Test]
    public async Task LeavingIsToldInsideTheCooldown()
    {
        await StartRound();
        var since = ChatCount();
        EntityUid ship = default;
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Holder, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - entered > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        TimeSpan left = default;
        await Server.WaitAssertion(() =>
        {
            var notices = Server.System<WFSectorNoticeSystem>();
            notices.Check();
            Place(ship, CentreOf(Open));
            notices.Check();
            left = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - left > WFSectorNoticeSystem.LeftDwell + TimeSpan.FromSeconds(0.5), 4000);
        await Server.WaitAssertion(() =>
        {
            Assert.That(STiming.CurTime - entered, Is.LessThan(WFSectorNoticeSystem.Cooldown), "Still inside the cooldown.");
            Server.System<WFSectorNoticeSystem>().Check();
        });
        await RunTicks(60);
        var lines = ChatSince(since);
        Assert.That(lines.Count(IsEntered), Is.EqualTo(1), "Told once that it entered.");
        Assert.That(lines.Count(IsLeft), Is.EqualTo(1), "Told that it left, though the cooldown has not run out.");
    }

    /// <summary>Crew stepping off and back aboard inside the cooldown does not bring the entered notice again.</summary>
    [Test]
    public async Task ShortAbsenceDoesNotRepeatTheNotice()
    {
        await StartRound();
        var since = ChatCount();
        EntityUid ship = default;
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Holder, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - entered > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        TimeSpan back = default;
        await Server.WaitAssertion(() =>
        {
            var notices = Server.System<WFSectorNoticeSystem>();
            notices.Check();
            Disembark();
            notices.Check();
            Board(ship);
            notices.Check();
            back = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - back > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        await Server.WaitAssertion(() =>
        {
            Assert.That(STiming.CurTime - entered, Is.LessThan(WFSectorNoticeSystem.Cooldown), "Still inside the cooldown.");
            Server.System<WFSectorNoticeSystem>().Check();
        });
        await RunTicks(60);
        Assert.That(ChatSince(since).Count(IsEntered), Is.EqualTo(1), "Told once, not again on return.");
    }

    [Test]
    public async Task ShipInsideHeldSpaceIsToldInChatAfterTheDwell()
    {
        await StartRound();
        var since = ChatCount();
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Holder, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await RunTicks(120);
        Assert.That(ChatSince(since).Any(IsEntered), Is.False, "Not before the dwell is up.");

        await WaitUntilServer(() => STiming.CurTime - entered > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        await Server.WaitPost(() => Server.System<WFSectorNoticeSystem>().Check());
        await RunTicks(60);
        Assert.That(ChatSince(since).Count(IsEntered), Is.EqualTo(1),
            "Told once that it has entered held space, with the cell.");

        // Another look straight away tells it nothing more.
        await Server.WaitPost(() => Server.System<WFSectorNoticeSystem>().Check());
        await RunTicks(60);
        Assert.That(ChatSince(since).Count(line => line.Contains("controlled space")), Is.EqualTo(1));
    }

    /// <summary>A ship with a working speaker gets the notice over its PA, and someone aboard out of the speaker's range still gets it in chat.</summary>
    [Test]
    public async Task NoticeGoesOverThePaAndInChat()
    {
        await StartRound();
        var since = ChatCount();
        EntityUid ship = default;
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            var maps = Server.System<SharedMapSystem>();
            var grid = SEntMan.GetComponent<MapGridComponent>(ship);
            for (var x = 1; x <= 30; x++)
            {
                maps.SetTile(ship, grid, new Vector2i(x, 0), new Tile(1));
            }

            // A speaker with no power receiver is always on.
            var speaker = SEntMan.SpawnEntity(null, new EntityCoordinates(ship, new Vector2(0.5f)));
            SEntMan.AddComponent<ShipPaSpeakerComponent>(speaker);
            Server.System<SharedTransformSystem>().AnchorEntity(speaker);

            // Thirty metres down the hull, well out of the speaker's range.
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(ship, new Vector2(30.5f, 0.5f)));
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Holder, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - entered > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        await Server.WaitAssertion(() =>
        {
            Server.System<WFSectorNoticeSystem>().Check();
            Assert.That(SEntMan.TryGetComponent<ShipPaBroadcastComponent>(ship, out var broadcasts), Is.True, "The PA carried something.");
            Assert.That(broadcasts!.Broadcasts.Any(broadcast => broadcast.Caption != null && IsEntered(broadcast.Caption)), Is.True,
                "The notice went over the PA.");
        });
        await RunTicks(60);
        Assert.That(ChatSince(since).Count(IsEntered), Is.EqualTo(1),
            "Out of the speaker's range, the notice still reaches chat, once.");
    }
}
