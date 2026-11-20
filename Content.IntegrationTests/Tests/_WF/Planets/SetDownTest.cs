#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets;
using Content.Server.Shuttles.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A hull that sets down on a planet destroys what it lands on and the trees it would rest against, leaves a wall
/// someone built beside it, and hurts a mob under it without killing it.
/// </summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class SetDownTest
{
    private const string Tree = "FloraTree";

    [Test]
    public async Task AHullSetsDownOnTreesAndThingsAndOnlyHurtsMobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        // Five tiles square, its corner just short of the origin, so that the tile row above it is a hand's breadth clear.
        var hull = await BuildDebris(pair, await CavernFixture.MapIdOf(pair, ground), size: 5, offset: new Vector2(0f, -0.15f));
        await MapInitHull(pair, hull);

        var under = EntityUid.Invalid;
        var beside = EntityUid.Invalid;
        var apart = EntityUid.Invalid;
        var crate = EntityUid.Invalid;
        var wall = EntityUid.Invalid;
        var mob = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            Assert.That(levels.TryEnterTransit(new Entity<MapGridComponent>(hull, grid), preferUpperGap: true), Is.True, "Precondition: the hull would not lift off.");

            under = entMan.SpawnEntity(Tree, new EntityCoordinates(ground, new Vector2(2.5f, 2.5f)));
            beside = entMan.SpawnEntity(Tree, new EntityCoordinates(ground, new Vector2(5.5f, 2.5f)));
            apart = entMan.SpawnEntity(Tree, new EntityCoordinates(ground, new Vector2(6.5f, 2.5f)));
            crate = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(ground, new Vector2(1.5f, 1.5f)));
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 5.5f)));
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ground, new Vector2(3.5f, 3.5f)));

            // The biome roots what it grows and knows it for its own; a tree spawned by hand is loose and nobody's.
            foreach (var tree in new[] { under, beside, apart })
            {
                server.System<SharedTransformSystem>().AnchorEntity(tree);
                entMan.EnsureComponent<WFBiomeGrownComponent>(tree).Tile =
                    maps.TileIndicesFor(ground, entMan.GetComponent<MapGridComponent>(ground), entMan.GetComponent<TransformComponent>(tree).Coordinates);
            }

            Assert.That(new[] { under, beside, apart }.All(tree => entMan.GetComponent<TransformComponent>(tree).Anchored), Is.True,
                "Precondition: the trees are not rooted.");
            Assert.That(levels.TryExitTransit(new Entity<MapGridComponent>(hull, grid)), Is.True, "Precondition: the hull would not set down.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground), "Precondition: the hull is not on the ground.");
                Assert.That(entMan.Deleted(under), Is.True, "A tree under the hull is still standing.");
                Assert.That(entMan.Deleted(beside), Is.True, "A tree the hull rests against is still standing.");
                Assert.That(entMan.Deleted(apart), Is.False, "A tree a tile clear of the hull was destroyed.");
                Assert.That(entMan.Deleted(crate), Is.True, "A crate under the hull is still there.");
                Assert.That(entMan.Deleted(wall), Is.False, "A wall built just clear of the hull was broken by a gentle landing.");
                Assert.That(entMan.Deleted(mob), Is.False, "A mob under the hull was deleted or gibbed.");
            });

            if (entMan.Deleted(mob))
                return;

            var at = server.System<SharedTransformSystem>().GetWorldPosition(mob);
            var half = new Vector2(0.4f);

            Assert.Multiple(() =>
            {
                Assert.That(server.System<MobStateSystem>().IsAlive(mob), Is.True, "A mob under the hull was killed.");
                Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage.Float(), Is.GreaterThanOrEqualTo(ShuttleSystem.WfSetDownDamage / 2f),
                    "A mob under the hull was not hurt.");
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(ground), "The mob left the ground.");
                Assert.That(maps.GetTilesIntersecting(hull, grid, new Box2(at - half, at + half)).Any(tile => !tile.Tile.IsEmpty), Is.False,
                    $"The mob was left under the hull, at {at}.");
            });
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
