#nullable enable
using Content.IntegrationTests.Pair;
using Content.Shared._Mono.ArmorPiercing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipArmor;

/// <summary>
/// Hull walls resolve the ArmorThickness the armor-backed mount scales by. Checks the
/// resolved value of each wall prototype, since multi-parent inheritance decides it.
/// </summary>
[TestFixture]
public sealed class WFHullArmorThicknessTest
{
    private static readonly (string Id, int Thickness)[] Expected =
    {
        ("WallPlastitanium", 10),
        ("WallPlastitaniumDiagonal", 10),
        ("WallPlastitaniumOutpost", 10),
        ("WallPlastitaniumDiagonalOutpost", 10),
        ("WFWallDurasteel", 13),
        ("WFWallDurasteelDiagonal", 13),
        ("WFWallDurasteelCapital", 15),
        ("WFWallDurasteelDiagonalCapital", 15),
        ("WallPlastitaniumCapital", 15),
        ("WallShuttle", 5),
        ("WallReinforced", 4),
        ("WallSolid", 3),
    };

    /// <summary>Every hull wall resolves to its expected ArmorThickness.</summary>
    [Test]
    public async Task HullWallsResolveExpectedThickness()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var compFactory = server.ResolveDependency<IComponentFactory>();

            Assert.Multiple(() =>
            {
                foreach (var (id, expected) in Expected)
                {
                    var proto = protoMan.Index<EntityPrototype>(id);
                    var actual = proto.TryComp<ArmorThicknessComponent>(out var comp, compFactory)
                        ? comp!.Thickness
                        : -1;
                    Assert.That(actual, Is.EqualTo(expected), $"{id}: {actual}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
