# HUD: the synthetic diagnostics readout a mechanical body sees instead of the organic vignette. Every
# string is drawn in a monospace face in ALL CAPS, so they are written that way here: the overlay does not
# transform them. Fault lines are kept to about sixteen characters so the right-hand column stays aligned.

# Block headings.
wolfmed-synthetic-system = SYSTEM
wolfmed-synthetic-diagnostics = DIAGNOSTICS

# SYSTEM block row labels. The overlay draws each gauge's bar at a fixed column and its value right-aligned, so a
# label is only the name (at most eight characters).
wolfmed-synthetic-row-integrity = CHASSIS
wolfmed-synthetic-row-power = POWER
wolfmed-synthetic-row-fluid = FLUID
wolfmed-synthetic-row-servo = SERVO
wolfmed-synthetic-row-sensor = SENSOR
wolfmed-synthetic-row-core = CORE
wolfmed-synthetic-row-core-temp = { $value } K
wolfmed-synthetic-row-faults = FAULTS

# The idle status line, cycled slowly while nothing is wrong.
wolfmed-synthetic-idle-1 = THERMAL NOMINAL
wolfmed-synthetic-idle-2 = GYRO CALIBRATED
wolfmed-synthetic-idle-3 = FIRMWARE 4.2.1
wolfmed-synthetic-idle-4 = NO FAULTS LOGGED
wolfmed-synthetic-idle-5 = BUS PARITY OK
wolfmed-synthetic-idle-6 = COOLANT CIRCULATING

# Severity tags.
wolfmed-synthetic-tag-info = INFO
wolfmed-synthetic-tag-warn = WARN
wolfmed-synthetic-tag-crit = CRIT
wolfmed-synthetic-tag-fail = FAIL

# Part labels for a fault line.
wolfmed-synthetic-part-chassis = CHASSIS
wolfmed-synthetic-part-head = HEAD
wolfmed-synthetic-part-torso = TORSO
wolfmed-synthetic-part-groin = FRAME
wolfmed-synthetic-part-left-arm = LEFT ARM
wolfmed-synthetic-part-right-arm = RIGHT ARM
wolfmed-synthetic-part-left-hand = LEFT HAND
wolfmed-synthetic-part-right-hand = RIGHT HAND
wolfmed-synthetic-part-left-leg = LEFT LEG
wolfmed-synthetic-part-right-leg = RIGHT LEG
wolfmed-synthetic-part-left-foot = LEFT FOOT
wolfmed-synthetic-part-right-foot = RIGHT FOOT

# Fault lines, named by the syntheticHudLine prototypes.
wolfmed-synthetic-line-dent = PANEL DEFORMED
wolfmed-synthetic-line-breach = FLUID LEAK
wolfmed-synthetic-line-short-circuit = WIRING SHORTED
wolfmed-synthetic-line-servo = SERVO DAMAGE
wolfmed-synthetic-line-overheating = THERMAL OVERLOAD
wolfmed-synthetic-line-chassis-breach = CHASSIS BREACH
wolfmed-synthetic-line-frame = FRAME DAMAGE
wolfmed-synthetic-line-fallback = UNCLASSED FAULT
wolfmed-synthetic-line-low-fluid = FLUID RESERVE LOW
wolfmed-synthetic-line-shutdown = POWER BUS DOWN
wolfmed-synthetic-line-core-degraded = CORE DEGRADED
wolfmed-synthetic-line-core-offline = CORE UNRESPONSIVE
wolfmed-synthetic-line-actuator-offline = ACTUATOR OFFLINE
wolfmed-synthetic-line-limb-detached = LIMB DETACHED
wolfmed-synthetic-line-embedded = FOREIGN OBJECT

# One-line advice for the worst fault. The analyzer's own advice keys are whole paragraphs; these are the
# same instruction cut to what fits under the SYSTEM block.
wolfmed-synthetic-advice = ADVICE: { $advice }
wolfmed-synthetic-advice-dent = BEAT OUT PANEL
wolfmed-synthetic-advice-breach = WELD BREACH
wolfmed-synthetic-advice-short-circuit = RESPLICE WIRING
wolfmed-synthetic-advice-servo = REPLACE SERVO
wolfmed-synthetic-advice-overheating = SHED HEAT
wolfmed-synthetic-advice-chassis-breach = SEEK AUTODOC
wolfmed-synthetic-advice-frame = SEEK MAINTENANCE
wolfmed-synthetic-advice-fallback = RUN FULL DIAGNOSTIC
wolfmed-synthetic-advice-low-fluid = REFILL FLUID
wolfmed-synthetic-advice-shutdown = RESTORE POWER
wolfmed-synthetic-advice-core = SEEK AUTODOC
wolfmed-synthetic-advice-limb = REATTACH LIMB
wolfmed-synthetic-advice-embedded = EXTRACT OBJECT

# Banners across the top edge, worst first.
wolfmed-synthetic-banner-integrity = STRUCTURAL INTEGRITY { $value } - SEEK MAINTENANCE
wolfmed-synthetic-banner-downed = MOBILITY LOST - CRAWL MODE
wolfmed-synthetic-banner-standby = STANDBY
wolfmed-synthetic-banner-reboot = REBOOT PENDING...

# M1a: the banner names what holds the chassis down instead of a blanket STANDBY (plan §5.6).
wolfmed-synthetic-cause-pain = MOBILITY LOST: FRAME DAMAGE
wolfmed-synthetic-cause-legs = MOBILITY LOST: ACTUATORS OFFLINE
wolfmed-synthetic-cause-oil = HYDRAULIC PRESSURE LOW
wolfmed-synthetic-cause-shutdown = { $source ->
    [Pump] COOLANT PUMP OFFLINE: SHUTDOWN
   *[other] CELL EMPTY: SHUTDOWN. AWAITING POWER.
}

wolfmed-synthetic-banner-panic = KERNEL PANIC
wolfmed-synthetic-panic-dump = SEGMENTATION FAULT AT 0x{ $address }
wolfmed-synthetic-death-banner = CORE OFFLINE
wolfmed-synthetic-death-banner-sub = No further diagnostics. The chassis has stopped reporting.

# M2 (OD10): a repaired core, cosmetic; no trauma on a machine.
wolfmed-synthetic-line-core-restored = CORE RESTORED: DIAGNOSTICS
wolfmed-synthetic-advice-core-restored = DIAGNOSTICS RUNNING. NO ACTION NEEDED
