## The contract board, its messages and the Contracts PDA app.

wf-contract-board-title = Contract Manager
wf-contract-board-name = the contract board
wf-contract-program-name = Contracts
wf-contract-station-unknown = an unknown station
wf-contract-designation-unknown = (unconfirmed)
wf-contract-thalers = {$amount} {$amount ->
        [one] thaler
       *[other] thalers
    }

wf-contract-rank-hostile = Hostile
wf-contract-rank-distrusted = Distrusted
wf-contract-rank-applicant = Applicant
wf-contract-rank-junior = Junior Contractor
wf-contract-rank-member = Contractor
wf-contract-rank-experienced = Experienced Contractor
wf-contract-rank-senior = Senior Contractor

wf-contract-category-all = All offers
wf-contract-category-delivery = Delivery
wf-contract-category-hauling = Hauling
wf-contract-category-supply = Supply
wf-contract-category-mining = Mining
wf-contract-category-bounty = Bounty Hunting
wf-contract-category-salvage = Salvage
wf-contract-category-escort = Escort
wf-contract-category-science = Science
wf-contract-category-planetary = Planetary

wf-contract-tier-short = T{$tier}
wf-contract-tier = Risk tier {$tier}

wf-contract-status-offered = Offered
wf-contract-status-active = In progress
wf-contract-status-completed = Completed
wf-contract-status-failed = Failed
wf-contract-status-abandoned = Abandoned
wf-contract-status-void = Called off

wf-contract-time-left = {$time} left
wf-contract-time-allowed = {$minutes} min allowed

## Board window

wf-contract-tab-offers = Offers ({$count})
wf-contract-tab-accepted = Accepted ({$count}/{$max})
wf-contract-tab-history = History
wf-contract-tab-standing = Standing
wf-contract-rail-entry = {$name} ({$count})
wf-contract-board-slots = {$crew ->
    [1] Solo: {$slots} contract slot. A crew of {$next} would hold more.
   *[other] Crew of {$crew}: {$slots} contract slots, shared. A crew of {$next} would hold one more.
} Track them in the Contracts app on your PDA.
wf-contract-board-no-character = Contracts are held by a character. Ghosts may only browse.
wf-contract-board-owed = {$pay} {$pay ->
        [one] thaler
       *[other] thalers
    } owed to you will be paid when your account can take it.

wf-contract-empty-offers = Nothing posted in this category right now. Contractors post again every few minutes.
wf-contract-empty-planetary = Planetary operations open once surface survey licences are issued. Check back later.
wf-contract-empty-accepted = You hold no contracts. Take one from any contract board.
wf-contract-empty-history = Nothing finished yet this shift.

wf-contract-details-none = Select a contract.
wf-contract-details-title = [font size=14][bold]{$title}[/bold][/font]
wf-contract-details-objectives = OBJECTIVES
wf-contract-details-objective = {$done ->
    [true] [color=#7fd18b]+[/color] [color=#8b98a5]{$text}[/color]
   *[false] [color=#5fc3e4]>[/color] {$text}
}
wf-contract-details-location = LOCATION
wf-contract-details-coordinates = Coordinates: {$x}, {$y} (enter them on your helm's waypoint panel)
wf-contract-details-terms = TERMS
wf-contract-details-reward = Reward
wf-contract-details-deposit = Deposit
wf-contract-details-deposit-none = None
wf-contract-details-time = Time limit
wf-contract-details-deadline = Time left
wf-contract-details-offer-ends = Offer ends in

wf-contract-action-accept = {$deposit ->
    [0] Accept contract
   *[other] Accept contract (pay {$deposit} deposit)
}
wf-contract-action-abandon = Abandon contract
wf-contract-action-abandon-confirm = {$deposit ->
    [0] Press again to abandon
   *[other] Press again to abandon and lose the {$deposit} deposit
}

wf-contract-standing-help = Standing is kept between shifts. It rises with each contract you finish, and falls when one fails, is abandoned, works against a contractor's interests, or when you fire on a target someone else holds. Each shift it drifts a little back toward neutral. Tier 2 needs Applicant, tier 3 and up need more; Distrusted gets tier 1 only, and Hostile contractors won't deal with you.
wf-contract-standing-absent = {$description} (not in the sector this shift)
wf-contract-standing-progress = {$points} / {$next} to the next rank
wf-contract-standing-max = {$points}: top rank

wf-contract-pda-hint = {$offers} offers on the boards. {$crew ->
    [1] Solo: {$slots} slot.
   *[other] Crew of {$crew}: {$slots} shared slots.
}

## Why an offer cannot be taken

wf-contract-locked-no-character = You need to be playing a character to take contracts.
wf-contract-locked-rank = Needs the rank of {$rank} with this contractor.
wf-contract-locked-full = Your crew already holds {$max ->
    [one] its one contract. A bigger crew can hold more.
   *[other] all {$max} of its contracts. A bigger crew can hold more.
}
wf-contract-locked-requires = You need to be carrying: {$items}.
wf-contract-locked-crew = Needs a crew of {$needed} in the sector; yours has {$size}. Form a crew in Crew Setup.
wf-contract-locked-company = {$contractor} only deals with its own people.
wf-contract-locked-hostile = {$contractor} won't deal with you. Your standing with them is Hostile.
wf-contract-error-gone = Someone else took that contract.
wf-contract-error-deposit = Your account can't cover the {$deposit} {$deposit ->
        [one] thaler
       *[other] thalers
    } deposit.
wf-contract-error-start = The contract fell through before it started. Your deposit was returned.

## Messages to the holder

wf-contract-message-wrap = [color=#9fd3e0][bold]Contracts:[/bold] {$message}[/color]
wf-contract-accepted = Accepted "{$title}". You have {$deadline} minutes. Track it in the Contracts app on your PDA.
wf-contract-abandoned = Abandoned "{$title}". Any deposit is forfeit.
wf-contract-completed = "{$title}" complete. {$pay} {$pay ->
        [one] thaler
       *[other] thalers
    } paid to your account{$crew ->
        [1] .
       *[other] , your share of the crew's pay.
    }
wf-contract-completed-owed = "{$title}" complete. {$pay} {$pay ->
        [one] thaler is
       *[other] thalers are
    } owed to you and will be paid when your account can take it.
wf-contract-failed = "{$title}" failed: {$reason} Any deposit is forfeit.
wf-contract-void = "{$title}" was called off: {$reason} {$deposit ->
    [0] {""}
   *[other] Your {$deposit} {$deposit ->
        [one] thaler
       *[other] thalers
    } deposit was returned.
}
wf-contract-owed-paid = {$pay} {$pay ->
        [one] thaler owed from finished contracts was
       *[other] thalers owed from finished contracts were
    } paid to your account.
wf-contract-package-delivered = "{$title}": {$done} of {$count} delivered.
wf-contract-anomaly-surveyed = "{$title}": {$done} of {$count} anomalies surveyed.

wf-contract-reason-expired = the deadline passed.
wf-contract-reason-lost = the cargo was destroyed or lost.
wf-contract-reason-escaped = the target got away.
wf-contract-reason-target-gone = the target is no longer out there.
wf-contract-reason-escort-lost = the ship you were escorting was lost.
wf-contract-reason-anomaly-collapsed = an anomaly collapsed before it was surveyed.
wf-contract-reason-hull-lost = the hull was destroyed.

wf-contract-interfered = You fired on a target held by {$claimant} under a {$contractor} contract. Your standing with {$contractor} fell by {$points}.

## Contract cargo

wf-contract-package-name = {$name} for {$destination}
wf-contract-crate-name = contract crate #{$id} for {$destination}
wf-contract-crate-void-name = empty contract crate
wf-contract-wreck-name = Wreck (contract #{$id})
wf-contract-item-examine = [color=#9fd3e0]Contract #{$id}, "{$title}", held by {$holder}. Deliver to {$destination}.[/color]
wf-contract-crate-line = {$done ->
    [true] [color=#7fd18b]{$name}: {$have}/{$count}[/color]
   *[false] {$name}: {$have}/{$count}
}
wf-contract-crate-missing-line = {$count} {$name}
wf-contract-crate-short = The manifest is short: {$missing}.

## Where to go, and what to do

wf-contract-location-courier = Pick up at {$origin}, deliver to {$destination} ({$km} km).
wf-contract-location-supply = Collect the manifest crate at the board; deliver it closed to {$destination} ({$km} km).
wf-contract-location-bounty = {$active ->
    [true] Target last reported about {$km} km out from {$home}. It shows on the sector radar.
   *[false] Target reported about {$km} km out from {$home}. Exact position on accepting.
}
wf-contract-location-recovery = {$active ->
    [true] Wreck about {$km} km out from {$home}. Bring the find to {$destination}.
   *[false] Wreck reported about {$km} km out from {$home}. Exact position on accepting; bring the find to {$destination}.
}

wf-contract-objective-courier = Deliver the cargo to {$destination} ({$done}/{$count})
wf-contract-objective-goods = {$active ->
    [true] Pack {$name} ({$have}/{$count})
   *[false] Pack {$count} {$name}
}
wf-contract-objective-crate = Close the crate aboard {$destination}
wf-contract-objective-bounty = Destroy {$target}
wf-contract-objective-recover = Recover the {$item} from the wreck
wf-contract-objective-return = Bring the {$item} to {$destination}
wf-contract-location-survey = {$active ->
    [true] Derelict about {$km} km out from {$home}. Expect hostiles aboard.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wf-contract-location-tow = {$active ->
    [true] Derelict about {$km} km out from {$home}. Tow it to within {$range} m of {$home}.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wf-contract-objective-tow = {$gap ->
    [-1] Tow the derelict to within {$range} m of {$destination}
   *[other] Tow the derelict to within {$range} m of {$destination} ({$gap} m away)
}
wf-contract-objective-survey = Scan every anomaly aboard with an anomaly scanner ({$done}/{$count})
wf-contract-objective-escort-meet = Meet {$target} at the rendezvous
wf-contract-objective-escort = See {$target} safely to {$destination}
wf-contract-location-escort = {$active ->
    [true] Rendezvous about 1–2 km out from {$home}; escort to {$destination} ({$km} km). It waits for you there.
   *[false] Rendezvous near {$home}, escort to {$destination} ({$km} km). Exact position on accepting.
}

wf-contract-claimed-name = {$name} [claimed: {$claimant}]
wf-contract-marker-name = Contract #{$id}
wf-contract-row-holder = held by {$holder}
wf-contract-harms = -{$points} with {$contractor}
wf-contract-credits = {$amount} {$credit}
wf-contract-details-bonus = Bonus
wf-contract-details-holder = Held by
wf-contract-details-harms = Standing
wf-contract-employer = Working for {$contractor}, on behalf of {$employer}.
wf-contract-details-requires = Bring
wf-contract-requires-rope = a coil of load-bearing rope or tow cable
wf-contract-requires-tether-installer = a tether installer
wf-contract-row-crew = crew of {$crew}+
wf-contract-details-crew = Needs
wf-contract-details-crew-bonus = Crew bonus
wf-contract-details-share = Crew pay
wf-contract-crew-bonus = +{$percent}% for each crewmate past {$past}, up to {$most} more
wf-contract-share = {$pool} thalers for a crew of {$size}, {$each} each

wf-contract-goods-entry = {$count} {$name}

## Admin commands

cmd-contractpost-desc = Posts an offer on the contract board from a contract template.
cmd-contractpost-help = Usage: contractpost <wfContract id>
cmd-contractpost-hint = wfContract id
cmd-contractpost-invalid = Expected one wfContract prototype id.
cmd-contractpost-failed = Could not post {$template}: no board, its contractor's station is not in the round, or it has no valid destination.
cmd-contractpost-done = Posted #{$id} "{$title}" for {$reward} {$reward ->
        [one] thaler
       *[other] thalers
    }.
cmd-contractlist-desc = Lists every offer and live contract on the contract board.
cmd-contractlist-help = Usage: contractlist
cmd-contractlist-none = Nothing on the contract board.
cmd-contractlist-entry = #{$id} [{$status}] {$title}: {$reward} {$reward ->
        [one] thaler
       *[other] thalers
    }, held by {$holder}

## Guidebook

guide-entry-wf-contracts = Contracts

## The second set of objectives.

wf-contract-location-mail = Collect the parcels at the board; deliver each to its own station ({$count} stops, about {$km} km).
wf-contract-objective-mail = Deliver the parcel for {$destination}
wf-contract-location-sweep = {$active ->
    [true] {$count} points out from {$home}, up to about {$km} km. The next one is on your waypoints.
   *[false] {$count} points out from {$home}, up to about {$km} km. Positions on accepting.
}
wf-contract-objective-sweep-count = Log {$count} points in open space
wf-contract-objective-sweep = Log the point at {$x}, {$y}
wf-contract-location-derelict = {$active ->
    [true] Derelict about {$km} km out from {$home}.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wf-contract-objective-clear = Destroy every nest aboard ({$done}/{$count})
wf-contract-objective-repair-weld = Weld the beacon whole
wf-contract-objective-repair-cell = Fit a power cell
wf-contract-objective-download = Stay by the terminal until the download completes ({$percent}%, {$minutes} min in all)
wf-contract-objective-inspect = Find the suspect cargo crate and inspect it

wf-contract-point-logged = "{$title}": {$done} of {$count} points logged.
wf-contract-download-started = "{$title}": download started. Stay by the terminal; it stops when no one is near.
wf-contract-follow-up = A new lead: "{$title}" is on the board for your crew for the next {$minutes} minutes.
wf-contract-inspect-verb = Inspect cargo
wf-contract-inspect-done = You go through the crate against its manifest and log what's really inside.
wf-contract-reason-device-lost = the objective aboard the derelict was destroyed.
