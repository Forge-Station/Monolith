#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Components;
using Content.Shared._WF.PlanetCargoHubs;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Light.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetCargoHubs;

/// <summary>Each hub map loads as one roofed grid inside its footprint, with the depot's machines powered and a berth per number.</summary>
[TestFixture]
public sealed class CargoHubMapsTest
{
    [Test]
    public async Task HubsLoadWholeWithTheirDepotMachines()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var loaded = new List<(WFPlanetCargoHubPrototype Hub, MapId Map, EntityUid Grid)>();

        await server.WaitAssertion(() =>
        {
            var hubs = server.ProtoMan.EnumeratePrototypes<WFPlanetCargoHubPrototype>().ToList();
            Assert.That(hubs, Has.Count.GreaterThanOrEqualTo(2), "Depots A and B each need a hub.");
            foreach (var hub in hubs)
            {
                var site = hub.Site;
                Assert.That(site.MapPath, Is.Not.Null);
                Assert.That(site.Footprint, Is.Not.Null);
                Assert.That(site.Connection, Is.EqualTo(WFPlanetPoiConnection.Roadside));
                Assert.That(site.Sockets, Has.Count.EqualTo(1));
                var footprint = site.Footprint!.Value;
                var socket = site.Sockets[0];
                var step = socket.Direction switch
                {
                    WFPlanetPoiDirection.North => new Vector2i(0, 1),
                    WFPlanetPoiDirection.East => new Vector2i(1, 0),
                    WFPlanetPoiDirection.South => new Vector2i(0, -1),
                    _ => new Vector2i(-1, 0),
                };
                Assert.That(footprint.ContainsTile(socket.Offset) && !footprint.ContainsTile(socket.Offset + step), Is.True,
                    $"{hub.ID}'s socket doesn't point out of its footprint.");

                maps.CreateMap(out var mapId);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(site.MapPath!), out var grid), Is.True, site.MapPath);
                loaded.Add((hub, mapId, grid!.Value.Owner));

                var grids = 0;
                var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    if (xform.MapID == mapId)
                        grids++;
                }

                Assert.That(grids, Is.EqualTo(1), $"{site.MapPath} loaded in pieces.");
                Assert.That(entMan.HasComponent<RoofComponent>(grid.Value.Owner), Is.True, $"{site.MapPath} has no roof.");
                var tiles = maps.GetAllTiles(grid.Value.Owner, grid.Value.Comp).Select(tile => tile.GridIndices).ToList();
                Assert.That(tiles.All(tile => footprint.ContainsTile(tile)), Is.True, $"{site.MapPath} spills out of its footprint.");
                Assert.That(tiles, Has.Count.EqualTo(footprint.Width * footprint.Height), $"{site.MapPath} doesn't fill its footprint.");
                Assert.That(maps.GetTileRef(grid.Value.Owner, grid.Value.Comp, socket.Offset).Tile.IsEmpty, Is.False, "The socket has no floor.");

                int Count(string proto) => entMan.EntityQuery<MetaDataComponent, TransformComponent>()
                    .Count(found => found.Item2.GridUid == grid.Value.Owner && found.Item1.EntityPrototype?.ID == proto);
                var berths = entMan.EntityQuery<WFCargoHubBerthComponent, TransformComponent>().Count(found => found.Item2.GridUid == grid.Value.Owner);
                Assert.That(berths, Is.InRange(2, 3), $"{site.MapPath} should have two or three berths.");
                Assert.That(Count("ComputerPalletConsoleNFHighMarket"), Is.EqualTo(berths), "Every berth needs a pallet console.");
                Assert.That(Count("CargoPalletSell"), Is.GreaterThanOrEqualTo(berths * 4), "Every berth needs sell pallets.");
                Assert.That(Count("CargoChuteDepot") + Count("CargoChuteDepotAlt"), Is.EqualTo(1), "The hub needs a turn-in chute.");
                Assert.That(Count("VendingMachineTraderDepot") + Count("VendingMachineTraderDepotAlt"), Is.EqualTo(1), "The hub needs the depot vendor.");
                Assert.That(Count("WFCargoHubBeacon"), Is.EqualTo(1), "The hub needs its beacon mast.");
                Assert.That(Count("WFCargoHubBerthStrobe"), Is.EqualTo(berths * 2), "Every berth needs a strobe at each corner of its mouth.");
            }
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            foreach (var (hub, mapId, grid) in loaded)
            {
                var unpowered = entMan.EntityQuery<ApcPowerReceiverComponent, MetaDataComponent>()
                    .Where(found => entMan.GetComponent<TransformComponent>(found.Item1.Owner).GridUid == grid
                                    && found.Item1.NeedsPower && !found.Item1.Powered)
                    .Select(found => found.Item2.EntityPrototype?.ID)
                    .ToList();
                Assert.That(unpowered, Is.Empty, $"{hub.ID} has unpowered machines: {string.Join(", ", unpowered)}");
                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }
}
