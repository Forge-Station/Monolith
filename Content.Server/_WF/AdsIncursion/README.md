# AdsIncursion

The Automated Defense System as a second sector threat, built on `SectorControl` territory and `Encounters` ships
beside the Ashfall Reaver campaign. The ADS founds nodes (a Nebula gun platform holding a cell centre), claims the
node's cell and its ring, takes one quiet frontier cell at a time and sends sweeps through held space. Every ADS ship
disables what it fights and stops there, and the ADS never claims, contests or challenges a cell with a station in it
(a claim-attempt veto enforces it), so stations stay safe harbours. A threat score (10 a node, 1 a held cell, up to 20
for held space near the origin) sets a tier from Dormant to Overrun; the sector map shows held cells in purple and a
legend line with the tier, nodes, cells and node bounty.
The sector watch announces in three tiers: chimed banners for the big moments, at most one quiet banner per five minutes
(the rest go on the radio) and routine lines on the Common channel only.

The first node, and a regrouped one after the last falls, appear far out (16 to 26 km, three cells from anyone else's
space). Later ones fly out as deployments from the standing node nearest the chosen cell, the cell shown contested
meanwhile; one stopped on the way pays half a node's bounty. A node that has stood its interval gets a sentinel, a
Wyrm that flies in from the sector edge and circles it, up to a cap by tier; when the node is hit its sentinels go
after the top attacker and come back three minutes after the last hit. Breaking a node (its lead out of the fight)
frees its cells and pays `nodeBounty[tier]` times one plus half a sentinel alive, to those who hit it or its sentinels,
with a fifth for the ships that stood by. A sweep or sentinel killed pays the sweep bounty, and a sweep frees the cell
it died in. A crewed ship that stays `interdictDwell` in ADS space has the nearest sweep sent to hold beside it and
query it.

Every ADS zone interrogates (`zoneTargets: Interrogate`, `WFEncounterInterrogationSystem` in `Encounters`): each crewed
player ship in a warning zone is hailed and asked whether it is a threat. Only a ship that answers YES or fires on the
ADS is warned off and fired on; NO leaves it alone for 15 minutes, no answer for 3. The crews are bare IPC automatons,
immune to vacuum, with their guns in the belt and back slots.

From `challengeTier` the ADS contests other factions' space: every `challengeInterval` an interdictor flies from the
nearest node to a cell another faction holds and the framework challenge on it is decided by presence at the deadline.
Won, the interdictor stays as a picket holding the cell; lost, it withdraws; killed, it pays the interdictor bounty.
The Reavers answer a challenge on their space with a response pack (`ReaverCampaign`, `.Conflict`).

At tier 5 the ADS activates its dreadnought (`dreadnought` on the campaign, `WFAdsDreadnought`, a Wyvern capital hull;
`.Dreadnought`). Once the campaign has stood at Overrun for `dreadnoughtDelay`, it activates beside the strongest node
with an anomaly alert and the gamma alert (`capitalSound`) and hunts the Reavers' strongholds one by one, the citadel
first, holding by a node when none stands. Its hits count as an attack on a Reaver hub, so the hub's reinforcements and
sorties turn on it. It adds 10 to the threat score while out. Destroying it pays `dreadnoughtBounty` and finishes the ADS
for the round: nothing more is founded or launched, and what still stands fights on until broken. `wf_ads dreadnought`
activates one by hand.

Each storyteller preset has its own `wfAdsCampaign` (`Resources/Prototypes/_WF/AdsIncursion/campaigns.yml`). It runs
only while `wf.encounters.enabled` and `wf.sector.enabled` are on, the round is in progress and rule
`WFAdsCampaignRule` runs (in preset `WFContestedSector`; `addgamerule` elsewhere; `wf.ads.enabled` forces it on).

`WFAdsCampaignSystem` is the driver, in partials by concern: `.Nodes` (founding, regroup, breaking), `.Sentinels`,
`.Deployments`, `.Spread`, `.Sweeps`, `.Interdicts`, `.Challenges` (interdictors and pickets), `.Retakes`, `.Threat`,
`.Announce`, `.Rewards`, `.Hold` (pause and the event runner), `.Disputes` (presence) and `.Dreadnought` (the tier 5 capstone and the finished state). Each step is a one-shot public
method for tests and the command: `TryFound`, `TryFoundAt`, `TryLaunchDeployment`, `CompleteDeployment`, `TrySentinel`,
`TrySpreadOnce`, `TryLaunchSweep`, `TryInterdict`, `TryChallenge`, `TryLaunchDreadnought`, `SetTier`, `Recompute`, `ClearCampaign`. State lives on
`WFAdsCampaignComponent` on the sector map entity, with one component per encounter kind on the encounter entities.

Admins use `wf_ads status | found [x y] | deploy | sentinel <node uid> | tier <n> | spread | sweep | interdict <grid uid> |
challenge [q r] | dreadnought | pause | resume | clear`, and see every campaign encounter in the Encounters window.

`Docs/_WF/AdsIncursion/design.md` is the design.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/AdsIncursion/Commands/WFAdsCommand.cs`](Commands/WFAdsCommand.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsCampaignComponent.cs`](Components/WFAdsCampaignComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsCampaignRuleComponent.cs`](Components/WFAdsCampaignRuleComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsDeploymentComponent.cs`](Components/WFAdsDeploymentComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsDreadnoughtComponent.cs`](Components/WFAdsDreadnoughtComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsInterdictorComponent.cs`](Components/WFAdsInterdictorComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsNodeComponent.cs`](Components/WFAdsNodeComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsPicketComponent.cs`](Components/WFAdsPicketComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsSentinelComponent.cs`](Components/WFAdsSentinelComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Components/WFAdsSweepComponent.cs`](Components/WFAdsSweepComponent.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignRuleSystem.cs`](Systems/WFAdsCampaignRuleSystem.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Announce.cs`](Systems/WFAdsCampaignSystem.Announce.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Challenges.cs`](Systems/WFAdsCampaignSystem.Challenges.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.cs`](Systems/WFAdsCampaignSystem.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Deployments.cs`](Systems/WFAdsCampaignSystem.Deployments.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Disputes.cs`](Systems/WFAdsCampaignSystem.Disputes.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Dreadnought.cs`](Systems/WFAdsCampaignSystem.Dreadnought.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Hold.cs`](Systems/WFAdsCampaignSystem.Hold.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Interdicts.cs`](Systems/WFAdsCampaignSystem.Interdicts.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Nodes.cs`](Systems/WFAdsCampaignSystem.Nodes.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Retakes.cs`](Systems/WFAdsCampaignSystem.Retakes.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Rewards.cs`](Systems/WFAdsCampaignSystem.Rewards.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Sentinels.cs`](Systems/WFAdsCampaignSystem.Sentinels.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Spread.cs`](Systems/WFAdsCampaignSystem.Spread.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Sweeps.cs`](Systems/WFAdsCampaignSystem.Sweeps.cs)
- [`Content.Server/_WF/AdsIncursion/Systems/WFAdsCampaignSystem.Threat.cs`](Systems/WFAdsCampaignSystem.Threat.cs)

### Shared

- [`Content.Shared/_WF/AdsIncursion/AdsIncursionCVars.cs`](../../../Content.Shared/_WF/AdsIncursion/AdsIncursionCVars.cs)
- [`Content.Shared/_WF/AdsIncursion/WFAdsCampaignPrototype.cs`](../../../Content.Shared/_WF/AdsIncursion/WFAdsCampaignPrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsAnnouncementTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsAnnouncementTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsChallengeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsChallengeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsContentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsDreadnoughtTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsDreadnoughtTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsInterrogationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsInterrogationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsNodeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsNodeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsTestBase.cs`](../../../Content.IntegrationTests/Tests/_WF/AdsIncursion/WFAdsTestBase.cs)

### Prototypes

- [`Resources/Prototypes/_WF/AdsIncursion/ai_factions.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/ai_factions.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/campaigns.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/campaigns.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/companies.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/companies.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/encounters.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/encounters.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/factions.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/factions.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/fonts.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/fonts.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/game_rules.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/game_rules.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/gear.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/gear.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/guidebook.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/guidebook.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/manifests.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/manifests.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/names.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/names.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/profiles.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/profiles.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/speech_verbs.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/speech_verbs.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/standing.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/standing.yml)
- [`Resources/Prototypes/_WF/AdsIncursion/vessels.yml`](../../../Resources/Prototypes/_WF/AdsIncursion/vessels.yml)

### Localization

- [`Resources/Locale/en-US/_WF/AdsIncursion/ads.ftl`](../../../Resources/Locale/en-US/_WF/AdsIncursion/ads.ftl)

### Audio

- [`Resources/Audio/_WF/AdsIncursion/aimalf.ogg`](../../../Resources/Audio/_WF/AdsIncursion/aimalf.ogg)
- [`Resources/Audio/_WF/AdsIncursion/attributions.yml`](../../../Resources/Audio/_WF/AdsIncursion/attributions.yml)

### Shared maps

- [`Resources/SharedMaps/_WF/AdsIncursion/nebula.yml`](../../../Resources/SharedMaps/_WF/AdsIncursion/nebula.yml)
- [`Resources/SharedMaps/_WF/AdsIncursion/razorn.yml`](../../../Resources/SharedMaps/_WF/AdsIncursion/razorn.yml)
- [`Resources/SharedMaps/_WF/AdsIncursion/wyrm.yml`](../../../Resources/SharedMaps/_WF/AdsIncursion/wyrm.yml)
- [`Resources/SharedMaps/_WF/AdsIncursion/wyvern.yml`](../../../Resources/SharedMaps/_WF/AdsIncursion/wyvern.yml)
- [`Resources/SharedMaps/_WF/AdsIncursion/zenith.yml`](../../../Resources/SharedMaps/_WF/AdsIncursion/zenith.yml)

### Guidebook

- [`Resources/ServerInfo/_WF/AdsIncursion/Guidebook/Ads.xml`](../../../Resources/ServerInfo/_WF/AdsIncursion/Guidebook/Ads.xml)

### Docs

- [`Docs/_WF/AdsIncursion/design.md`](../../../Docs/_WF/AdsIncursion/design.md)

## Non-modular edits

- [`Resources/Prototypes/_Mono/Guidebook/factions.yml`](../../../Resources/Prototypes/_Mono/Guidebook/factions.yml)

<!-- WOLFGATE-GENERATED END -->
