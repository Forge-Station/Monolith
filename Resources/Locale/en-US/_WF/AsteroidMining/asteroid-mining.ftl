tiles-wf-asteroid-core = asteroid core

wf-asteroid-examine-scan = A mining laser on it will show its contents on the gunnery console.
wf-asteroid-name-metallic = metallic {$name}
wf-asteroid-examine-metallic = Bare metal shows through the rock. It takes a lot of heat to crack.

wf-mining-gunnery-title = MINING LASER
wf-mining-gunnery-power = Power {$power}%
wf-mining-gunnery-power-help = Sets every mining laser on this ship. Keep the rock's heat in the green band. Yellow wastes ore, red blows it up.
wf-mining-gunnery-power-up = +{$amount}
wf-mining-gunnery-power-down = -{$amount}
wf-mining-gunnery-lasers = {$count ->
    [one] 1 laser
    *[other] {$count} lasers
}
wf-mining-gunnery-cooldown = Overloaded: {$seconds} s
wf-mining-gunnery-idle = Fire on a rock to scan it.
wf-mining-gunnery-idle-help = Fire a mining laser at an asteroid to scan it and track its heat.
wf-mining-gunnery-rock = {CAPITALIZE($name)}, {$mass} veins
wf-mining-gunnery-ore = {CAPITALIZE($name)} {$percent}%
wf-mining-gunnery-inert = Plain rock {$percent}%
wf-mining-gunnery-resistance = Resistance {$percent}%
wf-mining-gunnery-heat = Heat {$charge}%, window {$low}-{$high}%
wf-mining-gunnery-meter-help = The needle is the rock's heat. Keep it in the green band to crack the rock. Heat lags behind the power, so adjust in small steps.
wf-mining-gunnery-progress = Crack progress
wf-mining-gunnery-yield = Yield {$percent}%
wf-mining-gunnery-yield-help = How much of the rock's ore you get when it cracks. The yellow band cracks faster but wastes ore.
wf-mining-gunnery-overcharge = OVERCHARGING!
wf-mining-gunnery-overcharge-help = Too hot. Lower the power or stop firing before it blows.

wf-cockpit-mining = MINING
wf-cockpit-mining-short = MIN
wf-mining-page-target = TARGET
wf-mining-page-rock = {CAPITALIZE($name)}
wf-mining-page-hint = Point at a rock on the ship cam or NAV to scan it.
wf-mining-page-hint-help = Rocks in view range scan fully. Farther ones only show a signal until you get closer.
wf-mining-lever-caption = POWER
wf-mining-lever-help = Drag or scroll to set every mining laser on this ship. Keep the rock's heat in the green band. Yellow wastes ore, red blows it up.
wf-mining-dial-heat = HEAT
wf-mining-dial-unit = %
wf-mining-dial-empty = --
wf-mining-dial-idle = NO TARGET
wf-mining-dial-overcharge = OVERCHARGE
wf-mining-figure-window = WINDOW
wf-mining-figure-window-range = {$low}-{$high}
wf-mining-figure-crack = CRACK
wf-mining-figure-yield = YIELD
wf-mining-figures-help = WINDOW: the heat that cracks the rock cleanly. CRACK: how far the rock is to breaking apart. YIELD: how much of its ore you get when it cracks.
wf-mining-lamp-window = WINDOW
wf-mining-lamp-hot = HOT
wf-mining-lamp-overcharge = OVERCHARGE
wf-mining-lamp-overload = OVERLOAD
wf-mining-lamps-help = WINDOW: cracking cleanly. HOT: faster, but wastes ore. OVERCHARGE: about to blow. OVERLOAD: lasers down after a blast.

wf-mining-scan-tag = SCAN
wf-mining-scan-tag-far = SIGNAL
wf-mining-scan-range = Range {$range} m
wf-mining-scan-size = Ø {$size} m
wf-mining-scan-far = Out of scan range
wf-mining-scan-veins = {$mass} veins
wf-mining-scan-ore = {CAPITALIZE($name)}
wf-mining-scan-inert = Plain rock
wf-mining-scan-percent = {$percent}%
wf-mining-scan-resistance = Resistance
wf-mining-scan-window = Window
wf-mining-scan-window-range = {$low}-{$high}%
wf-mining-scan-stability = Heat drift
wf-mining-scan-stability-band = {$band ->
    [steady] Steady
    [drifting] Drifting
   *[erratic] Erratic
}
wf-mining-scan-blast = Overcharge blast
wf-mining-scan-blast-band = {$band ->
    [low] Low
    [high] High
   *[severe] Severe
}
wf-mining-scan-heat = Heat
wf-mining-scan-crack = Crack
wf-mining-scan-overcharge = OVERCHARGING
