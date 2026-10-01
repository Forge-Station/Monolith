# Playtest 4, IV: the IV drip.

wolfmed-iv-verb-attach = Attach IV to { $target }
wolfmed-iv-verb-detach = Take the needle out
wolfmed-iv-verb-eject = Remove { $container }
wolfmed-iv-verb-mode-take = Set to take blood
wolfmed-iv-verb-mode-inject = Set to inject
wolfmed-iv-verb-category-rate = Flow rate
wolfmed-iv-verb-rate-stop = Stop ({ $rate } u/s)
wolfmed-iv-verb-rate-slow = Slow ({ $rate } u/s)
wolfmed-iv-verb-rate-normal = Normal ({ $rate } u/s)
wolfmed-iv-verb-rate-fast = Fast ({ $rate } u/s)

wolfmed-iv-attach-begin-user = You begin attaching { THE($drip) } to { THE($target) }.
wolfmed-iv-attach-begin-others = { CAPITALIZE(THE($user)) } begins attaching { THE($drip) } to { THE($target) }...
wolfmed-iv-attach-done-user = You attach { THE($drip) } to { THE($target) }.
wolfmed-iv-attach-done-others = { CAPITALIZE(THE($user)) } attaches { THE($drip) } to { THE($target) }.
wolfmed-iv-detached = { CAPITALIZE(THE($target)) } is detached from { THE($drip) }.
wolfmed-iv-ripped-patient = The IV needle is ripped out of you, leaving an open bleeding wound!
wolfmed-iv-ripped-others = The IV needle is ripped out of { THE($target) }!
wolfmed-iv-no-container = There's nothing attached to { THE($drip) }!
wolfmed-iv-cannot-inject = You can't put a needle in { THE($target) }.
wolfmed-iv-too-far = { CAPITALIZE(THE($target)) } is too far from { THE($drip) }.
wolfmed-iv-not-empty = Something is already hanging on { THE($drip) }.
wolfmed-iv-loaded = You hang { THE($container) } on { THE($drip) }.
wolfmed-iv-mode-now-inject = { CAPITALIZE(THE($drip)) } is now injecting.
wolfmed-iv-mode-now-take = { CAPITALIZE(THE($drip)) } is now taking blood.
wolfmed-iv-rate-set = Flow set to { $rate } u/s.
wolfmed-iv-pack-no-refill = A blood pack can't be refilled; hang a beaker or a jug to take blood.

# Emotes the drip makes in chat, after its name.
wolfmed-iv-pings = pings.
wolfmed-iv-beeps = beeps loudly.

wolfmed-iv-examine-mode-inject = It is injecting at { $rate } u/s.
wolfmed-iv-examine-mode-take = It is taking blood at { $rate } u/s.
wolfmed-iv-examine-packs = Attached { $count ->
    [one] is a blood pack
   *[other] are { $count } blood packs
}, { $units } u of blood left.
wolfmed-iv-examine-opened = An opened blood pack hangs on the line with { $units } u left.
wolfmed-iv-examine-pack-take = A blood pack can't be refilled: taking blood needs a beaker or a jug.
wolfmed-iv-examine-pack-refused = Blood packs can't help { THE($target) }.
wolfmed-iv-examine-container = Attached is { THE($container) } with { $units } u of liquid.
wolfmed-iv-examine-container-empty = Attached is an empty { $container }.
wolfmed-iv-examine-nothing = No container is attached.
wolfmed-iv-examine-connected = { CAPITALIZE(THE($target)) } is connected.
wolfmed-iv-examine-not-connected = Nothing is connected.
