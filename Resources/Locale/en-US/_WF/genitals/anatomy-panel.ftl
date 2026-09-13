# In-game Anatomy panel (AnatomyUIController, AnatomyWindow). Clinical wording; arguments are localised words or numbers.

wf-anatomy-window-title = Anatomy
wf-anatomy-button-tooltip = Open the anatomy panel.
ui-options-function-open-anatomy-panel = Open anatomy panel

## Settings
wf-anatomy-reveal = Reveal anatomy
wf-anatomy-reveal-undergarments = When undergarments are removed
wf-anatomy-reveal-clothing = When clothing is removed
wf-anatomy-reveal-clothing-tooltip = Anatomy is drawn over undergarments once clothing is removed.
wf-anatomy-undergarments = Undergarments
wf-anatomy-undergarment-top = Top
wf-anatomy-undergarment-bottom = Bottom
wf-anatomy-undergarment-worn = Worn
wf-anatomy-undergarment-removed = Removed
wf-anatomy-undergarment-none = None worn
wf-anatomy-strip-consent = Others may remove my undergarments
wf-anatomy-strip-consent-tooltip = Changes this consent setting directly. Turning it off puts back anything others removed.
wf-anatomy-toggle-off = Off
wf-anatomy-toggle-on = On

## Arousal
wf-anatomy-arousal = Arousal
wf-anatomy-arousal-value = { $value } ({ $state })
wf-anatomy-arousal-state-none = none
wf-anatomy-arousal-state-partial = partial
wf-anatomy-arousal-state-full = full
wf-anatomy-arousal-snap-none = None
wf-anatomy-arousal-snap-partial = Partial
wf-anatomy-arousal-snap-full = Full

## Organ rows
wf-anatomy-row-penis = Penis: { $shape }, about { $length } cm (erect){ $sheath ->
    [sheath] , sheath
    [slit] , slit
   *[none] {""}
}
wf-anatomy-row-penis-plain = Penis
wf-anatomy-row-testicles = Testicles: external, { $size }
wf-anatomy-row-testicles-internal = Testicles: internal
wf-anatomy-row-vagina = Vulva: { $shape }
wf-anatomy-row-womb = Womb: internal
wf-anatomy-row-breasts = Breasts: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}
wf-anatomy-shape-unknown = unspecified
wf-anatomy-visibility = Visibility
wf-anatomy-visibility-normal = Normal
wf-anatomy-visibility-hidden = Always hidden
wf-anatomy-visibility-through = Show through clothing
wf-anatomy-status = Status: { $status }
wf-anatomy-status-exposed = exposed
wf-anatomy-status-covered = covered by { $item }
wf-anatomy-status-covered-generic = covered
wf-anatomy-status-undergarment = covered by undergarment
wf-anatomy-status-hidden = always hidden
wf-anatomy-status-through = shown through clothing

## Empty states and gates
wf-anatomy-no-organs = No external anatomy.
wf-anatomy-unavailable = Anatomy is not available for this body.
wf-anatomy-gate-consent = The anatomy panel is part of adult content. Turn on Adult content in consent settings to use it.

## Footer
wf-anatomy-save-defaults = Save as character defaults
wf-anatomy-save-defaults-tooltip = Saves the current reveal mode and visibility settings to the selected character.
wf-anatomy-save-defaults-unavailable = Only available while the selected character is the one you are playing.
wf-anatomy-consent-settings = Consent settings

## One-time notice for accounts with Adult content on
wf-anatomy-notice-title = Adult content
wf-anatomy-notice-text = Adult content now also covers the anatomy panel, anatomy sprites and anatomy examine descriptions.
    Other players can only remove your undergarments or perform anatomy surgery on you if you also turn on those separate consent toggles. They are off unless you enable them.
wf-anatomy-notice-open-consent = Open consent settings
wf-anatomy-notice-ok = OK
