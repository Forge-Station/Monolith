# The Wolfmed diagnostic panel (Content.Client/_WF/Wolfmed/Medical): the health analyzer's tabs, its organ and
# chemical readouts, and the wound findings it builds.

health-analyzer-window-entity-vital-damage-text = Vital Damage:
health-analyzer-window-damage-tab = Damage
health-analyzer-window-wounds-tab = Wounds
health-analyzer-window-organs-tab = Organs
health-analyzer-window-chemicals-tab = Chemicals
health-analyzer-window-organs-unavailable = Organ data unavailable.
health-analyzer-window-organ-health = { $percent } %
health-analyzer-window-chemicals-unavailable = Chemical data unavailable.
health-analyzer-window-chemicals-no-vessels = No metabolic vessels detected.
health-analyzer-window-solution-bloodstream = Bloodstream
health-analyzer-window-solution-metabolites = Chemicals
health-analyzer-window-solution-stomach = Stomach
health-analyzer-window-solution-lung = Lungs
health-analyzer-window-solution-empty = No reagents detected
health-analyzer-window-solution-reagent = { $reagent }: { $quantity } u
health-analyzer-wound-diagnostics-title = Status
health-analyzer-wound-diagnostics-inactive = No contact with patient.
health-analyzer-wound-diagnostics-unavailable = Diagnostics unavailable for this patient.
health-analyzer-wound-blood-level-dangerous = The patient has a [color=red]dangerously low[/color] blood level.
health-analyzer-wound-count = x{ $count }

# P4-D25: the fracture grade and treatment, which Onyx carries in the payload but never renders. The grade word
# comes from the fracture-grade-* keys.
health-analyzer-wound-fracture-short = fracture: { $grade }
health-analyzer-wound-fracture-treated-short = fracture: { $grade } ({ $treatment })
health-analyzer-wound-fracture-treatment-reduced = reduced
health-analyzer-wound-fracture-treatment-mended = mended

# P5-5, W7: the mechanical column. A chassis gets these in place of the organic short labels and clotting phases.
health-analyzer-wound-bleeding-short-mechanical = fluid leak
health-analyzer-wound-fracture-short-frame = frame damage: { $grade }
health-analyzer-wound-fracture-treated-short-frame = frame damage: { $grade } ({ $treatment })
health-analyzer-wound-pain-short-mechanical = fault signal: { $pain }
health-analyzer-wound-clotting-inprogress-mechanical = sealant setting
health-analyzer-wound-clotting-complete-mechanical = leak sealed
health-analyzer-wound-clotting-mixed-mechanical = partially sealed
