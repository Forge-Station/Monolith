# Sector economy

Status: built on `tuning/markets`. It replaced the pricing half of the TradeGoods module and kept its crates, chutes, vendors,
freight and lathe demand. It is modelled on Starsector: stations produce and consume, prices follow how much demand is met, and
shortages have causes players can see and act on.

The numbers come from a simulation, not from played rounds. Replace them as real rounds come in.

## The model

Every station keeps one ledger, and everything in the economy reads it.

> A station holds a stock of each good. Its industries make some goods and its population uses others. Freighters carry the
> surplus between stations. Price follows how full the shelf is.

The background economy runs on rates. Real crates are only counted when players or freighters move them. Starsector's
developers tried moving large stockpiles between markets and found it unstable, so imports here are worked out from rates and
delivered in lumps by freighters, and stock never flows directly from one station to another.

## The ledger

`WFTradeMarketComponent.Ledger` holds one `WFTradeRow` per good a station trades:

| Field | Meaning |
|---|---|
| `Stock` | Crates on hand. Purchases, deliveries, production and use all change it. |
| `Priced` | The level prices see: stock smoothed so half of any gap closes every 2 minutes. |
| `Inbound` | Crates owed to this station, waiting for a freighter. |
| `Made`, `Used` | Crates per hour at the last tick, for the Sector tab. |
| `ScarceSince` | When the good last turned scarce here, which a shortage report waits on. |

A station's level for a good is `Stock / stockTarget`, and 1 is normal. The target is 120 crates and a shelf holds 200. The sector
view is the sum of every station's rows.

## Station roles

Roles are data (`wfTradeStation` in `Resources/Prototypes/_WF/TradeGoods/economy.yml`), keyed by POI or game map id, and don't depend
on which machines a map places. `PointOfInterestSystem` stamps each POI grid with `WFTradeStationComponent`, and the game map does it
for Caelestinus. A vendor and a chute are how players reach the ledger, and neither defines the role.

A grid with no role stands outside the economy: it trades, and its stock settles back to target on `halfLife`. Wandering merchants,
PDV Helios and the Underground work this way, which keeps their prices as they were and off the sector's books.

| Station | Population | Industries | Uses |
|---|---|---|---|
| Caelestinus | 2 | Food, Luxury goods, Reactors, Ship works | Supplies, Medical, Fabric, Luxuries, Fusion Fuel |
| Camelot | 1 | Chemicals, Fabrication, Reclamation, Refining | Supplies, Medical, Ammo, Fusion Fuel |
| Halcyon | 1.5 | Munitions | Supplies, Medical, Ammo, Weapon Parts, Ship Components, Fusion Fuel |
| Zvezda | 1 | Agriculture, Pharmaceuticals | Supplies, Medical, Luxuries, Fabric, Equipment |
| Cargo Depot | 0.5 | Agriculture, Munitions | Supplies, Medical, Ship Components |
| Cargo Depot (alt) | 0.5 | Food, Pharmaceuticals, Refining | Medical, Equipment, Ship Components |
| Anvilhold Drydock | 0.5 | None | Ship Components, Metals, Supplies |

Every good but Fission Fuel has a maker at a station that always spawns, so a round whose four depots all roll one way loses no good.

| Industry | Input | Makes | Crates an hour, sector-wide | Materials it can work |
|---|---|---|---|---|
| Agriculture | Supplies | Fertilizer, Fabric | 16 | |
| Chemicals | Fertilizer | Chemical Supplies, Volatile Chemicals | 11 | Plasma ore, coal, salt |
| Pharmaceuticals | Chemical Supplies | Medical Supplies | 23 | |
| Food | Fertilizer | Supplies | 40 | |
| Munitions | Volatile Chemicals | Ammo | 8 | |
| Refining | Scrap | Metals | 7 | Steel and copper ore, scrap ore |
| Fabrication | Metals | Components, Weapon Parts | 10 | Steel and copper sheet |
| Ship works | Components | Ship Components, Equipment | 11 | Plasteel, steel |
| Reclamation | Equipment | Scrap | 4 | |
| Luxury goods | Fabric | Luxuries | 10 | Gold, silver, diamond |
| Reactors | Fission Fuel | Fusion Fuel | 14 | Uranium |

A rate is the whole sector's, shared between the stations running the industry, so a round's mix of depots doesn't change how much
the sector makes. An output's rate scales with its input's level, capped at 1 with a floor of 0.25, as in Starsector: a station short
of an input makes less and a break in the chain spreads downstream, but it can always restart. Each crate made takes half a crate of
input.

Fission Fuel has no maker. It reaches the pool from outside at 14 crates an hour, which makes it the strategic import. So does
anything else no running station makes, such as in a preset without Camelot.

## The tick

Every 5 minutes, for every station:

1. **Use.** `Stock -= useRate x population x hours`, stopping at zero, for each good on its list.
2. **Make.** Each industry runs as above, and more with materials to work from.
3. **Export or hoard.** Stock over target goes into the sector's pool at 100% an hour, so a glut clears in about an hour. A blockaded
   station hoards instead (below).
4. **Import.** Each good's pool is owed out as `Inbound` to the stations that don't make it, by how far each is under target, never
   past the gap.
5. **Scarcity.** A good under 0.3 is marked scarce, and clears at 0.45.

Prices smooth every 5 seconds, apart from the tick. Stations only receive `Inbound` when a freighter arrives, so losing one loses real
supply.

The first tick of a round opens every ledger, rolls each row between 25% and 100% of target and runs 8 hours of this with assumed
freight, so a round opens with the sector's surpluses and gaps already in place.

## Prices

One curve for chutes and vendors (`WFTradeMarketSystem.LevelFactor`):

```
level < 1:  factor = 1 + 1.5 x (1 - level)^2       level 0.6 = 1.24x, 0.2 = 1.96x, 0 = 2.5x
level >= 1: factor = 1 - 1.2 x min(level - 1, 0.5)  level 1.25 = 0.7x, 1.5 = 0.4x
```

- A vendor charges its vendor price x factor at `Priced`, never below base. A purchase leaves the shelf at once and reaches the price
  over a few minutes.
- A chute pays its table price x 0.9 x factor, stopping at the existing `floorPrices`, so a glutted market still covers a bought crate.
  Contraband keeps its street price and is outside all of this.
- A chute pays by the higher of `Level` and `Priced`, so a sale cuts its pay at once and a big haul walks down the curve, while stock
  bought away only raises its pay after the delay. Otherwise a whole hold would fetch the first crate's price.
- Selling a crate adds to `Stock`. A bulk order ignores the shelf while it's open, so its last crate pays what its first did.
- Event modifiers (booms, rushes, blockades, raids, war) multiply on top. Events are capped together at `modifierCap`, and the whole
  price at 3x base.
- Chutes keep refusing crates bought on their own station, so nothing can be bought and resold on the spot.
- A chute takes a good off its table only below level 0.3, at the best rate any open chute lists. This keeps the approved rule that
  anyone can bring a dry station what it needs.

The squared curve keeps prices near normal for small dips and steep near empty, which keeps the spirit of the old rule that scarcity
only bites when stock runs low, without a hard edge at 20%. The glut side is steeper than first drafted (1.2 against 0.6), so a sale
dents a price by about 1% a crate, close to the 1.75% it did before.

## Freight and blockades

Freighters are the visible part of the import flow. A run still picks a manifest and route, leaning toward stations owed the most and
manifests carrying what its stops are owed. A stop delivers what that station has `Inbound` of what the hold carries, up to its share
of the hold, at 25 owed crates per crate aboard. Real crates leave the hull only as they're delivered, so stolen cargo cuts the
delivery and a stripped hull brings nothing. Delivery moves prices at once. Materials stay on their manifests as before. A lost run
writes off what its remaining stops were owed, and they go short.

A blockaded station can't be reached, by raiders or a hostile faction, and freighters skip it. Its imports go off the shelves toward
level 0.25, halving the distance every 15 minutes, so most of the effect lands in the first half hour. What's hoarded stays owed to
it, so the first ship through after the blockade brings relief. The goods it makes itself stay on its shelf. A flat 1.25x on
essentials applies from the first minute, and the shelf does the rest.

## Players in the loop

- **Haulers** deliver to any chute. Every delivery adds to `Stock`, so filling a dry station really does relieve it.
- **Workshops** consume crates as lathe materials, which is the demand the ledger can't generate itself.
- **Smugglers** sell to the Underground, whose sales never reach a working station's books, so they never relieve a shortage. This is
  the Starsector black market rule.
- **Raiders** cut supply by destroying freighters and stealing cargo.
- **Miners** feed industries. An industry with materials in its station's cargo market makes half as much again, at 1000 spesos of
  material a crate, drawn from the stock pallets fill. Ore sold to a pallet becomes Metals in the sector's supply and clears the glut
  that was lowering the miner's pay.

Pallets and consoles keep their own curves rather than the crate curve, because the gap between them is what stops selling and buying
back at a profit (`SellingAndBuyingBackNeverProfits`).

## Upkeep

A shelf running low costs more than its price (`wfTradeService`):

- **Drydock work** at Anvilhold costs the curve at its Ship Components level, up to 2.5x with none left. Mobile drydocks pay in
  materials and are unchanged.
- **Patrols** fly less the less ammunition the sector holds: their scheduling weight is full down to a mean Ammo level of 0.6, a normal
  sector, then falls with it to a quarter. They thin out but never stop.

The Market app warns when either has fallen by a fifth. More services can follow one at a time: add a `wfTradeService` and read
`ServicePrice` or `ServiceState` where the service is priced or scheduled.

## Events that read the ledger

- **Shortages** are reports of real gaps. On the existing 25 to 40 minute schedule, the worst good scarce for at least 15 minutes is
  announced with its cause: a blockade, a lost freighter, a missing input or supply falling behind. A report adds nothing to the price,
  since the price already shows the gap, and ends when the shelf recovers. Booms stay random, as outside demand. An admin can still
  force a shortage with no gap behind it, which raises its group for its duration.
- **Rush and bulk orders** go to the station furthest under target for their group, and scarce groups are likelier.
- **Freight alerts** say what each scarce station is waiting on.

## Market app

A Sector tab shows, per good: crates in the sector and waiting on freight, made and used per hour, and which stations are short. Rows
on station cards have a shelf bar, and vendor rows still show crates left. `wf_trade ledger` prints the sector, and `wf_trade ledger
<station>` every row of a station.

## Numbers

From a simulation of nine stations with real roles and manifests, 3 runs an hour of 3 stops, after a 10 hour warm-up. It leaves out
player trade, materials and upkeep:

| Check | Result |
|---|---|
| Stability | Settles with no oscillation over 24 hours |
| Mean level of imported goods | 0.60, with 13% scarce, 51% tight and 37% fine |
| Mean level of a station's own goods | 0.93 |
| Blockade of 30 minutes | Level 0.47 to 0.32, 1.47x to 1.71x |
| Blockade of 1 hour | Level 0.47 to 0.28, 1.79x, so most of it lands in the first half hour |
| 2 hours after a 1 hour blockade | Level 0.34, with what's owed still waiting on matching freighters |
| 40 crates dumped over target | Level 1.33 to 1.08 in an hour |

## Risks

- **Money.** Imported goods sit under target, so chutes usually pay a premium: about 1.24x at the mean level, or about 1.1x after the
  0.9 table rate. Watch real income. `chuteTableRate` is the knob.
- **Freight throughput.** Manifests, not production, cap the sector. Medical Supplies are on one manifest in eight, so a station can
  wait hours for them. In the simulation raising production hardly moved imported levels, while lowering use did. If imports run too
  low in real rounds, widen manifests or raise run frequency before touching rates.
- **Recovery.** A station stays low for hours after a blockade if no matching freighter comes, so relief depends on players. That suits
  haulers, but watch it.
- **Too few stations.** `EveryGoodIsMadeAtAStationThatAlwaysSpawns` guards the roles.
- **Server load.** A tick is about 9 stations x 16 goods every 5 minutes, and 8 hours of them at round start. More freighters may
  matter more.
