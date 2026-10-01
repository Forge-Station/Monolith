# LOOK: the health examine read as a visual inspection. Every string here is something an examiner could
# see on the body in front of them; numbers, rates and severities stay on the health analyzer.
#
# LOOK2: the examine draws a row per damaged part, so every finding has two strings: the sentence below,
# which is its tooltip and its plain-text line, and a `-short` label of two to four words for the row
# itself. The wound data names the short one, the findings the system builds itself find it by suffix.

wolfmed-look-title-self = [font size=11][color=DarkGray]You look yourself over.[/color][/font]
wolfmed-look-title-other = [font size=11][color=DarkGray]You look { OBJECT($target) } over.[/color][/font]
wolfmed-look-part-self = [bold]Your { $part }[/bold]: { $findings }.
wolfmed-look-part-other = [bold]{ CAPITALIZE(POSS-ADJ($target)) } { $part }[/bold]: { $findings }.
wolfmed-look-none-self = [color=DarkGray]You see nothing wrong with yourself.[/color]
wolfmed-look-none-other = [color=DarkGray]You see no injuries on { OBJECT($target) }.[/color]
wolfmed-look-covered = [color=DarkGray]The rest is covered by clothing.[/color]
wolfmed-look-hidden = [color=DarkGray]Nothing shows through { POSS-ADJ($target) } clothing.[/color]
wolfmed-look-distant = [color=DarkGray]You are too far away to make out more.[/color]
wolfmed-look-sepsis-self = [color=crimson]You are flushed and sweating.[/color]
wolfmed-look-sepsis-other = [color=crimson]{ CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } flushed and sweating.[/color]
# INFECTION: septic shock, sepsis past wolfmed.septic_shock_at. Replaces the flush.
wolfmed-look-septic-shock-self = [color=crimson]You are grey and clammy, and your skin is blotched.[/color]
wolfmed-look-septic-shock-other = [color=crimson]{ CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } grey and clammy, { POSS-ADJ($target) } skin blotched.[/color]

# Part names in the lower case the sentences above want.
wolfmed-look-part-name-head = head
wolfmed-look-part-name-torso = torso
wolfmed-look-part-name-groin = groin
wolfmed-look-part-name-left-arm = left arm
wolfmed-look-part-name-left-hand = left hand
wolfmed-look-part-name-right-arm = right arm
wolfmed-look-part-name-right-hand = right hand
wolfmed-look-part-name-left-leg = left leg
wolfmed-look-part-name-left-foot = left foot
wolfmed-look-part-name-right-leg = right leg
wolfmed-look-part-name-right-foot = right foot

# Blood, and what a chassis leaks instead.
wolfmed-look-bleed-oozing = [color=red]oozing blood[/color]
wolfmed-look-bleed-flowing = [color=red]bleeding freely[/color]
wolfmed-look-bleed-spurting = [color=crimson]spurting blood[/color]
wolfmed-look-bleed-oozing-mechanical = [color=orange]weeping fluid[/color]
wolfmed-look-bleed-flowing-mechanical = [color=orange]leaking fluid[/color]
wolfmed-look-bleed-spurting-mechanical = [color=orange]spraying fluid[/color]
wolfmed-look-soaking = [color=red]blood soaking through the clothing[/color]
wolfmed-look-soaking-mechanical = [color=orange]fluid soaking through the clothing[/color]

# Treatment. What is tied, stitched or seared onto the part is as visible as the wound under it.
wolfmed-look-treatment-bandaged = dressed with a bandage
wolfmed-look-treatment-clamped = clamped shut
wolfmed-look-treatment-sutured = stitched shut
wolfmed-look-treatment-cauterized = seared shut
wolfmed-look-splint-gauze = wrapped in gauze
wolfmed-look-splint-splint = held in a splint
wolfmed-look-splint-splintimprovised = held in an improvised splint
wolfmed-look-splint-splinttribal = bound in a wooden splint
wolfmed-look-tourniquet = a tourniquet strapped above it

# Infection, never as a number.
wolfmed-look-infection-local = [color=orange]red and inflamed[/color]
wolfmed-look-infection-spreading = [color=red]red streaks running from the wound, weeping[/color]

wolfmed-look-scars = { $count } { $count ->
    [one] scar
   *[other] scars
}
wolfmed-look-numb = you cannot feel it

# Bruising and crushing.
wolfmed-look-bruise-minor = faint bruising
wolfmed-look-bruise-moderate = dark bruising
wolfmed-look-bruise-severe = deep purple bruising and swelling
wolfmed-look-bruise-critical = black, swollen bruising
wolfmed-look-crush-moderate = crushed and swollen
wolfmed-look-crush-severe = badly crushed, flattened out of shape

# Cuts and punctures.
wolfmed-look-cut-minor = a shallow cut
wolfmed-look-cut-moderate = a deep cut
wolfmed-look-cut-severe = a gaping cut
wolfmed-look-cut-critical = flesh laid open
wolfmed-look-puncture-minor = a small puncture
wolfmed-look-puncture-moderate = a deep puncture
wolfmed-look-puncture-severe = a wide puncture wound
wolfmed-look-puncture-critical = a wound punched clean through
wolfmed-look-avulsion-minor = a bite has torn the skin
wolfmed-look-avulsion-moderate = a bite has torn flesh away
wolfmed-look-avulsion-severe = a bite has torn a chunk out
wolfmed-look-arterial = [color=crimson]an artery cut open[/color]
wolfmed-look-incision = an open surgical incision
wolfmed-look-surgical-scar = a neat surgical scar

# Gunshots. A round still in there reads as a hole with nothing coming back out of it.
wolfmed-look-graze = a bullet graze
wolfmed-look-gunshot-minor = a bullet hole
wolfmed-look-gunshot-moderate = a bullet wound
wolfmed-look-gunshot-severe = a wide gunshot wound
wolfmed-look-gunshot-critical = a gunshot wound torn wide open
wolfmed-look-lodged-round = a bullet hole with no exit
wolfmed-look-shrapnel-minor = a metal fragment standing out of the skin
wolfmed-look-shrapnel-moderate = metal fragments standing out of the skin
wolfmed-look-shrapnel-severe = peppered with metal fragments

# Burns, cold and acid.
wolfmed-look-burn-minor = reddened skin
wolfmed-look-burn-moderate = blistered skin
wolfmed-look-burn-severe = a raw, weeping burn
wolfmed-look-burn-critical = skin burned black
wolfmed-look-charring-moderate = charred black skin
wolfmed-look-charring-severe = dead, charred tissue
wolfmed-look-charring-critical = burnt to a blackened crust
wolfmed-look-frostbite-minor = pale, waxy skin
wolfmed-look-frostbite-moderate = white, hard skin
wolfmed-look-frostbite-severe = grey, hard skin
wolfmed-look-frostbite-critical = blackened at the extremities
wolfmed-look-chemical-minor = reddened, raw skin
wolfmed-look-chemical-moderate = skin eaten away
wolfmed-look-chemical-severe = skin eaten down through the flesh
wolfmed-look-electrical-minor = a small scorch mark
wolfmed-look-electrical-moderate = a blackened entry burn
wolfmed-look-electrical-severe = a deep electrical burn
wolfmed-look-electrical-critical = burnt, split skin
wolfmed-look-internal-burn-self = something burns deep inside

# Bone. Only a bone out of line or through the skin shows; the rest is something you feel.
wolfmed-look-fracture-displaced = [color=orange]bent at a wrong angle[/color]
wolfmed-look-fracture-comminuted = [color=crimson]bone showing through the skin[/color]
wolfmed-look-fracture-ache = it aches deeply when you move it
wolfmed-look-fracture-ache-bad = it hurts sharply to move
wolfmed-look-dislocation = [color=orange]sitting out of joint[/color]
wolfmed-look-tendon-self = it will not answer properly
wolfmed-look-concussion-self = your head is swimming
wolfmed-look-concussion-severe = unfocused eyes, slow to follow anything

# Missing flesh.
wolfmed-look-dismemberment = [color=crimson]torn off at the joint[/color]
wolfmed-look-stump = [color=crimson]ends in a stump[/color]
wolfmed-look-necrosis = [color=purple]dark, dead-looking tissue[/color]

# Chassis. Never flesh words.
wolfmed-look-chassis-minor = scraped plating
wolfmed-look-chassis-moderate = buckled plating
wolfmed-look-chassis-severe = plating torn open
wolfmed-look-chassis-critical = the casing caved in
wolfmed-look-dent-minor = a shallow dent
wolfmed-look-dent-moderate = a deep dent
wolfmed-look-dent-severe = plating hammered out of shape
wolfmed-look-breach-minor = a punched hole with fluid seeping out
wolfmed-look-breach-moderate = a torn breach running fluid
wolfmed-look-breach-severe = [color=orange]a gaping breach, fluid running freely[/color]
wolfmed-look-short-minor = scorch marks around a seam
wolfmed-look-short-moderate = [color=yellow]sparks spitting from a seam[/color]
wolfmed-look-short-severe = [color=yellow]sparks arcing across the plating[/color]
wolfmed-look-servo-severe = hanging slack and unmoving
wolfmed-look-servo-slack = it answers slowly and badly
wolfmed-look-overheat-hot = giving off heat
wolfmed-look-overheat-overheated = [color=orange]heat shimmering off it[/color]
wolfmed-look-overheat-self = it runs uncomfortably hot
wolfmed-look-frame-displaced = [color=orange]the frame bent out of true[/color]
wolfmed-look-frame-comminuted = [color=crimson]the frame shattered, plating splayed open[/color]

# Slime.
wolfmed-look-slime-blunt-minor = a dented smear in the membrane
wolfmed-look-slime-blunt-moderate = a deep dent in the membrane
wolfmed-look-slime-blunt-severe = the membrane collapsed inward
wolfmed-look-slime-blunt-critical = the membrane caved in
wolfmed-look-slime-slash-minor = a shallow split in the membrane
wolfmed-look-slime-slash-moderate = a deep split in the membrane
wolfmed-look-slime-slash-severe = a gaping split leaking slime
wolfmed-look-slime-slash-critical = the membrane laid open
wolfmed-look-slime-piercing-minor = a small hole in the membrane
wolfmed-look-slime-piercing-moderate = a deep hole in the membrane
wolfmed-look-slime-piercing-severe = a wide hole leaking slime
wolfmed-look-slime-piercing-critical = a hole punched clean through
wolfmed-look-slime-burn-minor = clouded membrane
wolfmed-look-slime-burn-moderate = blistered membrane
wolfmed-look-slime-burn-severe = curdled membrane
wolfmed-look-slime-burn-critical = membrane burned away

# Plant.
wolfmed-look-plant-blunt-minor = a bruised stem
wolfmed-look-plant-blunt-moderate = a crushed stem
wolfmed-look-plant-blunt-severe = a badly crushed stem
wolfmed-look-plant-blunt-critical = the stem pulped
wolfmed-look-plant-slash-minor = a shallow cut in the bark
wolfmed-look-plant-slash-moderate = a deep cut in the bark
wolfmed-look-plant-slash-severe = a split running through the stem
wolfmed-look-plant-slash-critical = the stem split open
wolfmed-look-plant-piercing-minor = a small bore hole
wolfmed-look-plant-piercing-moderate = a deep bore hole
wolfmed-look-plant-piercing-severe = a wide bore through the stem
wolfmed-look-plant-piercing-critical = the stem bored through
wolfmed-look-plant-burn-minor = scorched leaves
wolfmed-look-plant-burn-moderate = blackened leaves
wolfmed-look-plant-burn-severe = a charred stem
wolfmed-look-plant-burn-critical = burned to cinder

# LOOK2: row labels. Two to four words each; the sentences above are what the chip says on hover.

# Blood, treatment, infection and what the patient alone knows.
wolfmed-look-bleed-oozing-short = oozing blood
wolfmed-look-bleed-flowing-short = bleeding freely
wolfmed-look-bleed-spurting-short = spurting blood
wolfmed-look-bleed-oozing-mechanical-short = weeping fluid
wolfmed-look-bleed-flowing-mechanical-short = leaking fluid
wolfmed-look-bleed-spurting-mechanical-short = spraying fluid
wolfmed-look-soaking-short = blood soaking through
wolfmed-look-soaking-mechanical-short = fluid soaking through
wolfmed-look-treatment-bandaged-short = bandaged
wolfmed-look-treatment-clamped-short = clamped
wolfmed-look-treatment-sutured-short = stitched
wolfmed-look-treatment-cauterized-short = seared shut
wolfmed-look-splint-gauze-short = gauze wrap
wolfmed-look-splint-splint-short = splinted
wolfmed-look-splint-splintimprovised-short = rough splint
wolfmed-look-splint-splinttribal-short = wooden splint
wolfmed-look-tourniquet-short = tourniquet
wolfmed-look-infection-local-short = red and inflamed
wolfmed-look-infection-spreading-short = red streaks
wolfmed-look-scars-short = { $count } { $count ->
    [one] scar
   *[other] scars
}
wolfmed-look-numb-short = numb

# Bruising and crushing.
wolfmed-look-bruise-minor-short = faint bruising
wolfmed-look-bruise-moderate-short = dark bruising
wolfmed-look-bruise-severe-short = deep bruising
wolfmed-look-bruise-critical-short = black bruising
wolfmed-look-crush-moderate-short = crushed and swollen
wolfmed-look-crush-severe-short = badly crushed

# Cuts and punctures.
wolfmed-look-cut-minor-short = shallow cut
wolfmed-look-cut-moderate-short = deep cut
wolfmed-look-cut-severe-short = gaping cut
wolfmed-look-cut-critical-short = flesh laid open
wolfmed-look-puncture-minor-short = small puncture
wolfmed-look-puncture-moderate-short = deep puncture
wolfmed-look-puncture-severe-short = wide puncture
wolfmed-look-puncture-critical-short = punched through
wolfmed-look-avulsion-minor-short = bite, torn skin
wolfmed-look-avulsion-moderate-short = bite, torn flesh
wolfmed-look-avulsion-severe-short = chunk torn out
wolfmed-look-arterial-short = cut artery
wolfmed-look-incision-short = open incision
wolfmed-look-surgical-scar-short = surgical scar

# Gunshots.
wolfmed-look-graze-short = bullet graze
wolfmed-look-gunshot-minor-short = bullet hole
wolfmed-look-gunshot-moderate-short = bullet wound
wolfmed-look-gunshot-severe-short = wide gunshot
wolfmed-look-gunshot-critical-short = gunshot torn open
wolfmed-look-lodged-round-short = bullet, no exit
wolfmed-look-shrapnel-minor-short = metal fragment
wolfmed-look-shrapnel-moderate-short = metal fragments
wolfmed-look-shrapnel-severe-short = peppered with metal

# Burns, cold and acid.
wolfmed-look-burn-minor-short = reddened skin
wolfmed-look-burn-moderate-short = blistered skin
wolfmed-look-burn-severe-short = raw burn
wolfmed-look-burn-critical-short = burned black
wolfmed-look-charring-moderate-short = charred skin
wolfmed-look-charring-severe-short = charred tissue
wolfmed-look-charring-critical-short = blackened crust
wolfmed-look-frostbite-minor-short = waxy skin
wolfmed-look-frostbite-moderate-short = frozen white
wolfmed-look-frostbite-severe-short = frozen grey
wolfmed-look-frostbite-critical-short = blackened extremities
wolfmed-look-chemical-minor-short = raw skin
wolfmed-look-chemical-moderate-short = skin eaten away
wolfmed-look-chemical-severe-short = eaten to the flesh
wolfmed-look-electrical-minor-short = scorch mark
wolfmed-look-electrical-moderate-short = entry burn
wolfmed-look-electrical-severe-short = deep electrical burn
wolfmed-look-electrical-critical-short = split, burnt skin
wolfmed-look-internal-burn-self-short = burns inside

# Bone, joints and the head.
wolfmed-look-fracture-displaced-short = bent wrong
wolfmed-look-fracture-comminuted-short = bone through skin
wolfmed-look-fracture-ache-short = aches to move
wolfmed-look-fracture-ache-bad-short = sharp on movement
wolfmed-look-dislocation-short = out of joint
wolfmed-look-tendon-self-short = answers badly
wolfmed-look-concussion-self-short = head swimming
wolfmed-look-concussion-severe-short = unfocused eyes

# Missing flesh.
wolfmed-look-dismemberment-short = torn off
wolfmed-look-stump-short = a stump
wolfmed-look-necrosis-short = dead tissue

# Chassis.
wolfmed-look-chassis-minor-short = scraped plating
wolfmed-look-chassis-moderate-short = buckled plating
wolfmed-look-chassis-severe-short = plating torn open
wolfmed-look-chassis-critical-short = casing caved in
wolfmed-look-dent-minor-short = shallow dent
wolfmed-look-dent-moderate-short = deep dent
wolfmed-look-dent-severe-short = plating hammered
wolfmed-look-breach-minor-short = hole, seeping fluid
wolfmed-look-breach-moderate-short = breach, running fluid
wolfmed-look-breach-severe-short = gaping breach
wolfmed-look-short-minor-short = scorched seam
wolfmed-look-short-moderate-short = sparking seam
wolfmed-look-short-severe-short = arcing sparks
wolfmed-look-servo-severe-short = hanging slack
wolfmed-look-servo-slack-short = answers slowly
wolfmed-look-overheat-hot-short = giving off heat
wolfmed-look-overheat-overheated-short = shimmering with heat
wolfmed-look-overheat-self-short = runs hot
wolfmed-look-frame-displaced-short = frame bent
wolfmed-look-frame-comminuted-short = frame shattered

# Slime.
wolfmed-look-slime-blunt-minor-short = dented membrane
wolfmed-look-slime-blunt-moderate-short = deep dent
wolfmed-look-slime-blunt-severe-short = membrane collapsed
wolfmed-look-slime-blunt-critical-short = membrane caved in
wolfmed-look-slime-slash-minor-short = shallow split
wolfmed-look-slime-slash-moderate-short = deep split
wolfmed-look-slime-slash-severe-short = gaping split
wolfmed-look-slime-slash-critical-short = membrane laid open
wolfmed-look-slime-piercing-minor-short = small hole
wolfmed-look-slime-piercing-moderate-short = deep hole
wolfmed-look-slime-piercing-severe-short = wide hole
wolfmed-look-slime-piercing-critical-short = punched through
wolfmed-look-slime-burn-minor-short = clouded membrane
wolfmed-look-slime-burn-moderate-short = blistered membrane
wolfmed-look-slime-burn-severe-short = curdled membrane
wolfmed-look-slime-burn-critical-short = membrane burned away

# Plant.
wolfmed-look-plant-blunt-minor-short = bruised stem
wolfmed-look-plant-blunt-moderate-short = crushed stem
wolfmed-look-plant-blunt-severe-short = badly crushed stem
wolfmed-look-plant-blunt-critical-short = stem pulped
wolfmed-look-plant-slash-minor-short = shallow cut
wolfmed-look-plant-slash-moderate-short = deep cut
wolfmed-look-plant-slash-severe-short = split stem
wolfmed-look-plant-slash-critical-short = stem split open
wolfmed-look-plant-piercing-minor-short = small bore hole
wolfmed-look-plant-piercing-moderate-short = deep bore hole
wolfmed-look-plant-piercing-severe-short = wide bore
wolfmed-look-plant-piercing-critical-short = bored through
wolfmed-look-plant-burn-minor-short = scorched leaves
wolfmed-look-plant-burn-moderate-short = blackened leaves
wolfmed-look-plant-burn-severe-short = charred stem
wolfmed-look-plant-burn-critical-short = burned to cinder

# Evisceration (EVISC). Nothing covers this one: clothing does not close an abdomen.

wolfmed-look-evisceration = [color=crimson]their abdomen laid open, organs exposed[/color]
wolfmed-look-evisceration-short = abdomen laid open
wolfmed-look-chassis-breach = [color=orange]their torso plating peeled back, internals exposed[/color]
wolfmed-look-chassis-breach-short = torso torn open

# ARREST: what anyone can see from across the room once the heart has stopped.
wolfmed-look-appears-dead-self = You do not look alive.
wolfmed-look-appears-dead-other = { CAPITALIZE(THE($target)) } appears to be dead.

# BRAIN: what a hand on the neck and an ear to the chest tell you.
wolfmed-look-no-pulse-self = You cannot find your own pulse.
wolfmed-look-no-pulse-other = { CAPITALIZE(THE($target)) } has no pulse.
wolfmed-look-not-breathing-self = You are not breathing.
wolfmed-look-not-breathing-other = { CAPITALIZE(THE($target)) } is not breathing.

# M1a: the chest and the pulse, from the networked vitals (plan §4.5, §5.5).
wolfmed-look-gasping-self = You are gasping for air.
wolfmed-look-gasping-other = { CAPITALIZE(THE($target)) } is gasping for air.
wolfmed-look-breathing-slow-self = Your breathing is slow and shallow.
wolfmed-look-breathing-slow-other = { CAPITALIZE(THE($target)) } is breathing slowly and shallowly.
wolfmed-look-pale-self = You look pale.
wolfmed-look-pale-other = { CAPITALIZE(THE($target)) } looks pale.
wolfmed-look-pulse-weak-self = You are pale and clammy, and your pulse is weak and fast.
wolfmed-look-pulse-weak-other = { CAPITALIZE(THE($target)) } is pale and clammy, with a weak, rapid pulse.
wolfmed-look-pulse-faint-self = You can barely find your own pulse.
wolfmed-look-pulse-faint-other = { CAPITALIZE(THE($target)) } has a barely palpable pulse.
wolfmed-look-shutdown-self = Nothing inside you is running.
wolfmed-look-shutdown-other = Nothing inside { THE($target) } is running.

# M2 (plan §5.5): the medic's close-up signs. AVPU from the state and cause; lips and pupils from the vitals.
wolfmed-look-avpu-alert = { CAPITALIZE(THE($target)) } is awake and answers you.
wolfmed-look-avpu-voice = { CAPITALIZE(THE($target)) } is drowsy and responds only to voice.
wolfmed-look-avpu-pain = { CAPITALIZE(THE($target)) } is out cold and stirs only to pain.
wolfmed-look-avpu-unresponsive = { CAPITALIZE(THE($target)) } is unresponsive.
wolfmed-look-blue-lips = { CAPITALIZE(POSS-ADJ($target)) } lips are blue.
wolfmed-look-pupils-pinpoint = { CAPITALIZE(POSS-ADJ($target)) } pupils are pinpoint.
wolfmed-look-pupils-unequal = { CAPITALIZE(POSS-ADJ($target)) } pupils are unequal, and { SUBJECT($target) } { CONJUGATE-BE($target) } confused.
# M2 (plan §5.3, OD20): Play dead, seen from a distance.
wolfmed-look-lifeless-other = { CAPITALIZE(THE($target)) } appears lifeless.

# P5-5: the chassis form of Onyx's health-examinable-part-bleeding. Nothing reads it yet.
health-examinable-part-bleeding-mechanical = leaking fluid
