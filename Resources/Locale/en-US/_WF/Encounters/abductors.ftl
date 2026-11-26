wf-encounter-name-abductor-light = Reaver snatch ship
wf-encounter-name-abductor-medium = Reaver slaver

wf-bounty-title-abductor-light = Reaver snatch ship
wf-bounty-desc-abductor-light = The crew of {$ship}, wanted for abduction. Paid when they are dead or the ship is gone.
wf-bounty-title-abductor-medium = Reaver slaver
wf-bounty-desc-abductor-medium = The crew of {$ship}, wanted for abduction. Paid when they are dead or the ship is gone.

# Read out to the whole sector when someone is taken.
wf-encounter-abduct-sender = Sector Traffic Control
wf-encounter-abduct-taken = {$name} has been abducted by {$ship}. The Reavers want {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }. A warrant is out on the ship.

# Told to the prisoner.
wf-encounter-abduct-prisoner-told = You are held for ransom. Right-click the chair to pay {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }.
wf-encounter-abduct-prisoner-short = You can't cover the {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }.
wf-encounter-abduct-released-paid = Paid. The Reavers put you back aboard.
wf-encounter-abduct-released-free = The Reavers are done waiting and send you back.
wf-encounter-abduct-verb-pay = Pay ransom ({$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    })
wf-encounter-abduct-handover-told = Your ransom is paid. The doors are open. Someone is coming to dock and collect you.

# On the mass scanner, for a ship holding someone.
wf-encounter-abduct-iff-label = HOSTAGE ABOARD - {$name}

# Said down the hail to whoever answers aboard the prisoner's ship.
wf-encounter-abduct-demand = We have {$name}. {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    } and they come home in one piece.
wf-encounter-abduct-handover = Paid. Dock with us and collect {$name}. Nobody aboard will touch you. You have {$minutes} minutes.
wf-encounter-abduct-short = You don't have {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }. Call back when you do.
wf-encounter-abduct-refused = Your choice. They stay here.
wf-encounter-abduct-option-pay = Pay {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }
wf-encounter-abduct-option-refuse = Refuse

# The admin's encounter window and wf_encounter list.
wf-encounter-abduct-status-close = closing on {$ship}
wf-encounter-abduct-status-dock = docking with {$ship}
wf-encounter-abduct-status-grab = taking someone off {$ship}
wf-encounter-abduct-status-ransom = holding {$name} for ransom
wf-encounter-abduct-status-handover = paid, waiting for the prisoner to be collected
wf-encounter-admin-order-abduct-unreachable = That ship is berthed at a station or hidden.
wf-encounter-admin-order-abduct-nobody = There is nobody aboard that ship to take.
