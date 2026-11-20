#nullable enable
using System.Linq;
using Content.Server._WF.ShipAccess;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>A ship that breaks apart keeps its helm lock state on every piece.</summary>
[TestFixture]
[TestOf(typeof(WFShipGridLockSplitSystem))]
public sealed class ShipGridLockSplitTest
{
    [Test]
    public async Task AnUnlockedShipStaysUnlockedInPieces()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        EntityUid ship = default;
        await server.WaitPost(() =>
        {
            ship = maps.CreateGridEntity(map.MapId).Owner;
            var grid = entMan.GetComponent<MapGridComponent>(ship);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            // Long enough that each piece outlasts the battle-load cleanup of tiny empty fragments.
            for (var x = 0; x < 11; x++)
                maps.SetTile(ship, grid, new Vector2i(x, 0), new Tile(plating));
            var source = new ShipGridLockComponent();
#pragma warning disable RA0002 // The test sets the ship's lock state directly.
            source.Locked = false;
            source.ShuttleId = ship.ToString();
#pragma warning restore RA0002
            entMan.AddComponent(ship, source);

            // Breaking the middle tile splits the ship in two.
            maps.SetTile(ship, grid, new Vector2i(5, 0), Tile.Empty);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var pieces = entMan.EntityQuery<MapGridComponent>()
                .Select(grid => grid.Owner)
                .Where(grid => entMan.GetComponent<TransformComponent>(grid).MapID == map.MapId && grid != map.Grid.Owner)
                .ToList();
            Assert.That(pieces, Has.Count.EqualTo(2), "The ship did not split.");
            foreach (var piece in pieces)
            {
                Assert.That(entMan.TryGetComponent<ShipGridLockComponent>(piece, out var lockComp), Is.True, "A piece lost its helm lock.");
                Assert.That(lockComp!.Locked, Is.False, "A piece of an unlocked ship came out locked.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
