# M2: arrest, revival and medic information (plan §3.4, §5.2-5.5, §7.2).

# Sedation (plan §3.4, OD14).
wolfmed-sedation-warn-1 = You feel heavy and drowsy.
wolfmed-sedation-warn-2 = Your breathing slows. Another dose could stop it.
wolfmed-sedation-warn-3 = You can barely stay awake.

reagent-name-wolfmed-naloxone = naloxone
reagent-desc-wolfmed-naloxone = An opioid antagonist. Reverses a painkiller overdose and gets the patient breathing. Wears off before the painkiller does.
reagent-effect-guidebook-wolfmed-reverse-sedation =
    { $chance ->
        [1] Takes
       *[other] chance to take
    } { $amount }% of sedation off per unit, and holds it off while it lasts.

# The restart button on a chassis (plan §7.2, OD10). Read what the machine needs, never a damage total.
wolfmed-restart-no-head = { CAPITALIZE(THE($target)) } buzzes: no head. Restart refused.
wolfmed-restart-no-core = { CAPITALIZE(THE($target)) } buzzes: no positronic core. Restart refused.
wolfmed-restart-core-destroyed = { CAPITALIZE(THE($target)) } buzzes: core destroyed. Core repair surgery first.
wolfmed-restart-no-pump = { CAPITALIZE(THE($target)) } buzzes: no coolant pump. Restart refused.
wolfmed-restart-no-power = { CAPITALIZE(THE($target)) } buzzes: no power. Charge or replace the cell.
health-analyzer-wound-brain-dead-core = [color=#d63c2c]CORE FAILURE[/color] - core destroyed. Core repair surgery, then the restart button.

# "Can't breathe: {source}" in place of the stock low-oxygen alert on a wound host (plan §3.3).
alerts-wolfmed-cant-breathe-air-name = Can't breathe: no air
alerts-wolfmed-cant-breathe-air-desc = Nothing breathable reaches your lungs. Find air, or put on internals.
alerts-wolfmed-cant-breathe-lungs-name = Can't breathe: no lungs
alerts-wolfmed-cant-breathe-lungs-desc = You have no working lungs. You need new ones; CPR buys time.

# The explanation card on the unconscious screen (plan §5.2, §2.3). No numbers.
wolfmed-card-blockers = Also holding you down: { $blockers }.
wolfmed-card-cpr = Someone is giving you CPR.
wolfmed-card-examined = A medic is examining you.
wolfmed-card-bar = Time your brain has
# Playtest 3: the countdown row under the title, and the breath and the blood as the patient feels them. The
# countdown's seconds are the card's only number.
wolfmed-card-countdown = COMING ROUND IN { $seconds } S
wolfmed-card-countdown-none = COMING ROUND: ∞
wolfmed-card-breathing-depressed = Breathing slowed
wolfmed-card-breathing-gasping = Gasping for air
wolfmed-card-breathing-none = Not breathing
wolfmed-card-breathing-laboured = Breathing laboured
wolfmed-card-blood-low = Blood low
wolfmed-card-blood-weak = Blood very low
wolfmed-card-blood-critical = Blood critically low

# The crawling stage (plan §5.3, OD20).
wolfmed-play-dead-start = You lie still and play dead.
wolfmed-play-dead-stop = You stop playing dead.
wolfmed-check-yourself-popup = You check yourself over.

# Wait as a ghost (OD8 (b), plan §5.4). The dialog text is the plan's, exactly.
wolfmed-dormant-dialog-title = Wait as a ghost?
wolfmed-dormant-dialog-text = Your body is stable for now ({ $cause }). It stays where it is. If someone wakes or repairs you, you will be offered the chance to return. If your body starts getting worse, you will be told.
wolfmed-dormant-dialog-accept = Wait as a ghost
wolfmed-dormant-dialog-deny = Stay
wolfmed-dormant-worse = Your body is getting worse: { $route }. You can return to it now.
wolfmed-dormant-route-bleeding = bleeding
wolfmed-dormant-route-internalbleeding = internal bleeding
wolfmed-dormant-route-burnfluid = burns weeping fluid
wolfmed-dormant-route-arrest = your heart has stopped
wolfmed-dormant-route-airway = you are suffocating
wolfmed-dormant-route-lungs = your lungs are failing
wolfmed-dormant-route-circulation = too little blood for your brain
wolfmed-dormant-route-sepsis = sepsis
wolfmed-dormant-route-sedation = an overdose is slowing your breathing
wolfmed-dormant-route-tissueloss = your brain is starving of oxygen

## Playtest 4, SEPSIS

wolfmed-dormant-route-sepsisorgans = sepsis is damaging your organs
