# The autodoc pod. S.A.M.'s spoken lines live in autodoc-voice.ftl, which the voice generator writes.

wolfmed-autodoc-verb-enter = Climb in
wolfmed-autodoc-verb-eject = Eject occupant

wolfmed-autodoc-examine-idle = The status panel reads STANDBY.
wolfmed-autodoc-examine-preparing = The status panel reads PREPARING.
wolfmed-autodoc-examine-step = The status panel reads OPERATING. The lid is locked.
wolfmed-autodoc-examine-waiting = The status panel reads WAITING FOR MATERIAL. The parts tray is open for the limb, organ or tool it asked for.
wolfmed-autodoc-examine-paused = The status panel reads HOLDING.
wolfmed-autodoc-examine-complete = The status panel reads PROCEDURE COMPLETE.
wolfmed-autodoc-examine-faulted = The status panel reads FAULT. Nothing is moving.
wolfmed-autodoc-examine-emagged = Something in the status panel is scrolling far too quickly.

wolfmed-autodoc-status-idle = STANDBY
wolfmed-autodoc-status-preparing = PREPARING
wolfmed-autodoc-status-step = OPERATING
wolfmed-autodoc-status-waiting = WAITING FOR MATERIAL
wolfmed-autodoc-status-waiting-clothing = WAITING: CLOTHING
wolfmed-autodoc-status-transfusing = TRANSFUSING
wolfmed-autodoc-status-paused = HOLDING
wolfmed-autodoc-status-complete = PROCEDURE COMPLETE
wolfmed-autodoc-status-faulted = FAULT
wolfmed-autodoc-status-anaesthesia = ANAESTHESIA { $seconds }s, SEDATION { $percent }%

wolfmed-autodoc-popup-clothing = S.A.M. cannot reach your skin. Take the clothing off now, or it gets cut.

wolfmed-autodoc-radio-critical = AUTODOC { $location }: PATIENT VITALS CRITICAL. OPERATOR REQUESTED.

wolfmed-autodoc-requirement-part = { $side } { $part }
wolfmed-autodoc-requirement-organ = { $organ }
wolfmed-autodoc-requirement-item = item
wolfmed-autodoc-requirement-reagent = { $units }u { $role }
wolfmed-autodoc-requirement-line = { $what }: { $status }
wolfmed-autodoc-requirement-loaded = LOADED
wolfmed-autodoc-requirement-waiting = WAITING

wolfmed-autodoc-symmetry-left = left
wolfmed-autodoc-symmetry-right = right
wolfmed-autodoc-symmetry-none = whole

wolfmed-autodoc-parttype-other = part
wolfmed-autodoc-parttype-torso = torso
wolfmed-autodoc-parttype-head = head
wolfmed-autodoc-parttype-arm = arm
wolfmed-autodoc-parttype-hand = hand
wolfmed-autodoc-parttype-leg = leg
wolfmed-autodoc-parttype-foot = foot
wolfmed-autodoc-parttype-tail = tail

# The organ an insert surgery asks for, keyed by the organ component the surgery's condition names.
wolfmed-autodoc-organ-brain = brain
wolfmed-autodoc-organ-borgbrain = positronic brain
wolfmed-autodoc-organ-heart = heart
wolfmed-autodoc-organ-lung = lungs
wolfmed-autodoc-organ-liver = liver
wolfmed-autodoc-organ-stomach = stomach
wolfmed-autodoc-organ-eyes = eyes

wolfmed-autodoc-reagent-anaesthetic = anaesthetic
wolfmed-autodoc-reagent-antibiotic = antibiotic
wolfmed-autodoc-reagent-antiseptic = antiseptic
wolfmed-autodoc-reagent-fluid = fluid
wolfmed-autodoc-reagent-oxygen = oxygen

wolfmed-autodoc-reservoir-empty = empty

wolfmed-autodoc-part-head = head
wolfmed-autodoc-part-torso = torso
wolfmed-autodoc-part-groin = groin
wolfmed-autodoc-part-leftarm = left arm
wolfmed-autodoc-part-lefthand = left hand
wolfmed-autodoc-part-rightarm = right arm
wolfmed-autodoc-part-righthand = right hand
wolfmed-autodoc-part-leftleg = left leg
wolfmed-autodoc-part-leftfoot = left foot
wolfmed-autodoc-part-rightleg = right leg
wolfmed-autodoc-part-rightfoot = right foot

wolfmed-autodoc-program-base = base surgical library
wolfmed-autodoc-program-transplant = transplant
wolfmed-autodoc-program-limb = limb
wolfmed-autodoc-program-neuro = neuro
wolfmed-autodoc-program-cavity = cavity

wolfmed-autodoc-slot-tray = Parts tray
wolfmed-autodoc-slot-disk = Program disk
wolfmed-autodoc-slot-module = Cardiac module
wolfmed-autodoc-slot-autofix = Autofix module
wolfmed-autodoc-slot-beaker = Reservoir

wolfmed-autodoc-window-title = S.A.M. - Surgical Automation Module
wolfmed-autodoc-window-procedures = PROCEDURES
wolfmed-autodoc-window-queue = QUEUE
wolfmed-autodoc-window-reservoir = RESERVOIR
wolfmed-autodoc-window-anaesthesia = Anaesthesia
wolfmed-autodoc-window-start = Start
wolfmed-autodoc-window-pause = Pause
wolfmed-autodoc-window-abort = Abort
wolfmed-autodoc-window-eject = Emergency eject
wolfmed-autodoc-window-step = STEP: { $step }
wolfmed-autodoc-window-disk = PROGRAM DISK: { $disk }
wolfmed-autodoc-window-disk-none = NONE
wolfmed-autodoc-window-module-installed = DEFIB MODULE: INSTALLED
wolfmed-autodoc-window-module-missing = DEFIB MODULE: NOT INSTALLED
wolfmed-autodoc-window-tray = TRAY: { $item }
wolfmed-autodoc-window-tray-empty = EMPTY
wolfmed-autodoc-window-locked = I DO NOT KNOW THAT PROCEDURE. INSERT THE PROGRAM DISK.

guide-entry-wolfmed-autodoc = Autodoc

# Playtest 3, S.A.M. round
wolfmed-autodoc-no-climb = The pod is for lying in, not on.
wolfmed-autodoc-status-waiting-garment = WAITING: { $item } ON { $slot }
wolfmed-autodoc-organ-breastsorgan = breasts
wolfmed-autodoc-organ-penisorgan = penis
wolfmed-autodoc-organ-testiclesorgan = testicles
wolfmed-autodoc-organ-vaginaorgan = vagina
wolfmed-autodoc-organ-womborgan = womb

# Playtest 4, IV: the blood reservoir.
wolfmed-autodoc-slot-blood = Blood reservoir
wolfmed-autodoc-status-no-blood = NO BLOOD LOADED
wolfmed-autodoc-reservoir-blood = blood: { $name } x{ $count }
wolfmed-autodoc-reservoir-blood-empty = blood: empty
wolfmed-autodoc-reservoir-blood-opened = blood: opened pack
wolfmed-autodoc-status-antibiotics = ANTIBIOTICS
wolfmed-autodoc-status-no-antibiotic = NO ANTIBIOTIC LOADED
