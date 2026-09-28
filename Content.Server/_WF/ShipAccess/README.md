# ShipAccess

Per-card access control for purchased ships, built on the normal airlock access system: every door and locker on
the ship is gated by its own access reader (an airlock's door electronics), and this module only decides what
that reader holds. Whoever carries the ship's deed is its owner, on an ID card or, for a ship bought with one,
the voucher held in hand; the owner keeps an allow list of ID cards, and in Faction mode the company's access
levels also open the ship. While a ship is locked, only those cards open its doors and lockers. Unlocked, every
door gets back the access its electronics had.

How the readers are set: a locked door requires `WFShipLocked`, an access level no ID card can carry (the admin
ghost carries it in its own access, so it opens ship doors like every other airlock), and is
given the crew record keys (the station record key written on each ID card) of the deed card and the listed
cards, plus one list per company access level on a faction ship. That is exactly how a station airlock keyed to
one person works, so the door denies, is hacked (access wire), put on emergency access, emagged and opened by
the AI the same way as any airlock, with no popups of its own. The reader's own access is kept in
`WFShipReaderBackupComponent`, on the reader itself (the board), while the ship holds it, and put back on
unlock, when the door or locker leaves the ship, or on resale. Once something else rewrites a reader the ship had
locked (an emag, an access configurator) the ship leaves that reader alone, as the change would stick on any
airlock. Mono's deed reader (`ShipAccessReaderComponent`) is switched off on these ships.

Clients predict these doors like the server because every holder of crew records is force-sent to them
(`WFShipAccessServerSystem.Stations.cs`): a record key names its holder, a station or on Frontier the sector
records service, which lives off the map where PVS never sends it. Without it the client drops the door's keys,
voids the player's own, and predicts a deny that the server then overrules by opening the door.

Owner keys come from the ID cards and vouchers that hold the ship's deed, rechecked every two seconds so a moved
deed takes effect. When no ID card holding the deed has a crew record (a voucher purchase), the buyer's card,
noted at purchase, also counts, so the captain's own card still opens the ship with the voucher put away. With
no owner key at all the ship is not locked at purchase and the console refuses to lock it, since that would
shut the owner out.

Ships the admin tools spawn are registered to players' accounts (`OwnerUsers`), so they stay theirs whatever
body they play, a ghost included. The vessel spawner registers the chosen owner and, when their living body
carries an ID card free of deeds, also puts the deed on it as a purchase would; a ghost, a player in the lobby
or a card that already holds a deed gets the registration alone, with no deed and the consoles left unlocked.
The ERT builder registers each responder to the team's ship as they take their place, and gives them the crew
record a normal spawn gets on the sector records service, which their card needs to be keyed to a door. A
registered player edits the Access tab and uses the console verbs like the deed holder, and the card they wear
(the ID slot, not one they hold) is an owner key while they are in the game. Lockers and crates that came with the ship are taken over whatever they ask for;
storage brought aboard later is only taken over when it asks for no access, so the ship never unlocks someone
else's secure locker for its owner.

The card is the key: steal a listed card and you get in, hand the deed over and you hand the ship over. Because
the key is the card's crew record, a replacement card printed for the same record opens the same doors, and a
card with no crew record (a blank card) can't be listed. The lists are round state, as record keys point at
this round's stations, and are not saved with the grid. The tab shows the buyer's name for reference only.

Players use the Access tab on the shuttle console: it shows who the ship is registered to, its mode and lock,
and lets the deed holder lock the ship, add the card of a humanoid standing near the console, remove cards and
mark them as builders' (stored only for now). A ship bought before this module existed gets its access record
the first time its deed holder edits it. Buying a ship locks it when `wf.shipaccess.lock_new_ships` is true
(the default). Doors and lockers built on a ship later have their readers set on the next tick, once their
electronics are in.

The old console verbs keep working and map onto this: granting guest access also puts the guest's card on the
allow list, resetting guest access also clears the allow list, and the lock verb flips the grid's lock.

## Per-door rules

Each door can override the ship rule with `WFDoorAccessRuleComponent`, saved with the grid: Ship default,
Deed only, Chosen IDs (the deed plus allow-listed cards ticked for that door), Code, Chosen IDs or code,
Public and Sealed. Every rule but Ship default is written into the door's reader whether or not the ship is
locked; Public empties it, so anyone opens the door whatever its electronics asked for. Sealed closes and bolts
the door for everyone, the owner included; picking another rule unbolts it, and bolts that sealing added are
removed again. A door sealed while open is scheduled to close after two seconds, with the door's usual retries,
and bolted once it is shut (bolts dropped mid-close would cancel the close and leave it bolted open); an
unpowered one bolts when power returns, and the console says so. Undocking and FTL arrivals lift dock airlock
bolts on their own, so a sealed dock airlock is bolted again; other sealed doors keep whatever a wire or remote
does to their bolts. Lockers keep the ship rule. Cards dropped from the allow list are dropped from every door
they were ticked on.

The Access tab shows the ship outline with each door as a node in its rule's colour (the same nav map the Ship
tab uses, sized to the tab instead of the nav map's fixed square) and a legend; clicking a node selects the door
and shows its name, rule and, for the people rules, checkboxes over the allow list. Firelocks are left out, on
the diagram and at the server. Only the owner can edit; others get a read-only view. The diagram is built
client-side from the doors the client knows about, so doors outside the client's view range on a very large
ship appear once the player has been near them.

Decisions taken while building F3.2: a Code door admits only the deed at the reader (chosen cards count
only under Chosen IDs or code); Sealed is applied on map init as well, so a saved sealed door comes back
bolted; bolts need power, as everywhere in the game, so an unpowered sealed door is refused at the reader only
and bolts the moment it is powered; `ShipViewControl` was unsealed so the diagram reuses its hull drawing and
fit.

## Codes and the keypad

The deed holder can set a four-digit ship code and, on any Code or Chosen-IDs-or-code door, a code of the
door's own; both open such a door. Someone the door's reader does not admit right-clicks it and picks Enter
Code, which opens a keypad. The server repeats the verb's checks, since a client can send a code without it:
the person must be able to act and reach the door within two and a half tiles with nothing solid in between,
and the door must still have a code rule and be closed and unbolted. A matching code stands in for the card the
reader wants; every other door check still applies, so the door needs power and must not be welded. Wrong
codes count per person per ship: five inside ten minutes lock that person out of every keypad on that ship for
fifteen minutes. Every miss and lockout is admin-logged, and the owner's Access tab shows an alert line with
the failed attempts and the people locked out. Changing a code never affects anyone already inside. Reselling a
ship on the used market clears the ship code, every door code and rule, lifts any seal and gives every reader
its own access back; a seal lifted without power unbolts when power returns.

Codes never travel on a networked component. The ship code and the lockout table live in the server-only
`WFShipAccessCodeComponent` on the grid; a door's own code is a server-only `WFDoorCodeComponent` on the door,
so it saves and moves with the door, and the networked rule component only carries `HasOwnCode`. The owner's
console asks for the codes when the Access tab opens and gets them in a directed network event; the tab
forgets them when it closes or switches ship, shows them masked with a Show toggle, and nobody else ever
receives them.

Decisions taken while building F3.3: the Enter Code verb sits on the door's rule component, because the
prying system already owns the alternative-verb pair on `DoorComponent`; a door with its own code takes the
ship code as well, as the design table says ("its own, or the ship code"); misses and lockouts are counted per
character name, so an NPC can lock itself out too; lockouts are round state and are not saved with the grid;
the lockout alert only reaches whoever carries the deed, by the same directed event path as the codes.

Entry points: `WFShipAccessComponent` (grid), `WFDoorAccessRuleComponent` (door), `WFShipAccessCodeComponent`
and `WFDoorCodeComponent` (server-only codes), `WFShipReaderBackupComponent` (a reader's own access while the
ship holds it), `WFShipAccessSystem` (shared reads: deed holder, card keys, faction), `WFShipAccessServerSystem`
(purchase, lock, allow list, door readers, door rules, codes, keypad verb, console messages, verb bridge),
`ShipAccessScreen` (the console tab), `ShipAccessDoorMapControl` (the door diagram), `WFShipAccessClientSystem`
and `ShipAccessKeypadWindow` (the keypad). The `WFShipLocked` access level is in
`Resources/Prototypes/_WF/ShipAccess/access.yml`. The admin vessel spawner and ERT builder
(`Content.Server/_WF/Administration`) register their ships through `SetupRegisteredShip` and `AddOwnerUser`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/ShipAccess/WFShipAccessCodeComponent.cs`](WFShipAccessCodeComponent.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Codes.cs`](WFShipAccessServerSystem.Codes.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Console.cs`](WFShipAccessServerSystem.Console.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.cs`](WFShipAccessServerSystem.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Doors.cs`](WFShipAccessServerSystem.Doors.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Readers.cs`](WFShipAccessServerSystem.Readers.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Stations.cs`](WFShipAccessServerSystem.Stations.cs)
- [`Content.Server/_WF/ShipAccess/WFShipReaderBackupComponent.cs`](WFShipReaderBackupComponent.cs)

### Shared

- [`Content.Shared/_WF/ShipAccess/WFDoorAccessRuleComponent.cs`](../../../Content.Shared/_WF/ShipAccess/WFDoorAccessRuleComponent.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessComponent.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessComponent.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessEvents.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessEvents.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessSystem.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessSystem.cs)

### Client

- [`Content.Client/_WF/ShipAccess/ShipAccessDoorMapControl.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessDoorMapControl.cs)
- [`Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml`](../../../Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml)
- [`Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml.cs)
- [`Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml`](../../../Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml)
- [`Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml.cs)
- [`Content.Client/_WF/ShipAccess/ShuttleConsoleBoundUserInterface.ShipAccess.cs`](../../../Content.Client/_WF/ShipAccess/ShuttleConsoleBoundUserInterface.ShipAccess.cs)
- [`Content.Client/_WF/ShipAccess/ShuttleConsoleWindow.ShipAccess.cs`](../../../Content.Client/_WF/ShipAccess/ShuttleConsoleWindow.ShipAccess.cs)
- [`Content.Client/_WF/ShipAccess/WFShipAccessClientSystem.cs`](../../../Content.Client/_WF/ShipAccess/WFShipAccessClientSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessCodeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessCodeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessDoorRuleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessDoorRuleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessOwnerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessOwnerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/ShipAccess/access.yml`](../../../Resources/Prototypes/_WF/ShipAccess/access.yml)

### Localization

- [`Resources/Locale/en-US/_WF/ShipAccess/ship-access.ftl`](../../../Resources/Locale/en-US/_WF/ShipAccess/ship-access.ftl)

## Non-modular edits

- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs)
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml)
  - xmlns:access for the access tab
  - access tab button
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs): access tab
- [`Content.Client/UserInterface/Controls/MapGridControl.xaml.cs`](../../../Content.Client/UserInterface/Controls/MapGridControl.xaml.cs)
  - virtual, so the door map can centre its drawing in a control of any size
  - virtual, so the door map can fit the hull to its own shorter side
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs): the ship link is networked, and this card may already have held a deed
- [`Content.Server/_NF/ShuttleRecords/ShuttleRecordsSystem.Console.cs`](../../_NF/ShuttleRecords/ShuttleRecordsSystem.Console.cs): the ship link is networked, and this card may already have held a deed
- [`Content.Server/Administration/Systems/AdminVerbSystem.Tools.cs`](../../Administration/Systems/AdminVerbSystem.Tools.cs): all access is not a key to every locked ship
- [`Content.Server/Sandbox/SandboxSystem.cs`](../../Sandbox/SandboxSystem.cs): all access is not a key to every locked ship
- [`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`](../../Shuttles/Systems/ShuttleConsoleLockSystem.cs)
  - a guest's card also joins the allow list
  - a held voucher with the deed, or a player the ship is registered to, holds deed access too
  - resetting guests also empties the allow list, and that alone counts as a reset
  - a ship with Wolfgate access keeps its lock on the grid, not in Mono's readers
  - Locked on the grid is the source of truth and flips the readers itself
- [`Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs`](../../../Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs)
  - AutoGenerateComponentState, so the client knows which ship a deed is for
  - the access tab and door prediction read it on the client
- [`Content.Shared/Access/Systems/AccessReaderSystem.cs`](../../../Content.Shared/Access/Systems/AccessReaderSystem.cs): dirty the reader whose lists were cleared (a door's electronics), or clients keep predicting its old access
- [`Resources/Prototypes/Entities/Mobs/Player/admin_ghost.yml`](../../../Resources/Prototypes/Entities/Mobs/Player/admin_ghost.yml): admin ghosts open locked ship doors, as they open every other airlock

<!-- WOLFGATE-GENERATED END -->
