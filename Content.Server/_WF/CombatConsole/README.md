# CombatConsole

Theme-aware instrument panels for the pilot and gunnery consoles. The existing **Wolfgate UI style**
option selects Retro's HighFleet-style mechanical housings and amber glass, or Futurist's cyan digital
arcs, segmented linear readouts, touch selectors and clean glass panels. Both use `WolfgateSkins` for
their palette and fonts, and restyle open windows without resetting selections or sending commands.
The helm retains navigation,
FTL/autopilot, docking, hull cameras, ship systems, access and shields. Its title bar carries the flight deck
caption beside the pop-out and close controls, so the page tabs start the window body. The Navigation page's left
column ends with the Enter Cockpit key, full width and half as tall again as a normal key, below the last panel.
That column's telemetry panel also ends with the cockpit's fuel reserve gauge and FUEL LOW lamp, with the same reading
and tooltip. While Navigation is on screen the helm asks the server for the summary-only ship status (hull integrity
and fuel, no tile overlays), and stops when the page is left; the Ship page and the cockpit keep their own requests.
The FTL page's drive panel opens with a notice in the skin's danger colour, and keeps the FTL button off, while the
ship has no powered FTL drive (see Shuttles).
The helm opens at 1240 by 960 and each page is laid out to fit that size without scrolling; smaller windows fall
back to scroll bars. On z-level maps the Navigation telemetry column adds altitude and climb, side by side in
shallow strips, and the travel readout, and still fits. Navigation shows the ship's position in its own glass box
between the longitudinal and lateral gauges. The shield page puts the bearing dial in the middle of the page, with
the sector and power banks on its left and the coverage readout on its right. Gunnery adds four numbered
weapon groups: select weapons, enable STORE and choose a slot, then recall it with one click. Groups belong to
the console and survive closing its window; saving an empty selection clears a slot.

The countermeasure panel controls connected GS-002 Sunny launchers and is hidden when none are installed,
including in the cockpit. Empty installed launchers remain visible. Launchers are not in the weapon list and
cannot be aimed; they fire only through DISPENSE and AUTO, and DISPENSE fires every ready launcher. AUTO responds to
exposed, launched missiles tracking this ship within 250 metres of the console, or within three seconds of flight at
their top speed when that is farther; DISPENSE requests a manual
burst. Automation continues while armed with the window closed. Each launcher shares a 15-second
burst lockout across consoles after each shot and uses its existing ammunition supply, including
the Sunny autoloader. Power, server membership, anchoring, firing lanes, FTL and pacifist
restrictions still apply. DISPENSE and AUTO choose a clear lane away from the hull rather than a
fixed bearing that may point into the ship. Actual emitted Sunny flares can distract an existing hostile
missile lock once outside the hull, if nearer than its target and inside its detection range and scan arc.
Unrelated seeker acquisition and ordinary aim-directed weapon commands are unchanged. Automation defaults to safe.
Group and flare commands sent from the console window also require the operator to be able to interact with the console and
be within its reach. Threat alerts are freshly scanned when a console reopens. Combat telemetry refreshes four times per second; periodic radar/weapon metadata refreshes once per
second, with native open/action updates remaining immediate. Unchanged snapshots and cockpit deliveries
are suppressed. Linked viewers share each snapshot and docking discovery is shared within each server
tick; cursor guidance does not rebuild radar or ammunition state.

The console and cockpit share one weapon list with readable names, exact supply values and thin
ammunition bars. Weapons sit two to a line, filled left to right then down, sorted by name without
regard to case and then by entity id so the order never shifts between updates. Each entry is two
text lines with almost no padding. The list takes only the height it needs, so the threat and flare
readouts follow it up the panel, and scrolls when the battery is longer than the panel. There are no
pages. Selections, firing and saved groups cover the whole list; flare launchers are never in it.
A single STORE mode exposes group assignment, and TYPE expands the secondary selection shortcuts.
The standalone console gives most of its width to the tactical plot. Missile alerts stay visible;
installed countermeasures use a compact supply/rearm readout with AUTO and DISPENSE controls.
Supply telemetry distinguishes finite rounds, unlimited feeds, recharging weapons and energy charge,
so an unlimited magazine is never marked empty.

Entry points: `WFCombatConsoleSystem`, `FireControlWindow.CombatConsole`, the helm screen partials,
and `WFInstrumentTheme`. Upstream XAML and control bindings are retained; small constructor hooks
recompose their controls into module-owned layouts. Each hook re-homes a fixed list of named upstream controls
and disposes the rest of the original layout, so a control added to an upstream XAML file later is lost unless the
hook is updated; `WFCombatConsoleTest.RecomposedConsolesKeepEveryNamedControl` lists the controls dropped on purpose
and fails on any other. Retro console faces use original Wolfgate drawings for worn metal
housings, display bezels, buttons, toggles and a compass dial. These are drawn at the current UI
scale without imported HighFleet textures. Live heading and supply readings reflect authoritative
telemetry. Control presses play mechanical cues and a new incoming
lock plays one short warning, throttled across windows. Replicated state changes make no click sounds. The OGG cues respect Interface volume.
The ship tab gives the hull plot its widest column, beside a six-gauge status bank (three rows of two) and the
announcement panel. The overlay toggles (damage, fire, pressure, power, show departments) and Fit to hull sit in their
own numbered panel below the announcements, keeping the entire page accessible without a scrolling sidebar.
The access tab gives its framed door map nearly half the page, making it the largest panel beside smaller
ship settings, crew lists and selected-door controls. Its wide legend uses two columns to preserve map
height. Smaller displays keep the map dominant beside or above one details scroller; lists scroll
individually only in the wide layout. Its bezel and controls match the selected Wolfgate theme.
Crew who do not hold the deed can read the selected door's rule description.
Shield shunting uses a CRT coverage plot, sector and power instrument banks, and a persistent
deployment/status strip. Its
original live allocation, warning colors, recovery and draft acknowledgement behavior are retained.
Dragging the bearing dial, adjusting its numeric field or selecting a direction plays a short metal
detent. Clicks are spaced at least 150 ms apart; received shield snapshots remain silent.

Flight motion, altitude, hull condition, FTL progress, ammunition, missile locks and shield allocation
use glass-faced instruments with graduated scales, damped indicators and exact numeric readouts.
Futurist arcs and position markers have antialiased edges and reuse their vertex buffers between frames. Hull
identity, coordinates and generator specifications sit behind matching glass windows. Counts use
labelled absolute scales rather than invented magazine capacities; signed motion retains its centre
zero. Motion components, FTL, ammunition and requested shield power use horizontal linear scales.
Missing or disconnected readings blank their needles, and an unlimited flare feed reads AUTO.
Altitude, climb and travel state appear only on z-level maps or during vertical transit, following the
existing altimeter rules. Camera controls appear only on Navigation; the Ship page's overlay panel holds department
and damage overlays, leaving the hull plot free of a separate display-control header.
Shield controls distinguish requested allocation from actual coverage and retain integrity warning colors.

Current sound credits name Extreme Sound Effects on YouTube, with free-to-use permission reported by
the Wolfgate maintainer. No specific video/channel URL or license text was supplied.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/CombatConsole/FireControlSystem.CombatConsole.cs`](FireControlSystem.CombatConsole.cs)
- [`Content.Server/_WF/CombatConsole/WFCombatConsoleComponent.cs`](WFCombatConsoleComponent.cs)
- [`Content.Server/_WF/CombatConsole/WFCombatConsoleSystem.Ammunition.cs`](WFCombatConsoleSystem.Ammunition.cs)
- [`Content.Server/_WF/CombatConsole/WFCombatConsoleSystem.cs`](WFCombatConsoleSystem.cs)
- [`Content.Server/_WF/CombatConsole/WFCombatConsoleSystem.Flares.cs`](WFCombatConsoleSystem.Flares.cs)
- [`Content.Server/_WF/CombatConsole/WFCombatSnapshotEquality.cs`](WFCombatSnapshotEquality.cs)

### Shared

- [`Content.Shared/_WF/CombatConsole/WFCombatConsoleMessages.cs`](../../../Content.Shared/_WF/CombatConsole/WFCombatConsoleMessages.cs)
- [`Content.Shared/_WF/CombatConsole/WFWeaponSupply.cs`](../../../Content.Shared/_WF/CombatConsole/WFWeaponSupply.cs)

### Client

- [`Content.Client/_WF/CombatConsole/DockingScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/DockingScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/FireControlWindow.Ammunition.cs`](../../../Content.Client/_WF/CombatConsole/FireControlWindow.Ammunition.cs)
- [`Content.Client/_WF/CombatConsole/FireControlWindow.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/FireControlWindow.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/MapGridControl.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/MapGridControl.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/MapScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/MapScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/NavMapControl.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/NavMapControl.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/NavScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/NavScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShieldShuntScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShieldShuntScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShipAccessScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShipAccessScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShipAlarmPanel.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShipAlarmPanel.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShipScreen.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShipScreen.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShuttleConsoleWindow.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShuttleConsoleWindow.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/ShuttleNavControl.CombatConsole.cs`](../../../Content.Client/_WF/CombatConsole/ShuttleNavControl.CombatConsole.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleAnnunciator.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleAnnunciator.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleAudio.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleAudio.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleDigital.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleDigital.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleFrameStyleBox.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleFrameStyleBox.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleMetal.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleMetal.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleStyleBox.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleStyleBox.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleThemeBinding.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleThemeBinding.cs)
- [`Content.Client/_WF/CombatConsole/WFConsoleThemeSystem.cs`](../../../Content.Client/_WF/CombatConsole/WFConsoleThemeSystem.cs)
- [`Content.Client/_WF/CombatConsole/WFDetentThrottle.cs`](../../../Content.Client/_WF/CombatConsole/WFDetentThrottle.cs)
- [`Content.Client/_WF/CombatConsole/WFGaugeScale.cs`](../../../Content.Client/_WF/CombatConsole/WFGaugeScale.cs)
- [`Content.Client/_WF/CombatConsole/WFGlassGauge.cs`](../../../Content.Client/_WF/CombatConsole/WFGlassGauge.cs)
- [`Content.Client/_WF/CombatConsole/WFHeadingInstrument.cs`](../../../Content.Client/_WF/CombatConsole/WFHeadingInstrument.cs)
- [`Content.Client/_WF/CombatConsole/WFInstrumentGlass.cs`](../../../Content.Client/_WF/CombatConsole/WFInstrumentGlass.cs)
- [`Content.Client/_WF/CombatConsole/WFInstrumentText.cs`](../../../Content.Client/_WF/CombatConsole/WFInstrumentText.cs)
- [`Content.Client/_WF/CombatConsole/WFInstrumentTheme.cs`](../../../Content.Client/_WF/CombatConsole/WFInstrumentTheme.cs)
- [`Content.Client/_WF/CombatConsole/WFShipAccessLayout.cs`](../../../Content.Client/_WF/CombatConsole/WFShipAccessLayout.cs)
- [`Content.Client/_WF/CombatConsole/WFWeaponBank.cs`](../../../Content.Client/_WF/CombatConsole/WFWeaponBank.cs)
- [`Content.Client/_WF/CombatConsole/WFWeaponGrid.cs`](../../../Content.Client/_WF/CombatConsole/WFWeaponGrid.cs)
- [`Content.Client/_WF/CombatConsole/WFWeaponRow.cs`](../../../Content.Client/_WF/CombatConsole/WFWeaponRow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFButtonTestInput.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFButtonTestInput.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFCombatConsoleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFCombatConsoleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFGunnerySupplyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFGunnerySupplyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFMapViewportTest.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFMapViewportTest.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFRadarOverlayTest.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFRadarOverlayTest.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFShipPageLayoutTest.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFShipPageLayoutTest.cs)
- [`Content.IntegrationTests/Tests/_WF/CombatConsole/WFWeaponListChecks.cs`](../../../Content.IntegrationTests/Tests/_WF/CombatConsole/WFWeaponListChecks.cs)

### Unit tests

- [`Content.Tests/_WF/CombatConsole/WFDetentThrottleTest.cs`](../../../Content.Tests/_WF/CombatConsole/WFDetentThrottleTest.cs)
- [`Content.Tests/_WF/CombatConsole/WFGaugeScaleTest.cs`](../../../Content.Tests/_WF/CombatConsole/WFGaugeScaleTest.cs)
- [`Content.Tests/_WF/CombatConsole/WFThreatAnnunciatorTest.cs`](../../../Content.Tests/_WF/CombatConsole/WFThreatAnnunciatorTest.cs)
- [`Content.Tests/_WF/CombatConsole/WFWeaponGroupsTest.cs`](../../../Content.Tests/_WF/CombatConsole/WFWeaponGroupsTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/CombatConsole/access-layout.ftl`](../../../Resources/Locale/en-US/_WF/CombatConsole/access-layout.ftl)
- [`Resources/Locale/en-US/_WF/CombatConsole/console.ftl`](../../../Resources/Locale/en-US/_WF/CombatConsole/console.ftl)
- [`Resources/Locale/en-US/_WF/CombatConsole/gunnery-layout.ftl`](../../../Resources/Locale/en-US/_WF/CombatConsole/gunnery-layout.ftl)
- [`Resources/Locale/en-US/_WF/CombatConsole/ship-layout.ftl`](../../../Resources/Locale/en-US/_WF/CombatConsole/ship-layout.ftl)
- [`Resources/Locale/en-US/_WF/CombatConsole/weapon-battery.ftl`](../../../Resources/Locale/en-US/_WF/CombatConsole/weapon-battery.ftl)

### Audio

- [`Resources/Audio/_WF/CombatConsole/HighFleet/attributions.yml`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/attributions.yml)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/bearing.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/bearing.ogg)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/key.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/key.ogg)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/selector.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/selector.ogg)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/switch_off.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/switch_off.ogg)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/switch_on.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/switch_on.ogg)
- [`Resources/Audio/_WF/CombatConsole/HighFleet/warning.ogg`](../../../Resources/Audio/_WF/CombatConsole/HighFleet/warning.ogg)

## Non-modular edits

- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): send group and flare commands through the console UI.
- [`Content.Client/_Mono/FireControl/UI/FireControlWindow.xaml`](../../../Content.Client/_Mono/FireControl/UI/FireControlWindow.xaml)
  - register the battery grid namespace on the window.
  - fit weapon controls into an adaptive battery grid.
- [`Content.Client/_Mono/FireControl/UI/FireControlWindow.xaml.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlWindow.xaml.cs)
  - compose the instrument deck and fire-group controls.
  - show the actual provider supply instead of treating autoloaders as empty.
  - update groups and countermeasure telemetry.
- [`Content.Client/Shuttles/UI/BaseShuttleControl.xaml.cs`](../../../Content.Client/Shuttles/UI/BaseShuttleControl.xaml.cs)
  - fill rectangular plots with range rings.
  - extend plot axes to the viewport corners.
  - extend the north line across the rectangular viewport.
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs): compose the flight deck instrument panels.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - theme the original radar mode controls.
  - instrument scopes retain these buttons with the selected console palette.
  - retain hulls visible beyond the shorter edge of a rectangular plot.
- [`Content.Client/UserInterface/Controls/MapGridControl.xaml.cs`](../../../Content.Client/UserInterface/Controls/MapGridControl.xaml.cs)
  - cull responsive plots against both viewport dimensions.
  - centre instrument plots in their available viewport.
  - fit scoped radar geometry to the shorter viewport edge.
  - keep plotting and mouse coordinates on the same responsive scale.
- [`Content.Server/_Mono/FireControl/FireControlSystem.Console.cs`](../../_Mono/FireControl/FireControlSystem.Console.cs)
  - share docking discovery across same-tick gunnery snapshots.
  - include group memory and countermeasure telemetry.
  - publish only changed gunnery snapshots.
- [`Content.Shared/_Mono/FireControl/FireControlMessages.cs`](../../../Content.Shared/_Mono/FireControl/FireControlMessages.cs): carry authoritative groups and countermeasure state.
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/flarelauncher.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/flarelauncher.yml): identify launchers for shared automatic countermeasure control.

<!-- WOLFGATE-GENERATED END -->
