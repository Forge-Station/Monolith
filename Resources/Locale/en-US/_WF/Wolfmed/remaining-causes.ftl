# M5: the remaining causes (plan §3.8-3.10): toxins, radiation, cold and heat.

# Toxins: Downed, then a toxic coma.
wolfmed-cause-toxin = poisoning
wolfmed-cause-toxin-title = Downed: poisoned
wolfmed-cause-toxin-title-out = Unconscious: toxic coma
wolfmed-cause-toxin-symptom = Nausea and weakness. There is poison in your blood.
wolfmed-cause-toxin-help = Antitoxin (dylovene) clears it; your liver clears it slowly on its own. Painkillers will not help.
wolfmed-cause-toxin-help-blocked = Antitoxin, and the rest treated, gets you up.
wolfmed-cause-toxin-help-out = You will wake as the poison clears. Antitoxin (dylovene) clears it fast.
wolfmed-cause-toxin-help-out-blocked = You will not wake until the poison clears and the rest is treated.
wolfmed-cause-toxin-down = Your stomach heaves and your legs give way.
wolfmed-cause-toxin-out = The poison drags you under.
wolfmed-cause-toxin-wake = You come round, sick and weak.
wolfmed-cause-toxin-stand = The nausea eases enough to stand.

# Radiation sickness: Downed only.
wolfmed-cause-radiation = radiation sickness
wolfmed-cause-radiation-title = Downed: radiation sickness
wolfmed-cause-radiation-symptom = Sick and weak to the bone. Your blood is not coming back.
wolfmed-cause-radiation-help = Get away from the radiation. Hyronalin or arithrazine treat it; you may need blood.
wolfmed-cause-radiation-help-blocked = Anti-radiation drugs, and the rest treated, get you up.
wolfmed-cause-radiation-down = A wave of sickness puts you on the floor.
wolfmed-cause-radiation-wake = You come round, sick to the bone.
wolfmed-cause-radiation-stand = The sickness eases enough to stand.

# Hypothermia: Downed, then Unconscious. The heart stopping is the arrest cause.
wolfmed-cause-cold = hypothermia
wolfmed-cause-cold-title = Downed: hypothermic
wolfmed-cause-cold-title-out = Unconscious: hypothermia
wolfmed-cause-cold-symptom = Shivering, numb and slow.
wolfmed-cause-cold-help = Get warm. Painkillers will not help.
wolfmed-cause-cold-help-blocked = Get warm, and have the rest treated.
wolfmed-cause-cold-help-out = You will come round as you warm up.
wolfmed-cause-cold-help-out-blocked = You will not come round until you are warm and the rest is treated.
wolfmed-cause-cold-down = The cold takes your legs.
wolfmed-cause-cold-out = The cold drags you under.
wolfmed-cause-cold-wake = You come round, shivering.
wolfmed-cause-cold-stand = Warm enough to stand.

# Heat exhaustion, then heat stroke.
wolfmed-cause-heat = overheating
wolfmed-cause-heat-title = Downed: heat exhaustion
wolfmed-cause-heat-title-out = Unconscious: heat stroke
wolfmed-cause-heat-symptom = Dizzy, hot and dry.
wolfmed-cause-heat-help = Get somewhere cool. Painkillers will not help.
wolfmed-cause-heat-help-blocked = Get cool, and have the rest treated.
wolfmed-cause-heat-help-out = Heat stroke is cooking your brain. You need cooling, now.
wolfmed-cause-heat-help-out-blocked = You need cooling, and the rest treated, before you come round.
wolfmed-cause-heat-down = The heat makes your head swim and you go down.
wolfmed-cause-heat-out = The heat knocks you out.
wolfmed-cause-heat-wake = You come round, feverish and dry.
wolfmed-cause-heat-stand = Cool enough to stand.

# The new drains behind hypoxia and the new arrest triggers.
wolfmed-cause-hypoxia-source-toxin = toxic coma
wolfmed-cause-hypoxia-source-heat = heat stroke
wolfmed-cause-arrest-source-cold = cold
wolfmed-cause-arrest-source-toxin = poisoning
wolfmed-cause-arrest-source-heat = heat stroke

alerts-wolfmed-downed-toxin-name = Downed: poisoned
alerts-wolfmed-downed-toxin-desc = Poison in your blood. Antitoxin (dylovene) clears it; your liver clears it slowly. You can crawl.
alerts-wolfmed-out-toxin-name = Unconscious: toxic coma
alerts-wolfmed-out-toxin-desc = You wake as the poison clears. Antitoxin clears it fast.
alerts-wolfmed-downed-radiation-name = Downed: radiation sickness
alerts-wolfmed-downed-radiation-desc = Get away from the radiation. Hyronalin or arithrazine treat it. You can crawl.
alerts-wolfmed-downed-cold-name = Downed: hypothermic
alerts-wolfmed-downed-cold-desc = Your core is too cold. Get warm. You can crawl.
alerts-wolfmed-out-cold-name = Unconscious: hypothermia
alerts-wolfmed-out-cold-desc = You come round as you warm up.
alerts-wolfmed-downed-heat-name = Downed: heat exhaustion
alerts-wolfmed-downed-heat-desc = Your core is too hot. Get somewhere cool. You can crawl.
alerts-wolfmed-out-heat-name = Unconscious: heat stroke
alerts-wolfmed-out-heat-desc = Heat stroke is damaging your brain. You need cooling.

# The analyzer's vitals block.
wolfmed-vitals-cause-toxin = poisoning
wolfmed-vitals-cause-radiation = radiation sickness
wolfmed-vitals-cause-cold = hypothermia
wolfmed-vitals-cause-heat = overheating
wolfmed-vitals-source-toxin = toxic coma
wolfmed-vitals-source-heat = heat stroke
wolfmed-vitals-source-arrestcold = cold
wolfmed-vitals-source-arresttoxin = poisoning
wolfmed-vitals-source-arrestheat = heat stroke
wolfmed-vitals-toxin-band-low = low
wolfmed-vitals-toxin-band-high = high
wolfmed-vitals-toxin-band-coma = comatose, brain at risk
wolfmed-vitals-radiation-low = Radiation { $dose }
wolfmed-vitals-radiation-suppressed = Radiation { $dose }, marrow suppressed
wolfmed-vitals-radiation-failing = Radiation { $dose }, marrow failing
wolfmed-vitals-core-cold = Core temperature { $kelvin } K, hypothermic
wolfmed-vitals-core-hot = Core temperature { $kelvin } K, overheating
wolfmed-vitals-verdict-toocold = Defib: refused: core { $kelvin } K, rewarm above { $line } K first

# The waiting ghost's "your body is getting worse" line.
wolfmed-dormant-route-toxin = the poison is starving your brain
wolfmed-dormant-route-heatstroke = heat stroke is damaging your brain
wolfmed-dormant-route-marrow = your marrow is failing and you are losing blood
wolfmed-dormant-route-hypothermia = you are still getting colder

# The paddles.
wolfmed-defib-too-cold = Shock refused: core temperature {$kelvin} K. Rewarm above {$rewarm} K first.

# Examine, close up.
wolfmed-look-poisoned-self = You feel sick to your stomach.
wolfmed-look-poisoned-other = { CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } retching and sweating.
wolfmed-look-radiation-sick-self = You feel sick and weak to the bone.
wolfmed-look-radiation-sick-other = { CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } grey and sickly, bruising at the gums.
wolfmed-look-hypothermic-self = You are shivering, numb with cold.
wolfmed-look-hypothermic-other = { CAPITALIZE(SUBJECT($target)) } { CONJUGATE-BE($target) } cold and stiff to the touch.
wolfmed-look-overheated-self = Your skin is burning hot and dry.
wolfmed-look-overheated-other = { CAPITALIZE(POSS-ADJ($target)) } skin is hot and dry.
