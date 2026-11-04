#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Vehicles.Safety;
using Content.Server.Parallax;
using Content.Shared._WF.Planets;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Falling;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Vehicles falling through z-levels: driving off an edge with a level below, a floor taken from under a parked vehicle,
/// the landing's damage and what it hits, and every case where the plain refusal stays.
/// </summary>
[TestFixture]
public sealed class VehicleFallTest
{
    private const string Chassis = "WFFallTestVehicle";
    private const string Sealed = "WFFallTestVehicleSealed";
    private const string Offset = "WFFallTestVehicleOffset";

    /// <summary>The first tile past the end of the upper deck.</summary>
    private const int EdgeX = 20;

    private const float FrameMax = 1000f;
    private const float WheelMax = 500f;

    /// <summary>Light weight class: 15% of the frame, 75 per wheel, 35 Blunt to a mob.</summary>
    private const float LightFrameDamage = 150f;
    private const float LightWheelDamage = 75f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFVehicleTestChassis
  id: WFFallTestVehicle
  components:
  - type: VehicleFall
  - type: GridVehicleMover
    weightClass: Light
  - type: VehicleSound
    crashSound:
      path: /Audio/Effects/explosion1.ogg
  - type: Damageable
    damageContainer: StructuralInorganic
  - type: Appearance
  - type: ItemSlots
    slots:
      wheel-1:
        startingItem: VehicleHumveeWheel
        disableEject: true
        whitelist:
          components:
          - VehicleWheelItem
  - type: HardpointSlots
    vehicleFamily: HardpointVehicleFamilyHumvee
    slots:
    - id: wheel-1
      hardpointType: HardpointTypeWheel
      slotType: HardpointSlotTypeWheel
      insertDelay: 0.5
      whitelist:
        components:
        - VehicleWheelItem
  - type: VehicleWheelSlots
    slotCount: 1
    slots:
    - wheel-1
  - type: HardpointIntegrity
    maxIntegrity: 1000
    integrity: 1000
  - type: VehicleEnter
    interiorPath: /Maps/_WF/Vehicles/Interiors/wf_test_interior.yml
    maxPassengers: 4
    enterDoAfter: 0.1
    exitDoAfter: 0.1
    entryPoints:
    - offset: 1,0
      radius: 0.6
      interiorCoords: 1.5,1.5
    - offset: -1,0
      radius: 0.6
      interiorCoords: 3.5,1.5

- type: entity
  parent: WFFallTestVehicle
  id: WFFallTestVehicleSealed
  components:
  - type: VehicleFall
    enabled: false

# The origin sits a quarter of the way along the hull, so one end of the hull is already well over an edge when the
# origin crosses it.
- type: entity
  parent: WFFallTestVehicle
  id: WFFallTestVehicleOffset
  components:
  - type: Fixtures
    fixtures:
      world:
        shape:
          !type:PhysShapeAabb
          bounds: -0.425,-0.6,0.425,1.8
        density: 1000
        mask:
        - Impassable
        - HighImpassable
        - LowImpassable
        - LargeMobLayer
        - Vehicle
        layer:
        - LargeMobLayer
        - Vehicle
      mob:
        shape:
          !type:PhysShapeAabb
          bounds: -0.425,-0.6,0.425,1.8
        density: 1000
        mask:
        - MidImpassable
        - LargeMobLayer
        layer:
        - LargeMobLayer
";

    /// <summary>The upper deck (the vehicle rig's map) over a lower map, linked as a CE z-network.</summary>
    private sealed class FallRig
    {
        public required VehicleRig Rig;
        public required TestMapData Lower;
        public EntityUid Network;

        public TestPair Pair => Rig.Pair;
        public EntityUid Vehicle => Rig.Vehicle;
        public EntityUid UpperGrid => Rig.Grid;
        public EntityUid LowerGrid => Lower.Grid.Owner;
        public MapId UpperMap => Rig.Map.MapId;
        public MapId LowerMap => Lower.MapId;
    }

    /// <summary>
    /// Builds an upper deck with plating to <paramref name="maxX"/> and a vehicle on it, a lower map with a plated floor
    /// under the area and a z-network between them. With <paramref name="levelBelow"/> off the second map is put above, so
    /// the deck has nothing under it.
    /// </summary>
    private static async Task<FallRig> Build(
        TestPair pair,
        int maxX = EdgeX,
        string proto = Chassis,
        float startX = 2.5f,
        float startY = 2.5f,
        bool levelBelow = true,
        IEnumerable<Vector2i>? walls = null,
        bool facingRight = false)
    {
        var rig = await VehicleRigs.Build(pair, maxX: maxX, startX: startX, startY: startY, proto: proto, walls: walls, facingRight: facingRight);
        var lower = await pair.CreateTestMap();
        var fall = new FallRig { Rig = rig, Lower = lower };
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -8; x < 60; x++)
            {
                for (var y = -8; y < 16; y++)
                {
                    maps.SetTile(lower.Grid.Owner, lower.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            var zLevels = server.System<CEZLevelsSystem>();
            var network = zLevels.CreateMapNetwork();
            fall.Network = network.Owner;
            Assert.That(
                zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [rig.Map.MapUid] = 0,
                    [lower.MapUid] = levelBelow ? -1 : 1,
                }),
                Is.True,
                "The test maps could not be linked.");
        });
        await server.WaitRunTicks(2);

        return fall;
    }

    private static async Task Teardown(FallRig fall)
    {
        var server = fall.Pair.Server;
        await server.WaitPost(() =>
        {
            if (!server.EntMan.Deleted(fall.Network))
                server.System<CEZLevelsSystem>().DeleteMapNetwork(fall.Network);
        });
        await server.WaitRunTicks(2);
    }

    private static MapId MapOf(FallRig fall, EntityUid uid)
    {
        return fall.Pair.Server.EntMan.GetComponent<TransformComponent>(uid).MapID;
    }

    private static float FrameIntegrity(FallRig fall)
    {
        return fall.Pair.Server.EntMan.GetComponent<HardpointIntegrityComponent>(fall.Vehicle).Integrity;
    }

    private static float WheelIntegrity(FallRig fall)
    {
        var server = fall.Pair.Server;
        Assert.That(server.System<ItemSlotsSystem>().TryGetSlot(fall.Vehicle, "wheel-1", out var slot), Is.True, "The vehicle has no wheel slot.");
        Assert.That(slot!.Item, Is.Not.Null, "The vehicle has no wheel.");
        return server.EntMan.GetComponent<HardpointIntegrityComponent>(slot.Item!.Value).Integrity;
    }

    /// <summary>Runs one tick at a time until the vehicle is on the lower map, so the position can be read as it lands.</summary>
    private static async Task<bool> RunUntilLanded(FallRig fall, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            await fall.Pair.Server.WaitRunTicks(1);

            var landed = false;
            await fall.Pair.Server.WaitPost(() => landed = MapOf(fall, fall.Vehicle) == fall.LowerMap);
            if (landed)
                return true;
        }

        return false;
    }

    private static async Task SpawnWalls(FallRig fall, IEnumerable<Vector2i> tiles)
    {
        var server = fall.Pair.Server;
        await server.WaitPost(() =>
        {
            foreach (var tile in tiles)
            {
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(fall.LowerGrid, tile.X + 0.5f, tile.Y + 0.5f));
            }
        });
        await server.WaitRunTicks(2);
    }

    private static IEnumerable<Vector2i> Block(int minX, int maxX, int minY, int maxY)
    {
        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                yield return new Vector2i(x, y);
            }
        }
    }

    /// <summary>
    /// Driving off the end of the deck with a level below drops the vehicle to the lower map at the matching position,
    /// hurts its frame and wheels, leaves its driver at the wheel, and it drives on from the landing.
    /// </summary>
    [Test]
    public async Task DrivingOffAnEdgeDropsTheVehicleToTheLevelBelow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        try
        {
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));

            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");

            var landedX = 0f;
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(fall.Vehicle);
                var vehicle = entMan.GetComponent<RMCVehicleComponent>(fall.Vehicle);
                var mover = VehicleRigs.Mover(fall.Rig);
                var world = VehicleRigs.WorldPosition(fall.Rig);
                landedX = world.X;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.GridUid, Is.EqualTo(fall.LowerGrid), "The vehicle did not land on the lower grid.");
                    Assert.That(world.X, Is.InRange(EdgeX - 1.5f, EdgeX + 0.5f), "The vehicle landed away from where it left the deck.");
                    Assert.That(world.Y, Is.EqualTo(2.5f).Within(0.1f), "The vehicle landed off its lane.");
                    Assert.That(FrameIntegrity(fall), Is.EqualTo(FrameMax - LightFrameDamage).Within(1f), "The frame took the wrong damage.");
                    Assert.That(WheelIntegrity(fall), Is.EqualTo(WheelMax - LightWheelDamage).Within(1f), "The wheel took the wrong damage.");
                    Assert.That(vehicle.Operator, Is.EqualTo(fall.Rig.Driver), "The driver lost the wheel.");
                    Assert.That(mover.SyncedGrid, Is.EqualTo(fall.LowerGrid), "The mover did not follow the vehicle to its new grid.");
                    Assert.That(mover.CurrentSpeed, Is.Zero, "A landing should end the vehicle's speed.");
                }
            });

            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.LowerMap), "The vehicle left the lower map again.");
                    Assert.That(VehicleRigs.WorldPosition(fall.Rig).X, Is.GreaterThan(landedX + 2f), "The vehicle could not be driven after landing.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The cargo carrier is a vehicle like the others, though it has no frame, wheels or interior: driven off an edge with a
    /// level below it drops to the lower grid, is still its own operator, and drives on from the landing.
    /// </summary>
    [Test]
    public async Task ACargoCarrierDrivenOffAnEdgeDropsToTheLevelBelow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, proto: "CMUCargoCarrier", facingRight: true);
        var server = pair.Server;
        var entMan = server.EntMan;

        try
        {
            await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(fall.Vehicle).HeldMoveButtons = MoveButtons.Up);

            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The carrier never dropped to the lower map.");

            var landedX = 0f;
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(fall.Vehicle);
                var world = VehicleRigs.WorldPosition(fall.Rig);
                landedX = world.X;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.GridUid, Is.EqualTo(fall.LowerGrid), "The carrier did not land on the lower grid.");
                    Assert.That(world.X, Is.InRange(EdgeX - 1.5f, EdgeX + 0.5f), "The carrier landed away from where it left the deck.");
                    Assert.That(entMan.GetComponent<RMCVehicleComponent>(fall.Vehicle).Operator, Is.EqualTo(fall.Vehicle), "The carrier lost itself as its operator.");
                    Assert.That(VehicleRigs.Mover(fall.Rig).SyncedGrid, Is.EqualTo(fall.LowerGrid), "The mover did not follow the carrier to its new grid.");
                }
            });

            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.LowerMap), "The carrier left the lower map again.");
                    Assert.That(VehicleRigs.WorldPosition(fall.Rig).X, Is.GreaterThan(landedX + 2f), "The carrier could not be driven after landing.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(fall.Vehicle).HeldMoveButtons = MoveButtons.None);
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull whose origin is near one end: driven off the deck either way round, the end that leads can be the short one,
    /// so the origin crosses the edge with little of the hull over it. The vehicle must still drop and not hang loose.
    /// </summary>
    [TestCase(MoveButtons.Right)]
    [TestCase(MoveButtons.Left)]
    public async Task AHullWithItsOriginNearOneEndStillDropsOverEitherEdge(MoveButtons direction)
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, proto: Offset, startX: direction == MoveButtons.Right ? 2.5f : 10.5f);
        var server = pair.Server;

        try
        {
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, direction));

            var landed = await RunUntilLanded(fall, 600);
            var state = "";
            await server.WaitPost(() =>
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(fall.Vehicle);
                var mover = VehicleRigs.Mover(fall.Rig);
                state = $" parent {server.EntMan.ToPrettyString(xform.ParentUid)} grid {xform.GridUid} pos {xform.LocalPosition} rot {xform.LocalRotation} dir {mover.CurrentDirection} speed {mover.CurrentSpeed} mover position {mover.Position}";
            });
            Assert.That(landed, Is.True, $"The vehicle driven {direction} never dropped to the lower map.{state}");

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(server.EntMan.GetComponent<TransformComponent>(fall.Vehicle).GridUid, Is.EqualTo(fall.LowerGrid), "The vehicle landed loose on the map.");
                    Assert.That(FrameIntegrity(fall), Is.LessThan(FrameMax));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Backing off an edge is movement too: a reversing vehicle drops the same way.</summary>
    [Test]
    public async Task ReversingOffAnEdgeDropsTheVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, startX: 12.5f);
        var server = pair.Server;

        try
        {
            await VehicleRigs.Seat(fall.Rig);

            // Face left by driving left a little, then let go and ask for the opposite direction, which is a reverse.
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Left));
            await server.WaitRunTicks(25);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
                Assert.That(VehicleRigs.Mover(fall.Rig).CurrentDirection, Is.EqualTo(new Vector2i(-1, 0)), "Test setup: the vehicle should face left."));

            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));

            Assert.That(await RunUntilLanded(fall, 600), Is.True, "The reversing vehicle never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(VehicleRigs.Mover(fall.Rig).CurrentDirection, Is.EqualTo(new Vector2i(-1, 0)), "The vehicle turned in the fall.");
                    Assert.That(VehicleRigs.WorldPosition(fall.Rig).X, Is.InRange(EdgeX - 1.5f, EdgeX + 0.5f));
                    Assert.That(FrameIntegrity(fall), Is.LessThan(FrameMax));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone inside survives the fall, stays inside, and is thrown about by the crash unless strapped in.</summary>
    [Test]
    public async Task AnOccupantSurvivesTheFallAndFeelsTheCrash()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var passenger = EntityUid.Invalid;
        var doorway = new Vector2(3.5f, 2.5f);

        try
        {
            await server.WaitPost(() =>
            {
                passenger = entMan.SpawnEntity("MobHuman", new EntityCoordinates(fall.UpperGrid, doorway));
            });
            await server.WaitRunTicks(2);
            await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(passenger, fall.Vehicle));
            await server.WaitRunTicks(30);

            MapId interiorMap = default;
            await server.WaitAssertion(() =>
            {
                var interior = entMan.GetComponent<VehicleInteriorComponent>(fall.Vehicle);
                interiorMap = interior.MapId;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(interior.Passengers, Does.Contain(passenger), "Test setup: the passenger should be inside.");
                    Assert.That(MapOf(fall, passenger), Is.EqualTo(interiorMap));
                }
            });

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                var interior = entMan.GetComponent<VehicleInteriorComponent>(fall.Vehicle);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.Deleted(passenger), Is.False, "The passenger was lost in the fall.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(passenger).CurrentState, Is.EqualTo(MobState.Alive));
                    Assert.That(MapOf(fall, passenger), Is.EqualTo(interiorMap), "The passenger left the interior.");
                    Assert.That(interior.MapId, Is.EqualTo(interiorMap), "The interior was replaced.");
                    Assert.That(interior.Passengers, Does.Contain(passenger), "The vehicle forgot its passenger.");
                    Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(passenger), Is.True);
                    Assert.That(entMan.HasComponent<KnockedDownComponent>(passenger), Is.True, "The crash did not knock the passenger down.");
                }
            });

            // The doorway still works: from the exit marker the passenger steps out onto the lower level, by the vehicle. The
            // test cabin has no walls, so the crash flings the passenger off its floor, and the safety sweep, once a second,
            // sends them back to the entry point; a teleport counts as moving and breaks the do-after of leaving. The sweeps
            // run on a clock that started when the pair did, so how they fall against this test depends on which tests the
            // pair ran before it. Leaving is tried once the passenger is back on the floor, at rest and on their feet, when
            // no sweep has anything to move.
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));

            var settled = false;
            for (var i = 0; i < 900 && !settled; i++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() =>
                {
                    var interior = entMan.GetComponent<VehicleInteriorComponent>(fall.Vehicle);
                    var inCabin = server.System<VehicleSafetySystem>().IsInsideCabin(
                        (fall.Vehicle, interior),
                        entMan.GetComponent<TransformComponent>(passenger).Coordinates);

                    settled = inCabin &&
                              entMan.GetComponent<PhysicsComponent>(passenger).LinearVelocity.Length() < 0.01f &&
                              entMan.GetComponent<StandingStateComponent>(passenger).CurrentState == StandingState.Standing &&
                              !entMan.HasComponent<KnockedDownComponent>(passenger) &&
                              !entMan.HasComponent<StunnedComponent>(passenger);
                });
            }

            Assert.That(settled, Is.True, "Test setup: the passenger never came to rest on their feet inside the cabin.");

            await server.WaitPost(() =>
            {
                var exit = EntityUid.Invalid;
                var query = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var marker, out var xform))
                {
                    if (xform.MapID == interiorMap && marker.EntryIndex == 0)
                        exit = uid;
                }

                Assert.That(exit, Is.Not.EqualTo(EntityUid.Invalid), "The interior has no exit marker for the first doorway.");
                server.System<SharedInteractionSystem>().InteractionActivate(passenger, exit);
            });
            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                var xforms = server.System<SharedTransformSystem>();
                var gap = Vector2.Distance(xforms.GetWorldPosition(passenger), xforms.GetWorldPosition(fall.Vehicle));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, passenger), Is.EqualTo(fall.LowerMap), "The passenger could not get out onto the level the vehicle fell to. " + DescribeExits(entMan, interiorMap, passenger));
                    Assert.That(gap, Is.LessThan(2.5f), "The passenger came out away from the vehicle.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Without a level below, driving off the deck is refused as it always was: the vehicle stops short and stays.</summary>
    [Test]
    public async Task WithNoLevelBelowTheEdgeStillStopsTheVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, levelBelow: false);
        var server = pair.Server;

        try
        {
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                var position = VehicleRigs.LocalPosition(fall.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "The vehicle dropped with nothing below it.");
                    Assert.That(position.X + 1.2f, Is.LessThan(EdgeX + 0.1f), "The vehicle drove off the deck.");
                    Assert.That(position.X + 1.2f, Is.GreaterThan(EdgeX - 0.7f), "The vehicle stopped well short of the edge.");
                    Assert.That(FrameIntegrity(fall), Is.EqualTo(FrameMax), "The frame was hurt without a fall.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle that is not allowed to fall is stopped at the edge even with a level below.</summary>
    [Test]
    public async Task AVehicleThatMayNotFallIsStoppedAtTheEdge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, proto: Sealed);
        var server = pair.Server;

        try
        {
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap));
                    Assert.That(VehicleRigs.LocalPosition(fall.Rig).X + 1.2f, Is.LessThan(EdgeX + 0.1f));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A parked vehicle whose floor is taken from under it falls by the same rule, with nobody driving.</summary>
    [Test]
    public async Task AParkedVehicleWhoseFloorIsRemovedFalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, maxX: 40, startX: 10.5f);
        var server = pair.Server;

        try
        {
            await server.WaitAssertion(() =>
                Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "Test setup: the vehicle should start on the deck."));

            // Take out the tiles under the hull: a 2.4 by 0.85 footprint around (10.5, 2.5).
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                foreach (var tile in Block(9, 13, 1, 4))
                {
                    maps.SetTile(fall.UpperGrid, fall.Rig.Map.Grid.Comp, tile, Tile.Empty);
                }
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                var xform = server.EntMan.GetComponent<TransformComponent>(fall.Vehicle);
                var world = VehicleRigs.WorldPosition(fall.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.MapID, Is.EqualTo(fall.LowerMap), "The parked vehicle stayed up over a hole.");
                    Assert.That(xform.GridUid, Is.EqualTo(fall.LowerGrid));
                    Assert.That(world.X, Is.EqualTo(10.5f).Within(0.1f));
                    Assert.That(world.Y, Is.EqualTo(2.5f).Within(0.1f));
                    Assert.That(FrameIntegrity(fall), Is.EqualTo(FrameMax - LightFrameDamage).Within(1f));
                }
            });
        }
        finally
        {
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Planet ground that is only unloaded holds a vehicle up and is no edge to drive off; the same tiles pinned as a hole
    /// drop it. The upper map is made a planet layer as the planets make theirs: the map is itself the ground grid, with a
    /// biome that has loaded nothing.
    /// </summary>
    [Test]
    public async Task UnloadedPlanetGroundIsNoEdgeButAPinnedHoleIs()
    {
        await using var pair = await PoolManager.GetServerClient();

        // The deck has no floor of its own; the vehicle stands on the map's ground.
        var fall = await Build(pair, maxX: -3, startX: 10.5f);
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = fall.Rig.Map.MapUid;
        var holes = Block(9, 13, 1, 4).ToList();

        try
        {
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

                var ground = entMan.EnsureComponent<MapGridComponent>(map);
                entMan.EnsureComponent<WFPlanetLayerComponent>(map);
                entMan.EnsureComponent<BiomeComponent>(map);

                foreach (var tile in Block(0, 40, 0, 6))
                {
                    maps.SetTile(map, ground, tile, new Tile(plating));
                }

                server.System<SharedTransformSystem>().SetCoordinates(fall.Vehicle, new EntityCoordinates(map, 10.5f, 2.5f));

                foreach (var tile in holes)
                {
                    maps.SetTile(map, ground, tile, Tile.Empty);
                }

                // Ground an unload emptied is not pinned; a hole is.
                var pinned = entMan.GetComponent<BiomeComponent>(map).ModifiedTiles;
                pinned.Clear();
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(fall.Vehicle).GridUid, Is.EqualTo(map), "Test setup: the vehicle should stand on the map's ground.");
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "A vehicle over unloaded ground fell.");
                }
            });

            await server.WaitPost(() =>
            {
                var biome = entMan.GetComponent<BiomeComponent>(map);
                server.System<BiomeSystem>().WfPinTiles((map, biome), holes);
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
                Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.LowerMap), "A vehicle over a pinned hole stayed up."));
        }
        finally
        {
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Driving towards ground that is only unloaded is refused as before, so the terrain can stream in.</summary>
    [Test]
    public async Task DrivingTowardsUnloadedPlanetGroundIsStillRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        try
        {
            await server.WaitPost(() =>
            {
                entMan.EnsureComponent<WFPlanetLayerComponent>(fall.UpperGrid);
                entMan.EnsureComponent<BiomeComponent>(fall.UpperGrid);
            });

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "The vehicle dropped into ground that is only unloaded.");
                    Assert.That(VehicleRigs.LocalPosition(fall.Rig).X + 1.2f, Is.LessThan(EdgeX + 0.1f), "The vehicle drove off the loaded ground.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A parked vehicle with most of its floor left stays where it is.</summary>
    [Test]
    public async Task AParkedVehicleWithMostOfItsFloorLeftStaysUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, maxX: 40, startX: 10.5f);
        var server = pair.Server;

        try
        {
            // Only the tile under the rear end of the 2.4 long hull goes.
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                maps.SetTile(fall.UpperGrid, fall.Rig.Map.Grid.Comp, new Vector2i(9, 2), Tile.Empty);
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "A vehicle with a third of its hull over a hole fell.");
                    Assert.That(FrameIntegrity(fall), Is.EqualTo(FrameMax));
                }
            });
        }
        finally
        {
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Walls where the vehicle would land send it to a free spot nearby, never into the walls.
    /// </summary>
    [Test]
    public async Task ALandingBlockedByWallsFindsAFreeSpotNearby()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var walls = Block(17, 22, 2, 3).ToList();

        try
        {
            await SpawnWalls(fall, walls);
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));

            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                var world = VehicleRigs.WorldPosition(fall.Rig);
                var footprint = new Box2(world.X - 1.2f, world.Y - 0.425f, world.X + 1.2f, world.Y + 0.425f);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        walls.Any(w => footprint.Intersects(new Box2(w.X, w.Y, w.X + 1f, w.Y + 1f))),
                        Is.False,
                        $"The vehicle landed inside the walls at {world}.");
                    Assert.That(MathF.Abs(world.Y - 2.5f), Is.GreaterThan(0.4f), "The vehicle landed on the wall row.");
                    Assert.That(world.X, Is.InRange(EdgeX - 4f, EdgeX + 3f), "The vehicle landed too far from where it fell.");
                    Assert.That(FrameIntegrity(fall), Is.LessThan(FrameMax));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>With walls over every spot it could land on, the vehicle does not fall and is stopped at the edge.</summary>
    [Test]
    public async Task AWallFilledLandingRefusesTheFall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;

        try
        {
            await SpawnWalls(fall, Block(14, 28, -2, 9));
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                var position = VehicleRigs.LocalPosition(fall.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "The vehicle fell into a solid block of wall.");
                    Assert.That(position.X + 1.2f, Is.LessThan(EdgeX + 0.1f), "The vehicle drove off the deck.");
                    Assert.That(FrameIntegrity(fall), Is.EqualTo(FrameMax));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A lower level with no floor where the vehicle would come down is no landing at all.</summary>
    [Test]
    public async Task AnEmptyLevelBelowRefusesTheFall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;

        try
        {
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                foreach (var tile in Block(12, 40, -8, 16))
                {
                    maps.SetTile(fall.LowerGrid, fall.Lower.Grid.Comp, tile, Tile.Empty);
                }
            });

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "The vehicle dropped onto nothing.");
                    Assert.That(VehicleRigs.LocalPosition(fall.Rig).X + 1.2f, Is.LessThan(EdgeX + 0.1f));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob under the landing is run over: hurt, knocked down and held down by the hull.</summary>
    [Test]
    public async Task AMobUnderTheLandingIsHit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var victim = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() =>
            {
                victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(fall.LowerGrid, 19.5f, 2.5f));
            });
            await server.WaitRunTicks(5);

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                var damage = entMan.GetComponent<DamageableComponent>(victim);
                var blunt = damage.Damage.DamageDict.GetValueOrDefault("Blunt").Float();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.Deleted(victim), Is.False);
                    Assert.That(blunt, Is.GreaterThan(20f), "The mob under the landing was not hurt.");
                    Assert.That(entMan.HasComponent<KnockedDownComponent>(victim), Is.True, "The mob under the landing was not knocked down.");
                    Assert.That(entMan.GetComponent<VehicleRunoverComponent>(victim).Vehicle, Is.EqualTo(fall.Vehicle), "The hull is not holding the mob down.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The open-tile question is answered on both sides, so the driver's prediction crosses the edge with the server and
    /// stops where the server stops: with a level below the client allows the tile, with walls filling the landing or
    /// nothing below it does not.
    /// </summary>
    [Test]
    public async Task TheClientAnswersTheOpenTileQuestionAsTheServerDoes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var edgeTile = new Vector2i(EdgeX, 2);

        async Task<(bool Server, bool Client)> Ask(FallRig fall)
        {
            var server = false;
            var client = false;
            NetEntity netVehicle = default;
            NetEntity netGrid = default;

            // The client applies the z-network a few ticks after the server makes it.
            await fall.Pair.RunTicksSync(20);
            await fall.Pair.Server.WaitPost(() =>
            {
                netVehicle = fall.Pair.Server.EntMan.GetNetEntity(fall.Vehicle);
                netGrid = fall.Pair.Server.EntMan.GetNetEntity(fall.UpperGrid);
                server = RaiseOpenTile(fall.Pair.Server.EntMan, fall.Vehicle, fall.UpperGrid, edgeTile);
            });
            await fall.Pair.Client.WaitPost(() =>
            {
                var entMan = fall.Pair.Client.EntMan;
                client = RaiseOpenTile(entMan, entMan.GetEntity(netVehicle), entMan.GetEntity(netGrid), edgeTile);
            });

            return (server, client);
        }

        var open = await Build(pair);
        try
        {
            var answers = await Ask(open);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(answers.Server, Is.True, "The server refused an edge with a level below.");
                Assert.That(answers.Client, Is.True, "The client refused an edge with a level below. ");
            }
        }
        finally
        {
            await Teardown(open);
        }

        var walled = await Build(pair);
        try
        {
            await SpawnWalls(walled, Block(14, 28, -2, 9));
            var answers = await Ask(walled);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(answers.Server, Is.False, "The server allowed an edge over a wall-filled landing.");
                Assert.That(answers.Client, Is.False, "The client allowed an edge over a wall-filled landing.");
            }
        }
        finally
        {
            await Teardown(walled);
        }

        var closed = await Build(pair, levelBelow: false);
        try
        {
            var answers = await Ask(closed);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(answers.Server, Is.False, "The server allowed an edge with nothing below.");
                Assert.That(answers.Client, Is.False, "The client allowed an edge with nothing below.");
            }
        }
        finally
        {
            await Teardown(closed);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// What a vehicle lands on is smashed the way driving into it smashes it: destroyed once, through its own thresholds, so
    /// a table leaves the sheet of steel a broken table leaves.
    /// </summary>
    [Test]
    public async Task ALandingSmashesATableAsADriveWould()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var table = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() => table = entMan.SpawnEntity("Table", new EntityCoordinates(fall.LowerGrid, 19.5f, 2.5f)));
            await server.WaitRunTicks(5);

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await server.WaitRunTicks(10);

            await server.WaitAssertion(() =>
            {
                var sheets = 0;
                var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == "SheetSteel1" && xform.MapID == fall.LowerMap)
                        sheets++;
                }

                var destroyed = server.System<VehicleEventProbeSystem>().Destructions.GetValueOrDefault(table);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.Deleted(table), Is.True, "The table under the landing was not destroyed.");
                    Assert.That(destroyed, Is.EqualTo(1), "The table was destroyed more than once.");
                    Assert.That(sheets, Is.EqualTo(1), "A table smashed by a landing should leave the sheet of steel a broken table leaves.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A powered door that driving at would be refused is no place to land: the vehicle comes down beside it, and the door
    /// is left as it was.
    /// </summary>
    [Test]
    public async Task APoweredLockedDoorUnderTheLandingIsLeftStanding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var door = EntityUid.Invalid;

        try
        {
            await server.WaitPost(() =>
            {
                door = entMan.SpawnEntity("AirlockEngineeringLocked", new EntityCoordinates(fall.LowerGrid, 19.5f, 2.5f));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(door, false);
            });
            await server.WaitRunTicks(40);

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<AirlockComponent>(door).Powered, Is.True, "Test setup: the door should be powered."));

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            Assert.That(await RunUntilLanded(fall, 500), Is.True, "The vehicle never dropped to the lower map.");
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await server.WaitRunTicks(10);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.Deleted(door) || entMan.IsQueuedForDeletion(door), Is.False, "The vehicle landed on a powered door and destroyed it.");
                    Assert.That(entMan.GetComponent<DamageableComponent>(door).TotalDamage.Double(), Is.Zero, "The vehicle landed on a powered door and hurt it.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A lower floor that starts a little way past the edge is room for the vehicle only from some way out. The vehicle
    /// is let off the deck onto the map only where the drop that follows has that room from where the vehicle stands, so
    /// it is never left on the map, on no grid and not driven: it stays on its deck until it has room, then lands.
    /// </summary>
    [Test]
    public async Task AVehicleIsNeverLetOffTheDeckWithNowhereToLand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        try
        {
            // The lower floor now starts a tile past the edge of the deck.
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                foreach (var tile in Block(-8, EdgeX + 1, -8, 16))
                {
                    maps.SetTile(fall.LowerGrid, fall.Lower.Grid.Comp, tile, Tile.Empty);
                }
            });
            await server.WaitRunTicks(2);

            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));

            var landed = false;
            var stranded = string.Empty;
            for (var i = 0; i < 900 && !landed && stranded.Length == 0; i++)
            {
                await server.WaitRunTicks(1);

                await server.WaitPost(() =>
                {
                    var xform = entMan.GetComponent<TransformComponent>(fall.Vehicle);
                    landed = xform.MapID == fall.LowerMap;

                    if (!landed && xform.GridUid == null)
                        stranded = $"at {VehicleRigs.WorldPosition(fall.Rig)}";
                }
                );
            }

            Assert.That(stranded, Is.Empty, "The vehicle was left on the map with no grid under it, " + stranded);
            Assert.That(landed, Is.True, "The vehicle never found its way down to the lower floor.");

            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(fall.Vehicle);
                Assert.That(xform.GridUid, Is.EqualTo(fall.LowerGrid), "The vehicle did not land on the lower grid.");
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(fall.Vehicle).Operator, Is.EqualTo(fall.Rig.Driver), "The driver lost the wheel.");
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A parked vehicle that is fully supported is not measured again at every sweep: not until a tile changes near it.
    /// A tile that changes far away, as terrain does when it streams in, leaves it alone.
    /// </summary>
    [Test]
    public async Task AParkedSupportedVehicleIsNotSampledAgainUntilATileChanges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair, maxX: 40, startX: 10.5f);
        var server = pair.Server;
        var falling = server.System<SharedVehicleFallSystem>();

        try
        {
            // Sweeps run on game time, a quarter of a second apart, and a supported vehicle is checked regardless every two;
            // everything below stays inside those two seconds, so only a change to the floor can make a sweep measure again.
            var rate = server.ResolveDependency<IGameTiming>().TickRate;

            // The first sweeps after the levels were linked have measured it.
            await server.WaitRunTicks((int) Math.Ceiling(rate * 0.75));
            var baseline = 0;
            await server.WaitPost(() => baseline = falling.FootprintSamplings);

            // Half a second of sweeps.
            await server.WaitRunTicks((int) Math.Ceiling(rate * 0.5));
            await server.WaitAssertion(() =>
                Assert.That(falling.FootprintSamplings, Is.EqualTo(baseline), "A parked vehicle on solid floor was sampled again at every sweep."));

            // A tile far away, in another part of the map, is no reason to look again.
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                maps.SetTile(fall.UpperGrid, fall.Rig.Map.Grid.Comp, new Vector2i(30, 2), Tile.Empty);
            });
            await server.WaitRunTicks((int) Math.Ceiling(rate * 0.3));
            await server.WaitAssertion(() =>
                Assert.That(falling.FootprintSamplings, Is.EqualTo(baseline), "A tile that changed far from a parked vehicle made the sweep measure it again."));

            // One beside the hull is.
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                maps.SetTile(fall.UpperGrid, fall.Rig.Map.Grid.Comp, new Vector2i(11, 2), Tile.Empty);
            });
            await server.WaitRunTicks((int) Math.Ceiling(rate * 0.3));

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(falling.FootprintSamplings, Is.GreaterThan(baseline), "A change to the floor beside the vehicle did not make the sweep look again.");
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "A vehicle with floor under it fell.");
                }
            });
        }
        finally
        {
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hand-over the level below has no room for is refused at every step that moves the vehicle on its deck, so its
    /// answer is kept for half a second, as the answer for the tile ahead is: the search is not run again for each step.
    /// </summary>
    [Test]
    public async Task ARefusedHandOverIsNotSearchedAgainAtEveryStep()
    {
        await using var pair = await PoolManager.GetServerClient();
        var fall = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var falling = server.System<SharedVehicleFallSystem>();

        try
        {
            // Nothing below can take the vehicle.
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                foreach (var tile in Block(-8, 60, -8, 16))
                {
                    maps.SetTile(fall.LowerGrid, fall.Lower.Grid.Comp, tile, Tile.Empty);
                }
            });
            await server.WaitRunTicks(2);

            var rate = server.ResolveDependency<IGameTiming>().TickRate;
            var searches = 0;
            var allowed = false;

            await server.WaitPost(() =>
            {
                var before = falling.LandingSearches;
                for (var i = 0; i < 60; i++)
                {
                    allowed |= RaiseOpenTile(entMan, fall.Vehicle, fall.UpperGrid, new Vector2i(EdgeX, 2), handOver: true);
                }

                searches = falling.LandingSearches - before;
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(allowed, Is.False, "Test setup: a hand-over with nowhere to land should be refused.");
                Assert.That(searches, Is.LessThanOrEqualTo(1), "A refused hand-over searched the level below again at every step.");
            }

            // Once the answer has lapsed it is asked again.
            await server.WaitRunTicks((int) Math.Ceiling(rate * 0.75));
            await server.WaitPost(() =>
            {
                var before = falling.LandingSearches;
                RaiseOpenTile(entMan, fall.Vehicle, fall.UpperGrid, new Vector2i(EdgeX, 2), handOver: true);
                searches = falling.LandingSearches - before;
            });

            Assert.That(searches, Is.EqualTo(1), "A refused hand-over was never asked again.");
        }
        finally
        {
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }

    private static string DescribeExits(IEntityManager entMan, MapId interiorMap, EntityUid passenger)
    {
        var text = $"passenger at {entMan.GetComponent<TransformComponent>(passenger).LocalPosition} stunned {entMan.HasComponent<StunnedComponent>(passenger)} down {entMan.HasComponent<KnockedDownComponent>(passenger)};";
        var query = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (xform.MapID == interiorMap)
                text += $" exit {entMan.ToPrettyString(uid)} index {marker.EntryIndex} pending {marker.PendingExit} at {xform.LocalPosition};";
        }

        return text;
    }

    private static bool RaiseOpenTile(IEntityManager entMan, EntityUid vehicle, EntityUid grid, Vector2i tile, bool handOver = false)
    {
        var ev = new VehicleOpenTileAttemptEvent(grid, tile, HandOver: handOver);
        entMan.EventBus.RaiseLocalEvent(vehicle, ref ev);
        return ev.Allow;
    }

    /// <summary>
    /// Only the drive may take a vehicle over an edge. A vehicle blocked ahead by a wall that leaves only the edge as a way
    /// round must stop, not slip sideways off the deck.
    /// </summary>
    [Test]
    public async Task AVehicleBlockedAheadDoesNotSlideSidewaysOffAnEdge()
    {
        await using var pair = await PoolManager.GetServerClient();

        // The deck is six tiles wide, and a wall fills all of it at x 12, so the only way past is off the north edge.
        var fall = await Build(pair, maxX: 40, startY: 5.5f, walls: Block(12, 13, 0, 6));
        var server = pair.Server;

        try
        {
            await VehicleRigs.Seat(fall.Rig);
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.Right));
            await server.WaitRunTicks(300);

            await server.WaitAssertion(() =>
            {
                var position = VehicleRigs.LocalPosition(fall.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(MapOf(fall, fall.Vehicle), Is.EqualTo(fall.UpperMap), "The vehicle slid off the edge and dropped.");
                    Assert.That(position.Y + 0.425f, Is.LessThanOrEqualTo(6.01f), "The vehicle slid sideways over the edge of the deck.");
                    Assert.That(position.X + 1.2f, Is.LessThan(12f + 0.1f), "The vehicle got past the wall.");
                    // Hitting the wall at speed costs the frame a share; a fall would cost more.
                    Assert.That(FrameIntegrity(fall), Is.InRange(FrameMax * 0.5f, FrameMax));
                }
            });
        }
        finally
        {
            await server.WaitPost(() => VehicleRigs.Hold(fall.Rig, MoveButtons.None));
            await Teardown(fall);
        }

        await pair.CleanReturnAsync();
    }
}
