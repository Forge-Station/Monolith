wf-trade-chute-local-goods = This chute only takes goods shipped in from elsewhere.
wf-trade-chute-not-bought = This chute doesn't buy that. Its manifest lists what it takes.
wf-trade-refuse-shunned = The merchant won't buy from someone who hurt them.
wf-trade-refuse-unsealed = The fence only takes sealed freight off the haulers.

wf-encounter-name-merchant = Merchant {$designation}
wf-encounter-announce-merchant = {$name} is in the sector buying luxury goods, medicine, reactor fuel, weapon parts and fabric at a premium. Bring cargo alongside before it moves on.

wf-trade-exchange = Sector Exchange

## Live prices

wf-trade-sale = Paying {$amount}.
wf-trade-sale-low = Paying {$amount}. Best now: {$station}, {$best}.
wf-trade-examine-header = [color=#d9a441]Live prices:[/color]
wf-trade-price-line = {$good}: {$amount} {$trend} ({$reason}), {$tonnes}t, {$perTonne}/t
wf-trade-paper-header = [bold]{$station}: live turn-in prices[/bold]
wf-trade-paper-footer = Prices follow how full this station's shelves are: they fall as crates come in and rise as they run low. Sector events raise them. The Market app on your PDA has every station's prices.

wf-trade-reason-steady = steady
wf-trade-reason-glutted = glutted
wf-trade-reason-wanted = in demand
wf-trade-reason-short = short
wf-trade-reason-boom = boom
wf-trade-reason-blockade = blockade
wf-trade-reason-relief = relief
wf-trade-reason-war = war demand
wf-trade-reason-raid = raid alert
wf-trade-reason-rush = rush order
wf-trade-reason-bulk = bulk order
wf-trade-reason-sealed = sealed freight
wf-trade-reason-salvage = salvage bonus
wf-trade-reason-defector = enemy buyer
wf-trade-reason-scarce = scarce
wf-trade-reason-restock = restocking
wf-trade-reason-sealed-only = sealed only

wf-trade-goods-medical = medical supplies
wf-trade-goods-supplies = supplies
wf-trade-goods-ammo = ammunition
wf-trade-goods-ship-components = ship components
wf-trade-goods-chemicals = chemicals
wf-trade-goods-fuel = reactor fuel
wf-trade-goods-industrial = industrial goods
wf-trade-goods-materials = construction materials
materials-wf-munitions = munitions
materials-wf-reactor-fuel = reactor fuel
materials-wf-medical-stock = medical stock
materials-wf-gun-parts = gun parts
materials-wf-ship-parts = ship parts
materials-unit-wf-crate-share = share
wf-trade-goods-agriculture = fertilizer
wf-trade-goods-luxuries = luxury goods
wf-trade-goods-textiles = fabric

## Industries

wf-trade-industry-agriculture = Agriculture
wf-trade-industry-chemicals = Chemicals
wf-trade-industry-pharmaceuticals = Pharmaceuticals
wf-trade-industry-food = Food
wf-trade-industry-munitions = Munitions
wf-trade-industry-refining = Refining
wf-trade-industry-fabrication = Fabrication
wf-trade-industry-ship-works = Ship works
wf-trade-industry-reclamation = Reclamation
wf-trade-industry-luxury-goods = Luxury goods
wf-trade-industry-reactors = Reactors

## Services

wf-trade-service-drydock = Drydock work
wf-trade-service-patrols = Patrols

## Sector Exchange radio

wf-trade-shortage-start = {$station} is short of {$goods}: paying {$factor}x for {$minutes} minutes.
wf-trade-shortage-report = {$station} is short of {$goods}: {$cause}. Paying up to {$factor}x.
wf-trade-shortage-cause-blockade = the blockade has cut it off
wf-trade-shortage-cause-lost = a freighter never arrived
wf-trade-shortage-cause-input = it has no {$input} to make them from
wf-trade-shortage-cause-supply = supply has fallen behind
wf-trade-boom-start = {CAPITALIZE($goods)} boom: every station pays {$percent}% more for {$minutes} minutes.
wf-trade-war-demand = War demand: {$stations} pay {$factor}x for ammunition, weapon parts and medical supplies.
wf-trade-blockade-landed = {$station} is blockaded. It pays up to {$factor}x for essentials while the blockade holds.
wf-trade-blockade-broken = The blockade of {$station} was broken by traffic: {$ships}.
wf-trade-blockade-driven-off = The blockade fleet off {$station} was driven off.
wf-trade-blockade-withdrawn = The raiders off {$station} have moved on.
wf-trade-relief-name = running the blockade of {$station}
wf-trade-rush-start = { $bulk ->
        [true] BULK ORDER
       *[false] RUSH
    }: {$station} needs {$count} crates of {$goods} within {$minutes} minutes, paying {$factor}x.
wf-trade-rush-filled = { $bulk ->
        [true] Bulk order
       *[false] Rush order
    } filled at {$station}: {$fills}.
wf-trade-rush-expired = The { $bulk ->
        [true] bulk order
       *[false] rush order
    } for {$goods} at {$station} ran out.
wf-trade-freight-supplied = {$ship} delivered {$goods} to {$station}.
wf-trade-freight-lost = {$station}'s {$run} never arrived. Its goods are short there.

## War

wf-trade-navy-tsf = the TSF
wf-trade-navy-pdv = the PDV
wf-trade-war-effort = War effort: {$navy} adds {$credits} credits.
wf-trade-defector-warrant = Sold war goods to {$navy} in wartime.

## Contraband

wf-trade-contraband-customs = Customs
wf-trade-contraband-warrant = Sold contraband at {$station}.
wf-trade-contraband-flagged = Customs flag: {$seller} sold contraband at {$station}. A warrant is posted.
wf-trade-contraband-flagged-ship = Customs flag: {$seller} of {$ship} sold contraband at {$station}. A warrant is posted.

## Priority deliveries

wf-trade-consignment-start = Priority delivery: collect a crate at {$origin} and turn it in at {$station}'s chute within {$minutes} minutes for {$value}.
wf-trade-consignment-taken = {$ship} picked up the priority delivery for {$station}.
wf-trade-consignment-delivered = {$ship} made the priority delivery to {$station} on time.
wf-trade-consignment-examine = [color=#e8c46a]Priority delivery for {$station}.[/color] Turn it in at {$station}'s chute within {$minutes} min for {$value}, less if late. Any other chute pays {$elsewhere}.
wf-trade-consignment-lapsed = The priority delivery for {$station} ran out of time and was recalled.
wf-trade-consignment-cancelled = The priority delivery's destination is gone; it was recalled.
wf-trade-unknown-station = an unknown station
wf-trade-rush-fill = {$seller} {$count}

## Freight

wf-trade-seal-examine = Manifest seal: {$run} freight run, bound for {$station}. Lawful chutes pay {$percent}%; deliver it to {$station} for a salvage bonus.
wf-trade-thermal-examine = [color=#9aa7b1]Warm: it shows on thermal sensors.[/color]

## Market app

wf-market-program-name = Market
wf-market-title = Sector Exchange
wf-market-status = {$stations} markets open
wf-market-tab-prices = Prices
wf-market-tab-alerts = Alerts
wf-market-tab-materials = Materials
wf-market-tab-sector = Sector
wf-market-sector-stock = {$stock} crates in the sector, {$owed} waiting on freight
wf-market-sector-flow = {$made} made and {$used} used an hour
wf-market-sector-short = Short at {$stations}
wf-market-no-sector = No station is reporting its stocks.
wf-market-shelf-tooltip = Shelf {$percent}% of target
wf-market-material-pays = Sell at {$station}: {$amount} each
wf-market-material-sells = Buy at {$station}: {$amount} each, {$stock} in stock
wf-market-material-no-buyer = No pallet is buying this.
wf-market-material-no-stock = No station has this in stock.
wf-market-no-materials = No station is trading materials.
wf-market-tab-alerts-count = Alerts ({$count})
wf-market-filter = Filter goods...
wf-market-price-amount = {$amount}
wf-market-price-stock = {$stock} left
wf-market-price-weight = {$tonnes}t · {$perTonne}/t
wf-market-station-best = ★ best for {$count}
wf-market-sell-heading = SELL HERE
wf-market-buy-heading = BUY HERE
wf-market-no-stations = No stations are buying right now.
wf-market-no-match = No market buys that.
wf-market-best-sale = Best price for {$item}: {$station}, {$amount}.
wf-market-best-sale-none = No market would buy {$item} from you right now.
wf-market-tab-underground = Underground
wf-market-underground-title = UNDERGROUND UPLINK
wf-market-underground-subtitle = Unlisted buyers, cash only.
wf-market-faction-caelestinus = Caelestinus
wf-market-faction-tsf = Trans-Solarian Federation
wf-market-faction-ussp = USSP
wf-market-faction-pdv = Phaethon Dynasty
wf-market-faction-underground = The Underground
wf-market-faction-independent = Independent
wf-market-no-alerts = Nothing happening in the sector right now.
wf-market-minutes-left = {$minutes} min left
wf-market-alert-kind-deal = DEAL
wf-market-alert-kind-traffic = TRAFFIC
wf-market-alert-kind-warning = WARNING
wf-market-alert-kind-personal = FOR YOU
wf-market-alert-rush = { $bulk ->
        [true] BULK ORDER
       *[false] RUSH
    } {$station}: {$left} of {$count} crates of {$goods} wanted at {$factor}x
wf-market-alert-consignment = Priority delivery: collect a crate at {$origin} and take it to {$station}'s chute. Pays {$value} on time.
wf-market-alert-consignment-carried = Priority delivery to {$station} is aboard {$ship}. Pays {$value} on time.
wf-market-alert-relief = {$station} pays relief rates ({$factor}x) while the blockade is contested; ships that break it split {$bounty}
wf-market-alert-raid = Raid on {$station}: ship components and medical supplies pay {$factor}x
wf-market-alert-blockade = {$station} is blockaded: essentials pay up to {$factor}x
wf-market-alert-shortage = {$station} is short of {$goods}: {$factor}x
wf-market-alert-shortage-report = {$station} is short of {$goods}: {$factor}x ({$cause})
wf-market-alert-waiting = {$station} is waiting on {$goods}
wf-market-alert-patrols = Patrols are at {$percent}% strength: the sector is short of ammunition
wf-market-alert-service = {$service} at {$station} costs {$factor}x: short of {$goods}
wf-market-alert-boom = {CAPITALIZE($goods)} boom: +{$percent}% everywhere
wf-market-alert-war = War demand: the navies pay {$factor}x for munitions and medicine
wf-market-alert-freight = Freighter {$ship} inbound to {$station} with {$goods}
wf-market-alert-merchant = Merchant {$ship} buying near {$cell} until it moves on
wf-market-alert-shunned = {$station} won't trade with you: you hurt its people
wf-market-alert-war-effort = War effort: {$navy} pays you {$credits} faction credits a crate for munitions and ship parts
wf-market-alert-defector = {$navy} pays you {$factor}x for war goods
wf-market-alert-defector-warrant = {$navy} pays you {$factor}x for war goods, but {$own} posts a {$warrant} warrant on anyone who sells

## wf_trade command

cmd-wf_trade-desc = Shows the sector economy and starts trade events: shortages, rush orders and consignments.
cmd-wf_trade-help = Usage: {$command} status | ledger [station] | shortage [prototype] | rush [prototype] | consignment | clear
cmd-wf_trade-hint-sub = <subcommand>
cmd-wf_trade-hint-shortage = [shortage prototype, random if left out]
cmd-wf_trade-hint-rush = [rush prototype, random if left out]
cmd-wf_trade-ledger-good = {$good}: {$stock} crates, {$owed} owed, {$made} made and {$used} used an hour, short at {$short} stations.
cmd-wf_trade-ledger-station = {$station} ({$role}):
cmd-wf_trade-ledger-row =   {$good}: {$stock} crates, level {$level}, priced at {$priced}, {$owed} owed, {$made} made and {$used} used an hour.
cmd-wf_trade-status-empty = No shortages, rush orders or consignments right now.
cmd-wf_trade-status-sector = whole sector
cmd-wf_trade-status-shortage = Shortage: {$goods} at {$station}, {$factor}x, {$seconds}s left.
cmd-wf_trade-status-rush = Rush: {$left}/{$count} {$goods} at {$station}, {$factor}x, {$seconds}s left.
cmd-wf_trade-status-consignment = Consignment for {$station}, carrier {$ship}, {$seconds}s to deadline.
cmd-wf_trade-status-supply = Supply at {$station}: {$goods} goods, {$soldOut} sold out, lowest stock {$lowest}, {$stops} freighter stops, blockaded {$blockaded}, short {$short}.
cmd-wf_trade-bad-shortage = {$arg} is not a shortage prototype.
cmd-wf_trade-bad-rush = {$arg} is not a rush prototype.
cmd-wf_trade-shortage = Started a {$goods} shortage at {$station}.
cmd-wf_trade-shortage-failed = No station buys those goods, or each one that does already has that shortage.
cmd-wf_trade-rush = Posted a rush order for {$goods} at {$station}.
cmd-wf_trade-rush-failed = No unblockaded station without an open rush buys those goods.
cmd-wf_trade-consignment = Posted a consignment as {$uid}.
cmd-wf_trade-consignment-failed = Couldn't post a consignment. It needs two unblockaded chute stations and one that isn't already a destination.
cmd-wf_trade-cleared = Ended {$shortages} shortages and booms and {$rushes} rush orders.
