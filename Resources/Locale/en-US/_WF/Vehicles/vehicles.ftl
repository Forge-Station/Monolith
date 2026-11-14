# Console commands that toggle the ground vehicle debug overlays (admin only).
cmd-rmc_vehicle_debug-desc = Toggles the vehicle debug overlay: fixtures, facing and tested tiles.
cmd-rmc_vehicle_debug-help = Usage: {$command}
cmd-rmc_vehicle_collision-desc = Toggles the vehicle collision overlay: probes and what blocked them.
cmd-rmc_vehicle_collision-help = Usage: {$command}
cmd-rmc_vehicle_movement-desc = Toggles the vehicle movement overlay: how each drive step was decided.
cmd-rmc_vehicle_movement-help = Usage: {$command}

wf-vehicles-debug-admin-only = You must be an admin to use this command.
wf-vehicles-debug-overlay-on = Vehicle {$overlay} overlay: enabled
wf-vehicles-debug-overlay-off = Vehicle {$overlay} overlay: disabled

# Entity names.
ent-WFVehicleTestChassis = test vehicle chassis
    .desc = A bare box on a grid mover, for tests.
ent-WFVehicleTestPusher = test vehicle pusher
    .desc = The test chassis, strong enough to shove other vehicles.
