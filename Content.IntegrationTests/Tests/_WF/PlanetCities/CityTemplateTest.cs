#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Doors.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetCities;

/// <summary>
/// Every city plot and sewer map follows the mapping rules in Content.Server/_WF/PlanetCities/README.md: full rectangles of the
/// allowed sizes, street doors on the south edge, stairs that meet a stairwell on the floor above, and sewer openings
/// on the edges.
/// </summary>
[TestFixture]
public sealed class CityTemplateTest
{
    private static readonly ResPath Root = new("/Maps/_WF/PlanetCities");
    private static readonly string[] Floors = { "basement", "ground", "upper1", "upper2" };

    private sealed record Loaded(ResPath Path, Box2i Bounds, int Tiles, Dictionary<string, List<Vector2i>> Markers, HashSet<Vector2i> Doors);

    [Test]
    public async Task CityMapsFollowTheMappingRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var files = server.ResolveDependency<IResourceManager>().ContentFindFiles(Root)
            .Where(path => path.Extension == "yml").OrderBy(path => path.ToString()).ToList();
        Assert.That(files, Is.Not.Empty, "No city maps were found.");

        var loaded = new List<Loaded>();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var loader = server.System<MapLoaderSystem>();
            foreach (var path in files)
            {
                var map = maps.CreateMap(out var mapId);
                Assert.That(loader.TryLoadGrid(mapId, path, out var grid), Is.True, $"{path} does not load as a grid.");
                var tiles = new List<Vector2i>();
                var enumerator = maps.GetAllTilesEnumerator(grid!.Value.Owner, grid.Value.Comp);
                while (enumerator.MoveNext(out var tile))
                {
                    tiles.Add(tile.Value.GridIndices);
                }

                var bounds = new Box2i(tiles.Min(t => t.X), tiles.Min(t => t.Y), tiles.Max(t => t.X) + 1, tiles.Max(t => t.Y) + 1);
                var markers = new Dictionary<string, List<Vector2i>>();
                var doors = new HashSet<Vector2i>();
                var children = entMan.GetComponent<TransformComponent>(grid.Value.Owner).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    var at = entMan.GetComponent<TransformComponent>(child).LocalPosition.Floored();
                    if (entMan.HasComponent<DoorComponent>(child))
                        doors.Add(at);
                    if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID is { } id && id.StartsWith("WFCity"))
                    {
                        if (!markers.TryGetValue(id, out var list))
                            markers[id] = list = new List<Vector2i>();
                        list.Add(at);
                    }
                }

                loaded.Add(new Loaded(path, bounds, tiles.Count, markers, doors));
                entMan.DeleteEntity(map);
            }
        });

        Assert.Multiple(() =>
        {
            foreach (var map in loaded.Where(map => map.Path.ToString().Contains("/Sewers/")))
            {
                CheckSewer(map);
            }

            var plots = loaded.Where(map => map.Path.ToString().Contains("/Plots/"))
                .GroupBy(map => Building(map.Path));
            foreach (var building in plots)
            {
                CheckBuilding(building.Key, building.ToDictionary(map => Floor(map.Path)!));
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void CheckSewer(Loaded map)
    {
        var size = map.Bounds.Size;
        Assert.That(size.X % 8 == 0 && size.Y % 8 == 0, Is.True, $"{map.Path} is {size.X}x{size.Y}; sewer pieces come in 8-tile cells.");
        Assert.That(map.Tiles, Is.EqualTo(size.X * size.Y), $"{map.Path} has holes; a sewer piece fills its whole rectangle.");
        var links = Marked(map, "WFCitySewerLink");
        Assert.That(links, Is.Not.Empty, $"{map.Path} has no sewer link markers, so nothing can join it.");
        foreach (var link in links)
        {
            Assert.That(OnEdge(map.Bounds, link), Is.True, $"{map.Path}: the sewer link at {link} is not on the edge.");
        }
    }

    private static void CheckBuilding(string name, Dictionary<string, Loaded> floors)
    {
        foreach (var (floor, map) in floors)
        {
            Assert.That(Floors, Does.Contain(floor), $"{map.Path}: the file name must end in _basement, _ground, _upper1 or _upper2.");
        }

        if (!floors.TryGetValue("ground", out var ground))
        {
            Assert.Fail($"{name} has no _ground floor.");
            return;
        }

        var size = ground.Bounds.Size;
        Assert.That(size.X % 4 == 0 && size.Y % 4 == 0 && size.X >= 8 && size.Y >= 8 && size.X <= 32 && size.Y <= 32, Is.True,
            $"{ground.Path} is {size.X}x{size.Y}; plots are 8 to 32 tiles a side in steps of 4.");
        foreach (var map in floors.Values)
        {
            Assert.That(map.Bounds, Is.EqualTo(ground.Bounds), $"{map.Path} does not line up with its ground floor.");
            Assert.That(map.Tiles, Is.EqualTo(size.X * size.Y), $"{map.Path} has holes; a floor fills its whole rectangle.");
        }

        var front = Marked(ground, "WFCityFrontDoor");
        Assert.That(front, Is.Not.Empty, $"{ground.Path} has no front door marker.");
        foreach (var door in front)
        {
            Assert.That(door.Y, Is.EqualTo(ground.Bounds.Bottom), $"{ground.Path}: the front door at {door} is not on the south edge.");
            Assert.That(ground.Doors, Does.Contain(door), $"{ground.Path}: the front door marker at {door} has no door on it.");
        }

        foreach (var door in Marked(ground, "WFCityBackDoor"))
        {
            Assert.That(door.Y, Is.EqualTo(ground.Bounds.Top - 1), $"{ground.Path}: the back door at {door} is not on the north edge.");
        }

        for (var i = 0; i < Floors.Length; i++)
        {
            if (!floors.TryGetValue(Floors[i], out var map))
                continue;

            floors.TryGetValue(i + 1 < Floors.Length ? Floors[i + 1] : string.Empty, out var above);
            floors.TryGetValue(i > 0 ? Floors[i - 1] : string.Empty, out var below);
            foreach (var stairs in Marked(map, "WFCityStairs"))
            {
                Assert.That(above != null && Marked(above, "WFCityStairwell").Contains(stairs), Is.True,
                    $"{map.Path}: the stairs at {stairs} have no stairwell marker above them.");
            }

            foreach (var well in Marked(map, "WFCityStairwell"))
            {
                Assert.That(below != null && Marked(below, "WFCityStairs").Contains(well), Is.True,
                    $"{map.Path}: the stairwell at {well} has no stairs marker below it.");
            }
        }
    }

    private static List<Vector2i> Marked(Loaded map, string id) => map.Markers.GetValueOrDefault(id) ?? new List<Vector2i>();

    private static bool OnEdge(Box2i bounds, Vector2i tile) =>
        tile.X == bounds.Left || tile.X == bounds.Right - 1 || tile.Y == bounds.Bottom || tile.Y == bounds.Top - 1;

    private static string? Floor(ResPath path)
    {
        var name = path.FilenameWithoutExtension;
        var cut = name.LastIndexOf('_');
        return cut < 0 ? null : name[(cut + 1)..];
    }

    private static string Building(ResPath path)
    {
        var name = path.ToString()[..^".yml".Length];
        var cut = name.LastIndexOf('_');
        return cut < 0 ? name : name[..cut];
    }
}
