using System.Collections.Generic;
using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Maps;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Xenoarchaeology.XenoArtifacts;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the ASF grant economy: Lantern Post's cargo bay, its ARG bounties and the grant kiosk.</summary>
[TestFixture]
public sealed class AsfGrantsTest
{
    private static readonly ResPath Lantern = new("/Maps/_WF/Asf/lantern.yml");
    private const string Grant = "WFAsfRewardGrant";

    /// <summary>Lantern Post sells cargo from pallets, offers only ARG bounties and redeems ARG at a kiosk.</summary>
    [Test]
    public async Task LanternHasCargoBay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            Assert.That(loader.TryLoadGrid(mapId, Lantern, out var grid), Is.True);
            var gridUid = grid!.Value.Owner;

            var bountyConsoles = 0;
            var bountyQuery = entities.EntityQueryEnumerator<CargoBountyConsoleComponent, TransformComponent>();
            while (bountyQuery.MoveNext(out _, out var xform))
            {
                if (xform.GridUid == gridUid)
                    bountyConsoles++;
            }

            Assert.That(bountyConsoles, Is.EqualTo(1), "Lantern Post should have one bounty console.");

            var consoles = new List<(System.Numerics.Vector2 Pos, int Range)>();
            var consoleQuery = entities.EntityQueryEnumerator<CargoPalletConsoleComponent, TransformComponent>();
            while (consoleQuery.MoveNext(out var console, out var xform))
            {
                if (xform.GridUid == gridUid)
                    consoles.Add((xform.LocalPosition, console.PalletDistance));
            }

            var sellPallets = 0;
            var palletQuery = entities.EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
            while (palletQuery.MoveNext(out var pallet, out var xform))
            {
                if (xform.GridUid != gridUid || (pallet.PalletType & BuySellType.Sell) == 0)
                    continue;
                Assert.That(xform.Anchored, $"Sell pallet at {xform.LocalPosition} isn't anchored.");
                Assert.That(consoles.Any(c => (c.Pos - xform.LocalPosition).Length() <= c.Range),
                    $"Sell pallet at {xform.LocalPosition} is out of every sale console's range.");
                sellPallets++;
            }

            Assert.That(sellPallets, Is.Positive, "Lantern Post has no sell pallets.");

            var kiosks = 0;
            var storeQuery = entities.EntityQueryEnumerator<StoreComponent, TransformComponent>();
            while (storeQuery.MoveNext(out var store, out var xform))
            {
                if (xform.GridUid == gridUid && store.CurrencyWhitelist.Contains(Grant))
                    kiosks++;
            }

            Assert.That(kiosks, Is.Positive, "Lantern Post has no kiosk that takes ARG.");

            var station = prototypes.Index<GameMapPrototype>("WFAsfLantern").Stations["WFAsfLantern"];
            Assert.That(station.StationComponentOverrides.TryGetComponent<StationCargoBountyDatabaseComponent>(factory, out var bounties));
            Assert.That(bounties!.AvailableBounties, Is.Not.Empty);
            foreach (var id in bounties.AvailableBounties)
            {
                Assert.That(prototypes.Index<CargoBountyPrototype>(id).RewardProto.Id, Is.EqualTo(Grant), $"{id} doesn't pay ARG.");
            }

            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Crystal bounties tell crystal sizes apart, an artifact in an artifact container counts, and fragment stacks count by size.</summary>
    [Test]
    public async Task BountiesMatchCrystalsAndArtifacts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var cargo = entities.System<CargoSystem>();
        var storage = entities.System<EntityStorageSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var mapUid = maps.CreateMap(out _);
            var at = new EntityCoordinates(mapUid, 0, 0);
            var bounty = (string id) => prototypes.Index<CargoBountyPrototype>(id);

            var crate = entities.SpawnEntity("CrateGenericSteel", at);
            for (var i = 0; i < 2; i++)
            {
                Assert.That(storage.Insert(entities.SpawnEntity("MonolithicCrystalSmall", at), crate));
            }

            Assert.That(cargo.IsBountyComplete(crate, bounty("WFAsfBountyCrystalSmall")), Is.True);
            Assert.That(cargo.IsBountyComplete(crate, bounty("WFAsfBountyCrystalSmallBulk")), Is.False);
            Assert.That(cargo.IsBountyComplete(crate, bounty("WFAsfBountyCrystalMedium")), Is.False);
            Assert.That(storage.Insert(entities.SpawnEntity("MonolithicCrystalMedium", at), crate));
            Assert.That(cargo.IsBountyComplete(crate, bounty("WFAsfBountyCrystalMedium")), Is.True);
            Assert.That(cargo.IsBountyComplete(crate, bounty("WFAsfBountyCrystalLarge")), Is.False);

            var container = entities.SpawnEntity("CrateArtifactContainer", at);
            Assert.That(cargo.IsBountyComplete(container, bounty("WFAsfBountyArtifact")), Is.False);
            Assert.That(storage.Insert(entities.SpawnEntity("SimpleXenoArtifact", at), container));
            Assert.That(cargo.IsBountyComplete(container, bounty("WFAsfBountyArtifact")), Is.True);

            var fragments = entities.SpawnEntity("CrateGenericSteel", at);
            var stack = entities.SpawnEntity("ArtifactFragment1", at);
            entities.System<StackSystem>().SetCount(stack, 5);
            Assert.That(storage.Insert(stack, fragments));
            Assert.That(cargo.IsBountyComplete(fragments, bounty("WFAsfBountyArtifactFragments")), Is.False);
            Assert.That(storage.Insert(entities.SpawnEntity("ArtifactFragment1", at), fragments));
            Assert.That(cargo.IsBountyComplete(fragments, bounty("WFAsfBountyArtifactFragments")), Is.True);

            maps.DeleteMap(entities.GetComponent<TransformComponent>(mapUid).MapID);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Each property an artifact bounty asks for is an effect an artifact keeps once unlocked, so it survives shipping.</summary>
    [Test]
    public async Task ArtifactBountiesAskForLastingEffects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        var lasting = prototypes.EnumeratePrototypes<ArtifactEffectPrototype>()
            .SelectMany(e => e.PermanentComponents.Keys)
            .ToHashSet();
        var checkedAny = false;
        foreach (var bounty in prototypes.EnumeratePrototypes<CargoBountyPrototype>().Where(b => b.ID.StartsWith("WFAsfBounty")))
        {
            foreach (var entry in bounty.Entries)
            {
                if (entry.Whitelist.Components is not { } comps || !comps.Contains("Artifact"))
                    continue;
                foreach (var comp in comps.Where(c => c != "Artifact"))
                {
                    Assert.That(lasting, Does.Contain(comp), $"{bounty.ID} asks for {comp}, which no artifact effect keeps.");
                    checkedAny = true;
                }
            }
        }

        Assert.That(checkedAny, "No ASF artifact bounty asks for a property.");
        await pair.CleanReturnAsync();
    }

    /// <summary>A bounty's reward spawns as ARG stacks totalling the reward, and every kiosk listing is priced in ARG.</summary>
    [Test]
    public async Task GrantsSpawnAndSpend()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var stacks = entities.System<StackSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var mapUid = maps.CreateMap(out _);
            var spawned = stacks.SpawnMultiple(Grant, 250, new EntityCoordinates(mapUid, 0, 0));
            Assert.That(spawned.Sum(e => entities.GetComponent<StackComponent>(e).Count), Is.EqualTo(250));
            Assert.That(spawned.All(e => entities.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID.StartsWith(Grant)));
            maps.DeleteMap(entities.GetComponent<TransformComponent>(mapUid).MapID);

            var kiosk = prototypes.Index<EntityPrototype>("WFAsfGrantKiosk");
            Assert.That(kiosk.TryGetComponent<StoreComponent>(out var store, entities.ComponentFactory));
            var listings = prototypes.EnumeratePrototypes<ListingPrototype>()
                .Where(l => l.Categories.Overlaps(store!.Categories))
                .ToList();
            Assert.That(listings.Any(l => l.ProductEntity?.Id == "WFAsfVoucherCivilian"), "The kiosk sells no ship voucher.");
            foreach (var listing in listings)
            {
                Assert.That(listing.Cost.Keys, Is.EquivalentTo(new[] { new ProtoId<CurrencyPrototype>(Grant) }), $"{listing.ID} isn't priced in ARG.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
