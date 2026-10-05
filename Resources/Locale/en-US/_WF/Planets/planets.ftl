## Map and marker names
wf-planet-orbit-map-name = { $planet } orbit
wf-planet-orbit-marker-name = { $planet } orbital marker
wf-planet-network-name = { $planet } planet network
wf-planet-asclepiu-surface = Asclepiu

## FTL
wf-shuttle-console-in-transit = Cannot engage FTL while changing altitude.

## Shuttle console orbit controls
wf-shuttle-console-enter-orbit = Enter orbit: { $planet }
wf-shuttle-console-leave-orbit = Leave orbit: { $planet }
wf-shuttle-console-orbit-none = No planet in range
wf-orbit-no-hull = This console is not aboard a flyable hull.
wf-orbit-no-network = { $planet } has no orbit layer to drop into.
wf-orbit-no-sector = This orbit layer has no sector body to return to.
wf-orbit-not-in-sector = { $planet } is not in this system.
wf-orbit-not-in-orbit = This hull is not in planet orbit.
wf-orbit-out-of-range = Too far from { $planet } for orbital insertion.
wf-orbit-refused = Orbital insertion refused.

## wfplanet command
cmd-wfplanet-desc = Build, inspect and tear down Wolfgate planet networks.
cmd-wfplanet-help = Usage: { $command } <list | build <planet> | delete <planet> | spawn <surface id> | system <starSystem id> | tp <planet>>
cmd-wfplanet-disabled = Planet networks are off. Set wf.planet_networks to true.
cmd-wfplanet-invalid-args = Expected one of: list, build, delete, spawn, system, tp.
cmd-wfplanet-unknown-planet = No sector planet named "{ $planet }".
cmd-wfplanet-unknown-surface = No wfPlanetSurface prototype "{ $surface }".
cmd-wfplanet-unknown-system = No starSystem prototype "{ $system }".
cmd-wfplanet-no-surface = { $planet } has no Wolfgate surface.
cmd-wfplanet-already-built = { $planet } already has a network ({ $network }).
cmd-wfplanet-not-built = { $planet } has no network.
cmd-wfplanet-built = Built { $layers } layers for { $planet }: network { $network }, orbit { $orbit }.
cmd-wfplanet-build-failed = Failed to build a network for { $planet }; see the server log.
cmd-wfplanet-deleted = Deleted the network for { $planet }.
cmd-wfplanet-row = { $planet } | surface: { $surface } | network: { $network }
cmd-wfplanet-row-none = none
cmd-wfplanet-empty = No sector planets registered.
cmd-wfplanet-system-set = Set { $system } on map { $map }.
cmd-wfplanet-no-map = Run this with a map in mind: attach to an entity on the sector map first.
cmd-wfplanet-tp-done = Moved to the orbit layer of { $planet }.
cmd-wfplanet-hint-sub = <list|build|delete|spawn|system|tp>
cmd-wfplanet-hint-planet = <planet name>
cmd-wfplanet-hint-surface = <wfPlanetSurface id>
cmd-wfplanet-hint-system = <starSystem id>

# Unsanctioned worlds
wf-orbit-unsanctioned-title = Unsanctioned world
wf-orbit-unsanctioned-text = [color=#ff3030][bold]{ $planet } is not a sanctioned world.[/bold][/color]
    Entering its orbit is a breach of sector law, and nobody is coming for a hull lost there.
wf-orbit-unsanctioned-proceed = Enter orbit
wf-orbit-unsanctioned-abort = Abort
wf-orbit-unsanctioned-unconfirmed = { $planet } is unsanctioned. Confirm the entry at the console.

# Gravity well
wf-gravity-well-warning = Warning: { $ship } is adrift inside the gravity well of { $planet }. Restore thrust or be pulled into orbit.

## Planet Control admin panel and command
wf-planet-control-title = Planet Control
wf-planet-control-refresh = Refresh
wf-planet-control-none = No worlds in this sector
wf-planet-control-not-built = Not built yet: nobody has visited. Only the sanction can be set.
wf-planet-control-status = { $time } local, { $weather } ({ $seconds } s to next change), { $gravity } g
wf-planet-control-time = Time
wf-planet-control-set = Set
wf-planet-control-dawn = Dawn
wf-planet-control-noon = Noon
wf-planet-control-dusk = Dusk
wf-planet-control-midnight = Midnight
wf-planet-control-weather = Weather
wf-planet-control-weather-clear = Clear
wf-planet-control-weather-seconds = Seconds the weather holds before the planet's own cycle resumes
wf-planet-control-gravity = Gravity
wf-planet-control-sanctioned = Sanctioned world
cmd-planetcontrol-desc = Lists the sector's worlds, or sets one's time, weather, gravity or sanction.
cmd-planetcontrol-help = Usage: planetcontrol [<planet> time <HH:MM> | weather <id|clear> [seconds] | gravity <g> | sanctioned <true|false>]
cmd-planetcontrol-invalid-args = Unknown planet, field or value.
cmd-planetcontrol-no-change = Nothing changed.
cmd-planetcontrol-row = { $planet }: { $time }, { $weather }, { $gravity }g, sanctioned={ $sanctioned }
cmd-planetcontrol-row-unbuilt = { $planet }: not built, sanctioned={ $sanctioned }
cmd-planetcontrol-weather-clear = clear
cmd-planetcontrol-applied = { $planet }: { $changes }
