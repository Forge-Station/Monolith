# Tethers, rope, harpoons, power cords

Module name `Tether`. Folders: `Content.{Shared,Server,Client}/_WF/Tether/`, `Content.IntegrationTests/Tests/_WF/Tether/`,
`Resources/Prototypes/_WF/Tether/`, `Resources/Locale/en-US/_WF/tether/`, `Resources/Textures/_WF/Tether/`.
Namespaces `Content.*._WF.Tether`. Do not reuse the names TetherGun, Tethered, GrapplingGun (taken upstream).

Conventions: new code only in `_WF` folders. Any edit to a non-`_WF` file is wrapped in `// WOLFGATE` (one line) or
`// WOLFGATE START: reason` / `// WOLFGATE END`. No licence headers. Comments are short precise summaries, matching
`Content.*/_WF/SafetyDepositBox` and `_WF/TractorBeam`. Client and shared types must be in the sandbox whitelist
(no BinaryWriter, Process, etc). Only one system may make a directed subscription for a given component+event pair;
check for an existing subscriber before subscribing on upstream components.

## 1. Rope core (stage 1)

`ropeType` prototype (`RopeTypePrototype`): name, `stiffness` (N/m per metre of rope, so k = stiffness / length),
`dampingRatio`, `maxStretch` (fraction past rest length where the hard limit sits), `breakForce` (N, 0 = unbreakable),
`maxLength` (m), `loadBearing` (false = applies no force, snaps when stretched past the limit; used by power cords),
visuals: `color`, `tautColor`, `width` (m), `segmentsPerMetre`, `stackType`/coil entity to refund, break sound.

`RopeComponent` (networked, on its own lightweight entity like `TractorBeamVisualComponent`'s entity; PVS override so
both ends see it): `EndA`, `EndB` (NetEntity attach points), `RopeType`, `Length` (rest length, m), `Strain` (0..1+
networked coarsely for visuals). Server-only runtime: joint id, current tension.

`RopeAttachPointComponent` (networked): `MaxRopes` (default 1), `Ropes` list, `LocalOffset`. The physical body of an
attach point is its grid when the entity is anchored (or parented to a grid and has no dynamic body of its own),
otherwise the entity's own physics body (so crates and loose objects can be towed). A player carrying a loose end
is a temporary end: not load bearing.

Server `RopeSystem : VirtualController` (model on `TractorBeamSystem`; `UpdatesAfter MoverController`):
- Physics. Rope is one sided: slack under `Length`, spring past it. The engine DistanceJoint spring is two sided, so
  use a DistanceJoint with Stiffness 0, MinLength 0, MaxLength = Length * (1 + maxStretch) as the hard limit only
  (anchors = attach point positions in each body's local space, CollideConnected = true), and apply the one sided
  spring-damper yourself each physics tick as impulses at the anchor points (ApplyLinearImpulse with the point
  overload / plus angular impulse from r x F) for extension in (0, maxStretch * Length]. Damping acts only on
  separating velocity along the rope and must never pull when slack. Use substeps or clamp the impulse so a light
  body on a stiff rope cannot explode (clamp so the impulse never reverses relative velocity beyond rest).
- Tension = spring force + joint reaction (`joint.GetReactionForce(invDt)` if available). If `breakForce > 0` and
  tension exceeds it for longer than 0.25 s, or instantly above 2x, the rope breaks: sound, popup-free, rope entity
  deleted, no refund. Same body on both ends: no joint, no forces, visual only.
- Wake both bodies every tick while taut (grapple gun hack). Skip static bodies when applying impulses.
- Sever when either end is deleted, unanchored into a different body, changes map (joints cannot span maps; FTL), or
  the joint is removed under us. Handle `JointRemovedEvent`/map change gracefully: recreate the joint if both ends
  are still valid and on the same map, otherwise break.
- Public API: `TryCreateRope(EntityUid a, EntityUid b, ProtoId<RopeTypePrototype> type, float length, out EntityUid? rope)`,
  `SetLength(rope, length)` (for reeling; clamps to [0.5, maxLength]), `BreakRope(rope, bool refund)`,
  `GetTension(rope)`, events `RopeAttachedEvent`, `RopeDetachedEvent` (raised on both attach points, carry the rope,
  the other end and the rope type), `RopeBrokenEvent`.

Hand interaction (`RopeCoilComponent` on a Stack item: `RopeType`, `MetresPerUnit` = 1, `Slack` = 1.15):
- Use coil on attach point A: starts a carried rope from A to the player (`RopeCarrierComponent` on the player,
  visual-only rope entity A -> player). Walking further than the coil can cover, dropping/storing the coil, or using
  the coil in hand cancels it.
- Use coil on attach point B: length = max(distance * Slack, 1), rounded up to whole units, must be <= coil count and
  <= maxLength; consumes the units and calls TryCreateRope. Popups for too far / not enough rope / point full.
- Verbs on an attach point per rope: "Untie" (doafter 2 s, refunds units as a coil into hands or on the floor),
  and alt-verbs "Take in slack" / "Pay out" (change length by 1 m, paying out consumes a unit from a held coil of the
  same type, taking in refunds one; cannot take in while strain > 0.9). Cutting tool on an attach point cuts all
  ropes on it (no refund). Examine shows rope type, length and a coarse strain word.

Client `RopeOverlay` (WorldSpaceBelowFOV, model on `TractorBeamOverlay`, DrawPrimitives with Texture.White):
- Per rope a verlet chain: N = clamp(Length * segmentsPerMetre, 8, 96) points, segment rest = Length / (N-1), ends
  pinned to the attach point world positions each frame, 8 to 12 constraint iterations, damping ~0.98, no gravity
  (top-down space) but inject the pinned ends' motion so the rope whips, trails and coils when slack, plus a small
  per-point pseudo-random drift so slack rope settles into loose curls rather than a line. When end distance >=
  Length the chain pulls straight on its own; draw it as straight and lerp colour to `tautColor` by Strain, with a
  subtle width thinning and a vibration when strain > 0.8.
- Simulation lives in world space on the map; when a pinned end teleports more than 10 m in a frame (grid jumps,
  PVS re-entry) re-seed the chain along the straight line with a sine wiggle. Simulate with fixed substeps from
  frame time, cap the step, skip ropes outside the viewport plus margin. State is kept in a client-only dictionary
  keyed by rope entity and dropped when the rope goes away.
- Draw as a quad strip (two triangles per segment, mitred normals), darker outline pass under a lighter core.
- Carried ropes (A -> player) use the same renderer with Length = coil reach.

Tests (`Content.IntegrationTests/Tests/_WF/Tether/RopeTest.cs`): two small dynamic grids joined by a rope, push one
away, assert separation never exceeds Length * (1 + maxStretch) + tolerance and that the other grid gets dragged;
slack rope applies no force; break force breaks; deleting an end removes the rope; coil interaction consumes units.
Put pure maths (spring, clamp, verlet step) in static shared/client helpers so they can be unit tested cheaply.
Use [TestPrototypes] for test rope types. Pair tests fail on unrelated db.ef sqlite warnings on some machines:
check an existing `_WF/TractorBeam` test first to tell the difference.

## 2. Content (stage 2A): rope types, anchor eye, installer

Rope types and coils (stack max 30, 1 unit = 1 m): `RopeHemp` (cheap, stiffness mid, breakForce low, maxStretch
0.08), `RopeSynthetic` (stronger, 0.12), `RopeBungee` (very low stiffness, maxStretch 1.0, strong), `RopeSteelCable`
(very stiff, maxStretch 0.02, very strong), `RopeTowCable` (capital ship grade, steel cable x4). Numbers must be
tuned against shuttle masses in this fork (read a few grids' masses via the test or the tractor beam README), not
guessed: a small shuttle under full thrust should snap hemp, stretch synthetic, and be held by steel.
Lathe recipes (autolathe / engineering techfab) and cargo-free: craftable from cloth / plastic / steel.

`TetherAnchorEye`: anchored, non colliding, draws above walls, may be built on any tile including wall tiles (hull
exterior), `RopeAttachPoint` maxRopes 2, damageable (destroyed -> ropes sever), construction graph (2 steel + 1 rod,
welder to finish; wrench/welder to deconstruct), construction menu entry under utilities.

`TetherInstaller` ("tether install gun"): item with material storage for steel (insert sheets by hand), examine
shows remaining installs, click a tile within range 3 -> 1.5 s doafter -> spawns `TetherAnchorEye` there, costs
the same steel as manual construction. Refuses tiles that already hold an eye or are space. Uses grapple gun
sounds (`/Audio/Weapons/Guns/MagIn/kinetic_reload.ogg`, `/Audio/Weapons/Guns/Gunshots/harpoon.ogg`).

Sprites: generate simple RSIs with a Python + Pillow script kept in `Tools/_WF/` (32x32, cassette-futurism palette,
see the grappling gun and cable coil RSIs for scale), with valid meta.json (licence CC-BY-SA-3.0, copyright
"Wolfgate"). Coils need count-threshold states like cable coils.

## 3. Harpoon turret (stage 2B)

`ShipHarpoonTurret`: anchored hardpoint structure, needs power (MV/APC receiver), a `Strap` so a crew member buckles
in to man it. While buckled the operator's gun input drives the turret's `Gun` (upstream hook: `SharedGunSystem.TryGetGun`
gets a WOLFGATE-marked branch that returns the manned turret's gun for an operator with `MannedTurretOperatorComponent`;
keep it predicted and shared). The turret sprite rotates to the aim direction; firing arc limited to a configurable
cone relative to the turret's mounting rotation. Unbuckling, power loss or turret destruction ends manning.

Fires `ShipHarpoon` (entity ammo, one at a time, reloaded by hand from `ShipHarpoon` items; the gun holds one). The
harpoon is an embeddable projectile. "Shot well" rule: embeds only when speed at impact >= minimum and the angle
between the velocity and the hit surface normal is within 50 degrees, and the target is an anchored entity on a
grid other than the firing grid (or a loose dynamic body >= 50 kg). Otherwise it glances off with a spark/clank and
lies loose. On embed the harpoon gains a `RopeAttachPoint` bound to the struck grid and the system calls
`RopeSystem.TryCreateRope(turret, harpoon, RopeTowCable, distance * 1.05)`. While in flight a visual-only rope pays
out behind the harpoon; it is cut if it outruns `maxLength`.

Operator controls (actions granted while manning, also verbs on the turret): Reel in, Pay out (hold to repeat;
`SetLength` at `ReelRate` m/s, reel stalls when tension exceeds `ReelMaxTension`, looping `/Audio/Weapons/reel.ogg`),
Release (cuts the cable at the turret; harpoon stays embedded and can be pried out with a crowbar doafter). If the
struck entity is destroyed the harpoon falls out and the cable goes slack-attached to the loose harpoon.
Shot sound `/Audio/Weapons/Guns/Gunshots/harpoon.ogg`, break `/Audio/Items/snap.ogg`.
Construction: machine-frame style or flatpack, researchable/lathe board optional; at minimum spawnable and buildable
from a construction graph (steel, plasteel, cables, rods).

## 4. Power cord (stage 2C)

Approach: node merge, modelled on `Content.Server/_NF/NodeContainer/Nodes/DockablePipeNode.cs`.
`PowerCordClampComponent` + node type `PowerCordNode : Node` with nodeGroupID HVPower / MVPower / Apc. Its reachable
nodes are: every node of the same group on its own tile (cables and device nodes such as generator output, SMES,
substation terminals) plus, when linked, the partner clamp's `PowerCordNode`. Reachability must be symmetric and both
ends are `QueueReflood`ed whenever the link is made or broken. A clamp is only conductive while anchored, its rope
exists and the partner references it back.

Items: `PowerCordHV`, `PowerCordMV`, `PowerCordLV` coils (rope types with `loadBearing: false`, maxLength 20 m, snap
when over-stretched, coloured like the matching cable). Using a cord coil on a cable, generator, SMES, substation or
APC that has a node of the matching voltage on that tile spawns an anchored `PowerCordClamp{HV,MV,LV}` there (small
sprite, `RopeAttachPoint`, non colliding) and starts the normal carried-rope flow from it; using it on a second valid
target makes the second clamp and the rope. Reuse the stage 1 coil flow through an event hook
(`RopeCoilTargetAttemptEvent` lets another system supply/spawn the attach point for a clicked entity) rather than
copying it. Untying or snapping the cord deletes clamps that have no rope left. Insulated-gloves electrocution is
out of scope. Test: two grids each with a test generator/consumer, cord them, assert the consumer on grid B
receives power and loses it when the cord breaks.

## Gate (final)

Merge stage branches, `dotnet build -c DebugOpt`, Release YAML linter, headless server start, run every
`_WF.Tether` test (rerun any Skipped test on its own), fix what is found. See memory notes on the YAML validation
workflow and CRLF traps.
