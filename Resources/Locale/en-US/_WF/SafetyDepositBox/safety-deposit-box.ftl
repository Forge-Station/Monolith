# Safety Deposit Box Console UI
safety-deposit-console-title = Safety Deposit Box Console
safety-deposit-console-header = Safety Deposit Box Management
safety-deposit-console-purchase-section = Purchase New Box
safety-deposit-console-bank-payment-note = Cost will be deducted from your bank account.
safety-deposit-console-purchase-small = Small (2x2) - ${$cost}
safety-deposit-console-purchase-medium = Medium (2x4) - ${$cost}
safety-deposit-console-purchase-large = Large (2x6) - ${$cost}
safety-deposit-console-deposit-section = Deposit Box
safety-deposit-console-box-in-slot = Box {$id} in slot
safety-deposit-console-no-box-in-slot = No box in slot
safety-deposit-console-deposit-button = Deposit Box
safety-deposit-console-withdraw-button = Withdraw
safety-deposit-console-reclaim-button = Reclaim
safety-deposit-console-owned-boxes = Your Safety Deposit Boxes:
safety-deposit-console-no-boxes = You don't own any safety deposit boxes yet.
safety-deposit-console-box-id = Box ID: {$id}... - {$status}
safety-deposit-console-box-id-short = Box ID: {$id}...
safety-deposit-console-box-unnamed = Box {$id}
safety-deposit-console-box-deposited = Stored
safety-deposit-console-box-not-deposited = Not Stored
safety-deposit-console-box-in-world = In World
safety-deposit-console-box-lost = Lost
safety-deposit-console-box-small = small safety deposit box
safety-deposit-console-box-medium = medium safety deposit box
safety-deposit-console-box-large = large safety deposit box

# Purchase confirmation
safety-deposit-console-confirm-title = Confirm Purchase
safety-deposit-console-confirm-text = Buy a [bold]{$box}[/bold] for [color=#ff6600]${$cost}[/color]? The cost is taken from your bank account straight away.
safety-deposit-console-confirm-buy = Buy
safety-deposit-console-confirm-cancel = Cancel

# Safety Deposit Box Entity
safety-deposit-box-boxSlot = Box Slot
safety-deposit-console-boxSlot = Box Slot

# Console popups
safety-deposit-console-error-invalid-box-size = Error: Invalid box size.
safety-deposit-console-error-no-character-data = Error: Could not load character data.
safety-deposit-console-error-no-bank-account = Error: No bank account found.
safety-deposit-console-error-insufficient-funds = Insufficient funds. You need ${$cost}, but only have ${$bank} in your bank account.
safety-deposit-console-error-transaction-failed = Transaction failed.
safety-deposit-console-error-database = Safety deposit storage is unavailable. Please try again later.
safety-deposit-console-purchase-success = Safety deposit box purchased! Box ID: {$id}...
safety-deposit-console-error-no-box-in-slot = Please insert a safety deposit box.
safety-deposit-console-error-invalid-box = Invalid safety deposit box.
safety-deposit-console-error-not-owner = This box does not belong to you.
safety-deposit-console-error-no-storage = Error: Box has no storage.
safety-deposit-console-deposit-success = Safety deposit box contents saved. The box has been stored.
safety-deposit-console-deposit-nested-refused = {$count ->
    [one] One item tucked inside something can't be stored and was handed back.
   *[other] {$count} items tucked inside things can't be stored and were handed back.
}
safety-deposit-console-error-box-not-found = Box not found.
safety-deposit-console-error-not-lost = This box is not lost and cannot be reclaimed.
safety-deposit-console-reclaim-success = Lost box reclaimed! A new empty box has been issued.
safety-deposit-console-error-already-withdrawn = Box already withdrawn in world.
safety-deposit-console-withdraw-success = Safety deposit box retrieved.
safety-deposit-console-withdraw-refused = Safety deposit box retrieved. {$count ->
    [one] One item can no longer be stored in it and was left at your feet.
   *[other] {$count} items can no longer be stored in it and were left at your feet.
}

# Examine text
safety-deposit-stored-examine = [color=gray]This item has a signature on it that indicates that it was at one point stowed in a deposit box.[/color]
safety-deposit-box-examine-id = [color=cyan]Box ID:[/color] {$id}...
safety-deposit-box-examine-owner = [color=yellow]Owner:[/color] {$owner}

# Guidebook list of refused items; anything missing here is listed under its raw name
safety-deposit-refused-component-idcard = ID cards
safety-deposit-refused-component-currency = Currency and LPCs
safety-deposit-refused-component-store = Anything with a store
safety-deposit-refused-component-encryptionkey = Encryption keys
safety-deposit-refused-component-headset = Headsets
safety-deposit-refused-component-implant = Implants
safety-deposit-refused-component-implanter = Implanters
safety-deposit-refused-component-storage = Anything with storage (bags, boxes, other deposit boxes)
safety-deposit-refused-component-safetydepositbox = Safety deposit boxes
safety-deposit-refused-component-shipyardvoucher = Shipyard vouchers
safety-deposit-refused-component-secretstash = Secret stashes (plushies and the like)
safety-deposit-refused-component-overwatchconsole = Overwatch consoles
safety-deposit-refused-component-ghostrolemobspawner = Ghostrole spawners (such as reinforcement radios)
safety-deposit-refused-component-doorremote = Door remotes
safety-deposit-refused-component-solutioncontainermanager = Anything holding reagents (beakers, bottles)
safety-deposit-refused-component-nukedisk = Nuclear authentication disks
safety-deposit-refused-component-contraband = Contraband (items that sell for FMC or DC, most faction gear)
safety-deposit-refused-component-thiefundeterminedbackpack = Thief kits
safety-deposit-refused-component-accessoverrider = Access overriders
safety-deposit-refused-component-pda = PDAs
safety-deposit-refused-component-pill = Pills
safety-deposit-refused-component-machineboard = Machine boards
safety-deposit-refused-component-flatpack = Flatpacks
safety-deposit-refused-tag-unstashable = Items marked unstashable (some weapons and loot)
safety-deposit-refused-tag-safetydepositblacklist = Items marked unstashable (some weapons and loot)
