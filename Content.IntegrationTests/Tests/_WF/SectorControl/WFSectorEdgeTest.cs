#nullable enable
using System;
using System.Threading.Tasks;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Only the cells the sector map draws can be held: nothing past the sector's edge is claimed or contested.</summary>
[TestOf(typeof(WFSectorTerritorySystem))]
public sealed class WFSectorEdgeTest : WFSectorTestBase
{
    [TearDown]
    public async Task ClearTerritory()
    {
        await Server.WaitPost(() => Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId));
    }

    [Test]
    public async Task CellsPastTheSectorEdgeCannotBeHeld()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var size = territory.CellSize;
            // The first cell out along the axis whose centre lies past the edge.
            var outside = new WFSectorCell(0, 0);
            for (var q = 1; WFSectorHex.InSector(outside, size); q++)
            {
                outside = new WFSectorCell(q, 0);
            }

            var edge = new WFSectorCell(outside.Q - 1, 0);
            Assert.That(WFSectorHex.InSector(edge, size), Is.True);
            Assert.That(territory.CanClaim(map, outside, Holder), Is.False, "A cell the map does not draw can't be claimed.");
            Assert.That(territory.TryClaim(map, outside, Holder, null), Is.False);
            Assert.That(territory.Contest(map, outside, Holder, STiming.CurTime + TimeSpan.FromMinutes(5)), Is.False, "Nor contested.");
            Assert.That(territory.TryClaim(map, edge, Holder, null), Is.True, "The outermost drawn cell can.");

            // In the outside cell's own column, a cell nearer the origin is drawn and can be held: it is the distance
            // of a cell's centre that counts, not its column.
            var column = new WFSectorCell(outside.Q, -1);
            for (var r = -1; !WFSectorHex.InSector(column, size) && r > -outside.Q; r--)
            {
                column = new WFSectorCell(outside.Q, r);
            }

            Assert.That(WFSectorHex.InSector(column, size), Is.True);
            Assert.That(territory.TryClaim(map, column, Holder, null), Is.True, "A drawn cell in the same column can be held.");
        });
    }
}
