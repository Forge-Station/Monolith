#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.IconSmoothing;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>A turned site's tiles, footprint and entrances land where the grid merge puts its tiles.</summary>
[TestFixture]
public sealed class PlanetPoiRotationTest
{
    private const string Station = "/Maps/_WF/PlanetPois/verda_gas_station.yml";
    private const string House = "/Maps/_WF/PlanetPois/verda_house_1.yml";
    private const string Outpost = "/Maps/_WF/PlanetComms/comms_outpost.yml";

    [TestCase(Station, 0)]
    [TestCase(Station, 1)]
    [TestCase(Station, 2)]
    [TestCase(Station, 3)]
    [TestCase(House, 0)]
    [TestCase(House, 1)]
    public async Task ARoadsideSiteTurnsWithItsRoad(string site, int turns)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var entry = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisAsclepiu").Entries.Single(e => e.MapPath == site);
            Assert.That(entry.Rotate, Is.True);
            var footprint = entry.Footprint!.Value;
            var turnedBox = WFPlanetPoiSystem.TurnBox(footprint, turns);

            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var sourceMap, runMapInit: false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(sourceMap, new ResPath(site), out var loaded), Is.True);
            var source = loaded!.Value;
            var sourceTiles = maps.GetAllTiles(source.Owner, source.Comp).ToDictionary(t => t.GridIndices, t => t.Tile.TypeId);

            var target = maps.CreateMap(out _, runMapInit: false);
            var targetGrid = entMan.AddComponent<MapGridComponent>(target);
            server.System<SharedTransformSystem>().SetCoordinates(source.Owner, new EntityCoordinates(target, System.Numerics.Vector2.Zero));
            server.System<GridFixtureSystem>().Merge(target, source.Owner, Vector2i.Zero, Angle.FromDegrees(90 * turns), targetGrid, source.Comp);
            var merged = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(target).ChildEnumerator;
            while (children.MoveNext(out var child))
                merged.Add(child);
            server.System<WFPlanetPoiSystem>().UnturnFixedRotation(merged);

            foreach (var (tile, type) in sourceTiles)
            {
                var turned = WFPlanetPoiSystem.TurnTile(tile, turns);
                Assert.That(maps.GetTileRef(target, targetGrid, turned).Tile.TypeId, Is.EqualTo(type), $"{tile} should land on {turned}.");
                Assert.That(Inside(turnedBox, turned), Is.True, $"{turned} is outside the turned footprint {turnedBox}.");
            }

            foreach (var socket in entry.Sockets)
            {
                var offset = WFPlanetPoiSystem.TurnTile(socket.Offset, turns);
                var direction = WFPlanetPoiSystem.TurnDirection(socket.Direction, turns);
                var step = direction switch
                {
                    WFPlanetPoiDirection.North => new Vector2i(0, 1),
                    WFPlanetPoiDirection.East => new Vector2i(1, 0),
                    WFPlanetPoiDirection.South => new Vector2i(0, -1),
                    _ => new Vector2i(-1, 0),
                };
                Assert.That(Inside(turnedBox, offset) && !Inside(turnedBox, offset + step), Is.True,
                    $"The entrance at {socket.Offset} should still face out of the site after {turns} turns.");
                Assert.That(maps.GetTileRef(target, targetGrid, offset).Tile.IsEmpty, Is.False, "An entrance must stand on the site's road.");
            }

            entMan.DeleteEntity(target);
            maps.DeleteMap(sourceMap);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A turned site's smoothed walls and fences, whose sprites only join square to the grid, end at rotation zero;
    /// everything else that rotates keeps its turn.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task ATurnedSiteKeepsItsSmoothedWallsSquare(int turns)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var poi = server.System<WFPlanetPoiSystem>();
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var sourceMap, runMapInit: false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(sourceMap, new ResPath(Outpost), out var loaded), Is.True);
            var source = loaded!.Value;
            var before = new Dictionary<EntityUid, Angle>();
            var sourceChildren = entMan.GetComponent<TransformComponent>(source.Owner).ChildEnumerator;
            while (sourceChildren.MoveNext(out var child))
                before[child] = entMan.GetComponent<TransformComponent>(child).LocalRotation;

            var target = maps.CreateMap(out _, runMapInit: false);
            var targetGrid = entMan.AddComponent<MapGridComponent>(target);
            server.System<SharedTransformSystem>().SetCoordinates(source.Owner, new EntityCoordinates(target, System.Numerics.Vector2.Zero));
            var turn = Angle.FromDegrees(90 * turns);
            server.System<GridFixtureSystem>().Merge(target, source.Owner, Vector2i.Zero, turn, targetGrid, source.Comp);
            poi.UnturnFixedRotation(before.Keys);

            var square = new HashSet<string>();
            var turned = new HashSet<string>();
            foreach (var (uid, rotation) in before)
            {
                if (entMan.Deleted(uid))
                    continue;
                var xform = entMan.GetComponent<TransformComponent>(uid);
                var id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "";
                Assert.That(xform.ParentUid, Is.EqualTo(target), $"{entMan.ToPrettyString(uid)} should be on the merged grid.");
                if (poi.GridAligned(id))
                {
                    Assert.That(xform.LocalRotation.EqualsApprox(Angle.Zero), $"{entMan.ToPrettyString(uid)} should be square to the grid, not {xform.LocalRotation}.");
                    square.Add(id);
                }
                else if (!xform.NoLocalRotation)
                {
                    Assert.That(xform.LocalRotation.EqualsApprox(rotation + turn), $"{entMan.ToPrettyString(uid)} should keep its turn: {rotation + turn}, not {xform.LocalRotation}.");
                    turned.Add(id);
                }
            }

            Assert.That(square, Does.Contain("ShiptestConcreteReinforced").And.Contain("CMFence"));
            Assert.That(turned, Does.Contain("HighSecDoor").And.Contain("ComputerRadar").And.Contain("ParaChairOfficeDark"));

            entMan.DeleteEntity(target);
            maps.DeleteMap(sourceMap);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The server's reading of which prototypes join square to the grid matches the client's smoothing modes.</summary>
    [Test]
    public async Task GridAlignedMatchesTheClientsSmoothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var server = pair.Server;
        var squareOnClient = new Dictionary<string, bool>();

        await client.WaitAssertion(() =>
        {
            var factory = client.ResolveDependency<IComponentFactory>();
            foreach (var proto in client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                squareOnClient[proto.ID] = proto.TryGetComponent<IconSmoothComponent>(out var smooth, factory)
                                           && smooth.Mode is IconSmoothingMode.CardinalFlags or IconSmoothingMode.Bitmask;
            }
        });

        await server.WaitAssertion(() =>
        {
            var poi = server.System<WFPlanetPoiSystem>();
            var wrong = squareOnClient.Where(entry => poi.GridAligned(entry.Key) != entry.Value).Select(entry => entry.Key).ToList();
            Assert.That(wrong, Is.Empty, "The server misreads these prototypes' smoothing.");
            Assert.That(squareOnClient["ShiptestConcreteReinforced"] && squareOnClient["CMFence"], Is.True);
            Assert.That(squareOnClient["WallConcrete"], Is.False, "Corner-smoothed walls allow for their own rotation.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The site code's containment: the right and top edges are outside.</summary>
    private static bool Inside(Box2i box, Vector2i tile) =>
        tile.X >= box.Left && tile.X < box.Right && tile.Y >= box.Bottom && tile.Y < box.Top;
}
