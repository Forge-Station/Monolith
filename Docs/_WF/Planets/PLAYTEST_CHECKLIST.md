# Planets — playtest checklist

For one human on a dev server. Runs planet flight end to end: a planet network, orbit, descent, landing, liftoff and the planet gear.

Each step is **Do** (what you type or click), **See** (what should happen) and **Report** (what to write down if it does not). Report anything with a file and line from the client or server log where you have one.

You need admin rights for `wfplanet`, `planetcontrol` and the spawn menu.

---

## 1. Enable the CVar

**Do.** Run a dev server with the Build development preset. `Resources/ConfigPresets/Build/development.toml` ends with:

```
# WOLFGATE
[wf]
planet_networks = true
```

If you are not on that preset, set `wf.planet_networks true` in the server console before anything else. It is `CVar.SERVERONLY` and is read at registration time.

**See.** `wfplanet list` answers rather than refusing.

**Report.** `Planet networks are off. Set wf.planet_networks to true.` — the CVar did not take. Nothing else in this checklist will work.

## 2. Make a planet

The dev preset disables the lobby, which forces the sandbox preset, which carries no star system — so nothing is registered until you make one. Two routes:

**Do (A, the real path).** Attach to an entity on the sector map (aghost is enough), then `wfplanet system SystemKyphrus`.

**See.** `Set SystemKyphrus on map <n>.` Six bodies appear — Fervidus (6.2 km from the star), Merak (9.4 km), Asclepiu (11.5 km), Aerumna (14 km), Thrascias and Carcinoma (14.5 km each), all short of Monolith's drone belt, which starts at 18 km. `wfplanet list` shows each with its `surface:` and a network id; every surface has `buildAtRoundStart: true`, so each world's five maps are built the moment it registers.

**Do (B, the shortcut).** `wfplanet spawn WFSurfaceAsclepiu` builds a **standalone** network at the sector origin with no sector body behind it.

**See.** `Built 5 layers for WFSurfaceAsclepiu: network <n>, orbit <n>.`

**Note.** A standalone network has no body to measure range from, so the inbound FTL gate stays wide open and you can jump to the orbit layer from anywhere. Route A is the one that exercises the real range check.

**Report.** `Failed to build a network for …` (see the server log for the real cause), or a body that registers with `surface: none` when it is Asclepiu. Also report if `wfplanet list` shows a network id but you cannot see the orbit map in any FTL destination list.

## 3. Spawn a ship

**Do.** Spawn any stock shuttle with `spawnvessel`, or admin-spawn one, and board it. Give it lift for the descent: spawn `WFThrusterLanding` and anchor them to the hull, or spawn a `WFLandingThrusterKit` and click an anchored ordinary thruster with it. Power the ship as usual.

**See.** Its shuttle console's FTL destination list shows Asclepiu's own beacon (named `Asclepiu`). The orbit layer is **not** in the list and must not be: it is not an FTL destination at all. If the map screen shows no destinations at all, **ping the map** — `MapScreen` only rebuilds its destination tree on a ping.

**Report.** An `Asclepiu orbit` entry in the destination list (the layer was registered as a destination again). The FTL button greyed with no popup (the FTL console rejects silently on almost every path, so a dead button is the symptom of a refused jump).

## 4. Enter orbit

**Orbit is a button on the shuttle console, not an FTL jump.** You do not need an FTL drive and the orbit layer is not a destination. The nav screen's settings column carries an *Enter orbit: &lt;planet&gt;* button, which lights up when the hull is parked on the body's own sector map within `orbitRange` (2000) of it; in orbit the same button reads *Leave orbit: &lt;planet&gt;*. The hop runs the ordinary FTL transit internally — 5 s warm-up, 5 s in hyperspace, the usual sound and shake — so it arrives at the same world XY on the far layer, or at the FTL arrival's free spot when something is already parked there. `WfAllowFTL` still refuses every jump **out of** a planet network from a surface or air layer, and every jump mid-transit: you climb to orbit first.

**Do.** FTL to `Asclepiu`. Once you are parked near the body, open the shuttle console's nav screen and press *Enter orbit: Asclepiu*.

**See.** The button is enabled only within 2000 of the body, and greys out (`Busy`) once the hop starts. After the warm-up and transit you arrive on a starless map with space atmosphere and a dim blue ambient light. The hull does **not** fall — the orbit layer is exempt from the CE gravity sweep. Looking down through the z-view you should see the air layers and eventually the ground.

**Report.** The button missing entirely while parked beside the body, or enabled from thousands of tiles away (the readout sweep or the range gate is wrong). A popup instead of a hop with the hull clearly in range. The hull starts falling on arrival (orbit exemption broken). Also report if you can FTL away while standing on the surface — the outbound gate should refuse that.

**Going down.** The same control block carries *Enter atmosphere: Asclepiu* under the orbit button, with the hull's lift ratio beneath it. That is the only way out of orbit downward — a held **F** from orbit is refused with a popup naming the button. See §5.

**Leaving.** Press *Leave orbit* on the same console; you come back out beside the body on the sector map. The sector body's own FTL beacon still works as a second route in for a hull that has a drive. If your gravgen dies while you are on a non-orbit layer you are stranded — the console offers neither the orbit button nor a destination list; `wfplanet tp <planet>` is the escape hatch.

## 4b. Orbit decay and planet drag

**An orbit layer holds up only what is holding itself up.** A grid keeps station while one linear thruster is switched on and running (landing thrusters count, gyroscopes do not), or it is force-anchored, or it is docked to something that has one. Anything else gets sixty seconds and then falls.

**Do.** With the hull parked in orbit, read the line under the lift ratio: it says *Orbit: stable*. Now kill the hull's thrusters — cut the power, or take the thrusters apart — and watch the same line.

**See.** A PA call: *"Orbit decaying: station-keeping lost, 60 seconds to atmospheric entry"*, the caution chime, and the ship's situation code taken over by **Orbit Decay**. The console line counts down, *Orbit decaying: 42 s*. Restore power inside the minute and the countdown clears, the code goes quietly back to whatever the ship was flying under, and the line reads *Orbit: stable* again. Let it run out and the hull drops out of orbit exactly as *Enter atmosphere* would have dropped it: lift lost, the wind, the GPWS callouts all the way down, a hard landing or a crash at the bottom. A wreck on the ground stays there — nothing cleans it up.

**Also do.** Spawn a bare grid or shoot a piece off a hull in orbit and leave it alone; it should come down on its own inside the minute with nobody aboard. Then fly around in orbit and on the air layers.

**See.** Flight over a planet is slow and heavy: about 6 m/s in orbit and 12 m/s on an air layer, with the thrusters clearly fighting the drag rather than beating it. That is deliberate — the surface streams in under you and a fast hull outruns its own terrain. Off the planet, the hull is its old self again the instant it leaves the layer.

**Report.** A hull with a running thruster decaying anyway, or a dead hull that never does. The countdown restarting, or the PA line repeating every second. The ship's own code not coming back after a recovery. A landed wreck disappearing. A hull that is still fast on a planet layer, or one that stays sluggish after leaving orbit.

## 5. Descend

**Lift over a planet is landing thrusters, not the gravity generator.** A hull needs `WFThrusterLanding` bolted on, or ordinary thrusters converted with a `WFLandingThrusterKit` (spawn one, click an anchored thruster with it). The console shows the hull's *lift ratio* under the orbit button: green at 1.00 or better, amber down to 0.50, red below that. At 1.00 the hull flies exactly as it always did.

**Do.** Board the ship, take its shuttle console, and press *Enter atmosphere: Asclepiu*. Then hold **F** to descend. On the ground, use **Liftoff** on the shuttle console to take off into the first air layer; click again or press **F** to cancel. From the air, hold **R** to climb on to orbit.

**See.** The lift readout above the button reads 1.00 : 1 or better once enough landing thrusters are powered. The hull drops out of orbit into the gap below it and hangs there; **F** then walks it down a layer at a time: orbit (depth 4) → three air layers → ground (depth 0). The altimeter tracks it. On touchdown the thrusters disable.

**FTL is barred below orbit.** From the surface, from an air layer and from mid-transit no FTL will start at all - not to a beacon, not to a listed destination, not to a dock. Climb back to orbit and *Leave orbit* first.

**Report.** A hull that FTLs away from the ground or from an air layer, through any console route. *Enter atmosphere* missing or greyed while parked in orbit. **F** doing nothing below orbit with the lift reading 1.00 or better. A hull leaving orbit on its own — orbit is parking, nothing pulls on you there. A layer skipped, or a hull arriving on the ground without the transit maps.

**Holding F in orbit does nothing on purpose.** You get a popup pointing at the console button. That is the only way down, and it is where the lift warning lives.

**Descending without lift.** Switch a landing thruster off (or unanchor one) so the readout goes amber or red, then press *Enter atmosphere*. You get a confirm dialog naming the ratio. Confirm it and the hull is in **lift lost**: it sinks and cannot climb back out. Between 0.50 and 1.00 the sink is slowed in proportion — near 0.90 it is a long slow descent; under 0.50 it falls at the full rate. Give the hull some sideways speed before you drop and it glides, picking up about 25 % more speed along its heading for each layer it falls through - and a hull that was holding station gets a 2 m/s nudge along its own facing anyway, so nothing ever comes down like a dropped brick.

**See.** The PA takes the ship over: the caution alarm starts **looping** and keeps looping for the whole emergency, with *Don't sink* out of orbit, *Sink rate*, *Terrain*, *Too low — terrain* in the last gap and *Pull up* repeating every 3 s in its final seconds. The ship's situation code shows each one on the console's PA panel and your own code comes back when it is over.

**Report.** Callouts out of order, a stage repeating, or the ship left on a flight code after it has landed. No alarms at all with the hull clearly sinking. A caution alarm that chimes once instead of looping, or one still looping after the hull is down or has got its lift back. Alarms you cannot hear with speakers online (check §Ship PA if the ship has none — the state still applies, you just cannot hear it).

**Hear.** Flying inside the atmosphere is not silent any more: a wind loop plays to everyone aboard on every air layer and in every gap between them, getting louder and higher the faster the hull is moving, and a second airframe rumble comes in under it the moment the lift goes and builds as the fall speeds up. Both stop when you land, and both stop when you reach orbit.

**Report.** Silence below orbit. Wind you can still hear after landing or in orbit. Two wind loops at once (it should be one, however long you fly), or a loop that keeps playing after the ship is deleted.

**Touchdown.** A **full free fall is now a crash** - it always was meant to be, and it was not: the threshold used to be a fixed 0.8 levels/second, which no fall in the game ever reaches, so every plummet landed softly and did nothing. It is now measured against the speed a free fall actually arrives at (about 0.47 levels/second), so dropping with no lift at all explodes the hull the way it always should have.

A **hard landing** is what you get below 80 % of that - a partial-lift sink, or a glide that shed speed. It costs you: one thud, a looping scrape, light damage across the whole hull, and heavy damage concentrated on the leading edge if you came in with any sideways speed, which normally means broken thrusters and machines along the nose and some tiles torn off it. The hull itself survives, is repairable, and flies again once the lift is back at 1.00. It then skids along the ground keeping the speed it came in with, flattening walls, crates and anything else anchored in its path. A crash that still had real speed left keeps ploughing after the blast rather than stopping on the spot.

**Report.** A slow lift-lost touchdown exploding anyway, or a full free fall walking away without an explosion (that is the bug this was). A hard landing that costs the hull nothing at all, or one that guts it. A hull that stops dead on the tile it touched instead of sliding. A skid that never stops. Tiles coming off a hull that is barely moving (the leading edge should only grind above about 4 m/s).

**Setting down on things.** Any landing, gentle ones included, destroys what is under the hull: trees, rocks, crates, loose items, and anything built there. Trees and rock the planet grew are also broken within a hand's breadth of the hull, so it has nothing to rest against; a wall someone built that close is left alone unless the hull actually touches it. A mob under the hull is not gibbed: it takes 40 blunt, is floored for four seconds and ends up on the nearest clear ground beside the ship. Record a hull that still shudders against a tree after landing, and anyone left inside the hull or killed outright by a landing.

**Note.** Planet terrain unloads about three minutes after the last viewer leaves (`wf.planet_terrain_unload_idle`), except round a ghost, under and one chunk round a hull, beside anything built, and for two chunks round whatever is still loaded. A hull parked with its crew back in orbit should keep its ground; record it if one loses it. Walk away from a mined-out patch and come back: the mined tiles stay mined and untouched rock is back as it was, ore and all. Water you drew from stays; still water comes back. Arriving somewhere new, the ground more than about 20 tiles out fills in over the next second or two (`wf.planet_terrain_load_budget`, 0 to turn it off); record a ship that touches down on ground that is not there yet.

---

## Known placeholders

Do not report these.

- **Every flight sound is a placeholder.** `lift_lost`, `dont_sink`, `sink_rate`, `terrain`, `too_low_terrain`, `pull_up`, `hard_landing` and `skid` under `/Audio/_WF/Planets/Flight/` are ffmpeg tones, distinct from each other and nothing more; `atmo_wind` and `fall_rumble` are stand-in copies of the stock weather wind and space wind loops. Report only that you cannot tell two of them apart, or that a loop is obviously the wrong length.
- **The surface is deterministic.** `WFSurfaceAsclepiu` fixes its biome seed, so the same terrain comes back every round.


### Terrain, thrust volume and unprotected orbital falls

- Radar terrain remains visible in orbit, atmospheric layers, transit gaps and on the surface.
- Thruster loop gain is 60% of its former level (-10.44 dB).
- A living mob falling on its own from orbit loses one available arm and one available leg at surface impact and receives damage sufficient for critical condition. The orbital impact replaces ordinary accumulated fall damage; already-dead bodies are not revived. Short falls and occupants riding crashing ships retain their existing injury behavior.

## Liftoff and Carcinoma hull infestation

- A grounded shuttle offers **Liftoff**. With sufficient lift and power it spools up, climbs off the ground and settles hovering on the first air layer, then hands the hull back to the pilot; **R** from there climbs to orbit. Cancel from the same button or by pressing descend. Losing the pilot, power/lift, or acquiring a tether cancels the ascent.
- Leave a stationary hull on Carcinoma for 45–75 seconds. Large flesh tendrils begin appearing around its perimeter, at most four; cut or attack every tendril to release the hull. The last tendril grants at least 45 seconds to launch before another can grow. A tendril on a docked hull also blocks departure.
- Sealed hulls exclude biomass. Open an exterior door and wait for growth: actual chimera flesh enters and spreads across connected hull tiles. It cannot spread onto planet terrain or through a dock onto another grid. Growth is capped at 256 biomass entities per hull.
- Supplied original audio is attributed to Gandalf under CC0-1.0. Existing borrowed audio retains its prior attribution.

### Fall and tendril effects

- An unprotected orbital fall triggers the species' scream emote once as the fall begins; surface impact plays a single wet gib splat while retaining the one-arm/one-leg critical-injury outcome.
- Each anchoring tendril plays the supplied quiet, positional loop (8-tile range, -12 dB). Removing the tendril removes its loop.
- A newly grown tendril converts existing walls in its immediate 3x3 hull neighborhood to meat walls. Doors are preserved, and terrain walls beneath the ship are unaffected.

- Carcinoma random accents now follow the same local day/night phase as its lighting and ambience beds. Night uses `carcinoma_random_sound_night_1`–`3`; dawn stops any remaining night accent. Other planets can optionally define separate day/night accent playlists, with their existing all-day lists as fallback.
- Tendril growth randomly plays `tendril_deploy_1`–`3` once, alongside its quiet persistent loop. Imported one-shots have trailing silence trimmed and are normalized to match the existing planet accents.

## Flesh ticks and pustule traps

- Spawn a flesh tick (`WFMobFleshTick`) near a non-Chimera humanoid. It should jump, remain visibly attached, and play one bite sound when it successfully latches. Confirm recurring damage affects the attachment site, blood declines, and Natural Letoferol accumulates gradually.
- Click the attached tick to remove it. Feeding must stop immediately on removal or death; removal must not leave a hidden attached entity. Chimera allies should not be attacked.
- An unlatched wandering tick occasionally plays a hunt vocalization. Death plays one death sound, without leaving a vocalization loop running.
- Approach an intact flesh pustule as a non-Chimera character. It should burst once, release exactly three ticks and a Natural Letoferol spill, then remain visibly popped. Repeated approaches must not release more ticks.
- Use the Chimera planting action on hive biomass, then try bare ground, an occupied trap tile, and an inaccessible location. Only the valid biomass location should accept a new pustule.
- Explore Carcinoma to find sparse generated pustules. Leaving and returning to a triggered site must not reset it into a new supply of ticks.

- Flesh ticks also chase and bite at close range. Each successful bite attempts to latch; at most three can attach to one host, while additional ticks can still bite. Attached ticks only use their feeding pulse. After manual removal, allow four seconds before another attack.

## Carcinoma regional ecology

- Explore widely for dormant tendon forests, assimilation sacks, and broad blood oceans; existing flesh groves and blood rivers remain. Tendons can be cut, but do not grow, damage walkers, or tether ships.
- Ambient encounters include the native aberrant flesh roster (Jared, golems, clamps, lovers, newborn variants and rare assimilated miners) alongside Chimera. They use the existing capped encounter system rather than spawning uncounted NPCs from markers.
- Assimilation sacks produce a newborn encounter at most once per four minutes while visitors are nearby and population capacity is available. They share the 4-nearby / 32-per-planet / 128-total wildlife limits. Destroying a sack stops its spawning; no independent endless spawner runs on it.
- With a hull held by tendrils, pressing Liftoff displays the remaining tendril count and the instruction to cut/destroy them. This is an attempted-liftoff popup, not a persistent cockpit warning.

## Imported flesh trees and harvested pustules

- Normal flesh trees choose randomly from the twelve replacement ordinary 64x64 frames. Pustule trees use frame 10 before harvest and frame 11 afterwards; the old shadow-tree recolour is no longer used.
- Click a pustule tree with an empty hand: receive exactly one flesh pustule and hear pustule_pop. Repeat harvesting gives nothing. Harvested terrain is pinned so leaving/re-entering cannot refill it.
- Eat the item for 15 units Natural Letoferol and 5 Nutriment in one bite. Click clear ground within two tiles to plant a normal nest, or throw it to trigger the usual nest-breaking sound, Letoferol spill and three ticks on impact. Merely dropping it does not pop it.
- Impact bursts still pop at the population cap, but omit tick spawning when capacity is exhausted; they never leave an armed grenade behind or bypass the cap.


## Orbit rules, gravity wells and planet gear (2026-09-20)

- **Jetpacks.** Step off a hull on the orbit layer with a jetpack on: you hold the layer. Switch it off, or let the tank run dry, and you fall (and are maimed on the surface, as before). On any layer below orbit the pack refuses to start ("cannot hold you up in atmosphere"), and a wearer carried below orbit on a hull has the pack cut.
- **Atmospheric jetpack.** Buy one from John Wolfgate. On the ground of a world at 1.5 g or less it lights; ascend and descend climb between the air layers at half a layer a second, and it burns welding fuel (examine for the level, click a welding fuel tank to refill). It refuses in orbit ("no air to burn") and on a heavy world ("the gravity is too heavy"), and an empty tank drops you. In a cavern, fly up under a mouth or a dug shaft and you come out on the surface; hold ascend anywhere else and you stop at the ceiling, with no flicker to the surface and back.
- **Unsanctioned worlds.** *Enter orbit* on an unsanctioned world opens a red warning first; Abort sends nothing, *Enter orbit* proceeds. A raw unconfirmed request is refused server-side with a popup. Flip a world's sanction from the admin panel to test both.
- **Gravity well.** Park a shuttle inside a world's orbit range in open space and cut every linear thruster (or its power). The PA warns once, the hull drifts towards the body, slowly at the rim and faster close in, and inside 35% of the range it is captured onto the orbit layer, where orbit decay takes over. Any running linear thruster, a force anchor, or a dock to a powered hull stops the pull.
- **Approach cinematic.** Enter orbit from the console: after spool-up the hyperspace tunnel is replaced by the planet itself, starting where it stood in the sector sky and swelling to fill the view; leaving orbit plays the reverse. Crews of hulls docked to it when the tunnel opens see it too; a tender that casts off during the spool-up stays behind with its ordinary view. The last frame holds until the hull arrives: no flash of streaking stars between the full (or shrunken) planet and the orbit or sector view, on either hop.
- **Drone belt.** Fly to Aerumna, Thrascias and Carcinoma and enter and leave orbit: no drones spawn at the beacon, in the well or where you drop out of orbit. The worlds sit at most 14.5 km out with 2 km wells; the belt starts at 18 km. `PlanetDroneBeltTest` fails if Monolith moves the belt or a world is moved into it. Drones already spawned at the belt's edge by some other ship can still lock on anywhere in the Thrascias and Carcinoma wells, since those bodies sit about 3.4 km from it; that is expected.
- **Building on the surface.** Rods lay lattice straight onto natural ground; plating and floors then go on as usual. Cutting the lattice gives the original ground tile back, not a hole. Already-built tiles and tiles the biome did not place are untouched by this.
- **Air in what you build.** Lay lattice, plating and a floor on a planet, wall it in (walls can stand on the bare ground around the floor) and open a canister inside: the room fills and holds, and cutting a wall lets it settle back to the planet's air. Check a vent and a scrubber run in it too. A newly plated tile starts with the planet's air, not a vacuum, so laying floor makes no gust. Bare ground counts as outdoors, as it does for weather: gas let out on it is gone, and a room with no floor laid does not hold air. An annex built flush against a landed ship is open on the ship's side, because the ship is a separate grid; wall or door that side on the ground too. `wf.planet_terrain_atmos` (on by default) turns it off for worlds built afterwards.
- **Cavern stairs.** In a cavern, open the construction menu (Structures, *cavern stairs*), face the ghost the way you want to climb from and build it with 10 steel: the ground above opens and daylight falls in. Walk up and you come out on the tile beyond the top; walk back down. Check: no "falls from above" popup and no damage either way, at a walk and at a creep; a crate or a downed crewmate you are pulling comes with you both ways, and does the same on *Climb up* and *Climb down*, with a tool in your other hand too; stepping into the stairwell from the side on the surface sets you down on the stairs unhurt; from the surface the hole shows the stairs below. Try to build them under a parked ship, under a floor you laid, and facing a wall or hole at the top: each is refused with its reason. Unbolt them with a wrench: the hole stays and gets a climb point.
- **Landing kits.** In the YouTool, and printable at an autolathe (Tools).
- **Parachutes.** AstroVend and autolathe. Use on a crate, an item or a person (or in hand, for yourself): 3 s to strap on. Push them off a hull in orbit: the canopy opens on the first level fallen through, the fall is held slow, there is no damage, and the pack is left at the landing site. A step down a ledge does not open it.
- **Takeoff sound.** Plays to the crew and nearby ground when Liftoff latches. PLACEHOLDER audio: replace `Resources/Audio/_WF/Planets/Flight/takeoff.ogg` in place.
- **Admin Planet Control.** Admin menu, Wolfgate tab, or `planetcontrol`. Set local time (or Dawn/Noon/Dusk/Midnight), hold a weather for N seconds, set gravity in g (lift ratios follow at once) and toggle the sanction. Unbuilt worlds only take the sanction.
- **Weather (SS13 port).** Storms run in phases: a light overlay telegraphs, the storm runs, a light overlay winds it down. Rain, dust, snow and ash use /tg/station's overlays; rain has start/mid/end loops. Thunderstorms (Asclepiu's heavy rain, Carcinoma's blood storm) drop real bolts on open ground near visitors: a flash, a crack, and a shock for anyone on that tile. Roofed tiles are never struck.
