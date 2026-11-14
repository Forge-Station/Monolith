#nullable enable
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Big xenos fit and walk through a one-tile airlock between two walls.</summary>
[TestFixture]
public sealed class XenoDoorwayTest
{
    private const int WallMask = (int) (CollisionGroup.WallLayer | CollisionGroup.AirlockLayer);

    [Test]
    public async Task WalksThroughOpenAirlock(
        [Values("CMXenoDrone", "CMXenoQueen", "RMCXenoKing", "RMCXenoCrusher", "CMXenoPraetorian", "CMXenoRavager", "CMXenoDefender", "RMCXenoBoiler")] string caste,
        [Values] bool startInDoorway)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid xeno = default, airlock = default;
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = -1; x <= 8; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 1.5f));
            entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, -0.5f));
            airlock = entities.SpawnEntity("Airlock", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            entities.System<SharedDoorSystem>().StartOpening(airlock);
        });
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DoorComponent>(airlock).State, Is.EqualTo(DoorState.Open));

            // inside the doorway, as if shoved into it, or walking in from the left
            xeno = entities.SpawnEntity(caste, new EntityCoordinates(map.Grid, startInDoorway ? 2.5f : 0.5f, 0.5f));
            foreach (var (id, fixture) in entities.GetComponent<FixturesComponent>(xeno).Fixtures)
            {
                if (!fixture.Hard || (fixture.CollisionMask & WallMask) == 0)
                    continue;

                Assert.That(fixture.Shape, Is.TypeOf<PhysShapeCircle>(), $"{caste} {id}");
                Assert.That(((PhysShapeCircle) fixture.Shape).Radius, Is.LessThan(0.49f),
                    $"{caste}'s wall-colliding fixture {id} must fit a one-tile doorway.");
            }
        });
        await pair.RunTicksSync(10);

        if (startInDoorway)
        {
            await server.WaitAssertion(() =>
                Assert.That(entities.System<SharedDoorSystem>().CanClose(airlock), Is.False,
                    $"The airlock must not close on {caste} standing in it."));
        }

        await server.WaitPost(() =>
            entities.GetComponent<InputMoverComponent>(xeno).HeldMoveButtons = MoveButtons.Right);
        await pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            entities.GetComponent<InputMoverComponent>(xeno).HeldMoveButtons = MoveButtons.None;
            var pos = entities.System<SharedTransformSystem>().GetMoverCoordinates(xeno).Position;
            Assert.That(pos.X, Is.GreaterThan(4f), $"{caste} should walk through the doorway (at {pos}).");
            Assert.That(pos.Y, Is.EqualTo(0.5f).Within(0.2f), $"{caste} should not be pushed into the walls (at {pos}).");
        });

        await pair.CleanReturnAsync();
    }
}
