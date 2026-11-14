# Guidebook text for Wolfmed's own reagent effects that have no Onyx source.

# TakeStaminaDamage: Onyx's effect overrides no guidebook text, but ReagentEffectGuidebookText is abstract here and
# returning null would hide the effect from the guidebook.
reagent-effect-guidebook-take-stamina-damage =
    { $chance ->
        [1] Causes
        *[other] cause
    } { NATURALFIXED($amount, 2) } stamina damage.
