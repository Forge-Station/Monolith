# Consciousness and painkillers (CONSC).

alerts-wolfmed-downed-name = Downed
alerts-wolfmed-downed-desc = On the floor. You can crawl, talk and use what you carry.

# Painkiller tiers, as the analyzer and the guidebook name them.

wolfmed-pain-relief-tier-none = none
wolfmed-pain-relief-tier-weak = weak
wolfmed-pain-relief-tier-strong = strong
wolfmed-pain-relief-tier-stimulant = stimulant
wolfmed-pain-relief-tier-emergency = emergency

reagent-effect-guidebook-wolfmed-pain-relief =
    { $chance ->
        [1] Relieves
       *[other] chance to relieve
    } pain at the { $tier } tier, worth { $strength } pain. Treats nothing.

# Analyzer banners.

health-analyzer-wound-pain-relief = Pain relief: { $tier }, { $seconds }s left. Masks pain only; treats nothing.
health-analyzer-wound-sedation = Sedation { $percent }%. Breathing is depressed past 60%.
health-analyzer-wound-banner-pain-relief = Pain relief
health-analyzer-wound-banner-sedation = Sedation

# Wolfmed's own painkiller ladder.

reagent-name-wolfmed-analgesic = analgesic
reagent-desc-wolfmed-analgesic = A plain painkiller. Enough to get a wounded crewman back on their feet, never enough to keep them awake.

reagent-name-wolfmed-opiate = opiate
reagent-desc-wolfmed-opiate = A heavy painkiller. It will walk a patient off the field on a broken leg, and it will stop them breathing if you keep going.

reagent-name-wolfmed-stim = stim
reagent-desc-wolfmed-stim = Thirty seconds of standing upright, bought against the next thirty. Does nothing for blood or air.

# M1a: causes (plan §5.1-5.2). Every line that names a cause and promises anything is conditional, and the
# blocked forms are shown while something else also holds the body.

wolfmed-condition-title-up = On your feet
wolfmed-condition-title-dead = Dead
wolfmed-condition-title-downed = Downed: { $cause }
wolfmed-condition-title-out = Unconscious: { $cause }
wolfmed-condition-cause-unknown = something else
wolfmed-condition-cause-with-source = { $cause } ({ $source })
wolfmed-condition-source-unknown = unknown cause
wolfmed-condition-blockers = Also: { $blockers }.
wolfmed-condition-chat = [color=#d9b38c]{ $message }[/color]

wolfmed-condition-down = You collapse.
wolfmed-condition-out = Everything goes dark.
wolfmed-condition-wake = You come round.
wolfmed-condition-wake-up = You come round and stand.
wolfmed-condition-stand = You get up.
wolfmed-condition-heart-restart = Your heart restarts.
wolfmed-condition-heart-restart-still = Your heart restarts. Still down: { $cause }. { $help }
wolfmed-condition-adrenaline-start = Adrenaline: you can crawl faster for a while.
wolfmed-condition-adrenaline-end = The adrenaline fades.

# Playtest 1: the patient feels a painkiller arrive and leave. Keys are built from the tier name.
wolfmed-painkiller-takes-hold-weak = The painkiller takes the edge off.
wolfmed-painkiller-takes-hold-strong = The opiate takes hold.
wolfmed-painkiller-takes-hold-stimulant = The stimulant kicks in.
wolfmed-painkiller-takes-hold-emergency = The stim hits. You have seconds on your feet.
wolfmed-painkiller-fading = The stronger painkiller wears off.
wolfmed-painkiller-worn-off = The painkiller wears off.

wolfmed-cause-pain = pain
wolfmed-cause-pain-symptom = Pain has you on the floor.
wolfmed-cause-pain-help = A painkiller gets you moving, unless something else holds you. The wounds remain.
wolfmed-cause-pain-help-mechanical = Sensors overloaded. Weld and cable the frame; painkillers do nothing.
wolfmed-cause-pain-down = The pain drops you.
wolfmed-cause-pain-wake = You come round, still in agony.
wolfmed-cause-pain-stand = The pain eases. You stand.

wolfmed-cause-pain-faint = a pain faint
wolfmed-cause-pain-faint-title = Passed out: pain
wolfmed-cause-pain-faint-symptom = Pain knocked you out.
wolfmed-cause-pain-faint-help = You come round in seconds.
wolfmed-cause-pain-faint-help-timed = Coming round in { $seconds } s.
wolfmed-cause-pain-faint-help-blocked = The pain passes in seconds; something else keeps you under.
wolfmed-cause-pain-faint-out = The pain takes you under.

wolfmed-cause-blood = blood loss
wolfmed-cause-blood-symptom = Light-headed and cold.
wolfmed-cause-blood-help = Stop the bleeding. You need blood, not painkillers.
wolfmed-cause-blood-help-out = You come round as your blood recovers, if the bleeding stops.
wolfmed-cause-blood-help-out-blocked = You need blood, and the rest treated.
wolfmed-cause-blood-down = You sink to the floor, light-headed.
wolfmed-cause-blood-out = Everything goes grey.
wolfmed-cause-blood-wake = You come round, weak and cold.
wolfmed-cause-blood-stand = Steady enough to stand.

wolfmed-cause-oil = hydraulic pressure low
wolfmed-cause-oil-title-out = Shutdown: hydraulic pressure low
wolfmed-cause-oil-symptom = Hydraulic pressure low. The frame cannot stand.
wolfmed-cause-oil-help = Weld the breach, refill the hydraulic fluid.
wolfmed-cause-oil-help-out = Weld the breach, refill the hydraulic fluid.
wolfmed-cause-oil-help-out-blocked = Weld the breach, refill the hydraulic fluid. Something else also holds the chassis.
wolfmed-cause-oil-down = Hydraulic pressure drops. Your frame sags.
wolfmed-cause-oil-out = Hydraulic pressure failed. Shutting down.
wolfmed-cause-oil-wake = Pressure restored. Online.
wolfmed-cause-oil-stand = Pressure holds. You stand.

wolfmed-cause-hypoxia = no oxygen
wolfmed-cause-hypoxia-title-downed = Short of breath
wolfmed-cause-hypoxia-source-airway = no air
wolfmed-cause-hypoxia-source-lungs = lungs failing
wolfmed-cause-hypoxia-source-circulation = poor circulation
wolfmed-cause-hypoxia-source-sepsis = sepsis
wolfmed-cause-hypoxia-source-sedation = breathing slowed
wolfmed-cause-hypoxia-symptom = You cannot get your breath.
wolfmed-cause-hypoxia-help = Get to air. Treat what starves you of oxygen.
wolfmed-cause-hypoxia-help-out = You need air or internals, and the cause treated.
wolfmed-cause-hypoxia-down = Short of breath, you sink to the floor.
wolfmed-cause-hypoxia-out = Your vision narrows to nothing.
wolfmed-cause-hypoxia-wake = You come round gasping.
wolfmed-cause-hypoxia-stand = Your breathing steadies.

wolfmed-cause-sedation = overdose
wolfmed-cause-sedation-title-downed = Drowsy
wolfmed-cause-sedation-symptom = Painkillers are slowing you.
wolfmed-cause-sedation-help = Wears off with time; naloxone reverses it. Another dose makes it worse.
wolfmed-cause-sedation-help-out = Overdose. Breathing slowed. Naloxone reverses it, or it wears off with time.
wolfmed-cause-sedation-down = You slump, eyelids drooping.
wolfmed-cause-sedation-out = The painkillers drag you under.
wolfmed-cause-sedation-wake = You surface, groggy.
wolfmed-cause-sedation-stand = The drowsiness lifts.

wolfmed-cause-legs = legs
wolfmed-cause-legs-symptom = Your legs will not hold you.
wolfmed-cause-legs-help = Splint or repair a leg. You can crawl.
wolfmed-cause-legs-down = Your legs give way.
wolfmed-cause-legs-stand = You get your legs under you.

wolfmed-cause-crash = stim crash
wolfmed-cause-crash-symptom = The stim wore off.
wolfmed-cause-crash-help = Passes in seconds, unless something else holds you.
wolfmed-cause-crash-down = The stim wears off. Your legs give out.
wolfmed-cause-crash-stand = The crash passes.

wolfmed-cause-arrest = cardiac arrest
wolfmed-cause-arrest-title = Cardiac arrest: { $source }
wolfmed-cause-arrest-source-blood = blood loss
wolfmed-cause-arrest-source-oxygen = no oxygen
wolfmed-cause-arrest-source-heart = heart failure
wolfmed-cause-arrest-source-sepsis = sepsis
wolfmed-cause-arrest-source-shock = shock
wolfmed-cause-arrest-source-other = unknown cause
wolfmed-cause-arrest-symptom = Your heart has stopped.
wolfmed-cause-arrest-help-out = Defibrillator needed. Brain injury in about a minute without CPR. You may let go.
wolfmed-cause-arrest-out = Your heart stops.

wolfmed-cause-shutdown = shutdown
wolfmed-cause-shutdown-title = Shutdown: { $source }
wolfmed-cause-shutdown-source-power = no power
wolfmed-cause-shutdown-source-pump = coolant pump offline
wolfmed-cause-shutdown-symptom = Systems down. Nothing is getting worse.
wolfmed-cause-shutdown-help-out = A charged cell or working pump brings you back, unless something else holds you.
wolfmed-cause-shutdown-out = Power lost. Shutting down.
wolfmed-cause-shutdown-wake = Systems online.

wolfmed-cause-other = something else

# M1a: condition alerts. Short: clicking the alert gives the full text with what else holds you down.

alerts-wolfmed-downed-pain-name = Downed: pain
alerts-wolfmed-downed-pain-desc = Pain has you down. Crawl and treat yourself. A painkiller gets you moving.
alerts-wolfmed-downed-frame-name = Downed: frame damage
alerts-wolfmed-downed-frame-desc = Sensors overloaded. The frame will not stand. Weld and cable to get moving.
alerts-wolfmed-faint-pain-name = Passed out: pain
alerts-wolfmed-faint-pain-desc = Knocked out by pain. Passes in seconds.
alerts-wolfmed-downed-blood-name = Downed: blood loss
alerts-wolfmed-downed-blood-desc = Light-headed and cold. Stop the bleeding. You need blood.
alerts-wolfmed-out-blood-name = Unconscious: blood loss
alerts-wolfmed-out-blood-desc = You come round as your blood recovers, if the bleeding stops.
alerts-wolfmed-downed-oil-name = Downed: hydraulic pressure low
alerts-wolfmed-downed-oil-desc = Hydraulic pressure low. Weld the breach, refill the hydraulic fluid.
alerts-wolfmed-out-oil-name = Shutdown: hydraulic pressure low
alerts-wolfmed-out-oil-desc = Shut down for lack of hydraulic pressure. Back online as it returns.
alerts-wolfmed-downed-hypoxia-name = Short of breath
alerts-wolfmed-downed-hypoxia-desc = Short of oxygen. Get to air.
alerts-wolfmed-out-hypoxia-name = Unconscious: no oxygen
alerts-wolfmed-out-hypoxia-desc = You need air or internals.
alerts-wolfmed-downed-sedation-name = Drowsy
alerts-wolfmed-downed-sedation-desc = Painkillers are slowing you. Wears off with time.
alerts-wolfmed-out-sedation-name = Unconscious: overdose
alerts-wolfmed-out-sedation-desc = Overdose. Breathing slowed. Naloxone reverses it, or it wears off with time.
alerts-wolfmed-downed-legs-name = Downed: legs
alerts-wolfmed-downed-legs-desc = Your legs will not hold you. Splint or repair. You can crawl.
alerts-wolfmed-downed-crash-name = Downed: stim crash
alerts-wolfmed-downed-crash-desc = Stim crash. Passes in seconds.
alerts-wolfmed-out-arrest-name = Cardiac arrest
alerts-wolfmed-out-arrest-desc = Heart stopped. Defibrillator needed. Brain injury in about a minute without CPR. You may let go.
alerts-wolfmed-out-shutdown-name = Shutdown
alerts-wolfmed-out-shutdown-desc = Systems down. Nothing is getting worse. A charged cell or working pump brings you back.

# M1a: Call for help, the crawling stage's own action (plan §5.3).
wolfmed-call-for-help-title = Call for help
wolfmed-call-for-help-prompt = Shout (blank = "{ $default }")
wolfmed-call-for-help-default = Help! I'm down!
wolfmed-call-for-help-cooldown = You just called. Wait.

# Playtest 2: what a limb's wound penalty costs, told once when it first bites and kept in the condition text.
wolfmed-limb-penalty-hands = Your hands are hurt; everything takes longer.
wolfmed-limb-penalty-hands-burn = Your hands are badly burned; everything takes longer.
wolfmed-limb-penalty-legs = Your legs are hurt; moving is slow.
wolfmed-limb-penalty-legs-burn = Your legs are badly burned; moving is slow.

# Playtest 3: a body that is down trying to climb onto a table.
wolfmed-downed-cant-climb = You can't climb while you're down.

# Playtest 4, VISUALS: the pain HUD. AlertControl passes the alert's severity: the body's effective pain against its
# soft cap, 0 to 7, in the light / strong / terrible / agony bands the self-examine lines use; 8 is a pain faint.
alerts-wolfmed-pain-name = Pain
alerts-wolfmed-pain-desc = { $severity ->
        [0] Light pain.
        [1] Light pain.
        [2] Strong pain.
        [3] Strong pain.
        [4] Terrible pain.
        [5] Terrible pain.
        [6] Agony. A little more and it floors you.
        [7] Agony, at or past the point it floors you.
        [8] Floored by the pain. Crawl, or wait for it to ease.
       *[other] Passed out from the pain. It passes in seconds.
    }
alerts-wolfmed-pain-mechanical-name = Sensor overload
alerts-wolfmed-pain-mechanical-desc = { $severity ->
        [0] Minor fault signals.
        [1] Minor fault signals.
        [2] Fault signals.
        [3] Fault signals.
        [4] Heavy fault signals.
        [5] Heavy fault signals.
        [6] Sensors near overload. A little more and they drop you.
        [7] Sensors overloaded, at or past the point they drop you.
       *[other] Sensors overloaded: dropped. Repairs clear it.
    }
