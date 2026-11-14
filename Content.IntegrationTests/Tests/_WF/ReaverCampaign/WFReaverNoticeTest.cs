#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The Reaver faction replaces the sector control border notice with its own, blunter line.</summary>
[TestOf(typeof(WFSectorNoticeSystem))]
public sealed class WFReaverNoticeTest : WFSectorTestBase
{
    private const string Reavers = "WFSectorFactionReavers";

    private static readonly WFSectorCell Cell = new(4, -2);

    [Test]
    public async Task ReaverSpaceNoticeUsesTheReaverLine()
    {
        await StartRound();
        var since = ChatCount();
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Reavers, null), Is.True);
            Server.System<WFSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - entered > WFSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        await Server.WaitPost(() => Server.System<WFSectorNoticeSystem>().Check());
        await RunTicks(60);
        var line = $"WARNING: entering Ashfall Reaver controlled space ({WFSectorHex.Callsign(Cell)}). Their patrols attack on sight";
        Assert.That(ChatSince(since).Count(message => message.Contains(line)), Is.EqualTo(1));
        Assert.That(ChatSince(since).Any(message => message.Contains("Ashfall Reavers controlled space")), Is.False,
            "The default line is not sent as well.");
    }
}
