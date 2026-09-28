#nullable enable
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ShipAccess;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Doors.Components;
using Content.Shared.Power;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Codes: a door code or the ship code opens a code door from the keypad path for anyone who can reach it,
/// through the normal door checks; wrong codes count misses and lock a character out; and no code ever sits on
/// a networked component.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAccessCodeComponent))]
public sealed class ShipAccessCodeTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string HumanProto = "MobHuman";
    private const string GhostProto = "MobObserver";
    private const string WallProto = "WallSolid";
    private const string Visitor = "Ada Vance";
    private const string Stranger = "Random Stranger";

    [Test]
    public async Task DoorCodeOpensAndMissesLockOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid doorA = default, doorB = default, visitor = default, npc = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            doorA = SpawnPoweredDoor(entMan, map.GridCoords);
            doorB = SpawnPoweredDoor(entMan, map.GridCoords);
            visitor = SpawnPerson(entMan, map.GridCoords, Visitor);
            npc = SpawnPerson(entMan, map.GridCoords, Stranger);
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            var comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.Locked = true;
            ship = (grid, comp);
        });

        await server.WaitAssertion(() =>
        {
            DoorState State(EntityUid door) => entMan.GetComponent<DoorComponent>(door).State;

            Assert.That(access.TrySubmitCode(npc, doorA, "1234"), Is.EqualTo(WFShipAccessCodeResult.NoKeypad), "A Default door has no keypad.");

            access.SetDoorRule(ship, doorA, WFDoorAccessRule.Code);
            Assert.That(access.SetDoorCode(ship, doorA, "12"), Is.False, "A code is four digits.");
            Assert.That(access.SetDoorCode(ship, doorA, "12a4"), Is.False, "A code is digits only.");
            Assert.That(access.SetDoorCode(ship, doorA, "1234"), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(doorA).HasOwnCode, Is.True, "Setting a door code flags the rule.");
                Assert.That(entMan.GetComponent<WFDoorCodeComponent>(doorA).Code, Is.EqualTo("1234"));
            });

            Assert.That(access.TrySubmitCode(npc, doorA, "1234"), Is.EqualTo(WFShipAccessCodeResult.Opened), "The door code opens the door for a stranger.");
            Assert.That(State(doorA), Is.EqualTo(DoorState.Opening), "A correct code starts the door opening.");

            Assert.That(access.SetDoorCode(ship, doorA, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(doorA).HasOwnCode, Is.False, "Clearing a door code clears the flag.");
                Assert.That(entMan.HasComponent<WFDoorCodeComponent>(doorA), Is.False, "Clearing a door code removes it.");
            });

            // Misses and the lockout, per character.
            access.SetDoorRule(ship, doorB, WFDoorAccessRule.Code);
            access.SetDoorCode(ship, doorB, "1234");
            var codes = entMan.GetComponent<WFShipAccessCodeComponent>(grid);
            for (var i = 1; i < WFShipAccessServerSystem.MaxMisses; i++)
            {
                Assert.That(access.TrySubmitCode(visitor, doorB, "0000"), Is.EqualTo(WFShipAccessCodeResult.Wrong), $"Miss {i} is a wrong code.");
                Assert.That(access.IsLockedOut(codes, Visitor), Is.False, $"Miss {i} does not lock out yet.");
            }

            Assert.That(codes.Misses, Is.EqualTo(WFShipAccessServerSystem.MaxMisses - 1), "Every miss is counted for the ship.");
            Assert.That(access.TrySubmitCode(visitor, doorB, "0000"), Is.EqualTo(WFShipAccessCodeResult.Wrong));
            Assert.That(access.IsLockedOut(codes, Visitor), Is.True, "The fifth miss locks the character out.");
            Assert.That(access.IsLockedOut(codes, Stranger), Is.False, "Only the character who missed is locked out.");
            Assert.That(access.TrySubmitCode(visitor, doorB, "1234"), Is.EqualTo(WFShipAccessCodeResult.LockedOut), "A locked-out character is refused even with the right code.");
            Assert.That(State(doorB), Is.EqualTo(DoorState.Closed), "The door stayed closed through it all.");

            codes.Lockouts[Visitor].LockedUntil = timing.CurTime - TimeSpan.FromSeconds(1);
            Assert.That(access.IsLockedOut(codes, Visitor), Is.False, "The lockout ends.");
            Assert.That(access.TrySubmitCode(visitor, doorB, "1234"), Is.EqualTo(WFShipAccessCodeResult.Opened), "After the lockout the right code opens the door.");
            Assert.That(State(doorB), Is.EqualTo(DoorState.Opening));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipCodeOpensCodeDoorsWithoutTheirOwn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid codeDoor = default, eitherDoor = default, ownDoor = default, npc = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            codeDoor = SpawnPoweredDoor(entMan, map.GridCoords);
            eitherDoor = SpawnPoweredDoor(entMan, map.GridCoords);
            ownDoor = SpawnPoweredDoor(entMan, map.GridCoords);
            npc = SpawnPerson(entMan, map.GridCoords, Stranger);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
        });

        await server.WaitAssertion(() =>
        {
            DoorState State(EntityUid door) => entMan.GetComponent<DoorComponent>(door).State;

            access.SetDoorRule(ship, codeDoor, WFDoorAccessRule.Code);
            access.SetDoorRule(ship, eitherDoor, WFDoorAccessRule.PlayersOrCode);
            access.SetDoorRule(ship, ownDoor, WFDoorAccessRule.Code);
            access.SetDoorCode(ship, ownDoor, "9999");

            Assert.That(access.TrySubmitCode(npc, codeDoor, "4321"), Is.EqualTo(WFShipAccessCodeResult.Wrong), "Without any code set, nothing opens.");
            Assert.That(access.SetShipCode(ship, "43210"), Is.False, "The ship code is four digits.");
            Assert.That(access.SetShipCode(ship, "4321"), Is.True);
            Assert.That(entMan.GetComponent<WFShipAccessCodeComponent>(grid).ShipCode, Is.EqualTo("4321"));

            Assert.That(access.TrySubmitCode(npc, codeDoor, "1111"), Is.EqualTo(WFShipAccessCodeResult.Wrong), "A wrong ship code is refused.");
            Assert.That(State(codeDoor), Is.EqualTo(DoorState.Closed));
            Assert.That(access.TrySubmitCode(npc, codeDoor, "4321"), Is.EqualTo(WFShipAccessCodeResult.Opened), "The ship code opens a Code door with no code of its own.");
            Assert.That(access.TrySubmitCode(npc, eitherDoor, "4321"), Is.EqualTo(WFShipAccessCodeResult.Opened), "The ship code opens a PlayersOrCode door too.");
            Assert.That(access.TrySubmitCode(npc, ownDoor, "4321"), Is.EqualTo(WFShipAccessCodeResult.Opened), "A door with its own code takes the ship code as well.");
            Assert.Multiple(() =>
            {
                Assert.That(State(codeDoor), Is.EqualTo(DoorState.Opening));
                Assert.That(State(eitherDoor), Is.EqualTo(DoorState.Opening));
                Assert.That(State(ownDoor), Is.EqualTo(DoorState.Opening));
            });

            Assert.That(access.SetShipCode(ship, null), Is.True, "The ship code clears.");
            Assert.That(entMan.GetComponent<WFShipAccessCodeComponent>(grid).ShipCode, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A client can send a code without the verb, so the server repeats its checks: a ghost can't use a keypad,
    /// a wall in the way blocks it, and a right code still can't open an unpowered airlock.
    /// </summary>
    [Test]
    public async Task CodeKeepsTheNormalDoorChecks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, ghost = default, near = default, walled = default, wall = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            // Two more floor tiles east of the door, for a wall and a person behind it.
            for (var x = 1; x <= 2; x++)
                mapSys.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);

            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            ghost = entMan.SpawnEntity(GhostProto, map.GridCoords);
            near = SpawnPerson(entMan, map.GridCoords, Visitor);
            wall = entMan.SpawnEntity(WallProto, new EntityCoordinates(grid, 1.5f, 0.5f));
            walled = SpawnPerson(entMan, new EntityCoordinates(grid, 2.5f, 0.5f), Stranger);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetDoorRule(ship, door, WFDoorAccessRule.Code);
            access.SetDoorCode(ship, door, "1234");
        });

        await server.WaitAssertion(() =>
        {
            DoorState State() => entMan.GetComponent<DoorComponent>(door).State;
            var codes = EnsureCodes(entMan, grid);

            Assert.That(access.TrySubmitCode(ghost, door, "1234"), Is.EqualTo(WFShipAccessCodeResult.CannotInteract), "A ghost can't use a keypad.");
            Assert.That(access.TrySubmitCode(walled, door, "1234"), Is.EqualTo(WFShipAccessCodeResult.OutOfRange), "A wall between the person and the door blocks the keypad.");
            entMan.DeleteEntity(wall);
            Assert.That(access.TrySubmitCode(walled, door, "1234"), Is.Not.EqualTo(WFShipAccessCodeResult.OutOfRange), "Without the wall the same person reaches it.");

            Assert.That(access.TrySubmitCode(near, door, "1234"), Is.EqualTo(WFShipAccessCodeResult.NoResponse), "A right code can't open an unpowered airlock.");
            Assert.That(State(), Is.EqualTo(DoorState.Closed));
            Assert.That(codes.Misses, Is.Zero, "A right code is never a miss.");

            Power(entMan, door);
            Assert.That(access.TrySubmitCode(near, door, "1234"), Is.EqualTo(WFShipAccessCodeResult.Opened), "Powered, the same code opens it.");
            Assert.That(State(), Is.EqualTo(DoorState.Opening));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Codes live on server-only components; the networked rule component carries no string at all.</summary>
    [Test]
    public void CodesAreNeverNetworked()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(WFShipAccessCodeComponent).GetCustomAttribute<NetworkedComponentAttribute>(), Is.Null, "The ship code component must not be networked.");
            Assert.That(typeof(WFDoorCodeComponent).GetCustomAttribute<NetworkedComponentAttribute>(), Is.Null, "The door code component must not be networked.");
            Assert.That(typeof(WFShipAccessCodeComponent).Assembly.GetName().Name, Is.EqualTo("Content.Server"), "Codes are stored server-side.");

            var members = typeof(WFDoorAccessRuleComponent).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var strings = members.Where(m => m is FieldInfo { FieldType: var ft } && ft == typeof(string) || m is PropertyInfo { PropertyType: var pt } && pt == typeof(string)).Select(m => m.Name).ToList();
            Assert.That(strings, Is.Empty, "The networked rule component must not carry a code string.");

            var accessStrings = typeof(WFShipAccessComponent).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(string) && f.Name.Contains("Code")).Select(f => f.Name).ToList();
            Assert.That(accessStrings, Is.Empty, "The networked grid component must not carry a code string.");
        });
    }

    /// <summary>A shuttle airlock that counts as powered. Server thread only.</summary>
    private static EntityUid SpawnPoweredDoor(IEntityManager entMan, EntityCoordinates coords)
    {
        var door = entMan.SpawnEntity(DoorProto, coords);
        Power(entMan, door);
        return door;
    }

    /// <summary>The test map has no power grid, so a powered-up event stands in for one.</summary>
    private static void Power(IEntityManager entMan, EntityUid door)
    {
        var powered = new PowerChangedEvent(true, 0f);
        entMan.EventBus.RaiseLocalEvent(door, ref powered);
    }

    private static WFShipAccessCodeComponent EnsureCodes(IEntityManager entMan, EntityUid grid) =>
        entMan.EnsureComponent<WFShipAccessCodeComponent>(grid);

    /// <summary>A human with a fixed name, since misses are counted by name. Server thread only.</summary>
    private static EntityUid SpawnPerson(IEntityManager entMan, EntityCoordinates coords, string name)
    {
        var uid = entMan.SpawnEntity(HumanProto, coords);
        entMan.System<MetaDataSystem>().SetEntityName(uid, name);
        return uid;
    }
}
