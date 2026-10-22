wf-encounter-ship-name = {$vessel} {$designation}
wf-encounter-name-convoy = Convoy {$designation}
wf-encounter-open-space = open space

wf-encounter-name-patrol = TSF patrol {$designation}
wf-encounter-name-freighter = Independent freighter {$designation}
wf-encounter-name-tsf-transport = TSF transport {$designation}
wf-encounter-cargo-none = no cargo
wf-encounter-cargo-general = general cargo
wf-encounter-cargo-food = foodstuffs
wf-encounter-cargo-medical = medical supplies
wf-encounter-cargo-industrial = industrial materials
wf-encounter-cargo-parts = ship parts
wf-encounter-cargo-munitions = munitions
wf-encounter-cargo-arms = arms and equipment
wf-encounter-cargo-luxury = luxury goods
wf-encounter-cargo-salvage = salvage and scrap
wf-encounter-announce-freight-run = This is {$name}, entering the sector with {$cargo}. We make {$stops} stops, first {$destination}. Any traffic on our lane, please keep clear.
wf-encounter-announce-tsf-transport = All vessels, this is {$name}, a Trans-Solar Federation transport under escort, inbound to {$destination}. Stay outside one kilometre. You will not be warned twice.
# $need is fuel, thrusters or none. A real call gives the ship's registered name as scanners show it, its position and what it lacks.
wf-encounter-distress-adrift = Mayday, mayday. This is {$name}, adrift at {$x}, {$y}. { $need ->
    [fuel] Our generators are dry and our batteries are flat. We need fuel brought aboard to get them started.
    [thrusters] Our thrusters are wrecked. We need someone with a repair device or spare thrusters.
   *[none] We have lost propulsion. Requesting assistance from any vessel.
}
wf-encounter-zone-warn-label = WARNING ZONE
wf-encounter-zone-attack-label = ATTACK ZONE
wf-encounter-zone-warn-1 = {$intruder}, you are {$distance} metres off a restricted vessel. Divert your course and open the range now.
wf-encounter-zone-warn-2 = {$intruder}, you are inside our warning zone. Turn away immediately or you will be fired upon.
wf-encounter-zone-warn-3 = Vessel {$intruder}, this is your warning. Alter course away from us. Do not approach.
wf-encounter-zone-attack = {$intruder}, you were warned. Weapons free.
wf-encounter-zone-unknown = Unidentified vessel
wf-encounter-zone-identify = Unidentified vessel at {$distance} metres, you are in a restricted zone with no IFF showing. Show your IFF or turn away now. Those were warning shots; the next will not be.
wf-encounter-pirate-zone-warn-1 = {$intruder}, cut your engines and stay right where you are. We are coming alongside.
wf-encounter-pirate-zone-warn-2 = You there, {$intruder}. Heave to and open your hold, and nobody has to get hurt.
wf-encounter-pirate-zone-warn-3 = {$intruder}, you are {$distance} metres into our hunting ground. Kill your drive.
wf-encounter-pirate-zone-attack = Too slow, {$intruder}. Light them up.
wf-encounter-name-pirates = Unidentified raiders {$designation}
wf-encounter-name-boarders = Pirate boarders {$designation}
wf-encounter-name-trader = Wandering trader {$designation}
wf-encounter-name-backup = TSF patrol {$designation} under attack
wf-encounter-announce-backup = Any vessel, any vessel, this is {$name}. We are engaged by pirate raiders and outgunned. Requesting immediate backup. The Federation pays its debts.
wf-encounter-reward-paid = For your help with {$name}, {$amount} { $amount ->
        [one] thaler has
       *[other] thalers have
    } been paid into your account.
wf-encounter-reward-paid-credits = For your help with {$name}, {$amount} { $amount ->
        [one] thaler has
       *[other] thalers have
    } been paid into your account and you have been issued {$credits} military credits.
wf-encounter-reward-thanks-tsf = All vessels that answered our call: the Federation thanks you. Payment has been made.
wf-encounter-reward-thanks-hauler = Whoever that was, thank you. We've sent what we can spare.
wf-encounter-announce-trader = {$name} is passing through the sector, open for business to anyone who comes alongside. Small stock, low prices, no refunds.
wf-encounter-name-ambush = Hauler {$designation} under attack
wf-encounter-sender-traffic = Sector traffic control
wf-encounter-announce-freighter = {$name} is entering the sector at {$origin}, inbound to {$destination} with general cargo.
wf-encounter-announce-ambush = Mayday, mayday, mayday. {$name}. Raiders have jumped us and we can't outrun them. Any vessel in range, we need guns out here. Please hurry.

wf-encounter-preset-quiet = Quiet sector
wf-encounter-preset-standard = Standard sector
wf-encounter-preset-dangerous = Dangerous sector
wf-encounter-vote-title = How dangerous is the sector this round?
wf-encounter-vote-initiator = Sector traffic control
wf-encounter-vote-result = This round: {$preset}.

wf-encounter-resolution-expired = Expired
wf-encounter-resolution-completed = Completed
wf-encounter-resolution-decided = Decided
wf-encounter-resolution-destroyed = Destroyed
wf-encounter-resolution-ended = Ended

# Admin encounter window
wf-encounter-admin-title = Encounters
wf-encounter-admin-scheduler = Scheduler
wf-encounter-admin-enabled = Start encounters automatically
wf-encounter-admin-paused = Paused
wf-encounter-admin-next = Next attempt in {$time}
wf-encounter-admin-next-none = Not running
wf-encounter-admin-interval = Every (seconds)
wf-encounter-admin-to = to
wf-encounter-admin-cap = At most running
wf-encounter-admin-apply = Apply
wf-encounter-admin-schedule-now = Pick one now
wf-encounter-admin-start = Start an encounter
wf-encounter-admin-distance = Metres north of you
wf-encounter-admin-spawn = Start
wf-encounter-admin-prototype-manual = {$id} (manual only)
wf-encounter-admin-search = Search
wf-encounter-admin-keep = Keep until ended
wf-encounter-admin-tier-sector = Sector threat tier
wf-encounter-admin-tier = Tier {$tier}
wf-encounter-admin-search-hint = name, prototype, ship or state
wf-encounter-admin-none = No encounters.
wf-encounter-admin-none-match = Nothing matches the search.
wf-encounter-admin-bad-input = Enter whole seconds, a cap and a distance as plain numbers.
wf-encounter-admin-bad-settings = Intervals must be 30 to 86400 seconds with the longest not below the shortest, and the cap 0 to 10.
wf-encounter-admin-gone = That encounter or ship no longer exists.
wf-encounter-admin-expires = Expires in {$time}
wf-encounter-admin-ship-gone = {$key}: gone, side {$side}
wf-encounter-admin-teleport = Go to
wf-encounter-admin-resolve = Resolve
wf-encounter-admin-end = End and remove
wf-encounter-admin-crew = Crew
wf-encounter-admin-preset = Preset
wf-encounter-admin-budget = Budget in use: {$cost} of {$budget}
wf-encounter-admin-start-round = Place round-start encounters
wf-encounter-admin-reveal = Reveal
wf-encounter-admin-tab-encounters = Running
wf-encounter-admin-tab-start = Start
wf-encounter-admin-tab-scheduler = Scheduler
wf-encounter-admin-show-resolved = Show finished encounters
wf-encounter-admin-pick = Pick an encounter on the left.
wf-encounter-admin-start-search-hint = prototype id
wf-encounter-admin-prototype = Encounter
wf-encounter-admin-tier-label = Threat
wf-encounter-admin-prototype-running = {$label} (running)
wf-encounter-admin-list-entry = {$rank}{$name} · {$ships} {$ships ->
    [one] ship
   *[other] ships
}{$simulated ->
    [0] {""}
   *[other] , {$simulated} simulated
}{$resolved ->
    [1] {" "}(finished)
   *[other] {""}
}
wf-encounter-admin-detail-prototype = Prototype: {$prototype}
wf-encounter-admin-detail-state = State: {$state}
wf-encounter-admin-detail-age = Running {$age}
wf-encounter-admin-detail-pinned = Kept until ended
wf-encounter-admin-detail-threat = Threat {$threat}
wf-encounter-admin-ships = Ships
wf-encounter-admin-ship-line = {$key}: {$name} · {$side} · {$status} · {$crew} crew
wf-encounter-admin-ship-status-gone = gone
wf-encounter-admin-ship-status-building = spawning
wf-encounter-admin-ship-status-simulated = simulated
wf-encounter-admin-ship-status-disabled = out of the fight
wf-encounter-admin-ship-status-real = spawned
wf-encounter-admin-order = Give an order
wf-encounter-admin-order-ship = Ship
wf-encounter-admin-order-do = Order
wf-encounter-admin-order-target = Target
wf-encounter-admin-order-go = Do it
wf-encounter-admin-target = {$name} ({$players}), {$distance}
wf-encounter-admin-target-distance = {$km} km
wf-encounter-admin-target-elsewhere = another map
wf-encounter-admin-target-none = No player ships
wf-encounter-admin-order-extort = Extort
wf-encounter-admin-order-board = Board
wf-encounter-admin-order-attack = Attack
wf-encounter-admin-order-follow = Follow
wf-encounter-admin-order-cometome = Come to me
wf-encounter-admin-order-hold = Hold position
wf-encounter-admin-order-resume = Resume its orders
wf-encounter-admin-order-spawnnow = Spawn now
wf-encounter-admin-order-sendaway = Send away
wf-encounter-admin-order-extort-hint = Goes after the target for its toll: closes in, hails and boards a ship that won't pay.
wf-encounter-admin-order-board-hint = Runs the target down, docks and raids it.
wf-encounter-admin-order-attack-hint = Engages the target, then holds where it ends up.
wf-encounter-admin-order-follow-hint = Keeps station on the target, then holds.
wf-encounter-admin-order-cometome-hint = Flies to where you are and holds there.
wf-encounter-admin-order-hold-hint = Stops where it is.
wf-encounter-admin-order-resume-hint = Takes up the orders it was spawned with.
wf-encounter-admin-order-spawnnow-hint = Builds the simulated ship's hull and crew now, wherever it is.
wf-encounter-admin-order-sendaway-hint = Wraps the encounter up: its ships jump out once nobody is aboard.
wf-encounter-admin-order-no-target = Pick a player ship to aim the order at.
wf-encounter-admin-order-done = {$key}: {$order}.
wf-encounter-admin-order-resolved = That encounter is already over.
wf-encounter-admin-order-building = That ship is spawning; try again in a moment.
wf-encounter-admin-order-failed = The ship could not take that order.
wf-encounter-admin-order-not-extorter = Only an extorter can extort.
wf-encounter-admin-order-boarding = It is boarding a ship already.
wf-encounter-admin-order-not-hunter = Only a boarding ship can board.
wf-encounter-admin-order-not-here = You need to be on the ship's map.
wf-encounter-admin-order-not-simulated = That ship is already spawned.

# wf_encounter command
cmd-wf_encounter-desc = Lists, spawns and ends encounters, and runs or pauses the encounter scheduler.
cmd-wf_encounter-help = Usage: {$command} list | spawn <prototype> [distance] [keep|jump] [tier=N] | end <encounter> | schedule | pause | resume
cmd-wf_encounter-hint-sub = <subcommand>
cmd-wf_encounter-hint-prototype = <encounter prototype>
cmd-wf_encounter-hint-distance = [metres north of you] [keep|jump]
cmd-wf_encounter-hint-keep = [keep until ended | jump out as usual]
cmd-wf_encounter-hint-tier = [tier=0..5, the sector threat's if left out]
cmd-wf_encounter-hint-uid = <encounter entity>
cmd-wf_encounter-list-line = {$uid} {$prototype} "{$name}" {$state}, {$ships} ships, origin {$x}, {$y}
cmd-wf_encounter-list-footer = {$count} encounters.
cmd-wf_encounter-state-active = Active
cmd-wf_encounter-no-player = You need a body or a ghost on a map to spawn an encounter.
cmd-wf_encounter-unknown = {$prototype} is not an encounter.
cmd-wf_encounter-bad-distance = {$arg} is not a distance between 0 and 20000.
cmd-wf_encounter-bad-tier = {$arg} is not a threat tier from tier=0 to tier={$max}.
cmd-wf_encounter-spawned = Started "{$name}" as {$uid}.
cmd-wf_encounter-spawn-failed = {$prototype} could not be started. Station encounters need a station (routes need two) on this map; otherwise the server log says which ship failed.
cmd-wf_encounter-not-found = {$arg} is not an encounter.
cmd-wf_encounter-ended = Ended {$uid} and removed its ships.
cmd-wf_encounter-scheduled = The scheduler started {$uid}.
cmd-wf_encounter-not-scheduled = The scheduler started nothing; the server log says why.
cmd-wf_encounter-paused = Encounter scheduler paused.
cmd-wf_encounter-resumed = Encounter scheduler resumed.
wf-encounter-distress-attacked = Mayday, mayday! This is {$name}. We are under attack at {$x}, {$y}. Any vessel, please, we need help!
wf-encounter-map-object = Contact: {$name}
wf-encounter-map-object-ranked = Contact: {$name} · Threat {$threat}
wf-encounter-threat-rank = Threat {$threat ->
    [0] none
    [1] one chevron
    [2] two chevrons
    [3] three chevrons
    [4] one star
    [5] two stars
   *[other] three stars
}
wf-encounter-threat-retrofit = Retrofitted civilian hulls
wf-encounter-threat-gunship = Pirate gunships
wf-encounter-name-trader-surplus = Surplus dealer {$designation}
wf-encounter-announce-trader-surplus = {$name} is in the sector with decommissioned military stock. Arms, kit and ammunition, priced to move. Come alongside with your hands where we can see them.
wf-encounter-name-trader-curios = Curio dealer {$designation}
wf-encounter-announce-trader-curios = {$name} is passing through with antiques and curiosities from estates across the frontier. One of each, and none of it will be here tomorrow.
wf-encounter-name-trader-medic = Medical supplier {$designation}
wf-encounter-announce-trader-medic = {$name} is in the sector with medical stock: dressings, pens, pills and clinic equipment below station prices while they last.
wf-encounter-name-trader-shipwright = Shipwright {$designation}
wf-encounter-announce-trader-shipwright = {$name} is in the sector with ship fittings off the slip: thrusters, gyros, drives, airlocks and the boards to run them. Come alongside and bring your refit list.
wf-encounter-name-trader-fence = Pirate fence {$designation}
wf-encounter-announce-trader-fence = {$name} is moving goods through the sector, no questions asked. Arms, kit and cheap contraband, and anyone who comes alongside peacefully is welcome to look.

# Larger ambush
wf-encounter-name-ambush-large = Freighter {$designation} under attack
wf-encounter-announce-ambush-large = Mayday, mayday, mayday. {$name}. Two raiders have us boxed in and our guns can't hold them off. Any vessel in range, we need help now. We will pay what we can.

# Stranded freighters. The adrift call is wf-encounter-distress-adrift above.
wf-encounter-name-stranded = Disabled freighter {$designation}
wf-encounter-reward-thanks-stranded = That's thrust under us again. Whoever you are, thank you. We've sent what we can spare.

# False mayday. It is made at once, on the common channel, by the ship that baits the trap. Unlike a real call it gives no
# registered name and no position, says only that the drive is gone, and names a hull class that is not the one on the scanner.
wf-encounter-announce-false-mayday = Mayday, mayday. This is a Brute-class hauler, adrift and losing power. We have no drive left. Come alongside and dock, any vessel. We can't hold out much longer.
# Dweller hulks
wf-encounter-name-hulk = Derelict hull {$designation}
wf-encounter-announce-hulk = Traffic control to all vessels: {$name}, adrift and unregistered, no power and no answer on any channel. Scans show movement aboard. The hull is unclaimed; whoever clears it and files its papers may keep it.
wf-salvage-claim-gone = The ship these papers name is gone.
wf-salvage-claim-taken = That ship already has an owner.
wf-salvage-claim-not-aboard = You have to be aboard the ship to claim it.
wf-salvage-claim-no-card = You need an ID card that holds no ship deed to register the ship to.
wf-salvage-claim-failed = The registry won't take the claim.
wf-salvage-claim-done = {$ship} is registered to you.
# Encounter ship deeds
wf-encounter-deed-name = deed to {$ship}
wf-encounter-deed-gone = The ship this deed names is gone.
wf-encounter-deed-already = {$ship} is already permanent.
wf-encounter-deed-done = {$ship} is now permanent. It will never despawn.
wf-encounter-ambush-sprung-1 = Kind of you to come. There never was a mayday. Cut your engines and open your holds, our friends are already on their way in.
wf-encounter-ambush-sprung-2 = That's close enough. Drive's fine, by the way. Shut down and stand by to be boarded, or the ships behind you open fire.
wf-encounter-ambush-sprung-3 = Thanks for answering. Nobody here needs rescuing but you. Kill your engines, we're taking the ship.
wf-encounter-announce-false-mayday-large = Mayday, mayday. This is an Olympus-class freighter, adrift with the lights failing. We have no drive left. Come alongside and dock, please. We can't hold out much longer.

# Skirmishes
wf-encounter-name-skirmish-patrol = Patrol engaging raiders {$designation}
wf-encounter-announce-skirmish-patrol = Traffic control to all vessels: {$name}. A TSF patrol has run pirate raiders to ground and the two are about to trade fire. Neither is taking calls. Keep clear, unless you mean to take a side.
wf-encounter-name-skirmish-bands = Rival raiders {$designation}
wf-encounter-announce-skirmish-bands = Traffic control to all vessels: {$name}. Two raider bands have found each other in open space and are settling an old score. Neither is taking calls. Stay clear of both, or pick one.
wf-encounter-name-skirmish-corsairs = Patrol engaging corsairs {$designation}
wf-encounter-announce-skirmish-corsairs = Traffic control to all vessels: {$name}. A TSF patrol has cornered a RedSail corsair crew in open space. Expect heavy fire. Anything that gets between them is on its own.
wf-encounter-name-skirmish-hostile = Running battle {$designation}
wf-encounter-announce-skirmish-hostile = Traffic control to all vessels: {$name}. Two armed parties are fighting it out in open space, and both are firing on anything that comes near. Do not approach either one.
wf-encounter-reward-thanks-skirmish-tsf = TSF patrol to the vessels that stood with us: your help is noted, and it has been paid for.
wf-encounter-reward-thanks-skirmish-pirates = You fight well for a stranger. Here is a cut of what we took off them.
wf-encounter-reward-thanks-skirmish-corsairs = RedSail remembers a favour. Take your share and fly on.

# Black market transport. Only a rumour goes out, once someone has fought it.
wf-encounter-name-blackmarket = Unregistered transport {$designation}
wf-encounter-announce-blackmarket = Sector traffic control has unconfirmed reports of weapons fire around {$name}, a vessel on no manifest. Whatever it carries, it isn't licensed. Keep your distance.
wf-encounter-blackmarket-zone-warn-1 = {$intruder}, you are {$distance} metres off a private vessel. Turn around. We won't ask twice.
wf-encounter-blackmarket-zone-warn-2 = {$intruder}, this is private cargo, and whatever you think you saw, you didn't. Open the range.
wf-encounter-blackmarket-zone-warn-3 = You are drifting into something that is none of your business, {$intruder}. Fly on.
wf-encounter-blackmarket-zone-attack = {$intruder}, that was your last chance. Open fire.

# Phaethon Dynasty navy. Its zone lines name nobody, because the hostile skirmishes use them on every ship.
wf-encounter-name-pdv-patrol = Dynasty patrol {$designation}
wf-encounter-name-pdv-transport = Dynasty supply convoy {$designation}
wf-encounter-announce-pdv-transport = All vessels, this is {$name}, a Phaethon Dynasty supply convoy under Vanguard escort, inbound to {$destination}. Federation vessels, stay outside one kilometre. You will not be warned twice.
wf-encounter-pdv-zone-warn-1 = {$intruder}, you are {$distance} metres off a Vanguard vessel. Open the range now, in the Sultan's name.
wf-encounter-pdv-zone-warn-2 = {$intruder}, you are inside the Vanguard's warning zone. Turn away at once or be fired upon.
wf-encounter-pdv-zone-warn-3 = Vessel {$intruder}, this is the Vanguard. Alter course and stay clear of us. This is your last courtesy.
wf-encounter-pdv-zone-attack = {$intruder}, you were warned. The Sultan's guns are loose.
wf-encounter-name-pdv-backup = Dynasty patrol {$designation} under attack
wf-encounter-announce-pdv-backup = Any vessel, any vessel, this is {$name}. Federation fighters have jumped us and we are outgunned. Requesting immediate backup. The Vanguard repays its debts, and with interest.
wf-encounter-reward-thanks-pdv = All vessels that answered our call: the Vanguard thanks you, and the Sultan remembers. Payment has been made.
wf-encounter-name-skirmish-navies = Rival patrols {$designation}
wf-encounter-announce-skirmish-navies = Traffic control to all vessels: {$name}. A TSF patrol and a Dynasty patrol have met in open space and are about to trade fire. Neither is taking calls. Keep clear, unless you mean to take a side.
wf-encounter-name-skirmish-pdv-corsairs = Dynasty patrol engaging corsairs {$designation}
wf-encounter-announce-skirmish-pdv-corsairs = Traffic control to all vessels: {$name}. A Dynasty patrol has caught a RedSail corsair crew in open space and means to finish it. Expect heavy fire. Anything that gets between them is on its own.
wf-encounter-reward-thanks-skirmish-pdv = Vanguard patrol to the vessels that stood with us: your help is noted, and the Sultan's purse has paid for it.

# Frigates under attack
wf-encounter-name-frigate = TSF frigate {$designation} under attack
wf-encounter-announce-frigate = Mayday, mayday, all vessels, this is {$name}. A pirate pack has us surrounded, raiders and fighters on every quarter, and our guns can't hold them all. Any armed vessel, engage the pirates. The Federation will pay, and pay well.
wf-encounter-name-pdv-frigate = Dynasty frigate {$designation} under attack
wf-encounter-announce-pdv-frigate = All vessels, all vessels, this is {$name}. A Federation strike group is on us from every side and we are taking hits faster than we can answer them. Any armed vessel, engage the Federation fighters. The Vanguard repays its debts, and with interest.

# Heavier and stranger patrols
wf-encounter-name-patrol-heavy = TSF heavy patrol {$designation}
wf-encounter-announce-patrol-heavy = Traffic control to all vessels: {$name}. A Federation gunboat and escort are holding station off {$destination} for the next while. Dynasty traffic is advised to keep its distance.
wf-encounter-name-pdv-patrol-heavy = Dynasty heavy patrol {$designation}
wf-encounter-announce-pdv-patrol-heavy = Traffic control to all vessels: {$name}. A Vanguard gunboat and escort are holding station off {$destination} for the next while. Federation traffic is advised to keep its distance.
wf-encounter-name-ussp-patrol = Union remnant {$designation}
wf-encounter-announce-ussp-patrol = Traffic control to all vessels: {$name}. Union warships are holding a position in open space and answering nobody's hails. All traffic is advised to give them a wide berth.
wf-encounter-ussp-zone-warn-1 = {$intruder}, you are {$distance} metres inside Union space. There is no Union space on your charts. There is on ours. Turn around.
wf-encounter-ussp-zone-warn-2 = Vessel {$intruder}, this is a warship of the Union. Your company means nothing here. Alter course away from us.
wf-encounter-ussp-zone-warn-3 = {$intruder}, we have not surrendered and we are not negotiating. Open the range.
wf-encounter-ussp-zone-attack = {$intruder}, for the Union. Fire.

# Capital fleet battle
wf-encounter-name-fleet-battle = Capital fleet action {$designation}
wf-encounter-announce-fleet-battle = GAMMA ALERT. Traffic control to all vessels: {$name}. A Federation capital ship and its frigate screen have met a Dynasty battle group in open space and are engaging. All civilian traffic is to stay well clear. Both navies are calling for any armed vessel that will fight for them.

# Capitals under attack
wf-encounter-name-capital = TSF capital ship {$designation} under attack
wf-encounter-announce-capital = Mayday, mayday, all vessels, this is {$name}. A pirate flotilla has us surrounded, raiders and fighters from every quarter, and they are boarding our wounded sections. Any armed vessel in the sector, engage the pirates. The Federation will remember who came.
wf-encounter-name-pdv-capital = Dynasty capital ship {$designation} under attack
wf-encounter-announce-pdv-capital = All vessels, all vessels, this is {$name}. A Federation strike wing has thrown itself at us and our screens are failing. Any armed vessel, engage the Federation. The Sultan does not forget a debt.

# Supercapitals under attack
wf-encounter-name-supercapital = TSF supercapital {$designation} under attack
wf-encounter-announce-supercapital = Mayday, mayday, all vessels, this is {$name}. The whole pirate flotilla has fallen on the Chengdu: heavies, raiders and fighters from every quarter, and our screens are going. This is the Federation's flagship. Every armed vessel in the sector, engage the pirates. The Federation will not forget who came.
wf-encounter-name-pdv-supercapital = Dynasty supercapital {$designation} under attack
wf-encounter-announce-pdv-supercapital = All vessels, all vessels, this is {$name}. A Federation strike force has fallen on the Jupiter with everything it has and our screens are failing. This is the Vanguard's flagship. Every armed vessel in the sector, engage the Federation. The Sultan does not forget a debt, and he will not forget this.

# Supercapital fleet battle
wf-encounter-name-fleet-battle-large = Grand fleet action {$designation}
wf-encounter-announce-fleet-battle-large = EPSILON ALERT. Traffic control to all vessels: {$name}. The Federation and the Dynasty have committed their supercapitals, with their capital ships and fighter wings. A full fleet action is under way in open space. All civilian traffic is to clear the sector lanes at once. Both navies are calling for every armed vessel that will fight for them.
wf-encounter-name-trader-exotic = Exotic dealers {$designation}
wf-encounter-announce-trader-exotic = {$name} is in the sector under Blackhawk escort with ship guns, hardpoints and systems that don't reach any catalogue. Serious buyers may come alongside. Anyone else is advised that the escort is not for show.

# Frigate, capital and supercapital patrols
wf-encounter-name-frigate-patrol = TSF frigate patrol {$designation}
wf-encounter-announce-frigate-patrol = Traffic control to all vessels: {$name}. A Federation frigate and fighter wing are holding station off {$destination} for the next while. Dynasty traffic is advised to keep well clear.
wf-encounter-name-pdv-frigate-patrol = Dynasty frigate patrol {$designation}
wf-encounter-announce-pdv-frigate-patrol = Traffic control to all vessels: {$name}. A Vanguard frigate and fighter wing are holding station off {$destination} for the next while. Federation traffic is advised to keep well clear.
wf-encounter-name-capital-patrol = TSF capital patrol {$designation}
wf-encounter-announce-capital-patrol = Traffic control to all vessels: {$name}. The Federation capital ship Flyssa and her fighter wing are holding station off {$destination}. Dynasty traffic is advised to keep well clear.
wf-encounter-name-pdv-capital-patrol = Dynasty capital patrol {$designation}
wf-encounter-announce-pdv-capital-patrol = Traffic control to all vessels: {$name}. The Vanguard capital ship Saturn and her fighter wing are holding station off {$destination}. Federation traffic is advised to keep well clear.
wf-encounter-name-supercapital-patrol = TSF supercapital patrol {$designation}
wf-encounter-announce-supercapital-patrol = Traffic control to all vessels: {$name}. The Federation supercapital Chengdu and her fighter wing are holding station off {$destination}. Dynasty traffic is advised to keep well clear.
wf-encounter-name-pdv-supercapital-patrol = Dynasty supercapital patrol {$designation}
wf-encounter-announce-pdv-supercapital-patrol = Traffic control to all vessels: {$name}. The Vanguard supercapital Jupiter and her fighter wing are holding station off {$destination}. Federation traffic is advised to keep well clear.

# Traders answering a hail; the caller can ask them to hold position so they can dock.
wf-encounter-trader-hail-greet = Trader here, under way. What can we do for you?
wf-encounter-trader-hail-holding = Trader here. We're holding position, come alongside when you're ready.
wf-encounter-trader-hail-stop = Hold position, we're coming to dock.
wf-encounter-trader-hail-leave = Never mind.
wf-encounter-trader-hail-stopping = Understood. Holding position for {$minutes} minutes. Dock when ready.
wf-encounter-trader-hail-cannot = Sorry, we can't stop here.
wf-encounter-trader-hail-farewell = Safe travels.
