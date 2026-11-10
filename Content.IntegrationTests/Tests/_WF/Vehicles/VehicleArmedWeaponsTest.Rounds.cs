#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.Vehicles.Turret;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Where the rounds of a fixed-distance gun come to rest against every kind of obstacle, with the grids turned any way, moving,
/// stacked over one another, and with aims that are not real points.
/// </summary>
public sealed partial class VehicleArmedWeaponsTest
{
    private enum Obstacle
    {
        Wall,
        Door,
        Window,
        DiagonalWall,
        Shutter,
        BlastDoor,
        Firelock,
        DirectionalWindow,
        Windoor,
        OtherVehicle,
        OtherGridWall,
        OtherGridWindoor,
        ShipOverFloorWall,
    }

    /// <summary>Things a round must fly past or through: it reaches the aimed point.</summary>
    private enum PassThrough
    {
        OpenDoor,
        Mob,
        LooseItem,
        OpenDeck,
    }

    // The firing grid's turn, paired with the turn of the second grid that holds some of the obstacles. The second grid is
    // never at the firing grid's angle, and 180 and 90 degrees are the ones that put a round's own frame furthest from the map's.
    private static readonly (Angle Grid, Angle Other)[] GridPairs =
    {
        (Angle.Zero, Angle.FromDegrees(180)),
        (Angle.FromDegrees(90), Angle.FromDegrees(37)),
        (Angle.FromDegrees(180), Angle.FromDegrees(90)),
        (Angle.FromDegrees(37), Angle.Zero),
    };

    // The tile the obstacles stand on, in the firing grid's own frame: in front of the vehicle on the aim line, nearer to it than
    // the aimed point. The face toward the gun is its y = 4 edge; another vehicle's hull stands out to y = 4.7.
    private static readonly Vector2i ObstacleTile = new(8, 3);
    private static readonly Vector2 ObstacleCentre = new(8.5f, 3.5f);
    private const float ObstacleNearFaceY = 4f;
    private const float VehicleNearFaceY = 4.7f;

    /// <summary>What a placed obstacle left in the world, to take away again.</summary>
    private sealed class Placed
    {
        public readonly List<EntityUid> Entities = new();
        public EntityUid OtherGrid;
        public bool TookTheFloor;
    }

    /// <summary>Puts one obstacle on the tile in front of the gun. A second grid, turned by <paramref name="otherRotation"/>, holds some.</summary>
    private Placed PlaceObstacle(Obstacle kind, Angle otherRotation)
    {
        var placed = new Placed();
        var grid = MapData.Grid.Owner;

        if (kind is Obstacle.OtherGridWall or Obstacle.OtherGridWindoor or Obstacle.ShipOverFloorWall)
        {
            // The second grid's wall stands where the firing grid's obstacle tile is. The wall is either on a grid with no floor under it,
            // or on a ship that lies over the firing grid's floor, as a ship does over a planet.
            if (kind != Obstacle.ShipOverFloorWall)
            {
                MapSystem.SetTile(grid, MapData.Grid.Comp, ObstacleTile, Tile.Empty);
                placed.TookTheFloor = true;
            }

            // A windoor is built on a ship the way a ship's parts are, square to the ship, on the edge that faces the gun most nearly.
            var windoor = kind == Obstacle.OtherGridWindoor;
            var turn = windoor ? Angle.FromDegrees(Math.Round(otherRotation.Degrees / 90.0) * 90.0) : otherRotation;

            var centre = Vector2.Transform(ObstacleCentre, Transform.GetWorldMatrix(grid));
            var other = MapSystem.CreateGridEntity(MapId);
            placed.OtherGrid = other.Owner;
            MapSystem.SetTile(other.Owner, other.Comp, Vector2i.Zero, new Tile(TileMan["Plating"].TileId));

            // A ship lying over the floor of another grid would be pushed off it, taking the wall out of the line of fire.
            SEntMan.System<SharedPhysicsSystem>().SetBodyType(other.Owner, BodyType.Static);
            Transform.SetWorldPositionRotation(other.Owner, centre - turn.RotateVec(new Vector2(0.5f, 0.5f)), turn);

            var barrier = SEntMan.SpawnEntity(windoor ? "WindoorSecure" : "WallSolid", new EntityCoordinates(other.Owner, new Vector2(0.5f, 0.5f)));
            if (windoor)
            {
                // Back along the line of fire, as near as the ship's squares allow.
                var facing = Transform.GetWorldRotation(grid) + Angle.FromDegrees(180);
                Transform.SetLocalRotation(barrier, Angle.FromDegrees(Math.Round((facing - turn).Degrees / 90.0) * 90.0));
            }

            placed.Entities.Add(barrier);
            return placed;
        }

        var proto = kind switch
        {
            Obstacle.Door => "Airlock",
            Obstacle.Window => "ReinforcedWindow",
            Obstacle.DiagonalWall => "WallSolidDiagonal",
            Obstacle.Shutter => "ShuttersNormal",
            Obstacle.BlastDoor => "BlastDoor",
            Obstacle.Firelock => "Firelock",
            Obstacle.DirectionalWindow => "WindowReinforcedDirectional",
            Obstacle.Windoor => "WindoorSecure",
            Obstacle.OtherVehicle => "WFVehicleTestChassis",
            _ => "WallSolid",
        };

        var obstacle = SEntMan.SpawnEntity(proto, new EntityCoordinates(grid, ObstacleCentre));

        // An edge barrier sits on the edge of its tile that faces the way it points; this one faces the gun.
        if (kind is Obstacle.DirectionalWindow or Obstacle.Windoor)
            Transform.SetLocalRotation(obstacle, Angle.FromDegrees(180));

        // A firelock is open until it is shut.
        if (kind == Obstacle.Firelock)
        {
            var doors = SEntMan.System<SharedDoorSystem>();
            doors.StartClosing(obstacle);
            doors.OnPartialClose(obstacle);
        }

        placed.Entities.Add(obstacle);
        return placed;
    }

    private void RemoveObstacle(Placed placed)
    {
        foreach (var entity in placed.Entities)
        {
            if (!SEntMan.Deleted(entity))
                SEntMan.DeleteEntity(entity);
        }

        if (placed.OtherGrid.IsValid())
            SEntMan.DeleteEntity(placed.OtherGrid);

        if (placed.TookTheFloor)
            MapSystem.SetTile(MapData.Grid.Owner, MapData.Grid.Comp, ObstacleTile, new Tile(TileMan["Plating"].TileId));
    }

    /// <summary>The firing line in the world: where the vehicle is and the way its guns point.</summary>
    private readonly record struct Line(Vector2 VehiclePos, Vector2 Direction, Vector2 Muzzle);

    private Line ReadLine(EntityUid vehicle, EntityUid gun)
    {
        var pos = Transform.GetWorldPosition(vehicle);
        var direction = Transform.GetWorldRotation(vehicle).ToWorldVec();
        var aimAhead = Transform.ToCoordinates(new MapCoordinates(pos + direction * AimDistance, MapId));

        var muzzle = SEntMan.TryGetComponent(gun, out GunMuzzleOffsetComponent? offset) &&
                     SEntMan.System<GunMuzzleOffsetSystem>().TryGetMuzzleCoordinates(gun, offset, aimAhead, out var muzzleCoordinates, out _)
            ? Transform.ToMapCoordinates(muzzleCoordinates).Position
            : Transform.GetWorldPosition(gun);

        return new Line(pos, direction, muzzle);
    }

    /// <summary>The grid the vehicle stands on, in the world: a rigid turn and shift of the frame everything was built in.</summary>
    private Vector2 GridLocal(Vector2 world) => Vector2.Transform(world, Transform.GetInvWorldMatrix(MapData.Grid.Owner));

    /// <summary>Whether a rest point is on the gun's side of the obstacle, with nothing of it inside the obstacle or behind it.</summary>
    private bool OnTheNearSide(Obstacle kind, Placed placed, Line line, Vector2 rest)
    {
        switch (kind)
        {
            case Obstacle.OtherGridWall:
            case Obstacle.OtherGridWindoor:
            case Obstacle.ShipOverFloorWall:
            {
                var local = Vector2.Transform(rest, Transform.GetInvWorldMatrix(placed.OtherGrid));
                var centre = Transform.GetWorldPosition(placed.Entities[0]);
                var inside = local.X >= 0f && local.X <= 1f && local.Y >= 0f && local.Y <= 1f;
                return !inside && Vector2.Dot(rest - centre, line.Direction) < 0f;
            }
            case Obstacle.OtherVehicle:
                return GridLocal(rest).Y >= VehicleNearFaceY - 0.001f;
            default:
                return GridLocal(rest).Y >= ObstacleNearFaceY - 0.001f;
        }
    }

    /// <summary>
    /// A fixed-distance round meets every kind of obstacle between the muzzle and the aimed point, with the firing grid turned four
    /// ways and the second grid another way again: it stops in front of the obstacle (smoke bursts there, the grenade's fuse runs
    /// there) and does not reach the point. The obstacles the round must pass are passed.
    /// </summary>
    private async Task CheckObstacles(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, List<string> failures)
    {
        var grid = MapData.Grid.Owner;

        foreach (var (gridRotation, otherRotation) in GridPairs)
        {
            await TurnTheGrid(gridRotation, vehicle);

            foreach (var kind in Enum.GetValues<Obstacle>())
            {
                void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: grid at {gridRotation.Degrees:0} degrees, against a {kind}: {what}");

                var placed = new Placed();
                await Server.WaitPost(() => placed = PlaceObstacle(kind, otherRotation));
                await RunTicks(kind == Obstacle.Firelock ? 40 : 3);

                var line = new Line();
                var shots = await FireAtThePoint(gun, netGun, vehicle, AimDistance, l => line = l);
                if (shots.Count == 0)
                    Fail("the gun fired nothing.");


                await RunSeconds(Settle(gun));

                await Server.WaitAssertion(() =>
                {
                    CheckRest(shots, kind, placed, line, Fail);
                    ClearRounds();
                    RemoveObstacle(placed);
                });
                await RunTicks(2);
            }

            await CheckPassThrough(gunProto, mount, vehicle, gun, netGun, gridRotation, failures);
        }

        await TurnTheGrid(Angle.Zero, vehicle);
    }

    /// <summary>
    /// Every round fired is at rest on the near side of the obstacle, and not well short of it; and where its fuse or its hit set it off,
    /// which is where its blast starts, it is on the near side too.
    /// </summary>
    private void CheckRest(List<EntityUid> shots, Obstacle kind, Placed placed, Line line, Action<string> fail)
    {
        var probe = Server.System<FixedStopProbeSystem>();
        var collide = Server.System<CollideTriggerProbeSystem>();
        foreach (var shot in shots)
        {
            if (!probe.Stops.TryGetValue(shot, out var stoppedAt))
            {
                // A shell that goes off on its first hit by the physics does not come to rest.
                if (!collide.TriggerPositions.TryGetValue(shot, out var wentOffAt))
                {
                    fail(probe.Ends.ContainsKey(shot) ? "a round was deleted without going off." : "a round never ended its flight.");
                    continue;
                }

                if (!OnTheNearSide(kind, placed, line, wentOffAt))
                    fail($"a round went off at {wentOffAt} (grid y {GridLocal(wentOffAt).Y:0.00}), behind the obstacle's near side.");

                continue;
            }

            if (!OnTheNearSide(kind, placed, line, stoppedAt))
                fail($"a round came to rest at {stoppedAt} (grid y {GridLocal(stoppedAt).Y:0.00}), behind the obstacle's near side.");
            else if (!NotStoppedShort(kind, placed, stoppedAt))
                fail($"a round came to rest at {stoppedAt} (grid y {GridLocal(stoppedAt).Y:0.00}), well short of the obstacle.");

            if (!SEntMan.Deleted(shot) && !OnTheNearSide(kind, placed, line, Transform.GetWorldPosition(shot)))
                fail($"a round lies at {Transform.GetWorldPosition(shot)} (grid y {GridLocal(Transform.GetWorldPosition(shot)).Y:0.00}), behind the obstacle's near side.");

            // The fuse of a grenade runs where it rests, and the blast of a shell starts where it stopped.
            if (collide.AnyTriggers.TryGetValue(shot, out var firedAt) && !OnTheNearSide(kind, placed, line, firedAt))
                fail($"a round went off at {firedAt} (grid y {GridLocal(firedAt).Y:0.00}), behind the obstacle's near side.");
        }
    }

    /// <summary>Whether a rest point is within a stand-off and a little of the obstacle, so a round that stopped at the muzzle does not pass.</summary>
    private bool NotStoppedShort(Obstacle kind, Placed placed, Vector2 rest)
    {
        return kind switch
        {
            Obstacle.OtherGridWall or Obstacle.OtherGridWindoor or Obstacle.ShipOverFloorWall => Vector2.Distance(rest, Transform.GetWorldPosition(placed.Entities[0])) <= 2.2f,
            Obstacle.OtherVehicle => GridLocal(rest).Y <= VehicleNearFaceY + 1.25f,
            _ => GridLocal(rest).Y <= ObstacleNearFaceY + 1.25f,
        };
    }
    /// <summary>How long to let a shot play out, in seconds: its flight and, for a grenade, its fuse.</summary>
    private float Settle(EntityUid gun)
    {
        return SEntMan.TryGetComponent(gun, out OnShootTriggerAmmoTimerComponent? timer) ? timer.Delay + 0.4f : 1f;
    }

    private void ClearRounds()
    {
        foreach (var shot in Projectiles())
        {
            SEntMan.DeleteEntity(shot);
        }
    }

    /// <summary>
    /// Turns the firing grid and gives the vehicle on it a moment to take in the new frame. A turret holds its direction in the world
    /// as the ship turns, as a gunner would find it, so the turrets are laid on the vehicle's new facing for the shot along it.
    /// </summary>
    private async Task TurnTheGrid(Angle rotation, EntityUid vehicle)
    {
        await Server.WaitPost(() => Transform.SetLocalRotation(MapData.Grid.Owner, rotation));
        await RunTicks(5);

#pragma warning disable RA0002
        await Server.WaitPost(() =>
        {
            var facing = Transform.GetWorldRotation(vehicle);
            var query = SEntMan.EntityQueryEnumerator<VehicleTurretComponent>();
            while (query.MoveNext(out var uid, out var turret))
            {
                turret.WorldRotation = Angle.Zero;
                turret.TargetRotation = turret.StabilizedRotation ? facing : Angle.Zero;
                turret.LastVehicleRotation = facing;
                turret.LastVehicleRotationValid = true;
                SEntMan.Dirty(uid, turret);
            }
        });
#pragma warning restore RA0002
        await RunTicks(3);
    }

    /// <summary>
    /// One shot at a point <paramref name="distance"/> tiles from the muzzle along the vehicle's facing, with the blast taken out. The
    /// gun's own state is reset so it fires. Returns the rounds in the air.
    /// </summary>
    private async Task<List<EntityUid>> FireAtThePoint(EntityUid gun, NetEntity netGun, EntityUid vehicle, float distance, Action<Line> line)
    {
        var shots = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var guns = SEntMan.System<SharedGunSystem>();
            var gunComp = SEntMan.GetComponent<GunComponent>(gun);
            var ammoComp = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);

            var read = ReadLine(vehicle, gun);
            line(read);

            var aim = Transform.ToCoordinates(new MapCoordinates(read.Muzzle + read.Direction * distance, MapId));
            guns.SetBallisticUnspawned((gun, ammoComp), 3);
            ResetGun(gunComp);
            guns.ShootRequested(netGun, SEntMan.GetNetCoordinates(aim), null, null, ServerSession);

            shots = Projectiles();
            foreach (var shot in shots)
            {
                SEntMan.RemoveComponent<ExplosiveComponent>(shot);
            }
        });

        return shots;
    }

    /// <summary>Open doors, mobs, loose items and open deck do not stop a round: it reaches the aimed point.</summary>
    private async Task CheckPassThrough(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, Angle gridRotation, List<string> failures)
    {
        var probe = Server.System<FixedStopProbeSystem>();
        var gunComp = SEntMan.GetComponent<GunComponent>(gun);


        foreach (var kind in Enum.GetValues<PassThrough>())
        {
            void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: grid at {gridRotation.Degrees:0} degrees, past {kind}: {what}");

            var placed = new List<EntityUid>();
            await Server.WaitPost(() =>
            {
                var grid = MapData.Grid.Owner;
                switch (kind)
                {
                    case PassThrough.OpenDoor:
                        var door = SEntMan.SpawnEntity("Airlock", new EntityCoordinates(grid, ObstacleCentre));
                        var doors = SEntMan.System<SharedDoorSystem>();
                        doors.SetState(door, DoorState.Open);
                        SEntMan.System<SharedPhysicsSystem>().SetCanCollide(door, false);
                        placed.Add(door);
                        break;
                    case PassThrough.Mob:
                        placed.Add(SEntMan.SpawnEntity("MobMouse", new EntityCoordinates(grid, ObstacleCentre)));
                        break;
                    case PassThrough.LooseItem:
                        placed.Add(SEntMan.SpawnEntity("Crowbar", new EntityCoordinates(grid, ObstacleCentre)));
                        break;
                }
            });
            await RunTicks(kind == PassThrough.OpenDoor ? 40 : 3);

            var line = new Line();
            var shots = await FireAtThePoint(gun, netGun, vehicle, AimDistance, l => line = l);
            await RunSeconds(1.5f);

            await Server.WaitAssertion(() =>
            {
                var wanted = AimDistance;
                if (SEntMan.TryGetComponent(gun, out ShootAtFixedPointComponent? fixedComp) && fixedComp.MaxFixedRange is { } gunCap)
                    wanted = Math.Min(wanted, gunCap);

                foreach (var shot in shots)
                {
                    if (!probe.Stops.TryGetValue(shot, out var stoppedAt))
                    {
                        // A shell goes off on a mob it meets; it only has to have ended.
                        if (kind != PassThrough.Mob && !probe.Ends.ContainsKey(shot))
                            Fail("a round never ended its flight.");

                        continue;
                    }

                    var flown = Vector2.Distance(stoppedAt, line.Muzzle);
                    var tolerance = StopTolerance + gunComp.ProjectileSpeedModified * (float) STiming.TickPeriod.TotalSeconds;
                    if (Math.Abs(flown - wanted) > tolerance)
                        Fail($"a round stopped {flown:0.0} tiles out, not about {wanted:0.0}.");
                }

                ClearRounds();
                foreach (var entity in placed)
                {
                    if (!SEntMan.Deleted(entity))
                        SEntMan.DeleteEntity(entity);
                }
            });
            await RunTicks(2);
        }
    }

    // Aims from this far in front of the obstacle's face to this far behind it.
    private static readonly float[] FaceOffsets = { -0.5f, -0.4f, -0.3f, -0.2f, -0.1f, 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };

    /// <summary>
    /// A shot aimed at the obstacle's face, a little before it and a little behind it, stops on the near side whatever phase of the
    /// physics step the round meets the obstacle in and whichever way the flight clock runs out: against a wall, a directional window
    /// and a windoor, whose tile the round must not rest in.
    /// </summary>
    private async Task CheckAimsAtTheFace(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, List<string> failures)
    {
        foreach (var gridRotation in new[] { Angle.Zero, Angle.FromDegrees(37) })
        {
            await TurnTheGrid(gridRotation, vehicle);

            foreach (var kind in new[] { Obstacle.Wall, Obstacle.DirectionalWindow, Obstacle.Windoor })
            {
                var placed = new Placed();
                await Server.WaitPost(() => placed = PlaceObstacle(kind, Angle.Zero));
                await RunTicks(3);

                foreach (var offset in FaceOffsets)
                {
                    void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: grid at {gridRotation.Degrees:0} degrees, aimed {offset:+0.0;-0.0;0.0} tiles from the face of a {kind}: {what}");

                    // A gun may break the barrier it is fired at; a round past the wreck of one has gone through nothing.
                    var replaced = false;
                    await Server.WaitPost(() =>
                    {
                        if (placed.Entities.Count == 0 || !SEntMan.Deleted(placed.Entities[0]))
                            return;

                        RemoveObstacle(placed);
                        placed = PlaceObstacle(kind, Angle.Zero);
                        replaced = true;
                    });
                    if (replaced)
                        await RunTicks(3);

                    // How far the muzzle is from the face, along the line of fire.
                    var toFace = 0f;
                    await Server.WaitPost(() =>
                    {
                        var read = ReadLine(vehicle, gun);
                        toFace = GridLocal(read.Muzzle).Y - ObstacleNearFaceY;
                    });

                    var line = new Line();
                    var shots = await FireAtThePoint(gun, netGun, vehicle, Math.Max(toFace + offset, 0.05f), l => line = l);
                    if (shots.Count == 0)
                        Fail("the gun fired nothing.");

                    await RunSeconds(Settle(gun));

                    await Server.WaitAssertion(() =>
                    {
                        CheckRest(shots, kind, placed, line, Fail);
                        ClearRounds();
                    });
                    await RunTicks(2);
                }

                await Server.WaitPost(() => RemoveObstacle(placed));
                await RunTicks(2);
            }
        }

        await TurnTheGrid(Angle.Zero, vehicle);
    }

    /// <summary>
    /// An aim point that is on another map, not a number, or infinite is refused before a round is spawned: no round, no ammunition used.
    /// The gunner's own body is on the vehicle's interior map, so a point relative to it is on the wrong map.
    /// </summary>
    private async Task CheckBadAims(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, List<string> failures)
    {
        var guns = SEntMan.System<SharedGunSystem>();
        var gunComp = SEntMan.GetComponent<GunComponent>(gun);
        var ammoComp = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);

        var bad = new List<(string Name, Func<Line, NetCoordinates> Point)>
        {
            ("a point on the interior map", line =>
            {
                // Chosen so that, taken as bare numbers, it points along the barrel.
                var wanted = line.Muzzle + line.Direction * AimDistance;
                return SEntMan.GetNetCoordinates(new EntityCoordinates(SPlayer, wanted - Transform.GetWorldPosition(SPlayer)));
            }),
            ("a point that is not a number", _ => SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, new Vector2(float.NaN, float.NaN)))),
            ("a point at infinity", _ => SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, new Vector2(float.PositiveInfinity, 0f)))),
            ("a point at minus infinity", _ => SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, new Vector2(0f, float.NegativeInfinity)))),
        };

        foreach (var (name, point) in bad)
        {
            await Server.WaitAssertion(() =>
            {
                void Fail(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: aimed at {name}: {what}");

                guns.SetBallisticUnspawned((gun, ammoComp), 3);
                ResetGun(gunComp);
                var before = ammoComp.Count;

                try
                {
                    guns.ShootRequested(netGun, point(ReadLine(vehicle, gun)), null, null, ServerSession);
                }
                catch (Exception e)
                {
                    Fail($"it threw {e.GetType().Name}.");
                    return;
                }

                var fired = Projectiles();
                if (fired.Count != 0 || ammoComp.Count != before)
                    Fail($"the shot was not refused: {fired.Count} rounds in the air, the magazine from {before} to {ammoComp.Count}.");

                foreach (var shot in fired)
                {
                    SEntMan.RemoveComponent<ExplosiveComponent>(shot);
                    SEntMan.DeleteEntity(shot);
                }
            });
        }
    }

    /// <summary>
    /// The firing ship moves and turns under the vehicle, and the wall is on that ship; or the wall is on a second ship that drifts and
    /// turns toward the first. Either way a round stops on the near side of the wall, and a round that stopped in front of the second
    /// ship's hull stays on that ship as it goes by, so a fuse runs where the round rests on the ship.
    /// </summary>
    private async Task CheckMovingShips(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, List<string> failures)
    {
        var probe = Server.System<FixedStopProbeSystem>();
        var grid = MapData.Grid.Owner;

        // The firing ship drifts and turns, with a wall line on it.
        void Fail1(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: on a moving and turning ship, at its own wall: {what}");

        var walls = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            foreach (var x in new[] { 7, 8, 9 })
            {
                walls.Add(SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(x + 0.5f, ObstacleTile.Y + 0.5f))));
            }

            SetShipMoving(grid, new Vector2(1.5f, 0.75f), 0.08f);
        });
        await RunTicks(3);

        var shots = await FireAtThePoint(gun, netGun, vehicle, AimDistance, _ => { });
        if (shots.Count == 0)
            Fail1("the gun fired nothing.");

        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            foreach (var shot in shots)
            {
                if (!probe.Stops.TryGetValue(shot, out var stoppedAt) &&
                    !Server.System<CollideTriggerProbeSystem>().TriggerPositions.TryGetValue(shot, out stoppedAt) &&
                    !probe.Ends.TryGetValue(shot, out stoppedAt))
                {
                    Fail1("a round never ended its flight.");
                    continue;
                }

                // The ship has moved on since the stop: a round that is gone is judged where it lay on the ship, one that is still there where it is now.
                var localY = probe.StopFrames.TryGetValue(shot, out var frame) && frame.Parent == grid
                    ? frame.Local.Y
                    : GridLocal(SEntMan.Deleted(shot) ? stoppedAt : Transform.GetWorldPosition(shot)).Y;
                if (localY < ObstacleNearFaceY - 0.001f)
                    Fail1($"a round ended at grid y {localY:0.00}, behind the wall's near side.");
            }

            ClearRounds();
            SetShipMoving(grid, Vector2.Zero, 0f);
            foreach (var wall in walls)
            {
                SEntMan.DeleteEntity(wall);
            }

            Transform.SetWorldPositionRotation(grid, Vector2.Zero, Angle.Zero);
        });
        await RunTicks(5);

        foreach (var kind in new[] { Obstacle.OtherGridWall, Obstacle.OtherGridWindoor })
        {
            await CheckClosingShip(gunProto, mount, vehicle, gun, netGun, kind, failures);
        }
    }

    /// <summary>
    /// A second ship, a hull of its own in a hole in the floor, closes on the gunner while turning. A round that stops in front of its hull
    /// lies on the ship, and the ship carries it, with the fuse of a grenade going off where it lies.
    /// </summary>
    private async Task CheckClosingShip(EntityPrototype gunProto, Mount mount, EntityUid vehicle, EntityUid gun, NetEntity netGun, Obstacle kind, List<string> failures)
    {
        var probe = Server.System<FixedStopProbeSystem>();
        var grid = MapData.Grid.Owner;
        void Fail2(string what) => failures.Add($"{gunProto.ID} on {mount.Vehicle}: at the {kind} of a ship closing on it: {what}");

        var placed = new Placed();
        await Server.WaitPost(() =>
        {
            for (var x = ObstacleTile.X - 3; x <= ObstacleTile.X + 3; x++)
            {
                for (var y = ObstacleTile.Y - 3; y <= ObstacleTile.Y + 3; y++)
                {
                    MapSystem.SetTile(grid, MapData.Grid.Comp, new Vector2i(x, y), Tile.Empty);
                }
            }

            placed = PlaceObstacle(kind, Angle.FromDegrees(37));
            SetShipMoving(placed.OtherGrid, new Vector2(-0.3f, 1.2f), 0.1f);
        });
        await RunTicks(3);

        var line = new Line();
        var shots = await FireAtThePoint(gun, netGun, vehicle, AimDistance, l => line = l);
        await RunSeconds(0.9f);

        var resting = new Dictionary<EntityUid, (EntityUid Parent, Vector2 Local)>();
        await Server.WaitAssertion(() =>
        {
            // The near side is the gunner's side of the wall, worked in the ship's own frame as the ship turns.
            var toTheWall = (Transform.GetWorldPosition(placed.Entities[0]) - line.Muzzle).Normalized();
            var localDirection = (-Transform.GetWorldRotation(placed.OtherGrid)).RotateVec(toTheWall);

            foreach (var shot in shots)
            {
                if (!probe.StopFrames.TryGetValue(shot, out var frame))
                {
                    if (!SEntMan.Deleted(shot))
                        Fail2("a round is still in the air.");

                    continue;
                }

                // The round rests on the ship that stopped it, so the ship carries it, whether it is still there or has burst.
                if (frame.Parent != placed.OtherGrid)
                    Fail2("a round that stopped in front of the ship's hull is not on the ship, so the ship carries its wall over the round.");

                var inside = frame.Local.X >= 0f && frame.Local.X <= 1f && frame.Local.Y >= 0f && frame.Local.Y <= 1f;
                if (inside || Vector2.Dot(frame.Local - new Vector2(0.5f, 0.5f), localDirection) >= 0f)
                    Fail2($"a round came to rest at {frame.Local} on the ship, in the hull or behind it.");

                if (!SEntMan.Deleted(shot))
                    resting[shot] = (frame.Parent, SEntMan.GetComponent<TransformComponent>(shot).LocalPosition);
            }
        });

        // The ship goes on; a round that rests on it keeps its place on it, and its fuse goes off where it lies.
        await RunSeconds(Math.Max(Settle(gun) - 0.9f, 0.3f));
        await Server.WaitAssertion(() =>
        {
            var collide = Server.System<CollideTriggerProbeSystem>();
            foreach (var (shot, (parent, local)) in resting)
            {
                if (SEntMan.Deleted(shot))
                    continue;

                var xform = SEntMan.GetComponent<TransformComponent>(shot);
                if (xform.ParentUid != parent || (xform.LocalPosition - local).Length() > 0.01f)
                    Fail2($"a resting round moved on the ship from {local} to {xform.LocalPosition}.");

                if (collide.AnyTriggerFrames.TryGetValue(shot, out var fired) && (fired.Parent != parent || (fired.Local - local).Length() > 0.01f))
                    Fail2($"a fuse went off at {fired.Local} on {fired.Parent}, not where the round lay on the ship at {local}.");
            }

            ClearRounds();
            SetShipMoving(placed.OtherGrid, Vector2.Zero, 0f);
            RemoveObstacle(placed);

            for (var x = ObstacleTile.X - 3; x <= ObstacleTile.X + 3; x++)
            {
                for (var y = ObstacleTile.Y - 3; y <= ObstacleTile.Y + 3; y++)
                {
                    MapSystem.SetTile(grid, MapData.Grid.Comp, new Vector2i(x, y), new Tile(TileMan["Plating"].TileId));
                }
            }
        });
        await RunTicks(2);
    }

    /// <summary>
    /// A gun put its muzzle through a wall: the round it spawned beyond a wall, a windoor or a directional window that lies between
    /// the gun and the muzzle is stopped on the gun's side of it, on grids turned any way, rather than delivered on the far side.
    /// </summary>
    [Test]
    public async Task ARoundSpawnedBeyondAThinWallIsStoppedOnTheGunsSide()
    {
        await LayPlating();

        var failures = new List<string>();
        var probe = Server.System<FixedStopProbeSystem>();
        var grid = MapData.Grid.Owner;

        foreach (var angle in new[] { Angle.Zero, Angle.FromDegrees(37), Angle.FromDegrees(180) })
        {
            await Server.WaitPost(() => Transform.SetLocalRotation(grid, angle));
            await RunTicks(3);

            foreach (var kind in new[] { Obstacle.Wall, Obstacle.Windoor, Obstacle.DirectionalWindow })
            {
                var entities = new List<EntityUid>();
                var round = EntityUid.Invalid;
                await Server.WaitPost(() =>
                {
                    // The barrier's tile is y 6 to 7 on the grid, an edge barrier on its y = 7 edge; the gun is at y 8.5 and the muzzle at 5.5.
                    var barrier = SEntMan.SpawnEntity(kind switch
                    {
                        Obstacle.Windoor => "WindoorSecure",
                        Obstacle.DirectionalWindow => "WindowReinforcedDirectional",
                        _ => "WallSolid",
                    }, new EntityCoordinates(grid, new Vector2(8.5f, 6.5f)));
                    if (kind != Obstacle.Wall)
                        Transform.SetLocalRotation(barrier, Angle.FromDegrees(180));

                    var gun = SEntMan.SpawnEntity("VehicleHumveeLauncher", new EntityCoordinates(grid, new Vector2(8.5f, 8.5f)));
                    SEntMan.GetComponent<GunComponent>(gun).ShootCoordinates = new EntityCoordinates(grid, new Vector2(8.5f, 0.5f));

                    round = SEntMan.SpawnEntity("WFVehicleGrenadeHEShell", new EntityCoordinates(grid, new Vector2(8.5f, 5.5f)));
                    SEntMan.RemoveComponent<ExplosiveComponent>(round);
                    SEntMan.GetComponent<ProjectileComponent>(round).Weapon = gun;

                    var physics = SEntMan.System<SharedPhysicsSystem>();
                    physics.SetBodyStatus(round, SEntMan.GetComponent<PhysicsComponent>(round), BodyStatus.InAir);
                    physics.SetLinearVelocity(round, Transform.GetWorldRotation(grid).RotateVec(new Vector2(0f, -18f)));

                    entities.Add(barrier);
                    entities.Add(gun);
                    SEntMan.EventBus.RaiseLocalEvent(gun, new AmmoShotEvent { FiredProjectiles = new List<EntityUid> { round } });
                });

                await RunSeconds(1f);

                await Server.WaitAssertion(() =>
                {
                    void Fail(string what) => failures.Add($"grid at {angle.Degrees:0} degrees, a {kind} between the gun and the muzzle: {what}");

                    if (!probe.Stops.TryGetValue(round, out var rest))
                        Fail("the round never stopped.");
                    else if (GridLocal(rest).Y < 7f - 0.001f)
                        Fail($"the round was delivered beyond the barrier, at grid y {GridLocal(rest).Y:0.00}.");

                    ClearRounds();
                    foreach (var entity in entities)
                    {
                        SEntMan.DeleteEntity(entity);
                    }
                });
                await RunTicks(2);
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// A round whose aim is on another map or is not a point is not left to fly: whatever sent it off on a course that nothing checked,
    /// it is taken away before it goes anywhere.
    /// </summary>
    [Test]
    public async Task ARoundWithAnAimThatIsNotOnItsMapIsTakenAway()
    {
        await LayPlating();

        var failures = new List<string>();
        var grid = MapData.Grid.Owner;
        var otherMap = EntityUid.Invalid;

        await Server.WaitPost(() => otherMap = SEntMan.System<SharedMapSystem>().CreateMap(out _));

        var aims = new List<(string Name, Func<EntityCoordinates> Point)>
        {
            ("a point on another map", () => new EntityCoordinates(otherMap, new Vector2(3f, 3f))),
            ("a point that is not a number", () => new EntityCoordinates(MapData.MapUid, new Vector2(float.NaN, 1f))),
            ("a point at infinity", () => new EntityCoordinates(MapData.MapUid, new Vector2(float.PositiveInfinity, 0f))),
        };

        foreach (var (name, point) in aims)
        {
            var round = EntityUid.Invalid;
            var gun = EntityUid.Invalid;
            await Server.WaitPost(() =>
            {
                gun = SEntMan.SpawnEntity("VehicleHumveeLauncher", new EntityCoordinates(grid, new Vector2(8.5f, 8.5f)));
                SEntMan.GetComponent<GunComponent>(gun).ShootCoordinates = point();

                round = SEntMan.SpawnEntity("WFVehicleGrenadeHEShell", new EntityCoordinates(grid, new Vector2(8.5f, 7.5f)));
                SEntMan.RemoveComponent<ExplosiveComponent>(round);
                SEntMan.GetComponent<ProjectileComponent>(round).Weapon = gun;

                var physics = SEntMan.System<SharedPhysicsSystem>();
                physics.SetBodyStatus(round, SEntMan.GetComponent<PhysicsComponent>(round), BodyStatus.InAir);
                physics.SetLinearVelocity(round, new Vector2(0f, -18f));
                SEntMan.EventBus.RaiseLocalEvent(gun, new AmmoShotEvent { FiredProjectiles = new List<EntityUid> { round } });
            });
            await RunTicks(3);

            await Server.WaitAssertion(() =>
            {
                if (!SEntMan.Deleted(round))
                    failures.Add($"aimed at {name}: the round was left to fly.");

                SEntMan.DeleteEntity(gun);
            });
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
    /// <summary>Gives a grid a body that drifts and turns on its own, or stops it.</summary>
    private void SetShipMoving(EntityUid grid, Vector2 velocity, float spin)
    {
        var physics = SEntMan.System<SharedPhysicsSystem>();
        var body = SEntMan.GetComponent<PhysicsComponent>(grid);

        physics.SetBodyType(grid, velocity == Vector2.Zero && spin == 0f ? BodyType.Static : BodyType.Dynamic, body: body);
        physics.SetSleepingAllowed(grid, body, false);
        physics.SetLinearDamping(grid, body, 0f);
        physics.SetAngularDamping(grid, body, 0f);
        physics.SetLinearVelocity(grid, velocity, body: body);
        physics.SetAngularVelocity(grid, spin, body: body);

        if (velocity != Vector2.Zero || spin != 0f)
            physics.WakeBody(grid, body: body);
    }
}
