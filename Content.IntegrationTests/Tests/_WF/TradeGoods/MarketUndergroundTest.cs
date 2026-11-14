#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._Mono.Company;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// The Market app groups stations by faction, and hidden markets reach only holders of an underground company.
/// </summary>
[TestFixture]
[TestOf(typeof(WFMarketCartridgeSystem))]
public sealed class MarketUndergroundTest
{
    [Test]
    public async Task HiddenMarketsReachOnlyTheUnderground()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cartridge = entMan.System<WFMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var (fence, _) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);
            var fenceName = TradeTestWorld.Name(entMan, fence);

            // A loader carried by someone of a given company; the app reads the holder's company.
            EntityUid Loader(string company)
            {
                var holder = entMan.SpawnEntity(null, map.GridCoords);
                entMan.EnsureComponent<CompanyComponent>(holder).CompanyName = company;
                return entMan.SpawnEntity(null, new EntityCoordinates(holder, 0f, 0f));
            }

            var lawful = cartridge.BuildState(Loader("TSFCivilian"));
            Assert.That(lawful.Stations.Any(station => station.Underground || station.Name == fenceName), Is.False,
                "A lawful holder should not see hidden markets.");

            var open = lawful.Stations.Single(station => station.Name == TradeTestWorld.Name(entMan, map.Grid.Owner));
            Assert.That(open.Faction, Is.EqualTo("Caelestinus"), "The trade mall chute should group under Caelestinus.");

            foreach (var company in new[] { "PDV", "WFUnderground" })
            {
                var criminal = cartridge.BuildState(Loader(company));
                var hidden = criminal.Stations.SingleOrDefault(station => station.Name == fenceName);
                Assert.That(hidden, Is.Not.Null, $"A {company} holder should see the fence.");
                Assert.That(hidden!.Underground, Is.True);
                Assert.That(hidden.Faction, Is.EqualTo("The Underground"));
            }
        });

        await pair.CleanReturnAsync();
    }
}
