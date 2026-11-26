#nullable enable
using System.Numerics;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Crate origins: one from off the market maps is pending until it shows up on one, then stamped where it
/// arrives by FTL, by the sweep, or at the chute it's offered to, and only sells after a haul from there.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradeProvenanceTest
{
    private const string ChuteProto = "CargoChuteTradeMall";
    private const string CrateProto = "TradeGoodFabric";

    /// <summary>Close to the chute's station but well inside the haul distance, and clear of FTL's anti-collision push.</summary>
    private static readonly Vector2 Arrival = new(200f, 0f);

    /// <summary>A one-tile grid on a map at a position.</summary>
    private static EntityUid Grid(IEntityManager entMan, MapId mapId, EntityUid mapUid, Tile tile, Vector2 position)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var grid = mapSys.CreateGridEntity(mapId);
        mapSys.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, tile);
        entMan.System<SharedTransformSystem>().SetCoordinates(grid.Owner, new EntityCoordinates(mapUid, position));
        return grid.Owner;
    }

    /// <summary>A ship on a fresh map that isn't a market map, with a crate aboard; the crate stays pending there.</summary>
    private static (EntityUid OtherMap, EntityUid Ship, EntityUid Crate) ShipOffMarket(IEntityManager entMan, TestMapData map)
    {
        var otherMap = entMan.System<SharedMapSystem>().CreateMap(out var otherId);
        var ship = Grid(entMan, otherId, otherMap, map.Tile.Tile, Vector2.Zero);
        var crate = entMan.SpawnEntity(CrateProto, new EntityCoordinates(ship, 0.5f, 0.5f));
        Assert.That(entMan.GetComponent<WFTradeOriginComponent>(crate).Pending, Is.True,
            "A crate made off the market maps should have no origin yet.");
        return (otherMap, ship, crate);
    }

    [Test]
    public async Task CrateIsStampedWhereItsShipArrivesByFtl()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();

        EntityUid crate = default, farChute = default;
        var quoted = 0;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var far = Grid(entMan, map.MapId, map.MapUid, map.Tile.Tile, new Vector2(5000f, 0f));
            farChute = entMan.SpawnEntity(ChuteProto, new EntityCoordinates(far, 0.5f, 0.5f));
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var (otherMap, ship, made) = ShipOffMarket(entMan, map);
            crate = made;
            var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);

            // Arriving on a map that isn't a market stamps nothing.
            var offMarket = new FTLCompletedEvent(ship, otherMap);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref offMarket);
            Assert.That(origin.Pending, Is.True, "An FTL arrival off the market maps stamped the crate.");

            // The ship jumps in near the chute's station.
            xformSys.SetCoordinates(ship, new EntityCoordinates(map.MapUid, Arrival));
            entMan.DeleteEntity(otherMap);
            var arrived = new FTLCompletedEvent(ship, map.MapUid);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref arrived);

            Assert.That(origin.Pending, Is.False, "The FTL arrival didn't stamp the crate.");
            Assert.That(origin.Grid, Is.EqualTo(ship));
            Assert.That(origin.Point.MapId, Is.EqualTo(map.MapId));
            Assert.That(Vector2.Distance(origin.Point.Position, xformSys.GetWorldPosition(crate)), Is.LessThan(1f),
                "The crate wasn't stamped where it arrived.");
            Assert.That(market.CheckHaul(chute, crate), Is.EqualTo((LocId?) "wf-trade-refuse-too-close"));

            // It flies on to a station past the haul distance; that jump doesn't move its origin.
            var stamped = origin.Point;
            xformSys.SetCoordinates(ship, new EntityCoordinates(map.MapUid, new Vector2(5000f, 100f)));
            var onward = new FTLCompletedEvent(ship, map.MapUid);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref onward);
            Assert.That(origin.Point, Is.EqualTo(stamped), "A second jump on the same map moved the crate's origin.");
            Assert.That(market.CheckHaul(farChute, crate), Is.Null, "A crate hauled past the haul distance was refused.");

            xformSys.SetCoordinates(crate, new EntityCoordinates(far, 0.5f, 0.5f));
            quoted = market.Quote(farChute, CrateProto)!.Value.Amount;
            Assert.That(interaction.InteractUsing(user, crate, farChute, new EntityCoordinates(far, 0.5f, 0.5f), false, false), Is.True);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(crate), Is.True, "The far chute refused the hauled crate.");
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.EqualTo(quoted));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SweepStampsCratesMovedInWithoutFtl()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid chute = default, ship = default, crate = default;
        var ticks = 0;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var (otherMap, onShip, made) = ShipOffMarket(entMan, map);
            ship = onShip;
            crate = made;

            // Teleported in, as shipyard docking and z-level moves do: no FTL event.
            xformSys.SetCoordinates(ship, new EntityCoordinates(map.MapUid, Arrival));
            entMan.DeleteEntity(otherMap);
            Assert.That(entMan.GetComponent<WFTradeOriginComponent>(crate).Pending, Is.True);

            // The sweep runs every 5 s.
            ticks = timing.TickRate * 6;
        });

        await pair.RunTicksSync(ticks);

        await server.WaitAssertion(() =>
        {
            var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
            Assert.That(origin.Pending, Is.False, "The sweep didn't stamp a crate moved onto a market map.");
            Assert.That(origin.Grid, Is.EqualTo(ship));
            Assert.That(origin.Point.MapId, Is.EqualTo(map.MapId));
            Assert.That(Vector2.Distance(origin.Point.Position, Arrival), Is.LessThan(2f), "The crate wasn't stamped where it arrived.");
            Assert.That(market.CheckHaul(chute, crate), Is.EqualTo((LocId?) "wf-trade-refuse-too-close"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PendingCrateIsRefusedAndStampedAtTheChute()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();

        EntityUid crate = default;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var chute = entMan.SpawnEntity(ChuteProto, map.GridCoords);
            var user = entMan.SpawnEntity(null, map.GridCoords);
            var (otherMap, _, made) = ShipOffMarket(entMan, map);
            crate = made;

            // Carried straight to the chute before anything stamped it.
            xformSys.SetCoordinates(crate, map.GridCoords);
            entMan.DeleteEntity(otherMap);
            var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
            Assert.That(origin.Pending, Is.True);

            Assert.That(interaction.InteractUsing(user, crate, chute, map.GridCoords, false, false), Is.True,
                "The chute should handle a crate it trades, even when refusing it.");
            Assert.That(origin.Pending, Is.False, "A pending crate offered at a chute should be stamped there.");
            Assert.That(origin.Grid, Is.EqualTo(map.Grid.Owner));
            Assert.That(market.CheckHaul(chute, crate), Is.EqualTo((LocId?) "wf-trade-refuse-made-here"),
                "Once stamped at the chute, the crate counts as made on its station.");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(crate), Is.False, "The chute took a crate of unknown origin.");
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.Zero, "The chute paid for a crate of unknown origin.");
        });

        await pair.CleanReturnAsync();
    }
}
