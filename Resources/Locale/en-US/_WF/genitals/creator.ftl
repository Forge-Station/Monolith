# Creator anatomy tab (WolfgateGenitalEditor). Clinical wording.
# Cup and testicle size words are shared with examine.ftl (wf-genitals-cup-*, wf-genitals-testicles-size-N).

wf-genitals-tab = Anatomy

## Gates, shown instead of the controls.
wf-genitals-gate-disabled = Anatomy options are turned off on this server.
wf-genitals-gate-offline = Anatomy options depend on your consent settings, which are only available while connected to a server.
wf-genitals-gate-consent = Anatomy options are part of adult content. Turn on Adult content in consent settings to use them.
wf-genitals-gate-age = Anatomy options are only available for characters aged { $age } or older.
wf-genitals-gate-species = This species has no configurable anatomy.
wf-genitals-open-consent = Open consent settings

## Preview. Only changes the creator's preview, never the character.
wf-genitals-preview = Preview
wf-genitals-preview-worn = As worn
wf-genitals-preview-underwear = Underwear only
wf-genitals-preview-nude = Unclothed
wf-genitals-preview-arousal = Arousal preview
wf-genitals-preview-arousal-none = None
wf-genitals-preview-arousal-partial = Partial
wf-genitals-preview-arousal-full = Full

## Typical anatomy.
wf-genitals-preset-menu = Typical anatomy ({ $species })
wf-genitals-preset-male = Typical male
wf-genitals-preset-female = Typical female
wf-genitals-preset-clear = Clear
wf-genitals-preset-suggested = Matches this character's sex.
wf-genitals-preset-confirm = Replace the current anatomy settings?
wf-genitals-preset-apply = Replace
wf-genitals-preset-cancel = Cancel

## Reveal mode and undergarments.
wf-genitals-reveal = Reveal anatomy when
wf-genitals-reveal-undergarments = Undergarments are removed
wf-genitals-reveal-clothing = Clothing is removed
wf-genitals-reveal-clothing-tooltip = Anatomy is drawn over undergarments once clothing is removed.
wf-genitals-no-undergarments = No undergarments selected: anatomy shows as soon as clothing is removed.
wf-genitals-no-undergarments-species = This species has no undergarments, so nothing covers anatomy once clothing is removed. Consider the Always hidden visibility.
wf-genitals-choose-undergarments = Choose undergarments

## Organ cards.
wf-genitals-include = Include
wf-genitals-shape = Shape
wf-genitals-penis = Penis
wf-genitals-length = Length
wf-genitals-length-value = { $cm } cm (size { $step })
wf-genitals-sheath = Sheath
wf-genitals-sheath-none = None
wf-genitals-sheath-sheath = Sheath
wf-genitals-sheath-slit = Slit
wf-genitals-sheath-unavailable = This shape does not allow this option.
wf-genitals-sheath-colour = Sheath colour
wf-genitals-testicles = Testicles
wf-genitals-testicles-type = Type
wf-genitals-testicles-none = None
wf-genitals-testicles-external = External
wf-genitals-testicles-internal = Internal
wf-genitals-testicles-internal-note = Internal testicles are neither drawn nor described.
wf-genitals-testicle-size = Size
wf-genitals-vagina = Vagina
wf-genitals-womb = Womb
wf-genitals-womb-present = Present (internal)
wf-genitals-breasts = Breasts
wf-genitals-cup = Cup
wf-genitals-lactation = Lactation
wf-genitals-lactating = Lactating
wf-genitals-udders-note = Size does not change the sprite of this shape.
wf-genitals-colour = Colour
wf-genitals-match-skin = Match skin tone
wf-genitals-visibility = Visibility
wf-genitals-visibility-normal = Normal
wf-genitals-visibility-normal-tooltip = Shown when not covered by clothing or undergarments.
wf-genitals-visibility-hidden = Always hidden
wf-genitals-visibility-hidden-tooltip = Never drawn or described to others. Can be changed during a round.
wf-genitals-visibility-through = Show through clothing
wf-genitals-visibility-through-tooltip = Drawn over clothing. Can be changed during a round.
wf-genitals-load-failed = Stored anatomy settings could not be fully read. They are kept unless you change them.

## Save confirmation for characters that cannot have anatomy.
wf-genitals-save-confirm-title = Anatomy settings
wf-genitals-save-confirm-text = Saving at this age removes the anatomy settings.
wf-genitals-save-confirm-save = Save
wf-genitals-save-confirm-cancel = Cancel

## Warnings for the validator issues the creator shows (GenitalIssue); the age, species and load gates have their own lines.
wf-genitals-warning-UnknownShape = A stored shape no longer exists, so that organ was removed.
wf-genitals-warning-WrongSlotShape = A stored shape does not fit its organ, so that organ was removed.
wf-genitals-warning-SheathNotAllowed = This shape does not allow the chosen sheath, so none is used. It returns when a shape that allows it is picked.
wf-genitals-warning-TesticlesWithoutPenis = Testicles need a penis, so they were removed.
wf-genitals-warning-WombWithoutVagina = A womb needs a vagina, so it was removed.
wf-genitals-warning-SizeClamped = The size was out of range and has been adjusted.
wf-genitals-warning-CupClamped = The cup size was out of range and has been adjusted.
wf-genitals-warning-LengthClamped = The length was out of range and has been adjusted.
wf-genitals-warning-InvalidValue = Some stored values were invalid and have been reset.
wf-genitals-warning-PregnancyMarkingWithoutWomb = A pregnancy marking is selected, but no womb is configured.
