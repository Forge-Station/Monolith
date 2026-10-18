# The ADS incursion

A second sector threat on `_WF/SectorControl`, beside the Ashfall Reaver campaign (`Docs/_WF/ReaverCampaign/design.md`):
the Automated Defense System, the pre-Fracture silicon fleet whose derelict hulls the storyteller already drops in as
"unknown shuttles" (Zenith, Nebula, Wyrm, Razor N). Here they are awake, organised and purple on the map. Where the
Reavers are a gang that grows, raids and hunts, the ADS is a machine that holds ground, disarms what crosses it and
follows its directives to the letter. Rounds run about six hours; every number below is tuned to that, and to the
Reavers running in the same round.

## Why it is beatable

- **It asks before it shoots.** Every ADS zone is `zoneTargets: Interrogate`: a player ship in a warning zone is hailed
  with "DIRECTIVE QUERY. ARE YOU A THREAT TO THE AUTOMATED DEFENSE SYSTEM?" and offered YES and NO. Only a ship that
  answers YES (the encounter's `Declared` list) or fires on an ADS ship is warned off and fired on; NO clears it for
  15 minutes, no answer for 3, and nobody else is ever shot at, civilian or navy, IFF or none. The hulls are far too
  strong to fire on bystanders.
- **It disables, it does not kill.** Every ADS ship fights with `disengage: Disable`: once a ship is combat-ineffective
  (`WFCrewShipStatusSystem.IsDisabled`) its guns stop. Nobody is hunted, boarded or chased across the sector. The
  Disarmament Directive is the rule, not flavour.
- **It never touches population centres.** The ADS never claims, contests or blockades a cell that holds a station
  (Asset Preservation Directive; `blockades: false`), so every station stays a safe harbour and the front can never
  reach the core.
- **It never fires first, and it never turns on itself.** Its crews are Veteran and its hulls are plated in adamantite
  (the profile swaps the plastitanium in), so what it does fight it outlasts; but nobody is shot at who has not said YES
  or opened fire, and ADS ships never count one another as attackers whatever fire passes between them (`forgivesOwn`).
- **It stands still.** Nodes are gun platforms that never leave their cell. Sentinels circle the node and sweeps fly
  fixed routes through held space; both give up once out of held space. No raids on player ships, no marks.
- **It is small.** Fewer nodes than Reaver strongholds, no docking reinforcements, no shields on the light hull that
  founds a node (Nebula). Growth is a sentinel wing at a time, capped by tier.
- **It has a rival.** From tier 2 the ADS spends its effort contesting Reaver space, and the Reavers answer with gunships.
  Two threats in one round fight each other as much as the players.

## Faction and company

`WFSectorFactionAds` (`Resources/Prototypes/_WF/AdsIncursion/factions.yml`): name "Automated Defense System", colour
purple `#9B4DFF`, company `WFAds`, `blockades: false`, `enteredNotice`/`leftNotice` in directive voice
("DIRECTIVE: you are entering an Automated Defense System exclusion zone (K-7). Military and unidentified craft will be
made combat-ineffective.", now "Your vessel will be queried. Only declared threats and attackers are made
combat-ineffective."). Company `WFAds` (`companies.yml`, `disabled: true`, same colour). Standing `WFAds` at war
with TSF, TSFCivilian, TSFHighComm, PDV, PDVCivilian, PDVHighComm and WFAshfallReavers (war is mutual, so the Reavers
are at war with it too). NPC faction `WFAds` (`ai_factions.yml`): `defaultHostile: true`, neutral to pets and
`SimpleNeutral`, as `WFPdvNavy` is set up.

## Hulls and crews

The hulls are copies of Mono's ADS maps in `Resources/SharedMaps/_WF/AdsIncursion/` (vessel grids must live in
`SharedMaps`): `nebula.yml`, `wyrm.yml`, `zenith.yml`, `razorn.yml`, `wyvern.yml`. The copies lose their ghost-role
spawners (`SpawnRedactedBorg`, `SpawnRedactedBorgPlaytime`, `PlayerStationAiRedacted`, and the Wyvern's
`StationAiUploadComputerUnlocked`): a ghost at the helm of a node would fly the threat away. The turret-mob spawners (`SpawnMobWeaponTurret*`) stay: an automated hull defends its decks. Vessel
prototypes `WFAdsNebula`, `WFAdsWyrm`, `WFAdsZenith`, `WFAdsRazorN`, `WFAdsWyvern` (`vessels.yml`), parent
`BaseVesselIFFDisabled`, `purchasable: false`, as `WFChengdu` is. The Wyvern is class Capital, priced 11,840,000 just
above its appraisal so the shipyard arbitrage test holds.

| hull | guns | role |
|---|---|---|
| Nebula | Sunder, Scythe, Harbringer, Flayer; no shield | node (the hub) |
| Wyrm | Entropy torpedo, Tartarus, Flayer | sentinel, sweep, interdictor |
| Zenith | Sunder, Scythe, Reaper, Flayer; shield | reserved: a fortified node at tier 5 (not built in this pass) |
| Razor N | none; scuttle charge | not used by the campaign |
| Wyvern | capital batteries; turret mobs | the tier 5 dreadnought |

Crews are `WFCrewProfileAds` (`profiles.yml`): automatons. Bodies are an IPC crew body (`WFMobCrewBodyIpc`, the IPC
mob base with `MobPrying` and `WFCrewKit`, built as `WFMobCrewBodyHuman` is) if the IPC base takes the crew kit
cleanly, else human bodies in ADS gear. Components: `PressureImmunity`, `BreathingImmunity`, a robotic accent if one
exists. Names from two datasets: designations (Glimmer, Orion, Expanse, Symphony, Chorus, Flashpoint, Phoron, Conquest,
Prime, Firestorm, Retaliation, Horizon, Marksman, Barricade, Vein, Zero, Midnight, Interdiction, Mist, Granite, Typhoon,
Storm, Rubicon, Cauterizer, Evasion, Apprehension: Mono's ADS core names) and serials ("Unit 03" ... "Unit 99"). Gear:
naked robots, no clothing or suits: `WFCrewAdsUnit` and `WFCrewAdsOfficer` (energy sidearm on the belt),
`WFCrewAdsSentry` (energy rifle on the back too); the belt and back slots need no jumpsuit. The IPC body carries
`PressureImmunity` and `BreathingImmunity` itself, so a post in vacuum never gets it a void kit; skills
`[ Veteran ]`. Zone lines `wf-ads-zone-*` (`Resources/Locale/en-US/_WF/AdsIncursion/ads.ftl`) in
directive voice, said only to a declared threat or an attacker: "DISARMAMENT DIRECTIVE. {$intruder}, you are logged as
a threat. ..." The query lines `wf-ads-zone-hail-question`, `-hail-yes`, `-hail-no`, `-hail-declared` and
`-hail-cleared` go over the hail. The attack line: "Compliance window closed. {$intruder} will be made combat-ineffective."

## Encounters (`encounters.yml`)

All `start: Manual`, off budget, `company: WFAds`, `faction: WFAds`, `profile: WFCrewProfileAds`, `distress: false`,
`zoneTargets: Interrogate`, `disengage: Disable`, `iffColor` purple, `deed: null` (nobody gets a deed to a machine).
`Interrogate` is generic (`Encounters`): the zone fires only on vessels in the ship's `Declared` set or known attackers,
and `WFEncounterInterrogationSystem` hails every other crewed player ship in the warning zone from the ship's helm with
its pilot (any living crewman will do), asks the `{zoneLines}-hail-question` and offers YES and NO. YES declares the
vessel to every ship of the encounter for its life; NO puts it on a 15 minute cooldown, an unanswered hail on 3. An
interrogating ship turns on no rival encounter ship by itself either; it answers fire and rivals that turn on it.

- `WFAdsNode`: lead Nebula, `platform: true`, `evades: false`, deckhands 2, guards 2, engineer, `fuelStores: 12`,
  skill Regular, warn 1800 m, attack 1000 m, disengage range 1500 m, `objectives: [ Hold ]`, `leash: 2000`,
  `icon: Flag`, `lifetime: Persistent`. Manifest `WFManifestAdsTech` (ADS repair tools and energy weapons: what the
  hull is worth boarding for).
- `WFAdsSentinel`: lead Wyrm, Persistent, `evades: false`, pilot and gunner and one hand, warn 1200 m, attack 700 m.
  The campaign orders it `GoTo` its node (500 m) then `Circle` it.
- `WFAdsSweep`: lead Wyrm alone, Transient, `duration: 3600`, warn 1200 m, attack 700 m, no wing.
- `WFAdsInterdictor`: lead Wyrm, Persistent, `evades: false`, warn 1200 m, attack 700 m, `objectives: [ Hold ]` once
  arrived. The conflict boat (below).
- `WFAdsDeployment`: lead Nebula, Persistent; a later node flying out from a standing one, as a Reaver convoy.
- `WFAdsDreadnought`: lead Wyvern, Persistent, `platform: false` (a ship under way), `evades: false`, deckhands 8,
  guards 6, engineer, `cargo: 4` of `WFManifestAdsTech`, `fuelStores: 24`, skill Veteran, warn 2500 m, attack 1500 m,
  `icon: Skull`, no leash. The campaign gives its orders (below).

The Reavers gain `WFReaverResponsePack` (`Resources/Prototypes/_WF/ReaverCampaign/encounters.yml`): raider and wing as
`WFReaverRaidPack`, Transient, `duration: 3600`.

## The campaign (`wfAdsCampaign`, one per storyteller preset)

`WFAdsCampaignSystem` in `Content.Server/_WF/AdsIncursion`, built on the Reaver campaign's shape (partials by concern,
one-shot public methods for tests and the command, state on `WFAdsCampaignComponent` on the sector map entity). Runs
only while `wf.encounters.enabled` and `wf.sector.enabled` are on, the round is in progress and rule
`WFAdsCampaignRule` runs (`wf.ads.enabled` is the admin override). The rule joins the `WFContestedSector` preset beside the
Reaver rule: Contested Sector is the sector-threats mode, and the two fight each other there. `addgamerule WFAdsCampaignRule`
starts it by hand in any other mode.

**Nodes.** `firstNodeDelay` after `StationsGeneratedEvent`: a quiet cell 16–26 km out, clear centre
(`IsClearSpace` 600 m grid, 6 km station), 8 km from any living player, no station in the cell, and at least three
cells from any cell another faction holds (the ADS and the Reavers start apart and meet by spreading). Founding claims
the cell and its ring (never a cell with a station: those are skipped). A node is **broken** when its lead is out of
the fight (as a Reaver hub: crew dead, disabled, wrecked, or two minutes unmanned; a hull dark only from lost power
gets `StarvedGrace`); the campaign resolves it `Destroyed`, pays `nodeBounty[tier] × (1 + 0.5 × sentinels alive)` to
those who hit it (sentinel hits folded in) and the 20 % support pool, releases its cells and announces. `Ended` pays
nothing. Later nodes fly out as **deployments** from the standing node nearest the target, as Reaver convoys do
(`WFAdsDeploymentComponent`, contest shown, overdue, stopped pays half). **Regroup**: no node for `regroupDelay` with
no deployment out founds a fresh one.

**Sentinels.** A node that has stood `sentinelInterval` since founding or its last sentinel, with sentinels under
`maxSentinelsByTier[tier]`, gets one: spawned at a clear point 25–28 km out on the node's side (else 3–5 km from the
node), ordered `GoTo` the node then `Circle` it (400 m). State on `WFAdsNodeComponent` (`Sentinels`, `Inbound`,
`NextSentinel`). A sentinel out of the fight resolves `Destroyed`, pays `sweepBounty`, and the node tries again after
the interval. A node under attack sends its sentinels to `Attack` the top attacker, back to the circle three minutes
after the last hit with no attacker within 2.5 km.

**Spread.** Every `spreadInterval`, one frontier cell: unowned, adjacent to a held cell, within `supportRange` rings
of a node (its source), quiet for `quietMinutes`, holding no station, not disputed, not vetoed; weighted toward the
origin. Never contested, never a blockade: the ADS either takes empty quiet space or leaves it.

**Sweeps.** Each node keeps up to `sweeps[tier]` out, under a campaign cap of `clamp(players / 6, 1, 4)`: route of
three to five held-cell centres then home; resolves `Completed` home; one lost frees the cell its lead died in (unless
a node stands there) and pays `sweepBounty`. Called back once 10 s outside held space and its ring.

**Interdiction.** A crewed player ship inside ADS space for `interdictDwell` and off its cooldown gets the nearest sweep
within three cells, else a fresh one from the nearest node, ordered `GoTo` it (600 m) then `Hold` 180 s, so its zone
queries the ship; it fires only through the zone, on a declared threat or an attacker. The ship is told "DISARMAMENT
DIRECTIVE. ADS interdictor closing on Hauler to query it, bearing 045, 3.2 km." over PA and chat, and common hears
it. Ends when the target is disabled, gone, 20 s out of held space or 20 s out of reach; then the sweep flies on
and the ship gets `interdictCooldown`. Capped by `interdicts[tier]`. No marks, no raids.

**Threat.** Score = nodes × 10 + held cells + 20 × (1 − nearest held cell's distance / 27 000), + 10 while the
dreadnought is out. Tiers from
`tierThresholds`: Dormant, Waking, Active, Alert, Fortified, Overrun. Rise at once, fall after 10 min under, never
within 10 min of the last announcement. Announcer `wf-ads-watch` (the sector watch), rising `/Audio/Misc/redalert.ogg`
in purple, falling `/Audio/Misc/notice1.ogg` in teal. Legend: "Automated Defense System: Active, 2 nodes, 14 cells,
bounty 100,000".

**Announcements.** The sector watch speaks in three tiers so a banner stays rare. A **chimed banner** (global, rising
or falling sound and colour) is for tier changes, a node founded (first, regrouped or deployment arrived), a node broken
and the ADS going dormant or finished; a rising one is sent as "Anomaly Alert" with the malfunctioning-AI klaxon from Meridian Rift
(`/Audio/_WF/AdsIncursion/aimalf.ogg`), a falling one by the sector watch with the notice tone. Every ADS banner is set in the
synthetic readout face. The capital event, the dreadnought activated, also plays the campaign's `capitalSound`
(`/Audio/Misc/gamma.ogg`, the alert-level sound) on top of its chime; its destruction keeps the falling chime alone. Minecraftia as font `WFAdsSynth` (its own wrap, `wf-ads-announcement-wrap`, since the chat system escapes markup in a message),
and the watch entity speaks with the `WFAdsSynth` verb, so its radio lines read the same way. A **quiet banner** (global, no sound) is for a deployment under way or stopped and a challenge
launched; at most one goes out per campaign every 5 minutes (`QuietBannerInterval`, `LastQuietBanner` on the
campaign), and one inside the window is sent as a radio line instead, never dropped. Everything routine is a **radio
line** on the Common channel from the watch entity, with no banner but still in every chat log: sentinels joined and
lost, sweeps destroyed, a challenge won, lost, withdrawn or its interdictor and picket destroyed, clearing and retaken.
Zone, interdict and ship warnings are unchanged.

**Retakes, clearing, disputes, pause, presence, round summary, admin command** as the Reaver campaign: a standing
node's cell is never retaken; freed cells clear; every campaign ship's cell is reported with `SetPresence` each tick
and a disputed cell holds the node's sentinel clock and a deployment's deadline; `wf_ads status | found [x y] | deploy |
sentinel <node uid> | tier <n> | spread | sweep | interdict <grid uid> | challenge [q r] | dreadnought | pause | resume | clear`.

| | Quiet | Standard | Dangerous |
|---|---|---|---|
| firstNodeDelay | 70 min | 35 min | 25 min |
| regroupDelay | 45 min | 30 min | 20 min |
| spreadInterval | 25 min | 15 min | 9 min |
| quietMinutes | 15 | 10 | 6 |
| deployInterval | 100 min | 70 min | 45 min |
| cellsPerNode | 8 | 7 | 6 |
| maxNodes | 2 | 3 | 4 |
| supportRange | 2 | 2 | 3 |
| sweeps by tier (0–5) | 0,1,1,1,1,2 | 0,1,1,1,2,2 | 0,1,1,2,2,3 |
| sweepInterval | 18 min | 12 min | 8 min |
| interdictDwell / cooldown | 90 s / 10 min | 90 s / 10 min | 90 s / 10 min |
| interdicts by tier | 0,1,1,1,1,2 | 0,1,1,1,2,2 | 0,1,1,2,2,3 |
| sentinelInterval | 25 min | 15 min | 10 min |
| maxSentinelsByTier | 0,0,1,1,1,2 | 0,0,1,1,2,2 | 0,1,1,2,2,3 |
| challengeTier / interval / deadline | 2 / 35 min / 10 min | 2 / 25 min / 10 min | 2 / 15 min / 10 min |
| maxChallenges | 1 | 1 | 2 |
| tierThresholds | 10,22,40,60,80 | 10,25,45,70,95 | 12,30,55,85,115 |
| nodeBounty by tier | 50k, 75k, 100k, 130k, 170k, 220k (all) | | |
| sweepBounty / interdictorBounty | 18k / 30k | | |
| retakeBounty by tier | 8k–20k as the Reavers' Quiet/Standard/Dangerous ladders | | |
| dreadnoughtDelay | 20 min | 10 min | 6 min |
| dreadnoughtBounty | 400k | 400k | 400k |

Credits as the Reavers' (`FederationMilitaryCredit1` for TSF, `Doubloon1` for PDV; node 30, sweep 10).

## Dreadnought

The capstone of an Overrun round: one capital hull with one directive, the Ashfall Reavers' strongholds, fought over as
the round's set piece and the ADS's end. Off when the campaign's `dreadnought` is null.

**Activated.** `OverrunSince` on the campaign state marks when tier 5 was reached and is cleared when the tier drops.
Once the campaign has stood at tier 5 for `dreadnoughtDelay`, with no dreadnought out and the campaign not finished, it
activates one (`TryLaunchDreadnought`) off budget at a clear point 600–1000 m from the strongest standing node's lead:
most sentinels alive, else nearest the origin. `WFAdsDreadnoughtComponent` holds its source node, its target and when
it set out, and `Dreadnoughts` counts it. The system announces it as an anomaly alert, a chimed rising banner with the
capital sound on top: "DIRECTIVE UPDATE. Dreadnought-class asset activated at K-7. Hostile strongholds will be
neutralised." An activation that finds no node or no clear point is tried again after a minute. `wf_ads dreadnought`
activates one now at any tier and past no delay, but never a second while one is out.

**Hunting.** Its target is the nearest standing Reaver stronghold (`WFReaverCampaignSystem.Strongholds()`); the citadel
counts as one and is preferred whenever it stands. Orders: `GoTo` the hub (800 m), then `Attack` the hub's lead hull.
When that stronghold resolves or its hub is gone it sets on the next; with none standing it flies `GoTo` the nearest
node (1000 m) and `Hold`, and looks again once a minute. Its cell is reported as ADS presence like any ADS ship, but a
dispute in its cell holds nothing: it is a ship under way. It fires on Reaver reinforcements, patrols and packs only
when they fire on it, through its crew's own alert, and on a player ship only by the interrogation rules (YES, or fire
first).

**The Reavers answer it.** A Reaver hub counts only player hits in its encounter's `ShipHits`, so the dreadnought's
own hull and shield hits on a hub or its reinforcements are counted in the hub's `NpcHits`
(`WFReaverCampaignSystem.RecordCampaignHit`, fed by the ADS system from `WFCrewHullHitEvent` and
`WFShipShieldAttackedEvent`). They add to the hub's `Hits`, so a fresh one breaks its reinforcements away at the
dreadnought and, with none docked, sends a sortie pack after it (unwarned: there is nobody aboard to warn), exactly as a
player's would; its crew's alert already makes the hub's gunners fire back. They pay nothing and mark nothing, and a hub
brought down by them is broken (`Destroyed`), not starved. A stronghold broken with a dreadnought among its attackers is
announced through the Reaver side as any broken stronghold and counted in `StrongholdsBroken` for the ADS round summary.

**Destroyed.** Out of the fight as a node is (lead out of the fight), it resolves `Destroyed`, pays `dreadnoughtBounty`
to those who hit it with the credits' node amounts, and the sector watch says with the falling chime "The ADS
dreadnought is destroyed at K-7. Bounty n thalers paid. The Automated Defense System has gone dormant for good." That
is the ending: it does not come back. `DreadnoughtsDestroyed` counts it and `Finished` on the state is set, holding
until `wf_ads clear` or the round restarts. While it holds the campaign founds, regroups, spreads and launches nothing:
no deployment (one under way is recalled, as it would found a node), sentinel, sweep, interdiction, challenge or
dreadnought, by the tick or by the `Try` methods. Nodes, sentinels, sweeps, interdictors and pickets still out fight on
until they are broken, and their cells free as usual. The tier still follows the score, but a rise is never announced
again, and the last node breaking adds no "dormant" line. The legend reads "finished", `wf_ads status` says so, and the
round summary notes it with the dreadnought counts. Ended by an admin or the round, it pays, announces and finishes
nothing.

## Sector conflict

The framework gains **challenges** (`Content.Server/_WF/SectorControl`): a contest on a cell another faction holds.

- `WFSectorTerritorySystem.Challenge(map, cell, faction, deadline, source)`: fails unless the cell is held by another
  faction and uncontested, and the source lives. Stored as a `WFSectorContest` with `Holder` set to the owner.
  `WFSectorCellContestedEvent` and `WFSectorContestResolvedEvent` gain a trailing `Holder` (null for a plain contest).
- Resolution in `CheckContests`, at the deadline, while the cell is not disputed (a dispute holds the deadline as it
  does for any contest): the challenger **wins** when its source lives, the challenger has reported presence in the
  cell within the presence window and the holder has none; the cell then **transfers**: the holder's claim goes
  without clearing, a claim with the challenger's source takes its place and `WFSectorCellChangedEvent(holder,
  challenger)` fires. Otherwise the challenge is dropped. The player-quiet rule does not apply to a challenge; the
  claim-attempt veto does (protected cells can't be challenged either). A challenge whose source dies is dropped.
- Retake countdowns skip a challenged cell (they already skip contested cells); spread (both drivers) skips it.
- Client: the challenged cell keeps the holder's fill and gets the challenger's hatch and countdown (the contest
  drawing already does this on a held cell); the hover line reads "held by Ashfall Reavers, challenged by Automated
  Defense System, m:ss" and the blip card the same. No new wire fields: a contest on a held cell is a challenge.

**ADS challenges.** From `challengeTier`, every `challengeInterval`, with challenges out under `maxChallenges`, the
campaign picks a cell held by another faction: no station in it, not contested, not disputed, none of its claim's
sources standing in it (a Reaver stronghold's own cell is never challenged; it falls only with the hub), preferring a
cell adjacent to ADS space, else the nearest to ADS space within four cells of it. It spawns `WFAdsInterdictor` at a
clear point 600–1000 m from the nearest node's lead with `WFAdsInterdictorComponent` (target cell and point, node,
launch time, overdue = twice the reckoned trip + 3 min), orders `GoTo` the centre (300 m), calls `Challenge` with a
deadline of the reckoned arrival + `challengeDeadline`, moved with `SetContestDeadline` as it flies, and announces:
"ADS interdictor under way to K-7, contesting Reaver space. 4.2 km bearing 045 from Outpost Lambda." Arrived, it holds
(its cell is its presence). Overdue short of the cell: ended, challenge dropped (`DropContest`). Lead out of the fight:
resolved `Destroyed`, `interdictorBounty` paid to those who hit it, challenge dropped by the framework, announced
"ADS interdictor destroyed at K-7." On `WFSectorContestResolvedEvent` with the ADS as the challenger: **won** makes the
boat a **picket** (`WFAdsPicketComponent`): it keeps holding, its encounter is the claim's source so the cell frees
when it dies, it reports presence, it is not a node (no sweeps, no sentinels, no score beyond the cell), announced
"K-7 has fallen to the Automated Defense System."; **lost** resolves it `Completed` (it jumps out and is swept),
announced "ADS interdictor withdraws from K-7."

**Reaver response** (`WFReaverCampaignSystem.Conflict.cs`): on `WFSectorCellContestedEvent` with `Holder` the
Reavers and another faction challenging, with no response out for that cell and a stronghold standing, the nearest
stronghold launches `WFReaverResponsePack` (off budget, `WFReaverResponseComponent`: cell, target encounter, source
stronghold) with orders `GoTo` the cell centre (600 m) then `Attack` the challenger's lead grid, then `GoTo` home; the
sector watch says "Reaver gunships are scrambling to K-7 to drive off the intruder." (a radio line). The pack's cells
count as Reaver presence, so a pack alive in the cell at the deadline keeps it Reaver. The pack flies home and resolves
`Completed` when the challenge resolves or its target is gone; destroyed by players it pays `patrolBounty`, and it
breaks as a patrol does (lead out of the fight). A Reaver cell lost to a challenge is announced "K-7 lost to the
Automated Defense System." and counted (`CellsLost`) for the round summary; the hub that supported it keeps its others.

The symmetric move (Reaver spread challenging ADS cells, the ADS answering with a sentinel) is the natural next step
and is not in this pass.

## Everyone can take part

- Guns: break nodes (collapses a region, ADS tech aboard), kill sweeps and sentinels (frees a cell, paid), finish off
  an interdictor or a Reaver response pack mid-fight, or stand off and let the machines and the pirates bleed each other.
- No guns: ADS space costs a disabling, not a death, and never a station; cross it fast, fly lanes to keep it quiet,
  stand by an assault for the support pool.
- Navies: credits through the bounty as the Reavers pay them.
- Ghosts and admins: every campaign encounter on the Encounters tab and admin window; `wf_ads`.
- Round end: nodes founded and broken, sweeps destroyed, cells challenged, won and lost.

## Module layout (`AdsIncursion`)

- `Content.Shared/_WF/AdsIncursion`: `WFAdsCampaignPrototype.cs`, `AdsIncursionCVars.cs` (`wf.ads.enabled`).
- `Content.Server/_WF/AdsIncursion`: `README.md`, `Components/` (`WFAdsCampaignComponent`, `WFAdsCampaignRuleComponent`,
  `WFAdsNodeComponent`, `WFAdsSentinelComponent`, `WFAdsSweepComponent`, `WFAdsDeploymentComponent`,
  `WFAdsInterdictorComponent`, `WFAdsPicketComponent`), `Systems/WFAdsCampaignSystem.cs` with partials `.Nodes`,
  `.Sentinels`, `.Deployments`, `.Spread`, `.Sweeps`, `.Interdicts`, `.Challenges`, `.Retakes`, `.Threat`, `.Announce`,
  `.Rewards`, `.Hold`, `.Disputes`, `Systems/WFAdsCampaignRuleSystem.cs`, `Commands/WFAdsCommand.cs`.
- `Resources/Prototypes/_WF/AdsIncursion`: `factions.yml`, `companies.yml`, `standing.yml`, `ai_factions.yml`,
  `profiles.yml`, `gear.yml`, `names.yml`, `vessels.yml`, `encounters.yml`, `manifests.yml`, `campaigns.yml`,
  `game_rules.yml`, `guidebook.yml`. `Resources/SharedMaps/_WF/AdsIncursion/*.yml`.
  `Resources/Locale/en-US/_WF/AdsIncursion/ads.ftl`. `Resources/ServerInfo/_WF/AdsIncursion/Guidebook/Ads.xml`.
- Edits in sibling `_WF` modules (no markers): SectorControl challenges; ReaverCampaign `WFReaverResponsePack`,
  `.Conflict.cs`, `WFReaverResponseComponent`, the preset gaining the ADS rule, its description.
- Tests: `Content.IntegrationTests/Tests/_WF/AdsIncursion/` (founding claims the ring and skips station cells; node
  broken pays and frees; sweep killed frees its cell; sentinel joins and the bounty scales; challenge launched, won by
  absence and transfers to a picket, lost by presence and withdrawn, dropped when the interdictor dies; the Reaver
  response pack is launched with attack orders and goes home when the challenge resolves; every votable preset has one
  ADS campaign; tier rises; interdiction past the dwell), `Content.IntegrationTests/Tests/_WF/SectorControl/
  WFSectorChallengeTest.cs` (framework rules alone, with the test faction).

## Decisions

- Disable, never destroy: the one rule that makes a machine fleet with capital turrets survivable for a hauler.
- Stations are off limits to the ADS: the core is never the front, and the player always has somewhere to run.
- Two drivers, one framework: the challenge is a framework contest with a holder, so the next faction (TSF, PDV) gets
  conflict for free, and each driver only decides when to challenge and how to answer.
- A won challenge leaves a picket, not a node: taking ground from the Reavers costs the ADS a hull it must keep there.
- The Reavers answer with ships, not with a timer: the fight is at the cell, visible on the map, and players can join
  either side or wait for the winner to limp.
