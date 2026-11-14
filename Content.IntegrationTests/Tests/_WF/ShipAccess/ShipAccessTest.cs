#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.ShipAccess;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ShipAccess;
using Content.Server.StationRecords;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Lock;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.StationRecords;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Ship access through the normal airlock access readers: the deed and listed ID cards open a locked ship by the
/// crew record keys they carry, an unlocked ship's doors get their own access back, the faction's access level
/// opens a faction ship, a refusal denies and a hacked or emagged reader opens as on any airlock, readers on
/// doors built later follow the lock, and the console tab loads, knows the deed holder and shows its notices in full.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAccessSystem))]
public sealed class ShipAccessTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string LockerProto = "LockerFreezer";

    /// <summary>A lockable button mapped with <see cref="SecureAccess"/>.</summary>
    private const string ButtonProto = "LockableButtonCaptain";
    private const string HumanProto = "MobHuman";
    private const string CardProto = "PassengerIDCard";
    private const string AdminGhostProto = "AdminObserver";

    /// <summary>A company with an access group of the same id, so the faction rule has access levels to check.</summary>
    private const string Company = "PDV";

    /// <summary>One of the levels in the PDV access group.</summary>
    private const string CompanyAccess = "Pirate";

    /// <summary>Access a secure locker brought aboard might ask for.</summary>
    private const string SecureAccess = "Captain";

    [Test]
    public async Task OwnerAddsHeldCardWithoutItsHolder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var hands = entMan.System<SharedHandsSystem>();
            var readers = entMan.System<AccessReaderSystem>();
            var access = entMan.System<WFShipAccessServerSystem>();
            var console = entMan.SpawnEntity("ComputerShuttle", map.GridCoords);
            var door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var (owner, deed) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            GiveDeed(entMan, deed, map.Grid.Owner);
            var (friend, card) = SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            entMan.System<SharedIdCardSystem>().TryChangeFullName(card, "Ben Ortiz");
            var ship = new Entity<WFShipAccessComponent>(map.Grid.Owner, entMan.EnsureComponent<WFShipAccessComponent>(map.Grid.Owner));
            access.SetLocked(ship, true);

            Assert.That(hands.TryDrop(friend, card), Is.True, "Precondition: the friend hands their card over.");
            entMan.DeleteEntity(friend);
            Assert.That(hands.TryPickupAnyHand(owner, card), Is.True, "Precondition: the owner holds the friend's card.");

            var message = new WFShipAccessAddPlayerMessage(entMan.GetNetEntity(card))
            {
                Actor = owner,
                UiKey = ShuttleConsoleUiKey.Key,
                Entity = entMan.GetNetEntity(console),
            };
            entMan.EventBus.RaiseLocalEvent(console, (object) message);

            Assert.That(ship.Comp.AllowList.Select(e => e.Name), Is.EqualTo(new[] { "Ben Ortiz" }), "A card in the owner's hands is listed under its name.");
            Assert.That(hands.TryDrop(owner, card), Is.True);
            var (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            Assert.That(hands.TryPickupAnyHand(stranger, card), Is.True);
            Assert.That(readers.IsAllowed(stranger, door), Is.True, "The listed card opens the locked ship.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeedAndListedCardsGateLockedDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, owner = default, deed = default, person = default, card = default, stranger = default, nobody = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            (owner, deed) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            GiveDeed(entMan, deed, grid);
            (person, card) = SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            nobody = entMan.SpawnEntity(HumanProto, map.GridCoords);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
        });

        await server.WaitAssertion(() =>
        {
            bool Allowed(EntityUid user) => readers.IsAllowed(user, door);

            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, door), Is.True, "Locking puts the door's electronics on the locked-ship access.");
                Assert.That(Allowed(owner), Is.True, "The deed's record key opens a locked ship.");
                Assert.That(Allowed(person), Is.False, "A stranger must not open a locked ship.");
                Assert.That(Allowed(nobody), Is.False, "Someone without a card must not either.");
                Assert.That(access.TryAddPerson(ship, nobody), Is.False, "There is no card to list on someone without one.");
                Assert.That(access.TryAddPerson(ship, owner), Is.False, "The deed card is never listed; it already opens everything.");
            });

            Assert.That(access.TryAddPerson(ship, person), Is.True, "A person's card can be listed.");
            Assert.That(access.TryAddPerson(ship, person), Is.False, "Listing the same card twice must fail.");
            Assert.That(ship.Comp!.AllowList.Single().Name, Is.EqualTo("Ben Ortiz"), "The entry shows the holder's name when the card is blank.");
            Assert.That(Allowed(person), Is.True, "A listed card opens the ship.");
            Assert.That(Allowed(stranger), Is.False, "Listing one card admits nobody else.");

            // The card carries the key, not the person.
            Assert.That(hands.TryDrop(person, card), Is.True, "Precondition: the person puts their card down.");
            Assert.That(Allowed(person), Is.False, "Without the listed card its holder is a stranger.");
            Assert.That(hands.TryPickupAnyHand(stranger, card), Is.True, "Precondition: the stranger picks the card up.");
            Assert.That(Allowed(stranger), Is.True, "Whoever carries the listed card gets in.");

            Assert.That(access.RemoveEntry(ship, ship.Comp.AllowList.Single().Key), Is.True);
            Assert.That(Allowed(stranger), Is.False, "A removed card is a stranger's card again.");

            // The deed card is the owner's key in the same way.
            Assert.That(hands.TryDrop(owner, deed), Is.True, "Precondition: the owner puts the deed down.");
            Assert.That(Allowed(owner), Is.False, "Without the deed card its former holder is a stranger.");
            Assert.That(hands.TryPickupAnyHand(nobody, deed), Is.True, "Precondition: someone else picks the deed up.");
            Assert.That(Allowed(nobody), Is.True, "Whoever carries the deed card is the owner.");

            access.SetLocked(ship, false);
            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, door), Is.False, "Unlocking gives the door its own access back.");
                Assert.That(entMan.HasComponent<WFShipReaderBackupComponent>(door), Is.False, "The backup goes once restored.");
                Assert.That(Allowed(person), Is.True, "An unlocked ship is open to everyone, as its electronics ask for nothing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An admin ghost opens a locked ship's doors, ruled ones included, as it opens every other airlock.</summary>
    [Test]
    public async Task AdminGhostOpensLockedShipDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, ownerDoor = default, ghost = default, stranger = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            ownerDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            ghost = entMan.SpawnEntity(AdminGhostProto, map.GridCoords);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            var (_, deed) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            GiveDeed(entMan, deed, grid);
            var ship = new Entity<WFShipAccessComponent>(grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
            access.SetDoorRule(ship, ownerDoor, WFDoorAccessRule.OwnerOnly);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "Precondition: the ship is locked to strangers.");
                Assert.That(readers.IsAllowed(ghost, door), Is.True, "An admin ghost opens a locked ship's door.");
                Assert.That(readers.IsAllowed(ghost, ownerDoor), Is.True, "An admin ghost opens a deed-only door too.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A refusal denies, and a cut access wire, emergency access or an emag open the door, all as on any airlock.</summary>
    [Test]
    public async Task RefusalDeniesLikeAnAirlockAndHackingBypasses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, stranger = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            // The test map has no power grid; a powered-up event stands in for it so the airlock opens and denies.
            var powered = new PowerChangedEvent(true, 0f);
            entMan.EventBus.RaiseLocalEvent(door, ref powered);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
        });

        await server.WaitAssertion(() =>
        {
            DoorState State() => entMan.GetComponent<DoorComponent>(door).State;
            Assert.That(entMan.GetComponent<AirlockComponent>(door).Powered, Is.True, "Precondition: the airlock counts as powered.");

            Assert.That(doors.TryOpen(door, user: stranger), Is.False, "A stranger cannot open the door.");
            Assert.That(State(), Is.EqualTo(DoorState.Denying), "The refusal plays the deny state.");
            doors.SetState(door, DoorState.Closed);

            // The door re-checks whoever still touches it once the deny ends; that check stays quiet.
            Assert.That(doors.TryOpen(door, user: stranger, quiet: true), Is.False, "A quiet re-check still refuses.");
            Assert.That(State(), Is.EqualTo(DoorState.Closed), "A quiet re-check does not deny again.");

            var wire = entMan.GetComponent<AccessReaderComponent>(door);
            wire.Enabled = false;
            Assert.That(doors.TryOpen(door, user: stranger), Is.True, "With the access wire cut the door opens.");
            doors.SetState(door, DoorState.Closed);
            wire.Enabled = true;
            Assert.That(doors.TryOpen(door, user: stranger), Is.False, "Mending the wire closes it to strangers again.");
            doors.SetState(door, DoorState.Closed);

            var airlocks = entMan.System<SharedAirlockSystem>();
            airlocks.SetEmergencyAccess((door, entMan.GetComponent<AirlockComponent>(door)), true);
            Assert.That(doors.TryOpen(door, user: stranger), Is.True, "Emergency access opens the door.");
            doors.SetState(door, DoorState.Closed);
            airlocks.SetEmergencyAccess((door, entMan.GetComponent<AirlockComponent>(door)), false);

            // An emag wipes the electronics' access, as AccessReaderSystem does, and leaves no marker on the door;
            // the ship notices its lock is gone and a later lock must not arm the reader again.
            Assert.That(entMan.System<AccessReaderSystem>().GetMainAccessReader(door, out var reader), Is.True);
            reader!.Value.Comp.AccessLists.Clear();
            access.SetLocked(ship, false);
            access.SetLocked(ship, true);
            Assert.That(ReaderLocked(entMan, door), Is.False, "Relocking leaves an emagged door open.");
            Assert.That(doors.TryOpen(door, user: stranger), Is.True, "An emagged door opens for anyone.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurchaseLocksReadersAndCoversLaterBuilds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, buyer = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            (buyer, _) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            // Mono's own reader, as its purchase sweep leaves it; this module keeps it switched off.
            entMan.EnsureComponent<ShipAccessReaderComponent>(door).Enabled = true;
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.False, "Precondition: no ship access before purchase.");
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, buyer));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.True, "The purchase must add ship access.");
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(comp.OwnerName, Is.EqualTo("Ada Vance"), "The buyer's name is kept for display.");
                Assert.That(comp.Mode, Is.EqualTo(WFShipAccessMode.Private), "A grid without a company is Private.");
                Assert.That(comp.Locked, Is.True, "wf.shipaccess.lock_new_ships locks a new ship.");
                Assert.That(ReaderLocked(entMan, door), Is.True, "The purchase locks the door's electronics.");
                Assert.That(entMan.GetComponent<ShipAccessReaderComponent>(door).Enabled, Is.False, "Mono's deed reader is switched off.");
            });
        });

        // Doors and lockers built after the purchase follow the lock once their electronics are in. The locker is
        // anchored so physics cannot shove it off the one-tile grid.
        var xforms = entMan.System<SharedTransformSystem>();
        EntityUid laterDoor = default, locker = default, secureLocker = default;
        await server.WaitPost(() =>
        {
            laterDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            locker = entMan.SpawnEntity(LockerProto, map.GridCoords);
            Assert.That(xforms.AnchorEntity(locker), Is.True, "Precondition: the locker anchors to the grid.");
            // Someone else's secure locker, brought aboard after the purchase.
            secureLocker = entMan.SpawnEntity(LockerProto, map.GridCoords);
            entMan.System<AccessReaderSystem>().SetAccesses(secureLocker, entMan.GetComponent<AccessReaderComponent>(secureLocker),
                new List<ProtoId<AccessLevelPrototype>> { SecureAccess });
            Assert.That(xforms.AnchorEntity(secureLocker), Is.True, "Precondition: the secure locker anchors to the grid.");
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, laterDoor), Is.True, "A door built on a locked ship is locked too.");
                Assert.That(ReaderLocked(entMan, locker), Is.True, "A locker asking for no access is locked with the ship.");
                Assert.That(ReaderLocked(entMan, secureLocker), Is.False, "A secure locker brought aboard keeps its own access.");
                Assert.That(entMan.GetComponent<AccessReaderComponent>(secureLocker).AccessLists.Single(), Does.Contain(new ProtoId<AccessLevelPrototype>(SecureAccess)));
            });

            access.SetLocked((grid, entMan.GetComponent<WFShipAccessComponent>(grid)), false);
            Assert.Multiple(() =>
            {
                foreach (var uid in new[] { door, laterDoor, locker })
                    Assert.That(ReaderLocked(entMan, uid), Is.False, $"Unlocking must restore the reader on {entMan.ToPrettyString(uid)}.");
            });
        });

        // Faction variant: a company grid goes Faction, and a card with the company's access opens it for an unlisted stranger.
        var readers = entMan.System<AccessReaderSystem>();
        var factionMap = await pair.CreateTestMap();
        var factionGrid = factionMap.Grid.Owner;
        EntityUid factionDoor = default, npc = default, companyCard = default;
        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<ShuttleDeedComponent>(factionGrid);
            entMan.EnsureComponent<CompanyComponent>(factionGrid).CompanyName = Company;
            factionDoor = entMan.SpawnEntity(DoorProto, factionMap.GridCoords);
            npc = entMan.SpawnEntity(HumanProto, factionMap.GridCoords);
            companyCard = entMan.SpawnEntity(CardProto, factionMap.GridCoords);
            entMan.System<SharedAccessSystem>().TrySetTags(companyCard, new List<ProtoId<AccessLevelPrototype>> { CompanyAccess });
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(factionGrid, buyer));
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipAccessComponent>(factionGrid);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Mode, Is.EqualTo(WFShipAccessMode.Faction), "A company grid is Faction.");
                Assert.That(comp.Locked, Is.True);
                Assert.That(readers.IsAllowed(npc, factionDoor), Is.False, "No card, no entry.");
            });

            Assert.That(hands.TryPickupAnyHand(npc, companyCard), Is.True, "Precondition: the stranger picks up the card.");
            Assert.That(readers.IsAllowed(npc, factionDoor), Is.True, "The company's access opens a faction ship.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A lockable button's reader follows the ship like a door's: on a locked ship only the owner's card unlocks it,
    /// whatever access it was mapped with, a button built later is covered, and unlocking the ship gives its own
    /// access back.
    /// </summary>
    [Test]
    public async Task LockableButtonsFollowTheShipLock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var locks = entMan.System<LockSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid button = default, plainSwitch = default, owner = default, deed = default, stranger = default;
        Entity<WFShipAccessComponent> ship = default;
        await server.WaitPost(() =>
        {
            button = entMan.SpawnEntity(ButtonProto, map.GridCoords);
            plainSwitch = entMan.SpawnEntity("SignalButton", map.GridCoords);
            (owner, deed) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            GiveDeed(entMan, deed, grid);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 2);
            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            Assert.That(readers.IsAllowed(owner, button), Is.False, "Precondition: the owner's card lacks the button's mapped access.");
            access.SetLocked(ship, true);
        });

        EntityUid laterButton = default;
        await server.WaitPost(() => laterButton = entMan.SpawnEntity(ButtonProto, map.GridCoords));
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, button), Is.True, "Locking the ship takes the button's reader over.");
                Assert.That(ReaderLocked(entMan, laterButton), Is.True, "A button built on a locked ship is covered too.");
                Assert.That(entMan.HasComponent<AccessReaderComponent>(plainSwitch), Is.False, "A button without a lock is left alone.");
                Assert.That(readers.IsAllowed(owner, button), Is.True, "The deed's record key is admitted.");
                Assert.That(readers.IsAllowed(stranger, button), Is.False, "A stranger is not.");
            });

            Assert.That(locks.TryUnlock(button, stranger), Is.False, "A stranger cannot unlock the button.");
            Assert.That(locks.IsLocked(button), Is.True);
            Assert.That(locks.TryUnlock(button, owner), Is.True, "The owner unlocks the button.");
            Assert.That(locks.IsLocked(button), Is.False);

            access.SetLocked(ship, false);
            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, button), Is.False, "Unlocking the ship gives the button its own access back.");
                Assert.That(entMan.GetComponent<AccessReaderComponent>(button).AccessLists.Single(), Does.Contain(new ProtoId<AccessLevelPrototype>(SecureAccess)));
                Assert.That(readers.IsAllowed(owner, button), Is.True, "The owner's card still works on the unlocked ship.");
                Assert.That(readers.IsAllowed(stranger, button), Is.False, "A stranger still needs the button's own access.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// On an unlocked ship a door that asks for access of its own keeps it, and the owner and the allow list open it
    /// too. A door that asks for nothing stays open to all, access changed while unlocked is kept, and leaving the
    /// ship takes the crew's keys off again.
    /// </summary>
    [Test]
    public async Task UnlockedShipStillAdmitsTheCrew()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, openDoor = default, owner = default, deed = default, crew = default, stranger = default, officer = default;
        Entity<WFShipAccessComponent> ship = default;
        Entity<AccessReaderComponent> reader = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            openDoor = entMan.SpawnEntity(DoorProto, map.GridCoords);
            Assert.That(readers.GetMainAccessReader(door, out var found), Is.True, "Precondition: the door has electronics.");
            reader = found!.Value;
            readers.SetAccesses(reader, reader.Comp, new List<ProtoId<AccessLevelPrototype>> { SecureAccess });

            (owner, deed) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            GiveDeed(entMan, deed, grid);
            (crew, _) = SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            (stranger, _) = SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            (officer, var officerCard) = SpawnPersonWithCard(entMan, hands, map, "Cass Reyes", 4);
            entMan.System<SharedAccessSystem>().TrySetTags(officerCard, new List<ProtoId<AccessLevelPrototype>> { SecureAccess });

            ship = (grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, false);
            Assert.That(access.TryAddPerson(ship, crew), Is.True, "Precondition: the crew member's card is listed.");
        });

        await server.WaitAssertion(() =>
        {
            bool Allowed(EntityUid user) => readers.IsAllowed(user, door);

            Assert.Multiple(() =>
            {
                Assert.That(ReaderLocked(entMan, door), Is.False, "An unlocked ship does not lock the door.");
                Assert.That(Allowed(officer), Is.True, "The door's own access still opens it.");
                Assert.That(Allowed(owner), Is.True, "The deed opens a door that asks for access, on an unlocked ship too.");
                Assert.That(Allowed(crew), Is.True, "So does a listed card.");
                Assert.That(Allowed(stranger), Is.False, "A stranger still needs the door's own access.");
                Assert.That(readers.IsAllowed(stranger, openDoor), Is.True, "A door that asks for nothing stays open to all.");
                Assert.That(entMan.HasComponent<WFShipReaderBackupComponent>(openDoor), Is.False, "And is left alone.");
            });

            // Locking and unlocking again brings the same access back.
            access.SetLocked(ship, true);
            Assert.That(Allowed(officer), Is.False, "Locked, the door's own access no longer opens it.");
            access.SetLocked(ship, false);
            Assert.Multiple(() =>
            {
                Assert.That(Allowed(officer), Is.True);
                Assert.That(Allowed(crew), Is.True);
                Assert.That(Allowed(stranger), Is.False);
            });

            Assert.That(access.RemoveEntry(ship, ship.Comp!.AllowList.Single().Key), Is.True);
            Assert.That(Allowed(crew), Is.False, "A card taken off the list is a stranger's again.");

            // An access configurator run over the unlocked door: the new access is the door's own from then on.
            readers.SetAccesses(reader, reader.Comp!, new List<ProtoId<AccessLevelPrototype>> { CompanyAccess });
            access.SetLocked(ship, true);
            access.SetLocked(ship, false);
            Assert.Multiple(() =>
            {
                Assert.That(reader.Comp!.AccessLists.Single(), Does.Contain(new ProtoId<AccessLevelPrototype>(CompanyAccess)), "Access changed while unlocked is kept.");
                Assert.That(Allowed(officer), Is.False, "The old access is gone with it.");
                Assert.That(Allowed(owner), Is.True);
            });

            // Off the ship, the reader is its own again.
            entMan.RemoveComponent<WFShipAccessComponent>(grid);
            access.RefreshReader(door);
            Assert.Multiple(() =>
            {
                Assert.That(Allowed(owner), Is.False, "Without ship access the owner's key is taken off the door.");
                Assert.That(reader.Comp!.AccessLists.Single(), Does.Contain(new ProtoId<AccessLevelPrototype>(CompanyAccess)));
                Assert.That(entMan.HasComponent<WFShipReaderBackupComponent>(reader), Is.False, "The backup goes once restored.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// With no crew record on the deed card or the buyer's card, no door can be keyed to the owner, so a new ship is
    /// left unlocked rather than shutting its buyer out.
    /// </summary>
    [Test]
    public async Task PurchaseWithoutOwnerKeyStaysUnlocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitPost(() =>
        {
            var buyer = entMan.SpawnEntity(HumanProto, map.GridCoords);
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, buyer));
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);
            var access = entMan.System<WFShipAccessServerSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(comp.Locked, Is.False, "A ship no card can own is left unlocked.");
                Assert.That(access.CanLock((grid, comp)), Is.False);
                Assert.That(access.TrySetLocked(grid, true, out var refused), Is.True, "The console verb is handled here.");
                Assert.That(refused, Is.True, "The verb is told the lock was refused, so it doesn't report a lock.");
                Assert.That(comp.Locked, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Owner keys follow the deed: when it moves to another card, the readers are rewritten for that card.</summary>
    [Test]
    public async Task OwnerKeysFollowTheDeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, first = default, firstCard = default, second = default, secondCard = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            (first, firstCard) = SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            (second, secondCard) = SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            GiveDeed(entMan, firstCard, grid);
            access.SetLocked((grid, entMan.EnsureComponent<WFShipAccessComponent>(grid)), true);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(readers.IsAllowed(first, door), Is.True, "Precondition: the first deed holder is the owner.");
            Assert.That(readers.IsAllowed(second, door), Is.False, "Precondition: the second is a stranger.");

            // The deed is written onto the second card and taken off the first, as a records console would.
            entMan.RemoveComponent<ShuttleDeedComponent>(firstCard);
            GiveDeed(entMan, secondCard, grid);
        });
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(readers.IsAllowed(second, door), Is.True, "The new deed card owns the ship.");
                Assert.That(readers.IsAllowed(first, door), Is.False, "The old card no longer does.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The XAML loads, and with no grid the tab reads as read-only with an empty list.</summary>
    [Test]
    public async Task ScreenLoadsAndShowsEmptyState()
    {
        // The client only has entity systems while it is in game, and the screen resolves one in its constructor.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var screen = new ShipAccessScreen();
            screen.SetShuttle(null);
            screen.SetConsole(null);
            screen.Refresh();
            Assert.Multiple(() =>
            {
                Assert.That(Find<Label>(screen, "ReadOnlyLabel").Visible, Is.True, "Without a deed the tab is read-only.");
                Assert.That(Find<BoxContainer>(screen, "AllDoorsBox").Visible, Is.False, "Without a deed there is no set-all control.");
                Assert.That(Find<Label>(screen, "AllowListEmptyLabel").Visible, Is.True, "An empty allow list says so.");
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The client predicts a locked ship's door the way the server decides it. Record keys point at a station,
    /// which lives off the map; unless the client has that station, it drops the door's keys and turns its own
    /// card's key invalid, so it buzzes a deny the server then overrules by opening the door.
    /// </summary>
    [Test]
    public async Task ClientPredictsLockedDoorsLikeTheServer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (_, body) = await AttachPlayer(pair, map, "Ben Ortiz");

        EntityUid door = default;
        await server.WaitPost(() =>
        {
            // A records holder off the map that is not a station, as Frontier's sector records service is: that
            // is what a spawning player's card is keyed to.
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationRecordsComponent>(station);

            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            var card = entMan.SpawnEntity(CardProto, map.GridCoords);
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(4, station));
            Assert.That(hands.TryPickupAnyHand(body, card), Is.True, "Precondition: the player picks up their card.");

            var ship = new Entity<WFShipAccessComponent>(grid, entMan.EnsureComponent<WFShipAccessComponent>(grid));
            access.SetLocked(ship, true);
            Assert.That(access.TryAddPerson(ship, body), Is.True, "Precondition: the player's card is on the allow list.");
            Assert.That(entMan.System<AccessReaderSystem>().IsAllowed(body, door), Is.True, "Precondition: the server lets the player through.");
        });
        await pair.RunTicksSync(10);

        var clientDoor = pair.ToClientUid(door);
        await pair.Client.WaitAssertion(() =>
        {
            var clientEnt = pair.Client.EntMan;
            var local = pair.Client.Session!.AttachedEntity;
            Assert.That(local, Is.Not.Null, "Precondition: the client has its body.");
            Assert.That(clientEnt.System<AccessReaderSystem>().GetMainAccessReader(clientDoor, out var reader), Is.True, "Precondition: the client has the door's reader.");
            Assert.Multiple(() =>
            {
                Assert.That(reader!.Value.Comp.AccessKeys, Is.Not.Empty, "The client keeps the door's record keys.");
                Assert.That(clientEnt.System<AccessReaderSystem>().IsAllowed(local!.Value, clientDoor), Is.True, "The client predicts the door opening, as the server decides.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The deed's ship link reaches the client, so the player carrying the deed gets the editable tab. Without it
    /// every client saw an empty deed and the tab stayed read-only.
    /// </summary>
    [Test]
    public async Task ClientSeesTheDeedAndGetsTheEditableTab()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var (_, body) = await AttachPlayer(pair, map, "Ada Vance");

        await server.WaitPost(() =>
        {
            var card = entMan.SpawnEntity(CardProto, map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(body, card), Is.True, "Precondition: the player picks up their card.");
            GiveDeed(entMan, card, grid);
            var comp = entMan.EnsureComponent<WFShipAccessComponent>(grid);
            comp.Locked = true;
            entMan.Dirty(grid, comp);
        });
        await pair.RunTicksSync(10);

        var clientGrid = pair.ToClientUid(grid);
        await pair.Client.WaitAssertion(() =>
        {
            var clientEnt = pair.Client.EntMan;
            var local = pair.Client.Session!.AttachedEntity;
            Assert.That(local, Is.Not.Null, "Precondition: the client has its body.");
            Assert.That(clientEnt.System<WFShipAccessSystem>().HasDeedFor(local!.Value, clientGrid), Is.True, "The client knows its card holds this ship's deed.");

            using var screen = new ShipAccessScreen();
            screen.SetShuttle(clientGrid);
            screen.Refresh();
            Assert.Multiple(() =>
            {
                Assert.That(Find<Label>(screen, "ReadOnlyLabel").Visible, Is.False, "The deed holder's tab is not read-only.");
                Assert.That(Find<CheckBox>(screen, "LockedCheck").Visible, Is.True, "The deed holder can flip the lock.");
                Assert.That(Find<OptionButton>(screen, "ShipModeButton").Visible, Is.True);
                Assert.That(Find<OptionButton>(screen, "ShipModeButton").ItemCount, Is.EqualTo(3), "Civilian owners choose Private, Crew or Company.");
                Assert.That(Find<OptionButton>(screen, "ShipModeButton").GetItemId(1), Is.EqualTo((int) WFShipAccessMode.Crew));
                Assert.That(Find<OptionButton>(screen, "ShipModeButton").ToolTip, Does.Contain("Crew Setup"));
                Assert.That(Find<BoxContainer>(screen, "AllDoorsBox").Visible, Is.True, "The deed holder gets the set-all control.");
                Assert.That(Find<OptionButton>(screen, "AllDoorsRuleButton").ItemCount, Is.EqualTo(9), "Every rule is offered for all doors.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The access page's one-line notices, by control name.</summary>
    private static readonly string[] Notices =
    {
        "ReadOnlyLabel", "CodeAlertLabel", "AllowListEmptyLabel", "NearbyEmptyLabel", "DoorNoneLabel", "DoorPlayersEmptyLabel",
    };

    /// <summary>
    /// The notices the page can show together: the deed holder sees the code alert and the empty lists of a picked door,
    /// anyone else sees the read-only notice and no door picked. The owner-only parts are hidden from everyone else.
    /// </summary>
    private static readonly string[][] NoticeStates =
    {
        new[] { "CodeAlertLabel", "AllowListEmptyLabel", "NearbyEmptyLabel", "DoorPlayersEmptyLabel" },
        new[] { "ReadOnlyLabel", "AllowListEmptyLabel", "DoorNoneLabel" },
    };

    /// <summary>The owner-only boxes that hold some of the notices.</summary>
    private static readonly string[] OwnerBoxes = { "CodeBox", "NearbyBox", "AllDoorsBox", "DoorBox", "DoorPlayersBox" };

    /// <summary>
    /// The notices on the access page, among them "only the owner can change who may board", wrap onto as many lines
    /// as their column needs instead of being clipped, at the helm's smallest and default sizes in both skins.
    /// </summary>
    [Test]
    public async Task NoticesAreNotClippedAtTheHelmSizes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var access = helm.FindControl<ShipAccessScreen>("AccessContainer");
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { helm.MinSize, ShuttleConsoleWindow.WfDefaultSize })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Access);
                    access.SetShuttle(null);
                    access.Refresh();

                    foreach (var state in NoticeStates)
                    {
                        foreach (var name in OwnerBoxes)
                            Find<Control>(access, name).Visible = false;
                        foreach (var name in Notices)
                            Find<WFWrappedLabel>(access, name).Visible = false;
                        foreach (var name in state)
                        {
                            for (Control? parent = Find<WFWrappedLabel>(access, name); parent != null && parent != access; parent = parent.Parent)
                                parent.Visible = true;
                        }

                        Find<WFWrappedLabel>(access, "CodeAlertLabel").Text = Loc.GetString("ship-access-code-alert", ("misses", 3), ("locked", 1));
                        helm.SetSize = size;
                        foreach (var control in Tree(helm))
                            control.InvalidateMeasure();
                        helm.Measure(size);
                        helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        foreach (var name in state)
                            AssertShownInFull(Find<WFWrappedLabel>(access, name), ui, $"{skin}, {size}, {string.Join("+", state)}, {name}");
                    }
                }
            }
            finally
            {
                helm.Close();
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A wrapped label breaks at spaces to its width, keeps every word and splits a word wider than the line.</summary>
    [Test]
    public async Task WrappedLabelBreaksToItsWidth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var label = new WFWrappedLabel { Text = Loc.GetString("ship-access-read-only"), HorizontalExpand = true };
            var longWord = new WFWrappedLabel { Text = new string('W', 60), HorizontalExpand = true };
            var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            box.AddChild(label);
            box.AddChild(longWord);
            ui.WindowRoot.AddChild(box);
            try
            {
                foreach (var width in new[] { 120f, 200f, 4000f })
                {
                    var size = new Vector2(width, 400);
                    box.SetSize = size;
                    box.InvalidateMeasure();
                    box.Measure(size);
                    box.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    AssertShownInFull(label, ui, $"width {width}");
                    if (width == 120f)
                        Assert.That(label.Lines.Count, Is.GreaterThan(1), "A narrow column wraps the notice.");
                    if (width == 4000f)
                        Assert.That(label.Lines.Count, Is.EqualTo(1), "A wide column keeps the notice on one line.");

                    var font = FontOf(longWord, ui);
                    Assert.That(string.Concat(longWord.Lines), Is.EqualTo(longWord.Text), $"width {width}: a long word loses nothing");
                    foreach (var line in longWord.Lines)
                        Assert.That(TextWidth(font, line, longWord.UIScale), Is.LessThanOrEqualTo(longWord.PixelWidth + 1), $"width {width}: a split word stays inside the label");
                }
            }
            finally
            {
                ui.WindowRoot.RemoveChild(box);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>All the label's text shows: every word kept, each line inside the label's width and the label tall enough for its lines.</summary>
    internal static void AssertShownInFull(WFWrappedLabel label, IUserInterfaceManager ui, string context)
    {
        var font = FontOf(label, ui);
        var scale = label.UIScale;
        var lines = label.Lines;
        Assert.That(label.VisibleInTree, Is.True, $"{context}: the notice must be on screen");
        Assert.That(label.PixelWidth, Is.GreaterThan(0), context);
        Assert.That(lines, Is.Not.Empty, context);
        Assert.That(string.Join(" ", lines), Is.EqualTo(label.Text), $"{context}: wrapping must keep every word");
        foreach (var line in lines)
            Assert.That(TextWidth(font, line, scale), Is.LessThanOrEqualTo(label.PixelWidth + 1), $"{context}: \"{line}\" runs past the label");

        var needed = font.GetHeight(scale) + font.GetLineHeight(scale) * (lines.Count - 1);
        Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(needed), $"{context}: the label has no room for its {lines.Count} lines");
    }

    /// <summary>The font the label draws with: its own, else the stylesheet's, else the theme default.</summary>
    private static Font FontOf(Label label, IUserInterfaceManager ui)
    {
        return label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
    }

    private static int TextWidth(Font font, string text, float scale)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += font.GetCharMetrics(rune, scale)?.Advance ?? 0;
        return width;
    }

    /// <summary>Moves the client's player, with a new mind, into a named MobHuman on the map.</summary>
    private static async Task<(ICommonSession Session, EntityUid Body)> AttachPlayer(TestPair pair, TestMapData map, string name)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity(HumanProto, map.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(body, name);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the body.");
        return (session, body);
    }

    /// <summary>
    /// A human with a fixed name holding a blank ID card whose crew record key is <paramref name="recordId"/> on
    /// the test map, as the station records give a spawning player. Server thread only.
    /// </summary>
    internal static (EntityUid Person, EntityUid Card) SpawnPersonWithCard(IEntityManager entMan, SharedHandsSystem hands, TestMapData map, string name, uint recordId)
    {
        var person = entMan.SpawnEntity(HumanProto, map.GridCoords);
        entMan.System<MetaDataSystem>().SetEntityName(person, name);
        var card = entMan.SpawnEntity(CardProto, map.GridCoords);
        entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
        entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(recordId, map.MapUid));
        Assert.That(hands.TryPickupAnyHand(person, card), Is.True, $"Precondition: {name} picks up their card.");
        return (person, card);
    }

    /// <summary>
    /// Puts this ship's deed on a card, as the shipyard does at purchase. The deed's fields are write-restricted
    /// to the shipyard systems, so reflection stands in for them here.
    /// </summary>
    internal static void GiveDeed(IEntityManager entMan, EntityUid card, EntityUid grid)
    {
        var deed = entMan.EnsureComponent<ShuttleDeedComponent>(card);
        typeof(ShuttleDeedComponent).GetField(nameof(ShuttleDeedComponent.ShuttleUid))!.SetValue(deed, grid);
        entMan.Dirty(card, deed);
    }

    /// <summary>Whether a door or locker's main reader wants the locked-ship access, which is how the module shuts it.</summary>
    internal static bool ReaderLocked(IEntityManager entMan, EntityUid uid)
    {
        return entMan.System<AccessReaderSystem>().GetMainAccessReader(uid, out var reader)
            && reader.Value.Comp.AccessLists.Any(set => set.Contains(WFShipAccessServerSystem.LockedAccess));
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Tree(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Tree(child))
            yield return descendant;
    }
}
