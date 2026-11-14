#nullable enable
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A parasite infects a host, falls off, and the incubated larva bursts out.</summary>
[TestFixture]
public sealed class XenoParasiteTest
{
    private static readonly string[] LifecycleEntities =
    [
        "XenoEgg", "XenoEggDestroyed", "XenoEggDestroyedFragile", "XenoEggDestroyedFragileSustained",
        "CMXenoParasite", "RMCXenoParasiteWatcher", "RMCXenoParasitePrimeHiveAssign",
        "XenoProjectileImpactEffect", "XenoSpitProjectile", "XenoChargedSpitProjectile", "XenoSlowingSpitProjectile",
        "XenoQueenNeuroSpitProjectile", "XenoScatteredSpitProjectile", "XenoAcidShotgunProjectile",
        "XenoBoneChipsProjectile", "XenoAcidBallSpitProjectile", "XenoSpitProjectilePraetorian",
        "XenoSpitProjectilePraetorianWarden", "XenoAcidBallProjectile", "XenoOppressorTailHook", "XenoSpikeProjectile",
        "XenoAcidNormal", "XenoAcidWeak", "XenoAcidStrong", "XenoAcidSprayWeak", "XenoAcidSprayStrong",
        "XenoAcidSprayAcidStacks", "XenoAcidSprayTrap", "XenoAcidExtinguishEffect", "RMCAcidHole", "RMCGirderDamaged",
        "RMCSmokeAcid", "RMCSmokeNeurotoxin",
    ];

    /// <summary>Every eggs, parasite, projectile and acid prototype spawns and initialises.</summary>
    [Test]
    public async Task LifecycleEntitiesSpawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            foreach (var id in LifecycleEntities)
            {
                var uid = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.EntityExists(uid), id);
            }
        });
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    /// <summary>The parasite takes the host's mask slot, the host is infected after it falls off, and a larva spawns once incubated.</summary>
    [Test]
    public async Task ParasiteInfectsAndLarvaSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var inventory = entities.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        EntityUid host = default;
        EntityUid parasite = default;
        await server.WaitPost(() =>
        {
            host = entities.SpawnEntity("MobHuman", map.GridCoords);
            parasite = entities.SpawnEntity("CMXenoParasite", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<InfectableComponent>(host), "A human is infectable.");
            Assert.That(parasites.Infect((parasite, entities.GetComponent<XenoParasiteComponent>(parasite)), host, false, true),
                "The parasite should attach.");
            Assert.That(inventory.TryGetSlotEntity(host, "mask", out var mask) && mask == parasite,
                "The parasite should sit in the host's mask slot.");
        });

        // The parasite falls off after its fall-off delay, leaving the host infected.
        await pair.RunSeconds(17);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent(host, out VictimInfectedComponent? infected), "The host should be infected.");
            Assert.That(entities.HasComponent<ParasiteSpentComponent>(parasite), "The parasite should be spent.");
            Assert.That(infected!.SpawnedLarva, Is.Null, "The larva should still be incubating.");
            parasites.SetBurstDelay((host, infected), TimeSpan.FromSeconds(1));
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            var infected = entities.GetComponent<VictimInfectedComponent>(host);
            Assert.That(infected.SpawnedLarva, Is.Not.Null, "A larva should have spawned in the host.");
            Assert.That(entities.GetComponent<MetaDataComponent>(infected.SpawnedLarva!.Value).EntityPrototype?.ID, Is.EqualTo("CMXenoLarva"));
        });

        await pair.CleanReturnAsync();
    }
}
