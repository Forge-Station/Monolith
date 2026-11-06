# Lattice design

Design for the sector's boundary. Kyphrus is an anomalous black hole whose pull on reality, the Unmooring, is held off
by the Lattice: reality anchors round Caelestinus Central. Past their reach ships build up Drift, and their crews see
and hear things that are not there. Further out, things that are. The further out, the richer the space, and exotic
matter is found only in the deep bands. All seven steps of the build order are built; the Choir is its opt-in core, not yet a full campaign.

## Lore

- **The Unmooring:** Kyphrus loosens physics and perception around it. Unchecked, it reaches the whole sector.
- **The Lattice:** six primary anchors, raised before the sector was settled, hold a stable bubble round the core.
- **Drones:** Lattice maintenance drones. Those still in range of a working anchor guard it; those whose anchor failed,
  or that wandered out of the field, went feral and attack anything, because they read every hull as an anomaly. That
  is why drones thicken with distance, and the drone rings become the anchor bands.
- **The Choir:** wants the Lattice down so the song can reach everyone. They are at home in the Coda.

## Bands

Field strength at a point is the sum of every anchor's contribution, so the edges are lobed, not circles. Distances
below are for an intact Lattice and slide inward where anchors fall.

| Band | About | Drone zone | Drift | Threats | Reward |
|---|---|---|---|---|---|
| Unison | 0-18 km | none | fades | none | baseline |
| Detune | 18-24 km | 1 | slow | comm static, rare whispers, phantom radar blips | more ore, more metallic rocks |
| Dissonance | 24-31 km | 2 | moderate | false hails, ghost contacts, footsteps, glimpses, screen warping | rich ore, rare exotic matter |
| Feedback | 31-40 km | 3 | fast | hallucinated Remade, nav drift | exotic matter deposits |
| the Coda | 40 km+ | 4 | exponential | small animals aboard turn Remade (low chance), Monolith hulks | the best salvage, dense exotic matter |

- **Edges follow the drone rings.** The default worldgen (`MonoDefault`) is nearly empty inside 18 km apart from the
  9 km asteroid ring, Cluster A and the Wilds, and its Very Far biomes start drones at 18, 24, 31 and 40 km with no
  outer edge. The bands take the map's drone ring radii as their edges, so the Coda is the existing Very Far T2 Far
  biome and needs no new worldgen. The Wilds hold drones inside Moored space; lore can make them a dead anchor's site.
- **Garbling:** the Coda's name is never shown cleanly (`the D#e$p`, `#h$ D%%@`, then noise), and popups, radar labels,
  ship names and coordinates garble on the same exponential curve with depth. Character substitution, not combining
  marks, which the game fonts may not draw.
- **Crossing a band** shows a popup and a chip: leaving anchored space, restricted unsafe space, and the yield bonus.
- **Radar:** the drone rings are relabelled as bands, and the shuttle console shows Lattice stability.
- **Monolith ships** (the hulk, the guarded hulk and the adrift freighter) spawn in the Coda only, 41-47 km from the
  centre (`latticeBand` on the encounter), and the coordinates in the Scion hint whispers garble.

## Drift

- **Per ship.** Everyone aboard shares the grid's Drift. Off a ship (EVA, stations, wrecks) it follows the person.
- Builds by band and fades inside the Moored band, so a short dip is cheap and a long haul gets worse. Effects come
  from Drift thresholds, not position alone.
- **Planets count:** a planet's surface and orbit take the Drift of the world's position in the sector.
- **Hallucinations are client-only:** each player sees their own phantom contacts, hails and figures, and nobody else
  does. No networked entities.
- **Remade in the Coda:** the Unmooring takes the smallest minds first. A pet or mouse aboard may turn into a Remade
  critter (`Remade/Critters`). A running stabiliser prevents it.

## Counterplay

- **Ship stabiliser:** a machine that draws power and fuel and keeps a small Moored bubble round the ship. It holds
  Drift down and stops Deep conversions. Built with exotic matter.
- **Anchor charm:** personal gear that slows Drift for one person off a ship. Built with exotic matter.

## Anchors

- **Six primary anchors** at the corners of a regular hexagon round Caelestinus Central, which sits under Kyphrus. The
  hexagon matches the SectorControl hex grid.
- **Heavily fortified:** each is a station with ShipShields shields and a garrison of loyal drones. They can be
  destroyed, but only by an organised raid. Losing one opens a breach wedge in its direction, announced sector-wide.
- **Relay anchors:** players build them with exotic matter and power. They are smaller and far less fortified, push
  the Moored band outward, and are the Choir's softer target.
- **Lattice integrity** is a value on the sector map that clients read.

## Kyphrus reacts

The client feeds Lattice integrity into the `WFBlackHole` shader. As anchors fall, lensing strengthens, the disk flares
and stutters, its colour moves from warm orange towards violet, doubled disk images appear and the edge of the shadow
tears. From the Coda it always looks wrong, whatever the Lattice's state.

## Exotic matter

Found only in the Dissonance band and further out. It sells for a lot, and it is the ingredient for stabilisers, charms
and relay anchors. It is stable and does not decay. Its price and spawn rates need tuning against the Mining module's
150-minute voucher ladder.

## The Choir as a role

A player faction, built like the ADS and Reaver campaigns: tiers, DEFCON scoring, campaign events. A crew whose Drift
runs high hears the song and may choose to join. Nobody is converted against their will. Their chapel is in the Coda,
Drift does not harm them, and their aim is to break anchors and carry Scions inward. It needs admin support and
guardrails for low population, so it comes after the Lattice itself.

Built so far: the opt-in offer at high Drift, the faction, the briefing, admin alerts and logs, population and member
caps, and DEFCON scoring. Still to come: a Choir chapel in the Coda, campaign tiers and events, and objectives.

## Build order

1. Anchors and the field: an anchor component, per-grid Drift, band changes with popups and chips, radar labels.
2. The hulk move, garbling of radar labels, coordinates and hints.
3. Hallucination effects by Drift threshold. This reuses `Remade/Dread`, which first needs splitting from its Monolith.
4. Exotic matter, the stabiliser and the charm; yield bonuses by band in `AsteroidMining`.
5. Primary anchor stations, their shields and loyal drones, relay anchors, breach wedges.
6. Kyphrus shader states.
7. The Choir role.

## Open questions

- How many relay anchors a faction may hold, and whether SectorControl cells gate where they go.
- Stabiliser fuel: plasma, exotic matter, or power alone.
- What a destroyed primary anchor takes to rebuild, if anything. For now it can't be; relays fill the gap.
