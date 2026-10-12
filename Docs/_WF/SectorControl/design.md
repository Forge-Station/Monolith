# Sector control

Faction territory on the sector map: a hex grid over the sector, cells held by factions, drawn on the shuttle
console's MAP tab, the nav radar and the sector map window, with a claim API that drivers use. The first driver is the Ashfall Reaver
campaign (`Docs/_WF/ReaverCampaign/design.md`); ADS incursions and TSF/PDV sector control come later as drivers of
their own. This module knows nothing about strongholds, threat tiers or pirates.

## Model

**Hex grid.** Flat-top hexes on axial coordinates `(q, r)`, centred on the sector origin (0, 0), circumradius from
`wf.sector.cell_size` (3000 m; the sector's stations lie from the central cluster at 0 m out to about 27 km, which is
97 cells). Cells exist implicitly; only claimed, contested or recently active cells are stored. Every cell has a
callsign for announcements and the map (`WFSectorHex.Callsign(cell)`: a letter run for `q`, a number for `r`, such as
`K-7`), and a world position for its centre and corners.

**Faction.** A `wfSectorFaction` prototype: name (LocId), colour, the company it fights as (for standing and for who
is "theirs"), `blockades` (whether a station in a cell it holds is cut off from NPC freight; the Encounters
scheduler asks through `WFEncounterStationsEvent`), and optional `enteredNotice` and `leftNotice` (LocIds that replace
the default border notices for its space, with the same arguments).

**Claim.** `{ map, cell, faction, sources }`. A source is an entity (a stronghold encounter, later a station or a
flagship) that supports the claim; a cell stays held while any of its sources lives. `TryClaim(map, cell, faction,
source)` raises a cancellable `WFSectorClaimAttemptEvent` first, so protection rules veto once for every driver;
`Release(source)` frees every cell that source alone supported; `Free(map, cell)` frees one. A claim on a cell with
another owner fails (drivers contest explicitly).

**Protected cells.** `WFSectorProtectionSystem` vetoes claims on the origin cell and its ring, and on any cell that
holds a station whose name matches `wf.sector.protected_stations` (default `Halcyon`: the TSF flagship). Hidden
stations are not protected: an unclaimable hole in held space would give them away. A station is hidden when its name
matches `wf.encounters.hidden_stations` (PDV Helios) or its IFF has `Hide`, `HideLabel` or `HideLabelAlways`
(`WFEncounterSchedulerSystem.IsHidden`). `Stations()` leaves hidden ones out, so no driver names them, blockades them,
raids them or lists them as the nearest station, and their cell is claimed silently like open space. Drivers keep spawns
and routes out of them with the Encounters placement helper, which already knows the hidden clearance.

**Retaking.** Each second `CheckRetakes` looks at every held cell with a crewed player ship in it (the same ships as
disputes), no contest on it, and none of the holder's ships reported there within the presence timeout (a driver that
reports nothing counts as absent). Such a cell gets a countdown of `wf.sector.retake_minutes` (3, 0 is off); any break
in those conditions drops it and starts over. A driver can veto a cell with `WFSectorRetakeAttemptEvent`. At zero the
cell is freed, stamped active so a spreading faction waits out its quiet time, and `WFSectorCellRetakenEvent` is
raised. The countdown goes to clients in `WFSectorTerritoryEvent.Retakes` and the map pulses the cell in a neutral
colour with "Retaking m:ss" in place of its callsign; the hover line says so. This is the one place a parked ship
counts, and only against a holder with no presence there.

**Clearing.** `Free` (so a retake, a lost source, `Release` and `Clear` too) puts the cell in
`WFSectorTerritoryComponent.Clearing` until `wf.sector.clearing_minutes` (10, 0 is off) pass. Drones stay out of a clearing
cell exactly as out of a held one (`IsHeldOrClearing`, used by the DroneRings hook), the territory event carries the cell
and its end time in `Clearing`, and the map draws a dim neutral outline with "Clearing m:ss" in place of the callsign
(the hover line and blip card say drones return in that time). `WFSectorCellClearingEvent(map, cell, until, previous)`
fires when it starts, `WFSectorCellClearedEvent(map, cell)` when the time runs out; a claim on the cell ends the state
quietly. Wiping all territory (switch off, resize) starts no clearing. A contest on a clearing cell does not end it.

**Activity.** `WFSectorActivitySystem` scans every 60 s. A grid counts when it is on the sector map, has a living,
non-ghost player aboard, is not an encounter grid, is not a station (a `StationMember` grid with no `ShuttleDeed`) and
has moved at least 100 m since the last scan: parked, docked and AFK ships hold nothing. A ship that jumped (its cell
changed by more than one ring) marks the cell it left as well as the one it arrived in; a ship in FTL keeps its track
for three minutes, so a jump that a scan lands in the middle of still counts. `LastActivity(map, cell)`
answers when a cell was last active; `IsQuiet(map, cell, since)` is the driver's question.

**Contested cells.** A driver may `Contest(map, cell, faction, deadline)` instead of claiming at once. The cell is
shown as contested on the map with a countdown, `WFSectorCellContestedEvent` fires (drivers announce it), and at the
deadline the territory system claims it for the faction if the cell has been quiet since the contest began, or drops
the contest. For a station cell this is the blockade warning: traffic can hold the lane by flying it. A faction may
also contest a cell it already holds, to show a move on it (a Reaver founding convoy); the cell stays held.
`SetContestDeadline` moves a deadline and raises the contested event again; `DropContest` withdraws one.

**Disputed cells.** A driver reports where its faction's ships are every tick with
`WFSectorTerritorySystem.SetPresence(faction, map, cells)`; a report counts for five seconds. Once a second the
territory system marks a cell **disputed** when a faction has presence in it and a crewed player ship is in it
(`WFSectorActivitySystem.CellsWithCrewedShips`, by position with no moved rule), raises `WFSectorCellDisputedEvent(map,
cell, faction, disputed)` on each change and answers `IsDisputed(map, cell)`. While a cell is disputed its contest
is not decided and its deadline is pushed forward by the time since the last check, so the countdown holds, and its
activity stamp is refreshed, so a contest that ends after a dispute finds the cell flown and is dropped. The territory
event carries a `Disputed` list of `(cell, faction index)` for held or contested cells; the client hatches the cell in
the faction's colour at alpha 0.35, pulses its border, freezes its countdown, adds "disputed" to the hover card and a
legend line. The nav radar is unchanged.

**Notices.** `WFSectorNoticeSystem` tells those aboard a crewed player ship when it has been inside a faction's space
for 10 s ("WARNING: entering {faction} controlled space (K-7).") and when it has been back outside for 30 s ("Leaving
{faction} controlled space. Now in open space at K-7."), with a 2 min per-ship cooldown between entered notices (a
leave is always told once its dwell is up); the dwell is the hysteresis that keeps a ship skimming a border from being
told every few seconds. Every notice goes both ways: over the ship's PA
(`ShipPaSystem.Announce`) when it has a working speaker, and in chat, in the faction's colour, to everyone aboard the
PA did not reach (the PA puts its own line in the chat of those in a speaker's range), so each player aboard gets it
once. Entering plays the announcement sound in chat; leaving does not. A faction's `enteredNotice` and `leftNotice`
replace the default lines. `Tell(grid, message, sender, colour, sound)` is the same delivery for a driver's own
warnings. Sector control is the only framework code that touches ShipPa.

**Sync.** `WFSectorSyncSystem` sends `WFSectorTerritoryEvent` to everyone when territory changes and to a player when
they attach: the map, the cell size, the faction ids, the claims as `(q, r, faction index)`, the contested cells with
their deadlines, and whether it is the sector map. No grids or stations go on the wire: the sector map names what the
client already knows, as the console's MAP tab does. The sector map is sent on attach even with nothing held, so the
map has its grid before the first claim. One update without claims after the last claim goes, then silence; the
client drops a map when an event has no claims or contests, except the sector map, which it keeps empty. Drivers send `WFSectorStatusEvent`: per
faction, a legend line already localised on the server (a driver's tier and stronghold count are its own business;
the framework only shows the line).

**Drawing.** `ShuttleMapControl.SectorControl.cs` draws, right after the star system and before FTL ranges and map
objects: each held cell as a filled hex in its faction's colour at alpha 0.10 (one `DrawPrimitives` TriangleList per
faction), every edge whose neighbour has another owner as a border line at alpha 0.8, contested cells hatched or
outlined with their countdown, the cell callsign in held cells when zoomed in, and a legend at the top-left corner
(the cursor readout owns the bottom-left) with one line per faction from the status event. Edge segments are built
once when an event arrives. `ShuttleNavControl.SectorControl.cs` draws the border edges within the scope beneath grids
and contacts, so a pilot sees the line they are about to cross. Both hooks are one marked line in the upstream
control, like the Planets and Encounters hooks.

**Sector map.** `WFSectorMapControl` is a map of the sector alone, not the mass scanner, and it gives no intel the
console's MAP tab would not: a dark backing, zoom from 1.5 to 60 km half-width (fitted at first to the named grids
within 60 km, the player's ship and the territory, plus 3 km), the wheel zooming about the cursor, right-drag panning
and a left double-click centring on the player's own grid. It draws a faint outline (white at alpha 0.08, one LineList) of
every cell whose centre lies within 30 km of the origin (`WFSectorClientSystem.SectorRadius`; `WFSectorHex.CellsWithin`),
each shared edge once and none once a cell is under 6 px across; the territory over it through `WFSectorOverlay` (one
`worldToView` matrix a frame, `view = (world - centre) * scale` with Y flipped about the midpoint); the callsign of every
cell once a cell spans 60 px, held cells in their faction's colour and open ones in dim grey (a contested cell shows its
countdown instead); the grids the MAP tab names without a scan, by its own rule on the client's grids
(`WFSectorClientSystem.CollectLandmarks`): none on a beacon map, none with IFF `Hide` or `HideLabelAlways`, none with
`HideLabel` (the map has no console to detect them with), never the player's own ship, each as a dot, named only
while the cursor is on it; one marker per visible encounter at its first ship, as the
MAP tab lists them, in its category's glyph and colour, named only while hovered; the player's grid as a white
triangle along its heading; and, once the cursor has rested half a second on one cell (not while panning), the cell's callsign, its distance from
the origin and its owner ("K-7 (12.4 km): open space", or "contested by X, m:ss"). With no view of the map it shows only "No territory data".
`WFSectorMapPanel` puts it over a bar with "Centre on me", "Whole sector" and the scale. It opens several ways:
`WFSectorMapWindow` (resizable, 900x700) from the top-bar button beside the guidebook or its key (`OpenSectorMap`, Shift+N by
default, rebindable), "Sector map" in the game menu or the client command `wf_sectormap`, all through
`WFSectorMapUIController`, for every player, ghosts included (a ghost is never "aboard" a ship, so "Centre on me" centres
on the ghost and a white ring marks it); the window follows the player's map (or any map the client has, the sector map
first). The shuttle console has no sector mode: its MAP tab already shows the territory.

**Admin.** `wf_sector status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | clear
[faction] | callsign <x> <y>` (`LocalizedEntityCommands`, `AdminFlags.Admin`).

**State and lifecycle.** Claims, contests and activity live on `WFSectorTerritoryComponent` on the sector map entity,
so VV shows them, and are reset on `RoundRestartCleanupEvent`. Nothing runs unless `wf.sector.enabled` (default true)
and the round is in progress.

## Public API

`WFSectorTerritorySystem`: `CellOf(MapCoordinates)`, `Centre(map, cell)`, `Owner(map, cell)`, `TryClaim`, `Release`,
`Free`, `Contest`, `SetContestDeadline`, `DropContest`, `Held(faction)`, `Frontier(faction)` (unowned cells adjacent to held ones), `Sources(map, cell)`,
`StationsIn(map, cell)`. `WFSectorActivitySystem`: `LastActivity`, `IsQuiet`, `ActiveShips(map, cell)`. Local events
in `WFSectorEvents.cs`: `WFSectorClaimAttemptEvent` (cancellable, by ref), `WFSectorCellChangedEvent`,
`WFSectorCellContestedEvent`, `WFSectorContestResolvedEvent`, `WFSectorRetakeAttemptEvent` (cancellable),
`WFSectorCellRetakingEvent`, `WFSectorCellRetakenEvent`, `WFSectorCellClearingEvent`, `WFSectorCellClearedEvent`, and `WFSectorResetEvent`, raised when territory comes back on
after being switched off or resized, so drivers can claim what they still hold again.

## Module layout

- `Content.Shared/_WF/SectorControl`: `WFSectorHex.cs`, `WFSectorCell.cs` (the `(q, r)` struct), `WFSectorFactionPrototype.cs`,
  `WFSectorNetEvents.cs`, `SectorControlCVars.cs`.
- `Content.Server/_WF/SectorControl`: `README.md`, `WFSectorEvents.cs`, `Components/WFSectorTerritoryComponent.cs`,
  `Systems/WFSectorTerritorySystem.cs`, `Systems/WFSectorActivitySystem.cs`, `Systems/WFSectorProtectionSystem.cs`,
  `Systems/WFSectorSyncSystem.cs`, `Systems/WFSectorNoticeSystem.cs`, `Commands/WFSectorCommand.cs`.
- `Content.Client/_WF/SectorControl`: `WFSectorClientSystem.cs`, `WFSectorOverlay.cs`, `ShuttleMapControl.SectorControl.cs`,
  `ShuttleNavControl.SectorControl.cs`, `WFSectorMapControl.cs`, `WFSectorMapPanel.cs`, `WFSectorMapWindow.cs`,
  `WFSectorMapUIController.cs`, `WFSectorMapCommand.cs`.
- `Resources/Locale/en-US/_WF/SectorControl/sectorcontrol.ftl`. The module ships no faction; each driver defines its
  own `wfSectorFaction` (the Reavers' is in `Resources/Prototypes/_WF/ReaverCampaign/factions.yml`), and the tests
  declare theirs.
- Tests: `Content.Tests/_WF/SectorControl/WFSectorHexTest.cs`; `Content.IntegrationTests/Tests/_WF/SectorControl/`
  (claims and sources, protection veto, activity with the station and moved rules, contest deadline, sync event,
  notices in chat with and without a PA speaker, a leave told inside the cooldown, no grids on the wire, the sector
  map kept empty, the cell grid and callsigns, the grids named by the MAP tab's rule, one marker per encounter, the map
  window's layout, fit and zoom, the top-bar button and key, a ghost's sync and centring).

Upstream edits, each one marked line: `Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs` after
`DrawStarSystem(handle, matty);` and `Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs` after the `DrawWfTerrain`
line. The sector map adds the game menu entry in `EscapeMenu.xaml` and `EscapeUIController.cs`, the top-bar button and key (`GameTopMenuBar.xaml`,
`GameTopMenuBarUIController.cs`, `ContentKeyFunctions.cs`, `ContentContexts.cs`, `KeyRebindTab.xaml.cs`, `keybinds.yml`;
icon `Resources/Textures/_WF/SectorControl/menu.rsi`, set in code), and `wf_sectormap` in `Resources/clientCommandPerms.yml` so every
player may run it. Edits inside `_WF/Encounters` (same fork, no markers): `WFEncounterStationsEvent` raised in the scheduler's
`Stations()` so blockaded stations drop out of freight circuits.

## Decisions

- Hexes, not circles of influence: union rendering is "draw the edges whose neighbour differs", frontier and support
  are ring counts, and a later owner needs no new drawing.
- Claims carry sources, not ranges: the framework only checks liveness; what supports what is the driver's rule.
- Protection is an event, so every driver gets the same vetoes without a list of its own.
- Moving ships only count as activity. Parked pickets would otherwise fence the sector for free. Retaking is the
  exception, and it needs the holder to be absent, so a picket cannot take a cell a faction is flying in.
- Only the framework draws and networks; drivers send text for the legend.
- The sector map shows no more than a MAP scan: the server sends territory only, and the client names grids and
  encounters by the MAP tab's own rules, without the detection a console would add.
