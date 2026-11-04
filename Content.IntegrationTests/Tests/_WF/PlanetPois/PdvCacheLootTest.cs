#nullable enable
using System.Linq;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>The planetary vault's PDV guards carry a field pack worth killing them for.</summary>
[TestFixture]
public sealed class PdvCacheLootTest
{
    private static readonly string[] Guards = { "WFMobPlanetPdvA", "WFMobPlanetPdvB", "WFMobPlanetPdvC", "WFMobPlanetPdvD" };

    [Test]
    public async Task GuardsCarryAFilledPack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            foreach (var id in Guards)
            {
                var guard = em.SpawnEntity(id, map.GridCoords);
                Assert.That(inventory.TryGetSlotEntity(guard, "back", out var pack), Is.True, $"{id} has no pack.");
                var contents = em.GetComponent<StorageComponent>(pack!.Value).Container.ContainedEntities
                    .Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID ?? string.Empty)
                    .ToList();
                Assert.That(contents.Any(item => item.StartsWith("SpaceCash")), Is.True, $"{id}'s pack has no cash: {string.Join(", ", contents)}.");
                Assert.That(contents.Count(item => item.StartsWith("Magazine")), Is.EqualTo(2), $"{id}'s pack has no spare magazines.");
                Assert.That(contents, Does.Contain("Brutepack").And.Contain("Ointment"));
                em.DeleteEntity(guard);
            }
        });

        await pair.CleanReturnAsync();
    }
}
