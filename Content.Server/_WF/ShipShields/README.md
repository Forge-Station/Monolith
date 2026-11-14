# Ship shields

The gunnery console carries a simplified shield control on its right side: the bearing dial, raise/lower, arc, shunt
power and integrity, with none of the helm page's detail. It only shows while the ship has a shield generator. A
gunner at a powered, anchored console they can access may work the shields of the ship it sits on
(`WFShipShieldShuntSystem`); the console is sent the same shield state as the helm.

Ship shields use a symmetric oval fitted around the occupied hull, with a five-tile margin instead of the previous eight-tile hull-tracing field. Long ships get an oval and square ships get a circle; detached sections on one grid share the same envelope. A regular ship-local hexagonal lattice fades inward from the edge. Traveling impact ripples and repeated-hit heat remain local, while the overall colour shows remaining emitter capacity. Hull changes refresh the collision perimeter and visuals together without disabling interception.

The upstream ship shield emitter keeps its power, damage, recharge and projectile rules. Hitscan beams intersect the same perimeter and shunt sectors, charge emitter capacity, and trigger impact effects. Ship-fired projectiles sweep their full travel path each tick, including slow mining pulses. Collision-triggered ship warheads are absorbed before their explosion trigger runs, regardless of projectile collision subscriber order. Absorption also clears pending timers and proximity triggers before deletion. Ordinary hull contacts still detonate normally. Absorbed EMP warheads retain their area EMP pulse and capped EMP capacity cost without activating explosive or deployment payloads. Flak fragments, mine-laying shells and deployed naval mines are intercepted; deployed payloads inherit their launcher's grid so they pass through their own shields. Confirmed shield ray hits absorb the round immediately instead of waiting for a later thin-edge physics contact; the nearest obstacle and existing collision filters still apply. Shots from the protected ship pass outward. Emitter ownership and deleted-field handles are reconciled before each update; actual field transitions are logged for diagnosis. `ShipShieldsSystem.Wolfgate.cs` supplies hull fixtures and visual updates; `WFShipShieldGeometry` builds the shared contours. Collision edges retain a small physical radius to catch slower projectiles between physics steps.

Confirmed damaging projectile and hitscan absorption raises `WFShipShieldAttackedEvent` with the protected grid,
attacking grid, shooter and weapon. NPC escort crews use it to retaliate even when shields prevent all hull damage.
Attribution prefers the launch grid, then the weapon and shooter grids. Collision probes, harmless shots, shield
gaps and shots leaving the protected ship do not report attacks; a projectile cannot report the same absorption twice.
`WFShipShieldInterceptAttemptEvent` allows crew friendly-fire rules to pass protected projectiles and beams
through before clipping or absorption, preserving capacity and suppressing impact effects. Launched crew rounds
retain their protection when the cannon changes hands. Explicit Attack orders and retaliation still penetrate
normal faction protection after leaving an escort formation.

FTL spool-up immediately lowers shields on the departing ship and docked ships travelling with it. Deployment remains locked through travel, arrival and the full jump cooldown, including manual and admin deployment attempts. Normal power, recharge and manual-switch rules resume when FTL ends; the lockout does not reset shield damage or recharge progress. The helm and generator Shields panels show the FTL lockout while it applies.

Hull collisions spend the impacted ship's remaining shield capacity before damaging its hull. Absorption uses the existing collision energy-to-damage conversion and current local shunt strength; unpowered sectors and exhausted or offline fields let impact damage through. Each ship pays for its own protection, and excess energy reaches the hull when capacity runs out. The shield never becomes a solid barrier: normal hull contact, slowdown and occupant inertia remain. Absorbed collisions trigger the same localized, damage-scaled shield shimmer and rate-limited impact sound as weapons. Collision depletion immediately drops the field and starts its normal recharge/overload recovery.

Healthy shields use the generator's configured shield colour for their faint edge and readable idle hexes, including radar outlines. Damage blends that colour through amber to red; local impact heat reddens both the surface and hex lattice while untouched areas retain the overall health colour. The helm integrity bar remains a standard health indicator. The field becomes more visible as health drops, and smooth traveling shimmer crests brighten it briefly. Hits reveal the surface and nearby hexes with a bright white flash confined to the impact zone. Traveling ripples brighten the shield in its health colour, followed by a broader red wake. Impact brightness and size scale with absorbed damage relative to full generator capacity, including explosive/EMP damage, hitscan modifiers and shunting. Small hits create compact dim patches and shorter ripples; hits consuming at least 5% of capacity reach the full visual response. The lower of the overload and power limits determines capacity, so stronger generators react less to the same round. Full-strength ripples travel for four seconds at 22 tiles per second; sustained fire leaves a brighter red patch that cools gradually over a sixteen-second lifetime. Untouched healthy surfaces remain faint without hiding the idle hex lattice. Surface and hex opacity are sampled separately without rebuilding meshes. Each vertex blends four cached samples to prevent square impact-lighting patches.

Shield startup uses a coloured forming sweep before settling to normal visibility. Shutdown removes protection immediately and leaves a short visual-only copy: combat/power collapse flares and dissolves in smooth uneven patches, while manual lowering fades evenly. Copies retain the generator colour, hull pose and allocation, expire within a second, and are replaced rather than stacked when shields restart. Radar and helm status continue to show actual protection.

The client caches hull meshes and hex lines and animates shimmer in a lightweight shader. Below 16 pixels per tile, oval shields use at most 96 segments (1,728 vertices and 384 direct impact samples), regardless of station size. Distant flashes cover the gaps between sparse samples; normal views retain the detailed hex lattice and interpolated lighting. Legacy non-oval contours retain their original outline. Health-only network deltas preserve the cached outline; impacts retain coarser feedback when zoomed out. Impact sounds randomly choose from three synthesized impact designs (soft bubble, warbling field and heavy shield), each with a soft echo tail. Impacts play at +4 dB with a 0.75-1.1 second per-ship cooldown that survives shield collapse. At most two echo tails overlap; another impact retires the oldest tail. Global audio visibility and a hull-sized full-volume radius let occupants hear impacts throughout large stations.

At the helm (when a generator is installed), or directly at an anchored shield generator, open **Shields** to choose a ship-relative bearing (0° forward, 180° rear), an arc width from 30° to 360°, and concentration from 0% to 100%. Changes send live without an Apply step; the white marker and outline show the requested target while the coloured ring animates actual protection. Concentration moves power from the remaining perimeter into the chosen arc; at 100%, the rest of the perimeter disappears and lets projectiles through. Reset requests even coverage. Direction and width turn at up to 90 degrees per second, and power moves at up to 50 percentage points per second, easing into the target. Server allocation advances at most ten times per second; partial allocations reuse full-perimeter fixtures, and only fully open sectors require clipped fixture updates. Raise/lower controls switch deployment without discarding allocation or bypassing recharge. The allocation ring and thicker integrity bar share health colours and pulse red below 10% capacity. Ring segments use wider pixel-sized gaps and fewer segments at smaller display sizes to keep low-resolution edges readable. When offline, the shared panel shows an estimated return countdown from the emitter recharge and overload rules, including delayed overload reset and the next update tick. Missing power, manual lowering, and disabled grids show the blocking condition instead. Allocation persists on the grid through collapse/recharge and is shared by every helm. Open helms poll authoritative emitter capacity and field availability every 0.2 seconds, sending shield-only snapshots when the visible percentage, recovery second, or settings change without rebuilding navigation, map or docking state. Field removal immediately reports offline, including entities queued for deletion. Power loss can still collapse a field before capacity reaches zero under the existing emitter rules. Stronger arcs absorb more incoming damage per unit of emitter capacity while weaker arcs consume more.

WFShipShieldShuntMath defines the allocation and clipped sectors; WFShipShieldShuntSystem validates helm requests; the client shunting screen and shield overlay show the same allocation.

Navigation and fire-control radar views draw the hull contours through `ShuttleNavControl.ShipShields.cs`. Cached map outlines show health colours and allocation strength, leave fully unpowered sectors open, and retain radar detection and FTL visibility rules. Radar outlines use the protected grid transform, matching the vessel drawing even when the separate shield entity receives a delayed rotation update.

Generator specifications are available when examining an emitter and through **Generator stats** in the helm or generator Shields panel. Values come from the actual emitter: usable capacity (limited by both overload and power demand), overload threshold, normal and recharge-mode repair rates, idle and maximum power demand, and configured overload lockout. The panel follows the active field owner, or the selected recovery emitter while offline; it does not add the capacities of multiple installed generators. The live recovery countdown remains the estimate for the current situation.

Generator startup and shutdown use the supplied `shield_on` and `shield_off` recordings, converted to mono Ogg at their original pitch. Startup and shutdown share a five-second per-hull cooldown, including across generator replacement, so rapid toggling cannot stack or queue transition sounds.

The impact bank is generated by `Tools/_WF/ShipShields/synthesize_impacts.py` using NumPy and FFmpeg (`--ffmpeg PATH` when it is not on PATH). It reproduces all three approved previews with mono encoding, 3.4-second tails and safe peak headroom. The sound collection chooses a design randomly, with a small additional pitch variation during playback.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/ShipShields/ProjectileGrenadeSystem.ShipShields.cs`](ProjectileGrenadeSystem.ShipShields.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Attacks.cs`](ShipShieldsSystem.Attacks.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Audio.cs`](ShipShieldsSystem.Audio.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Collisions.cs`](ShipShieldsSystem.Collisions.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Ftl.cs`](ShipShieldsSystem.Ftl.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Hitscan.cs`](ShipShieldsSystem.Hitscan.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Lifecycle.cs`](ShipShieldsSystem.Lifecycle.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Recovery.cs`](ShipShieldsSystem.Recovery.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Shunting.cs`](ShipShieldsSystem.Shunting.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Stats.cs`](ShipShieldsSystem.Stats.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Transitions.cs`](ShipShieldsSystem.Transitions.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.TriggerCollisions.cs`](ShipShieldsSystem.TriggerCollisions.cs)
- [`Content.Server/_WF/ShipShields/ShipShieldsSystem.Wolfgate.cs`](ShipShieldsSystem.Wolfgate.cs)
- [`Content.Server/_WF/ShipShields/ShuttleConsoleSystem.Shunting.cs`](ShuttleConsoleSystem.Shunting.cs)
- [`Content.Server/_WF/ShipShields/ShuttleSystem.Collisions.cs`](ShuttleSystem.Collisions.cs)
- [`Content.Server/_WF/ShipShields/ShuttleSystem.FtlShields.cs`](ShuttleSystem.FtlShields.cs)
- [`Content.Server/_WF/ShipShields/TriggerSystem.ShipShields.cs`](TriggerSystem.ShipShields.cs)
- [`Content.Server/_WF/ShipShields/WFShipShieldAttackedEvent.cs`](WFShipShieldAttackedEvent.cs)
- [`Content.Server/_WF/ShipShields/WFShipShieldHitscanDamageSystem.cs`](WFShipShieldHitscanDamageSystem.cs)
- [`Content.Server/_WF/ShipShields/WFShipShieldImpactAudioComponent.cs`](WFShipShieldImpactAudioComponent.cs)
- [`Content.Server/_WF/ShipShields/WFShipShieldProjectileRayHitEvent.cs`](WFShipShieldProjectileRayHitEvent.cs)
- [`Content.Server/_WF/ShipShields/WFShipShieldShuntSystem.cs`](WFShipShieldShuntSystem.cs)

### Shared

- [`Content.Shared/_WF/ShipShields/WFShipShieldEffects.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldEffects.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldGeneratorStats.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldGeneratorStats.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldGeometry.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldGeometry.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldHelmAngles.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldHelmAngles.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldHitscanEvents.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldHitscanEvents.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldHitscanMath.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldHitscanMath.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldHitscanSystem.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldHitscanSystem.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldInterceptAttemptEvent.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldInterceptAttemptEvent.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldMesh.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldMesh.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldRadarGeometry.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldRadarGeometry.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldShuntComponent.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldShuntComponent.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldShuntMath.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldShuntMath.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldShuntMessages.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldShuntMessages.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldTransitionEffects.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldTransitionEffects.cs)
- [`Content.Shared/_WF/ShipShields/WFShipShieldVisualsComponent.cs`](../../../Content.Shared/_WF/ShipShields/WFShipShieldVisualsComponent.cs)

### Client

- [`Content.Client/_WF/ShipShields/FireControlWindow.ShipShields.cs`](../../../Content.Client/_WF/ShipShields/FireControlWindow.ShipShields.cs)
- [`Content.Client/_WF/ShipShields/ShuttleConsoleBoundUserInterface.ShipShields.cs`](../../../Content.Client/_WF/ShipShields/ShuttleConsoleBoundUserInterface.ShipShields.cs)
- [`Content.Client/_WF/ShipShields/ShuttleConsoleWindow.ShipShields.cs`](../../../Content.Client/_WF/ShipShields/ShuttleConsoleWindow.ShipShields.cs)
- [`Content.Client/_WF/ShipShields/ShuttleNavControl.ShipShields.cs`](../../../Content.Client/_WF/ShipShields/ShuttleNavControl.ShipShields.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldGeneratorBoundUserInterface.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldGeneratorBoundUserInterface.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldGeneratorWindow.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldGeneratorWindow.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldOverlay.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldOverlay.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldOverlaySystem.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldOverlaySystem.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldShuntScreen.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldShuntScreen.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldShuntScreen.Gunnery.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldShuntScreen.Gunnery.cs)
- [`Content.Client/_WF/ShipShields/WFShipShieldStatsWindow.cs`](../../../Content.Client/_WF/ShipShields/WFShipShieldStatsWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldAttackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldAttackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldCentreTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldCentreTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldCollisionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldCollisionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldControlsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldControlsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldFixturesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldFixturesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldFtlTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldFtlTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGeneratorControlTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGeneratorControlTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGeneratorStatsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGeneratorStatsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGunneryControlTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldGunneryControlTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldHitscanTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldHitscanTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldHullDamageTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldHullDamageTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldLifecycleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldLifecycleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldLiveShuntTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldLiveShuntTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldMovingProjectileTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldMovingProjectileTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldPinholeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldPinholeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldProjectileContactTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldProjectileContactTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldRecoveryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldRecoveryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldStatsLayoutTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldStatsLayoutTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldTransitionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipShields/WFShipShieldTransitionTest.cs)

### Unit tests

- [`Content.Tests/_WF/ShipShields/WFShipShieldEffectsTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldEffectsTest.cs)
- [`Content.Tests/_WF/ShipShields/WFShipShieldGeometryTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldGeometryTest.cs)
- [`Content.Tests/_WF/ShipShields/WFShipShieldHitscanMathTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldHitscanMathTest.cs)
- [`Content.Tests/_WF/ShipShields/WFShipShieldMeshTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldMeshTest.cs)
- [`Content.Tests/_WF/ShipShields/WFShipShieldRadarGeometryTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldRadarGeometryTest.cs)
- [`Content.Tests/_WF/ShipShields/WFShipShieldShuntMathTest.cs`](../../../Content.Tests/_WF/ShipShields/WFShipShieldShuntMathTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/ShipShields/Shaders/shaders.yml`](../../../Resources/Prototypes/_WF/ShipShields/Shaders/shaders.yml)
- [`Resources/Prototypes/_WF/ShipShields/sounds.yml`](../../../Resources/Prototypes/_WF/ShipShields/sounds.yml)

### Localization

- [`Resources/Locale/en-US/_WF/ShipShields/clientstats.ftl`](../../../Resources/Locale/en-US/_WF/ShipShields/clientstats.ftl)
- [`Resources/Locale/en-US/_WF/ShipShields/shunting.ftl`](../../../Resources/Locale/en-US/_WF/ShipShields/shunting.ftl)
- [`Resources/Locale/en-US/_WF/ShipShields/stats.ftl`](../../../Resources/Locale/en-US/_WF/ShipShields/stats.ftl)

### Textures

- [`Resources/Textures/_WF/ShipShields/Shaders/shield_shimmer.swsl`](../../../Resources/Textures/_WF/ShipShields/Shaders/shield_shimmer.swsl)

### Audio

- [`Resources/Audio/_WF/ShipShields/impact_1.ogg`](../../../Resources/Audio/_WF/ShipShields/impact_1.ogg)
- [`Resources/Audio/_WF/ShipShields/impact_2.ogg`](../../../Resources/Audio/_WF/ShipShields/impact_2.ogg)
- [`Resources/Audio/_WF/ShipShields/impact_3.ogg`](../../../Resources/Audio/_WF/ShipShields/impact_3.ogg)
- [`Resources/Audio/_WF/ShipShields/meta.yml`](../../../Resources/Audio/_WF/ShipShields/meta.yml)
- [`Resources/Audio/_WF/ShipShields/shield_off.ogg`](../../../Resources/Audio/_WF/ShipShields/shield_off.ogg)
- [`Resources/Audio/_WF/ShipShields/shield_on.ogg`](../../../Resources/Audio/_WF/ShipShields/shield_on.ogg)

### Tools

- [`Tools/_WF/ShipShields/synthesize_impacts.py`](../../../Tools/_WF/ShipShields/synthesize_impacts.py)

## Non-modular edits

- [`Content.Client/_Crescent/ShipShields/ShipShieldOverlay.cs`](../../../Content.Client/_Crescent/ShipShields/ShipShieldOverlay.cs): Hull shields use their own contour overlay.
- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): shield state for the gunner's shield control
- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs): route shield allocation requests through the helm.
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs)
  - add the shield allocation tab.
  - hide shield allocation outside its helm tab.
  - expose allocation controls at the helm.
  - shield allocation mode.
  - share authoritative allocation across helms.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): hull contours and directional coverage on all radar views.
- [`Content.Server/_Crescent/ShipShields/ShipShieldsSystem.cs`](../../_Crescent/ShipShields/ShipShieldsSystem.cs)
  - refresh hull geometry and shield health
  - recover stale handles and keep one owner per hull
  - complete recharge when healing lands exactly on zero damage
  - respect the ship's manual field switch
  - record actual field transitions for diagnosis
  - only announce successful startup and rate-limit power transitions per hull
  - a standby emitter cannot remove another generator's field
  - share the startup cooldown and use shutdown audio parameters
  - announce only actual shutdown
  - track hull tile changes
  - map-parented shield still phases its ship's outgoing shots
  - unpowered sectors let shots pass without changing their shooter
  - apply damage only after a projectile contacts the perimeter
  - remove only the field owned by this emitter
  - block normal and administrative shield deployment throughout FTL
  - discard stale grid fields and reserve active fields for their owner
  - replace oval and interior blocker with the padded hull perimeter
  - dissipate visually after protection stops immediately
- [`Content.Server/_Crescent/ShipShields/ShipShieldsSystem.Emitter.cs`](../../_Crescent/ShipShields/ShipShieldsSystem.Emitter.cs)
  - removing a standby generator leaves the active field intact
  - preserve the EMP while absorbing explosive and spawn payloads
  - show runtime specifications for generator comparison.
- [`Content.Server/Explosion/EntitySystems/ProjectileGrenadeSystem.cs`](../../Explosion/EntitySystems/ProjectileGrenadeSystem.cs): preserve outgoing fragments and mines through their own shields.
- [`Content.Server/Explosion/EntitySystems/TriggerSystem.cs`](../../Explosion/EntitySystems/TriggerSystem.cs): absorb collision-triggered ship warheads before they can explode on the shield
- [`Content.Server/Projectiles/ProjectileSystem.cs`](../../Projectiles/ProjectileSystem.cs)
  - sweep ship rounds at every speed so slow pulses cannot miss the shield edge
  - consume confirmed shield ray hits without relying on a later edge contact
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - include directional shield settings in the helm state
  - refresh open helm shield status on visible changes
- [`Content.Server/Shuttles/Systems/ShuttleSystem.FasterThanLight.cs`](../../Shuttles/Systems/ShuttleSystem.FasterThanLight.cs): drop departing shield fields immediately after successful spoolup.
- [`Content.Server/Shuttles/Systems/ShuttleSystem.Impact.cs`](../../Shuttles/Systems/ShuttleSystem.Impact.cs): debit each hull shield for absorbed collision damage
- [`Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanDiffractSystem.cs`](../../../Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanDiffractSystem.cs): an intervening shield stops beams before they can split behind it
- [`Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanMultiRaycastSystem.cs`](../../../Content.Shared/_Mono/Weapons/Hitscan/Systems/HitscanMultiRaycastSystem.cs)
  - shield fixtures are resolved against the visible perimeter
  - log piercing targets after shield interception
  - stop piercing beams while retaining targets before the perimeter
- [`Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs`](../../../Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs): expose the ship's authoritative shield allocation at every helm.
- [`Content.Shared/Weapons/Hitscan/Systems/HitscanBasicRaycastSystem.cs`](../../../Content.Shared/Weapons/Hitscan/Systems/HitscanBasicRaycastSystem.cs)
  - resolve shield crossings against their visible perimeter
  - log only the target remaining after shield interception
  - intercept beams before their visuals and damage
- [`Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/projectiles.yml`](../../../Resources/Prototypes/_Mono/Entities/SpaceArtillery/SpaceArtillery/Kinetic/projectiles.yml)
  - intercept artillery payloads at the shield perimeter
  - let deployed mines contact shield fixtures
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/shield_generator.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/shield_generator.yml)
  - expose shared ship shield controls directly at every generator
  - use dedicated generator startup and shutdown sounds at their original pitch

<!-- WOLFGATE-GENERATED END -->
