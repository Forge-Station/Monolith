#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Server.Movement.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// Flight in a cavern: an atmospheric jetpack lights there and carries its wearer up through a hole, but the ground
/// overhead is a ceiling everywhere else, whether its terrain is loaded or not.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernFlightTest
{
    private const string Surface = "WFSurfaceMerak";
    private const string Pack = "WFJetpackAtmospheric";

    /// <summary>How long a wearer holds ascend: the pack climbs half a level a second, so this is a level and a half.</summary>
    private const float AscendSeconds = 3f;

    /// <summary>East of the planet centre, clear of the gate.</summary>
    private static readonly Vector2i Site = new(200, 40);

    /// <summary>
    /// A wearer holding ascend under ground whose terrain isn't loaded, and so has no tiles, stays in the cavern
    /// under the ceiling; once the ground loads it is the same ceiling.
    /// </summary>
    [Test]
    public async Task GroundThatIsNotLoadedIsStillACeiling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadChunks(pair, world.Cavern, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearCavern(pair, world, Site, 1);

            await server.WaitAssertion(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                Assert.That(!maps.TryGetTileRef(world.Ground, groundGrid, Site, out var tile) || tile.Tile.IsEmpty, Is.True,
                    "Precondition: the ground over the site is loaded.");
            });

            var (mob, pack) = await WearLit(pair, world, Site);
            await Ascend(pair, mob, 2 * AscendSeconds);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "A flyer rose out of the cavern through ground that isn't loaded.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.InRange(0.9f, 1f),
                        "The flyer is not up against the ceiling.");
                    Assert.That(entMan.HasComponent<ActiveJetpackComponent>(pack), Is.True, "Precondition: the pack went out.");
                }
            });

            await LoadChunks(pair, world.Ground, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await Ascend(pair, mob, 2f);

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                    "A flyer rose out of the cavern through loaded ground."));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A wearer holding ascend under a hole in the ground comes up through it onto the ground's level.</summary>
    [Test]
    public async Task AJetpackFliesUpThroughAHole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadChunks(pair, world.Ground, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await LoadChunks(pair, world.Cavern, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));

            await server.WaitPost(() => maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), Site, Tile.Empty));
            await server.WaitRunTicks(3);

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades.ContainsKey(Site), Is.True,
                    "Precondition: the hole was not fitted out."));

            var (mob, _) = await WearLit(pair, world, Site);
            await Ascend(pair, mob, AscendSeconds);

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                    "A flyer under a hole did not come up through it onto the ground's level."));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Deletes everything anchored within a reach of a cavern tile, so a mob can stand and rise there.</summary>
    private static async Task ClearCavern(TestPair pair, World world, Vector2i index, int reach)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);

            for (var x = -reach; x <= reach; x++)
            for (var y = -reach; y <= reach; y++)
            {
                foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, grid, index + new Vector2i(x, y)).ToList())
                {
                    entMan.DeleteEntity(anchored);
                }
            }
        });
    }

    /// <summary>Spawns a human on a cavern tile at 1 g with a lit atmospheric jetpack on its back.</summary>
    private static async Task<(EntityUid Mob, EntityUid Pack)> WearLit(TestPair pair, World world, Vector2i index)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;
        var pack = EntityUid.Invalid;
        var worn = false;

        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFPlanetLayerComponent>(world.Cavern).Gravity = 1f;
            entMan.GetComponent<WFPlanetLayerComponent>(world.Ground).Gravity = 1f;

            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(world.Cavern, TileCentre(index)));
            pack = entMan.SpawnEntity(Pack, new EntityCoordinates(world.Cavern, TileCentre(index)));
            worn = server.System<InventorySystem>().TryEquip(mob, pack, "back", force: true);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        Assert.That(worn, Is.True, "Precondition: the pack did not go on the mob's back.");

        await server.WaitPost(() =>
            server.System<JetpackSystem>().SetEnabled(pack, entMan.GetComponent<JetpackComponent>(pack), true, mob));
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<JetpackUserComponent>(mob), Is.True, "The atmospheric jetpack did not light in the cavern."));

        return (mob, pack);
    }

    /// <summary>Holds the wearer's ascend key for a time, then lets it go.</summary>
    private static async Task Ascend(TestPair pair, EntityUid mob, float seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.GetComponent<JetpackUserComponent>(mob).AscendHeld = true);
        await server.WaitRunTicks(pair.SecondsToTicks(seconds));
        await server.WaitPost(() => entMan.GetComponent<JetpackUserComponent>(mob).AscendHeld = false);
    }
}
