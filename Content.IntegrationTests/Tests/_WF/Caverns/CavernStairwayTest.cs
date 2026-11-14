#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// The stairs the caverns cut under every way down: a mob walks them up and down with one level change each way,
/// unhurt, on every world, and falling into a hole still works beside them.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernStairsSystem))]
public sealed class CavernStairwayTest
{
    private const string Mob = "MobHuman";

    /// <summary>How far a walking mob is moved between looks, in tiles.</summary>
    private const float Stride = 0.05f;

    /// <summary>Where the dug-hole walk happens, east of the planet centre and clear of the gate.</summary>
    private static readonly Vector2i Site = new(200, 40);

    /// <summary>
    /// A mob stood at the foot of each world's gate stairs and walked to the lip changes level once, stands on the ground
    /// on the lip tile at rest and unhurt; walked back down it changes level once and stands at the foot.
    /// </summary>
    [Test]
    public async Task GateStairsAreWalkedUpAndDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var spec = CavernOf(pair, surfaceId).Mouths;
                var gate = await Gate(pair, world);
                var stairsTile = StairsTile(gate, spec);
                var foot = stairsTile - spec.StairsSide.ToIntVec();
                var top = ClimbTile(gate);

                await LoadChunks(pair, world.Ground, stairsTile - new Vector2i(ChunkSize, ChunkSize), stairsTile + new Vector2i(ChunkSize, ChunkSize));
                await LoadChunks(pair, world.Cavern, stairsTile - new Vector2i(ChunkSize, ChunkSize), stairsTile + new Vector2i(ChunkSize, ChunkSize));

                await server.WaitAssertion(() =>
                {
                    var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(ground.Stairways.ContainsKey(stairsTile), Is.True, $"{surfaceId}: the gate has no stairs under its edge.");
                        Assert.That(entMan.GetComponent<WFCavernStairsComponent>(ground.Stairways[stairsTile]).Open, Is.True,
                            $"{surfaceId}: the gate's stairs did not open the ground.");
                    }
                });

                var mob = await SpawnSettled(pair, world.Cavern, TileCentre(foot));

                var changes = await Walk(pair, mob, TileCentre(foot), TileCentre(top));
                await server.WaitRunTicks(30);
                await AssertStands(pair, mob, world.Ground, top, changes, $"{surfaceId}: walking up the gate stairs");

                changes = await Walk(pair, mob, TileCentre(top), TileCentre(foot));
                await server.WaitRunTicks(30);
                await AssertStands(pair, mob, world.Cavern, foot, changes, $"{surfaceId}: walking down the gate stairs");
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Stepping into the gate's hole away from the stairs still drops a mob into the cavern; the stairs don't catch it.</summary>
    [Test]
    public async Task AHoleBesideTheStairsStillDrops()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var spec = CavernOf(pair, surfaceId).Mouths;
            var gate = await Gate(pair, world);
            var drop = DropTile(gate, spec);

            Assert.That(drop, Is.Not.EqualTo(StairsTile(gate, spec)), "Precondition: the drop tile is the stairs tile.");

            var mob = await SpawnSettled(pair, world.Ground, TileCentre(ClimbTile(gate)));
            await server.WaitPost(() => transform.SetCoordinates(mob, new EntityCoordinates(world.Ground, TileCentre(drop))));
            await server.WaitRunTicks(pair.SecondsToTicks(3f));

            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(mob);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.MapUid, Is.EqualTo(world.Cavern), "A mob stepping into the hole did not fall to the cavern.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).Velocity, Is.Zero, "The mob never came to rest below.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hole opened in the ground, as a blast or a shovel does, gets walkable stairs under it that come out on solid
    /// ground beside it, and a mob walks up and down them.
    /// </summary>
    [Test]
    public async Task AnOpenedHolesStairsAreWalked()
    {
        const string surfaceId = "WFSurfaceMerak";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            await LoadChunks(pair, world.Ground, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await LoadChunks(pair, world.Cavern, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 3);

            await server.WaitPost(() => maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), Site, Tile.Empty));
            await server.WaitRunTicks(5);

            var exit = Vector2i.Zero;
            var foot = Vector2i.Zero;

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

                Assert.That(ground.Stairways.TryGetValue(Site, out var stairs), Is.True, "The opened hole got no stairs under it.");

                var xform = entMan.GetComponent<TransformComponent>(stairs);
                var up = -xform.LocalRotation.GetCardinalDir().ToIntVec();
                exit = Site + up;
                foot = Site - up;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<WFCavernStairsComponent>(stairs).Open, Is.True, "The stairs did not open the ground above them.");
                    Assert.That(IsEmpty(maps, world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), exit), Is.False,
                        "The stairs come out on no ground.");
                }
            });

            var mob = await SpawnSettled(pair, world.Cavern, TileCentre(foot));

            var changes = await Walk(pair, mob, TileCentre(foot), TileCentre(exit));
            await server.WaitRunTicks(30);
            await AssertStands(pair, mob, world.Ground, exit, changes, "Walking up an opened hole's stairs");

            changes = await Walk(pair, mob, TileCentre(exit), TileCentre(foot));
            await server.WaitRunTicks(30);
            await AssertStands(pair, mob, world.Cavern, foot, changes, "Walking down an opened hole's stairs");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Deletes everything anchored within a reach of a tile on both maps, so the ground and the cavern floor are bare.</summary>
    private static async Task ClearSite(TestPair pair, World world, Vector2i index, int reach)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            foreach (var map in new[] { world.Ground, world.Cavern })
            {
                var grid = entMan.GetComponent<MapGridComponent>(map);

                for (var x = -reach; x <= reach; x++)
                for (var y = -reach; y <= reach; y++)
                {
                    foreach (var anchored in maps.GetAnchoredEntities(map, grid, index + new Vector2i(x, y)).ToList())
                    {
                        entMan.DeleteEntity(anchored);
                    }
                }
            }
        });
    }

    /// <summary>Spawns a mob and gives it a second to settle.</summary>
    private static async Task<EntityUid> SpawnSettled(TestPair pair, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() => mob = server.EntMan.SpawnEntity(Mob, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return mob;
    }

    /// <summary>Moves a mob from one spot to another a stride at a time, two ticks a stride, and counts its map changes.</summary>
    private static async Task<int> Walk(TestPair pair, EntityUid mob, Vector2 from, Vector2 to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var strides = (int) MathF.Round((to - from).Length() / Stride);
        var changes = 0;
        EntityUid? map = null;

        await server.WaitPost(() => map = entMan.GetComponent<TransformComponent>(mob).MapUid);

        for (var i = 0; i <= strides; i++)
        {
            var position = Vector2.Lerp(from, to, (float) i / strides);

            await server.WaitPost(() => transform.SetWorldPosition(mob, position));
            await server.WaitRunTicks(2);
            await server.WaitPost(() =>
            {
                var now = entMan.GetComponent<TransformComponent>(mob).MapUid;
                if (now != map)
                    changes++;

                map = now;
            });
        }

        return changes;
    }

    /// <summary>Asserts that a walk ended with one map change and the mob at rest, unhurt, on a tile of a map.</summary>
    private static async Task AssertStands(TestPair pair, EntityUid mob, EntityUid map, Vector2i tile, int changes, string what)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);
            var blunt = entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Blunt", FixedPoint2.Zero).Int();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(changes, Is.EqualTo(1), $"{what} changed map {changes} times.");
                Assert.That(xform.MapUid, Is.EqualTo(map), $"{what} ended on the wrong map.");
                Assert.That(maps.TileIndicesFor(map, entMan.GetComponent<MapGridComponent>(map), xform.Coordinates), Is.EqualTo(tile),
                    $"{what} ended on the wrong tile.");
                Assert.That(physics.LocalPosition, Is.EqualTo(0f).Within(0.01f), $"{what} left the mob off the floor.");
                Assert.That(physics.Velocity, Is.Zero, $"{what} left the mob moving.");
                Assert.That(blunt, Is.Zero, $"{what} hurt the mob.");
            }
        });
    }

    /// <summary>Whether a map has no tile at an index.</summary>
    private static bool IsEmpty(SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i index)
    {
        return !maps.TryGetTileRef(map, grid, index, out var tile) || tile.Tile.IsEmpty;
    }
}
