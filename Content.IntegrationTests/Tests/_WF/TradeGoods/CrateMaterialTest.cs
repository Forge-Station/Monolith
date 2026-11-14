#nullable enable
using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server.Power.Components;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Trade crates go into lathes like sheets, as a material recipes spend a share of.</summary>
[TestFixture]
[TestOf(typeof(SharedMaterialStorageSystem))]
public sealed class CrateMaterialTest
{
    private const string Lathe = "MedicalTechFab";
    private const string Medical = "TradeGoodMedicalSupplies";
    private const string Ammo = "TradeGoodAmmo";
    private const string MedicalStock = "WFMedicalStock";

    private static readonly string[] Crates = { Ammo, Medical, "TradeGoodFissionFuel", "TradeGoodWeaponParts", "TradeGoodShipComponents" };

    [Test]
    public async Task ACrateGoesIntoALatheOnlyIfItCanUseIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var storage = entMan.System<SharedMaterialStorageSystem>();

        await server.WaitAssertion(() =>
        {
            var lathe = entMan.SpawnEntity(Lathe, map.GridCoords);
            entMan.RemoveComponent<ApcPowerReceiverComponent>(lathe);
            var user = entMan.SpawnEntity(null, map.GridCoords);

            var unwanted = entMan.SpawnEntity(Ammo, map.GridCoords);
            Assert.That(storage.TryInsertMaxPossibleMaterialEntity(user, unwanted, lathe, out _), Is.False, "A medical lathe has no use for munitions.");
            Assert.That(entMan.EntityExists(unwanted), Is.True, "A refused crate should be left alone.");

            var crate = entMan.SpawnEntity(Medical, map.GridCoords);
            Assert.That(storage.TryInsertMaxPossibleMaterialEntity(user, crate, lathe, out _), Is.True);
            Assert.That(storage.GetMaterialAmount(lathe, MedicalStock), Is.EqualTo(10000));
            Assert.That(entMan.EntityExists(crate), Is.False, "A crate should be used up going in, or it's no sink.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A silo or flatpacker can't eject a crate's material, so it must not take the crate.</summary>
    [Test]
    public async Task StorageThatCantGiveACrateBackRefusesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var storage = entMan.System<SharedMaterialStorageSystem>();

        await server.WaitAssertion(() =>
        {
            var user = entMan.SpawnEntity(null, map.GridCoords);
            foreach (var machine in new[] { "MachineMaterialSilo", "MachineFlatpacker" })
            {
                var uid = entMan.SpawnEntity(machine, map.GridCoords);
                entMan.RemoveComponent<ApcPowerReceiverComponent>(uid);
                var crate = entMan.SpawnEntity(Ammo, map.GridCoords);
                Assert.That(storage.TryInsertMaxPossibleMaterialEntity(user, crate, uid, out _), Is.False, $"{machine} took a crate it can't give back.");
                Assert.That(entMan.EntityExists(crate), Is.True);

                var sheet = entMan.SpawnEntity("SheetSteel1", map.GridCoords);
                Assert.That(storage.TryInsertMaxPossibleMaterialEntity(user, sheet, uid, out _), Is.True, $"{machine} should still take sheets.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryCrateMaterialIsUsedAndAddsNoValue()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var used = protoMan.EnumeratePrototypes<LatheRecipePrototype>().SelectMany(recipe => recipe.Materials.Keys).Select(material => material.Id).ToHashSet();
            foreach (var crate in Crates)
            {
                Assert.That(protoMan.Index<EntityPrototype>(crate).TryGetComponent<PhysicalCompositionComponent>(out var composition, factory), Is.True, crate);
                foreach (var material in composition!.MaterialComposition.Keys)
                {
                    Assert.That(used, Does.Contain(material), $"Nothing uses {material} from {crate}.");
                    Assert.That(protoMan.Index<MaterialPrototype>(material).Price, Is.Zero, $"{material} would add to what {crate} sells for.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A new crew should never have to buy crates to fire its guns.</summary>
    [Test]
    public async Task RoundstartShipAmmoNeedsNoCrates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var crateMaterials = Crates.SelectMany(crate =>
            {
                protoMan.Index<EntityPrototype>(crate).TryGetComponent<PhysicalCompositionComponent>(out var composition, factory);
                return composition!.MaterialComposition.Keys;
            }).ToHashSet();

            foreach (var id in protoMan.Index<LatheRecipePackPrototype>("RoundstartShipgunAmmo").Recipes)
            {
                var recipe = protoMan.Index(id);
                Assert.That(recipe.Materials.Keys.Any(material => crateMaterials.Contains(material.Id)), Is.False, $"{recipe.ID} needs a trade crate.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TradeCratesStayOutOfConsoleStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ProtoMan.Index<EntityPrototype>("MarketFrontierOutpost").TryGetComponent<CargoMarketDataComponent>(out var market, entMan.ComponentFactory), Is.True);
            var crate = entMan.SpawnEntity(Ammo, map.GridCoords);
            Assert.That(entMan.System<EntityWhitelistSystem>().IsBlacklistPass(market!.Blacklist, crate), Is.True,
                "Being a material shouldn't put a crate sold at a pallet on the market console.");
        });

        await pair.CleanReturnAsync();
    }
}
