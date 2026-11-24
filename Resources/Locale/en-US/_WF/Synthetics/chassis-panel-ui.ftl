# Synthetics: the chassis panel window and the bay-open overlay. ALL CAPS, like the synthetic HUD.
wf-chassis-ui-title = CHASSIS PANEL
wf-chassis-ui-tab-parts = PARTS
wf-chassis-ui-tab-components = COMPONENTS
wf-chassis-ui-controls = CONTROLS

wf-chassis-ui-col-part = PART
wf-chassis-ui-col-state = STATE
wf-chassis-ui-col-integrity = INTEGRITY
wf-chassis-ui-col-load = LOAD
wf-chassis-ui-col-faults = FAULTS
wf-chassis-ui-col-slot = SLOT
wf-chassis-ui-col-component = COMPONENT
wf-chassis-ui-col-health = HEALTH
wf-chassis-ui-col-band = STATUS
wf-chassis-ui-col-actions = ACTIONS
wf-chassis-ui-doll-hint = SELECT A LIMB

wf-chassis-ui-state-detached = DETACHED
wf-chassis-ui-state-isolated = ISOLATED
wf-chassis-ui-state-loose = LOOSE
wf-chassis-ui-state-offline = OFFLINE
wf-chassis-ui-state-unpowered = UNPOWERED
wf-chassis-ui-state-ok = OK

wf-chassis-ui-none = --
wf-chassis-ui-empty = EMPTY
wf-chassis-ui-vital = VITAL
wf-chassis-ui-isolate = ISOLATE
wf-chassis-ui-restore = RESTORE
wf-chassis-ui-detach = DETACH
wf-chassis-ui-disconnect = DISCONNECT
wf-chassis-ui-confirm = CONFIRM
wf-chassis-ui-bay-open = OPEN BAY
wf-chassis-ui-bay-close = CLOSE BAY
wf-chassis-ui-reboot = REBOOT
wf-chassis-ui-overclock-off = OVERCLOCK: OFF
wf-chassis-ui-overclock-on = OVERCLOCK: ON

wf-chassis-ui-bay-banner = CHASSIS BAY OPEN - COMPONENTS EXPOSED
wf-chassis-ui-overclock-tag = OVERCLOCK

wf-chassis-band-normal = NOMINAL
wf-chassis-band-impaired = IMPAIRED
wf-chassis-band-failed = FAILED
wf-chassis-band-charge = CHARGE

wf-chassis-slot-eyes = OPTICS
wf-chassis-slot-posbrain = POSITRONIC CORE
wf-chassis-slot-brain = CCU
wf-chassis-slot-pump = COOLANT PUMP
wf-chassis-slot-heart = PUMP
wf-chassis-slot-lungs = COOLING LOOP
wf-chassis-slot-stomach = CONVERTER
wf-chassis-slot-liver = FILTER
wf-chassis-slot-kidneys = SCRUBBER
wf-chassis-slot-ears = AUDIO PACK
wf-chassis-slot-tongue = VOCODER
wf-chassis-slot-appendix = SPARE
wf-chassis-slot-battery = POWER CELL
wf-chassis-slot-radcollector = RAD COLLECTOR

# Hover tips. Sentences, not readout caps.
wf-chassis-ui-tip-state-ok = Running within rating.
wf-chassis-ui-tip-state-offline = Too damaged to run. Weld the chassis, replace the servo, or isolate it to stop the sensor load.
wf-chassis-ui-tip-state-unpowered = The cell is empty, so only one arm has power. Route power to this arm from your own menu, or seat a charged cell.
wf-chassis-ui-tip-state-isolated = Servos and sensors cut from the panel. The limb does nothing and reports no pain. RESTORE to power it.
wf-chassis-ui-tip-state-loose = Seated by hand. Slow and clumsy until a cable coil recouples it.
wf-chassis-ui-tip-state-detached = The limb is off the chassis. Click the limb on the body to seat it; it runs loose until a cable coil recouples it.

wf-chassis-ui-tip-load = Pain this part's damage sensors put on the chassis, as a share of what drops it. Repair the wounds, or isolate the part to mute it.
wf-chassis-ui-tip-faults-none = No faults logged.
wf-chassis-ui-tip-fault = { $fault } - { $advice }

wf-chassis-ui-tip-band-normal = Running within rating.
wf-chassis-ui-tip-band-impaired = Worn or damaged and running badly. Repair it before it fails.
wf-chassis-ui-tip-band-failed = Dead. The chassis loses whatever this component does until it is replaced or repaired.
wf-chassis-ui-tip-band-charge = Charge left in the power cell. At zero the chassis drops to low power: crawl speed and one arm, until a charged cell is seated.
wf-chassis-ui-tip-vital = Pulling this stops the chassis (pump) or ends the mind (core).

wf-chassis-ui-tip-isolate = Cuts the limb's servos and sensors. The limb does nothing and its wounds put no pain on the chassis. RESTORE reverses it.
wf-chassis-ui-tip-restore = Powers the limb's servos and sensors again. Its wounds count towards the sensor load once more.
wf-chassis-ui-tip-detach = Releases the limb's coupling so it comes off in one piece. Click it on the body to seat it again; it runs loose until a cable coil recouples it.
wf-chassis-ui-tip-disconnect = Pulls the component out of the chassis. Needs the bay open. A vital one asks you to confirm.
wf-chassis-ui-tip-bay-open = Opens the torso plating. Takes 3 seconds, 6 on yourself. Components can be pulled and seated by hand, but the chassis is slower and its components are exposed while it is open.
wf-chassis-ui-tip-bay-close = Shuts the torso plating. Takes 3 seconds, 6 on yourself. Speed returns to normal and the components are covered again.
wf-chassis-ui-tip-reboot = Restarts the chassis. It is down for 4 seconds and uses 10% of the charge, and the sensor load and lockups are cleared. Once every 2 minutes. Click twice to confirm.
wf-chassis-ui-tip-overclock-off = Runs the servos 25% faster for 3 times the drain. The chassis heats, throws sparks and short circuits as it does, and cuts out at 15% charge or when the torso overheats.
wf-chassis-ui-tip-overclock-on = Overclock is running: 25% faster, 3 times the drain. Click to stop it and let the chassis cool.

# The radiation collector and the built-in radiation meter.
wf-chassis-ui-rad-collector = RAD COL
wf-chassis-ui-rad-collector-none = NONE
wf-chassis-ui-radiation = RAD {$value} r/s
wf-chassis-ui-tip-rad-collector = How full the radiation collector is. Full, more radiation strains it and leaks into the core; pull it through the bay to let it vent. NONE: missing or burnt out, and the core takes all of it.
wf-chassis-ui-tip-radiation = Radiation reaching the chassis right now, in rads a second.
