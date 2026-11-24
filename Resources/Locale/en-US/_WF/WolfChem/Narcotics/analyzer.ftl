## Health analyzer Conditions tab: drug levels, held effects, crashes and dependence, with what clears each.

wf-analyzer-conditions-tab = Conditions
wf-analyzer-conditions-unavailable = Conditions unavailable.
wf-analyzer-conditions-none = Nothing active.
wf-analyzer-time = { $minutes }:{ $seconds }

wf-analyzer-drugs-title = Drug levels
wf-analyzer-drug = { $family }: { $phase }
wf-analyzer-phase-none = trace, no effect
wf-analyzer-phase-light = light
wf-analyzer-phase-heavy = heavy
wf-analyzer-phase-overdose = OVERDOSE
wf-analyzer-drug-no-cause = Nothing in the blood is raising it; it fades on its own.
wf-analyzer-antidote = Brought down by { $reagents }.

wf-analyzer-cause = { $reagent }, { $quantity }u: clears in about { $time }
wf-analyzer-cause-stalled = { $reagent }, { $quantity }u: not clearing
wf-analyzer-cause-stomach = { $reagent }, { $quantity }u in the stomach
wf-analyzer-removed-by = Removed faster by { $reagents }.

wf-analyzer-held-title = Held effects
wf-analyzer-held = { $effect }: { $time } left
wf-analyzer-held-fading = Nothing is holding it; it wears off on its own.
wf-analyzer-source-high = Held by the high on { $family }.
wf-analyzer-source-crash = Held by the crash from { $family }.
wf-analyzer-source-withdrawal = Held by withdrawal from { $family }.

wf-analyzer-crashes-title = Crashes
wf-analyzer-crash = Crash from { $family }: { $time } left
wf-analyzer-crash-hint = Wears off on its own; another dose only pauses it.

wf-analyzer-habits-title = Dependence
wf-analyzer-habit = Dependence on { $family }: { $meter }%
wf-analyzer-withdrawal = WITHDRAWAL, stage { $stage }: a dose of { $family } ends it.
wf-analyzer-withdrawal-in = Withdrawal in { $time } without { $family }.
wf-analyzer-clean-in = Out of withdrawal risk after { $time } clean.
wf-analyzer-clean-never = A trait keeps it at { $floor }% or more; the withdrawal risk never fades.
wf-analyzer-tolerance = Tolerance { $tolerance }%: doses hit { $weaker }% weaker.
