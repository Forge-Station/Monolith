# Notices to a ship crossing a border; a faction may replace them with its own lines taking the same arguments
sector-control-notice-sender = Sector Watch
sector-control-notice-entered = WARNING: entering {$faction} controlled space ({$callsign}).
sector-control-notice-left = Leaving {$faction} controlled space. Now in open space at {$callsign}.

# Sector map: the top-bar button and key, the game menu entry and its window; distance is km from the sector centre
wf-sector-map-button = Sector map
wf-sector-map-button-tooltip = Open the sector map.
ui-options-function-open-sector-map = Open sector map
wf-sector-map-window-title = Sector map
wf-sector-map-centre-on-me = Centre on me
wf-sector-map-whole-sector = Whole sector
wf-sector-map-scale = {$width} km across
wf-sector-map-no-data = No territory data
wf-sector-map-hover-held = {$callsign} ({$distance} km): {$faction}
wf-sector-map-hover-open = {$callsign} ({$distance} km): open space
wf-sector-map-hover-contested = {$callsign} ({$distance} km): contested by {$faction}, {$time}
wf-sector-map-hover-challenged = {$callsign} ({$distance} km): held by {$holder}, challenged by {$faction}, {$time}
wf-sector-map-hover-disputed = {$callsign} ({$distance} km): disputed, {$faction} ships and a player ship are both there
wf-sector-map-hover-retaking = {$callsign} ({$distance} km): held by {$faction}, being retaken, {$time}
wf-sector-map-hover-clearing = {$callsign} ({$distance} km): clearing, drones return in {$time}
wf-sector-map-legend-clearing = Clearing: no drones yet
wf-sector-map-clearing-label = Clearing {$time}
wf-sector-map-legend-disputed = Disputed: timers paused
wf-sector-map-legend-retaking = Being retaken
wf-sector-map-retake-label = Retaking {$time}

# Sector map hover card for a blip; distance and bearing are from the player, or from the origin when they are not on the map
wf-sector-map-kind-station = Station
wf-sector-map-kind-ship = Ship
wf-sector-map-category-traffic = Traffic
wf-sector-map-category-patrol = Patrol
wf-sector-map-category-threat = Threat
wf-sector-map-category-distress = Distress
wf-sector-map-card-range = {$distance} km, bearing {$bearing}
wf-sector-map-card-range-core = {$distance} km from the sector core, bearing {$bearing}
wf-sector-map-card-held = {$callsign}: held by {$faction}
wf-sector-map-card-open = {$callsign}: open space
wf-sector-map-card-contested = Contested by {$faction}, {$time}
wf-sector-map-card-challenged = {$callsign}: held by {$holder}, challenged by {$faction}, {$time}
wf-sector-map-card-disputed = Disputed with {$faction}: timers paused
wf-sector-map-card-retaking = Being retaken, {$time}
wf-sector-map-card-clearing = Clearing, drones return in {$time}

# wf_sectormap command
cmd-wf_sectormap-desc = Opens or closes the sector map.
cmd-wf_sectormap-help = Usage: {$command}

# wf_sector command
cmd-wf_sector-desc = Shows and edits faction territory on the sector map.
cmd-wf_sector-help = Usage: {$command} status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | challenge <faction> <q> <r> <minutes> | clear [faction] | callsign <x> <y>
cmd-wf_sector-hint-sub = Subcommand
cmd-wf_sector-hint-faction = Faction id
cmd-wf_sector-hint-q = Cell q
cmd-wf_sector-hint-r = Cell r
cmd-wf_sector-hint-minutes = Minutes until the contest ends
cmd-wf_sector-hint-challenge-minutes = Minutes until the challenge ends
cmd-wf_sector-hint-x = Map x in metres
cmd-wf_sector-hint-y = Map y in metres
cmd-wf_sector-status-header = Sector control: enabled {$enabled}, map {$map}, cell size {$size} m.
cmd-wf_sector-status-faction = {$faction} ({$name}): {$cells} cells {$callsigns}
cmd-wf_sector-status-contest = Contested {$callsign} by {$faction}, {$seconds} s left.
cmd-wf_sector-status-challenge = Challenged {$callsign} by {$faction}, held by {$holder}, {$seconds} s left.
cmd-wf_sector-status-retake = Retaking {$callsign}, {$seconds} s left.
cmd-wf_sector-claimed = {$faction} now holds {$callsign}.
cmd-wf_sector-claim-failed = {$faction} cannot claim {$callsign}.
cmd-wf_sector-freed = {$callsign} is free.
cmd-wf_sector-not-held = {$callsign} is not held or contested.
cmd-wf_sector-contested = {$faction} contests {$callsign}.
cmd-wf_sector-contest-failed = {$faction} cannot contest {$callsign}.
cmd-wf_sector-challenged = {$faction} challenges {$callsign}.
cmd-wf_sector-challenge-failed = {$faction} cannot challenge {$callsign}.
cmd-wf_sector-bad-minutes = Not a number of minutes: {$arg}
cmd-wf_sector-cleared = Cleared {$count} cells.
cmd-wf_sector-bad-position = Not a position: {$x} {$y}
cmd-wf_sector-callsign = {$callsign} is cell ({$q}, {$r}), centre at {$x} {$y}.
cmd-wf_sector-unknown-faction = Unknown faction: {$faction}
cmd-wf_sector-bad-cell = Not a cell: {$q} {$r}

# Company claim beacons. A researched flatpack, anchored in the sector, paints one hex until it is destroyed.
wf-sector-beacon-name = claim beacon
wf-sector-beacon-desc = Anchor it on a hull or lattice in the sector and activate it. The hex turns your company's colour until the beacon is destroyed.
wf-sector-beacon-immortal-desc = An admin or NPC claim beacon. It ignores damage, and a player beacon cannot paint its hex.
wf-sector-beacon-flatpack-name = claim beacon flatpack
wf-sector-beacon-flatpack-desc = Unpack it with a wrench on a hull or lattice in the sector, then activate the beacon.
wf-sector-beacon-named = {$company} beacon
wf-sector-beacon-verb-activate = Activate
wf-sector-beacon-activated = {$callsign} is held by {$company}.
wf-sector-beacon-occupied = A beacon is already in this hex. Remove it first.
wf-sector-beacon-held = This hex is already held.
wf-sector-beacon-not-in-space = Anchor the beacon on a grid in the sector.
wf-sector-beacon-no-company = You need a company to claim a hex.
wf-sector-beacon-no-faction = That company cannot claim hexes.
wf-sector-beacon-refused = This hex cannot be claimed.
wf-sector-beacon-admin-only = Only an admin can activate this beacon.
wf-sector-beacon-already-on = This beacon is already on.
wf-sector-beacon-examine-on = Holding {$callsign} for {$company}.
wf-sector-beacon-examine-off = Off. Activate it in the sector to claim the hex.
wf-sector-beacon-legend = {$faction}: {$count} {$count ->
    [one] beacon
    *[other] beacons
}
research-technology-sector-beacon = Claim beacons
