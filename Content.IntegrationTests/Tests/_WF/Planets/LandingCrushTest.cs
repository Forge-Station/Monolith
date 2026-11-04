#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._WF.LandingGuide;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A hull settling onto a planet is only held off by what it can't crush: trees, rock, furniture, machines, tables
/// and grilles are flattened as it lands, while built walls, windows and doors still refuse the landing.
/// </summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
[TestOf(typeof(WFLandingSurveySystem))]
public sealed class LandingCrushTest
{
    private const string Tree = "FloraTree";

    /// <summary>Things that cover a landing spot and give way to the hull.</summary>
    private static readonly (string Proto, Vector2 At)[] Crushable =
    {
        (Tree, new Vector2(2.5f, 2.5f)),
        (Tree, new Vector2(0.5f, 4.5f)),
        ("CrateGenericSteel", new Vector2(1.5f, 1.5f)),
        ("TableWood", new Vector2(3.5f, 1.5f)),
        ("Grille", new Vector2(4.5f, 3.5f)),
        ("WallRock", new Vector2(0.5f, 2.5f)),
    };

    [Test]
    public async Task ACoveredSpotAcceptsALandingAndTheCoverIsGone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();
        var survey = server.System<WFLandingSurveySystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        var hull = await BuildDebris(pair, await CavernFixture.MapIdOf(pair, ground), size: 5, offset: new Vector2(0f, -0.15f));
        await MapInitHull(pair, hull);

        var spawned = new List<EntityUid>();
        var refused = true;
        var read = new List<WFLandingTile>();

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            Assert.That(levels.TryEnterTransit(new Entity<MapGridComponent>(hull, grid), preferUpperGap: true), Is.True,
                "Precondition: the hull would not lift off.");

            foreach (var (proto, at) in Crushable)
            {
                var uid = entMan.SpawnEntity(proto, new EntityCoordinates(ground, at));
                Anchor(pair, uid);
                spawned.Add(uid);
            }

            Assert.That(spawned.All(uid => entMan.GetComponent<TransformComponent>(uid).Anchored), Is.True,
                "Precondition: some of the cover is not anchored.");

            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            refused = levels.WfObstacleUnder(hull, ground);

            foreach (var (_, at) in Crushable)
            {
                read.Add(survey.Classify((ground, groundGrid), maps.TileIndicesFor(ground, groundGrid, new EntityCoordinates(ground, at))));
            }

            Assert.That(levels.TryExitTransit(new Entity<MapGridComponent>(hull, grid)), Is.True, "The hull would not set down.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(refused, Is.False, "Trees, a crate, a table and a grille under the hull held its landing off.");
                Assert.That(read, Is.All.EqualTo(WFLandingTile.Clear), "The landing guide would show the cover as blocked.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground), "The hull is not on the ground.");

                for (var i = 0; i < spawned.Count; i++)
                {
                    Assert.That(entMan.Deleted(spawned[i]), Is.True, $"{Crushable[i].Proto} at {Crushable[i].At} was left standing under the hull.");
                }
            });
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [TestCase("WallSolid")]
    [TestCase("Window")]
    public async Task WallsWindowsAndDoorsStillRefuseTheLanding(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();
        var survey = server.System<WFLandingSurveySystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        var hull = await BuildDebris(pair, await CavernFixture.MapIdOf(pair, ground), size: 5, offset: new Vector2(0f, -0.15f));
        await MapInitHull(pair, hull);

        var alone = true;
        var withCover = false;
        var tile = WFLandingTile.Clear;
        var after = true;

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            Assert.That(levels.TryEnterTransit(new Entity<MapGridComponent>(hull, grid), preferUpperGap: true), Is.True,
                "Precondition: the hull would not lift off.");

            var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
            alone = levels.WfObstacleUnder(hull, ground);

            var cover = entMan.SpawnEntity(Tree, new EntityCoordinates(ground, new Vector2(1.5f, 1.5f)));
            Anchor(pair, cover);

            var thing = entMan.SpawnEntity(proto, new EntityCoordinates(ground, new Vector2(3.5f, 3.5f)));
            Anchor(pair, thing);

            withCover = levels.WfObstacleUnder(hull, ground);
            tile = survey.Classify((ground, groundGrid), maps.TileIndicesFor(ground, groundGrid, new EntityCoordinates(ground, new Vector2(3.5f, 3.5f))));

            entMan.DeleteEntity(thing);
            after = levels.WfObstacleUnder(hull, ground);
        });

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.False, "Precondition: bare ground refused the landing.");
            Assert.That(withCover, Is.True, $"{proto} under the hull did not hold its landing off.");
            Assert.That(tile, Is.EqualTo(WFLandingTile.Blocked), $"{proto} did not read as blocked in the landing guide.");
            Assert.That(after, Is.False, "The tree alone still held the landing off once the other thing was gone.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static void Anchor(TestPair pair, EntityUid uid)
    {
        if (!pair.Server.EntMan.GetComponent<TransformComponent>(uid).Anchored)
            pair.Server.System<SharedTransformSystem>().AnchorEntity(uid);
    }
}
