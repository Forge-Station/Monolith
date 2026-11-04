#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Server._WF.Vehicles.Npc;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Scanner;
using Content.Shared._WF.Vehicles.Viewport;
using Content.Shared.Actions;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Standing;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>Entering, leaving, locking and driving a vehicle whose interior is the small map in Maps/_WF/Vehicles/Interiors.</summary>
[TestFixture]
public sealed class VehicleInteriorTest
{
    private const string Chassis = "WFInteriorTestVehicle";
    private const string OneDoorChassis = "WFInteriorTestOneDoorVehicle";

    // The chassis sits at this spot of the grid; its doorways are a metre to its left and right.
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);
    private static readonly Vector2[] DoorwayOffsets = { new(1f, 0f), new(-1f, 0f) };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFVehicleTestChassis
  id: WFInteriorTestVehicle
  components:
  - type: VehicleLock
    breakDelay: 1
    repairDelay: 1
  - type: VehicleEnter
    interiorPath: /Maps/_WF/Vehicles/Interiors/wf_test_interior.yml
    maxPassengers: 2
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
  parent: WFInteriorTestVehicle
  id: WFInteriorTestOneDoorVehicle
  components:
  - type: VehicleEnter
    entryPoints:
    - offset: 1,0
      radius: 0.6
      interiorCoords: 1.5,1.5

- type: entity
  parent: BaseItem
  id: WFInteriorTestPrimedGrenade
  components:
  - type: OnUseTimerTrigger
    delay: 100
  - type: ActiveTimerTrigger
    timeRemaining: 100

- type: entity
  id: WFInteriorTestViewport
  components:
  - type: Transform
  - type: VehicleViewport
";

    private sealed class Rig
    {
        public required TestMapData Map;
        public EntityUid Vehicle;

        /// <summary>The map itself when it is the ground grid, as planet terrain is; otherwise the test grid.</summary>
        public EntityUid? PlanetGround;

        public EntityUid Ground => PlanetGround ?? Map.Grid.Owner;
    }

    /// <summary>Records the <see cref="VehicleInteriorLoadedEvent"/>s the server raises and can refuse every driver seat.</summary>
    public sealed class InteriorProbeSystem : EntitySystem
    {
        public readonly List<VehicleInteriorLoadedEvent> Loaded = new();
        public bool RefuseDriverSeats;

        public override void Initialize()
        {
            SubscribeLocalEvent<VehicleInteriorLoadedEvent>(OnLoaded);
            SubscribeLocalEvent<VehicleDriverSeatComponent, VehicleSeatAttemptEvent>(OnSeatAttempt);
        }

        private void OnLoaded(ref VehicleInteriorLoadedEvent ev)
        {
            Loaded.Add(ev);
        }

        private void OnSeatAttempt(Entity<VehicleDriverSeatComponent> ent, ref VehicleSeatAttemptEvent args)
        {
            if (RefuseDriverSeats)
                args.Cancelled = true;
        }
    }

    /// <summary>A block of plating around the origin (or the tiles of it that <paramref name="floor"/> keeps) with the test chassis in the middle of it.</summary>
    private static async Task<Rig> Build(TestPair pair, string proto = Chassis, Func<Vector2i, bool>? floor = null)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map };

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -4; x < 16; x++)
            {
                for (var y = -4; y < 16; y++)
                {
                    var tile = new Vector2i(x, y);
                    if (floor == null || floor(tile))
                        maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, new Tile(plating));
                }
            }

            rig.Vehicle = server.EntMan.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, VehicleSpot));
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static EntityUid SpawnHuman(TestPair pair, Rig rig, Vector2 gridPosition)
    {
        return pair.Server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Ground, gridPosition));
    }

    private static Vector2 Doorway(int index) => VehicleSpot + DoorwayOffsets[index];

    private static bool Activate(TestPair pair, EntityUid user, EntityUid target)
    {
        return pair.Server.System<SharedInteractionSystem>().InteractionActivate(user, target);
    }

    private static MapId MapOf(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(uid).MapID;
    }

    private static VehicleInteriorComponent Interior(TestPair pair, Rig rig)
    {
        return pair.Server.EntMan.GetComponent<VehicleInteriorComponent>(rig.Vehicle);
    }

    private static EntityUid ExitMarker(TestPair pair, Rig rig, int entryIndex)
    {
        var entMan = pair.Server.EntMan;
        var mapId = Interior(pair, rig).MapId;
        var query = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var exit, out var xform))
        {
            if (xform.MapID == mapId && exit.EntryIndex == entryIndex)
                return uid;
        }

        Assert.Fail($"No exit marker for doorway {entryIndex} in the interior.");
        return default;
    }

    private static EntityUid DriverSeat(TestPair pair, Rig rig)
    {
        var entMan = pair.Server.EntMan;
        var mapId = Interior(pair, rig).MapId;
        var query = entMan.EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId)
                return uid;
        }

        Assert.Fail("No driver seat in the interior.");
        return default;
    }

    /// <summary>Puts <paramref name="user"/> at a doorway, activates the vehicle and waits out the do-after.</summary>
    private static async Task Enter(TestPair pair, Rig rig, EntityUid user, int doorway)
    {
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var xform = server.System<SharedTransformSystem>();
            xform.SetCoordinates(user, new EntityCoordinates(rig.Ground, Doorway(doorway)));
        });
        await server.WaitRunTicks(2);

        await server.WaitPost(() => Activate(pair, user, rig.Vehicle));
        await server.WaitRunTicks(30);
    }

    /// <summary>Activates the exit marker of a doorway and waits out the do-after.</summary>
    private static async Task Leave(TestPair pair, Rig rig, EntityUid user, int doorway)
    {
        var server = pair.Server;
        var marker = EntityUid.Invalid;

        await server.WaitPost(() => marker = ExitMarker(pair, rig, doorway));
        await server.WaitPost(() => Activate(pair, user, marker));
        await server.WaitRunTicks(30);
    }

    private static EntityUid FindAction(TestPair pair, EntityUid user, string prototype)
    {
        var entMan = pair.Server.EntMan;
        foreach (var action in entMan.GetComponent<ActionsComponent>(user).Actions)
        {
            if (entMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == prototype)
                return action;
        }

        Assert.Fail($"The user has no {prototype} action.");
        return default;
    }

    private static void PerformAction(TestPair pair, EntityUid user, EntityUid action)
    {
        var actions = pair.Server.System<SharedActionsSystem>();
        Assert.That(actions.TryGetActionData(action, out var data), Is.True);
        actions.PerformAction(user, null, action, data!, data!.BaseEvent, pair.Server.Resolve<IGameTiming>().CurTime, predicted: false);
    }

    private static VehicleLockComponent LockOf(TestPair pair, Rig rig)
    {
        return pair.Server.EntMan.GetComponent<VehicleLockComponent>(rig.Vehicle);
    }

    /// <summary>Uses a key on the vehicle: the first use of a blank one imprints it, each later use flips the lock.</summary>
    private static void UseKey(TestPair pair, Rig rig, EntityUid key, EntityUid user)
    {
        var locks = pair.Server.System<VehicleLockSystem>();
        var keyComp = pair.Server.EntMan.GetComponent<VehicleKeyComponent>(key);
        locks.TryUseKeyOnVehicle((key, keyComp), rig.Vehicle, user);
    }

    /// <summary>Walking in through each doorway and back out through the matching exit lands the user where they stood.</summary>
    [Test]
    [TestCase(0)]
    [TestCase(1)]
    public async Task EnterAndExitRoundTrip(int doorway)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(doorway)));

        await Enter(pair, rig, user, doorway);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);
            var link = entMan.GetComponent<VehicleInteriorLinkComponent>(interior.Map);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, user), Is.EqualTo(interior.MapId), "The user did not reach the interior map.");
                Assert.That(MapOf(pair, user), Is.Not.EqualTo(rig.Map.MapId));
                Assert.That(link.Vehicle, Is.EqualTo(rig.Vehicle), "The interior map does not point back at the vehicle.");
                Assert.That(interior.Passengers, Does.Contain(user));
                Assert.That(entMan.GetComponent<VehicleInteriorOccupantComponent>(user).Vehicle, Is.EqualTo(rig.Vehicle));
                Assert.That(entMan.GetComponent<TransformComponent>(user).ParentUid, Is.EqualTo(interior.Grid), "The user is not on the interior grid.");
            }
        });

        await Leave(pair, rig, user, doorway);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);
            var xform = entMan.GetComponent<TransformComponent>(user);
            var expected = Doorway(doorway);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user did not come back out.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
                Assert.That(Vector2.Distance(xform.LocalPosition, expected), Is.LessThan(0.2f), "The user did not exit at the doorway they used.");
                Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.False);
                Assert.That(interior.Passengers, Does.Not.Contain(user));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The doorway test again on a turned grid that moves between the entry and the exit, as a ship deck does.</summary>
    [Test]
    public async Task EnterAndExitOnAMovingRotatedGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map };
        var user = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var xform = server.System<SharedTransformSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -4; x < 16; x++)
            {
                for (var y = -4; y < 16; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            xform.SetLocalPositionRotation(map.Grid.Owner, new Vector2(30f, -12f), Angle.FromDegrees(90));

            rig.Vehicle = entMan.SpawnEntity(Chassis, new EntityCoordinates(map.Grid.Owner, VehicleSpot));
            user = SpawnHuman(pair, rig, Doorway(0));
        });
        await server.WaitRunTicks(5);

        // The user has turned their view outside; coming in turns it to the interior's north.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(user).TargetRelativeRotation = Angle.FromDegrees(90));

        await Enter(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "The user did not get in through the doorway on a turned grid.");

            var mover = entMan.GetComponent<InputMoverComponent>(user);
            Assert.That(mover.TargetRelativeRotation, Is.EqualTo(Angle.Zero), "Coming in did not turn the view to north.");
            Assert.That(entMan.GetComponent<TransformComponent>(mover.RelativeEntity!.Value).MapID, Is.EqualTo(Interior(pair, rig).MapId),
                "The view is not held to the interior.");

            // The ship moves and turns while the user is inside.
            var xform = server.System<SharedTransformSystem>();
            xform.SetLocalPositionRotation(map.Grid.Owner, new Vector2(-18f, 25f), Angle.FromDegrees(200));
        });
        await server.WaitRunTicks(2);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(map.MapId));
                Assert.That(xform.ParentUid, Is.EqualTo(map.Grid.Owner));
                Assert.That(Vector2.Distance(xform.LocalPosition, Doorway(0)), Is.LessThan(0.2f), "The exit did not follow the grid.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone pulled by the user comes in and goes out with them and is still pulled afterwards.</summary>
    [Test]
    public async Task PulledEntityComesAlong()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid puller = default, pulled = default;
        await server.WaitPost(() =>
        {
            puller = SpawnHuman(pair, rig, Doorway(0));
            pulled = SpawnHuman(pair, rig, Doorway(0) + new Vector2(0f, 0.9f));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<PullingSystem>().TryStartPull(puller, pulled), Is.True, "The pull did not start.");
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, puller, 0);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, puller), Is.EqualTo(interior.MapId));
                Assert.That(MapOf(pair, pulled), Is.EqualTo(interior.MapId), "The pulled entity was left outside.");
                Assert.That(interior.Passengers, Does.Contain(pulled));
                Assert.That(entMan.GetComponent<PullerComponent>(puller).Pulling, Is.EqualTo(pulled), "The pull did not survive going in.");
                Assert.That(entMan.GetComponent<PullableComponent>(pulled).Puller, Is.EqualTo(puller));
            }
        });

        await Leave(pair, rig, puller, 0);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, puller), Is.EqualTo(rig.Map.MapId));
                Assert.That(MapOf(pair, pulled), Is.EqualTo(rig.Map.MapId), "The pulled entity was left inside.");
                Assert.That(interior.Passengers, Does.Not.Contain(pulled));
                Assert.That(entMan.GetComponent<PullerComponent>(puller).Pulling, Is.EqualTo(pulled), "The pull did not survive coming out.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The third person to try gets refused until someone leaves.</summary>
    [Test]
    public async Task CapacityLimitRefusesTheThirdPassenger()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;

        EntityUid first = default, second = default, third = default;
        await server.WaitPost(() =>
        {
            first = SpawnHuman(pair, rig, Doorway(0));
            second = SpawnHuman(pair, rig, Doorway(0));
            third = SpawnHuman(pair, rig, Doorway(0));
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, first, 0);
        await Enter(pair, rig, second, 0);
        await Enter(pair, rig, third, 0);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, first), Is.EqualTo(interior.MapId));
                Assert.That(MapOf(pair, second), Is.EqualTo(interior.MapId));
                Assert.That(MapOf(pair, third), Is.EqualTo(rig.Map.MapId), "A third passenger got into a vehicle that holds two.");
                Assert.That(interior.Passengers, Has.Count.EqualTo(2));
            }
        });

        await Leave(pair, rig, first, 0);
        await Enter(pair, rig, third, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, third), Is.EqualTo(Interior(pair, rig).MapId), "The freed place was not usable.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A locked vehicle refuses people at the door and refuses to let them out.</summary>
    [Test]
    public async Task LockBlocksEntryAndExit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid user = default, key = default;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            key = entMan.SpawnEntity("RMCVehicleKeyBlank", new EntityCoordinates(rig.Map.Grid.Owner, 2f, 2f));

            // The first use imprints the blank key, the second locks the vehicle.
            UseKey(pair, rig, key, user);
            UseKey(pair, rig, key, user);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() => Assert.That(LockOf(pair, rig).Locked, Is.True, "The key did not lock the vehicle."));

        await Enter(pair, rig, user, 0);
        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, user), Is.EqualTo(rig.Map.MapId), "A locked vehicle let someone in.");
            UseKey(pair, rig, key, user);
        });

        await Enter(pair, rig, user, 0);
        await server.WaitAssertion(() =>
        {
            Assert.That(LockOf(pair, rig).Locked, Is.False);
            Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "The unlocked vehicle refused someone.");
            UseKey(pair, rig, key, user);
        });

        await Leave(pair, rig, user, 0);
        await server.WaitAssertion(() =>
        {
            Assert.That(LockOf(pair, rig).Locked, Is.True);
            Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "A locked vehicle let someone out.");
            UseKey(pair, rig, key, user);
        });

        await Leave(pair, rig, user, 0);
        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, user), Is.EqualTo(rig.Map.MapId), "The unlocked vehicle kept someone in.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Wire cutters break a lock open and a screwdriver mends it; a broken lock cannot be locked with a key.</summary>
    [Test]
    public async Task CuttingTheLockBreaksItAndAScrewdriverRepairsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid user = default, key = default, cutters = default, screwdriver = default;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            key = entMan.SpawnEntity("RMCVehicleKeyBlank", new EntityCoordinates(rig.Map.Grid.Owner, 2f, 2f));
            cutters = entMan.SpawnEntity("Wirecutter", new EntityCoordinates(rig.Map.Grid.Owner, 2f, 3f));
            screwdriver = entMan.SpawnEntity("Screwdriver", new EntityCoordinates(rig.Map.Grid.Owner, 3f, 3f));

            UseKey(pair, rig, key, user);
            UseKey(pair, rig, key, user);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(LockOf(pair, rig).Locked, Is.True);

            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, cutters), Is.True);
            Assert.That(hands.TryPickupAnyHand(user, screwdriver), Is.True);

            server.System<SharedInteractionSystem>().InteractUsing(user, cutters, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LockOf(pair, rig).Broken, Is.True, "The cutters did not break the lock.");
                Assert.That(LockOf(pair, rig).Locked, Is.False, "A broken lock is still locked.");
            }

            UseKey(pair, rig, key, user);
            Assert.That(LockOf(pair, rig).Locked, Is.False, "A key locked a broken lock.");

            server.System<SharedInteractionSystem>().InteractUsing(user, screwdriver, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(LockOf(pair, rig).Broken, Is.False, "The screwdriver did not mend the lock.");

            UseKey(pair, rig, key, user);
            Assert.That(LockOf(pair, rig).Locked, Is.True, "A mended lock would not lock.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// With someone inside, the vehicle takes their factions and NPCs hostile to them go for it; empty, it belongs to
    /// nobody and nothing targets it.
    /// </summary>
    [Test]
    public async Task HostileNpcsTargetAnOccupiedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var factions = server.System<NpcFactionSystem>();

        EntityUid user = default, hostile = default;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            hostile = SpawnHuman(pair, rig, VehicleSpot + new Vector2(0f, 3f));
            factions.ClearFactions(hostile, false);
            factions.AddFaction(hostile, "SimpleHostile");
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "Test setup: the user did not get in.");
                Assert.That(entMan.HasComponent<WFOccupiedVehicleComponent>(rig.Vehicle), Is.True, "The occupied vehicle is not a live target.");
                Assert.That(factions.IsMember(rig.Vehicle, "NanoTrasen"), Is.True, "The vehicle did not take its occupant's faction.");
                Assert.That(factions.GetNearbyHostiles(hostile, 10f), Does.Contain(rig.Vehicle), "A hostile NPC does not see the vehicle as a target.");
            }
        });

        await Leave(pair, rig, user, 0);
        await pair.RunTicksSync(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOccupiedVehicleComponent>(rig.Vehicle), Is.False, "The empty vehicle is still a live target.");
                Assert.That(factions.IsMember(rig.Vehicle, "NanoTrasen"), Is.False, "The empty vehicle kept its occupant's faction.");
                Assert.That(factions.GetNearbyHostiles(hostile, 10f), Does.Not.Contain(rig.Vehicle));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Buckling into the driver seat makes the user the operator and gives them the lock and view actions; unbuckling clears it.</summary>
    [Test]
    public async Task DriverSeatSetsAndClearsTheOperator()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);

        var seat = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            seat = DriverSeat(pair, rig);
            var xform = server.System<SharedTransformSystem>();
            xform.SetCoordinates(user, new EntityCoordinates(Interior(pair, rig).Grid, 3.5f, 1.5f));
        });
        await server.WaitRunTicks(3);

        var probe = server.System<InteriorProbeSystem>();
        await server.WaitAssertion(() =>
        {
            probe.RefuseDriverSeats = true;
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.False, "A refused seat still took the user.");
            probe.RefuseDriverSeats = false;
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.True, "The user could not strap into the driver seat.");
        });
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var vehicle = entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vehicle.Operator, Is.EqualTo(user), "Buckling in did not make the user the operator.");
                Assert.That(entMan.GetComponent<RMCVehicleOperatorComponent>(user).Vehicle, Is.EqualTo(rig.Vehicle));
                Assert.That(entMan.GetComponent<VehicleWatchingComponent>(user).Watching, Is.EqualTo(rig.Vehicle));
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle), "The driver's eye is not on the vehicle.");
                Assert.That(entMan.HasComponent<VehicleLockActionComponent>(user), Is.True, "The driver has no lock action.");
                Assert.That(entMan.HasComponent<VehicleViewToggleComponent>(user), Is.True, "The driver has no view toggle.");
            }

            // The seat's scanner: an action for the driver, looking out from the vehicle.
            FindAction(pair, user, "WFActionVehicleScanner");
            var origin = new WFRadarOriginEvent();
            entMan.EventBus.RaiseLocalEvent(seat, ref origin);
            Assert.That(origin.Origin, Is.EqualTo(rig.Vehicle), "The driver's scanner does not look out from the vehicle.");
        });

        // The lock action drives the same lock the keys use; the view action swaps the driver's eye.
        await server.WaitAssertion(() =>
        {
            var lockAction = FindAction(pair, user, "ActionVehicleLock");
            var viewAction = FindAction(pair, user, "ActionVehicleToggleView");

            PerformAction(pair, user, lockAction);
            Assert.That(LockOf(pair, rig).Locked, Is.True, "The lock action did not lock the vehicle.");

            PerformAction(pair, user, lockAction);
            Assert.That(LockOf(pair, rig).Locked, Is.False, "The lock action did not unlock the vehicle.");

            PerformAction(pair, user, viewAction);
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The view action did not bring the eye inside.");

            PerformAction(pair, user, viewAction);
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle), "The view action did not put the eye back outside.");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryUnbuckle(user, user, true), Is.True, "The user could not unstrap.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var vehicle = entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vehicle.Operator, Is.Null, "Unbuckling did not clear the operator.");
                Assert.That(entMan.HasComponent<RMCVehicleOperatorComponent>(user), Is.False);
                Assert.That(entMan.HasComponent<VehicleLockActionComponent>(user), Is.False);
                Assert.That(entMan.HasComponent<VehicleViewToggleComponent>(user), Is.False);
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The driver's eye is still on the vehicle.");
                Assert.That(entMan.GetComponent<ActionsComponent>(user).Actions
                    .Any(action => entMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "WFActionVehicleScanner"),
                    Is.False, "The scanner action stayed with someone who got up.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Looking out of a viewport puts the eye on the vehicle, and the view action then swaps it to and fro.</summary>
    [Test]
    public async Task ViewportShowsTheOutsideAndTheViewActionSwapsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);

        var viewport = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            viewport = entMan.SpawnEntity("WFInteriorTestViewport", new EntityCoordinates(Interior(pair, rig).Grid, 1.5f, 2.5f));
            Assert.That(Activate(pair, user, viewport), Is.True, "The viewport did not react to being used.");
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle), "Looking through the viewport did not show the outside.");
                Assert.That(entMan.HasComponent<VehicleViewportUserComponent>(user), Is.True);
                Assert.That(entMan.GetComponent<VehicleWatchingComponent>(user).Watching, Is.EqualTo(rig.Vehicle));
            }

            var viewAction = FindAction(pair, user, "ActionVehicleToggleView");
            PerformAction(pair, user, viewAction);
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The view action did not bring the eye back inside.");

            PerformAction(pair, user, viewAction);
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle));
        });

        await server.WaitPost(() => Assert.That(Activate(pair, user, viewport), Is.True));
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "Using the viewport again did not close it.");
                Assert.That(entMan.HasComponent<VehicleViewportUserComponent>(user), Is.False);
                Assert.That(entMan.HasComponent<VehicleViewToggleComponent>(user), Is.False, "The view action outlived the viewport.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A primed timer item used on a doorway ends up on the interior floor instead of in the user's hand.</summary>
    [Test]
    public async Task PrimedGrenadeIsDroppedThroughTheDoorway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid user = default, grenade = default;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            grenade = entMan.SpawnEntity("WFInteriorTestPrimedGrenade", new EntityCoordinates(rig.Map.Grid.Owner, Doorway(0)));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, grenade), Is.True, "The user could not pick the grenade up.");

            var interaction = server.System<SharedInteractionSystem>();
            interaction.InteractUsing(user, grenade, rig.Vehicle, new EntityCoordinates(rig.Vehicle, Vector2.Zero));

            var interior = Interior(pair, rig);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, grenade), Is.EqualTo(interior.MapId), "The grenade did not go through the doorway.");
                Assert.That(entMan.GetComponent<TransformComponent>(grenade).ParentUid, Is.EqualTo(interior.Grid));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An interior whose grid was deleted is replaced on the next entry, and whoever was still in it is put back outside.</summary>
    [Test]
    public async Task InteriorWithoutAGridIsReloaded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = server.System<InteriorProbeSystem>();

        await server.WaitPost(() => probe.Loaded.Clear());

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            first = SpawnHuman(pair, rig, Doorway(0));
            second = SpawnHuman(pair, rig, Doorway(1));
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, first, 0);

        MapId oldMap = default;
        EntityUid oldGrid = default;
        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);
            oldMap = interior.MapId;
            oldGrid = interior.Grid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(probe.Loaded, Has.Count.EqualTo(1), "Loading the interior did not raise the event once.");
                Assert.That(probe.Loaded[0].Vehicle, Is.EqualTo(rig.Vehicle));
                Assert.That(probe.Loaded[0].Grid, Is.EqualTo(interior.Grid));
                Assert.That(probe.Loaded[0].MapUid, Is.EqualTo(interior.Map));
            }

            // A grid takes its children with it, so the occupant is moved onto the bare map first.
            server.System<SharedTransformSystem>().SetCoordinates(first, new EntityCoordinates(interior.Map, Vector2.Zero));
            entMan.DeleteEntity(oldGrid);
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, second, 1);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);
            var maps = server.System<SharedMapSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(probe.Loaded, Has.Count.EqualTo(2), "The interior was not loaded again.");
                Assert.That(interior.MapId, Is.Not.EqualTo(oldMap));
                Assert.That(maps.MapExists(oldMap), Is.False, "The old interior map was left behind.");
                Assert.That(entMan.EntityExists(interior.Grid), Is.True);
                Assert.That(MapOf(pair, second), Is.EqualTo(interior.MapId), "The newcomer is not in the new interior.");
                Assert.That(MapOf(pair, first), Is.EqualTo(rig.Map.MapId), "The old occupant was not put back outside.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static Vector2i TileOf(Vector2 position)
    {
        return new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
    }

    /// <summary>Puts a wall on the tile at <paramref name="tile"/> and returns its entity.</summary>
    private static EntityUid SpawnWall(TestPair pair, Rig rig, Vector2i tile)
    {
        return pair.Server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(rig.Ground, tile.X + 0.5f, tile.Y + 0.5f));
    }

    private static Vector2 LocalPosition(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(uid).LocalPosition;
    }

    /// <summary>With a wall or another vehicle in the doorway the user is let out at the vehicle's other doorway.</summary>
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExitThroughABlockedDoorwayUsesTheOtherDoorway(bool blockWithAVehicle)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(1)));
        await Enter(pair, rig, user, 1);

        await server.WaitPost(() =>
        {
            if (blockWithAVehicle)
                entMan.SpawnEntity(Chassis, new EntityCoordinates(rig.Map.Grid.Owner, Doorway(0)));
            else
                SpawnWall(pair, rig, TileOf(Doorway(0)));
        });
        await server.WaitRunTicks(3);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "A blocked doorway kept the user in.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
                Assert.That(Vector2.Distance(xform.LocalPosition, Doorway(1)), Is.LessThan(0.2f), "The user did not come out of the free doorway.");
                Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The tiles of the bay around the chassis: a wall on every tile touching the hull, and the ring beyond them.</summary>
    private static IEnumerable<Vector2i> BayWalls()
    {
        // The hull covers x = 5 and y = 4 to 6.
        for (var x = 4; x <= 6; x++)
        {
            for (var y = 3; y <= 7; y++)
            {
                if (x == 5 && y is >= 4 and <= 6)
                    continue;

                yield return new Vector2i(x, y);
            }
        }
    }

    /// <summary>The walls of a sealed room east of the bay, sharing the bay's east wall (x = 6). Its floor is x 7 to 9 and y 4 to 6.</summary>
    private static IEnumerable<Vector2i> RoomWalls()
    {
        for (var x = 7; x <= 10; x++)
        {
            yield return new Vector2i(x, 3);
            yield return new Vector2i(x, 7);
        }

        for (var y = 4; y <= 6; y++)
        {
            yield return new Vector2i(10, y);
        }
    }

    private static bool InRoom(Vector2 position)
    {
        return position.X is > 7f and < 10f && position.Y is > 4f and < 7f;
    }

    /// <summary>Puts a user through the first doorway, then walls the given tiles (a closed door at <paramref name="door"/>), so nothing is built on the user.</summary>
    private static async Task<(EntityUid User, Dictionary<Vector2i, EntityUid> Walls)> EnterThenWall(
        TestPair pair,
        Rig rig,
        IEnumerable<Vector2i> tiles,
        Vector2i? door = null)
    {
        var server = pair.Server;
        var user = EntityUid.Invalid;
        var walls = new Dictionary<Vector2i, EntityUid>();
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);

        await server.WaitPost(() =>
        {
            foreach (var tile in tiles)
            {
                walls[tile] = tile == door
                    ? server.EntMan.SpawnEntity("Airlock", new EntityCoordinates(rig.Ground, tile.X + 0.5f, tile.Y + 0.5f))
                    : SpawnWall(pair, rig, tile);
            }
        });
        await server.WaitRunTicks(3);

        return (user, walls);
    }

    /// <summary>
    /// A vehicle with a wall on every tile around it keeps its user inside: the free floor behind the wall is no place to put
    /// them. The exit works as soon as one way out opens, onto the tile that touches the hull and never behind the wall.
    /// </summary>
    [Test]
    public async Task ExitFromAVehicleWalledInOnEverySideKeepsTheUserInsideUntilAWayOpens()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var (user, walls) = await EnterThenWall(pair, rig, BayWalls());

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "A vehicle walled in on every side put its user behind the wall.");
                Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.True);
                Assert.That(entMan.GetComponent<VehicleExitComponent>(ExitMarker(pair, rig, 0)).PendingExit, Is.False, "The refused exit left its marker held.");
            }
        });

        // A way out opens at the north end of the hull.
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(walls[new Vector2i(5, 7)]);
            walls.Remove(new Vector2i(5, 7));
        });
        await server.WaitRunTicks(3);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave through the one open side.");
                Assert.That(walls.Keys, Does.Not.Contain(TileOf(xform.LocalPosition)), "The user was put in a wall.");
                Assert.That(Vector2.Distance(xform.LocalPosition, new Vector2(5.5f, 7.5f)), Is.LessThan(0.2f),
                    "The user was not put on the one tile that touches the hull.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle against the outside wall of a sealed room never lets its user out inside the room, whether the doorways are walls or a closed door.</summary>
    [Test]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ExitNeverLandsInASealedRoomBesideTheVehicle(bool aClosedDoor, bool anOpenSide)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        // The bay's east wall is the room's west wall; the east doorway of the chassis opens on it.
        var bay = new HashSet<Vector2i>(BayWalls());
        if (anOpenSide)
            bay.Remove(new Vector2i(5, 7));

        var door = new Vector2i(6, 5);
        var (user, _) = await EnterThenWall(pair, rig, bay.Concat(RoomWalls()), aClosedDoor ? door : null);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            if (aClosedDoor)
            {
                var closed = 0;
                var doors = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
                while (doors.MoveNext(out _, out var doorComp, out var doorXform))
                {
                    if (doorXform.MapID == rig.Map.MapId && doorComp.State == DoorState.Closed)
                        closed++;
                }

                Assert.That(closed, Is.EqualTo(1), "Test setup: the door in the bay's east wall should be closed.");
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(InRoom(xform.LocalPosition) && xform.MapID == rig.Map.MapId, Is.False, "The user was put inside the sealed room.");

                if (anOpenSide)
                {
                    Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave by the open side.");
                    Assert.That(Vector2.Distance(xform.LocalPosition, new Vector2(5.5f, 7.5f)), Is.LessThan(0.2f), "The user was not put by the hull on the open side.");
                }
                else
                {
                    Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "A sealed vehicle let its user out on the far side of a wall.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A free tile that touches the hull but is cut off from it by walls on both sides, a closet in the corner, is not a way
    /// out. The tile that can be walked to is used even though it is farther from the doorway the user chose.
    /// </summary>
    [Test]
    public async Task ExitPrefersAReachableTileToACutOffPocketNearerTheDoorway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        // Every tile touching the hull is wall but (5, 7), and (6, 3) next to the doorway's wall is a sealed closet.
        var bay = new HashSet<Vector2i>(BayWalls());
        bay.Remove(new Vector2i(5, 7));
        bay.Add(new Vector2i(6, 7));
        bay.Remove(new Vector2i(6, 3));
        bay.Add(new Vector2i(7, 3));
        bay.Add(new Vector2i(6, 2));

        var (user, _) = await EnterThenWall(pair, rig, bay);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave.");
                Assert.That(TileOf(xform.LocalPosition), Is.Not.EqualTo(new Vector2i(6, 3)), "The user was put in a closet that can only be reached through the walls.");
                Assert.That(Vector2.Distance(xform.LocalPosition, new Vector2(5.5f, 7.5f)), Is.LessThan(0.2f), "The user was not put on the tile that can be walked to.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Whoever the user pulls follows the same rule: both stay inside while the vehicle is walled in, and both are put where
    /// the user is, never behind a wall, once a way out opens.
    /// </summary>
    [Test]
    public async Task APulledPersonIsNotPutBehindTheWallEither()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid puller = default, pulled = default;
        await server.WaitPost(() =>
        {
            puller = SpawnHuman(pair, rig, Doorway(0));
            pulled = SpawnHuman(pair, rig, Doorway(0) + new Vector2(0f, 0.9f));
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() => Assert.That(server.System<PullingSystem>().TryStartPull(puller, pulled), Is.True, "The pull did not start."));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, puller, 0);

        var bay = new HashSet<Vector2i>(BayWalls().Concat(RoomWalls()));
        var walls = new Dictionary<Vector2i, EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var wall in bay)
            {
                walls[wall] = SpawnWall(pair, rig, wall);
            }
        });
        await server.WaitRunTicks(3);

        await Leave(pair, rig, puller, 0);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, puller), Is.EqualTo(Interior(pair, rig).MapId), "A walled in vehicle put its user behind the wall.");
                Assert.That(MapOf(pair, pulled), Is.EqualTo(Interior(pair, rig).MapId), "A walled in vehicle put the person its user pulls behind the wall.");
            }
        });

        // A way out opens at the north end of the hull.
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(walls[new Vector2i(5, 7)]);
            bay.Remove(new Vector2i(5, 7));
        });
        await server.WaitRunTicks(3);

        await Leave(pair, rig, puller, 0);

        await server.WaitAssertion(() =>
        {
            var one = entMan.GetComponent<TransformComponent>(puller);
            var other = entMan.GetComponent<TransformComponent>(pulled);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(one.MapID, Is.EqualTo(rig.Map.MapId));
                Assert.That(other.MapID, Is.EqualTo(rig.Map.MapId), "The pulled person was left inside.");
                Assert.That(InRoom(one.LocalPosition) || InRoom(other.LocalPosition), Is.False, "Someone was put in the sealed room.");
                Assert.That(bay, Does.Not.Contain(TileOf(one.LocalPosition)));
                Assert.That(bay, Does.Not.Contain(TileOf(other.LocalPosition)));
                Assert.That(Vector2.Distance(other.LocalPosition, one.LocalPosition), Is.LessThan(1.5f), "The pulled person was not put by the puller.");
                Assert.That(entMan.GetComponent<PullerComponent>(puller).Pulling, Is.EqualTo(pulled), "The pull did not survive coming out.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A planet: the map is itself the ground grid, plated around the origin, with the chassis on it and no other grid.</summary>
    private static async Task<Rig> BuildPlanet(TestPair pair, string proto = OneDoorChassis)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map, PlanetGround = map.MapUid };

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            var ground = entMan.EnsureComponent<MapGridComponent>(map.MapUid);

            for (var x = -4; x < 16; x++)
            {
                for (var y = -4; y < 16; y++)
                {
                    maps.SetTile(map.MapUid, ground, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Vehicle = entMan.SpawnEntity(proto, new EntityCoordinates(map.MapUid, VehicleSpot));
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    /// <summary>The footprint of the landed ship in the world: it is laid from (6, 3), five tiles wide and four high.</summary>
    private static readonly Box2 ShipFootprint = new(6f, 3f, 11f, 7f);

    /// <summary>Lands a ship over the planet beside the chassis: plated everywhere, with a wall on its outer ring when asked.</summary>
    private static async Task<EntityUid> LandShip(TestPair pair, Rig rig, bool withWalls)
    {
        var server = pair.Server;
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                tiles.Add(new Vector2i(x, y));
            }
        }

        var ship = await VehicleRigs.AddGrid(new VehicleRig { Pair = pair, Map = rig.Map }, new Vector2(6f, 3f), 0f, tiles);

        if (withWalls)
        {
            await server.WaitPost(() =>
            {
                foreach (var tile in tiles)
                {
                    if (tile.X is 0 or 4 || tile.Y is 0 or 3)
                        server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(ship, tile.X + 0.5f, tile.Y + 0.5f));
                }
            });
            await server.WaitRunTicks(3);
        }

        return ship;
    }

    /// <summary>
    /// On planet ground a ship lies as a grid over the ground's own, so the ground has a tile under the ship's hull and
    /// floor. An exit toward the ship never lands under its wall or on its floor while the ground beside the vehicle is free.
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task ExitOnAPlanetNeverLandsUnderALandedShip(bool shipHasWalls)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildPlanet(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);

        var ship = await LandShip(pair, rig, shipHasWalls);
        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);
            var world = server.System<SharedTransformSystem>().GetWorldPosition(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave.");
                Assert.That(ShipFootprint.Contains(world), Is.False, $"The user was put under the ship, at {world}.");
                Assert.That(xform.ParentUid, Is.Not.EqualTo(ship), "The user was put on the ship's grid.");
                Assert.That(entMan.HasComponent<VehicleInteriorOccupantComponent>(user), Is.False);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A vehicle on planet ground with the ship's wall on one side and ground walls on the others has no way out: the ground
    /// tiles under the ship's interior are not the vehicle's to put someone on.
    /// </summary>
    [Test]
    public async Task ExitOnAPlanetWalledInAgainstAShipKeepsTheUserInside()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildPlanet(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);

        // The ground walls close the north, the south and the west; the east is the ship's wall, a tile away from the hull.
        await server.WaitPost(() =>
        {
            foreach (var wall in BayWalls().Where(t => t.X != 6))
            {
                SpawnWall(pair, rig, wall);
            }
        });
        var ship = await LandShip(pair, rig, true);
        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "A vehicle walled in against a ship put its user under the ship.");
                Assert.That(entMan.EntityExists(ship), Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With the doorways taken by a wall and by another vehicle, the user lands on free floor that is neither.</summary>
    [Test]
    public async Task ExitNeverLandsInAWallOrInsideAnotherVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);

        var other = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            other = entMan.SpawnEntity(Chassis, new EntityCoordinates(rig.Map.Grid.Owner, Doorway(0)));
            SpawnWall(pair, rig, TileOf(Doorway(1)));
        });
        await server.WaitRunTicks(3);

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);
            var lookup = server.System<EntityLookupSystem>();
            var position = server.System<SharedTransformSystem>().GetWorldPosition(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "A vehicle with both doorways taken trapped its user.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
                Assert.That(TileOf(xform.LocalPosition), Is.Not.EqualTo(TileOf(Doorway(1))), "The user was put in a wall.");
                Assert.That(lookup.GetWorldAABB(other).Enlarged(0.3f).Contains(position), Is.False, "The user was put inside another vehicle.");
                Assert.That(lookup.GetWorldAABB(rig.Vehicle).Enlarged(0.3f).Contains(position), Is.False, "The user was put inside their own vehicle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A doorway over the open edge of the deck is no way out: the user lands on floor, by the other doorway or beside the hull.</summary>
    [Test]
    [TestCase(Chassis)]
    [TestCase(OneDoorChassis)]
    public async Task ExitAtTheEdgeOfTheDeckNeverLandsInSpace(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, proto, tile => tile.X <= 5);
        var server = pair.Server;
        var entMan = server.EntMan;

        // Doorway 0 is one tile past the last plated column.
        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(1)));
        await Enter(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, user), Is.EqualTo(Interior(pair, rig).MapId), "The user did not get in.");
        });

        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);
            var maps = server.System<SharedMapSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
                Assert.That(maps.TryGetTileRef(rig.Map.Grid.Owner, rig.Map.Grid.Comp, TileOf(xform.LocalPosition), out var tile) && !tile.Tile.IsEmpty, Is.True,
                    "The user was put over open space.");
                Assert.That(Vector2.Distance(xform.LocalPosition, Doorway(0)), Is.GreaterThan(0.6f), "The user was put out of the doorway over the edge.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Floor of the vehicle's own grid is preferred to another grid's, so a doorway that opens onto a neighbour does not strand the user there.</summary>
    [Test]
    public async Task ExitPrefersTheVehiclesOwnGridToAnotherGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, OneDoorChassis, tile => tile.X <= 5);
        var server = pair.Server;
        var entMan = server.EntMan;

        // A second grid, plated where the doorway opens: the only floor right there.
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                tiles.Add(new Vector2i(x, y));
            }
        }

        var neighbour = await VehicleRigs.AddGrid(new VehicleRig { Pair = pair, Map = rig.Map }, new Vector2(6f, 4f), 0f, tiles);

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);
        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(user);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(rig.Map.MapId), "The user could not leave.");
                Assert.That(xform.ParentUid, Is.EqualTo(rig.Map.Grid.Owner), "The user was put on the neighbouring grid while their own had room.");
                Assert.That(xform.ParentUid, Is.Not.EqualTo(neighbour));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle on a platform no bigger than itself has no floor to put the user on, so they are let out into the open rather than kept in.</summary>
    [Test]
    public async Task ExitFromAVehicleOnABarePlatformStillLeaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair, OneDoorChassis, tile => tile.X == 5 && tile.Y is >= 4 and <= 6);
        var server = pair.Server;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await Enter(pair, rig, user, 0);
        await Leave(pair, rig, user, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, user), Is.EqualTo(rig.Map.MapId), "A vehicle on a bare platform trapped its user.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Things dragged in come along but take no seat: a crate pulled into a two-seat vehicle leaves room for another person.</summary>
    [Test]
    public async Task PulledCrateTakesNoSeat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid puller = default, crate = default, second = default;
        await server.WaitPost(() =>
        {
            puller = SpawnHuman(pair, rig, Doorway(0));
            crate = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(rig.Map.Grid.Owner, Doorway(0) + new Vector2(0f, 0.9f)));
            second = SpawnHuman(pair, rig, Doorway(0));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<PullingSystem>().TryStartPull(puller, crate), Is.True, "The pull did not start.");
        });
        await server.WaitRunTicks(3);

        await Enter(pair, rig, puller, 0);
        await Enter(pair, rig, second, 0);

        await server.WaitAssertion(() =>
        {
            var interior = Interior(pair, rig);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MapOf(pair, crate), Is.EqualTo(interior.MapId), "The crate was left outside.");
                Assert.That(MapOf(pair, second), Is.EqualTo(interior.MapId), "A crate in tow used up a seat.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The enter do-after has no target, so a vehicle that drives off during the wait must not still take the user in.</summary>
    [Test]
    public async Task EnteringIsRefusedWhenTheVehicleHasDrivenOffDuringTheDoAfter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            Assert.That(Activate(pair, user, rig.Vehicle), Is.True, "The vehicle did not start the enter do-after.");
            server.System<SharedTransformSystem>().SetLocalPosition(rig.Vehicle, VehicleSpot + new Vector2(8f, 0f));
        });
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Vector2.Distance(LocalPosition(pair, rig.Vehicle), VehicleSpot), Is.GreaterThan(5f), "The vehicle did not stay where it was moved to.");
                Assert.That(MapOf(pair, user), Is.EqualTo(rig.Map.MapId), "The user was taken into a vehicle that was no longer at the doorway.");
                Assert.That(entMan.TryGetComponent(rig.Vehicle, out VehicleInteriorComponent? interior) ? interior.EntryLocks : null, Is.Null.Or.Empty, "The doorway was left held.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone deleted partway through entering must not leave the only doorway marked as busy.</summary>
    [Test]
    public async Task DeletingAUserMidEnterDoesNotLockTheDoorway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            first = SpawnHuman(pair, rig, Doorway(0));
            second = SpawnHuman(pair, rig, Doorway(0));
            Assert.That(Activate(pair, first, rig.Vehicle), Is.True, "The vehicle did not start the enter do-after.");
            entMan.DeleteEntity(first);
        });
        await server.WaitRunTicks(30);

        await Enter(pair, rig, second, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, second), Is.EqualTo(Interior(pair, rig).MapId), "A deleted user left the doorway busy for good.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone deleted partway through leaving must not leave the exit marked as busy.</summary>
    [Test]
    public async Task DeletingAUserMidExitDoesNotLockTheExit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            first = SpawnHuman(pair, rig, Doorway(0));
            second = SpawnHuman(pair, rig, Doorway(0));
        });
        await Enter(pair, rig, first, 0);
        await Enter(pair, rig, second, 0);

        await server.WaitPost(() =>
        {
            Assert.That(Activate(pair, first, ExitMarker(pair, rig, 0)), Is.True, "The exit did not start its do-after.");
            entMan.DeleteEntity(first);
        });
        await server.WaitRunTicks(30);

        await Leave(pair, rig, second, 0);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(pair, second), Is.EqualTo(rig.Map.MapId), "A deleted user left the exit busy for good.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A blank key does not imprint on a locked vehicle from outside; used once the vehicle is open it does.</summary>
    [Test]
    public async Task BlankKeyDoesNotImprintOnALockedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid user = default, owner = default, stranger = default;
        await server.WaitPost(() =>
        {
            user = SpawnHuman(pair, rig, Doorway(0));
            owner = entMan.SpawnEntity("RMCVehicleKeyBlank", new EntityCoordinates(rig.Map.Grid.Owner, 2f, 2f));
            stranger = entMan.SpawnEntity("RMCVehicleKeyBlank", new EntityCoordinates(rig.Map.Grid.Owner, 2f, 3f));

            // The owner's key is imprinted and locks the vehicle.
            UseKey(pair, rig, owner, user);
            UseKey(pair, rig, owner, user);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(LockOf(pair, rig).Locked, Is.True, "The key did not lock the vehicle.");

            UseKey(pair, rig, stranger, user);
            Assert.That(entMan.GetComponent<VehicleKeyComponent>(stranger).KeyId, Is.Null, "A blank key imprinted on a locked vehicle.");
            Assert.That(LockOf(pair, rig).Locked, Is.True, "A blank key opened a locked vehicle.");

            UseKey(pair, rig, owner, user);
            Assert.That(LockOf(pair, rig).Locked, Is.False);

            UseKey(pair, rig, stranger, user);
            Assert.That(entMan.GetComponent<VehicleKeyComponent>(stranger).KeyId, Is.Not.Null, "A blank key would not imprint on an open vehicle.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone out cold who is strapped into the driver seat is neither the driver nor given the lock action.</summary>
    [Test]
    public async Task UnconsciousPersonInTheDriverSeatIsNotTheOperator()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid helper = default, victim = default;
        await server.WaitPost(() =>
        {
            helper = SpawnHuman(pair, rig, Doorway(0));
            victim = SpawnHuman(pair, rig, Doorway(0));
        });
        await Enter(pair, rig, helper, 0);
        await Enter(pair, rig, victim, 0);

        await server.WaitAssertion(() =>
        {
            var seat = DriverSeat(pair, rig);
            server.System<MobStateSystem>().ChangeMobState(victim, MobState.Critical);
            server.System<SharedTransformSystem>().SetCoordinates(victim, entMan.GetComponent<TransformComponent>(seat).Coordinates);
            server.System<SharedTransformSystem>().SetCoordinates(helper, entMan.GetComponent<TransformComponent>(seat).Coordinates);

            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(victim, helper, seat), Is.True, "The helper could not strap the victim in.");
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.Null, "Someone unconscious became the driver.");
                Assert.That(entMan.HasComponent<RMCVehicleOperatorComponent>(victim), Is.False, "An empty operator component was left on the victim.");
                Assert.That(entMan.HasComponent<VehicleLockActionComponent>(victim), Is.False, "The victim was given the lock action.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Someone strapped into the driver seat while out cold takes the wheel, with the lock action, once they are well enough
    /// to act. A body that is lying down is stood up, and so unstrapped, by recovering; this one is out cold on its feet, as
    /// someone who fell asleep or was hurt in the seat is, and wakes up still strapped in.
    /// </summary>
    [Test]
    public async Task SomeoneStrappedInWhileOutColdTakesTheWheelOnceTheyRecover()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid helper = default, victim = default;
        await server.WaitPost(() =>
        {
            helper = SpawnHuman(pair, rig, Doorway(0));
            victim = SpawnHuman(pair, rig, Doorway(0));
        });
        await Enter(pair, rig, helper, 0);
        await Enter(pair, rig, victim, 0);

        await server.WaitAssertion(() =>
        {
            var seat = DriverSeat(pair, rig);
            server.System<MobStateSystem>().ChangeMobState(victim, MobState.Critical);
            server.System<StandingStateSystem>().Stand(victim, force: true);
            server.System<SharedTransformSystem>().SetCoordinates(victim, entMan.GetComponent<TransformComponent>(seat).Coordinates);
            server.System<SharedTransformSystem>().SetCoordinates(helper, entMan.GetComponent<TransformComponent>(seat).Coordinates);

            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(victim, helper, seat), Is.True, "The helper could not strap the victim in.");
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.Null, "Test setup: someone out cold became the driver.");
                Assert.That(entMan.HasComponent<VehicleDriverRecoveryComponent>(victim), Is.True, "The victim is not waiting to take the wheel. " + DescribeDriver(entMan, victim));
            }


            server.System<MobStateSystem>().ChangeMobState(victim, MobState.Alive);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(victim),
                    "Someone who recovered in the driver seat did not take the wheel. " + DescribeDriver(entMan, victim));
                Assert.That(entMan.HasComponent<RMCVehicleOperatorComponent>(victim), Is.True);
                Assert.That(entMan.HasComponent<VehicleLockActionComponent>(victim), Is.True, "The new driver was not given the lock action.");
                Assert.That(entMan.HasComponent<VehicleDriverRecoveryComponent>(victim), Is.False, "The marker was left on a driver.");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static string DescribeDriver(IEntityManager entMan, EntityUid driver)
    {
        var buckle = entMan.GetComponent<BuckleComponent>(driver);
        var xform = entMan.GetComponent<TransformComponent>(driver);
        return $"State {entMan.GetComponent<MobStateComponent>(driver).CurrentState}, buckled {buckle.Buckled} to {buckle.BuckledTo}, " +
               $"asleep {entMan.HasComponent<SleepingComponent>(driver)}, waiting {entMan.HasComponent<VehicleDriverRecoveryComponent>(driver)}, " +
               $"at {xform.LocalPosition} on {xform.MapID} parent {xform.ParentUid}.";
    }

    /// <summary>Someone out cold who is unstrapped is no longer waiting to take the wheel.</summary>
    [Test]
    public async Task UnstrappingTheOneOutColdEndsTheWait()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid helper = default, victim = default;
        await server.WaitPost(() =>
        {
            helper = SpawnHuman(pair, rig, Doorway(0));
            victim = SpawnHuman(pair, rig, Doorway(0));
        });
        await Enter(pair, rig, helper, 0);
        await Enter(pair, rig, victim, 0);

        await server.WaitAssertion(() =>
        {
            var seat = DriverSeat(pair, rig);
            server.System<MobStateSystem>().ChangeMobState(victim, MobState.Critical);
            server.System<SharedTransformSystem>().SetCoordinates(victim, entMan.GetComponent<TransformComponent>(seat).Coordinates);
            server.System<SharedTransformSystem>().SetCoordinates(helper, entMan.GetComponent<TransformComponent>(seat).Coordinates);

            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(victim, helper, seat), Is.True, "The helper could not strap the victim in.");
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<VehicleDriverRecoveryComponent>(victim), Is.True, "Test setup: the victim should be waiting to take the wheel.");
            Assert.That(server.System<SharedBuckleSystem>().TryUnbuckle(victim, helper), Is.True, "The helper could not unstrap the victim.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            server.System<MobStateSystem>().ChangeMobState(victim, MobState.Alive);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<VehicleDriverRecoveryComponent>(victim), Is.False);
                Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.Null, "Someone who had been unstrapped took the wheel.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Looks through the vehicle's "Look inside" verb as <paramref name="user"/>, who stands at a doorway, and returns the anchor the eye went to.</summary>
    private static async Task<EntityUid> Peek(TestPair pair, Rig rig, EntityUid user)
    {
        var server = pair.Server;
        var anchor = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var verbs = server.System<SharedVerbSystem>();
            var text = server.ResolveDependency<ILocalizationManager>().GetString("rmc-vehicle-look-inside");
            var verb = verbs.GetLocalVerbs(rig.Vehicle, user, typeof(AlternativeVerb)).FirstOrDefault(v => v.Text == text);

            Assert.That(verb, Is.Not.Null, "The user was not offered a look inside.");
            verbs.ExecuteVerb(verb!, user, rig.Vehicle);

            var state = server.EntMan.GetComponent<VehicleViewportUserComponent>(user);
            Assert.That(state.PeekTarget, Is.Not.Null, "Looking inside spawned no anchor.");
            anchor = state.PeekTarget!.Value;
            Assert.That(server.EntMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(anchor), "The eye did not go to the anchor.");
        });

        return anchor;
    }

    /// <summary>A peek ends when the vehicle drives off, even if the viewer never presses a key.</summary>
    [Test]
    public async Task PeekEndsWhenTheVehicleDrivesOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        var anchor = await Peek(pair, rig, user);

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalPosition(rig.Vehicle, VehicleSpot + new Vector2(8f, 0f)));
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<VehicleViewportUserComponent>(user), Is.False, "The peek outlived the vehicle's leaving.");
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The viewer's eye is still on the vehicle's inside.");
                Assert.That(entMan.Deleted(anchor), Is.True, "The peek anchor was left on the interior map.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A viewer who is deleted while peeking leaves no anchor behind on the interior map.</summary>
    [Test]
    public async Task DeletedPeekerLeavesNoAnchor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        var anchor = await Peek(pair, rig, user);

        await server.WaitPost(() => entMan.DeleteEntity(user));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(anchor), Is.True, "The peek anchor outlived its viewer.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Someone looking out of a viewport who is pulled out of the vehicle loses the view and its action.</summary>
    [Test]
    public async Task ViewportEndsWhenTheViewerLeavesTheInterior()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);

        await server.WaitPost(() =>
        {
            var viewport = entMan.SpawnEntity("WFInteriorTestViewport", new EntityCoordinates(Interior(pair, rig).Grid, 1.5f, 2.5f));
            Assert.That(Activate(pair, user, viewport), Is.True, "The viewport did not react to being used.");
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle), "Looking through the viewport did not show the outside.");

            // Dragged out by someone else, so no movement key of their own.
            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(rig.Map.Grid.Owner, Doorway(0)));
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<VehicleViewportUserComponent>(user), Is.False, "The viewport outlived the viewer's leaving.");
                Assert.That(entMan.HasComponent<VehicleViewToggleComponent>(user), Is.False, "The view action outlived the viewer's leaving.");
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The viewer still looks through the vehicle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A driver already has the view toggle set for the seat; a viewport is refused rather than overwriting it.</summary>
    [Test]
    public async Task DriverCannotUseAViewport()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);

        var seat = EntityUid.Invalid;
        var viewport = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            seat = DriverSeat(pair, rig);
            var seatCoordinates = entMan.GetComponent<TransformComponent>(seat).Coordinates;
            server.System<SharedTransformSystem>().SetCoordinates(user, seatCoordinates);
            viewport = entMan.SpawnEntity("WFInteriorTestViewport", seatCoordinates.Offset(new Vector2(0f, 0.5f)));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(user, user, seat), Is.True, "The user could not strap into the driver seat.");
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(Activate(pair, user, viewport), Is.True, "The viewport did not react to being used.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<VehicleViewportUserComponent>(user), Is.False, "The driver was given a viewport view.");
                Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.EqualTo(rig.Vehicle));
            }

            // The seat's toggle still reaches the inside.
            var viewAction = FindAction(pair, user, "ActionVehicleToggleView");
            PerformAction(pair, user, viewAction);
            Assert.That(entMan.GetComponent<EyeComponent>(user).Target, Is.Null, "The view action no longer brings the eye inside.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Deleting a vehicle takes its interior map with it.</summary>
    [Test]
    public async Task DeletingTheVehicleRemovesItsInterior()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        var user = EntityUid.Invalid;
        await server.WaitPost(() => user = SpawnHuman(pair, rig, Doorway(0)));
        await server.WaitRunTicks(3);

        await Enter(pair, rig, user, 0);
        await Leave(pair, rig, user, 0);

        MapId interiorMap = default;
        await server.WaitPost(() =>
        {
            interiorMap = Interior(pair, rig).MapId;
            entMan.DeleteEntity(rig.Vehicle);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedMapSystem>().MapExists(interiorMap), Is.False, "The interior map outlived its vehicle.");
        });

        await pair.CleanReturnAsync();
    }
}
