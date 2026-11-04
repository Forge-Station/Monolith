#nullable enable
using System.Numerics;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A crate dragged into a box van lands beside its puller, not on top of them by the door, and can be dragged on.</summary>
[TestFixture]
public sealed class VehicleDraggedCrateTest
{
    [Test]
    public async Task ADraggedCrateFollowsIntoTheVan()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default, user = default, crate = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -10; x < 30; x++)
            for (var y = -10; y < 30; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            vehicle = entMan.SpawnEntity("VehicleBoxVan", new EntityCoordinates(map.Grid.Owner, 10.5f, 10.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var door = new Vector2(10.5f, 10.5f) + entMan.GetComponent<VehicleEnterComponent>(vehicle).EntryPoints[0].Offset;
            user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, door));
            crate = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map.Grid.Owner, door + new Vector2(0.9f, 0f)));
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() => Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True));
        await server.WaitRunTicks(3);
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(user, vehicle));
        await server.WaitRunTicks(200);

        var landed = Vector2.Zero;
        await server.WaitAssertion(() =>
        {
            var interior = entMan.GetComponent<VehicleInteriorComponent>(vehicle).MapId;
            var xforms = server.System<SharedTransformSystem>();
            landed = xforms.GetWorldPosition(crate);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(user).MapID, Is.EqualTo(interior), "The user did not get in.");
                Assert.That(entMan.GetComponent<TransformComponent>(crate).MapID, Is.EqualTo(interior), "The crate was left outside.");
                Assert.That(entMan.GetComponent<PullerComponent>(user).Pulling, Is.EqualTo(crate), "The pull did not survive going in.");
                Assert.That(Vector2.Distance(landed, xforms.GetWorldPosition(user)), Is.GreaterThan(0.6f), "The crate landed on top of its puller.");
            }
        });

        // Walk away from the door; the crate comes along.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(user).HeldMoveButtons = MoveButtons.Down);
        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            entMan.GetComponent<InputMoverComponent>(user).HeldMoveButtons = MoveButtons.None;
            var moved = Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(crate), landed);
            Assert.That(moved, Is.GreaterThan(0.3f), "The crate stuck where it landed.");
        });

        await pair.CleanReturnAsync();
    }
}
