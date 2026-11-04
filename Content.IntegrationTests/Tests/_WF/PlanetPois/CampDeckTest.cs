#nullable enable
using Content.Server._WF.PlanetPois;
using Content.Shared.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Guards and wildlife are only put on deck no solid thing reaches over, such as the far end of a ship gun.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class CampDeckTest
{
    private static readonly string[] Hulls =
    {
        "/SharedMaps/_WF/AdsIncursion/wyrm.yml",
        "/SharedMaps/_WF/AdsIncursion/nebula.yml",
    };

    [Test]
    public async Task OpenDeckIsClearOfSolidThings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var loader = server.System<MapLoaderSystem>();
            var maps = server.System<SharedMapSystem>();
            var poi = server.System<WFPlanetPoiSystem>();
            foreach (var path in Hulls)
            {
                Assert.That(loader.TryLoadGrid(map.MapId, new ResPath(path), out var loaded), Is.True, path);
                var ship = loaded!.Value;
                var solid = poi.SolidTiles((ship.Owner, ship.Comp));

                var children = em.GetComponent<TransformComponent>(ship.Owner).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    var xform = em.GetComponent<TransformComponent>(child);
                    if (!xform.Anchored || !em.TryGetComponent<FixturesComponent>(child, out var fixtures))
                        continue;

                    var pose = new Robust.Shared.Physics.Transform(xform.LocalPosition, xform.LocalRotation);
                    foreach (var fixture in fixtures.Fixtures.Values)
                    {
                        if (!fixture.Hard || (fixture.CollisionLayer & (int) CollisionGroup.MidImpassable) == 0)
                            continue;

                        var box = fixture.Shape.ComputeAABB(pose, 0);
                        foreach (var tile in maps.GetAllTiles(ship.Owner, ship.Comp))
                        {
                            // The middle of a tile, where a guard stands.
                            var middle = Box2.CenteredAround(tile.GridIndices + new System.Numerics.Vector2(0.5f), new System.Numerics.Vector2(0.4f));
                            if (box.Intersects(middle))
                                Assert.That(solid, Does.Contain(tile.GridIndices),
                                    $"{em.ToPrettyString(child)} reaches over {tile.GridIndices} on {path}, which counts as open deck.");
                        }
                    }
                }

                em.DeleteEntity(ship.Owner);
            }
        });

        await pair.CleanReturnAsync();
    }
}
