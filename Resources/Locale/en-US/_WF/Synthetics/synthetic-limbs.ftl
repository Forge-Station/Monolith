# Synthetics: chassis limbs put back on by hand (WFSyntheticLimbSystem).
wf-synthetic-limb-reattaching = You start coupling { THE($limb) } back on.
wf-synthetic-limb-reattaching-other = { CAPITALIZE(THE($user)) } starts coupling { THE($limb) } onto you.
wf-synthetic-limb-reattached = { CAPITALIZE(THE($limb)) } clicks into place, loosely.
wf-synthetic-limb-not-chassis = That will not couple to a chassis.
wf-synthetic-limb-no-socket = There is no free socket for that.
wf-synthetic-limb-examine-loose = [color=yellow]{ CAPITALIZE(POSS-ADJ($target)) } { $part } sits loose in its socket. Cable coil on it will recouple it.[/color]
wf-synthetic-limb-coupling-tightened = { CAPITALIZE(THE($limb)) }'s coupling tightens. { $passes ->
    [one] One more pass of cable.
   *[other] About { $passes } more passes of cable.
}
wf-synthetic-limb-coupling-seated = { CAPITALIZE(THE($limb)) } seats firmly in its socket.

# The wound a hand reattach leaves (wounds.yml).
wf-synthetics-wound-name-loose-coupling = loose coupling
wf-synthetics-wound-stage-loose = loose
wf-synthetics-look-loose-coupling = sitting crooked and loose in its socket
wf-synthetics-look-loose-coupling-short = loose in socket

# The synthetic readout (hud_lines.yml).
wf-synthetic-line-loose = LOOSE COUPLING
wf-synthetic-advice-loose = RECOUPLE: CABLE COIL
