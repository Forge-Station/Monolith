#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A driver steers a bare chassis around a test grid: it moves, stops at a wall and runs over a mob.</summary>
[TestFixture]
public sealed class GridVehicleMoverTest
{
    private const int WallX = 20;
    private const float StartX = 2.5f;
    private const float StartY = 2.5f;

    private sealed class Rig
    {
        public required TestMapData Map;
        public EntityUid Vehicle;
        public EntityUid Driver;
    }

    /// <summary>A strip of plating six tiles wide and, when asked, a solid wall across it at <see cref="WallX"/>.</summary>
    private static async Task<Rig> Build(TestPair pair, bool wall)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map };

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -3; x < 40; x++)
            {
                for (var y = 0; y < 6; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            if (wall)
            {
                for (var y = 0; y < 6; y++)
                {
                    entMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, WallX + 0.5f, y + 0.5f));
                }
            }

            rig.Vehicle = entMan.SpawnEntity("WFVehicleTestChassis", new EntityCoordinates(map.Grid.Owner, StartX, StartY));
            rig.Driver = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -2.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static void Drive(TestPair pair, Rig rig, MoveButtons buttons)
    {
        var entMan = pair.Server.EntMan;
        entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = buttons;
    }

    private static Vector2 LocalPosition(TestPair pair, Rig rig)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition;
    }

    private static GridVehicleMoverComponent Mover(TestPair pair, Rig rig)
    {
        return pair.Server.EntMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle);
    }

    private static async Task Seat(TestPair pair, Rig rig)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var vehicles = pair.Server.System<RMCVehicleSystem>();
            var vehicle = pair.Server.EntMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);

            Assert.That(vehicles.TrySetOperator((rig.Vehicle, vehicle), rig.Driver), Is.True, "The human could not take the wheel.");
        });
    }

    /// <summary>Seating a driver sets the operator and gives both sides the components the mover reads.</summary>
    [Test]
    public async Task SeatingADriverSetsTheOperator()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;
        var entMan = server.EntMan;

        await Seat(pair, rig);
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var vehicle = entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);
            var op = entMan.GetComponent<RMCVehicleOperatorComponent>(rig.Driver);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vehicle.Operator, Is.EqualTo(rig.Driver));
                Assert.That(op.Vehicle, Is.EqualTo(rig.Vehicle));
                Assert.That(entMan.HasComponent<GridVehicleOperatorComponent>(rig.Driver), Is.True);
                Assert.That(entMan.GetComponent<VehicleWatchingComponent>(rig.Driver).Watching, Is.EqualTo(rig.Vehicle));
                Assert.That(entMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle).SyncedGrid, Is.EqualTo(rig.Map.Grid.Owner),
                    "The mover did not pick up the grid it was spawned on.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With the driver holding a direction the chassis turns to face it, speeds up and moves along the grid.</summary>
    [Test]
    public async Task HoldingAMoveButtonDrivesTheChassis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;

        await Seat(pair, rig);
        var start = LocalPosition(pair, rig);

        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var position = LocalPosition(pair, rig);
            var mover = Mover(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(start.X + 3f), "The chassis barely moved with the button held.");
                Assert.That(position.Y, Is.EqualTo(start.Y).Within(0.2f), "It should drive straight.");
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(1, 0)), "It did not turn to face the pressed direction.");
                Assert.That(mover.CurrentSpeed, Is.GreaterThan(0f));
                Assert.That(mover.CurrentSpeed, Is.LessThanOrEqualTo(mover.MaxSpeed));
                Assert.That(mover.Position.X, Is.EqualTo(position.X).Within(0.01f), "The mover and the transform disagree.");
                Assert.That(mover.IsMoving, Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Letting go coasts to a halt, and reversing backs the chassis up.</summary>
    [Test]
    public async Task ReleasingTheButtonStopsAndReversingBacksUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;

        await Seat(pair, rig);
        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(90);

        await server.WaitPost(() => Drive(pair, rig, MoveButtons.None));
        await server.WaitRunTicks(180);

        Vector2 stopped = default;
        await server.WaitAssertion(() =>
        {
            stopped = LocalPosition(pair, rig);
            Assert.That(Mover(pair, rig).CurrentSpeed, Is.Zero, "It never came to rest.");
            Assert.That(Mover(pair, rig).IsMoving, Is.False);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(LocalPosition(pair, rig), Is.EqualTo(stopped), "A stopped chassis drifted.");
        });

        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Left));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var mover = Mover(pair, rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LocalPosition(pair, rig).X, Is.LessThan(stopped.X - 0.5f), "It did not back up.");
                Assert.That(mover.CurrentSpeed, Is.LessThan(0f), "Reversing should give a negative speed.");
                Assert.That(mover.CurrentDirection, Is.EqualTo(new Vector2i(1, 0)), "Reversing keeps the facing.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A wall across the strip stops the chassis short of it and keeps it there however long the button is held.</summary>
    [Test]
    public async Task AWallStopsTheChassis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: true);
        var server = pair.Server;

        await Seat(pair, rig);
        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(400);

        Vector2 stopped = default;
        await server.WaitAssertion(() =>
        {
            stopped = LocalPosition(pair, rig);
            var frontEdge = stopped.X + 1.2f;
            var mover = Mover(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(frontEdge, Is.LessThan(WallX + 0.1f), "The chassis drove into the wall.");
                Assert.That(frontEdge, Is.GreaterThan(WallX - 0.6f), "The chassis stopped well before the wall.");
                Assert.That(mover.CurrentSpeed, Is.Zero, "A blocked chassis should have no speed.");
                Assert.That(stopped.Y, Is.EqualTo(StartY).Within(0.2f));
            }
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var position = LocalPosition(pair, rig);
            Assert.That(position.X, Is.EqualTo(stopped.X).Within(0.05f), "It crept into the wall while the button was held.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driving a standing human at speed hurts and knocks it down, and the chassis carries on over it.</summary>
    [Test]
    public async Task ADrivenChassisRunsOverAMob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;
        var entMan = server.EntMan;
        var victim = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, 14.5f, StartY));
        });
        await server.WaitRunTicks(5);

        await Seat(pair, rig);
        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<DamageableComponent>(victim).TotalDamage;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(LocalPosition(pair, rig).X, Is.GreaterThan(18f), "The chassis was stopped by a mob; mobs never block a vehicle.");
                Assert.That((float) damage, Is.GreaterThan(0f), "The mob took no damage from being run over.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Taking the driver out stops input: the chassis coasts to rest and the driver loses the components.</summary>
    [Test]
    public async Task RemovingTheOperatorStopsTheChassis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;
        var entMan = server.EntMan;

        await Seat(pair, rig);
        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var vehicles = server.System<RMCVehicleSystem>();
            var vehicle = entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);
            Assert.That(vehicles.TryRemoveOperator((rig.Vehicle, vehicle)), Is.True);
        });
        await server.WaitRunTicks(180);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.Null);
                Assert.That(entMan.HasComponent<RMCVehicleOperatorComponent>(rig.Driver), Is.False);
                Assert.That(entMan.HasComponent<GridVehicleOperatorComponent>(rig.Driver), Is.False);
                Assert.That(entMan.HasComponent<VehicleWatchingComponent>(rig.Driver), Is.False);
                Assert.That(Mover(pair, rig).CurrentSpeed, Is.Zero, "It kept rolling with nobody at the wheel.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>What takes a driver's hands off the wheel.</summary>
    public enum Incapacity
    {
        Dead,
        Stunned,
    }

    /// <summary>
    /// A driver who has died or been stunned steers nothing, however long they keep a key
    /// held: the chassis coasts to rest. Brought round again, they have the wheel again.
    /// </summary>
    [Test]
    public async Task AnIncapacitatedDriverSteersNothing([Values] Incapacity incapacity)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, wall: false);
        var server = pair.Server;
        var entMan = server.EntMan;

        await Seat(pair, rig);
        await server.WaitPost(() => Drive(pair, rig, MoveButtons.Right));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(Mover(pair, rig).CurrentSpeed, Is.GreaterThan(1f), "Test setup: the chassis should be moving.");

            switch (incapacity)
            {
                case Incapacity.Dead:
                    server.System<MobStateSystem>().ChangeMobState(rig.Driver, MobState.Dead);
                    break;
                case Incapacity.Stunned:
                    server.System<SharedStunSystem>().TryStun(rig.Driver, TimeSpan.FromSeconds(60), refresh: true);
                    break;
            }
        });
        await server.WaitRunTicks(180);

        var stoppedAt = 0f;
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons, Is.EqualTo(MoveButtons.Right), "Test setup: the driver should still hold the key.");
                Assert.That(entMan.GetComponent<MobStateComponent>(rig.Driver).CurrentState,
                    Is.EqualTo(incapacity == Incapacity.Dead ? MobState.Dead : MobState.Alive),
                    "Test setup: the driver should still be in the state the case names.");
                Assert.That(Mover(pair, rig).CurrentSpeed, Is.Zero, "An incapacitated driver kept the chassis going.");
            }

            stoppedAt = LocalPosition(pair, rig).X;
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(LocalPosition(pair, rig).X, Is.EqualTo(stoppedAt).Within(0.01f), "An incapacitated driver drove the chassis on.");

            switch (incapacity)
            {
                case Incapacity.Stunned:
                    entMan.RemoveComponent<StunnedComponent>(rig.Driver);
                    break;
                default:
                    server.System<MobStateSystem>().ChangeMobState(rig.Driver, MobState.Alive);
                    break;
            }
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
            Assert.That(LocalPosition(pair, rig).X, Is.GreaterThan(stoppedAt + 1f), "A driver brought round did not get the wheel back."));

        await pair.CleanReturnAsync();
    }
}
