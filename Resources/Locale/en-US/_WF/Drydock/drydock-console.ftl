drydock-console-no-ship = No ship is in the berth.
drydock-console-reserved = Berth { $berth } is reserved for the { $ship }.
drydock-console-busy = The drydock is busy.
drydock-console-own-grid = The cranes can't work on the ship they're mounted on.
drydock-console-ship-moving = Hold the ship still first.
drydock-console-nothing-to-repair = Nothing to repair.
drydock-console-insufficient-funds = Insufficient funds: { $price } cr required.
drydock-console-no-bank-account = No bank account found.
drydock-console-no-character = No character data found.
drydock-console-transaction-failed = The transaction failed.
drydock-console-started = Repair started: { $count } { $count ->
    [one] item
   *[other] items
} for { $price } cr.
drydock-console-refit-started = Refit started on { $count } { $count ->
    [one] machine
   *[other] machines
}.
drydock-console-unknown-refit = Unknown refit.
drydock-console-no-parts = No parts to refit.
drydock-console-refit-maxed = Fully refitted.
drydock-console-finished = Work complete on { $ship }.
drydock-console-finished-skipped = Work complete on { $ship }. { $count } { $count ->
    [one] item
   *[other] items
} out of crane reach refunded: { $refund } cr.
drydock-console-aborted = Ship left the berth. Unfinished work refunded: { $refund } cr.
drydock-console-aborted-power = The cranes lost power. Unfinished work refunded: { $refund } cr.
drydock-console-no-power = The cranes have no power.
drydock-console-cranes-blocked = The cranes' arms are blocked.
drydock-console-crane-busy = This crane is already working for another console.

drydock-console-insufficient-materials = Not enough materials: { $missing } short.
drydock-console-started-materials = Work started: { $count } { $count ->
    [one] item
   *[other] items
}, paid from stored materials.
drydock-console-finished-skipped-materials = Work complete on { $ship }. { $count } { $count ->
    [one] item
   *[other] items
} out of crane reach. Materials returned: { $refund }.
drydock-console-aborted-materials = Ship left the berth. Unfinished work's materials returned: { $refund }.
drydock-console-aborted-power-materials = The cranes lost power. Unfinished work's materials returned: { $refund }.

drydock-berth-lights-no-berth = No berth to light up.

drydock-console-break-unavailable = This console can't break hulls.
drydock-console-break-station = Stations and outposts can't be broken.
drydock-console-nothing-to-break = Nothing to break.
drydock-console-break-started = Breaking up { $ship }: { $count } { $count ->
    [one] tile
   *[other] tiles
}.
drydock-console-break-finished = { $ship } is broken up. Recovered: { $yield }.
drydock-console-break-finished-skipped = Finished breaking { $ship }. { $count } { $count ->
    [one] tile
   *[other] tiles
} left standing. Recovered: { $yield }.
drydock-console-break-aborted = Ship left the berth. Breaking stopped. Recovered: { $yield }.
drydock-console-break-aborted-power = The cranes lost power. Breaking stopped. Recovered: { $yield }.

drydock-salvaged-flatpack-name = salvaged { $name } flatpack
drydock-salvaged-flatpack-description = { CAPITALIZE(INDEFINITE($name)) } { $name } stripped off a broken hull. It unpacks damaged.
