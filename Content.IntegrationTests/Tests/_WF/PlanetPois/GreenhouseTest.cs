#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Doors.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>The greenhouse stands on the breathable worlds, roofed, with its eighteen soil plots whichever way it turns.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class GreenhouseTest
{
    private const string GreenhousePath = "/Maps/_WF/PlanetPois/greenhouse.yml";
    private const int Plots = 18;

    [Test]
    public async Task GreenhouseIsOnTheBreathableWorldsOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var expected = new Dictionary<string, int?>
            {
                ["WFPoisAsclepiu"] = 1, ["WFPoisMerak"] = 0, ["WFPoisFervidus"] = null,
                ["WFPoisThrascias"] = null, ["WFPoisAerumna"] = null, ["WFPoisCarcinoma"] = null,
            };
            foreach (var (table, minimum) in expected)
            {
                var entries = server.ProtoMan.Index<WFPlanetPoiTablePrototype>(table).Entries.Where(e => e.MapPath == GreenhousePath).ToList();
                if (minimum == null)
                {
                    Assert.That(entries, Is.Empty, $"{table} has no air to grow in, but has a greenhouse.");
                    continue;
                }

                Assert.That(entries, Has.Count.EqualTo(1), $"{table} has no greenhouse entry.");
                Assert.That(entries[0].MinCount, Is.EqualTo(minimum), table);
                Assert.That(entries[0].MaxCount, Is.EqualTo(1), table);
                Assert.That(entries[0].Name.Id, Is.EqualTo("wf-poi-greenhouse"), table);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GreenhouseLoadsWholeAndRoofed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(GreenhousePath), out var loaded), Is.True);
            var grid = loaded!.Value;
            Assert.That(Grids(entMan, map), Has.Count.EqualTo(1), "The greenhouse loaded in pieces.");

            var roof = entMan.GetComponent<RoofComponent>(grid.Owner);
            var roofs = server.System<SharedRoofSystem>();
            var tiles = maps.GetAllTiles(grid.Owner, grid.Comp).Select(tile => tile.GridIndices).ToList();
            Assert.That(tiles.Where(tile => !roofs.IsRooved((grid.Owner, grid.Comp, roof), tile)), Is.Empty, "Part of the greenhouse is open to the sky.");

            var children = Children(entMan, grid.Owner);
            Assert.That(children.Count(uid => Proto(entMan, uid) == "hydroponicsSoil"), Is.EqualTo(Plots));
            Assert.That(children.Any(entMan.HasComponent<DoorComponent>), Is.True, "The greenhouse has no door.");
            Assert.That(children.Any(uid => Proto(entMan, uid) == "CrateHydroponicsSeeds"), Is.True, "The seed crate is missing.");
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The plots, mapped turned half round, survive any turn of the site: all there, anchored, on their tiles.</summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task TurnedGreenhouseKeepsItsPlots(int turns)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var sourceMap, runMapInit: false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(sourceMap, new ResPath(GreenhousePath), out var loaded), Is.True);
            var source = loaded!.Value;
            var plots = Children(entMan, source.Owner).Where(uid => Proto(entMan, uid) == "hydroponicsSoil")
                .ToDictionary(uid => uid, uid => entMan.GetComponent<TransformComponent>(uid).LocalPosition.Floored());

            var target = maps.CreateMap(out _, runMapInit: false);
            var targetGrid = entMan.AddComponent<MapGridComponent>(target);
            server.System<SharedTransformSystem>().SetCoordinates(source.Owner, new EntityCoordinates(target, System.Numerics.Vector2.Zero));
            server.System<GridFixtureSystem>().Merge(target, source.Owner, Vector2i.Zero, Angle.FromDegrees(90 * turns), targetGrid, source.Comp);
            server.System<WFPlanetPoiSystem>().UnturnFixedRotation(Children(entMan, target));

            Assert.That(plots, Has.Count.EqualTo(Plots));
            foreach (var (plot, tile) in plots)
            {
                Assert.That(entMan.Deleted(plot), Is.False, $"A plot from {tile} was lost in the turn.");
                var xform = entMan.GetComponent<TransformComponent>(plot);
                Assert.That(xform.Anchored, Is.True, $"The plot from {tile} came loose.");
                Assert.That(xform.LocalPosition.Floored(), Is.EqualTo(WFPlanetPoiSystem.TurnTile(tile, turns)), $"The plot from {tile} moved off its tile.");
            }

            entMan.DeleteEntity(target);
            maps.DeleteMap(sourceMap);
        });

        await pair.CleanReturnAsync();
    }

    private static string Proto(IEntityManager entMan, EntityUid uid) =>
        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty;

    private static List<EntityUid> Grids(IEntityManager entMan, EntityUid map) =>
        Children(entMan, map).Where(entMan.HasComponent<MapGridComponent>).ToList();

    private static List<EntityUid> Children(IEntityManager entMan, EntityUid parent)
    {
        var result = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
            result.Add(child);
        return result;
    }
}
