# Faction name
wf-sector-faction-reavers = Ashfall Reavers

# The Underground black market's company, which the Reavers leave alone
wf-company-underground-description = The Underground is the black market and the people who work out of it: fugitives, fixers and their boss. The Reavers leave its ships and crews alone.

# Border notices for Reaver space, in place of the sector control defaults
wf-reaver-notice-entered = WARNING: entering Ashfall Reaver controlled space ({$callsign}). Their patrols attack on sight and will come looking for you.
wf-reaver-notice-left = Leaving Ashfall Reaver controlled space ({$callsign}).

# Sender of the announcements and name of the radio entity
wf-reaver-watch = Sector Watch

# Used as the station when the map has none
wf-reaver-place-origin = the sector core

# Threat tiers
wf-reaver-tier-0 = None
wf-reaver-tier-1 = Scattered
wf-reaver-tier-2 = Organised
wf-reaver-tier-3 = Entrenched
wf-reaver-tier-4 = Dominant
wf-reaver-tier-5 = Overrun

wf-reaver-tier-rising = Reaver threat is rising: {$tier}. { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Stronghold bounty is {$bounty} spesos.
wf-reaver-tier-falling = Reaver threat is falling: {$tier}. { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Stronghold bounty is {$bounty} spesos.

# $station is the nearest station, $bearing and $range are from it
wf-reaver-founded = Reavers have built a stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} spesos.
wf-reaver-regrouped = Reavers have regrouped and built a new stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} spesos.
wf-reaver-broken = The Reaver stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}, is broken. { $cells ->
        [one] {$cells} cell is free.
       *[other] {$cells} cells are free.
    } { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }
wf-reaver-patrol-destroyed = Reaver patrol destroyed at {$callsign}. The cell is clear. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }
wf-reaver-patrol-destroyed-held = Reaver patrol destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }
wf-reaver-retaken = {$callsign} retaken from the Reavers. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }
# $cells is a list of callsigns
wf-reaver-clearing = Reaver space at {$cells} is clearing. Drone activity returns in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Clear the area.
wf-reaver-defeated = The Ashfall Reavers are broken. Their strongholds are gone and the sector is clear.
wf-reaver-retake-name = retaking {$callsign}
wf-reaver-blockade = Reavers are moving to blockade {$station} at {$callsign}. They take the cell in {$minutes} minutes unless traffic keeps the lane.

# A stronghold flying out from a standing one to found itself; $station, $bearing and $range are as above
wf-reaver-convoy-launched = A Reaver convoy is under way to {$callsign}, {$range} km bearing {$bearing} from {$station}. Stop it before it digs in.
wf-reaver-convoy-destroyed = The Reaver convoy bound for {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }

# A hub gaining a level; $minutes is the reinforcement's reckoned flight
wf-reaver-reinforcement-launched = Reaver reinforcements are inbound to {$callsign}, ETA {$minutes} min.
wf-reaver-grown = The Reaver stronghold at {$callsign} has grown to level {$level}.
wf-reaver-reinforcement-destroyed = Reaver reinforcements bound for {$callsign} are destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} spesos paid.
    }

wf-reaver-raid-warning-pa = Reaver raiders are heading for {$ship}, bearing {$bearing}, {$range} km.
wf-reaver-raid-warning-radio = Reaver raiders are heading for {$ship}, bearing {$bearing}, {$range} km.
wf-reaver-raid-station-warning = Reaver raiders are heading for {$station}, bearing {$bearing}, {$range} km.
wf-reaver-raid-hunting = Reaver raiders are hunting {$ship}, bearing {$bearing}, {$range} km.
wf-reaver-intercept-warning = Reaver patrol turning to intercept {$ship}, bearing {$bearing}, {$range} km.

# Map legend line
wf-reaver-legend = {$faction}: {$tier}, { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, bounty {$bounty}{$levels}
wf-reaver-legend-broken = {$faction}: broken, no strongholds standing
wf-reaver-legend-levels = , { $count ->
        [one] level {$levels}
       *[other] levels {$levels}
    }

wf-reaver-round-end = Reavers held up to { $peak ->
        [one] {$peak} cell
       *[other] {$peak} cells
    }. They founded { $founded ->
        [one] {$founded} stronghold
       *[other] {$founded} strongholds
    }, { $broken ->
        [one] {$broken} was
       *[other] {$broken} were
    } broken, { $patrols ->
        [one] {$patrols} patrol was
       *[other] {$patrols} patrols were
    } destroyed, { $convoys ->
        [one] {$convoys} convoy was
       *[other] {$convoys} convoys were
    } stopped and { $raids ->
        [one] {$raids} raid was
       *[other] {$raids} raids were
    } sent. Players retook { $retaken ->
        [one] {$retaken} cell
       *[other] {$retaken} cells
    }.

wf-reaver-round-end-broken = The Reavers were broken { $count ->
        [one] once
       *[other] {$count} times
    }.

# wf_reavers command
cmd-wf_reavers-desc = Shows and drives the Ashfall Reaver campaign.
cmd-wf_reavers-help = Usage: {$command} status | found [x y] | convoy | reinforce <stronghold uid> | tier <n> | spread | patrol | raid | intercept <grid uid> | pause | resume | clear
    found places a level 0 hub at once, an admin shortcut; convoy sends one out from a standing stronghold, as the campaign does; reinforce sends a stronghold its next reinforcement now.
cmd-wf_reavers-hint-sub = Subcommand
cmd-wf_reavers-hint-x = Map x in metres
cmd-wf_reavers-hint-y = Map y in metres
cmd-wf_reavers-hint-tier = Threat tier
cmd-wf_reavers-hint-grid = Grid uid of the ship to intercept
cmd-wf_reavers-hint-stronghold = Stronghold uid
cmd-wf_reavers-no-campaign = No Reaver campaign runs under the current preset.
cmd-wf_reavers-status-header = Campaign {$campaign}: running {$running}, paused {$paused}, tier {$tier}, score {$score}.
cmd-wf_reavers-status-counts = { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, { $patrols ->
        [one] {$patrols} patrol
       *[other] {$patrols} patrols
    }, { $raids ->
        [one] {$raids} raid
       *[other] {$raids} raids
    }.
cmd-wf_reavers-status-clocks = Seconds to next spread: {$spread}. Seconds to next raid: {$raid}.
cmd-wf_reavers-no-stronghold = No stronghold stands. Run wf_reavers found to place one now.
cmd-wf_reavers-status-switch-off = No stronghold stands and none will be founded: {$cvar} is off. Run wf_reavers found to place one anyway.
cmd-wf_reavers-status-mode-off = No stronghold stands and none will be founded: the Ashfall game mode is not running. Admins can force it with {$cvar} or by adding the rule (addgamerule WFReaverCampaignRule).
cmd-wf_reavers-status-not-in-round = No stronghold stands and none will be founded: no round is in progress.
cmd-wf_reavers-status-paused = No stronghold stands and none will be founded: the campaign is paused. Run wf_reavers resume.
cmd-wf_reavers-status-runner-paused = No stronghold stands and none will be founded: the event runner is paused, which holds the campaign too. Unpause it in the Encounters window.
cmd-wf_reavers-status-first-due = No stronghold stands. The first is due in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wf_reavers found to place one now.
cmd-wf_reavers-status-regroup-due = No stronghold stands. The Reavers regroup in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wf_reavers found to place one now.
cmd-wf_reavers-status-founding-overdue = No stronghold stands. A founding is due now and retries every minute until a cell qualifies. Run wf_reavers found to place one now.
cmd-wf_reavers-status-founding-untimed = No stronghold stands and the founding clock has not started. Run wf_reavers found to place one now.
cmd-wf_reavers-status-stronghold = Stronghold {$uid} at {$callsign}: level {$level}, {$prototype}, {$hits} hits, {$patrols} patrols, {$reinforcements} reinforcements{$state}.
cmd-wf_reavers-status-stronghold-inbound = , one inbound
cmd-wf_reavers-status-stronghold-scattered = , broken apart
cmd-wf_reavers-status-reinforcement = Reinforcement {$uid} for {$callsign}, level {$level}: {$phase}, {$range} km from the hub, out {$seconds} s.
cmd-wf_reavers-status-intercept = Patrol {$patrol} intercepting {$ship} ({$uid}) for {$seconds} s.
cmd-wf_reavers-status-mark = {$ship} ({$uid}) is marked for {$seconds} s more.
cmd-wf_reavers-status-convoy-out = A convoy is under way; the next founding waits until it arrives or is stopped.
cmd-wf_reavers-status-convoy = Convoy {$uid} bound for {$callsign}: {$range} km to go, under way {$seconds} s.
cmd-wf_reavers-founded = Founded stronghold {$uid} at {$callsign}.
cmd-wf_reavers-bad-stronghold = Not a standing stronghold: {$arg}
cmd-wf_reavers-reinforce = Reinforcement {$uid} sent to the stronghold at {$callsign}.
cmd-wf_reavers-reinforce-failed = No reinforcement could be sent: the stronghold is at its tier's level cap, one is already on its way, or there is no clear space for it.
cmd-wf_reavers-found-failed = No stronghold could be founded there.
cmd-wf_reavers-convoy = Convoy {$uid} sent to found a stronghold at {$callsign}.
cmd-wf_reavers-convoy-failed = No convoy could be sent: one is already under way, or no held cell qualifies, or no stronghold has clear space beside it.
cmd-wf_reavers-bad-position = Not a position: {$x} {$y}
cmd-wf_reavers-bad-tier = Not a tier from 0 to {$max}: {$arg}
cmd-wf_reavers-tier-set = Tier set to {$tier}.
cmd-wf_reavers-spread = Claimed or contested one cell.
cmd-wf_reavers-spread-failed = No cell could be claimed.
cmd-wf_reavers-patrol = Patrol sent from the stronghold at {$callsign}.
cmd-wf_reavers-patrol-failed = No patrol could be launched.
cmd-wf_reavers-raid = Raid launched.
cmd-wf_reavers-raid-failed = No raid could be launched.
cmd-wf_reavers-bad-grid = Not a grid: {$arg}
cmd-wf_reavers-intercept = A patrol is turning to intercept {$ship}.
cmd-wf_reavers-intercept-failed = No patrol could be sent to intercept {$ship}.
cmd-wf_reavers-paused = Campaign paused.
cmd-wf_reavers-resumed = Campaign resumed.
cmd-wf_reavers-cleared = Cleared the campaign: {$count} encounters ended.

# Encounter names
wf-reaver-name-stronghold = Reaver stronghold {$designation}
wf-reaver-name-level = {$name} (level {$level})
wf-reaver-name-reinforcement = Reaver reinforcements {$designation}
wf-reaver-name-patrol = Reaver patrol {$designation}
wf-reaver-name-raid = Reaver raiders {$designation}
wf-reaver-name-boarders = Reaver boarders {$designation}

# Game mode
wf-reaver-preset-title = Ashfall
wf-reaver-preset-description = Standard sector, and the Ashfall Reavers build strongholds and spread across it.
