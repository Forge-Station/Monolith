# WOLFGATE - NPC traders (Docs/Wolfgate/Traders/plan.md)

## Conversation
trader-busy = One moment, please.
trader-put-on-table = Put it on the table, please.
trader-not-your-id = This isn't your ID.
trader-short = You're { $amount } short.
trader-request-item = Please give me your { $thing }.
trader-thing-payment = ID or cash
trader-thing-id = ID
trader-thing-id-voucher = ID or ship voucher
trader-thing-deed-id = ship's ID
trader-cannot-help = I can't help you with that.
trader-verb-talk = Talk
trader-confirm-yes = Yes, go ahead.
trader-confirm-no = Never mind.

## Windows
trader-dialogue-window-title = Conversation
trader-shop-window-title = For Sale
trader-shop-zone-cash = Cash in zone: { $amount }
trader-shop-zone-id = ID in zone: { $name }
trader-shop-zone-none = Nothing in the zone.
trader-shop-balance = Bank balance: { $amount }
trader-shop-basket-title = Your order
trader-shop-basket-empty = Nothing picked out yet.
trader-shop-total = Total: { $amount }
trader-shop-purchase = Purchase
trader-shop-clear = Clear
trader-shop-search = Search...
trader-shop-search-empty = Nothing matches.
trader-shop-empty = Nothing in stock right now.
trader-shop-thanks = Pleasure doing business.

## Parcels
trader-parcel-open-verb = Open
trader-parcel-examine = There { $count ->
    [one] is one item
   *[other] are { $count } items
} inside.

## Shop receipt
trader-shop-receipt-name = Sales Receipt
trader-shop-receipt-header = [head=2]{ $trader }[/head]
    Sold to: { $customer }
    { $time }
trader-shop-receipt-line = { $count }x { $item } - { $price }
trader-shop-receipt-total =
    Total: { $total }
    Paid: { $paid }
    Change: { $change }

## Refuelling
trader-refuel-not-docked = Your ship isn't docked here.
trader-refuel-no-ship = I don't see your ship.
trader-refuel-full = Your tanks are already full.
trader-refuel-quote = { $count } generators, { $cost }.
trader-refuel-done = All done.
trader-refuel-receipt-name = Refuelling Receipt
trader-refuel-receipt-header = [head=2]{ $trader }[/head]
    Serviced for: { $customer }
    Ship: { $ship }
    { $time }
trader-refuel-receipt-line = { $generator }: { $amount } units { $fuel } - { $price }
trader-refuel-receipt-total =
    Total: { $total }
    Paid: { $paid }
    Change: { $change }

## Shipyard dealer
trader-shipyard-no-selling = I only sell them. Take her to the used ship salesman.
trader-shipyard-sold = She's all yours. Fly her carefully.
trader-shipyard-unknown-design = Unknown design
trader-shipyard-receipt-name = Vessel Purchase Receipt
trader-shipyard-receipt-header = [head=2]{ $trader }[/head]
    Sold to: { $customer }
    { $time }
trader-shipyard-receipt-line = { $ship } ({ $design }) - { $price }

## Used ships
trader-used-window-title = The Lot
trader-used-empty = Nothing on the lot right now. Come back when somebody trades one in.
trader-used-buy = Buy
trader-used-row-detail = { $design } - one owner: { $seller }
trader-used-unknown-design = unknown design
trader-used-unknown-seller = an anonymous seller
trader-used-no-ship = I don't see a ship on that ID.
trader-used-quote = I can give you { $amount } for the { $ship }.
trader-used-unsavable-aboard = Get the { $thing } off her first; I can't take it with the ship.
trader-used-sale-refused = Can't take her like that, I'm afraid.
trader-used-sale-refused-reason = Can't take her like that, I'm afraid. { $reason }
trader-used-sale-done = { $amount }, straight into your account. Pleasure.
trader-used-gone = Somebody beat you to that one.
trader-used-load-failed = She won't come out of the yard. Your money's back on the table.
trader-used-sold = The { $ship }, sold! No refunds, no take-backs.
trader-used-no-session = I need a real buyer to put on the paperwork.
trader-used-deed-failed = The paperwork won't go through. I've put the money back in your account; she stays on the lot.
trader-used-new-stock = Fresh on the lot: the { $ship }, a { $design } - { $price }.
trader-used-sale-receipt-name = Vessel Trade-In Receipt
trader-used-sale-receipt-header = [head=2]{ $trader }[/head]
    Bought from: { $seller }
    { $time }
trader-used-sale-receipt-line = { $ship } - { $amount }
trader-used-buy-receipt-name = Used Vessel Receipt
trader-used-buy-receipt-header = [head=2]{ $trader }[/head]
    Sold to: { $customer }
    { $time }
trader-used-buy-receipt-line = { $ship } ({ $design }), formerly of { $seller } - { $price }

## Corwin Ashgrove - shipyard dealer (shipyard_dealer.yml)
trader-shipyard-greeting = Looking to put your name on a hull?
trader-shipyard-farewell = Fair skies out there.
trader-shipyard-prompt-civilian = What ships have you got for sale?
trader-shipyard-response-civilian = The full civilian book. Take your time.
trader-shipyard-prompt-expedition = Anything outfitted for expeditions?
trader-shipyard-response-expedition = Long-range hulls, here you are.
trader-shipyard-prompt-sr = I'm Colonial staff - what's on the staff list?
trader-shipyard-response-sr = Staff list it is. Credentials speak for themselves.
trader-shipyard-prompt-who = What do you do?
trader-shipyard-response-who = Corwin Ashgrove, factory-direct sales. Every hull on my books comes off the line untouched, deeded to you on the spot. I don't buy back - if you're done with one, Benny down the concourse handles trade-ins.

## Benny Sokolov - used ship salesman (used_ship_salesman.yml)
trader-used-greeting = Step right up - kicking tyres or signing papers?
trader-used-farewell = You know where to find me.
trader-used-prompt-lot = What have you got on the lot?
trader-used-response-lot = Have a look. Every one of them flew in here under her own power.
trader-used-prompt-sell = I'd like to sell my ship.
trader-used-response-sell = Let's see what she's worth.
trader-used-prompt-who = What do you do?
trader-used-response-who = Benny Sokolov, trade-ins and turnarounds. Dock her here and I'll buy her off you at the going rate, cash in your account same minute. Few minutes later she goes back on the lot exactly as you left her, small markup on top. Everything sells as-is, and every sale is final.

## Dara Voss - fuel dock trader (fuel_technician.yml)
trader-fuel-tech-greeting = What can I get you?
trader-fuel-tech-farewell = Mind the fumes on your way out.
trader-fuel-tech-prompt-shop = What have you got for sale?
trader-fuel-tech-response-shop = Here's what I've got for sale!
trader-fuel-tech-prompt-refuel = Can you refuel my ship?
trader-fuel-tech-response-refuel = Let me take a look at her.
trader-fuel-tech-prompt-who = Who are you?
trader-fuel-tech-response-who = I'm Dara Voss - I run the Caelestinus Central fuel dock. I sell fuel-grade plasma, uranium and bananium, AME jars and welding fuel. Hand over your ship's ID and I'll top off every generator aboard her at the going rate.
