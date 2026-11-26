using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.FireControl;
using Content.Server._NF.Shipyard.Components;
using Content.Server.Power.Components;
using Content.Server.Research.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks ASF Lantern Post: it loads, has engines and guns, every ASF job can spawn there, its shipyard and vouchers sell real hulls, and the vouchers can be researched and printed there.</summary>
[TestFixture]
public sealed class AsfLanternTest
{
    private static readonly ResPath Lantern = new("/Maps/_WF/Asf/lantern.yml");

    /// <summary>ASF voucher entities; each has a lathe recipe of the same ID.</summary>
    private static readonly string[] Vouchers = ["WFAsfVoucherCivilian", "WFAsfVoucherEscort", "WFAsfVoucherLodestar"];

    /// <summary>Lathe recipes the post researches and prints besides vouchers.</summary>
    private static readonly string[] Arms =
        ["WFAsfWeaponPistolCoil", "WFAsfWeaponRifleCoil", "WFAsfMagazinePistolCoil", "WFAsfMagazineRifleCoil",
         "WFAsfBoxCoilSlug", "WFAsfClothingModsuitField", "WFAsfClothingModsuitAegis"];

    /// <summary>The post's armoury: racks and their guns, the slug boxes on its table, and the Pathfinder suit units.</summary>
    private static readonly (string Id, int Count)[] Armoury =
    [
        ("WFAsfGunRackFilled", 1), ("WFAsfPistolRackFilled", 1), ("WFAsfWeaponRifleCoil", 3),
        ("WFAsfWeaponPistolCoil", 4), ("WFAsfBoxCoilSlug", 4), ("WFAsfBoxMagazineRifleCoil", 1),
        ("WFAsfBoxMagazinePistolCoil", 1), ("WFAsfSuitStoragePathfinder", 3),
        ("WFAsfClothingModsuitFieldPowerCell", 3),
    ];

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    [Test]
    public async Task LanternHasSpawnsAndShipyard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            Assert.That(loader.TryLoadGrid(mapId, Lantern, out var grid), Is.True, "Lantern Post loads as a grid.");

            var spawns = new HashSet<string>();
            var spawnQuery = entities.EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (spawnQuery.MoveNext(out _, out var spawn, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner && spawn.Job is { } job)
                    spawns.Add(job);
            }

            var roles = prototypes.Index<DepartmentPrototype>("WFAsf").Roles;
            Assert.That(roles, Is.Not.Empty);
            foreach (var role in roles)
            {
                Assert.That(spawns, Does.Contain(role.Id), $"Lantern Post has no spawn point for {role}.");
            }

            var listed = new List<string>();
            var consoleQuery = entities.EntityQueryEnumerator<ShipyardListingComponent, TransformComponent>();
            while (consoleQuery.MoveNext(out _, out var listing, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    listed.AddRange(listing.Shuttles);
            }

            Assert.That(listed, Is.Not.Empty, "Lantern Post has no shipyard console with a listing.");

            // ASF vouchers can only be redeemed at the ASF console, so they may only name hulls it lists.
            var factory = server.ResolveDependency<IComponentFactory>();
            var vouchered = new HashSet<string>();
            foreach (var voucherId in Vouchers)
            {
                Assert.That(prototypes.Index<EntityPrototype>(voucherId).TryGetComponent<ShipyardVoucherComponent>(out var voucher, factory));
                foreach (var vessel in voucher!.Vessels)
                {
                    Assert.That(listed, Does.Contain(vessel.Id), $"{voucherId} names {vessel}, which the ASF console doesn't sell.");
                    vouchered.Add(vessel.Id);
                }
            }

            Assert.That(listed.Any(id => !prototypes.Index<VesselPrototype>(id).Purchasable), Is.True,
                "The ASF console should list at least one voucher-only hull, like other faction shipyards.");
            foreach (var id in listed)
            {
                Assert.That(prototypes.TryIndex<VesselPrototype>(id, out var vessel), Is.True, $"Unknown vessel {id}.");
                Assert.That(vessel!.Purchasable || vouchered.Contains(id), Is.True, $"{id} is neither for sale nor on an ASF voucher.");
                // An ASF ID carries no other faction's access, so a gated hull could never be bought here.
                Assert.That(vessel.Access, Is.Empty, $"{id} needs {vessel.Access} access.");
            }

            // Members earn vouchers and coilguns by research: the post's server must hold the unlocking tech and its lathe must print them.
            var disciplines = new HashSet<string>();
            var serverQuery = entities.EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent, TransformComponent>();
            while (serverQuery.MoveNext(out _, out _, out var database, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    disciplines.UnionWith(database.SupportedDisciplines);
            }

            var printable = new HashSet<string>();
            var latheQuery = entities.EntityQueryEnumerator<LatheComponent, TransformComponent>();
            while (latheQuery.MoveNext(out _, out var lathe, out var xform))
            {
                if (xform.GridUid != grid!.Value.Owner)
                    continue;
                foreach (var pack in lathe.DynamicPacks)
                {
                    printable.UnionWith(prototypes.Index(pack).Recipes.Select(r => r.Id));
                }
            }

            var technologies = prototypes.EnumeratePrototypes<TechnologyPrototype>().ToList();
            foreach (var recipe in Vouchers.Concat(Arms))
            {
                Assert.That(printable, Does.Contain(recipe), $"No lathe on Lantern Post can print {recipe}.");
                Assert.That(technologies.Any(t => t.RecipeUnlocks.Contains(recipe) && disciplines.Contains(t.Discipline)),
                    $"Lantern Post's research server can't unlock {recipe}.");
            }

            var thrusters = 0;
            var thrusterQuery = entities.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusterQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    thrusters++;
            }

            Assert.That(thrusters, Is.Positive, "Lantern Post is a base ship and needs thrusters.");

            // Self-defence guns need a fire-control server on the same grid to be fired from the bridge.
            var guns = 0;
            var gunQuery = entities.EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (gunQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    guns++;
            }

            var fireControl = 0;
            var fireControlQuery = entities.EntityQueryEnumerator<FireControlServerComponent, TransformComponent>();
            while (fireControlQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    fireControl++;
            }

            Assert.That(guns, Is.Positive, "Lantern Post has no guns for self-defence.");
            Assert.That(fireControl, Is.Positive, "Lantern Post's guns have no fire-control server.");

            // Like the other faction stations: a station shield and station-grade plastitanium hull walls.
            var shields = 0;
            var shieldQuery = entities.EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
            while (shieldQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner)
                    shields++;
            }

            Assert.That(shields, Is.EqualTo(1), "Lantern Post should carry exactly one shield generator.");
            var tags = entities.System<TagSystem>();
            var weakWalls = new List<string>();
            var plastitanium = 0;
            var children = entities.GetComponent<TransformComponent>(grid!.Value.Owner).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                var id = entities.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID;
                if (id == null || !tags.HasTag(child, WallTag))
                    continue;
                if (id.StartsWith("WallPlastitanium"))
                    plastitanium++;
                else
                    weakWalls.Add(id);
            }

            Assert.That(plastitanium, Is.Positive, "Lantern Post has no plastitanium walls.");
            Assert.That(weakWalls, Is.Empty, "Lantern Post's hull should be plastitanium throughout.");

            // The post's armoury: racked coil rifles and pistols, a table of slug and magazine boxes and three stored Pathfinder modsuits.
            var armoury = new Dictionary<string, int>();
            var metaQuery = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (metaQuery.MoveNext(out var meta, out var xform))
            {
                if (xform.GridUid == grid!.Value.Owner && meta.EntityPrototype?.ID is { } protoId)
                    armoury[protoId] = armoury.GetValueOrDefault(protoId) + 1;
            }

            foreach (var (id, count) in Armoury)
            {
                Assert.That(armoury.GetValueOrDefault(id), Is.EqualTo(count), $"Lantern Post's post should hold {count} {id}.");
            }

            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every anchored, LV-fed machine on Lantern Post, the shield and engines included, is powered once the nets settle, and every lathe draws from the lab's material silo.</summary>
    [Test]
    public async Task LanternIsPowered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        MapId mapId = default;
        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId);
            Assert.That(loader.TryLoadGrid(mapId, Lantern, out var grid), Is.True);
            gridUid = grid!.Value.Owner;
        });

        // APC and LAPC supply ramps up over several seconds
        await server.WaitRunTicks(1800);

        await server.WaitAssertion(() =>
        {
            var unpowered = new List<string>();
            var query = entities.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var receiver, out var xform))
            {
                if (xform.GridUid != gridUid || !xform.Anchored || !receiver.NeedsPower || receiver.PowerDisabled || receiver.Powered)
                    continue;
                // hardpoints take no LV; their guns run on their own batteries
                if (!entities.HasComponent<ExtensionCableReceiverComponent>(uid))
                    continue;
                unpowered.Add($"{entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} at {xform.Coordinates.Position}");
            }

            Assert.That(unpowered, Is.Empty, "Unpowered on Lantern Post: " + string.Join(", ", unpowered));

            // A server earns points only from its clients, so without a data farm the post can't research at all.
            var research = entities.System<ResearchSystem>();
            var points = 0;
            var serverQuery = entities.EntityQueryEnumerator<ResearchServerComponent, TransformComponent>();
            while (serverQuery.MoveNext(out var uid, out var researchServer, out var xform))
            {
                if (xform.GridUid == gridUid)
                    points += research.GetPointsPerSecond(uid, researchServer);
            }

            Assert.That(points, Is.Positive, "Lantern Post's research server earns no points.");

            var silos = entities.System<SharedOreSiloSystem>();
            var unlinked = new List<string>();
            var latheQuery = entities.EntityQueryEnumerator<LatheComponent, OreSiloClientComponent, TransformComponent>();
            while (latheQuery.MoveNext(out var uid, out _, out var client, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                if (client.Silo is not { } silo
                    || !entities.TryGetComponent<OreSiloComponent>(silo, out var siloComp)
                    || !Enumerable.Contains(siloComp.Clients, uid)
                    || !silos.CanTransmitMaterials(silo, uid))
                    unlinked.Add($"{entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} at {xform.Coordinates.Position}");
            }

            Assert.That(unlinked, Is.Empty, "Lathes on Lantern Post not drawing from the silo: " + string.Join(", ", unlinked));
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }
}
