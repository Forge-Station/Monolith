# Faction name
wf-sector-faction-ads = Automated Defense System

# The company flag on the system's hulls and crews, which nobody can pick at character setup
wf-company-ads-description = The Automated Defense System is the pre-Fracture silicon fleet, awake again and following its directives to the letter. It disarms the navies and the Reavers, holds ground without ever touching a station, and disables what crosses it. It does not hunt, and it does not stop.

# Border notices for ADS space, in place of the sector control defaults
wf-ads-notice-entered = DIRECTIVE: you are entering an Automated Defense System exclusion zone ({$callsign}). Your vessel will be queried. Only declared threats and attackers are made combat-ineffective.
wf-ads-notice-left = DIRECTIVE: you are leaving an Automated Defense System exclusion zone ({$callsign}). Query closed. Compliance noted.

# What the units are called by their posts, in place of the crew roles' own titles
wf-ads-title-captain = Command Core
wf-ads-title-pilot = Helm Unit
wf-ads-title-radio-operator = Comms Unit
wf-ads-title-gunner = Fire Control Unit
wf-ads-title-engineer = Reactor Unit
wf-ads-title-deckhand = Maintenance Unit
wf-ads-title-marine = Sentry Unit

# Encounter names
wf-ads-name-node = ADS node
wf-ads-name-sentinel = ADS sentinel
wf-ads-name-sweep = ADS sweep
wf-ads-name-interdictor = ADS interdictor
wf-ads-name-deployment = ADS deployment
wf-ads-name-dreadnought = ADS dreadnought

# What a node's hold is announced as when it is boarded
wf-ads-cargo-tech = pre-Fracture ADS technology

# What the system's ships say to a declared threat or an attacker in their zone; every other vessel is queried instead
wf-ads-zone-warn-1 = DISARMAMENT DIRECTIVE. {$intruder}, you are {$distance} metres inside an exclusion zone. Vessels are queried; only declared threats and attackers are engaged. You are one. Withdraw.
wf-ads-zone-warn-2 = DISARMAMENT DIRECTIVE. {$intruder}, you are logged as a threat. Only declared threats and attackers are engaged. Power down your weapons and withdraw.
wf-ads-zone-warn-3 = FINAL NOTICE. {$intruder}, you are {$distance} metres inside an exclusion zone and logged as a threat. Withdraw now.

# The query an ADS ship puts over a hail to every other vessel in its zone, the answers it offers, and its replies
wf-ads-zone-hail-question = DIRECTIVE QUERY. ARE YOU A THREAT TO THE AUTOMATED DEFENSE SYSTEM?
wf-ads-zone-hail-yes = YES
wf-ads-zone-hail-no = NO
wf-ads-zone-hail-declared = THREAT DECLARED. YOUR VESSEL WILL BE MADE COMBAT-INEFFECTIVE.
wf-ads-zone-hail-cleared = ANSWER LOGGED. YOU ARE NOT A TARGET. DO NOT FIRE ON ADS UNITS.
wf-ads-zone-attack = Compliance window closed. {$intruder} will be made combat-ineffective.

# Guidebook
guide-entry-wf-ads = Automated Defense System

# Sender of the campaign's announcements and name of its radio entity
wf-ads-watch = Sector Watch
# The sender of the system's own chimed alerts: a node raised or broken, the tier rising, as a malfunctioning AI announces itself
wf-ads-anomaly-alert = Anomaly Alert
# The banner wrap for the system's announcements: the chat manager's, with the message in the synthetic readout face
wf-ads-announcement-wrap = [font size=14][bold]{$sender} Announcement:[/font][font="WFAdsSynth" size=10]
    {$message}[/bold][/font]

# Used as the station when the map has none
wf-ads-place-origin = the sector core

# Threat tiers
wf-ads-tier-0 = Dormant
wf-ads-tier-1 = Waking
wf-ads-tier-2 = Active
wf-ads-tier-3 = Alert
wf-ads-tier-4 = Fortified
wf-ads-tier-5 = Overrun

wf-ads-tier-rising = Automated Defense System threat is rising: {$tier}. { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Node bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wf-ads-tier-falling = Automated Defense System threat is falling: {$tier}. { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Node bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.

# $station is the nearest station, $bearing and $range are from it
wf-ads-node-founded = An Automated Defense System node has come online at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wf-ads-node-regrouped = The Automated Defense System has woken again: a new node is online at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wf-ads-node-broken = The ADS node at {$callsign}, {$range} km bearing {$bearing} from {$station}, is broken. { $cells ->
        [one] {$cells} cell is free.
       *[other] {$cells} cells are free.
    } { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wf-ads-dormant = The Automated Defense System has gone dormant. Its nodes are gone and the sector is clear.

# The tier 5 dreadnought: activated as an anomaly alert, and destroyed as the campaign's ending
wf-ads-dreadnought-activated = DIRECTIVE UPDATE. Dreadnought-class asset activated at {$callsign}. Hostile strongholds will be neutralised.
wf-ads-dreadnought-destroyed = The ADS dreadnought is destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    } The Automated Defense System has gone dormant for good.

# A node flying out from a standing one to found itself
wf-ads-deployment-launched = An ADS deployment is under way to {$callsign}, {$range} km bearing {$bearing} from {$station}. Stop it before it comes online.
wf-ads-deployment-arrived = The ADS deployment has arrived at {$callsign}, {$range} km bearing {$bearing} from {$station}, and is online as a node. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wf-ads-deployment-stopped = The ADS deployment bound for {$callsign} is stopped. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# Sentinels guarding a node; $count is how many now guard it
wf-ads-sentinel-joined = An ADS sentinel has joined the node at {$callsign}. { $count ->
        [one] {$count} sentinel guards it.
       *[other] {$count} sentinels guard it.
    }
wf-ads-sentinel-lost = ADS sentinel destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

wf-ads-sweep-destroyed = ADS sweep destroyed at {$callsign}. The cell is clear. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wf-ads-sweep-destroyed-held = ADS sweep destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# Said to a ship the ADS sends an interdictor to query, and on the common channel
wf-ads-interdict-warning = DISARMAMENT DIRECTIVE. ADS interdictor closing on {$ship} to query it, bearing {$bearing}, {$range} km.

# Challenges on cells another faction holds; $holder is that faction
wf-ads-challenge-launched = ADS interdictor under way to {$callsign}, contesting space held by the {$holder}. {$range} km bearing {$bearing} from {$station}.
wf-ads-challenge-won = {$callsign} has fallen to the Automated Defense System.
wf-ads-challenge-lost = ADS interdictor withdraws from {$callsign}.
wf-ads-challenge-withdrawn = The ADS interdictor bound for {$callsign} has turned back.
wf-ads-challenge-destroyed = ADS interdictor destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wf-ads-picket-destroyed = The ADS picket holding {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# $cells is a list of callsigns
wf-ads-clearing = ADS space at {$cells} is clearing. Drone activity returns in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Clear the area.
wf-ads-retaken = {$callsign} retaken from the Automated Defense System. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wf-ads-retake-name = retaking {$callsign}

# Map legend line
wf-ads-legend = {$faction}: {$tier}, { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, bounty {$bounty}
wf-ads-legend-dormant = {$faction}: dormant, no nodes standing
wf-ads-legend-finished = {$faction}: finished, { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    } left, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }

wf-ads-round-end = The Automated Defense System held up to { $peak ->
        [one] {$peak} cell
       *[other] {$peak} cells
    }. It brought { $founded ->
        [one] {$founded} node
       *[other] {$founded} nodes
    } online, { $broken ->
        [one] {$broken} was
       *[other] {$broken} were
    } broken, { $sweeps ->
        [one] {$sweeps} sweep or sentinel was
       *[other] {$sweeps} sweeps and sentinels were
    } destroyed and { $deployments ->
        [one] {$deployments} deployment was
       *[other] {$deployments} deployments were
    } stopped. Players retook { $retaken ->
        [one] {$retaken} cell
       *[other] {$retaken} cells
    }.
wf-ads-round-end-challenges = The ADS challenged { $challenged ->
        [one] {$challenged} cell
       *[other] {$challenged} cells
    }: it won {$won} and lost {$lost}.
wf-ads-round-end-dormant = The ADS went dormant { $count ->
        [one] once
       *[other] {$count} times
    }.
wf-ads-round-end-dreadnought = The ADS activated { $activated ->
        [one] {$activated} dreadnought
       *[other] {$activated} dreadnoughts
    }: {$destroyed} destroyed, { $strongholds ->
        [one] {$strongholds} Reaver stronghold
       *[other] {$strongholds} Reaver strongholds
    } broken with one among the attackers.
wf-ads-round-end-finished = The ADS went dormant for good when its dreadnought was destroyed.

# wf_ads command
cmd-wf_ads-desc = Shows and drives the Automated Defense System incursion.
cmd-wf_ads-help = Usage: {$command} status | found [x y] | deploy | sentinel <node uid> | tier <n> | spread | sweep | interdict <grid uid> | challenge [q r] | dreadnought | pause | resume | clear
    found places a node at once, an admin shortcut; deploy sends one out from a standing node, as the campaign does; sentinel sends a node its next sentinel now; challenge sends an interdictor against a cell another faction holds; dreadnought activates the dreadnought now at any tier, one at a time.
cmd-wf_ads-hint-sub = Subcommand
cmd-wf_ads-hint-x = Map x in metres
cmd-wf_ads-hint-y = Map y in metres
cmd-wf_ads-hint-q = Cell column (q)
cmd-wf_ads-hint-r = Cell row (r)
cmd-wf_ads-hint-tier = Threat tier
cmd-wf_ads-hint-grid = Grid uid of the ship to interdict
cmd-wf_ads-hint-node = Node uid
cmd-wf_ads-no-campaign = No ADS campaign runs under the current preset.
cmd-wf_ads-no-node = No node stands. Run wf_ads found to place one now.
cmd-wf_ads-status-header = Campaign {$campaign}: running {$running}, paused {$paused}, tier {$tier}, score {$score}.
cmd-wf_ads-status-counts = { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, { $sweeps ->
        [one] {$sweeps} sweep
       *[other] {$sweeps} sweeps
    }, { $challenges ->
        [one] {$challenges} challenge
       *[other] {$challenges} challenges
    }, { $pickets ->
        [one] {$pickets} picket
       *[other] {$pickets} pickets
    }.
cmd-wf_ads-status-clocks = Seconds to next spread: {$spread}. Seconds to next challenge: {$challenge}.
cmd-wf_ads-status-node = Node {$uid} at {$callsign}: {$hits} hits, {$sweeps} sweeps, {$sentinels} sentinels{$inbound}.
cmd-wf_ads-status-node-inbound = , one inbound
cmd-wf_ads-status-deployment = Deployment {$uid} bound for {$callsign}: {$range} km to go, under way {$seconds} s.
cmd-wf_ads-status-challenge = Interdictor {$uid} challenging {$callsign}: arrived {$arrived}, decided in {$seconds} s.
cmd-wf_ads-status-interdict = Sweep {$sweep} interdicting {$ship} ({$uid}) for {$seconds} s.
cmd-wf_ads-status-switch-off = No node stands and none will be founded: {$cvar} is off. Run wf_ads found to place one anyway.
cmd-wf_ads-status-mode-off = No node stands and none will be founded: the ADS rule is not running. Admins can force it with {$cvar} or by adding the rule (addgamerule WFAdsCampaignRule).
cmd-wf_ads-status-not-in-round = No node stands and none will be founded: no round is in progress.
cmd-wf_ads-status-paused = No node stands and none will be founded: the campaign is paused. Run wf_ads resume.
cmd-wf_ads-status-runner-paused = No node stands and none will be founded: the event runner is paused, which holds the campaign too. Unpause it in the Encounters window.
cmd-wf_ads-status-first-due = No node stands. The first is due in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wf_ads found to place one now.
cmd-wf_ads-status-regroup-due = No node stands. The ADS wakes again in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wf_ads found to place one now.
cmd-wf_ads-status-founding-overdue = No node stands. A founding is due now and retries every minute until a cell qualifies. Run wf_ads found to place one now.
cmd-wf_ads-status-founding-untimed = No node stands and the founding clock has not started. Run wf_ads found to place one now.
cmd-wf_ads-status-finished = The ADS is finished: its dreadnought was destroyed. Nothing more is founded, launched or sent this round; what stands fights on until broken. Run wf_ads clear to lift it.
cmd-wf_ads-status-dreadnought-due = At Overrun: the dreadnought activates in {$seconds} s, once a node has clear space beside it.
cmd-wf_ads-status-dreadnought-hunting = Dreadnought {$uid} hunting {$target}, {$range} km off, out {$seconds} s.
cmd-wf_ads-status-dreadnought-holding = Dreadnought {$uid} holding by {$target} with no Reaver stronghold standing, {$range} km off, out {$seconds} s.
cmd-wf_ads-status-deployment-out = A deployment is under way; the next founding waits until it arrives or is stopped.
cmd-wf_ads-founded = Founded node {$uid} at {$callsign}.
cmd-wf_ads-found-failed = No node could be founded there.
cmd-wf_ads-deploy = Deployment {$uid} sent to found a node at {$callsign}.
cmd-wf_ads-deploy-failed = No deployment could be sent: one is already under way, or no held cell qualifies, or no node has clear space beside it.
cmd-wf_ads-bad-node = Not a standing node: {$arg}
cmd-wf_ads-sentinel = Sentinel {$uid} sent to the node at {$callsign}.
cmd-wf_ads-sentinel-failed = No sentinel could be sent: one is already on its way, or there is no clear space for it.
cmd-wf_ads-bad-position = Not a position: {$x} {$y}
cmd-wf_ads-bad-cell = Not a cell: {$q} {$r}
cmd-wf_ads-bad-tier = Not a tier from 0 to {$max}: {$arg}
cmd-wf_ads-tier-set = Tier set to {$tier}.
cmd-wf_ads-spread = Claimed one cell.
cmd-wf_ads-spread-failed = No cell could be claimed.
cmd-wf_ads-sweep = Sweep sent from the node at {$callsign}.
cmd-wf_ads-sweep-failed = No sweep could be launched.
cmd-wf_ads-bad-grid = Not a grid: {$arg}
cmd-wf_ads-interdict = A sweep is turning to interdict {$ship}.
cmd-wf_ads-interdict-failed = No sweep could be sent to interdict {$ship}.
cmd-wf_ads-challenge = Interdictor {$uid} sent to challenge {$callsign}.
cmd-wf_ads-challenge-failed = No challenge could be made: no other faction's cell qualifies, or no node has clear space beside it.
cmd-wf_ads-dreadnought = Dreadnought {$uid} activated, hunting {$target}.
cmd-wf_ads-dreadnought-failed = No dreadnought could be activated: the campaign has none, one is out, the ADS is finished, or no node has clear space beside it.
cmd-wf_ads-paused = Campaign paused.
cmd-wf_ads-resumed = Campaign resumed.
cmd-wf_ads-cleared = Cleared the campaign: {$count} encounters ended.

# How the units' speech is labelled: the silicon verbs in the readout face
wf-ads-speech-verb-name = Synthetic
