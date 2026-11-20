#nullable enable
using System.IO;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.Power.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Power;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Per-door rules, written into each door's airlock access reader: each rule's decision, sealing, a ruled door
/// taking over its reader on an unlocked ship, door lists following the allow list, rules surviving a grid save
/// and load, and a resale wiping the seller's codes, rules and seals and restoring the readers.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDoorAccessRuleComponent))]
public sealed class ShipAccessDoorRuleTest
{
    private const string DoorProto = "AirlockShuttle";

    [Test]
    public async Task RulesDecideAtTheDoor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, owner = default, deed = default, person = default, stranger = default, strangerCard = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            // Bolts need power and the test map has none; without a receiver the bolt system treats the door as powered.
            entMan.RemoveComponent<ApcPowerReceiverComponent>(door);
            (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            (person, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            (stranger, strangerCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
        });

        await server.WaitAssertion(() =>
        {
            bool Allowed(EntityUid user) => readers.IsAllowed(user, door);

            Assert.That(access.TryAddPerson(ship, person), Is.True, "Precondition: the person's card is on the allow list.");
            var listed = ship.Comp!.AllowList.Single().Key;
            Assert.That(Allowed(person), Is.True, "Precondition: a listed card opens a Default door.");

            // Deed only.
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.True);
            Assert.That(access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly), Is.False, "Setting the same rule again is a no-op.");
            Assert.That(Allowed(person), Is.False, "OwnerOnly refuses a listed card.");
            Assert.That(Allowed(owner), Is.True, "OwnerOnly admits the deed.");

            // Public, even locked and even for a stranger.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Public);
            Assert.That(ship.Comp.Locked, Is.True, "Precondition: the ship is still locked.");
            Assert.That(Allowed(stranger), Is.True, "Public admits a stranger on a locked ship.");

            // Sealed: bolted, and nobody, not even the deed.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Sealed);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(door), Is.True, "Sealing bolts the door.");
                Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open), "A sealed door is not left open.");
                Assert.That(Allowed(owner), Is.False, "Sealed refuses the deed at the reader.");
                Assert.That(Allowed(stranger), Is.False, "Sealed refuses a stranger.");
            });
            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.That(doors.IsBolted(door), Is.False, "Leaving Sealed unbolts the door.");
            Assert.That(entMan.HasComponent<DoorBoltComponent>(door), Is.True, "Bolts the door already had are kept.");

            // Players: the door's own list, kept in step with the allow list.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Players);
            Assert.That(Allowed(person), Is.False, "Players refuses a listed card that is not picked for the door.");
            var strangerKey = entMan.System<WFShipAccessSystem>().TryGetKey(strangerCard, out var key) ? key : default;
            Assert.That(access.SetDoorPlayer(ship, door, strangerKey, true), Is.False, "Only allow-listed cards can be picked for a door.");
            Assert.That(access.SetDoorPlayer(ship, door, listed, true), Is.True);
            Assert.That(access.SetDoorPlayer(ship, door, listed, true), Is.False, "Picking a card twice is a no-op.");
            Assert.That(Allowed(person), Is.True, "Players admits a picked card.");
            Assert.That(Allowed(owner), Is.True, "Players admits the deed.");
            Assert.That(Allowed(stranger), Is.False, "Players refuses a stranger.");

            Assert.That(access.RemoveEntry(ship, listed), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(door).Players, Is.Empty, "Leaving the allow list drops the door pick.");
                Assert.That(Allowed(person), Is.False, "A dropped card is refused again.");
            });

            // Code doors admit the deed at the reader; everyone else needs the keypad.
            access.SetDoorRule(ship, door, WFDoorAccessRule.Code);
            Assert.That(Allowed(person), Is.False, "Code refuses a stranger at the reader.");
            Assert.That(Allowed(owner), Is.True, "Code admits the deed at the reader.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Sealing an open door closes it and bolts it once shut. Bolts dropped mid-close used to cancel the close and
    /// leave the door bolted open.
    /// </summary>
    [Test]
    public async Task SealingAnOpenDoorBoltsItOnceShut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            // Without a receiver the bolts count as powered; the event makes the airlock itself powered.
            entMan.RemoveComponent<ApcPowerReceiverComponent>(door);
            var powered = new PowerChangedEvent(true, 0f);
            entMan.EventBus.RaiseLocalEvent(door, ref powered);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            Assert.That(doors.TryOpen(door), Is.True, "Precondition: the door opens.");
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open), "Precondition: the door is open.");
            access.SetDoorRule(ship, door, WFDoorAccessRule.Sealed);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(door), Is.False, "An open door is not bolted mid-close.");
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(door).SealPending, Is.True, "The seal waits for the door to shut.");
                Assert.That(entMan.GetComponent<DoorComponent>(door).NextStateChange, Is.Not.Null, "Sealing schedules the door to close.");
            });
        });
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed), "The sealed door shut.");
                Assert.That(doors.IsBolted(door), Is.True, "Once shut, the sealed door is bolted.");
                Assert.That(entMan.GetComponent<WFDoorAccessRuleComponent>(door).SealPending, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RuleTakesOverReaderOnUnlockedShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, false);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(ShipAccessTest.ReaderLocked(entMan, door), Is.False, "Precondition: an unlocked ship leaves the reader alone.");

            access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly);
            Assert.That(ShipAccessTest.ReaderLocked(entMan, door), Is.True, "A ruled door takes its reader over on an unlocked ship.");

            access.SetLocked(ship, true);
            access.SetLocked(ship, false);
            Assert.That(ShipAccessTest.ReaderLocked(entMan, door), Is.True, "Flipping the lock leaves a ruled door's reader as it was.");

            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.Multiple(() =>
            {
                Assert.That(ShipAccessTest.ReaderLocked(entMan, door), Is.False, "Back on Default the door gets its own access back.");
                Assert.That(entMan.HasComponent<WFShipReaderBackupComponent>(door), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The rule is plain data that saves with the grid; the card list is round state and does not.</summary>
    [Test]
    public async Task RulesSurviveGridSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loader = entMan.System<MapLoaderSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        var yaml = string.Empty;
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<WFShipAccessComponent>(grid);
            var door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var rule = entMan.EnsureComponent<WFDoorAccessRuleComponent>(door);
            rule.Rule = WFDoorAccessRule.PlayersOrCode;
            rule.Players.Add(new WFShipAccessKey(entMan.GetNetEntity(map.MapUid), 7));
            rule.HasOwnCode = true;

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(grid, writer), Is.True, "The grid saves.");
            yaml = writer.ToString();
        });

        Assert.That(yaml, Does.Contain("WFDoorAccessRule"), "The rule component is written to the save.");

        MapId loadedMap = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out loadedMap);
            using var reader = new StringReader(yaml);
            Assert.That(loader.TryLoadGrid(loadedMap, reader, "ship-access-test", out var loaded), Is.True, "The grid loads again.");

            WFDoorAccessRuleComponent? found = null;
            var query = entMan.EntityQueryEnumerator<WFDoorAccessRuleComponent, TransformComponent>();
            while (query.MoveNext(out _, out var rule, out var xform))
            {
                if (xform.GridUid == loaded!.Value.Owner)
                    found = rule;
            }

            Assert.That(found, Is.Not.Null, "The loaded grid has the ruled door.");
            Assert.Multiple(() =>
            {
                Assert.That(found!.Rule, Is.EqualTo(WFDoorAccessRule.PlayersOrCode));
                Assert.That(found.Players, Is.Empty, "Door lists are round state, like guest cards, and are not saved.");
                Assert.That(found.HasOwnCode, Is.True);
            });
        });

        await server.WaitPost(() => maps.DeleteMap(loadedMap));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A used ship comes to its buyer without the seller's codes, rules or seals, and every reader has its own
    /// access back. A seal lifted without power leaves a bare rule that unbolts the door once power returns.
    /// </summary>
    [Test]
    public async Task ResaleClearsCodesRulesAndSeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var doors = entMan.System<SharedDoorSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid codeDoor = default, sealedDoor = default, darkDoor = default;
        await server.WaitPost(() =>
        {
            codeDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            sealedDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            darkDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            // Without a receiver the bolt system treats a door as powered, so these seal at once.
            foreach (var door in new[] { codeDoor, sealedDoor, darkDoor })
                entMan.RemoveComponent<ApcPowerReceiverComponent>(door);

            var ship = new Entity<WFShipAccessComponent>(grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
            access.SetShipCode(ship, "4321");
            access.SetDoorRule(ship, codeDoor, WFDoorAccessRule.Code);
            access.SetDoorCode(ship, codeDoor, "1234");
            access.SetDoorRule(ship, sealedDoor, WFDoorAccessRule.Sealed);
            access.SetDoorRule(ship, darkDoor, WFDoorAccessRule.Sealed);
            // The last door loses power after sealing, so its bolts can't come up at the sale.
            entMan.AddComponent<ApcPowerReceiverComponent>(darkDoor);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(sealedDoor), Is.True, "Precondition: the sealed door is bolted.");
                Assert.That(doors.IsBolted(darkDoor), Is.True, "Precondition: the unpowered sealed door is bolted.");
                Assert.That(ShipAccessTest.ReaderLocked(entMan, codeDoor), Is.True, "Precondition: the seller's lock is in the readers.");
            });

            entMan.System<ShipyardSystem>().StripForResale(grid);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.False, "The seller's access record is gone.");
                Assert.That(entMan.HasComponent<WFShipAccessCodeComponent>(grid), Is.False, "The seller's ship code is gone.");
                Assert.That(entMan.HasComponent<WFDoorCodeComponent>(codeDoor), Is.False, "The seller's door code is gone.");
                Assert.That(entMan.HasComponent<WFDoorAccessRuleComponent>(codeDoor), Is.False, "The seller's door rule is gone.");
                Assert.That(entMan.HasComponent<WFDoorAccessRuleComponent>(sealedDoor), Is.False, "The seal's rule is gone.");
                Assert.That(doors.IsBolted(sealedDoor), Is.False, "The seal's bolts are up.");
                foreach (var door in new[] { codeDoor, sealedDoor, darkDoor })
                {
                    Assert.That(ShipAccessTest.ReaderLocked(entMan, door), Is.False, $"{entMan.ToPrettyString(door)} has its own access back.");
                    Assert.That(entMan.HasComponent<WFShipReaderBackupComponent>(door), Is.False);
                }
            });

            var dark = entMan.GetComponent<WFDoorAccessRuleComponent>(darkDoor);
            Assert.Multiple(() =>
            {
                Assert.That(dark.Rule, Is.EqualTo(WFDoorAccessRule.Default), "An unpowered seal is lifted as a rule.");
                Assert.That(dark.UnboltWhenPowered, Is.True, "Its bolts wait for power.");
                Assert.That(doors.IsBolted(darkDoor), Is.True, "Bolts can't come up without power.");
            });

            entMan.GetComponent<ApcPowerReceiverComponent>(darkDoor).Powered = true;
            var powered = new PowerChangedEvent(true, 0f);
            entMan.EventBus.RaiseLocalEvent(darkDoor, ref powered);
            Assert.Multiple(() =>
            {
                Assert.That(doors.IsBolted(darkDoor), Is.False, "Power brings the bolts up.");
                Assert.That(dark.UnboltWhenPowered, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
