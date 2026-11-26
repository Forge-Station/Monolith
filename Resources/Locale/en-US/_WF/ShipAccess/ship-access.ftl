# Access level a locked ship's doors require; no ID card can carry it
id-card-access-level-wf-ship-locked = Locked ship

# Shuttle console access tab
ship-access-mode-button = ACCESS
ship-access-title = Ship access
ship-access-owner = Registered to
ship-access-owner-none = None
ship-access-mode = Mode
ship-access-mode-private = Private
ship-access-mode-faction = Faction
ship-access-mode-crew = Crew
ship-access-mode-company = Company
ship-access-no-company = Join a player company, and enter the round as that character, before selecting Company access.
ship-access-linked-company = Linked company: {$name} ({$count} issued IDs). Members playing this round open these doors; leaving the company removes access.
ship-access-mode-hint = Private admits the owner and allowed IDs. Crew also admits current crew members' issued IDs. Faction also admits company access. Individual door rules override this mode.
ship-access-no-crew = Join a crew through Crew Setup and enter the round before selecting Crew access.
ship-access-mode-unavailable = This ship does not support that access mode.
ship-access-linked-crew = Linked crew: {$name} ({$count} issued IDs). Leaving the crew or respawning removes crew access; ownership and deliberately allowed IDs remain.
ship-access-rule-crew-name = Crew
ship-access-rule-company-name = Company
ship-access-rule-company-desc = The owner and members of the linked company who are playing this round, even when the ship uses another mode or is unlocked.
ship-access-rule-crew-desc = The owner and current members of the linked crew, even when the ship uses another mode or is unlocked. Leaving the crew or respawning removes crew access. Join a crew through Crew Setup before selecting this rule.
ship-access-lock = Lock
ship-access-locked-check = Locked
ship-access-locked-yes = Locked
ship-access-locked-no = Unlocked
ship-access-read-only = Only the ship's owner can change who may board.
ship-access-allow-list = Allowed ID cards
ship-access-allow-list-empty = No ID cards have been added yet.
ship-access-builder = Builder
ship-access-remove = Remove
ship-access-nearby = Nearby
ship-access-nearby-empty = Nobody is standing near the console.
ship-access-add = Add
ship-access-no-card = no ID
ship-access-no-record = no record
ship-access-held = in hand
ship-access-label-guest = Guest

# Server popups
ship-access-not-owner = Your ID is not an owner of this ship.
ship-access-added = { $name }'s ID card may now open the ship.
ship-access-add-no-card = They carry no ID card.
ship-access-add-no-record = Their ID card has no crew record, so no door can be keyed to it.
ship-access-add-card-no-record = That ID card has no crew record, so no door can be keyed to it.
ship-access-add-out-of-range = They must stand closer to the console.
ship-access-add-already = That ID card is already allowed aboard.
ship-access-allow-list-cleared = Allow list cleared: { $count } removed.

# Door diagram and rules
ship-access-door = Selected door
ship-access-door-none = Click a door on the diagram.
ship-access-door-name = Door
ship-access-door-rule = Rule
ship-access-door-label = { $name } ({ $x }, { $y })
ship-access-doors-empty = No doors in view. Doors far from the console show up once you have been near them. Firelocks are not listed.
ship-access-door-players = ID cards for this door
ship-access-door-players-empty = Add ID cards to the allowed list first.
ship-access-rule-default-name = Ship default
ship-access-rule-owner-only-name = Deed only
ship-access-rule-players-name = Chosen IDs
ship-access-rule-code-name = Code
ship-access-rule-players-or-code-name = Chosen IDs or code
ship-access-rule-public-name = Public
ship-access-rule-sealed-name = Sealed
ship-access-rule-default-desc = Follows the ship: while it is locked, the owner (the deed, or the ID card worn by a player the ship is registered to), the allowed ID cards and the selected mode's crew or company access. Unlocked, the door keeps its own access, and the owner and the allowed ID cards open it too.
ship-access-rule-owner-only-desc = Only the owner opens this door, locked ship or not: the deed, or the ID card worn by a player the ship is registered to.
ship-access-rule-players-desc = The owner and the ID cards ticked below.
ship-access-rule-code-desc = The owner, or anyone who enters this door's code or the ship code at its keypad.
ship-access-rule-players-or-code-desc = The owner, the ID cards ticked below, or a code at the keypad.
ship-access-rule-public-desc = Anyone opens this door, even while the ship is locked, whatever access its electronics ask for.
ship-access-rule-sealed-desc = Bolted shut for everyone, the owner included. Pick another rule to unseal it.
ship-access-all-doors = All doors
ship-access-all-doors-apply = Set all
ship-access-all-doors-confirm = Confirm
ship-access-all-doors-hint = Gives every door on the ship this rule. Firelocks are left alone; lockers and lockable buttons always follow the ship. { $desc }
ship-access-all-doors-set = { $count ->
        [0] No doors changed.
        [one] 1 door set.
       *[other] { $count } doors set.
    }
ship-access-door-not-on-ship = That door is not on this ship.
ship-access-seal-pending = The door will bolt as soon as it is shut and powered.
ship-access-no-owner-key = Neither the deed nor your ID card has a crew record, so no door can be keyed to you. Locking would shut you out.

# Codes and the keypad
ship-access-ship-code = Ship code
ship-access-door-code = Door code
ship-access-code-none = Not set
ship-access-code-masked = ****
ship-access-code-placeholder = 4 digits
ship-access-code-set = Set
ship-access-code-clear = Clear
ship-access-code-reveal = Show
ship-access-ship-code-hint = Opens every code door on the ship. Only you can see it here.
ship-access-door-code-hint = A door without its own code takes the ship code; a door with one takes either.
ship-access-code-alert = { $misses } failed code attempts, { $locked } locked out.
ship-access-keypad-verb = Enter Code
ship-access-keypad-title = Door keypad
ship-access-keypad-clear = C
ship-access-keypad-enter = OK
ship-access-code-invalid = A code is exactly 4 digits.
ship-access-code-wrong = Wrong code.
ship-access-code-locked-out = Too many wrong codes. Try again later.
ship-access-code-out-of-range = Stand closer to the door.
ship-access-code-bolted = The door is bolted.
ship-access-code-no-response = The keypad takes the code, but the door doesn't respond.
ship-access-code-not-closed = The door is not closed.
ship-access-code-no-keypad = This door has no keypad.
