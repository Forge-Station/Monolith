# Anatomy examine lines (GenitalExamineSystem). Clinical wording; every argument is a localised word or a number.
# Selectors: $shape is "none" when the shape has no examine word; $aroused, $lactating and $plural are "yes" or "no".

## Another body: one line per exposed organ. The womb and internal testicles are never described.
wf-genitals-examine-penis = { CAPITALIZE(POSS-ADJ($target)) } penis is exposed: { $shape }, approximately { $length } cm when erect, { $state }.
wf-genitals-examine-penis-noshape = { CAPITALIZE(POSS-ADJ($target)) } penis is exposed: approximately { $length } cm when erect, { $state }.
wf-genitals-examine-penis-sheathed = { CAPITALIZE(POSS-ADJ($target)) } genital { $sheath } is exposed; the penis is { $state }.
wf-genitals-examine-testicles = { CAPITALIZE(POSS-ADJ($target)) } testicles are exposed: { $size }.
wf-genitals-examine-vagina = { CAPITALIZE(POSS-ADJ($target)) } vulva is exposed{ $shape ->
    [none] {""}
   *[other] : { $shape }
}{ $aroused ->
    [yes] , visibly aroused
   *[no] {""}
}.
wf-genitals-examine-breasts = { CAPITALIZE(POSS-ADJ($target)) } breasts are exposed: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wf-genitals-examine-breasts-noshape = { CAPITALIZE(POSS-ADJ($target)) } breasts are exposed: { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.

## Self-examine: the same lines with "Your", plus organs that others cannot see.
wf-genitals-examine-self-penis = Your penis is exposed: { $shape }, approximately { $length } cm when erect, { $state }.
wf-genitals-examine-self-penis-noshape = Your penis is exposed: approximately { $length } cm when erect, { $state }.
wf-genitals-examine-self-penis-sheathed = Your genital { $sheath } is exposed; the penis is { $state }.
wf-genitals-examine-self-testicles = Your testicles are exposed: { $size }.
wf-genitals-examine-self-vagina = Your vulva is exposed{ $shape ->
    [none] {""}
   *[other] : { $shape }
}{ $aroused ->
    [yes] , visibly aroused
   *[no] {""}
}.
wf-genitals-examine-self-breasts = Your breasts are exposed: { $shape }, { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wf-genitals-examine-self-breasts-noshape = Your breasts are exposed: { $cup }{ $lactating ->
    [yes] , lactating
   *[no] {""}
}.
wf-genitals-examine-self-covered = Your { $organ } { $plural ->
    [yes] are
   *[no] is
} covered.
wf-genitals-examine-self-hidden = Your { $organ } { $plural ->
    [yes] are
   *[no] is
} hidden from others by your visibility setting.
wf-genitals-examine-organ-penis = penis
wf-genitals-examine-organ-testicles = testicles
wf-genitals-examine-organ-vagina = vulva
wf-genitals-examine-organ-breasts = breasts

## Arousal, sheath, cup and size words.
wf-genitals-state-flaccid = flaccid
wf-genitals-state-partial = partially erect
wf-genitals-state-erect = erect
wf-genitals-sheath-state-retracted = retracted
wf-genitals-sheath-state-partial = partly extended
wf-genitals-sheath-state-full = extended and erect
wf-genitals-sheath-word-sheath = sheath
wf-genitals-sheath-word-slit = slit
wf-genitals-cup-letter = cup { $letter }
wf-genitals-cup-oversize = oversize grade { $grade }
wf-genitals-testicles-size-1 = small
wf-genitals-testicles-size-2 = average-sized
wf-genitals-testicles-size-3 = large
wf-genitals-testicles-size-4 = very large
wf-genitals-testicles-size-5 = exceptionally large

## Loose organs, described by the server only. Otherwise the item is only "organ tissue".
wf-genitals-organ-examine-penis = It is a penis: { $shape }, approximately { $length } cm when erect.
wf-genitals-organ-examine-penis-noshape = It is a penis, approximately { $length } cm when erect.
wf-genitals-organ-examine-penis-plain = It is a penis.
wf-genitals-organ-examine-testicles = It is a pair of testicles: { $size }.
wf-genitals-organ-examine-testicles-plain = It is a pair of testicles.
wf-genitals-organ-examine-vagina = It is a vulva and vagina{ $shape ->
    [none] {""}
   *[other] : { $shape }
}.
wf-genitals-organ-examine-womb = It is a womb.
wf-genitals-organ-examine-breasts = It is breast tissue: { $shape }, { $cup }.
wf-genitals-organ-examine-breasts-noshape = It is breast tissue: { $cup }.
