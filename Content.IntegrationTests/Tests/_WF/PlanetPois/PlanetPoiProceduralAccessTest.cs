#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Replays sealed procedural layouts and verifies validation replaces them before placement.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetPoiProceduralAccessTest
{
    [TestCase("WFPoiCarcinomaRuin", 424282498, 24, true)]
    [TestCase("WFPoiFervidusCamp", 1462688670, 40, true)]
    [TestCase("WFPoiCarcinomaRuin", 1977, 24, false)]
    [TestCase("WFPoiCarcinomaRuin", 8231, 24, false)]
    [TestCase("WFPoiFervidusCamp", 1977, 40, false)]
    [TestCase("WFPoiFervidusCamp", 8231, 40, false)]
    public async Task ProceduralLayoutHasRealEntranceBeforeMerge(string dungeon, int seed, int footprint, bool originallySealed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        Task<WFPlanetPoiSystem.StagedDungeon>? generation = null;
        await server.WaitPost(() =>
        {
            server.ResolveDependency<IRobustRandom>().SetSeed(seed);
            var entry = new WFPlanetPoiEntry { Dungeon = dungeon, Name = "wf-poi-carcinoma-ruin" };
            var roads = new WFPlanetRoadNetwork { Width = 5, ShoulderWidth = 1 };
            generation = server.System<WFPlanetPoiSystem>().GenerateAccessibleDungeonAsync(entry, roads,
                new Box2i(-footprint, -footprint, footprint + 1, footprint + 1), seed);
        });
        try
        {
            for (var attempt = 0; attempt < 900 && !generation!.IsCompleted; attempt++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                Assert.That(generation!.IsCompletedSuccessfully, Is.True, $"{dungeon} seed {seed}: {generation.Exception}");
                var staged = generation.GetAwaiter().GetResult();
                Assert.That(staged.Attempts, Is.InRange(originallySealed ? 2 : 1, 16), "The recorded sealed layout must be rejected before merging.");
                Assert.That(staged.Site.Sockets, Has.Count.GreaterThanOrEqualTo(1));
                foreach (var socket in staged.Site.Sockets)
                {
                    Assert.That(staged.Site.AccessPaths.TryGetValue(socket, out var path), Is.True);
                    Assert.That(path, Is.Not.Empty);
                    Assert.That(staged.Tiles, Does.Contain(path![0]), "The road must lead to real generated floor.");
                    Assert.That(path.Last(), Is.EqualTo(socket.Position));
                    var direction = WFPlanetRoadPlanner.Direction(socket.Direction);
                    var next = socket.Position + direction;
                    Assert.That(next.X < staged.Footprint.Left || next.X >= staged.Footprint.Right
                        || next.Y < staged.Footprint.Bottom || next.Y >= staged.Footprint.Top, Is.True);
                }
                Assert.That(staged.Tiles, Is.Not.Empty);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (generation?.IsCompletedSuccessfully == true) entities.DeleteEntity(generation.GetAwaiter().GetResult().Map);
            });
        }
        await pair.CleanReturnAsync();
    }
}
