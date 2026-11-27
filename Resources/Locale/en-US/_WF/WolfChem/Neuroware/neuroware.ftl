ent-WFNeurowareStick = data stick
    .desc = A small data stick with an unlabelled port and a sampling pin. This one looks blank.

ent-WFNeurowareStickWhiteline = data stick (Whiteline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickNullLoop = data stick (Null Loop)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickBurst = data stick (Burst)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickRedline = data stick (Redline)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickDamper = data stick (Damper)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickChoir = data stick (Choir.sig)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickHalo = data stick (Halo)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

ent-WFNeurowareStickIdle = data stick (Idle)
    .desc = A data stick with a hand-written label. The port only fits a synthetic's maintenance jack.

wf-neuroware-stick-blank = The stick is blank.
wf-neuroware-stick-spent = The stick has no loads left.
wf-neuroware-stick-examine = It holds [color=violet]{ $program }[/color] at { $integrity }% integrity, with { $loads ->
    [one] one load
   *[other] { $loads } loads
} left.
wf-neuroware-no-port = { CAPITALIZE(THE($target)) } has no port for this.
wf-neuroware-load-target = { CAPITALIZE(THE($user)) } is plugging something into your maintenance jack!
wf-neuroware-copy-done = You copy the program onto the blank stick.
wf-neuroware-copy-fail = You need a loaded stick and a blank one.
wf-neuroware-encode-done = The stick reads { THE($source) } in and writes it down as { $program }.
wf-neuroware-encode-short = There is too little in { THE($source) } for the stick to read.

## Synthetic HUD lines

wf-neuroware-advice = A panel reboot clears running software.
wf-neuroware-crash-fault = POST-RUN THROTTLE ACTIVE
wf-neuroware-habit-loop = HABIT LOOP: process not found
wf-neuroware-false-fault-1 = SECTOR 0 CHECKSUM MISMATCH
wf-neuroware-false-fault-2 = COOLANT LOOP RUPTURE
wf-neuroware-false-fault-3 = MOTOR CORTEX UNRESPONSIVE
