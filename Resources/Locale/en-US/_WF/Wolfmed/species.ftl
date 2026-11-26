# M4: species and IPC death (plan §3.11, §9, OD3 (b), OD10, OD16).

## Thermal shutdown: a machine's core past its heat line (plan §3.11). Dying, like arrest.

wolfmed-cause-core-heat = core overheating
wolfmed-cause-core-heat-title = Thermal shutdown: core overheating
wolfmed-cause-core-heat-symptom = Your core is overheating. You have shut down to protect it, and it is still cooking.
wolfmed-cause-core-heat-help-out = Cooling needed: put the fire out, get to cold air. Back online once the core cools. Core damage needs core repair. You may let go.
wolfmed-cause-core-heat-out = THERMAL SHUTDOWN. Your core is overheating.
wolfmed-condition-core-cooled = Core temperature back under the line. Systems online.
wolfmed-condition-core-cooled-still = Core temperature back under the line. Still down: { $cause }.
wolfmed-synthetic-cause-core-heat = THERMAL SHUTDOWN: CORE DAMAGE
wolfmed-synthetic-line-core-temp-critical = CORE TEMP CRITICAL
wolfmed-synthetic-advice-core-temp = EXTINGUISH. SEEK COOLING
alerts-wolfmed-out-core-heat-name = Thermal shutdown
alerts-wolfmed-out-core-heat-desc = Core overheating and losing integrity. Cooling brings you back online. You may let go.

wolfmed-succumb-dialog-text-core = Your core is overheating. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a technician can bring you back with core repair and the restart button for about { $minutes } minutes. You will be asked to return.
wolfmed-succumb-dialog-text-core-no-decay = Your core is overheating. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a technician can bring you back with core repair and the restart button. You will be asked to return.
# Playtest 5: a chassis shut down for want of power or a pump can let go too. Nothing in it runs out, but nobody may come.
wolfmed-succumb-dialog-text-shutdown = Your chassis is shut down ({ $reason }). Nothing in it is running out, and nothing comes back on its own. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a technician can bring you back with core repair and the restart button for about { $minutes } minutes. You will be asked to return.
wolfmed-succumb-dialog-text-shutdown-no-decay = Your chassis is shut down ({ $reason }). Nothing in it is running out, and nothing comes back on its own. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a technician can bring you back with core repair and the restart button. You will be asked to return.

wolfmed-vitals-state-thermalshutdown = THERMAL SHUTDOWN: { $cause }
wolfmed-vitals-cause-coreheat = core overheating
wolfmed-vitals-cause-shutdown-mechanical = shutdown
wolfmed-vitals-temperature = Core { $core } K, chassis { $chassis } K
wolfmed-dormant-route-coreheat = your core is overheating

wolfmed-look-too-hot-self = You are smoking, too hot to touch.
wolfmed-look-too-hot-other = { CAPITALIZE(THE($target)) } is smoking, too hot to touch.

## Circulatory collapse (OD16): the arrest of a species with no heart (Diona, the slimes).

wolfmed-cause-collapse = circulatory collapse
wolfmed-cause-collapse-title = Circulatory collapse: { $source }
wolfmed-cause-collapse-symptom = Your circulation has collapsed.
wolfmed-cause-collapse-help-out = Defibrillator needed. Brain injury in about a minute without CPR. You may let go.
wolfmed-cause-collapse-out = Your circulation collapses.
wolfmed-condition-collapse-restart = Your circulation returns.
wolfmed-condition-collapse-restart-still = Your circulation returns. Still down: { $cause }. { $help }
alerts-wolfmed-out-collapse-name = Circulatory collapse
alerts-wolfmed-out-collapse-desc = Circulation stopped. Defibrillator needed. Brain injury in about a minute without CPR. You may let go.
wolfmed-collapse-banner = YOUR CIRCULATION HAS COLLAPSED

wolfmed-succumb-dialog-text-collapse = Your circulation has collapsed ({ $cause }). Let go and you die now: catastrophic brain injury. Your brain stays; a defibrillator brings you back with major brain damage, until the brain is repaired, for about { $minutes } minutes. You will be asked to return.
wolfmed-succumb-dialog-text-collapse-no-decay = Your circulation has collapsed ({ $cause }). Let go and you die now: catastrophic brain injury. Your brain stays; a defibrillator brings you back with major brain damage, until the brain is repaired. You will be asked to return.

wolfmed-vitals-state-collapse = CIRCULATORY COLLAPSE: { $cause }
