#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.TradeGoods.Commands;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Robust.Server.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// The wf_trade admin command starts each trade event at the test's stations, and clear ends the shortages and rush
/// orders it started.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeCommand))]
public sealed class TradeCommandTest
{
    [Test]
    public async Task StartsAndClearsEvents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var console = server.ResolveDependency<IServerConsoleHost>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var rushes = entMan.System<WFTradeRushSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var other = TradeTestWorld.Station(entMan, map, new Vector2(5000f, 0f), "WF command test depot");
            var stations = new[] { map.Grid.Owner, other.Grid };

            // Forced to a station-local shortage: a sector-wide boom would outlive the test's grids.
            console.ExecuteCommand(null, "wf_trade shortage WFTradeShortageIndustrial");
            Assert.That(shortages.Active.Count(shortage => shortage.Grid is { } grid && stations.Contains(grid)), Is.EqualTo(1),
                "wf_trade shortage started no shortage at the test's stations.");

            console.ExecuteCommand(null, "wf_trade rush WFTradeRushIndustrial");
            Assert.That(rushes.Active.Count(rush => stations.Contains(rush.Grid)), Is.EqualTo(1),
                "wf_trade rush posted no rush order at the test's stations.");

            console.ExecuteCommand(null, "wf_trade consignment");
            var consignments = entMan.EntityQuery<WFConsignmentComponent>().ToList();
            Assert.That(consignments, Has.Count.EqualTo(1), "wf_trade consignment posted no consignment.");
            Assert.That(stations, Does.Contain(consignments[0].Destination));

            console.ExecuteCommand(null, "wf_trade status");
            console.ExecuteCommand(null, "wf_trade clear");
            Assert.That(shortages.Active, Is.Empty, "wf_trade clear left a shortage running.");
            Assert.That(rushes.Active, Is.Empty, "wf_trade clear left a rush order open.");
        });

        await pair.CleanReturnAsync();
    }
}
