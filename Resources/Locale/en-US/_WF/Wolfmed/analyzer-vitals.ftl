# M1a: the analyzer's vitals block (plan §5.5). State and cause, breathing, circulation, and the defib verdict.
# A shock succeeds on a roll, so the verdict never says it will work. [OD1 wording] on the dead line.

wolfmed-vitals-state-up = CONSCIOUS
wolfmed-vitals-state-up-mechanical = ONLINE
wolfmed-vitals-state-downed = DOWNED: { $cause }
wolfmed-vitals-state-faint = FAINTED: { $cause }
wolfmed-vitals-state-faint-timed = FAINTED: { $cause }, { $seconds } s
wolfmed-vitals-state-unconscious = UNCONSCIOUS: { $cause }
wolfmed-vitals-state-arrest = CARDIAC ARREST: { $cause }
wolfmed-vitals-state-shutdown = SHUTDOWN: { $cause }
wolfmed-vitals-state-dead = DEAD
wolfmed-vitals-state-dead-brain = DEAD: catastrophic brain injury
wolfmed-vitals-state-dead-mechanical = CORE FAILURE
wolfmed-vitals-blockers = { $state } (also: { $blockers })

wolfmed-vitals-cause-none = unknown cause
wolfmed-vitals-cause-pain = pain
wolfmed-vitals-cause-pain-mechanical = frame damage
wolfmed-vitals-cause-painfaint = pain
wolfmed-vitals-cause-blood = blood loss
wolfmed-vitals-cause-oil = hydraulic pressure low
wolfmed-vitals-cause-hypoxia = no oxygen
wolfmed-vitals-cause-sedation = overdose
wolfmed-vitals-cause-legs = legs
wolfmed-vitals-cause-crash = stim crash
wolfmed-vitals-cause-arrest = unknown cause
wolfmed-vitals-cause-shutdown = unknown cause
wolfmed-vitals-cause-other = other
wolfmed-vitals-cause-with-source = { $cause } ({ $source })

wolfmed-vitals-source-none = unknown cause
wolfmed-vitals-source-airway = no air
wolfmed-vitals-source-lungs = lungs failing
wolfmed-vitals-source-circulation = poor circulation
wolfmed-vitals-source-sepsis = sepsis
wolfmed-vitals-source-sedation = breathing depressed
wolfmed-vitals-source-arrestblood = blood
wolfmed-vitals-source-arrestoxygen = oxygen
wolfmed-vitals-source-arrestheart = heart
wolfmed-vitals-source-arrestsepsis = sepsis
wolfmed-vitals-source-arrestshock = shock
wolfmed-vitals-source-arrestother = unknown cause
wolfmed-vitals-source-power = no power
wolfmed-vitals-source-pump = coolant pump offline

# Playtest 3: breathing, the pulse and the blood are items on the one vitals line, shown only when not normal.
wolfmed-vitals-breathing-depressed = Breathing depressed, sedation { $percent }%
wolfmed-vitals-breathing-gasping = Not breathing: no air, gasping
wolfmed-vitals-breathing-none-none = Not breathing
wolfmed-vitals-breathing-none-noair = Not breathing: no air
wolfmed-vitals-breathing-none-lungs = Not breathing: no working lungs
wolfmed-vitals-breathing-none-sedation = Not breathing: sedation
wolfmed-vitals-breathing-none-arrest = Not breathing
wolfmed-vitals-breathing-none-nobrain = Not breathing: no brain
wolfmed-vitals-breathing-none-dead = Not breathing
wolfmed-vitals-cooling-offline = Cooling offline

wolfmed-vitals-pulse-low = Pale
wolfmed-vitals-pulse-weak = Pulse weak, rapid
wolfmed-vitals-pulse-critical = Pulse barely palpable
wolfmed-vitals-pulse-none = No pulse
# The blood's direction, after the percentage. Steady shows nothing.
wolfmed-vitals-trend-steady = { "" }
wolfmed-vitals-trend-rising = ↑
wolfmed-vitals-trend-falling = ↓
wolfmed-vitals-trend-fallingfast = ↓↓

wolfmed-vitals-verdict-indicated = Defib: shock indicated
wolfmed-vitals-verdict-pulsepresent = Defib: refused: pulse present
wolfmed-vitals-verdict-noheart = Defib: refused: no heart, transplant first
wolfmed-vitals-verdict-noblood = Defib: refused: blood { $percent }%, transfuse ≈ { $units } u first (≈ { $safe } u to { $line }%)
wolfmed-vitals-verdict-braindead = Defib: refused: core destroyed, core repair surgery first
wolfmed-vitals-verdict-nobrain = Defib: refused: no brain
wolfmed-vitals-verdict-rotten = Defib: refused: body decayed
wolfmed-vitals-verdict-unrevivable = Defib: refused: { $reason }

# M2 (plan §5.5): the last arrest; a dead chassis's restart button.
wolfmed-vitals-yes = yes
wolfmed-vitals-no = no
wolfmed-vitals-restart = After a restart: arrest cause { $cause }. Still present: { $present }.
wolfmed-vitals-restart-transfuse = After a restart: arrest cause { $cause }. Still present: yes. Transfuse ≈ { $units } u within { $seconds } s to stop the heart stopping again; ≈ { $safe } u to { $line }% to stop the brain injury.
wolfmed-vitals-restart-transfuse-late = After a restart: arrest cause { $cause }. Still present: yes. Transfuse ≈ { $safe } u to { $line }% to stop the brain injury.
wolfmed-vitals-restart-verdict-ready = Restart: ready
wolfmed-vitals-restart-verdict-nohead = Restart: refused: no head
wolfmed-vitals-restart-verdict-nocore = Restart: refused: no positronic core
wolfmed-vitals-restart-verdict-coredestroyed = Restart: refused: core destroyed, core repair surgery first
wolfmed-vitals-restart-verdict-nopump = Restart: refused: no coolant pump
wolfmed-vitals-restart-verdict-nopower = Restart: refused: no power
wolfmed-vitals-restart-verdict-refused = Restart: refused

## Playtest 3: the compact block. Line 2 is every abnormal vital joined by the separator; line 3 is what to do first.

wolfmed-vitals-normal = Vitals normal
wolfmed-vitals-separator = { " · " }
wolfmed-vitals-item-blood = Blood { $percent }% { $trend }
wolfmed-vitals-item-oil = Fluid { $percent }% { $trend }
wolfmed-vitals-item-burn-fluid = Burn fluid loss { $rate } u/s
wolfmed-vitals-item-toxins = Toxins { $load }, { $band }
wolfmed-vitals-item-liver-missing = No liver

wolfmed-vitals-do-first = Do first: { $aids }
wolfmed-vitals-do-first-none = Do first: nothing; stable
# Each running route's first aid, a few words, in the routes' order.
wolfmed-vitals-aid-transfuse = transfuse ≈ { $units } u
wolfmed-vitals-aid-refill = refill hydraulic fluid ≈ { $units } u
wolfmed-vitals-aid-bleeding = gauze or tourniquet the bleeding
wolfmed-vitals-aid-bleeding-mechanical = weld the fluid leak
wolfmed-vitals-aid-internalbleeding = surgery for the internal bleeding
wolfmed-vitals-aid-burnfluid = dress the burns and give fluids
wolfmed-vitals-aid-arrest = CPR, then the defibrillator
wolfmed-vitals-aid-airway = air or internals
wolfmed-vitals-aid-lungs = lung surgery
wolfmed-vitals-aid-circulation = transfuse
wolfmed-vitals-aid-sepsis = antibiotics
wolfmed-vitals-aid-sedation = naloxone
wolfmed-vitals-aid-tissueloss = oxygen, now
wolfmed-vitals-aid-coreheat = put the fire out, cool the chassis
wolfmed-vitals-aid-toxin = antitoxin
wolfmed-vitals-aid-heatstroke = cool them, now
wolfmed-vitals-aid-marrow = anti-radiation drugs and blood
wolfmed-vitals-aid-hypothermia = warm them

## Playtest 4, SEPSIS: sepsis past the organ-damage line. Replaces the plain sepsis aid while it runs.

wolfmed-vitals-aid-sepsisorgans = antibiotics now, sepsis is damaging the organs

## INFECTION: septic shock, sepsis past wolfmed.septic_shock_at. Replaces the two sepsis aids while it holds.

wolfmed-vitals-aid-sepsis-shock = antibiotics now, septic shock
wolfmed-vitals-aid-sepsisorgans-shock = antibiotics now, septic shock is damaging the organs
