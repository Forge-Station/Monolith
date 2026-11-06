#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Destructible;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipArmor;

/// <summary>
/// Durasteel walls are their plastitanium counterparts with five thirds the health and the same resistances, and no
/// recipe builds them.
/// </summary>
[TestFixture]
public sealed class WFDurasteelWallTest
{
    private static readonly (string Plastitanium, string Durasteel)[] Pairs =
    {
        ("WallPlastitanium", "WFWallDurasteel"),
        ("WallPlastitaniumDiagonal", "WFWallDurasteelDiagonal"),
        ("WallPlastitaniumCapital", "WFWallDurasteelCapital"),
        ("WallPlastitaniumDiagonalCapital", "WFWallDurasteelDiagonalCapital"),
    };

    /// <summary>Each durasteel wall falls at five thirds its counterpart's damage, to the nearest 5, and resists as it does.</summary>
    [Test]
    public async Task DurasteelIsFiveThirdsItsCounterpart()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var destructible = server.System<DestructibleSystem>();
            Assert.Multiple(() =>
            {
                foreach (var (plastitaniumId, durasteelId) in Pairs)
                {
                    var plastitanium = em.SpawnEntity(plastitaniumId, map.MapCoords);
                    var durasteel = em.SpawnEntity(durasteelId, map.MapCoords);
                    Assert.That(destructible.TryGetDestroyedAt(plastitanium, out var plastitaniumAt), plastitaniumId);
                    Assert.That(destructible.TryGetDestroyedAt(durasteel, out var durasteelAt), durasteelId);
                    Assert.That(durasteelAt!.Value.Float(), Is.EqualTo(plastitaniumAt!.Value.Float() * 5f / 3f).Within(5f),
                        $"{durasteelId} should take five thirds {plastitaniumId}'s damage.");
                    Assert.That(em.GetComponent<DamageableComponent>(durasteel).DamageModifierSetId,
                        Is.EqualTo(em.GetComponent<DamageableComponent>(plastitanium).DamageModifierSetId),
                        $"{durasteelId} should resist as {plastitaniumId} does.");
                    em.DeleteEntity(plastitanium);
                    em.DeleteEntity(durasteel);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>No construction recipe builds a durasteel wall; only the drydock refit makes one.</summary>
    [Test]
    public async Task NoRecipeBuildsDurasteel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        var recipes = protoMan.EnumeratePrototypes<ConstructionPrototype>()
            .Where(recipe => recipe.Graph == "WFDurasteelWall")
            .Select(recipe => recipe.ID)
            .ToList();
        Assert.That(recipes, Is.Empty, "Durasteel walls should have no recipe.");

        await pair.CleanReturnAsync();
    }
}
