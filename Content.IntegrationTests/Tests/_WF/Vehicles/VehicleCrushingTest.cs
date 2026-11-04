#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Access.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// A vehicle is driven at what the smash rules cover: what it does to each, by weight class. The rules are data, so each
/// case uses a real Wolfgate prototype, and a wall is there to show that nothing makes a wall give.
/// </summary>
[TestFixture]
public sealed class VehicleCrushingTest
{
    private const float TargetX = 12.5f;
    private const float TargetY = 2.5f;

    /// <summary>Long enough that a vehicle driven for the length of a test never reaches the end of the floor, where it would crash.</summary>
    private const int StripLength = 100;

    /// <summary>What the vehicle does at the target.</summary>
    public enum Meets
    {
        /// <summary>Drives on without being stopped.</summary>
        Passes,

        /// <summary>Smashes it, stops, and drives on once it is gone.</summary>
        Stops,

        /// <summary>Is stopped and cannot get past it.</summary>
        Blocked,
    }

    /// <summary>What is left of the target.</summary>
    public enum Left
    {
        Gone,

        /// <summary>Gone, or standing and damaged.</summary>
        Hurt,

        Intact,
    }

    private const bool Powered = true;
    private const bool Unpowered = false;

    [TestCase("Table", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Table", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    // Small things never stop a vehicle (owner, 2026-10-17).
    [TestCase("ClosetEmergency", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("ClosetEmergency", VehicleWeightClass.Light, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("ClosetEmergency", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("ClosetEmergency", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("CrateGeneric", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("VendingMachineCola", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("VendingMachineCola", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    // Economy fixtures are no hull and no vehicle breaks them, a tank or a heavy vehicle included: an access-locked locker,
    // an evidence locker, a secure crate and the vendors of a trade station and of the traders stop the vehicle and are
    // left undamaged. (Owner decision of 2026-10-11.)
    [TestCase("LockerSteel", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("LockerSteel", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("LockerEvidence", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("CrateSecgear", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("CrateSecgear", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("WFTraderStockGeneral", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("WFTraderStockGeneral", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("VendingMachineTraderTradeMall", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("Barricade", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Barricade", VehicleWeightClass.Light, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Barricade", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("FenceMetalStraight", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("FenceMetalStraight", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("CrocusStump", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("N14BarricadeSandbagSingle", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("N14BarricadeSandbagSingle", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("N14BarricadeTanktrap", VehicleWeightClass.Medium, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("N14ShelfWood", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Window", VehicleWeightClass.Weak, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Window", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Airlock", VehicleWeightClass.Weak, Unpowered, Meets.Stops, Left.Gone)]
    [TestCase("Airlock", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Windoor", VehicleWeightClass.Light, Unpowered, Meets.Stops, Left.Gone)]
    [TestCase("Windoor", VehicleWeightClass.Heavy, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("Airlock", VehicleWeightClass.Weak, Powered, Meets.Passes, Left.Intact)]
    [TestCase("Airlock", VehicleWeightClass.Heavy, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockEngineeringLocked", VehicleWeightClass.Weak, Powered, Meets.Blocked, Left.Intact)]
    [TestCase("AirlockEngineeringLocked", VehicleWeightClass.Heavy, Powered, Meets.Blocked, Left.Intact)]
    // Ship hull is broken by tanks only (Medium): a lighter vehicle is stopped by it and leaves it undamaged, a tank breaks
    // it. A hull door still opens for a driver with access, whatever the vehicle, and holds a tank when it is powered and
    // the driver has no access.
    [TestCase("ReinforcedWindow", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("ReinforcedWindow", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("ReinforcedWindow", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("ReinforcedPlasmaWindow", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("ReinforcedPlasmaWindow", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("ShuttleWindow", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("ShuttleWindow", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("PlastitaniumWindow", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("PlastitaniumWindow", VehicleWeightClass.Medium, Unpowered, Meets.Passes, Left.Gone)]
    [TestCase("AirlockExternal", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("AirlockExternal", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("AirlockExternal", VehicleWeightClass.Medium, Unpowered, Meets.Stops, Left.Gone)]
    [TestCase("AirlockShuttle", VehicleWeightClass.Light, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("AirlockShuttle", VehicleWeightClass.Medium, Unpowered, Meets.Stops, Left.Gone)]
    [TestCase("AirlockExternal", VehicleWeightClass.Weak, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockExternal", VehicleWeightClass.Medium, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockShuttle", VehicleWeightClass.Weak, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockShuttle", VehicleWeightClass.Heavy, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockExternalShuttleLocked", VehicleWeightClass.Weak, Powered, Meets.Blocked, Left.Intact)]
    [TestCase("AirlockExternalShuttleLocked", VehicleWeightClass.Medium, Powered, Meets.Blocked, Left.Intact)]
    // A door no rule covers (a heavy glass airlock) still opens for a driver with access, and is a wall when it will not.
    [TestCase("AirlockHeavyGlass", VehicleWeightClass.Weak, Powered, Meets.Passes, Left.Intact)]
    [TestCase("AirlockHeavyGlass", VehicleWeightClass.Medium, Unpowered, Meets.Blocked, Left.Intact)]
    // A wall is not hull: nothing breaks it, a tank included.
    [TestCase("WallSolid", VehicleWeightClass.Medium, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("WallSolid", VehicleWeightClass.Weak, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("WallSolid", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    [TestCase("WallReinforced", VehicleWeightClass.Heavy, Unpowered, Meets.Blocked, Left.Intact)]
    public async Task AVehicleMeetsWhatTheRulesSay(string prototype, VehicleWeightClass weight, bool powered, Meets meets, Left left)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = server.System<VehicleEventProbeSystem>();
        var column = new List<EntityUid>();

        // One across the whole width of the floor: a vehicle that is held steers round a single target.
        await server.WaitPost(() =>
        {
            for (var y = 0; y < 6; y++)
            {
                var target = entMan.SpawnEntity(prototype, new EntityCoordinates(rig.Grid, TargetX, y + 0.5f));
                column.Add(target);

                // A locker or crate is a loose body until it is anchored, and a vehicle shoves what it can neither smash nor
                // drive through; a map anchors them, and so does this.
                if (entMan.TryGetComponent(target, out PhysicsComponent? body) && body.BodyType == BodyType.Dynamic)
                {
                    server.System<SharedPhysicsSystem>().SetBodyType(target, BodyType.Static, body: body);
                    server.System<SharedTransformSystem>().AnchorEntity(target);
                }

                if (powered)
                    server.System<SharedPowerReceiverSystem>().SetNeedsPower(target, false);
            }

            VehicleRigs.Mover(rig).WeightClass = weight;
        });

        // The doors learn that they are powered a tick or two after the switch, and not all in the same one.
        await server.WaitRunTicks(powered ? 40 : 10);

        if (powered)
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(column.All(t => entMan.GetComponent<AirlockComponent>(t).Powered), Is.True, "Test setup: the doors should be powered.");
            });
        }

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var target = column[2];
            var x = VehicleRigs.LocalPosition(rig).X;
            var mine = probe.Collisions.Where(c => c.Vehicle == rig.Vehicle).ToList();

            bool Gone(EntityUid uid) => entMan.Deleted(uid) || entMan.IsQueuedForDeletion(uid);
            double Damage(EntityUid uid) => !Gone(uid) && entMan.TryGetComponent(uid, out DamageableComponent? damageable)
                ? damageable.TotalDamage.Double()
                : 0;

            TestContext.Out.WriteLine($"{prototype} {weight}: vehicle at {x}, gone {Gone(target)}, damage {Damage(target)}, events " +
                                      string.Join(", ", mine.Select(c => $"{c.Event.Kind}->{c.Event.Other}")));

            using (Assert.EnterMultipleScope())
            {
                switch (left)
                {
                    case Left.Gone:
                        Assert.That(Gone(target), Is.True, $"{prototype} should have been destroyed.");
                        break;
                    case Left.Hurt:
                        Assert.That(Gone(target) || Damage(target) > 0, Is.True, $"{prototype} should have been damaged.");
                        break;
                    case Left.Intact:
                        Assert.That(column.Any(Gone), Is.False, $"{prototype} should be standing.");
                        Assert.That(column.Sum(Damage), Is.Zero, $"{prototype} should be undamaged.");
                        break;
                }

                switch (meets)
                {
                    case Meets.Passes:
                        Assert.That(x, Is.GreaterThan(TargetX + 2f), "The vehicle should have driven on past it.");
                        Assert.That(mine, Is.Empty, "The vehicle should not have been stopped by it.");
                        break;
                    case Meets.Stops:
                        Assert.That(x, Is.GreaterThan(TargetX + 2f), "The vehicle should have driven on once it was gone.");
                        Assert.That(mine.Any(c => c.Event.Kind == VehicleCollisionKind.Crash), Is.True, "The vehicle should have been stopped by it.");
                        break;
                    case Meets.Blocked:
                        Assert.That(x + 1.2f, Is.LessThan(TargetX), "The vehicle should have been held in front of it.");
                        Assert.That(x + 1.2f, Is.GreaterThan(TargetX - 1.6f), "The vehicle should have driven up to it.");
                        Assert.That(mine.Any(c => c.Event.Kind == VehicleCollisionKind.Impact && c.Event.Other is { } other && column.Contains(other)), Is.True,
                            "The impact should name what stopped the vehicle.");
                        break;
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Puts a powered door of the given prototype across the whole width of the floor, starting at <paramref name="x"/>.</summary>
    private static async Task<List<EntityUid>> PoweredColumn(VehicleRig rig, string prototype, float x)
    {
        var server = rig.Pair.Server;
        var column = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var y = 0; y < 6; y++)
            {
                var door = server.EntMan.SpawnEntity(prototype, new EntityCoordinates(rig.Grid, x, y + 0.5f));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(door, false);
                column.Add(door);
            }
        });
        await server.WaitRunTicks(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(column.All(t => server.EntMan.GetComponent<AirlockComponent>(t).Powered), Is.True, "Test setup: the doors should be powered.");
        });

        return column;
    }

    /// <summary>
    /// A vehicle with nobody at the wheel opens nothing. It coasts on after the driver leaves, and the door system lets a
    /// missing user through any door, so a door must still be a wall to it, locked or not.
    /// </summary>
    [TestCase("Airlock")]
    [TestCase("AirlockEngineeringLocked")]
    [TestCase("AirlockShuttle")]
    [TestCase("AirlockExternalShuttleLocked")]
    public async Task AVehicleWithNoDriverOpensNoDoor(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = server.System<VehicleEventProbeSystem>();
        const float doorX = 4.5f;

        // The doors stand a third of a tile ahead of the hull, which has not yet moved.
        var column = await PoweredColumn(rig, prototype, doorX);

        // A moment at the wheel gives it a facing; then the driver is gone and it is rolling at speed.
        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            VehicleRigs.Hold(rig, MoveButtons.None);
            var vehicles = server.System<RMCVehicleSystem>();
            Assert.That(vehicles.TryRemoveOperator((rig.Vehicle, entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle))), Is.True);
            VehicleRigs.Mover(rig).CurrentSpeed = 6f;
        });
        await server.WaitRunTicks(200);

        await server.WaitAssertion(() =>
        {
            var x = VehicleRigs.LocalPosition(rig).X;
            TestContext.Out.WriteLine($"{prototype}: vehicle at {x}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(column.Any(d => entMan.GetComponent<DoorComponent>(d).State != DoorState.Closed), Is.False, "A driverless vehicle opened a door.");
                Assert.That(x + 1.2f, Is.LessThanOrEqualTo(doorX), "A driverless vehicle went through a door.");
                Assert.That(column.Any(d => entMan.Deleted(d)), Is.False, "A powered door was smashed.");
                Assert.That(probe.Collisions.Any(c => c.Vehicle == rig.Vehicle && c.Event.Other is { } other && column.Contains(other)), Is.True,
                    "Test setup: the vehicle should have run into the door and been stopped by it.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The door's reader is asked once for a pass through it, not once for every probe of every step: each yes goes in the
    /// door's access log, which would otherwise hold nothing but the vehicle's driver.
    /// </summary>
    [Test]
    public async Task DrivingThroughADoorLogsItsAccessOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;

        var column = await PoweredColumn(rig, "Airlock", TargetX);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var logged = column.Select(d => entMan.GetComponent<AccessReaderComponent>(d).AccessLog.Count).ToList();
            TestContext.Out.WriteLine($"Access log entries per door: {string.Join(", ", logged)}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(TargetX + 2f), "The vehicle should have driven through the doors.");
                Assert.That(logged.Max(), Is.LessThanOrEqualTo(3), "A door was asked for access over and over by one vehicle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The grille a hull window is set on is as safe from a vehicle too light to break the window as the window is: such a
    /// vehicle is held in front of both and breaks neither, whichever of the two the probe meets first, and a tank breaks
    /// both and drives on.
    /// </summary>
    [TestCase(VehicleWeightClass.Weak, false, false)]
    [TestCase(VehicleWeightClass.Weak, true, false)]
    [TestCase(VehicleWeightClass.Light, false, false)]
    [TestCase(VehicleWeightClass.Light, true, false)]
    [TestCase(VehicleWeightClass.Medium, false, true)]
    [TestCase(VehicleWeightClass.Medium, true, true)]
    [TestCase(VehicleWeightClass.Heavy, true, true)]
    public async Task AGrilleUnderAHullWindowIsAsSafeAsTheWindow(VehicleWeightClass weight, bool grilleFirst, bool breaksThrough)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;
        var windows = new List<EntityUid>();
        var grilles = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var y = 0; y < 6; y++)
            {
                var at = new EntityCoordinates(rig.Grid, TargetX, y + 0.5f);

                if (grilleFirst)
                    grilles.Add(entMan.SpawnEntity("Grille", at));

                windows.Add(entMan.SpawnEntity("ReinforcedWindow", at));

                if (!grilleFirst)
                    grilles.Add(entMan.SpawnEntity("Grille", at));
            }

            VehicleRigs.Mover(rig).WeightClass = weight;
        });
        await server.WaitRunTicks(10);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var x = VehicleRigs.LocalPosition(rig).X;

            bool Gone(EntityUid uid) => entMan.Deleted(uid) || entMan.IsQueuedForDeletion(uid);
            double Damage(EntityUid uid) => !Gone(uid) && entMan.TryGetComponent(uid, out DamageableComponent? damageable)
                ? damageable.TotalDamage.Double()
                : 0;

            using (Assert.EnterMultipleScope())
            {
                if (breaksThrough)
                {
                    Assert.That(Gone(windows[2]), Is.True, "A tank should have broken the window in its way.");
                    Assert.That(Gone(grilles[2]), Is.True, "A tank should have broken the grille under it.");
                    Assert.That(x, Is.GreaterThan(TargetX + 2f), "The tank should have driven on through.");
                }
                else
                {
                    Assert.That(windows.Any(Gone), Is.False, "A vehicle too light broke a hull window.");
                    Assert.That(grilles.Any(Gone), Is.False, "A vehicle too light broke the grille a hull window is set on.");
                    Assert.That(windows.Concat(grilles).Sum(Damage), Is.Zero, "A vehicle too light damaged a hull window or its grille.");
                    Assert.That(x + 1.2f, Is.LessThan(TargetX), "The vehicle should have been held in front of the window.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A light vehicle slows down on a fence and a heavy one does not.</summary>
    [TestCase(VehicleWeightClass.Weak, true)]
    [TestCase(VehicleWeightClass.Light, true)]
    [TestCase(VehicleWeightClass.Medium, false)]
    [TestCase(VehicleWeightClass.Heavy, false)]
    public async Task AFenceSlowsOnlyLightVehicles(VehicleWeightClass weight, bool slowed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("FenceMetalStraight", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            VehicleRigs.Mover(rig).WeightClass = weight;
        });
        await server.WaitRunTicks(10);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));

        // The slowdown lasts 0.4 s from the hit, so it is seen while the vehicle is still at the fence.
        var seen = false;
        for (var i = 0; i < 300 && !seen; i++)
        {
            await server.WaitRunTicks(1);
            seen = VehicleRigs.Mover(rig).SmashSlowdownMultiplier < 1f;
        }

        Assert.That(seen, Is.EqualTo(slowed), slowed ? "A fence should slow a vehicle of this weight." : "A fence should not slow a vehicle of this weight.");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A vehicle loses speed to a fence once, when the slowdown begins, and is then held to the slower top speed for its
    /// duration. It overlaps the fence for many steps, and cutting its speed at every one of them would stop it dead.
    /// </summary>
    [Test]
    public async Task AFenceCostsALightVehicleItsSpeedOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("FenceMetalStraight", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            VehicleRigs.Mover(rig).WeightClass = VehicleWeightClass.Light;
        });
        await server.WaitRunTicks(10);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));

        // From the tick of the hit, a quarter of a second: well inside the 0.4 s of the slowdown.
        var speedBefore = 0f;
        var slowest = float.MaxValue;
        var hitAt = -1;
        for (var i = 0; i < 400 && (hitAt < 0 || i < hitAt + 15); i++)
        {
            var speed = 0f;
            var slowed = false;
            await server.WaitPost(() =>
            {
                speed = VehicleRigs.Mover(rig).CurrentSpeed;
                slowed = VehicleRigs.Mover(rig).SmashSlowdownMultiplier < 1f;
            });

            if (hitAt < 0 && slowed)
                hitAt = i;
            else if (hitAt < 0)
                speedBefore = speed;

            if (hitAt >= 0)
                slowest = Math.Min(slowest, speed);

            await server.WaitRunTicks(1);
        }

        Assert.That(hitAt, Is.GreaterThanOrEqualTo(0), "The vehicle was never slowed by the fence.");
        Assert.That(speedBefore, Is.GreaterThan(4f), "Test setup: the vehicle should reach the fence at speed.");
        Assert.That(slowest, Is.GreaterThan(speedBefore * 0.4f), "The fence took the vehicle's speed again at every step it overlapped it.");

        await pair.CleanReturnAsync();
    }

    /// <summary>A table is destroyed through its own thresholds, so it leaves what a broken table leaves.</summary>
    [Test]
    public async Task ASmashedTableLeavesItsLeftovers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.SpawnEntity("Table", new EntityCoordinates(rig.Grid, TargetX, TargetY)));
        await server.WaitRunTicks(10);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            // The vehicle shoves loose things ahead of it, so the sheet is anywhere along the floor.
            var map = entMan.GetComponent<TransformComponent>(rig.Grid).MapID;
            var sheets = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == "SheetSteel1" && xform.MapID == map)
                    sheets.Add(uid);
            }

            Assert.That(sheets, Has.Count.EqualTo(1), "A smashed table should leave one sheet of steel, as a table broken any other way does.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A locker with things inside is destroyed and they spill; nothing in it is deleted with it.</summary>
    [Test]
    public async Task ASmashedLockerSpillsWhatIsInsideIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;
        var locker = EntityUid.Invalid;
        var crowbar = EntityUid.Invalid;
        var toolbox = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            locker = entMan.SpawnEntity("ClosetEmergency", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            crowbar = entMan.SpawnEntity("Crowbar", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            toolbox = entMan.SpawnEntity("ToolboxEmergency", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            VehicleRigs.Mover(rig).WeightClass = VehicleWeightClass.Light;

            var storage = server.System<SharedEntityStorageSystem>();
            Assert.That(storage.Insert(crowbar, locker), Is.True);
            Assert.That(storage.Insert(toolbox, locker), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(crowbar).ParentUid, Is.EqualTo(locker), "Test setup: the crowbar should be in the locker.");
        });

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(locker), Is.True, "The locker should have been destroyed.");
                Assert.That(entMan.Deleted(crowbar), Is.False, "The crowbar was deleted with the locker.");
                Assert.That(entMan.Deleted(toolbox), Is.False, "The toolbox was deleted with the locker.");
            }

            var containers = server.System<SharedContainerSystem>();
            Assert.That(containers.IsEntityInContainer(crowbar), Is.False, "The crowbar should lie on the floor, not still be in the wreck.");
            Assert.That(containers.IsEntityInContainer(toolbox), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob in the way is run over as in RMC: hurt, knocked down, and the vehicle goes on.</summary>
    [TestCase(VehicleWeightClass.Weak)]
    [TestCase(VehicleWeightClass.Heavy)]
    public async Task AMobInThePathIsRunOver(VehicleWeightClass weight)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: StripLength);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        // The pair is pooled and the mover keeps an entry for every mob it has run over until the round restarts, so the
        // count says something only as a change from before this test's mob was in the way.
        var trackedBefore = 0;
        await server.WaitPost(() =>
        {
            trackedBefore = server.System<GridVehicleMoverSystem>().TrackedMobCollisions;
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, TargetX, TargetY));
            VehicleRigs.Mover(rig).WeightClass = weight;
        });
        await server.WaitRunTicks(10);

        await VehicleRigs.Seat(rig);
        await server.WaitPost(() => VehicleRigs.Hold(rig, MoveButtons.Right));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var mover = server.System<GridVehicleMoverSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mover.TrackedMobCollisions, Is.EqualTo(trackedBefore + 1), "The mob should have been run over, once.");
                Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage.Double(), Is.GreaterThan(0), "The mob should be hurt.");
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(TargetX + 2f), "A mob does not stop the vehicle.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
