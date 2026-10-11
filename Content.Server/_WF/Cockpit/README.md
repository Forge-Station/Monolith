# Cockpit

Optional fullscreen flight deck for the shuttle console. Enter Cockpit requires piloting that console
while buckled into any seat on the same grid. Unbuckling, losing piloting, closing the BUI,
changing character or leaving gameplay restores the normal HUD. The existing server-side permissions
still govern every flight, camera, access and shield request; cockpit mode only changes presentation.
Entry selects EXT while preserving zoom and low-light settings; leaving restores that pilot's prior camera.
Cockpit camera changes never overwrite the console's shared saved settings, so overlapping pilots and
ordinary console users retain their own choices. The other camera views remain available. In the External
view, a right click that does not move opens the entity menu on release, and dragging with the right button pans.
Live pressure, internals and buckle alerts, votes, and speech bubbles remain available around the world
view. Character hotkeys, inventory controls and the menu bar remain hidden; Escape closes other windows
first and then leaves the cockpit, as the EXIT button does; the helm stays open.

The cockpit can open by itself. The Misc options tab has a checkbox for it, on by default (client CVar
`wf.cockpit_auto_enter`). When the helm window opens while the player is already buckled, the bound interface calls
`WFCockpitUIController.AutoEnterOnOpen`, which queues one entry. The piloting state arrives from the server just after
the window opens, so the controller waits up to about a second of UI frame time for the usual entry check to pass, then
enters; if it never does, nothing happens. The request ends when it enters, times out or the window closes, so leaving
the cockpit by the Exit button, Escape or any other way never puts the pilot back in while the same window stays open;
opening the helm again is a new request. Buckling after the window is already open does not trigger it. It goes through the
same entry check as the button, so a helm the pilot cannot use, another active cockpit and replays are all excluded, and
with the option off the helm opens as a window as before.

There is no header row: the Wolfgate wordmark and Exit Cockpit share one slim row at the top of the left column,
which stays put in every arrangement, so the other columns start at the screen margin. Only a tractor capture
warning adds a full-width row above everything, and only while it shows.
Flight instruments, compact camera controls and propulsion occupy the left column. A row above the central world viewport holds the longitudinal strip, a coordinates box
(the ship's centre-of-mass position, X and Y at a fixed width) and the lateral strip; communications and shield controls sit below it. The shield panel is only as
wide as its controls need (found from the controls, not a ratio) and communications take the rest of the row,
so the chat is the larger panel on any window wide enough for both. The velocity dial shows
actual drift relative to the bow, with forward at the top and starboard on the right, alongside total speed.
Its direction pointer uses a fixed square-root response against a 100 m/s full scale, making low-speed drift
more visible without scale jumps, and disappears when nearly stationary.
A horizontal fuel gauge below HULL shares its row with a FUEL LOW lamp and stays fixed outside the dial scroller.
The instrument housing uses compact padding and gaps while preserving dial sizes. HULL remains the final
scrolling strip above fuel on short screens. The velocity dial keeps its exact speed in a readout bar under the dial. The MFD fills the right column unless swapped or maximised. Its maps use
right-mouse panning and compact department labels, with scrolling details kept separate from the larger plots. Numbered approach markers link to readable port actions below the plot. Clicking one of the ship's own ports on the docking plot, or its numbered marker, views that port exactly as picking it from the port list does; the windowed helm's docking page does the same.
Shield arc width stays in the permanent shield control panel. Digital and mechanical instruments share the same large dial footprint, and the
lower flight bank aligns with communications and shields. Its six status lamps show live autopilot, FTL,
dampener, parking, docking and shield state; autopilot activity is replicated independently of MFD selection. The FTL lamp lights red when the ship has no powered FTL drive, and the strategic map page shows a danger notice for it above the plot.
Retro lamps use recessed glass lenses and metal sockets; Futurist lamps use illuminated traces and light strips.
A permanent TCAS bank below the instruments has CAUTION and WARNING lamps above OK and FAULT lamps.
Caution flashes slowly; warning flashes quickly while caution keeps its slow rhythm. OK lights steadily
when clear, and FAULT lights when TCAS is off or telemetry is unavailable. The bank remains visible while
using guns, with impact countdown, bearing, closing speed and full threat details on hover.
The speed limiter lives on SYS; flight controls retain dampeners and orbit controls.
The MFD holds navigation, hull overlays, strategic travel, docking, access, shield details and auxiliary
systems. Its header has three toggles. Swap trades places between the world view and the MFD: the MFD
takes the large centre area and the world view the full-height column beside it, and nothing else moves.
Expand shares the width of the two big areas half and half; whichever of them is in the full-height side column
(the MFD, or the world view when swapped) grows and the other gives way, and the motion gauges and lower row follow
the centre column. The centre never drops below 500 px, so a small window gains little and the smallest gains nothing:
Expand is then off, except that a pressed one stays on so it can be released. It is also off while the MFD is
maximised and applies again when Maximise is released, and it keeps its state when the panels are swapped.
Maximise stops drawing the world view and gives the MFD both areas; the motion gauges stay above it
and the lower row below it, widened to match, the crew alert bank docks as a strip along its right edge, vote
popups dock in a row above it, and speech bubbles are not shown (chat still carries the speech). Fire input
ignores the hidden world view, while NAV aiming keeps working. Pressing the button again (Restore)
brings back the earlier arrangement, swapped or not. The toggles use short captions with the full names in their
tooltips so the three of them and the caption fit the narrowest MFD header. On the smallest window a swapped or maximised MFD is short, so
its plots take most of the height and the details scroll beneath them. The state lasts one cockpit session and every exit
restores the world view and the normal HUD, whatever the arrangement. A wide SHP display gives half its width to the hull plot, with two columns of gauges beside it.
Damage, fire, pressure and power overlays share one row, with department labels and hull fitting below.
Compact MFDs retain the vertical layout with independently scrolling details. ACCESS keeps its framed door map
as the largest panel, beside the controls when there is room and above them in compact layouts. Its
details use one scroller without nested scrolling lists, and the bezel matches the selected
Wolfgate theme. Exit restores the same controls and chat draft without reopening
interfaces or changing the player's display settings. The dials switch to compact faces on short windows
(under about 1000 px of height); at 1920x1080 with UI scale 1 nothing in the instrument column, flight controls or
shield panel scrolls, and neither do the SYS and SHD pages of the normal arrangement. The
docking page has a Recenter plot button. The planet timepiece panel is hidden while the cockpit is open, and
replay viewers cannot enter it.

A seated pilot can also link to an accessible, powered gunnery console within normal interaction reach
on the same ship. The nearest eligible console is chosen and retained while usable. FLIGHT/GUNS
switches the left column between flight instruments and the shared weapon list (one scrolling list,
two columns, sorted by name), including selection, saved groups and compact countermeasures. Weapon
entries retain readable text and slim supply bars; scrolling never changes the selection used for
firing and group saves. Left-click or hold over the world viewport or NAV plot fires the
selected weapons on either tab; moving the pointer updates guided-missile aim, and right mouse still pans. While aiming, a preview line runs from
each selected weapon's own position to the aim point, on the NAV plot and on the ship cam. A weapon gets a line when it
ignores line of sight or nothing on its own grid is in the way, the check the server makes before it fires; the ship cam draws nothing while the world view is hidden. A native aiming reticle
replaces the pointer over valid world and NAV targets while offensive weapons are selected, independently
of character combat mode. HUD controls, chat, modifiers, lost focus and an unarmed bank retain ordinary
cursors and mouse interactions. The cockpit holds gun control while GUNS is open or any weapon is selected,
so weapons selected on GUNS stay armed when the pilot returns to FLIGHT to fly and shoot; the click is
consumed by firing there. With nothing selected, FLIGHT releases gun control and keeps the normal pointer
and clicks. A lost link clears the selection and releases control at once; a changed link does the same unless GUNS is open.
Exiting or losing pilot eligibility releases the link and all input hooks. Reach follows normal body-fixture distance and obstruction checks,
independent of the camera. Nearby unopened consoles discover their gun server when acquired. Temporary
gunnery power or access loss clears the weapon link; restoring it reconnects without leaving cockpit mode.
Loss of helm power ends piloting and exits the cockpit.

Commands travel through the existing helm BUI, with seat, reach, power, access and weapon ownership
checked by the server for each request. They reuse native firing, group and flare handlers; no second
gunnery window is opened. Discovery without gun control only supplies telemetry; NPC gunners recognize the pilot as
a human console operator only while the pilot holds gun control (GUNS open or weapons selected). The cockpit link does not apply the crewed-shuttle
rule that blocks a player from holding the helm and gunnery windows together, so one seated pilot can fly and
fire a crewed ship from the cockpit.

Entry points: `WFCockpitUIController`, `WFCockpitView`, `WFCockpitLease` and the console partials.
`WFCockpitPanelLayout` computes where the big panels sit for each arrangement and how wide the side column is, normal or expanded, and is unit tested.
`WFCockpitGunnerySystem` owns authorized gun links; `WFCockpitGunneryPanel` and `WFCockpitFireInput`
provide the embedded controls and aiming. `WFCockpitAimPreview` works out the aim lines in world space for both the
NAV plot (`WFCockpitAimLines`) and the ship cam (`WFCockpitAimOverlay`, added while the cockpit is open and removed through the lease). `WFCockpitSeatComponent` is inherited by seats; `SharedWFCockpitSystem` checks replicated eligibility.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Cockpit/FireControlSystem.Cockpit.cs`](FireControlSystem.Cockpit.cs)
- [`Content.Server/_WF/Cockpit/ShuttleCameraSystem.Cockpit.cs`](ShuttleCameraSystem.Cockpit.cs)
- [`Content.Server/_WF/Cockpit/ShuttleConsoleSystem.CockpitStatus.cs`](ShuttleConsoleSystem.CockpitStatus.cs)
- [`Content.Server/_WF/Cockpit/WFCockpitGunnerySystem.Camera.cs`](WFCockpitGunnerySystem.Camera.cs)
- [`Content.Server/_WF/Cockpit/WFCockpitGunnerySystem.cs`](WFCockpitGunnerySystem.cs)
- [`Content.Server/_WF/Cockpit/WFCombatConsoleSystem.Cockpit.cs`](WFCombatConsoleSystem.Cockpit.cs)

### Shared

- [`Content.Shared/_WF/Cockpit/SharedWFCockpitSystem.cs`](../../../Content.Shared/_WF/Cockpit/SharedWFCockpitSystem.cs)
- [`Content.Shared/_WF/Cockpit/WFCockpitAutopilotUpdateMessage.cs`](../../../Content.Shared/_WF/Cockpit/WFCockpitAutopilotUpdateMessage.cs)
- [`Content.Shared/_WF/Cockpit/WFCockpitGunneryMessages.cs`](../../../Content.Shared/_WF/Cockpit/WFCockpitGunneryMessages.cs)
- [`Content.Shared/_WF/Cockpit/WFCockpitSeatComponent.cs`](../../../Content.Shared/_WF/Cockpit/WFCockpitSeatComponent.cs)

### Client

- [`Content.Client/_WF/Cockpit/CollisionWarningBanner.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/CollisionWarningBanner.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/DefaultGameScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/DefaultGameScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/DockingScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/DockingScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/DockingScreen.CockpitStatus.cs`](../../../Content.Client/_WF/Cockpit/DockingScreen.CockpitStatus.cs)
- [`Content.Client/_WF/Cockpit/FireControlWindow.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/FireControlWindow.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/MapGridControl.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/MapGridControl.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/MapScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/MapScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/MapScreen.CockpitStatus.cs`](../../../Content.Client/_WF/Cockpit/MapScreen.CockpitStatus.cs)
- [`Content.Client/_WF/Cockpit/NavScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/NavScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/NavScreen.CockpitStatus.cs`](../../../Content.Client/_WF/Cockpit/NavScreen.CockpitStatus.cs)
- [`Content.Client/_WF/Cockpit/ResizableChatBox.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ResizableChatBox.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShieldShuntScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShieldShuntScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShieldShuntScreen.CockpitStatus.cs`](../../../Content.Client/_WF/Cockpit/ShieldShuntScreen.CockpitStatus.cs)
- [`Content.Client/_WF/Cockpit/ShipScreen.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShipScreen.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleCameraBar.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleCameraBar.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleConsoleBoundUserInterface.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleConsoleBoundUserInterface.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleConsoleWindow.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleConsoleWindow.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitGunnery.cs`](../../../Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitGunnery.cs)
- [`Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitLayout.cs`](../../../Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitLayout.cs)
- [`Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitStatus.cs`](../../../Content.Client/_WF/Cockpit/ShuttleConsoleWindow.CockpitStatus.cs)
- [`Content.Client/_WF/Cockpit/ShuttleDockControl.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleDockControl.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleExternalCameraSystem.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleExternalCameraSystem.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/ShuttleNavControl.Cockpit.cs`](../../../Content.Client/_WF/Cockpit/ShuttleNavControl.Cockpit.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitAimLines.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitAimLines.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitAimOverlay.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitAimOverlay.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitAimPreview.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitAimPreview.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitCoordinates.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitCoordinates.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitFireInput.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitFireInput.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitFuelBank.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitFuelBank.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitFuelLamp.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitFuelLamp.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitGunneryPanel.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitGunneryPanel.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitInstrumentSizing.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitInstrumentSizing.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitLease.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitLease.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitMfdLayout.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitMfdLayout.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitPanelLayout.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitPanelLayout.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitShipLayout.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitShipLayout.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitSpeechClip.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitSpeechClip.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitStatusLights.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitStatusLights.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitStatusReading.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitStatusReading.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitTcasPanel.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitTcasPanel.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitTcasReading.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitTcasReading.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitUIController.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitUIController.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitVelocityReading.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitVelocityReading.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitView.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitView.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitView.Gunnery.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitView.Gunnery.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitView.Header.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitView.Header.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitView.Hud.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitView.Hud.cs)
- [`Content.Client/_WF/Cockpit/WFCockpitView.Panels.cs`](../../../Content.Client/_WF/Cockpit/WFCockpitView.Panels.cs)
- [`Content.Client/_WF/Cockpit/WFDockMarkerLayout.cs`](../../../Content.Client/_WF/Cockpit/WFDockMarkerLayout.cs)
- [`Content.Client/_WF/Cockpit/WFVelocityVectorInstrument.cs`](../../../Content.Client/_WF/Cockpit/WFVelocityVectorInstrument.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitCameraTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitCameraTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitDockingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitDockingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitFireInputTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitFireInputTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitFtlDriveTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitFtlDriveTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitGunneryPanelTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitGunneryPanelTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitGunneryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitGunneryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitPanInputTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitPanInputTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitSeatTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitSeatTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitStatusTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitStatusTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitTcasTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitTcasTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Cockpit/WFCockpitTest.cs)

### Unit tests

- [`Content.Tests/_WF/Cockpit/WFCockpitCoordinatesTest.cs`](../../../Content.Tests/_WF/Cockpit/WFCockpitCoordinatesTest.cs)
- [`Content.Tests/_WF/Cockpit/WFCockpitInstrumentSizingTest.cs`](../../../Content.Tests/_WF/Cockpit/WFCockpitInstrumentSizingTest.cs)
- [`Content.Tests/_WF/Cockpit/WFCockpitPanelLayoutTest.cs`](../../../Content.Tests/_WF/Cockpit/WFCockpitPanelLayoutTest.cs)
- [`Content.Tests/_WF/Cockpit/WFCockpitTcasReadingTest.cs`](../../../Content.Tests/_WF/Cockpit/WFCockpitTcasReadingTest.cs)
- [`Content.Tests/_WF/Cockpit/WFCockpitVelocityTest.cs`](../../../Content.Tests/_WF/Cockpit/WFCockpitVelocityTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/Cockpit/cockpit-gunnery-mode.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/cockpit-gunnery-mode.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/cockpit-status.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/cockpit-status.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/cockpit.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/cockpit.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/dock-plot.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/dock-plot.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/fuel.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/fuel.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/tcas.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/tcas.ftl)
- [`Resources/Locale/en-US/_WF/Cockpit/velocity.ftl`](../../../Resources/Locale/en-US/_WF/Cockpit/velocity.ftl)

### Textures

- [`Resources/Textures/_WF/Cockpit/attributions.yml`](../../../Resources/Textures/_WF/Cockpit/attributions.yml)
- [`Resources/Textures/_WF/Cockpit/gun_sight.png`](../../../Resources/Textures/_WF/Cockpit/gun_sight.png)

## Non-modular edits

- [`Content.Client/_RMC14/CombatMode/RMCCombatModeUISystem.cs`](../../../Content.Client/_RMC14/CombatMode/RMCCombatModeUISystem.cs): let the cockpit controls own their reticle and ordinary HUD cursors.
- [`Content.Client/CombatMode/CombatModeIndicatorsOverlay.cs`](../../../Content.Client/CombatMode/CombatModeIndicatorsOverlay.cs): character combat indicators must not cover the ship's aiming reticle.
- [`Content.Client/Options/UI/Tabs/MiscTab.xaml`](../../../Content.Client/Options/UI/Tabs/MiscTab.xaml): open the cockpit view automatically
- [`Content.Client/Options/UI/Tabs/MiscTab.xaml.cs`](../../../Content.Client/Options/UI/Tabs/MiscTab.xaml.cs): option to open the cockpit view automatically
- [`Content.Client/Pinpointer/UI/NavMapControl.cs`](../../../Content.Client/Pinpointer/UI/NavMapControl.cs)
  - department labels stay compact instead of growing with map zoom.
  - update shared right-mouse map panning.
- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs): enter the cockpit by itself when the helm opens while buckled.
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs): consume authoritative autopilot status on every page.
- [`Content.Client/Shuttles/UI/ShuttleDockControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleDockControl.xaml.cs)
  - selecting an approach target recentres its panned plot.
  - reset compact port callout placement for this frame.
  - find grids in the panned approach plot without changing the selected dock.
  - pan hulls and their dock buttons together.
  - keep approach guidance anchored to its panned dock.
  - keep full port names and actions out of the approach geometry.
  - pan the pilot's dock marker with the rest of the approach plot.
  - rebuild port callout numbers with their live action rows.
  - use numbered callouts and a separate list for full port actions.
- [`Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs): rotating an FTL destination must not also scroll its MFD page.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): a cockpit aim handler owns its consumed click through release.
- [`Content.Client/UserInterface/Controls/MapGridControl.xaml.cs`](../../../Content.Client/UserInterface/Controls/MapGridControl.xaml.cs)
  - use the same right-mouse gesture on every cockpit plot.
  - deliver releases to the borrowed plot's cockpit input handler.
  - map zoom must not also scroll its containing panel.
- [`Content.Client/UserInterface/Screens/DefaultGameScreen.xaml.cs`](../../../Content.Client/UserInterface/Screens/DefaultGameScreen.xaml.cs)
  - Small cockpit viewports must retain a positive action grid limit.
  - Keep critical alerts in a compact bank beside the cockpit world view.
- [`Content.Client/UserInterface/Screens/SeparatedChatGameScreen.xaml.cs`](../../../Content.Client/UserInterface/Screens/SeparatedChatGameScreen.xaml.cs): Hidden HUD containers can briefly have zero width while borrowed.
- [`Content.Client/UserInterface/Systems/Chat/Widgets/ResizableChatBox.cs`](../../../Content.Client/UserInterface/Systems/Chat/Widgets/ResizableChatBox.cs)
  - the cockpit controls chat sizing.
  - preserve the normal chat margins while docked.
- [`Content.Client/UserInterface/Systems/CloseWindow/CloseRecentWindowUIController.cs`](../../../Content.Client/UserInterface/Systems/CloseWindow/CloseRecentWindowUIController.cs): Escape must not close the hidden helm that supplies the cockpit.
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeContextUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeContextUIController.cs): Escape leaves the cockpit before it opens the game menu.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - report confirmed steering in the initial helm snapshot.
  - keep autopilot lamps current while any helm page is open.
- [`Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs`](../../../Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs): include confirmed autopilot activity in the initial helm state.
- [`Resources/Prototypes/Entities/Structures/Furniture/chairs.yml`](../../../Resources/Prototypes/Entities/Structures/Furniture/chairs.yml): allow every seat family to host the buckled piloting HUD.

<!-- WOLFGATE-GENERATED END -->
