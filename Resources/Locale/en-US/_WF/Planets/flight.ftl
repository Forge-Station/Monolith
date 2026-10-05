## Shuttle console: entering the atmosphere
wf-shuttle-console-enter-atmosphere = Enter atmosphere: { $planet }
wf-shuttle-console-lift-ratio = Atmo lift { $ratio } : 1
wf-shuttle-console-atmosphere-power = Atmo power { $power } kW
wf-shuttle-console-atmosphere-power-deficit = Power deficit
wf-shuttle-console-lift-power-deficit = Power deficit | Lift { $ratio }
wf-shuttle-console-atmosphere-mode-tooltip = Atmosphere mode:
    Ordinary thrusters: 50% thrust, 3× power.
    Converted thrusters: normal thrust and power.

## Descent gates
wf-flight-descend-use-button = Use the console's "enter atmosphere" control to leave orbit.
wf-flight-busy = This hull is already under way.
wf-flight-descent-refused = Atmospheric entry refused.
wf-flight-lift-warning = Atmospheric lift is { $ratio } : 1. Confirm descent to proceed.

## Confirm dialog
wf-flight-confirm-title = Atmospheric entry
wf-flight-confirm-descend = Descend
wf-flight-confirm-abort = Stay in orbit
wf-flight-confirm-text = { $planet }: atmo lift [color={ $colour }]{ $ratio } : 1[/color].
    Atmo power demand: { $power } kW.
wf-flight-confirm-low-lift = Low lift: cannot climb back out.
wf-flight-confirm-falling = Low lift: hull will fall.
wf-flight-confirm-power-deficit = Power deficit: lift may fail.

## Thruster examination
wf-thruster-atmosphere-mode = Atmosphere Mode: 50% thrust, 3x power draw.
wf-thruster-landing-converted = Landing conversion: full thrust and normal power draw in atmosphere.

## Landing thruster conversion kit
wf-landing-kit-wrong-target = Use the kit on a linear thruster, not a gyroscope.
wf-landing-kit-not-anchored = Anchor the thruster before converting it.
wf-landing-kit-converted = Landing conversion installed: full thrust and normal power use in atmosphere.

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
