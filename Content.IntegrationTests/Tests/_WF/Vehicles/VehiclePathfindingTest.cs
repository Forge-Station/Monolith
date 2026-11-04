#nullable enable
using Content.Server.NPC.Pathfinding;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Mobs path round a vehicle's whole hull, and through where it stood once it drives off.</summary>
[TestFixture]
[TestOf(typeof(PathfindingSystem))]
public sealed class VehiclePathfindingTest
{
    private const float StartX = 5.5f;
    private const float StartY = 5.5f;

    [Test]
    public async Task HullBlocksEveryTileItCovers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        EntityUid tank = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < 12; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            tank = em.SpawnEntity("VehicleSPPTank", new EntityCoordinates(grid, StartX, StartY));
        });
        await server.WaitRunTicks(30);

        var pathfinding = server.System<PathfindingSystem>();
        bool Blocked(float x, float y)
        {
            var poly = pathfinding.GetPoly(new EntityCoordinates(grid, x, y));
            return poly != null && (poly.Data.CollisionLayer & (int) CollisionGroup.MobMask) != 0;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(Blocked(StartX, StartY), Is.True, "The tile under the vehicle's middle is open.");
            Assert.That(Blocked(StartX, StartY + 1f), Is.True, "The tile under the vehicle's front is open.");
            Assert.That(Blocked(StartX, StartY - 1f), Is.True, "The tile under the vehicle's back is open.");
            Assert.That(Blocked(StartX + 3f, StartY), Is.False, "Open floor beside the vehicle is blocked.");
        });

        await server.WaitPost(() =>
            server.System<SharedTransformSystem>().SetCoordinates(tank, new EntityCoordinates(grid, StartX + 16f, StartY)));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            for (var dy = -1f; dy <= 1f; dy++)
                Assert.That(Blocked(StartX, StartY + dy), Is.False, $"The vehicle still blocks where it stood ({dy}).");
            Assert.That(Blocked(StartX + 16f, StartY + 1f), Is.True, "The vehicle's front is open where it stopped.");
        });

        await pair.CleanReturnAsync();
    }
}
