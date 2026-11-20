#nullable enable
using System.Numerics;
using Content.Server._Crescent.Dispenser;
using Content.Server._Mono.VendingMachine;
using Content.Server._WF.TradeGoods.Components;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// A cargo chute refuses trade goods bought on its own grid and still pays for goods bought on another station and
/// hauled in.
/// </summary>
[TestFixture]
[TestOf(typeof(DispenserSystem))]
public sealed class TradeChuteTest
{
    // Caelestinus Central maps this vendor next to this chute, and the chute pays far more than the vendor charges.
    private const string ChuteProto = "CargoChuteTradeMall";
    private const string VendorProto = "VendingMachineTraderDepot";
    private const string CrateProto = "TradeGoodFabric";

    [Test]
    public async Task ChuteRefusesLocalGoods()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.System<SharedMapSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();
        var purchase = entMan.System<VendingMachinePurchaseSystem>();

        var chute = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var local = EntityUid.Invalid;
        var imported = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            entMan.AddComponent<WFTradeMarketMapComponent>(map.MapUid);
            var coords = map.GridCoords;
            chute = entMan.SpawnEntity(ChuteProto, coords);
            user = entMan.SpawnEntity(null, coords);

            var chuteComp = entMan.GetComponent<DispenserComponent>(chute);
            Assert.That(chuteComp.Inventory.ContainsKey(CrateProto), Is.True,
                $"{ChuteProto} no longer takes {CrateProto}; pick a crate it does take.");

            var vendor = entMan.SpawnEntity(VendorProto, coords);
            local = entMan.SpawnEntity(CrateProto, coords);
            purchase.MarkAsPurchased(local, vendor, 1000);

            // Another station well past the haul distance: its crate is stamped there, then carried to the chute.
            var otherGrid = mapMan.CreateGridEntity(map.MapId);
            mapSys.SetTile(otherGrid.Owner, otherGrid.Comp, Vector2i.Zero, map.Tile.Tile);
            xformSys.SetLocalPosition(otherGrid.Owner, new Vector2(4000f, 0f));

            var otherVendor = entMan.SpawnEntity(VendorProto, new EntityCoordinates(otherGrid.Owner, 0.5f, 0.5f));
            imported = entMan.SpawnEntity(CrateProto, new EntityCoordinates(otherGrid.Owner, 0.5f, 0.5f));
            purchase.MarkAsPurchased(imported, otherVendor, 1000);
            xformSys.SetCoordinates(imported, coords);
            Assert.That(purchase.WasPurchasedOnGrid(imported, otherGrid.Owner), Is.True,
                "The imported crate wasn't stamped with the other grid, so it can't test goods bought elsewhere.");

            Assert.That(interaction.InteractUsing(user, local, chute, coords, false, false), Is.True,
                "The chute should handle a crate it trades, even when refusing it.");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(local), Is.False, "The chute took a crate bought on its own grid.");
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.Zero, "The chute paid for a crate bought on its own grid.");

            Assert.That(interaction.InteractUsing(user, imported, chute, map.GridCoords, false, false), Is.True);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(imported), Is.True, "The chute refused a crate bought on another station.");
            Assert.That(TradeTestHelpers.CashOnMap(entMan, map.MapId), Is.GreaterThan(0), "The chute didn't pay for a crate bought on another station.");
        });

        await pair.CleanReturnAsync();
    }
}

/// <summary>Shared bits for the trade-goods tests.</summary>
public static class TradeTestHelpers
{
    /// <summary>Spesos lying loose on a map, as cash stacks.</summary>
    public static int CashOnMap(IEntityManager entMan, MapId map)
    {
        var total = 0;
        var query = entMan.AllEntityQueryEnumerator<StackComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var stack, out var xform, out var meta))
        {
            if (xform.MapID == map && meta.EntityPrototype?.ID.StartsWith("SpaceCash") == true)
                total += stack.Count;
        }

        return total;
    }
}
