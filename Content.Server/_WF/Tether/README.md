# Ropes

Stage 1 of the `Tether` module: the rope core that harpoons, tow cables and power
cords are built on. Code lives in `Content.{Shared,Server,Client}/_WF/Tether/`.

## How a rope works

A rope is its own lightweight entity carrying `RopeComponent`, with a global PVS
override so both hulls can draw it. Its ends are two entities with
`RopeAttachPointComponent`.

The physical body a rope pulls on is the attach point's **grid** while the point is
anchored (or has no dynamic body of its own), otherwise the point's own body, so
loose crates can be towed. Both ends on the same body means no joint and no forces:
the rope is visual only.

Rope is one sided. Below its rest length it does nothing at all. Past it, the server
applies a spring-damper as impulses at both anchor points (plus the torque each
anchor arm generates). The engine has no rope joint, so the inextensible limit is a
`DistanceJoint` with `Stiffness = 0`, `MinLength = 0` and
`MaxLength = Length * (1 + maxStretch)`; with stiffness zero and distinct limits the
engine skips its two-sided soft spring entirely and solves only the upper bound,
which is exactly what a rope needs. Damping acts on separating motion only, and the
spring impulse is clamped so it can never reverse the relative motion past the rest
length — a 1 kg body on a 5 MN/m rope stays stable.

A rope severs when an end is deleted, when the ends end up on different maps (joints
cannot span maps, so FTL cuts ropes), or when the tension rule fires. When an end's
body changes underneath it — unanchored, re-anchored, embedded — the joint is quietly
re-created against the new bodies rather than snapping.

## Tuning: `ropeType` prototype

| Field | Meaning |
| --- | --- |
| `stiffness` | N/m **per metre of rope**. The spring constant is `stiffness / length`, so a long rope is softer. |
| `dampingRatio` | Fraction of critical damping, applied to separating motion only. |
| `maxStretch` | Fraction past the rest length where the hard limit sits. Also the denominator of `Strain`. |
| `breakForce` | Tension in newtons that parts the rope. Above it for 0.25 s, or instantly above twice it. `0` is unbreakable. |
| `maxLength` | Longest rest length the rope may be set to. |
| `loadBearing` | `false` (power cords): no joint and no force, but the rope still snaps once stretched past the limit. |
| `color` / `tautColor` | Drawn colour, lerped by strain. |
| `width` | Drawn width in metres. Thins as the rope goes taut. |
| `segmentsPerMetre` | Verlet chain resolution; the point count is clamped to [8, 96]. |
| `stackType` | Stack refunded when the rope is untied. |
| `breakSound` | Played when it parts. |

`RopeCoilComponent` (on a `Stack` item) carries `ropeType`, `metresPerUnit` (1) and
`slack` (1.15, the rope laid per metre of separation).
`RopeAttachPointComponent` carries `maxRopes` (1) and `localOffset`.

## Public API (`RopeSystem`, server)

```csharp
bool TryCreateRope(EntityUid a, EntityUid b, ProtoId<RopeTypePrototype> type, float length, out EntityUid? rope);
bool SetLength(EntityUid rope, float length);            // clamps to [0.5 m, type.MaxLength]
void BreakRope(EntityUid rope, bool refund = false, EntityUid? user = null);
float GetTension(EntityUid rope);                        // newtons, 0 while slack
Vector2 GetAnchorPosition(EntityUid point);
bool TryGetBody(EntityUid point, out EntityUid body, out Vector2 localAnchor);
```

Events (all `[ByRefEvent]`, in `Content.Shared._WF.Tether`):

- `RopeAttachedEvent(Rope, Other, RopeType)` — on both attach points when tied.
- `RopeDetachedEvent(Rope, Other, RopeType)` — on both attach points when untied, for any reason.
- `RopeBrokenEvent(Rope, EndA, EndB, RopeType, Refunded)` — on both ends and broadcast, before deletion.
- `RopeCoilTargetAttemptEvent(User, Coil, Target, RopeType)` with `AttachPoint` and `Handled` —
  raised on an entity a coil was used on that is not itself an attach point. Stage 2C's power cord
  clamps set `AttachPoint` here instead of copying the coil flow.

## Hand interaction

Use a coil on an attach point to take the loose end off it (`RopeCarrierComponent` on
the player, visual-only rope). Dropping or stowing the coil, using it in hand, or
walking past the coil's reach drops the end. Use the coil on a second point to tie
off: the length is `max(distance * slack, 1)` rounded up to whole units, and must fit
both the coil and `maxLength`.

Verbs on an attach point, per rope: **Untie** (2 s doafter, refunds exactly the units
paid out), and alt-verbs **Take in slack** / **Pay out** (one metre; paying out spends
a unit from a held coil of the same type, taking in returns one, and is refused above
0.9 strain). Any tool with the `Cutting` quality cuts every rope on the point with no
refund. Examining a point lists its ropes, their lengths and a coarse strain word.

## Client

`RopeVisualizerSystem` keeps a client-only verlet chain per rope entity and steps it
in `FrameUpdate` with capped substeps; `RopeOverlay` draws it as a mitred quad strip,
a darker outline pass under a lighter core. There is no gravity, so slack comes from
the pinned ends' own motion plus a small per-point drift — slack rope curls, trails
and whips, taut rope straightens, tints, thins and buzzes. A pinned end jumping more
than 10 m re-seeds the chain. Ropes off screen keep their chain but are neither
stepped nor drawn.

## Debug content

`Resources/Prototypes/_WF/Tether/` holds only the `RopeDebug` rope type, the
`RopeAttachPointDebug` eye and the `RopeCoilDebug` coil, so the core can be tried in
game. Stage 2A owns the real content.

## Tests

```
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~Tether" --logger "console;verbosity=normal"
```

`RopeMathTest` covers the pure spring and verlet maths with no server pair.
`RopeTest` ties two dynamic grids together and checks the hard limit, towing, the
absence of force while slack, breaking, severing on deletion and the coil's unit
accounting. Rerun any Skipped test on its own: pair tests can be skipped by unrelated
`db.ef` sqlite warnings on some machines.
