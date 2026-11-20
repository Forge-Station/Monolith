## Shuttle console: entering the atmosphere
wf-shuttle-console-enter-atmosphere = Enter atmosphere: { $planet }
wf-shuttle-console-atmosphere-mode-tooltip = In atmosphere:
    Ordinary thrusters give half thrust.
    Landing thrusters give full thrust.
    Lift must reach 100% to take off or hover.

## Shuttle console: flight status and checklist
wf-flight-phase-orbit = In orbit: { $planet }
wf-flight-phase-landed = Landed: { $planet }
wf-flight-phase-lifting = Lifting off
wf-flight-phase-spooling = Engines spooling: { $countdown } s
wf-flight-phase-climbing = Climbing
wf-flight-phase-descending = Descending
wf-flight-phase-hovering = Hovering
wf-flight-phase-lift-lost = LIFT LOST: falling
wf-flight-ready = READY FOR LIFTOFF
wf-flight-not-ready = NOT READY
wf-flight-check-lift = Lift
wf-flight-check-lift-value = { $percent }% / 100%
wf-flight-check-power = Power
wf-flight-check-power-ok = OK, { $power } kW
wf-flight-check-power-short = Short { $short } kW
wf-flight-check-hold = Held: { $reason }
wf-flight-check-cargo = Cargo
wf-flight-check-cargo-value = { $tonnes } t
wf-flight-fix-convert = Fix: convert { $count } { $count ->
        [one] thruster
       *[other] thrusters
    } to landing thrusters.
wf-flight-fix-add = Fix: add { $count } more landing { $count ->
        [one] thruster
       *[other] thrusters
    }.
wf-flight-fix-convert-add = Fix: convert all { $convert } ordinary thrusters and add { $add } landing { $add ->
        [one] thruster
       *[other] thrusters
    }.
wf-flight-fix-power = Fix: add { $short } kW of power supply.
wf-flight-hint-grounded = Hold ascend or press Liftoff to take off.
wf-flight-hint-air = Ascend and descend change altitude.
wf-flight-hint-orbit = Hold descend to enter the atmosphere.

## Descent gates
wf-flight-landing-blocked = Can't set down here. There's something solid under the hull.
wf-flight-busy = This hull is already under way.
wf-flight-descent-refused = Atmospheric entry refused.
wf-flight-lift-warning = Lift is only { $percent }%. Use Enter atmosphere on the console to descend anyway.
wf-flight-power-warning = Not enough power to hover. Use Enter atmosphere on the console to descend anyway.

## Confirm dialog
wf-flight-confirm-title = Atmospheric entry
wf-flight-confirm-descend = Descend
wf-flight-confirm-abort = Stay in orbit
wf-flight-confirm-text = { $planet }: lift [color={ $colour }]{ $percent }%[/color] of the 100% needed to hover.
    Hover power: { $power } kW.
wf-flight-confirm-low-lift = Not enough lift: the hull will sink and can't climb back out.
wf-flight-confirm-falling = Far too little lift: the hull will fall and crash.
wf-flight-confirm-power-deficit = Not enough power: thrusters will cut out.

## Thruster examination
wf-thruster-atmosphere-mode = Atmosphere mode: half thrust. A landing conversion restores full thrust.
wf-thruster-landing-converted = Landing conversion: full thrust in atmosphere.

## Landing thruster conversion kit
wf-landing-kit-wrong-target = Use the kit on a linear thruster, not a gyroscope.
wf-landing-kit-not-anchored = Anchor the thruster before converting it.
wf-landing-kit-converted = Landing conversion installed: full thrust in atmosphere.

## Flight alarm situation codes
wf-alert-lift-lost = Lift Lost
wf-alert-lift-lost-desc = The hull no longer has the lift to hold itself up.
wf-alert-lift-lost-announcement = Caution: lift lost. Hull is descending.

wf-alert-dont-sink = Don't Sink
wf-alert-dont-sink-desc = Out of orbit and sinking into the atmosphere.
wf-alert-dont-sink-announcement = Don't sink. { $ship } is out of orbit and going down.

wf-alert-sink-rate = Sink Rate
wf-alert-sink-rate-desc = Descending faster than the hull can arrest.
wf-alert-sink-rate-announcement = Sink rate. Sink rate.

wf-alert-terrain = Terrain
wf-alert-terrain-desc = The ground is coming up.
wf-alert-terrain-announcement = Terrain. Terrain ahead.

wf-alert-too-low-terrain = Too Low - Terrain
wf-alert-too-low-terrain-desc = Below the last safe layer with no lift.
wf-alert-too-low-terrain-announcement = Too low. Terrain.

wf-alert-pull-up = Pull Up
wf-alert-pull-up-desc = Seconds from impact.
wf-alert-pull-up-announcement = Pull up. Pull up.

## Orbit decay
wf-shuttle-console-orbit-stable = Orbit: stable
wf-shuttle-console-orbit-decaying = Orbit decaying: { $seconds } s

wf-alert-orbit-decay = Orbit Decay
wf-alert-orbit-decay-desc = Station-keeping is lost; the hull is falling out of orbit.
wf-alert-orbit-decay-announcement = Orbit decaying: station-keeping lost. { $ship } is falling out of orbit.
wf-alert-orbit-decay-countdown = Orbit decaying: station-keeping lost, { $seconds } seconds to atmospheric entry.

wf-landing-kit-already-converted = This thruster already has a landing conversion.
wf-landing-kit-out-of-reach = Move closer to install the landing conversion.

wf-crash-apc-fault-examine = The power regulator is damaged and intermittently cuts out. Recalibrate it with a multitool.
wf-crash-apc-fault-repaired = You recalibrate the damaged APC regulator. Its output is stable again.
