#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared.GameTicking;
using Content.Server.Administration.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The grid mover on Wolfgate's decks: turned grids, throws, blasts, vehicle chains and what it sends to clients.</summary>
[TestFixture]
public sealed class GridVehicleHardeningTest
{
    private const int WallX = 12;

    /// <summary>A wall across the strip at <see cref="WallX"/>, leaving row 2 open when <paramref name="gap"/> is set.</summary>
    private static IEnumerable<Vector2i> WallColumn(bool gap)
    {
        for (var y = 0; y < 6; y++)
        {
            if (gap && y == 2)
                continue;

            yield return new Vector2i(WallX, y);
        }
    }

    /// <summary>
    /// The opening is one tile wide and the chassis is 0.85 wide, so it goes through however the grid is turned: the
    /// boxes are measured in the grid's frame, not as bigger boxes round the turned shapes in the world.
    /// </summary>
    [Test]
    public async Task AVehicleFitsThroughAnOpeningOnATurnedGrid([Values(0, 30, 45, 90, 135)] int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 80, degrees: degrees, walls: WallColumn(gap: true));
        var server = pair.Server;
        var probe = server.System<VehicleEventProbeSystem>();

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(200);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(WallX + 8f), "The chassis did not get through the opening.");
                Assert.That(position.Y, Is.EqualTo(2.5f).Within(0.2f));
                Assert.That(probe.Collisions.Any(c => c.Vehicle == rig.Vehicle), Is.False, "Nothing should have been hit.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The same wall without the opening still stops the chassis, so the test above is not passing for want of a wall.</summary>
    [Test]
    public async Task AWallStopsAVehicleOnATurnedGrid([Values(0, 30, 45, 90, 135)] int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 80, degrees: degrees, walls: WallColumn(gap: false));
        var server = pair.Server;
        var probe = server.System<VehicleEventProbeSystem>();

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(200);

        await server.WaitAssertion(() =>
        {
            var front = VehicleRigs.LocalPosition(rig).X + 1.2f;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(front, Is.LessThan(WallX + 0.1f), "The chassis drove into the wall.");
                Assert.That(front, Is.GreaterThan(WallX - 0.7f), "The chassis stopped well short of the wall.");
                Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.Zero);
                Assert.That(probe.Collisions.Any(c => c.Vehicle == rig.Vehicle && c.Event.Kind == VehicleCollisionKind.Impact && c.Event.Other != null), Is.True,
                    "Running into the wall should raise an impact naming it.");
                Assert.That(probe.Collisions.Any(c => c.Vehicle == rig.Vehicle && c.Event.Kind == VehicleCollisionKind.Crash), Is.True,
                    "The stop should raise a crash.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A throw is over before it starts: the vehicle keeps its place and facing, has no velocity and is not left airborne.</summary>
    [Test]
    public async Task AThrownVehicleIsStopped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        var control = EntityUid.Invalid;
        var controlStart = Vector2.Zero;

        await server.WaitPost(() =>
        {
            start = VehicleRigs.LocalPosition(rig);
            startRotation = entMan.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation;
            control = entMan.SpawnEntity("Crowbar", new EntityCoordinates(rig.Grid, 5.5f, 4.5f));
            controlStart = entMan.GetComponent<TransformComponent>(control).LocalPosition;

            var throwing = server.System<ThrowingSystem>();
            throwing.TryThrow(rig.Vehicle, new Vector2(8f, 0f), baseThrowSpeed: 20f);
            throwing.TryThrow(control, new Vector2(8f, 0f), baseThrowSpeed: 20f);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(rig.Vehicle);
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<ThrownItemComponent>(rig.Vehicle), Is.False, "The vehicle is still counted as thrown.");
                Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.OnGround), "The vehicle was left airborne.");
                Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(body.AngularVelocity, Is.Zero);
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, start), Is.LessThan(0.001f), "The throw moved the vehicle.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.02f), "The throw's spin was kept.");
                Assert.That((entMan.GetComponent<TransformComponent>(control).LocalPosition - controlStart).Length(), Is.GreaterThan(0.5f),
                    "Test setup: a loose item thrown the same way should have moved.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship impact throws everything aboard from inside the physics step, so the bodies are integrated with the velocity
    /// the throw gave them before any mover can clear it. A vehicle parked side-on at a deck edge has its centre less than
    /// a step's travel from the edge, so the shove carries the centre off the deck, or onto the grid next to it. It must
    /// stay a child of its deck, and the next step puts it back where it was with the facing it had.
    /// </summary>
    [Test]
    public async Task AThrowInsideThePhysicsStepCannotCarryAVehicleOffItsDeck([Values(false, true)] bool gridBeyondTheEdge)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 10.5f, startY: 5.575f, facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = server.System<VehicleEventProbeSystem>();

        if (gridBeyondTheEdge)
            await VehicleRigs.AddGrid(rig, new Vector2(0f, 6f), 0f, Enumerable.Range(0, 40).SelectMany(x => Enumerable.Range(0, 6).Select(y => new Vector2i(x, y))));

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;

        await server.WaitPost(() =>
        {
            start = VehicleRigs.LocalPosition(rig);
            startRotation = entMan.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation;
            probe.PendingThrow = (rig.Vehicle, new Vector2(0f, 8f), 20f);
        });

        // The throw lands at the start of a physics step, so the first thing to look at is the state after the next tick.
        for (var i = 0; i < 6; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.GridUid, Is.EqualTo(rig.Grid), $"Tick {i}: the shove took the vehicle off its deck.");
                    Assert.That(VehicleRigs.Mover(rig).SyncedGrid, Is.EqualTo(rig.Grid));
                }
            });
        }

        await server.WaitAssertion(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(rig.Vehicle);
            var xform = entMan.GetComponent<TransformComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(probe.PendingThrow, Is.Null, "Test setup: the throw should have been made.");
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, start), Is.LessThan(0.001f), "The vehicle was not put back where it was.");
                Assert.That(VehicleRigs.Gap(VehicleRigs.Mover(rig).Position, start), Is.LessThan(0.001f), "The mover took on the displaced position.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.001f), "The throw's spin was kept.");
                Assert.That(VehicleRigs.Mover(rig).CurrentDirection, Is.EqualTo(Vector2i.Zero), "Test setup: the vehicle has never been driven.");
                Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.OnGround));
                Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(body.AngularVelocity, Is.Zero);
                Assert.That(entMan.HasComponent<ThrownItemComponent>(rig.Vehicle), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A blast beside a parked vehicle shoves everything loose and leaves the vehicle where it was.</summary>
    [Test]
    public async Task AnExplosionLeavesAParkedVehicleInPlace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 60, startX: 10.5f);
        var server = pair.Server;
        var entMan = server.EntMan;

        // Drive it a little so it has a facing, then park it.
        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(45);
        await server.WaitPost(() =>
        {
            VehicleRigs.Hold(rig, MoveButtons.None);
            entMan.EnsureComponent<WFVehicleEngineComponent>(rig.Vehicle).ParkingBrake = true;
        });
        await server.WaitRunTicks(60);

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        var control = EntityUid.Invalid;
        var controlStart = Vector2.Zero;

        await server.WaitAssertion(() =>
        {
            var vehicles = server.System<RMCVehicleSystem>();
            Assert.That(vehicles.TryRemoveOperator((rig.Vehicle, entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle))), Is.True);
            Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.Zero, "Test setup: the chassis should be at rest.");

            start = VehicleRigs.LocalPosition(rig);
            startRotation = entMan.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation;
            control = entMan.SpawnEntity("Crowbar", new EntityCoordinates(rig.Grid, start.X, start.Y + 2.2f));
            controlStart = entMan.GetComponent<TransformComponent>(control).LocalPosition;

            var epicenter = new MapCoordinates(start + new Vector2(0f, 1.5f), rig.Map.MapId);
            server.System<ExplosionSystem>().QueueExplosion(epicenter, "Default", 20f, 5f, 8f, null);
        });
        await server.WaitRunTicks(40);

        await server.WaitAssertion(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(rig.Vehicle), Is.False);
                Assert.That(VehicleRigs.Gap(VehicleRigs.LocalPosition(rig), start), Is.LessThan(0.001f), "The blast moved the vehicle.");
                Assert.That(entMan.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.001f),
                    "The blast turned the vehicle.");
                Assert.That(entMan.HasComponent<ThrownItemComponent>(rig.Vehicle), Is.False);
                Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(body.AngularVelocity, Is.Zero);
                Assert.That(entMan.Deleted(control) || (entMan.GetComponent<TransformComponent>(control).LocalPosition - controlStart).Length() > 0.5f, Is.True,
                    "Test setup: the blast should have shoved the loose item.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A parked vehicle sends nothing: neither the mover's state nor the transform changes from one tick to the next.</summary>
    [Test]
    public async Task AnIdleVehicleSendsNoState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        GameTick moverTick = default;
        GameTick xformTick = default;

        await server.WaitAssertion(() =>
        {
            moverTick = VehicleRigs.Mover(rig).LastModifiedTick;
            xformTick = entMan.GetComponent<TransformComponent>(rig.Vehicle).LastModifiedTick;
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(VehicleRigs.Mover(rig).LastModifiedTick, Is.EqualTo(moverTick), "The idle mover was marked dirty.");
                Assert.That(entMan.GetComponent<TransformComponent>(rig.Vehicle).LastModifiedTick, Is.EqualTo(xformTick), "The idle transform was marked dirty.");
            }
        });

        // And a driven one does send, or the check above would pass for want of anything to send.
        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.Mover(rig).LastModifiedTick, Is.GreaterThan(moverTick), "A driven mover should be marked dirty.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Vehicles that push at parking speed on a strip one tile wide, so none can steer round another: a lead and two followers
    /// move together, while a chain deeper than the collision buffers is refused instead of running past them. Every
    /// vehicle closes the gap to the next one, then the push fails at the third follower and the lead is stopped.
    /// </summary>
    [TestCase(2, true)]
    [TestCase(4, false)]
    public async Task APushChainIsBoundedByTheBuffers(int followers, bool shouldMove)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 80, minY: 2, maxY: 3, proto: "WFVehicleTestPusher", facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => VehicleRigs.Mover(rig).MaxSpeed = 0.5f);
        var chain = new List<EntityUid>();
        var starts = new List<float>();

        await server.WaitPost(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            for (var i = 0; i < followers; i++)
            {
                var follower = entMan.SpawnEntity("WFVehicleTestPusher", new EntityCoordinates(rig.Grid, 6f + 3f * i, 2.5f));
                xforms.SetLocalRotation(follower, Angle.FromWorldVec(Vector2.UnitX));
                chain.Add(follower);
            }
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            foreach (var follower in chain)
            {
                starts.Add(entMan.GetComponent<TransformComponent>(follower).LocalPosition.X);
            }
        });

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(1800);

        await server.WaitAssertion(() =>
        {
            var lead = VehicleRigs.LocalPosition(rig).X;
            for (var i = 1; i < chain.Count; i++)
            {
                var previous = entMan.GetComponent<TransformComponent>(chain[i - 1]).LocalPosition.X;
                var next = entMan.GetComponent<TransformComponent>(chain[i]).LocalPosition.X;
                Assert.That(next - previous, Is.GreaterThan(2.35f), "Collision momentum overlapped consecutive hulls.");
            }

            if (shouldMove)
            {
                Assert.That(lead, Is.GreaterThan(12f), "The lead vehicle could not push the others.");
                for (var i = 0; i < chain.Count; i++)
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(chain[i]).LocalPosition.X, Is.GreaterThan(starts[i] + 5f), $"Follower {i} was not pushed along.");
                }
            }
            else
            {
                var at = string.Join(", ", chain.Select(f => entMan.GetComponent<TransformComponent>(f).LocalPosition.X.ToString("0.000")));
                Assert.That(lead + 1.2f, Is.LessThan(starts[2] - 1.2f + 0.1f), $"The lead vehicle pushed a chain that is too long. Lead {lead:0.000}, followers {at}.");
                for (var i = 2; i < chain.Count; i++)
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(chain[i]).LocalPosition.X, Is.EqualTo(starts[i]).Within(0.05f), $"Follower {i} moved. Lead {lead:0.000}, followers {at}.");
                }
            }
        });

        // The depth counter must have come back to zero: a plain drive still works afterwards.
        if (!shouldMove)
        {
            await server.WaitPost(() =>
            {
                // Restore the driver after the long run on the airless test strip before checking reversal.
                server.System<RejuvenateSystem>().PerformRejuvenate(rig.Driver);
                VehicleRigs.Hold(rig, MoveButtons.Left);
            });
            var blockedAt = 0f;
            await server.WaitAssertion(() => blockedAt = VehicleRigs.LocalPosition(rig).X);
            await server.WaitRunTicks(60);
            await server.WaitAssertion(() =>
            {
                var mover = VehicleRigs.Mover(rig);
                var driverState = entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(rig.Driver).CurrentState;
                var at = string.Join(", ", chain.Select(f => entMan.GetComponent<TransformComponent>(f).LocalPosition.ToString()));
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.LessThan(blockedAt - 1f),
                    $"The vehicle could not reverse after the refused push. Speed {mover.CurrentSpeed}, heading {mover.Heading}, direction {mover.CurrentDirection}, position {mover.Position}, impact {mover.ImpactVelocity}, driver {driverState}, followers {at}.");
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The smash settings come through one helper; a smashable table is crushed and the chassis drives on.</summary>
    [Test]
    public async Task ASmashableTableIsCrushedThroughTheHelper()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 60);
        var server = pair.Server;
        var entMan = server.EntMan;
        var table = EntityUid.Invalid;
        var wall = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            // A girder, not a table: the smash rules make tables smashable (VehicleCrushingTest), and this one needs a thing no rule matches.
            table = entMan.SpawnEntity("Girder", new EntityCoordinates(rig.Grid, 12.5f, 2.5f));
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(rig.Grid, 12.5f, 5.5f));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var mover = server.System<GridVehicleMoverSystem>();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(mover.TryGetSmashable(table, out _), Is.False, "A plain girder should not be smashable.");
                Assert.That(mover.TryGetSmashable(wall, out _), Is.False);
            }

            var smashable = entMan.AddComponent<VehicleSmashableComponent>(table);
            smashable.SlowdownMultiplier = 0.25f;
            smashable.SlowdownDuration = 1f;

            Assert.That(mover.TryGetSmashable(table, out var settings), Is.True, "The component should be read as smash settings.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings.DeleteOnHit, Is.True);
                Assert.That(settings.SlowdownMultiplier, Is.EqualTo(0.25f));
                Assert.That(settings.SlowdownDuration, Is.EqualTo(1f));
                Assert.That(settings.MinDestroyWeightClass, Is.Null);
            }
        });

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(table), Is.True, "The table was not crushed.");
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(20f), "The chassis did not drive on over the table.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The cooldown kept per run-over mob is dropped when the round restarts, as entity ids are reused. The test raises the
    /// real event, which resets every system that listens for it, so the pair is marked dirty and the next test to borrow
    /// it gets a restarted round rather than whatever this left.
    /// </summary>
    [Test]
    public async Task TheRunOverCooldownsAreClearedOnRoundRestart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var rig = await VehicleRigs.Build(pair, maxX: 60);
        var server = pair.Server;
        var entMan = server.EntMan;
        var victim = EntityUid.Invalid;

        await server.WaitPost(() => victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, 14.5f, 2.5f)));
        await server.WaitRunTicks(5);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(200);

        await server.WaitAssertion(() =>
        {
            var mover = server.System<GridVehicleMoverSystem>();
            Assert.That(mover.TrackedMobCollisions, Is.GreaterThan(0), "Test setup: running the mob over should have started its cooldown.");

            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());

            Assert.That(mover.TrackedMobCollisions, Is.Zero, "The cooldowns survived the restart.");
        });

        await pair.CleanReturnAsync();
    }
    /// <summary>
    /// A vehicle put somewhere else on its own grid, by an admin or a portal, stays where it was put: the mover takes the
    /// place as its own and does not write its old one back. It drives on from there.
    /// </summary>
    [Test]
    public async Task AVehicleMovedWithinItsGridStaysWhereItIsPut([Values(0, 45)] int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 60, degrees: degrees);
        var server = pair.Server;
        var target = new Vector2(30.5f, 4.5f);

        await VehicleRigs.Seat(rig);
        await server.WaitRunTicks(10);

        await server.WaitPost(() =>
        {
            server.System<SharedTransformSystem>().SetLocalPosition(rig.Vehicle, target);
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
            Assert.That(VehicleRigs.Gap(VehicleRigs.LocalPosition(rig), target), Is.LessThan(0.05f), "The mover put the vehicle back where it had been."));

        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X, Is.GreaterThan(target.X + 2f), "The vehicle could not be driven from where it was put.");
                Assert.That(position.Y, Is.EqualTo(target.Y).Within(0.2f));
            }
        });

        await pair.CleanReturnAsync();
    }
}
