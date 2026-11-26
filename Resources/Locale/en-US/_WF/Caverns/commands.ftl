cmd-wfcavern-desc = Inspect caverns, visit their gates, carve mouths by hand and sample cavern terrain.
cmd-wfcavern-help = Usage: { $command } <list | tp <planet> [pad|mouth] | mouths <planet> | open | stats <planet>>
cmd-wfcavern-disabled = Caverns are off. Set wf.caverns to true.
cmd-wfcavern-invalid-args = Expected one of: list, tp <planet> [pad|mouth], mouths <planet>, open, stats <planet>.
cmd-wfcavern-unknown-planet = No built planet network named "{ $planet }".
cmd-wfcavern-no-cavern = { $planet } has no cavern, or its cavern has no gate.
cmd-wfcavern-empty = Nothing to list.
cmd-wfcavern-row = { $planet } | cavern: { $cavern } | mouths: { $mouths } | below: { $players }
cmd-wfcavern-row-none = none
cmd-wfcavern-mouth-row = { $kind } | anchor: { $origin } | tiles: { $tiles } | climb tile: { $climb }
cmd-wfcavern-mouth-kind = { $kind ->
    [gate] Gate
    [cell] Cell
   *[admin] Admin
}
cmd-wfcavern-tp-done = Moved to the gate { $target } of { $planet }.
cmd-wfcavern-no-map = Attach to an entity first.
cmd-wfcavern-not-ground = Stand on a planet's ground above a cavern first.
cmd-wfcavern-open-done = Carved a mouth around { $origin }.
cmd-wfcavern-open-refused = Can't carve a mouth here: { $reason ->
    [grid] a ship or debris is over it.
    [built] something built stands in the hole.
    [mob] someone is standing in the hole.
    [cavern] this planet's cavern is missing.
   *[mouth] there is a mouth there already.
}
cmd-wfcavern-hint-sub = <list|tp|mouths|open|stats>
cmd-wfcavern-stats-started = Sampling { $size }x{ $size } cavern tiles around { $centre }...
cmd-wfcavern-stats = { $planet }: { $size }x{ $size } tiles around { $centre } are { $open }% open; the largest region you can walk without crossing lava, plasma or acid holds { $largest }% of the walkable ground, and { $veins }% of the rock is ore. The square holds { $lights } lights, and { $glow }% of the tunnel floor in its middle { $inner }x{ $inner } is within a { $walk }-tile walk of one.
cmd-wfcavern-hint-planet = <planet name>
cmd-wfcavern-hint-target = <pad|mouth>
