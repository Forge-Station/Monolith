# Ballistic wounds and pulling objects back out of them (W1).

wolfmed-wound-name-graze = graze
wolfmed-wound-name-gunshot = gunshot wound
wolfmed-wound-name-lodged-round = lodged round
wolfmed-wound-name-shrapnel = shrapnel wound

wolfmed-embedded-removal-start = { CAPITALIZE(THE($user)) } starts digging something out of { THE($target) }.
wolfmed-embedded-removal-start-self = { CAPITALIZE(THE($user)) } starts digging something out of { POSS-ADJ($user) } own body.
wolfmed-embedded-removal-success = Something comes free.
wolfmed-embedded-removal-partial = Something comes free. { $count } left in there.

health-analyzer-wound-embedded-short = embedded objects: { $count }

# Slash and bite wounds (W2).

wolfmed-wound-name-arterial-bleed = arterial bleed
wolfmed-wound-name-tendon-cut = severed tendon
wolfmed-wound-name-avulsion = avulsion

wolfmed-tourniquet-nowhere-to-tie = There is nothing here to tie a tourniquet around.

# Blunt trauma wounds and setting a dislocated joint (W3).

wolfmed-wound-name-crush-injury = crush injury
wolfmed-wound-name-concussion = concussion
wolfmed-wound-name-dislocation = dislocated joint
wolfmed-wound-name-organ-contusion = bruised organ

wolfmed-relocate-verb = Relocate joint
wolfmed-relocate-start = { CAPITALIZE(THE($user)) } starts forcing { THE($target) }'s joint back into place.
wolfmed-relocate-start-self = { CAPITALIZE(THE($user)) } grits { POSS-ADJ($user) } teeth and grabs { POSS-ADJ($user) } own joint.
wolfmed-relocate-success = The joint goes back in with a sickening pop.

# Burn wounds, cauterisation and washing off caustic (W4).

wolfmed-wound-name-charring = charred tissue
wolfmed-wound-name-frostbite = frostbite
wolfmed-wound-name-chemical-burn = chemical burn
wolfmed-wound-name-internal-burn = internal burns

wolfmed-cauterize-verb = Cauterise wound
wolfmed-cauterize-start = { CAPITALIZE(THE($user)) } presses { THE($tool) } against { THE($target) }'s wound.
wolfmed-cauterize-start-self = { CAPITALIZE(THE($user)) } presses { THE($tool) } against { POSS-ADJ($user) } own wound.
wolfmed-cauterize-success = The wound seals with a hiss and the smell of cooking.
wolfmed-cauterize-nothing = There is nothing bleeding there to seal.

wolfmed-chemical-burn-washed = The residue rinses away.

reagent-effect-guidebook-wash-chemical-burns =
    { $chance ->
        [1] Washes
       *[other] chance to wash
    } corrosive residue off burned tissue

# Infection, sepsis and necrosis (W5).

wolfmed-wound-name-necrosis = necrotic tissue
wolfmed-wound-stage-necrotic = dead

wolfmed-wound-cleaned = The antiseptic stings.
wolfmed-necrosis-warning = Something in your limb has gone cold and numb.
wolfmed-necrosis-dead = The flesh there has died.
wolfmed-tourniquet-loosen-verb = Loosen tourniquet
wolfmed-tourniquet-loosened = The tourniquet comes off and the bleeding starts again.
wolfmed-tourniquet-loosened-safe = The tourniquet comes off. Nothing under it is bleeding any more.
wolfmed-tourniquet-loosen-part-holding = { CAPITALIZE($part) } (still holding a bleed)
wolfmed-tourniquet-loosen-part-safe = { CAPITALIZE($part) } (bleeding stopped, safe to remove)
wolfmed-tourniquet-loosen-holding-tip = The wound under this tourniquet will bleed again the moment it comes off. Close it first.
wolfmed-tourniquet-loosen-safe-tip = Nothing under this tourniquet is bleeding. Take it off before the limb starts to die.
wolfmed-tourniquet-slipped = The makeshift tourniquet on the { $part } is knocked loose!

health-analyzer-wound-infection-local = infection: local
health-analyzer-wound-infection-spreading = infection: spreading
health-analyzer-wound-infection-septic = infection: septic
health-analyzer-wound-necrotic-short = NECROTIC
health-analyzer-wound-necrosis-risk-short = circulation failing
health-analyzer-wound-sepsis = [color=#d63c2c]SEPSIS[/color] - systemic infection at { $percent }%

# INFECTION: AlertControl passes the alert's severity to both lines; 1 is septic shock.
alerts-wolfmed-sepsis-name = { $severity ->
        [1] Septic shock
       *[other] Sepsis
    }
alerts-wolfmed-sepsis-desc = { $severity ->
        [1] Septic shock. The infection is shutting your body down; without antibiotics now it eats your organs.
       *[other] The infection is in your blood. You need antibiotics, and you needed them a while ago.
    }

# Playtest 5: septic shock as a consciousness cause (Resources/Prototypes/_WF/Wolfmed/Consciousness/remaining_causes.yml).
wolfmed-cause-septicshock = septic shock
wolfmed-cause-septicshock-title-out = Unconscious: septic shock
wolfmed-cause-septicshock-symptom = Grey, clammy and burning up. The infection is in your blood.
wolfmed-cause-septicshock-help-out = You wake once antibiotics (spaceacillin) pull the sepsis back. Left alone it eats your organs.
wolfmed-cause-septicshock-help-out-blocked = Antibiotics, and the rest treated, wake you.
wolfmed-cause-septicshock-out = A wave of cold sweat, and the room goes.
wolfmed-cause-septicshock-wake = You come round shivering and soaked.
wolfmed-vitals-cause-septicshock = septic shock
alerts-wolfmed-out-septicshock-name = Unconscious: septic shock
alerts-wolfmed-out-septicshock-desc = The infection is in your blood. You wake once antibiotics pull it back; left alone it eats your organs.

reagent-name-spaceacillin = spaceacillin
reagent-desc-spaceacillin = A broad-spectrum antibiotic. Clears an infected wound and pulls a septic patient back; a heavy dose is poisonous in its own right.

reagent-effect-guidebook-clean-wounds =
    { $chance ->
        [1] Cleans
       *[other] chance to clean
    } open wounds, preventing infection

reagent-effect-guidebook-treat-infection =
    { $chance ->
        [1] Clears
       *[other] chance to clear
    } infection from wounds and from the bloodstream

# Mechanical wounds (W6).

wolfmed-wound-name-dent = dent
wolfmed-wound-name-breach = chassis breach
wolfmed-wound-name-short-circuit = short circuit
wolfmed-wound-name-servo-damage = servo damage
wolfmed-wound-name-overheating = overheating

wolfmed-wound-stage-warm = warm
wolfmed-wound-stage-hot = hot
wolfmed-wound-stage-overheated = overheated

wolfmed-overheating-doused = Steam hisses off the casing.

health-analyzer-wound-overheating-short = running hot

reagent-effect-guidebook-cool-overheating =
    { $chance ->
        [1] Cools
       *[other] chance to cool
    } an overheated chassis

# Splinting a fracture (V5).

wolfmed-splint-start = { CAPITALIZE(THE($user)) } straps a splint around { THE($target) }'s limb.
wolfmed-splint-start-self = { CAPITALIZE(THE($user)) } starts strapping a splint around { POSS-ADJ($user) } own limb.
wolfmed-splint-success = The limb is braced. The bone stops shifting.
wolfmed-splint-no-part = Select the limb to splint on the targeting doll first.
wolfmed-splint-wrong-part = A splint cannot brace that part.
wolfmed-splint-no-fracture = There is no broken bone there.
wolfmed-splint-too-slight = The crack is too slight for a splint to hold anything.
wolfmed-splint-already-treated = That bone is already set.

# Analyzer wounds tab: the categorised card readout (UI2).

wolfmed-wound-category-cut = cuts
wolfmed-wound-category-puncture = punctures
wolfmed-wound-category-ballistic = ballistic trauma
wolfmed-wound-category-blunt = blunt trauma
wolfmed-wound-category-burn = burns
wolfmed-wound-category-internal = internal injury
wolfmed-wound-category-infection = infection
wolfmed-wound-category-mechanical = chassis damage
wolfmed-wound-category-other = other

health-analyzer-wound-category-chip = { $category }: { $count }
health-analyzer-wound-no-findings = No wounds detected.
health-analyzer-wound-no-findings-filtered = No { $category } on this patient.

# Evisceration (EVISC): a torso opened by a hit its cap could not absorb.

wolfmed-wound-name-evisceration = evisceration
wolfmed-wound-name-chassis-breach = torn chassis

wolfmed-evisceration-popup = { CAPITALIZE(THE($target)) }'s abdomen is torn open and the contents come out!
wolfmed-chassis-breach-popup = { CAPITALIZE(THE($target)) }'s torso plating tears open and its internals spill out!

# BRAIN: the body-level vitals. Everything else on this tab is survivable; these two are not.
health-analyzer-wound-cardiac-arrest = [color=#d63c2c]CARDIAC ARREST[/color] - pulse: none
health-analyzer-wound-cardiac-arrest-timed = [color=#d63c2c]CARDIAC ARREST[/color] - pulse: none, est. brain death in { $minutes }:{ $seconds }
health-analyzer-wound-brain-dead = [color=#d63c2c]BRAIN DESTROYED[/color] - no brain activity, no pulse. A shock brings them back with major brain damage; repair the brain after.
health-analyzer-wound-brain-activity = brain activity { $activity }%, oxygenation { $oxygen }%
# Playtest 5: appended to the brain line while a repaired brain carries its trauma.
health-analyzer-wound-brain-trauma = Brain trauma from the repair, about { $minutes } min left: blurred sight and a stammer. Nothing shortens it.
health-analyzer-wound-brain-damage = brain damage, { $activity }% tissue left, oxygenation { $oxygen }%. Brain repair surgery once the patient is breathing.
health-analyzer-wound-brain-damage-critical = CRITICAL brain damage, { $activity }% tissue left, oxygenation { $oxygen }%. Brain repair surgery now.
health-analyzer-wound-brain-activity-core = core integrity { $activity }%
health-analyzer-wound-brain-damage-core = core damage, { $activity }% integrity left. Core repair surgery.
health-analyzer-wound-brain-damage-critical-core = CRITICAL core damage, { $activity }% integrity left. Core repair surgery now, then the restart button.
health-analyzer-wound-shutdown = [color=#d63c2c]SHUTDOWN[/color] - no power or no pump

# Overheating on a wound host burns the chassis instead of killing it outright.
wolfmed-overheat-popup = {$name}'s circuits are overheating!

# Playtest 4, SEPSIS: the sepsis banner once it damages the torso organs.
health-analyzer-wound-sepsis-organs = [color=#d63c2c]SEPSIS[/color] - systemic infection at { $percent }%, damaging the organs

# INFECTION: septic shock, the late stage of sepsis past wolfmed.septic_shock_at.
health-analyzer-wound-septic-shock = [color=#d63c2c]SEPTIC SHOCK[/color] - systemic infection at { $percent }%
health-analyzer-wound-septic-shock-organs = [color=#d63c2c]SEPTIC SHOCK[/color] - systemic infection at { $percent }%, damaging the organs

# INFECTION: a part's own infection, which travels towards the torso with no wound on the part. The chip's label, then
# its headline.
health-analyzer-part-infection-local-short = tissue: local
health-analyzer-part-infection-spreading-short = tissue: spreading
health-analyzer-part-infection-septic-short = tissue: septic
health-analyzer-part-infection-local = tissue infection: local
health-analyzer-part-infection-spreading = tissue infection: spreading
health-analyzer-part-infection-septic = tissue infection: septic
