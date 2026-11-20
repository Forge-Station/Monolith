# Core vehicle features (overcharge, horn, spotlight) and the hardpoint window.
ent-WFActionVehicleOvercharge = Overcharge engine
    .desc = Pushes the engine past its limit for a few seconds of extra top speed. It needs time to recover afterwards.

rmc-hardpoint-ui-title = Hardpoints
rmc-hardpoint-ui-empty-slot = Empty
rmc-hardpoint-ui-removing = Removing...
rmc-hardpoint-ui-integrity = {$current}/{$max} ({$percent}%)

wf-vehicles-hardpoint-ui-heading = Vehicle Hardpoints
wf-vehicles-hardpoint-ui-frame-integrity = Frame Integrity
wf-vehicles-hardpoint-ui-part-integrity = {$current}/{$max}
wf-vehicles-hardpoint-ui-open-slot = Open: {$slot}
wf-vehicles-hardpoint-ui-nested-slot = {$parent} / {$slot}

cmd-rmc_vehicle_hardpoint-desc = Toggles the vehicle hardpoint overlay: turret anchors and offsets.
cmd-rmc_vehicle_hardpoint-help = Usage: {$command}

wf-vehicles-entity-category = Vehicles

wf-vehicle-plow-working = The plow bites into the structure. Keep pushing forward!
wf-vehicle-plow-indestructible = The plow cannot break through this structure.

ent-WFActionVehicleSteeringMode = Switch steering
    .desc = Switch between free steering and fixed four-way driving.
ent-WFActionVehicleScanner = Scanner
    .desc = Open the vehicle's mass scanner.
wf-vehicle-scanner-position = Position: { $x }, { $y }
wf-vehicle-steering-free = Steering: free.
wf-vehicle-steering-fixed = Steering: four-way.
wf-vehicle-steering-no-room = No room to square the vehicle up.
