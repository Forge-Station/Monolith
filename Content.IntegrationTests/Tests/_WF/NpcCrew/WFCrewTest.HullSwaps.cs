#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A pirate-crewed hull loses its durasteel plating for plastitanium, in place and still anchored.</summary>
    [Test]
    public async Task PirateHullSwapsDurasteelForPlastitanium()
    {
        var deck = await CreateDeck(new Vector2(700, 700), 7, true);
        EntityUid wall = default;
        EntityUid diagonal = default;
        await Server.WaitAssertion(() =>
        {
            wall = SEntMan.SpawnAtPosition("WFWallDurasteel", new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)));
            diagonal = SEntMan.SpawnAtPosition("WFWallDurasteelDiagonalCapital", new EntityCoordinates(deck, new Vector2(4.5f, 2.5f)));
            var transforms = Server.System<SharedTransformSystem>();
            foreach (var part in new[] { wall, diagonal })
            {
                if (!SEntMan.GetComponent<TransformComponent>(part).Anchored)
                    transforms.AnchorEntity(part);
            }

            transforms.SetLocalRotation(diagonal, Angle.FromDegrees(90));
            var swapped = Server.System<WFCrewSetupSystem>().ApplyHullSwaps(deck, "WFCrewProfilePirate");
            Assert.That(swapped, Is.EqualTo(2), "Both durasteel parts are swapped.");
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(wall) && SEntMan.Deleted(diagonal), Is.True, "The durasteel is gone.");
            var map = Server.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(deck);
            var plain = FindAnchored(map, deck, gridComp, new Vector2i(2, 2));
            var corner = FindAnchored(map, deck, gridComp, new Vector2i(4, 2));
            Assert.That(plain, Is.EqualTo("WallPlastitanium"), "The straight wall is plastitanium.");
            Assert.That(corner, Is.EqualTo("WallPlastitaniumDiagonalCapital"), "The reinforced diagonal stays reinforced and keeps its shape.");
            var rotated = map.GetAnchoredEntities(deck, gridComp, new Vector2i(4, 2));
            foreach (var part in rotated)
            {
                Assert.That(SEntMan.GetComponent<TransformComponent>(part).LocalRotation.Degrees, Is.EqualTo(90).Within(0.01), "The diagonal keeps its rotation.");
            }
        });
    }

    /// <summary>A big hull is swapped a share at a time over several ticks, never all in one, and ends with no durasteel.</summary>
    [Test]
    public async Task BigHullSwapsAreSpreadOverTicks()
    {
        var deck = await CreateDeck(new Vector2(760, 700), 24, true);
        var walls = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var transforms = Server.System<SharedTransformSystem>();
            for (var x = 0; x < 20; x++)
            {
                for (var y = 0; y < 20; y++)
                {
                    var wall = SEntMan.SpawnAtPosition("WFWallDurasteel", new EntityCoordinates(deck, new Vector2(x + 0.5f, y + 0.5f)));
                    if (!SEntMan.GetComponent<TransformComponent>(wall).Anchored)
                        transforms.AnchorEntity(wall);
                    walls.Add(wall);
                }
            }

            Assert.That(Server.System<WFCrewSetupSystem>().ApplyHullSwaps(deck, "WFCrewProfilePirate"), Is.EqualTo(walls.Count));
        });
        await RunTicks(1);

        var setup = Server.System<WFCrewSetupSystem>();
        await Server.WaitAssertion(() =>
            Assert.That(setup.HullSwapsPending, Is.True, "Four hundred parts are not all swapped in one tick."));
        for (var i = 0; i < 600 && setup.HullSwapsPending; i++)
            await RunTicks(1);
        await RunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(setup.HullSwapsPending, Is.False);
            Assert.That(walls.All(SEntMan.Deleted), Is.True, "Every durasteel wall is gone.");
            var map = Server.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(deck);
            for (var x = 0; x < 20; x++)
            {
                for (var y = 0; y < 20; y++)
                    Assert.That(FindAnchored(map, deck, gridComp, new Vector2i(x, y)), Is.EqualTo("WallPlastitanium"));
            }
        });
    }

    /// <summary>A profile with no swaps leaves a hull alone.</summary>
    [Test]
    public async Task HaulerHullKeepsItsDurasteel()
    {
        var deck = await CreateDeck(new Vector2(700, 760), 7, true);
        await Server.WaitAssertion(() =>
        {
            var wall = SEntMan.SpawnAtPosition("WFWallDurasteel", new EntityCoordinates(deck, new Vector2(2.5f, 2.5f)));
            if (!SEntMan.GetComponent<TransformComponent>(wall).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(wall);
            Assert.That(Server.System<WFCrewSetupSystem>().ApplyHullSwaps(deck, "WFCrewProfileHauler"), Is.EqualTo(0));
            Assert.That(SEntMan.Deleted(wall), Is.False);
        });
    }

    /// <summary>Every profile that kits its crew as pirates swaps out every durasteel wall.</summary>
    [Test]
    public async Task PirateProfilesNeverFlyDurasteel()
    {
        await Server.WaitAssertion(() =>
        {
            var durasteel = ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => proto.ID.StartsWith("WFWallDurasteel"))
                .Select(proto => proto.ID)
                .ToList();
            Assert.That(durasteel, Is.Not.Empty);

            var checkedAny = false;
            foreach (var profile in ProtoMan.EnumeratePrototypes<WFCrewProfilePrototype>())
            {
                if (!profile.Loadouts.Values.Any(gear => gear.Any(id => id.Id.StartsWith("WFCrewPirate"))))
                    continue;
                checkedAny = true;
                foreach (var wall in durasteel)
                {
                    Assert.That(profile.HullSwaps.ContainsKey(wall), Is.True, $"{profile.ID} crews pirates but keeps {wall}.");
                }
            }

            Assert.That(checkedAny, Is.True, "No pirate profile was found.");
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
