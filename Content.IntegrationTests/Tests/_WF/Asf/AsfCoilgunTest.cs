using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the ASF coilguns spawn loaded, have no bolt, and fire caseless slugs.</summary>
[TestFixture]
public sealed class AsfCoilgunTest
{
    private static readonly (string Gun, int Magazine)[] Guns =
        [("WFAsfWeaponRifleCoil", 24), ("WFAsfWeaponPistolCoil", 15)];

    private static readonly EntProtoId Slug = "WFAsfCartridgeCoilSlug";

    [Test]
    public async Task CoilgunsSpawnLoadedAndCaseless()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var slug = prototypes.Index(Slug);
            Assert.That(slug.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, factory), Is.True);
            Assert.That(cartridge!.DeleteOnSpawn, Is.True, "Coil slugs are caseless and must leave nothing behind.");

            maps.CreateMap(out var mapId);
            foreach (var (id, magazine) in Guns)
            {
                var gun = entities.SpawnEntity(id, new MapCoordinates(0, 0, mapId));
                var ammo = new GetAmmoCountEvent();
                entities.EventBus.RaiseLocalEvent(gun, ref ammo);
                Assert.That(ammo.Count, Is.EqualTo(magazine + 1), $"{id} should spawn with a full magazine and a chambered slug.");
                Assert.That(entities.GetComponent<ChamberMagazineAmmoProviderComponent>(gun).BoltClosed, Is.Null,
                    $"{id} is a coilgun and has no bolt to rack.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
