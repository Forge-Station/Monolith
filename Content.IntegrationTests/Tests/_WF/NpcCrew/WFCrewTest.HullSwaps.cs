#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A pirate-crewed hull loses its adamantite plating for plastitanium, in place and still anchored.</summary>
    [Test]
    public async Task PirateHullSwapsAdamantiteForPlastitanium()
    {
        var deck = await CreateDeck(new Vector2(700, 700), 7, true);
        EntityUid wall = default;
        EntityUid diagonal = default;
        await Server.WaitAssertion(() =>
        {
            wall = SEntMan.SpawnAtPosition("WFWallAdamantite", new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)));
            diagonal = SEntMan.SpawnAtPosition("WFWallAdamantiteDiagonalCapital", new EntityCoordinates(deck, new Vector2(4.5f, 2.5f)));
            var transforms = Server.System<SharedTransformSystem>();
            foreach (var part in new[] { wall, diagonal })
            {
                if (!SEntMan.GetComponent<TransformComponent>(part).Anchored)
                    transforms.AnchorEntity(part);
            }

            transforms.SetLocalRotation(diagonal, Angle.FromDegrees(90));
            var swapped = Server.System<WFCrewSetupSystem>().ApplyHullSwaps(deck, "WFCrewProfilePirate");
            Assert.That(swapped, Is.EqualTo(2), "Both adamantite parts are swapped.");
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(wall) && SEntMan.Deleted(diagonal), Is.True, "The adamantite is gone.");
            var map = Server.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(deck);
            var plain = FindAnchored(map, deck, gridComp, new Vector2i(2, 2));
            var corner = FindAnchored(map, deck, gridComp, new Vector2i(4, 2));
            Assert.That(plain, Is.EqualTo("WallPlastitanium"), "The straight wall is plastitanium.");
            Assert.That(corner, Is.EqualTo("WallPlastitaniumDiagonal"), "The diagonal keeps its shape.");
            var rotated = map.GetAnchoredEntities(deck, gridComp, new Vector2i(4, 2));
            foreach (var part in rotated)
            {
                Assert.That(SEntMan.GetComponent<TransformComponent>(part).LocalRotation.Degrees, Is.EqualTo(90).Within(0.01), "The diagonal keeps its rotation.");
            }
        });
    }

    /// <summary>A profile with no swaps leaves a hull alone.</summary>
    [Test]
    public async Task HaulerHullKeepsItsAdamantite()
    {
        var deck = await CreateDeck(new Vector2(700, 760), 7, true);
        await Server.WaitAssertion(() =>
        {
            var wall = SEntMan.SpawnAtPosition("WFWallAdamantite", new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)));
            if (!SEntMan.GetComponent<TransformComponent>(wall).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(wall);
            Assert.That(Server.System<WFCrewSetupSystem>().ApplyHullSwaps(deck, "WFCrewProfileHauler"), Is.EqualTo(0));
            Assert.That(SEntMan.Deleted(wall), Is.False);
        });
    }

    private string? FindAnchored(SharedMapSystem map, EntityUid grid, Robust.Shared.Map.Components.MapGridComponent gridComp, Vector2i tile)
    {
        foreach (var part in map.GetAnchoredEntities(grid, gridComp, tile))
        {
            var id = SEntMan.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID;
            if (id != null && id.StartsWith("Wall"))
                return id;
        }

        return null;
    }
}
