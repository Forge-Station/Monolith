# Shuttles

Shuttle console additions for ships: a whole-ship view with hull telemetry (structure condition, fires, pressure, dead
power), a collision warning (TCAS) that sweeps a piloted ship's path and warns before a hard impact, hull cameras the
pilot can look through with a low-light feed, and nav map data for ships that were never registered as a station. The
cameras' EXTERNAL view shows the ship from outside with every hull plated over: a right-mouse drag pans it, the scroll
wheel zooms it, and a right click without dragging opens the entity menu on release. The client
`ShuttleExternalCameraSystem` and `ShuttleHullRoofOverlay` run it. It is refused on a planet's surface, and speech
bubbles are taken from the point it looks at rather than from the helm. It draws no mobs, jetpack trails or
do-after bars, so it can't be used to spot people outside a hull.

In every camera view, hull cameras and EXTERNAL alike, positional sound is still heard from the pilot's own body:
the client `ShuttleCameraListenerSystem` takes over the engine's per-sound pass while a view is up, judges range and
occlusion from the body, and places each sound so the eye-bound engine listener hears it from there. The ship PA
picks its nearest speaker from the same point.

The cockpit HULL gauge averages the condition of surveyed hull locations, including missing floors and destroyed
walls, windows (diagonal ones included) and airtight doors; curtains, gates, machinery and furniture do not
contribute to the hull baseline. Piloting or opening the ship view starts the survey; later splits inherit that ship's original footprint,
and rebuilding a lost location restores its condition. Successful construction-graph or RCD removal subtracts
only the removed structure from its surveyed design; cancelled operations and combat destruction remain losses.
Removing the last floor or lattice at a location with the RCD, or by cutting the lattice, drops that location from the survey too; floor lost
to damage or explosions stays a loss until it is laid again.
The separate minimum-integrity readout still describes the
weakest surviving structure. The survey is kept for the current server lifetime and cannot infer sections already
missing before the first survey. Outside the SHP page, cockpit requests collect hull condition and fuel reserves only,
and the windowed Navigation page asks for the same summary-only status while it is showing;
pressure, power and damage overlays are gathered when a detailed ship view is open. The structure survey reads only
the flown grid's own anchored structures; fuel and power are still read with component queries filtered to the grid.

The cockpit fuel gauge reports the mean reserve fraction per anchored generator, antimatter injector or fission
reactor on the flown grid, including stopped machinery. Solid and chemical sources use accepted local fuel fill;
antimatter uses its installed jar. Fission uses the sum of remaining fuel-rod activity divided by fresh activity,
weighted by each rod's fresh capacity. Neutron-active fuel has a factor of 1.5 because it converts into half as
much ordinary active fuel before becoming spent. Empty sources contribute zero; loose fuel and unanchored machines
are excluded. This is average reserves, not a runtime estimate or a sum of incompatible fuel units. The FUEL LOW lamp
is red: steady at 20% or less and flashing once a second below 10% (`ShipFuelSummary.LowThreshold` and
`CriticalThreshold`, timed from real time so it keeps flashing while the game is paused). It lights only when every
installed source is measurable; absent or unsupported sources leave it dark and the gauge at no signal.
The windowed helm's Navigation page shows the same fuel gauge and lamp.
Fuel shares the existing one-second ship-status sweep, including the lightweight cockpit snapshot; the one-off
hull survey taken when a grid gets its first pilot leaves it out.

Autopilot arrival, cancellation and manual takeover use the supplied disengagement cue credited to gandalf.
The mono import supports positional audio and plays at -12 dB; other console and navigation cues retain their sounds.

Entry points: the server `ShipStatusSystem`, `CollisionWarningSystem`, `ShuttleCameraSystem` and
`ShuttleNavMapSystem`; the client `ShipScreen`, `ShipViewControl`, `CollisionWarningBanner` and `ShuttleCameraBar`,
hooked into the upstream console by the `ShuttleConsoleWindow.Wolfgate` and
`ShuttleConsoleBoundUserInterface.Wolfgate` partials. Crews can switch the warning off per ship
(`CollisionWarningDisabledComponent`); tuning is in `CollisionWarningCVars`.

It also fixes Mono's docked-ship IFF hosts (the PDV Helios hides every ship docked to it): either grid of a dock or
undock can be the host, and a ship gets its label back once no host it is docked to still hides it, keeping flags it
had before. The logic is in the `ApplyIFFFlagsToDockedShipsSystem.Wolfgate` partial, which records what hosts added
in `DockedHostIffFlagsComponent` on the ship.

`FTLToDockTest` covers docking jumps (the bus, arrivals and evac): a full FTL through every state that ends docked,
even with a grid parked nearby. The fix it guards is a standalone edit listed in `Docs/_WF/NONMODULAR.md`.

The helm's FTL page, and the cockpit's strategic map page, say when the ship cannot jump. The server reports the drive
in `ShuttleMapInterfaceState.FtlDrive` (`WFFtlDriveStatus`: missing, unpowered or ready), worked out by
`SharedShuttleSystem.GetFtlDriveStatus` from the two checks `GetFTLRange` makes, and `ConsoleFTL` refuses a jump
unless the status is ready, with a popup to the sender only. Open helms are checked four times a second and rebuilt when a drive is built, removed,
powered or unpowered, and a helm that opens never shows an older snapshot. Without a working drive the page shows a
notice in the skin's danger colour (`WFFtlDriveNotice`, using the existing "No FTL drive detected" and "FTL drive
detected but not powered" wording): above the plot and outside the scrolling details in the cockpit, at the top of
the drive panel in the windowed helm. The FTL button is off and gives the reason as its tooltip, a running FTL preview
is released, and the readout says Offline. Scan, the destination list and autopilot targeting do not need a drive
and are left alone. The cockpit's FTL lamp lights red instead of green. `WFFtlDriveTest`, `WFFtlDriveWindowTest`
and `WFCockpitFtlDriveTest` cover it.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Shuttles/ApplyIFFFlagsToDockedShipsSystem.Wolfgate.cs`](ApplyIFFFlagsToDockedShipsSystem.Wolfgate.cs)
- [`Content.Server/_WF/Shuttles/DockedHostIffFlagsComponent.cs`](DockedHostIffFlagsComponent.cs)
- [`Content.Server/_WF/Shuttles/NavMapSystem.Wolfgate.cs`](NavMapSystem.Wolfgate.cs)
- [`Content.Server/_WF/Shuttles/ShuttleConsoleSystem.FtlDrive.cs`](ShuttleConsoleSystem.FtlDrive.cs)
- [`Content.Server/_WF/Shuttles/Systems/CollisionWarningSystem.cs`](Systems/CollisionWarningSystem.cs)
- [`Content.Server/_WF/Shuttles/Systems/ShipStatusSystem.cs`](Systems/ShipStatusSystem.cs)
- [`Content.Server/_WF/Shuttles/Systems/ShipStatusSystem.Fuel.cs`](Systems/ShipStatusSystem.Fuel.cs)
- [`Content.Server/_WF/Shuttles/Systems/ShipStatusSystem.Hull.cs`](Systems/ShipStatusSystem.Hull.cs)
- [`Content.Server/_WF/Shuttles/Systems/ShuttleCameraSystem.cs`](Systems/ShuttleCameraSystem.cs)
- [`Content.Server/_WF/Shuttles/Systems/ShuttleNavMapSystem.cs`](Systems/ShuttleNavMapSystem.cs)

### Shared

- [`Content.Shared/_WF/Shuttles/CollisionWarningComponent.cs`](../../../Content.Shared/_WF/Shuttles/CollisionWarningComponent.cs)
- [`Content.Shared/_WF/Shuttles/CollisionWarningDisabledComponent.cs`](../../../Content.Shared/_WF/Shuttles/CollisionWarningDisabledComponent.cs)
- [`Content.Shared/_WF/Shuttles/RCDSystem.Hull.cs`](../../../Content.Shared/_WF/Shuttles/RCDSystem.Hull.cs)
- [`Content.Shared/_WF/Shuttles/SharedShuttleSystem.FtlDrive.cs`](../../../Content.Shared/_WF/Shuttles/SharedShuttleSystem.FtlDrive.cs)
- [`Content.Shared/_WF/Shuttles/ShipFuelSummary.cs`](../../../Content.Shared/_WF/Shuttles/ShipFuelSummary.cs)
- [`Content.Shared/_WF/Shuttles/ShipStatus.cs`](../../../Content.Shared/_WF/Shuttles/ShipStatus.cs)
- [`Content.Shared/_WF/Shuttles/ShuttleCameraComponent.cs`](../../../Content.Shared/_WF/Shuttles/ShuttleCameraComponent.cs)
- [`Content.Shared/_WF/Shuttles/TileSystem.Hull.cs`](../../../Content.Shared/_WF/Shuttles/TileSystem.Hull.cs)
- [`Content.Shared/_WF/Shuttles/WFFtlDriveStatus.cs`](../../../Content.Shared/_WF/Shuttles/WFFtlDriveStatus.cs)
- [`Content.Shared/_WF/Shuttles/WFHullDeconstructedEvent.cs`](../../../Content.Shared/_WF/Shuttles/WFHullDeconstructedEvent.cs)
- [`Content.Shared/_WF/Shuttles/WFHullTileDeconstructedEvent.cs`](../../../Content.Shared/_WF/Shuttles/WFHullTileDeconstructedEvent.cs)

### Client

- [`Content.Client/_WF/Shuttles/BUI/ShuttleConsoleBoundUserInterface.Wolfgate.cs`](../../../Content.Client/_WF/Shuttles/BUI/ShuttleConsoleBoundUserInterface.Wolfgate.cs)
- [`Content.Client/_WF/Shuttles/ScalingViewport.ShuttleCamera.cs`](../../../Content.Client/_WF/Shuttles/ScalingViewport.ShuttleCamera.cs)
- [`Content.Client/_WF/Shuttles/Systems/ShuttleCameraListenerSystem.cs`](../../../Content.Client/_WF/Shuttles/Systems/ShuttleCameraListenerSystem.cs)
- [`Content.Client/_WF/Shuttles/Systems/ShuttleCameraSystem.cs`](../../../Content.Client/_WF/Shuttles/Systems/ShuttleCameraSystem.cs)
- [`Content.Client/_WF/Shuttles/Systems/ShuttleCameraZoomPredictionSystem.cs`](../../../Content.Client/_WF/Shuttles/Systems/ShuttleCameraZoomPredictionSystem.cs)
- [`Content.Client/_WF/Shuttles/Systems/ShuttleExternalCameraSystem.cs`](../../../Content.Client/_WF/Shuttles/Systems/ShuttleExternalCameraSystem.cs)
- [`Content.Client/_WF/Shuttles/Systems/ShuttleExternalCameraSystem.Hidden.cs`](../../../Content.Client/_WF/Shuttles/Systems/ShuttleExternalCameraSystem.Hidden.cs)
- [`Content.Client/_WF/Shuttles/UI/CollisionWarningBanner.xaml`](../../../Content.Client/_WF/Shuttles/UI/CollisionWarningBanner.xaml)
- [`Content.Client/_WF/Shuttles/UI/CollisionWarningBanner.xaml.cs`](../../../Content.Client/_WF/Shuttles/UI/CollisionWarningBanner.xaml.cs)
- [`Content.Client/_WF/Shuttles/UI/MapScreen.FtlDrive.cs`](../../../Content.Client/_WF/Shuttles/UI/MapScreen.FtlDrive.cs)
- [`Content.Client/_WF/Shuttles/UI/ShipScreen.xaml`](../../../Content.Client/_WF/Shuttles/UI/ShipScreen.xaml)
- [`Content.Client/_WF/Shuttles/UI/ShipScreen.xaml.cs`](../../../Content.Client/_WF/Shuttles/UI/ShipScreen.xaml.cs)
- [`Content.Client/_WF/Shuttles/UI/ShipViewControl.cs`](../../../Content.Client/_WF/Shuttles/UI/ShipViewControl.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttleCameraBar.xaml`](../../../Content.Client/_WF/Shuttles/UI/ShuttleCameraBar.xaml)
- [`Content.Client/_WF/Shuttles/UI/ShuttleCameraBar.xaml.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttleCameraBar.xaml.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttleConsoleWindow.Wolfgate.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttleConsoleWindow.Wolfgate.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttleFlightHudOverlay.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttleFlightHudOverlay.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttleHullRoofOverlay.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttleHullRoofOverlay.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttleLowLightOverlay.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttleLowLightOverlay.cs)
- [`Content.Client/_WF/Shuttles/UI/ShuttlePoiPointerOverlay.cs`](../../../Content.Client/_WF/Shuttles/UI/ShuttlePoiPointerOverlay.cs)
- [`Content.Client/_WF/Shuttles/UI/WFFtlDriveNotice.cs`](../../../Content.Client/_WF/Shuttles/UI/WFFtlDriveNotice.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Shuttles/CollisionWarningTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/CollisionWarningTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/DockedShipIffTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/DockedShipIffTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/FTLToDockTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/FTLToDockTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShipFuelTelemetryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShipFuelTelemetryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShipHullConstructionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShipHullConstructionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShipStatusTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShipStatusTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleCameraTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleCameraTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleExternalCameraTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleExternalCameraTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleNavMapTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/ShuttleNavMapTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/WFFtlDriveTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/WFFtlDriveTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Shuttles/WFFtlDriveWindowTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Shuttles/WFFtlDriveWindowTest.cs)

### Unit tests

- [`Content.Tests/_WF/Shuttles/ShuttleCameraListenerTest.cs`](../../../Content.Tests/_WF/Shuttles/ShuttleCameraListenerTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Shuttles/Entities/Markers/shuttle_camera.yml`](../../../Resources/Prototypes/_WF/Shuttles/Entities/Markers/shuttle_camera.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Shuttles/collision-warning.ftl`](../../../Resources/Locale/en-US/_WF/Shuttles/collision-warning.ftl)
- [`Resources/Locale/en-US/_WF/Shuttles/ftl-drive.ftl`](../../../Resources/Locale/en-US/_WF/Shuttles/ftl-drive.ftl)
- [`Resources/Locale/en-US/_WF/Shuttles/ship-view.ftl`](../../../Resources/Locale/en-US/_WF/Shuttles/ship-view.ftl)
- [`Resources/Locale/en-US/_WF/Shuttles/shuttle-camera.ftl`](../../../Resources/Locale/en-US/_WF/Shuttles/shuttle-camera.ftl)

### Audio

- [`Resources/Audio/_WF/Shuttles/Autopilot/attributions.yml`](../../../Resources/Audio/_WF/Shuttles/Autopilot/attributions.yml)
- [`Resources/Audio/_WF/Shuttles/Autopilot/autopilot_disengaged.ogg`](../../../Resources/Audio/_WF/Shuttles/Autopilot/autopilot_disengaged.ogg)
- [`Resources/Audio/_WF/Shuttles/Tcas/attributions.yml`](../../../Resources/Audio/_WF/Shuttles/Tcas/attributions.yml)
- [`Resources/Audio/_WF/Shuttles/Tcas/collision_alarm.ogg`](../../../Resources/Audio/_WF/Shuttles/Tcas/collision_alarm.ogg)
- [`Resources/Audio/_WF/Shuttles/Tcas/collision_warning.ogg`](../../../Resources/Audio/_WF/Shuttles/Tcas/collision_warning.ogg)
- [`Resources/Audio/_WF/Shuttles/Tcas/traffic.ogg`](../../../Resources/Audio/_WF/Shuttles/Tcas/traffic.ogg)
- [`Resources/Audio/_WF/Shuttles/Tcas/traffic_alarm.ogg`](../../../Resources/Audio/_WF/Shuttles/Tcas/traffic_alarm.ogg)

## Non-modular edits

- [`Content.Client/DoAfter/DoAfterOverlay.cs`](../../../Content.Client/DoAfter/DoAfterOverlay.cs): a progress bar would give away a mob the external pilot view hides
- [`Content.Client/Parallax/ParallaxOverlay.cs`](../../../Content.Client/Parallax/ParallaxOverlay.cs): the external view pans away from where its eye sits
- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs)
- [`Content.Client/Shuttles/UI/MapScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/MapScreen.xaml.cs)
  - the drive status decides whether the FTL button can be used
  - without a powered drive the FTL button stays off and its preview is released
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml)
  - xmlns:wf for _WF shuttle controls
  - collision warning, across every mode
  - ship mode button
  - hull camera views
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs)
  - ship mode
  - ship screen and collision state
- [`Content.Server/_Mono/Detection/ApplyIFFFlagsToDockedShipsSystem.cs`](../../_Mono/Detection/ApplyIFFFlagsToDockedShipsSystem.cs)
  - the host can be grid B, since docking orders the pair by entity id.
  - the host can be grid B, since grid A is the side that undocked, usually the ship.
  - a ship loses only flags hosts added, once no host it is still docked to provides them.
- [`Content.Server/Shuttles/Components/ShuttleConsoleComponent.cs`](../../Shuttles/Components/ShuttleConsoleComponent.cs): use the supplied quiet autopilot disengagement cue.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - a console off any grid has no drive
  - keep the FTL page current when a drive is built, removed or loses power
  - capture hull condition before piloting can damage it.
  - restore the console's camera view
  - report the drive so the FTL page can warn
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.FTL.cs)
  - the no-drive refusal goes only to the sender
  - a jump needs a powered drive; this is the test the helm's FTL page reports
- [`Content.Shared/Maps/TileSystem.cs`](../../../Content.Shared/Maps/TileSystem.cs): cutting the last lattice leaves the hull survey before any split copies it.
- [`Content.Shared/RCD/Systems/RCDSystem.cs`](../../../Content.Shared/RCD/Systems/RCDSystem.cs)
  - removing the last lattice leaves the hull survey before any split copies it.
  - Successful RCD removal updates the hull design without erasing combat losses.
- [`Content.Shared/Shuttles/BUIStates/ShuttleMapInterfaceState.cs`](../../../Content.Shared/Shuttles/BUIStates/ShuttleMapInterfaceState.cs)
  - the FTL page warns before a jump is tried
  - the drive status is a required argument

<!-- WOLFGATE-GENERATED END -->
