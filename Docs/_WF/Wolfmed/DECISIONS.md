# Wolfmed orchestrator decisions (2026-09-12)

These are constraints for every agent. Do not re-litigate them; flag concrete problems with evidence.

- **Onyx source pinned:** commit `2f5bab9946539cbe083010c9ae6fbc59b47ae377` (Space-Onyx/space-onyx-14 master, 2026-09-13). Reference sparse checkout at `<onyx-checkout>`. Only paths listed in the sparse set exist; if a path is absent say so, never guess file contents.
- **Wolfgate worktree:** `<worktree>` (branch `clanker/wolfmed-port-orchestration-454c3d`). RobustToolbox 277.0.0 is junctioned in. Baseline builds with 0 errors.
- **D1 StatusEffectNew:** port it verbatim from Onyx's copy at the upstream path `Content.Shared/StatusEffectNew` (plus any client/server parts and the prototypes/locale it needs). Treat as vendored upstream code; edits marked `// WOLFGATE`. Rationale: keeps `_Onyx` wound files verbatim; it is upstream Wizden code that Monolith may inherit later. The old `Content.Shared.StatusEffect` system stays and keeps serving existing content.
- **D2 Damage bridge:** for entities with `WoundHostComponent`, Onyx `WoundDamageRoutingSystem` owns part damage. Shitmed's in-`DamageableSystem` spreading, sever-at-130 and part regen are bypassed for those entities via minimal `// WOLFGATE` guards. Entities without `WoundHostComponent` behave exactly as today. Everything is gated on Onyx's `CCVars.Wounds` plus component presence.
- **D3 Phase 1 species:** organic humanoids only (the Human body and any species sharing organic parts). IPC/cybernetic/slime/plant profiles are later phases.
- **D4 Balance:** Onyx defaults. Tuning later via CCVars and prototypes.
- **D5 Missing APIs** (verified absent in Wolfgate): `StatusEffectNew`, new-style `DamageableSystem` API (`Entity<DamageableComponent?>` overloads, `ChangeDamage`, `HealEvenly`, `HealDistributed`, `GetTotalDamage`...), `EntityEffectSystem<T>` ECS entity effects (Wolfgate has the old class-based `EntityEffect`), `AlertsSystem.UpdateAlert`, `DamageSpecifier.ArmorPenetration`. Strategy: a compat layer in `Content.Shared/_WF/Wolfmed/Compat` (extension methods, adapter classes) wherever a shim keeps a vendored file verbatim. Where a shim is impossible, a `// WOLFGATE` edit inside the vendored file. Upstream Wolfgate systems only get one- or two-line `// WOLFGATE` hooks.
- **D6 Layout:** vendored Onyx code in `Content.{Shared,Server,Client}/_Onyx/...`, `Resources/Prototypes/_Onyx/...`, `Resources/Locale/en-US/_Onyx/...`, `Resources/Textures/_Onyx/...` keeping Onyx's relative paths. Wolfgate glue in `_WF/Wolfmed`. Docs and manifest in `Docs/_WF/Wolfmed/`.
- **D7 Surgery:** phase 1 keeps Wolfgate's Shitmed surgery. Onyx's own surgery system (`_Onyx/Medical/Surgery/SharedSurgerySystem.*`, `_Onyx/Surgery`) is NOT ported. Wound surgeries are re-expressed on Shitmed's step system later (phase 4).
- **D8 Body:** stay on Wolfgate's Shitmed `BodyPartComponent`. Onyx's extra part fields go in a separate `_WF/Wolfmed` component. Onyx `_Onyx/Body` Nubody glue is not ported; only organ-damage and functional-organ pieces the wounds need.
- **Style:** `_WF` files: no license header, `/// <summary>` one-liners, `[Dependency] private X _x = default!;` without readonly. Vendored `_Onyx` files keep Onyx's headers verbatim.
- **Testing:** headless integration tests only. Port Onyx's wound tests into `Content.IntegrationTests/Tests/_Onyx/Wounds`.

## Orchestrator calls on PLAN.md §8.1 (2026-09-13) — reversible, flagged to the user

- **D32 Species (§8.1 item 1):** `- type: WoundHost` on `BaseMobSpeciesOrganic`. Diona and slime ship on the Organic profile (Onyx-consistent) until phase 5. **Protogen is excluded** (synthetic: remove `WoundHost` in its own prototype with a `# WOLFGATE` comment). Re-enumerate descendants at implementation time and list every exclusion in the manifest.
- **D33 PassiveDamage (§8.1 item 2):** accept D29 — body-level `PassiveDamage` neutralised on wound hosts; Onyx per-profile recovery is the only passive heal. Recorded as a balance deviation.
- **D34 Do-afters (§8.1 item 5):** accept the loss of damage-interrupts-do-after on wound hosts for phase 1.
  *[M6 correction: closed by OD18 (a). One part hit of `wolfmed.doafter_interrupt_damage` (10) or more cancels the hit
  body's treatment, surgery and break-on-damage do-afters; ticks and systemic damage do not. See "M6".]*
- **D35 Prediction (§8.1 item 6):** accept unpredicted wound-host damage for phase 1 (transient mispredict). Predicting routing is a later phase.
- **No commits.** Work packages leave the tree uncommitted; the verify stage snapshots a patch per WP under `<plan>/snapshots/`. The user commits.

## Phase 2 (2026-09-13) — scope and constraints

Phase 1 is committed (`23c0a74cb9 initial commit of port`). Phase 2 = PLAN.md WP10 plus WP9's handed-forward gates. Same rules: vendored Onyx files in `_Onyx` marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines (bodies in `_WF` partials), no directed subscription without checking existing subscribers, no commits, RobustToolbox untouched.

- **P2-1 Scope:** `FractureEffectsSystem`, `FractureAlertSystem`, fracture/pain/shock alerts and their textures, `MovementModStatusEffectComponent` + trimmed `MovementModStatusSystem` and the `StatusEffectSlowdown` chain if wounds need it, `_Onyx/StatusEffects/wounds.yml` only if a consumer exists (Onyx's two entries are orphaned at the pin; skip if so), the client pain damage overlay, `EmoteOnDamage` pain sounds, GUARD E2 + `_Onyx/HealthExaminable` (examine part status and pain), `HighPainThreshold` trait + `PainNumbness` status effect, `MobStandStatusEffectBase` with a Wolfgate `KnockdownImmune` tag only if something in scope needs it.
- **P2-2 Tests:** port `WoundFractureTest.EffectsRefreshOnTreatmentHealingAndDetachTest`; write T-AP (armour penetration survives routing) and T-PASSIVE (D29: no passive heal on wound hosts) from PLAN.md §6.2; add a fracture-alert and pain-alert assertion.
- **P2-3 `wounds.body_part_functionality_enabled` stays false.** Fracture movement/hand multipliers apply through `FractureEffectsSystem` regardless; Shitmed's `Enabled` thresholds stay the only limb-disable mechanism.
- **P2-4 Pain is live for the first time** (PainSystem multiplier fix). Balance defaults stay Onyx's, but every number the pain HUD shows must be verified against a real mob in a test, not assumed from Onyx's tests.
- **P2-5 Docs:** phase-2 plan at `Docs/_WF/Wolfmed/WOLFMED_PLAN2.md`; manifest and status doc updated in the same work.

## Phase 2 — answers to PLAN2.md §8.2 (2026-09-13)

- **§8.2-1 Fracture manipulation (user decision):** FIX. Set the four `manipulationModifier` values to the C# defaults `1.1 / 1.25 / 1.5 / 2.0` in the ported YAML behind a `# WOLFGATE` balance comment; a fractured arm slows hand work. Record as a corrected-upstream-bug deviation.
- **§8.2-2 Do-after `Used`:** zero-edit active-hand approximation.
- **§8.2-3 Pain sounds (user decision):** PORT with the YAML key corrected (`emotesThreshold`); recorded as a corrected upstream bug (Onyx's never bound).
- **§8.2-4/5/6 Hooks:** GUARD F + HOOK 14, HOOK 15 + 16, HOOK 17 + 18 are authorised, with bodies in `_WF` partials so each upstream site stays one or two marked lines.
- **§8.2-7 `PartDamageVisualsComponent`:** defer to phase 3; note in the manifest.
- **§8.2-8 Part status readout:** wound hosts only (P2-D20).
- **§8.2-9 `HealingMultiplier`:** leave for the balance pass; keep on record.
- **Execution note:** packages run SEQUENTIALLY in the one worktree (concurrent builds collide), so each package appends its rows directly to `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md`; WP10-7 reconciles rather than merges. `base.yml` still has one owner (WP10-5).

## Phase 3 (2026-09-13) — scope and constraints

Phase 2 is committed (`1171e02fb6 phase 2`). Phase 3 = the deferred body-integrity pieces. Same rules as phases 1–2 (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription audit, no commits, RobustToolbox untouched, sequential packages, manifest appended directly).

- **P3-1 Amputation:** port `AmputationSystem.cs` (D26 lifted): traumatic amputation from overflow + finishing hits, explosion chance, thrown limb, `AmputationConsequence` wound on the parent, `Severable`, the `WolfmedBodySystem.TryDetachPart` shim already exists. Re-enable `OrganDamageSystem`'s amputation dependency and call. Reconcile with Shitmed: GUARD B already suppresses sever-at-130 for wound hosts; Shitmed's `DropPart`/`PartRemoveDamage` cascade must not double-apply with Onyx's consequence wound. `WolfmedBodyPartComponent` gets the amputation fields (`AmputationThresholds`, `DismembermentFinishingDamage`, `AmputationConsequenceSeverity`, `DismembermentSeverity`) populated for human parts.
- **P3-2 Organ consequences:** what Onyx does when organs take damage (`OrganDamageSystem` caps, `OrganConsequenceComponents`, `FunctionalOrganComponent`, server `OrganEffectSystem` if it is wound-driven) mapped onto Wolfgate's organs (Shitmed organ prototypes, Mono pump organ for IPCs is phase 5). Only pieces with a consumer at the pin.
- **P3-3 Per-part armour:** Onyx's locational armour (`ArmorComponent` coverage / coverageSymmetry / partModifiers) on top of phase-1's `WolfmedPartArmorSystem`; the three locational-armour tests. Wolfgate is gun PvP: this is the piece that makes helmets and vests matter per limb.
- **P3-4 Limb damage sprites:** the `PartDamageVisualsComponent` consumer (Onyx `Content.Client/Damage/DamageVisualsSystem.cs` changes + `_Onyx/Wounds/{brute,burn}_damage.rsi`, already copied in WP7 — verify) so wounds show on the body sprite.
- **P3-5 Tests:** `TraumaticAmputationCreatesSevereStumpBleedingTest`, the three locational-armour tests, a surgery-attach assertion (a reattached limb gets `Woundable` and rejoins bleeding — PLAN.md §8.3 trap 2), an organ-damage consequence assertion, a limb-visuals state assertion if testable headlessly. `RepairSelectionAndSnapshotValidationTest` only if `_Onyx.Repairable` is cheap; otherwise skip and record.
- **P3-6 Balance:** Onyx defaults (D4). Amputation thresholds and finishing damage must be reported as numbers in the plan so the user can sanity-check them against Wolfgate gun damage before playtest.
- **P3-7 Docs:** `Docs/_WF/Wolfmed/WOLFMED_PLAN3.md`, manifest and status doc updated in the same work.

## Phase 3 — answers to PLAN3.md §8.6 (2026-09-13)

- **§8.6-1 Amputation by guns/lasers (user decision): YES, guns and lasers can sever.** Deviation from Onyx defaults, expressed ONLY in Wolfgate's own part YAML (the `_WF/Wolfmed` WolfmedBodyPart data), marked `# WOLFGATE (P3 balance)`: (a) Piercing finishing minimum lowered from 40 to **12** so a standard 14-Piercing round can finish an over-threshold limb; (b) **Heat** thresholds added per part equal to that part's Piercing threshold (Head 200, Arm 250, Hand 200, Leg 250, Foot 220) with a Heat finishing minimum of **15** so a 16-Heat laser can finish. Amputation thresholds themselves stay at Onyx values, so gunfire still needs many hits before a finishing shot. The implementing agent must verify how `AmputationSystem` compares accumulated part damage to the threshold (per damage type vs total) and document it; if Heat needs a wound-type mapping (Burn wounds) to count, add it in the same YAML. Replace T-AMP-NOGUN with **T-AMP-GUN** (a limb over threshold is severed by one 14-Piercing hit and by one 16-Heat hit; a below-threshold limb is not) plus a melee case. Record in the manifest as a Wolfgate balance deviation; the balance pass may retune.
- **§8.6-2 Armour (user decision): annotate the 5 vests + the ballistic helmet** (PROTO B as planned). Record the aimed-headshot change in the status doc.
- **§8.6-3:** take P3-D1 (host-gated vital-part Bloodloss charge in `WolfmedBodyPartLifecycleSystem.OnPartRemoved`; no HOOK 19).
- **§8.6-4:** organ damage ships irreversible with Onyx caps; recorded.
- **§8.6-5:** Option B (severed-limb art) in WP11-4 if the package is otherwise green.
- **§8.6-6:** explosion amputation stays out.
- **§8.6-7:** organ damage human-lineage only.
- **§8.6-8:** hand/foot wound visuals fold onto arm/leg (P3-D25).
- **Execution:** packages sequential in the one worktree; append manifest rows directly; one owner per shared YAML file as PLAN3 assigns.

## Phase 4 (2026-09-13) — scope and constraints

Phase 3 is committed (`6329d204e3 Phase 3 completion`). The `GibbingSystem.cs` container-mutation crash is fixed with a marked `.ToArray()` (two loops) before phase 4 starts. Phase 4 = treatment and diagnostics: everything that lets a medic fix what phases 1–3 inflict. Same rules as before (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription audit, D2 for non-wound-hosts, sequential packages, manifest appended directly, no commits, RobustToolbox untouched).

- **P4-1 Reagent treatments (D16):** old-style `EntityEffect` classes in `Content.Shared/_WF/Wolfmed/EntityEffects` (or Server if the base is server-only) keeping Onyx's `!type:` names: `SuppressPain`, `MendFractures`, `TakeStaminaDamage`, `StaminaDamageCondition`, `TreatmentCapabilities` datafield + HOOK 9 on `HealthChange` and `EvenHealthChange` (and `DistributedHealthChange` only if a ported reagent uses it). Onyx wound reagents (`_Onyx/Reagents/Medicine/*.yml`) ported with id collisions resolved: `Stasizium` and `SalicylicAcid` already exist in Wolfgate with different definitions — **rename Onyx's to `OnyxStasizium`/`OnyxSalicylicAcid`** unless the analysts find Wolfgate's versions are functionally equivalent, in which case extend Wolfgate's (marked). Existing Wolfgate topicals/medicines (brute pack, ointment, bicaridine, dermaline, etc.) gain `treatmentCapabilities` so they treat wounds; propose the mapping.
- **P4-2 Tourniquet** (`_Onyx/Medical/Tourniquet`, server-side per D13) and **medical patch** (`Content.Server/_Onyx/Medical/MedicalPatch*`), with prototypes, sprites (license-checked), locale, and cargo/loadout/medkit placement (`_WF/Wolfmed` fills, minimal marked edits to existing medkit fills).
- **P4-3 Wound surgeries on Shitmed steps (D7):** `SurgeryStopBleeding`, `SurgeryTendWoundsBruteDeep`/`BurnDeep`, `SurgeryStopInternalBleeding`, `SurgeryMendFracture` (BoneGel/BoneSetter drive reduction → mended), `SurgeryHealAmputationConsequence` (and make `AmputationConsequenceWound` block `CanAttachPart` until treated — the phase-3 P3-D2 gap), `SurgeryHeal<Organ>` via `OrganHealthSystem.ChangeHealth` (the phase-3 organ-healing gap). Reuse Wolfgate's `SurgeryOpenIncision`/`SurgeryCloseIncision`; **extend** Wolfgate's `SurgeryTendWoundsEffectComponent`/`SurgeryWoundedConditionComponent` (marked) rather than vendoring Onyx's colliding components. Onyx's own `SharedSurgerySystem.*` is NOT ported.
- **P4-4 Diagnostics:** health analyzer wound/fracture/bleeding/organ/pain readout (`HealthAnalyzerWoundDiagnostic`, `HealthAnalyzerOrganInfo`, `HealthAnalyzerChemicalInfo`, Onyx's `HealthAnalyzerSystem` hooks) on Wolfgate's Shitmed analyzer. The UI shape (graft onto Shitmed's monolithic analyzer window vs a parallel Wolfmed panel/tab) is a user decision the plan must present with a recommendation. Examine gains organ-damage status only if Onyx has it.
- **P4-5 Pain numbness / narcotics:** `StatusEffectPainNumbness` chain and the `ModifyStatusEffect` entity effect (deferred from phase 2) if the ported reagents need them; otherwise record as skipped.
- **P4-6 Explosion amputation:** the `ExplosionSystem` hook (one or two lines, body in `_WF`) with the Mono `DamageOriginFlag.Explosion` plate regression test, if the analysts confirm the hook is small; otherwise stays phase 6.
- **P4-7 Guidebook:** Onyx's wounds guidebook entry (`_Onyx/guidebook/wounds.ftl` + prototype) adapted to what Wolfgate actually shipped (no IPC/slime yet, guns can sever, corrected fracture multipliers).
- **P4-8 Tests:** tourniquet (`TourniquetStopsOnlySelectedPartTest`), medical patch, each wound surgery step (`WoundSurgeryTest`/`WoundSurgeryScarTest` from Onyx adapted to Shitmed steps), reagent treatment (a topical with `treatmentCapabilities` heals a wound, one without does not; `SuppressPain`), organ healing, analyzer message contents, reattachment blocked until consequence treated. `HealthAnalyzerPartDamageTest` from Onyx if portable.
- **P4-9 Docs:** `Docs/_WF/Wolfmed/WOLFMED_PLAN4.md`, manifest, status doc.

## Phase 4 — answers to PLAN4.md §8.4 (2026-09-13)

All defaults taken (orchestrator; user pre-authorised more lethality in phase 3):
- **§8.4-1 Explosion amputation:** SHIP (WP12-8), gated on T-EXPLOSION-PLATE + T-EXPLOSION-WRAPPER; tunable via Onyx's explosion CVars.
- **§8.4-2 Organ heal rate:** `amount: 3`.
- **§8.4-3 Surgery scarring / incision bleeding:** ship the four-prototype chain as revised (careful incision carries no bleed effect; `SurgeryStepSealTendWound` closes).
- **§8.4-4 Reagents:** Tier A; Tier B only if WP12-2 is green with time to spare.
- **§8.4-5 Analyzer UI:** PARALLEL — self-contained `_WF` panel mounted by marked lines; Shitmed's window geometry untouched.
- **§8.4-6:** no organ examine line. **§8.4-7:** no `treatmentCapabilities` annotation on existing items now (cable coil first in phase 5). **§8.4-8:** pain numbness skipped and recorded.
- **Gibbing fix:** `Content.Shared/Gibbing/Systems/GibbingSystem.cs` two `.ToArray()` snapshots + `using System.Linq`, all marked `// WOLFGATE`; add a manifest row (WP12-10).
- **Execution:** sequential packages; manifest appended directly; one owner per shared YAML/XAML file as PLAN4 assigns.

## Phase 5 (2026-09-13) — scope and constraints

Phase 4 is committed (`2b4a4675d0 phase 4`). Phase 5 = species coverage and the deferred treatment/pain items. Same rules as before (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription and name-collision audits, D2, sequential packages, manifest appended directly, no commits, RobustToolbox untouched).

- **P5-1 Species profiles:** Onyx's `Ipc`, `Cybernetic`, `Slime` and `Plant` body-part profiles, fracture profiles and species wound sets (`wounds.yml` species sections: mechanical damage, `CyberneticFrameFracture`, slime and plant variants) mapped onto Wolfgate's species. Wolfgate targets: IPC (Mono's IPC with pump organ), protogen (currently excluded from `WoundHost` — decide whether it becomes a cybernetic wound host), slime, diona, and cybernetic limb replacements (`_Shitmed/Cybernetics`, Onyx `_Onyx/Cybernetics`). The plan must state for each species what changes for the player (bleeding oil/coolant? no bleeding? no pain? scars?).
- **P5-2 Circulatory streams for non-organic species:** the phase-1 blocker was that Onyx's IPC/Slime/Plant streams need a shared stage-based metabolizer. The plan must find the CHEAPEST route that gives correct behaviour: (a) profiles that simply set bleed multiplier 0 / a Wolfgate-native bloodstream reagent (IPC oil/coolant, slime jelly) via the existing server-only `BloodstreamComponent`, without porting the stage metabolizer; (b) only if (a) cannot work, scope the metabolizer rewrite as a separate, explicitly out-of-scope project. Do not port `MetabolismStagePrototype`/`SolutionManagerComponent` in phase 5.
- **P5-3 `treatmentCapabilities` on existing items:** the annotation pass deferred from phase 4 (§8.4-7): which Wolfgate items should treat which wound stages (cable coil / welder for IPC and cybernetic limbs, brute packs, gauze, ointment, etc.) — data-driven `_WF` overrides or marked lines; the plan lists every item and its capability set.
- **P5-4 Pain numbness / narcotics:** `StatusEffectPainNumbness` chain and an old-style `ModifyStatusEffect`-equivalent entity effect if any ported or Wolfgate reagent should grant pain numbness (morphine-class chems); otherwise record as skipped with reasons.
- **P5-5 Health analyzer / examine:** whatever the species profiles need to display correctly (mechanical damage names, "leaking oil" instead of bleeding) in the phase-4 panel and phase-2 examine text.
- **P5-6 Tests:** one wound-host test per new species profile (routing, bleeding or its absence, pain or its absence, fracture profile), cybernetic limb on an organic body, the treatment-capability matrix (at least three items), IPC/protogen spawn-and-delete smoke, analyzer text for a mechanical wound.
- **P5-7 Docs:** `Docs/_WF/Wolfmed/WOLFMED_PLAN5.md`, manifest, status doc; the status doc's "next phases" collapses to phase 6 (predicted routing) plus anything phase 5 explicitly defers.

## Phase 5 — answers to PLAN5.md §8.4 (2026-09-14)

- **U5 Cable coil (user decision): YES, nerf now** — Mechanical-only `treatmentCapabilities`; humans lose the cable-coil burn heal. Changelog-worthy; record as a deliberate balance fix.
- **U4 Protogen (user decision): LIFT the exclusion** — organic-profile wound host; organ gap (no `OrganDamage` on protogen organs) recorded.
- **U1 IPC pain (user decision): KEEP Onyx's pain on IPCs** (pain shock possible, no chemical relief; repair lowers pain).
  *[M6 correction: the CONSC report's "IPCs have no pain" was wrong; this entry is right. Since M1a (OD9) pain only
  Downs a machine: it never faints one or holds one under.]*
- **U3′:** (b) 190/210 `MajorLimb` parity for IPC limb gib triggers. **U15:** (a) marked `Destructible` on `CyberneticPartBase`, no Heat/Ash rung. **U2:** (a) `SiliconWolfmed` container + PROTO S/T companion lines (welder/nanite keep working). **U13′:** (b) `_WF` `InorganicWolfmed` part container restoring Cold/Caustic.
- **Group B:** all PLAN5 §8.4 defaults (U16 `chemicalMaxVolume: 0`, no `InjectableSolution`; U17 diona limbs destroyed not severed; U18 drop PROTO R; pain numbness closed permanently per numbness.md).
- **Execution:** sequential packages; manifest appended directly; one owner per shared YAML file as PLAN5 assigns; the `_WF` container file lands in WP13-0 (revision N-ordering fix).

## Phase 6 (2026-09-14) — DEFERRED by the user (token budget)

Scope when resumed: client prediction of wound routing (removes the transient damage-number / limb-doll flicker on wound hosts), the `HurtCommand` part argument (patch kept at `<plan>\wp\WP8-hurtcommand-deferred.patch`), locational-armour follow-ups. Run it lean: one Opus design+implement agent using `<plan>/reports/analysis/damage-bridge.md` and PLAN.md §2/§3, one Sonnet verify at the end.

## Phase 6 — shipped (2026-09-19)

- **P6-D1 The flicker is fixed by suppression, not by prediction.** The mechanism: on a wound host the client
  ran the ordinary whole-body path (routing's `OnBeforeDamageChanged` and `OnDamageDealt` are both
  `_net.IsServer`-gated), wrote the full post-armour figure into the mob's own `DamageableComponent`, and had
  it replaced one state later by the server's projection (the sum of what each part kept, after the part
  profile and the locational armour). Predicting the routing properly is not possible: wounds are entities
  created server-side in a part container, the part choice consults server-only state, and `WoundSystem`
  refuses to run off the server. So the client now suppresses the *write* at the existing GUARD D seam and
  lets the mob's damage be pure server state. Cost: damage numbers and the health bar move one state late on
  wound hosts, which is what D35 already recorded and what the flicker was hiding.
- **P6-D2 The predicted hit still reports its damage.** `DamageDealtEvent` gained a `Suppressed` flag instead
  of reusing "clear the dict": clearing it would make `TryChangeDamage` return an empty specifier, and
  `SharedMeleeWeaponSystem` gates the red damage flash and the blunt→stamina prediction on that return while
  the server's `DoDamageEffect` filter deliberately excludes the attacker. Clearing it would therefore have
  left an attacker with no feedback at all on wound hosts. One token of upstream change
  (`|| dealt.Suppressed`); the decision itself lives in `Content.Client._WF.Wolfmed.Damage.WolfmedPredictedDamageSystem`.
- **P6-D3 Hitscan is derived, not declared.** `WolfmedWoundCause.Hitscan` existed but nothing ever produced
  it, so beam hits fell through to `Environmental`. `HitscanBasicDamageSystem` now passes the beam entity as
  `tool` (one marked line) and `WolfmedWoundRuleSystem.GetCause` reads `HitscanBasicDamageComponent`, so no
  per-prototype data is needed. **Only `WolfmedRuleGunshot` and `WolfmedRuleGraze` admit it**: the fork's
  Piercing hitscan is railgun and coilgun fire (`Magnum45`, `Coilgun134x92mm`, `Hitscan145x114mm`), which is
  hypervelocity and leaves nothing behind, so it gets the through-and-through wound but never a lodged round
  or a spent-round item. Every laser is Heat or Radiation, so the Piercing rules never see one and burns are
  untouched. Side effect, accepted: a Blunt hitscan (`Hitscan145x114mmEMP`) no longer counts as a fall for
  `WolfmedRuleDislocationThrown`.
- **P6-D4 The armour pass is by slot, and full-body suits stay unannotated.** 123 `- type: Armor` blocks
  gained `coverage:`: head, mask and eye items `[Head]`, gloves `[Hand]`, dedicated over-uniform armour
  `[Torso, Arm, Leg]` (the set phase 3 gave the `_Mono` vests). Hardsuits, EVA, bio/rad suits, modsuit bodies,
  coats, winter coats and jumpsuits keep coverage **unset**, which P3-D5 defines as "protects every part" —
  that is what "full-body suits declare full coverage" means here, and writing an explicit part list for them
  would go stale the first time a new `BodyPartType` appears. `WolfmedArmorCoverageTest` guards both halves.
- **P6-D5 Balance consequence, stated plainly.** This finishes what P3-D26 flagged: a helmet no longer
  protects the torso and a vest no longer protects the head, so stacking a vest with an unrelated helmet stops
  working. Aimed headshots against a vest-only wearer hit for full damage, and hands/feet are now protected
  only by gloves/boots or a full-body suit. Left for a balance pass, not guessed at: coats/winter coats and
  jumpsuits with armour (soft armour over an ambiguous area), the `_Mono` Aurora exosuit (described as having
  no head covering but sealing the rest), `_NF` brass knuckles (an armour *penalty* on a hands item), and
  every `- type: Armor` on non-clothing (vehicles, blast doors, mothroaches).

## Phase 7 backlog — "Viscera" (user wants, 2026-09-14; not started, token budget)

Wolfmed-only additions in `_WF/Wolfmed` (no Onyx source), to be planned lean (one Opus design+implement agent per package, builds per stage, lint/server/tests once at the end, one two-reviewer round):
- **V1 Wound SFX by cause:** on `WoundCreatedEvent`/`WoundChangedEvent` above a severity floor, play a sound keyed by the wound's damage type (Slash/Piercing splatter, Blunt thud, Heat sizzle, Shock crackle; mechanical profiles: clank/spark). Data-driven map on the body-part profile or a `_WF` `WoundSoundProfile` prototype; audio predicted where the routing is server-only means `PlayPvs`.
- **V2 Dismemberment SFX + VFX:** on `PartDetachedEvent`/amputation, a tear/rip sound (organic) or shear/spark (mechanical), a blood splatter (existing `BloodstreamSystem` puddle spawn) or spark/debris entities for synthetics, plus a gib-style limb fling (already thrown).
- **V3 Part degradation visuals:** per-part severity → sprite layer stages showing muscle/bone (organic) or struts/wiring (synthetic) on the humanoid sprite; extends phase-3's `PartDamageVisualsComponent` layers with new RSI states (needs art: 2 stages × 8 parts × organic/synthetic).
- **V4 Debris/gib particles on hits:** small blood mist / spark effect entities spawned at the struck part's position scaled by severity; throttled per part.
- **Health bar fix (done 2026-09-14):** `EntityHealthBarOverlay.CalcProgress` ratios clamped to [0,1] (marked) — projected wound damage can exceed the dead threshold, which drew a negative bar for ghosts.
- **V5 Splint (2026-09-14):** a `_WF` handheld splint item (craftable from cloth + metal rod or steel; stocked in medkits where a cell is free and the medical vendor) applied to a fractured limb via do-after; sets the fracture's treatment to *Reduced* (effects ×0.25, no surgery) and shows on the analyzer; a new hard hit resets it as usual. Mended still needs bone gel surgery or Osteogen/Stasizium. Reuse `WoundFractureSystem.SetTreatment` and the tourniquet's part-targeting pattern.
- **Guide fixes (done 2026-09-14):** Onyx's `FTLTextpart` tag inlined as markdown, bracket escapes removed, headings capped at `##`, guide names restored in `_WF/Wolfmed/guidebook/wounds.ftl`.

## Phase 8 backlog — "Wound expansion" (user wants, 2026-09-14; all confirmed wanted; not started)

All in `_WF/Wolfmed` data and small `_WF` behaviours; Onyx files untouched. Plan lean (per-package Opus design+implement, builds per stage, lint/server/tests once at the end, one review round). Order by gameplay value:

- **W0 Healing multiplier fix (first, data-only):** bleeding wounds `healingMultiplier: 0` (damage removal never closes them; gauze/sutures/surgery do), all others 0.15 (Onyx's intended value). Bruise packs treat Blunt wounds only; ointment Burn only; sutures Slash/Piercing. Update the treatment-matrix tests and the guide.
- **W1 Ballistic:** `GunshotWound` (Piercing from projectiles; through-and-through vs *lodged round* at higher severity — lodged keeps bleeding/pain until removed), `ShrapnelWound` (explosions, buckshot: several small embedded fragments), `GrazeWound` (low severity, brief bleed). **Removal ala CMSS13:** forceps or any sharp item (knife, scalpel, glass shard) can pry fragments/rounds out via a do-after; knives hurt more and risk a small slash wound; forceps/surgery are clean. Removed objects spawn as items (spent round / shrapnel).
- **W2 Slash and bite:** severe-stage `ArterialBleed` (fast bleed; gauze only slows, tourniquet or clamp surgery stops), `TendonCut` on arms/hands (manipulation penalty, suture surgery) and legs (limp), `Avulsion` from bites (infection risk, scars).
- **W3 Blunt:** `CrushInjury` at severe stage (function loss + internal-bleed chance), `Concussion` on head (blur, stutter, short knockdown; sleep/time heals), `Dislocation` on joints (fracture-like penalty, cleared by a painful "relocate" do-after, no bone gel), `OrganContusion` on torso (organ damage without destruction).
- **W4 Burns:** `Charring` top stage (needs skin graft step or synthflesh), *cauterisation* (Heat on a bleeding wound seals it: lasers/welders stop bleeding at the cost of a burn), `Frostbite` (numbness then necrosis), `ChemicalBurn` (keeps ticking until washed at a shower/water), electrical internal burns with a heart-damage chance and spasm.
- **W5 Time-based:** `Infection` (open wound untreated for N minutes: fever, slow toxin, worsening; antibiotics or cleaning; start from Onyx's surgery-infection component), `Necrosis` (tourniquet left too long or late reattachment; permanent, amputate and replace), `Sepsis` as the systemic stage.
- **W6 Mechanical (IPC/cybernetic):** `Dent`, `Breach` (fluid leak, welder), `ShortCircuit` (shock: brief stun + spark), `ServoDamage` (function loss, cable coil), `Overheating` (slows until cooled).
- **W7 Treatment matrix + content:** forceps item, skin graft step, antibiotics reagent, shower/water wash interaction, guide and analyzer wording, tests per wound type.
- **Balance note (2026-09-14):** fractures almost never occur with Onyx's per-hit thresholds (20/35/50/60, 5–100 %) against Wolfgate melee (8–15 Blunt per swing); proposed `_WF` override 12/20/32/45 at 25/50/80/100 % with accumulation 0.8, and optional Piercing fracture profile for heavy rounds — decide with W0.
- **Art sourcing (2026-09-14):** sprites for the new items (forceps, splint, spent round, shrapnel, skin graft) and phase-7 degradation/debris art may be taken from other SS13/SS14 codebases (tg, CM-SS13, Goon, Bay, other SS14 forks) provided the source license is CC-BY-SA or otherwise compatible and every `meta.json` records license and attribution verbatim. CC-BY-NC-SA and unlicensed art are excluded. Record each borrowed RSI in the manifest with its source repo and commit.

## Final stages (2026-09-19)

Phases 6, 7 and 8 plus the crit heartbeat (WP H) shipped this run: H, W0–W7 (phase 8), V5/V124/V3 (phase 7)
and P6 (phase 6), 14 work packages, one report each in `<plan>\p6\wp\`. The
decisions below were made by those packages, not re-litigated here; this section exists so the owner does
not have to open all 14 reports to find them.

**Decisions carried from the individual work-package reports:**
- **Fracture thresholds are 12/20/32/45 at 25/50/80/100 %** (`WolfmedFractureProfile`, W0), replacing Onyx's
  20/35/50/60 at 5–100 % because Wolfgate melee (8–15 Blunt per swing) almost never crossed the Onyx bands.
  `accumulationMultiplier` raised 0.4 → 0.8 alongside it. `BoneFractureWound`'s own stage thresholds were
  lowered to match (20/35/50/60 → 12/20/32/45) so a fresh Hairline fracture is not below the wound's own
  lowest stage.
- **Deterministic damage bands instead of chance rolls, wherever the spec allowed it** (W1, W2): a heavy
  round (>= 18 Piercing in one hit) always lodges rather than lodging "at high damage or by chance"; the
  arterial-bleed and tendon-cut thresholds are flat damage bands, not rolls. The one chance roll that
  remains is internal bleeding at 30 Blunt / 40 % (W3), kept because Onyx's own `InternalBleedingWound`
  is chance-based. Rarity lives in the thresholds, which keeps the wound suite RNG-free.
  *[M6 correction: the lodged round is a roll, not a certainty: 35 % at 18 Piercing or more
  (`WolfmedRuleLodgedRoundHeavy`) and 20 % from 7 to 18 (`WolfmedRuleLodgedRound`), kept as flavour by OD15. The crush
  internal bleed became a band in M3: every Blunt hit of 40 or more.]*
- **No forceps item.** The existing hemostat and tweezers components are the "clean tool" for pulling an
  embedded object (W1); W7's planned forceps item was dropped because the two components it would have
  carried already exist and are already lathe-printable and in the surgical crate.
- **No Piercing fracture profile.** `WolfmedBodyPartComponent.FractureProfile` is a single id and
  `WoundFractureSystem` assumes one fracture per part (`GetFracture` returns the first, creation bails if
  one exists, grading re-derives the profile from the part). A second damage type needs a list on the
  component plus a rewrite of creation, grading and treatment — past the "small marked change" the task
  allowed, so W0 skipped it as scoped. Still open; see below.
- **Mechanical wounds keep `healingMultiplier: 1`, not the 0.15 organic nerf** (W0). A welder is the only
  thing that closes a chassis wound and it works by removing damage; at 0.15 the repeat loop would burn
  fuel on a wound it could no longer reduce and chassis wounds would become permanently open. Flesh has
  sutures and surgery for the 0.15 case; chassis does not.
- **Spaceacillin is not in a medkit** (W7). Every medkit is at 4–8 cells already and `StorageFill` silently
  drops an entry (and logs an error that fails every pair test) when a kit overflows. The medical vendor
  (3), `CrateMedicalSupplies` (2) and the chemistry reaction from W5 are three routes without risking a
  spawn error; the skin graft did fit the burn kit and shipped there instead.
- **The routing flicker is fixed by client-side suppression, not by prediction** (P6-D1/D2). Wound routing
  cannot be predicted — wounds are server-side entities in a per-part container and `WoundSystem` refuses
  to run off the server — so the client now suppresses the *write* of predicted damage onto a wound host's
  `DamageableComponent` at the existing GUARD D seam (`DamageDealtEvent.Suppressed`, one upstream token)
  instead of writing and then being overwritten a state later. The report (not the return value) is kept,
  so melee's red-flash and stamina prediction still fire for the attacker. Cost, already recorded at D35:
  damage numbers and the health bar move one network state late on wound hosts.
- **The heartbeat asset's license is pending the project owner's confirmation** (WP H /
  `Resources/Audio/_WF/Wolfmed/attributions.yml`). The audio and its attributions entry were already on the
  branch before WP H; the entry reads `license: "Custom"` with copyright text "Source and license to be
  confirmed by the owner." WP H only wired playback and did not source or re-license the file. Needs an
  answer before this branch ships to players.
  *[M2-M6 review correction: resolved by `65aab0b387` (heartbeat attribution). The entry reads
  `license: "CC-BY-SA-3.0"`, taken from Skyrat-tg by Skyrat-SS13 and downmixed to mono, source
  `https://github.com/Skyrat-SS13/Skyrat-tg`. Nothing is pending.]*

**Every gap still open, for the owner to act on:**
- No Piercing fracture profile (W0) — see decision above; needs a list-typed `FractureProfile` plus a
  rewrite of `WoundFractureSystem`'s creation/grading/treatment assumptions if a second fracture type per
  part is wanted.
- `Surgery` stays in `WolfmedWoundCause` with nothing deriving it, and no rule filters on it (W1/W7);
  deriving it would add an upstream hook nothing currently reads, so it was left undone on purpose.
- Systemic bleeding chemicals (`ModifyBodyBleeding`/`StopBodyBleeding`) still stop an arterial bleed; only
  the part-targeted topical path is gated behind the arterial-bleed treatment ladder (W2, open per W4).
  *[2026-09-29: no reagent reached either method, so none stopped anything; the reagent path added then clots an
  artery only down to its `CoagulantFloor`. See "A coagulant reaches the wounds" at the end.]*
- The analyzer names every wound but has no dedicated arterial / dislocated / concussed / numb / residue /
  mechanical-overheating flag; a medic reads the wound name and the guide's quick reference (W2–W6).
- Necrosis has no sprite or visual on the limb, only the analyzer flag, the popup and the wound name (W5).
- The short circuit's spark effect spawns at the body rather than at the struck part, because parts live in
  nullspace with no coordinate of their own (W6). `WolfmedOverheatingComponent.CoolingPerMinute` is
  networked but nothing on the client reads it yet.
- Seven interactions reuse the plain do-after bar with no sound or custom visuals of their own: embedded
  object removal (W1), joint relocation (W3), deliberate cautery (W4), tourniquet loosening (W5), wrench
  panel-beating (W6) and the splint (V5). V124 gave five of these a *begin/end sound* through
  `SoundSpecifier` fields with sane defaults, so only the bar and any bespoke visual are still missing.
- Spaceacillin has a chemistry reaction and is stocked on the vendor and in a supply crate, but has no
  lathe recipe and is in no medkit fill (W5/W7, deliberate per the decision above).
- Fever (infection's Spreading stage) climbs toward a fixed 313 K with no shiver, sweat or other feedback,
  and nothing reads it back out except the existing temperature UI (W5). Non-sterile *conditions* are not
  modelled beyond W1's improvised embedded-object removal calling `Contaminate` once (W5).
- V3's per-part degradation stage thresholds (25/60 summed severity) are a first guess against a
  dismemberment range of 80–200 and want a playtest pass; the bone overlay reads subtle at 32 px on pale
  skin tones.
- No inhand sprite for the splint (V5) — the RSI has room for eight frames. Nothing clears a splint's
  reduction when the part is amputated (the item is consumed on use, so there is no worn state to clean up
  and the reduction simply dies with the part).
- Wound sounds and hit debris are all `PlayPvs`, never predicted, because wound creation is server-only
  (V124); blood mist is the existing puddle-splatter sprite re-tinted, not bespoke art.
- Locational armour is unannotated (full coverage) on armoured coats, winter coats and jumpsuits (soft
  armour over an ambiguous body area), the `_Mono` Aurora exosuit (seals everything except the head), `_NF`
  brass knuckles (an armour *penalty*, so narrowing its coverage would be a buff) and every non-clothing
  `- type: Armor` block — all left for a balance pass rather than guessed at (P6-D4/D5).
- The heartbeat asset's license is pending owner confirmation — see decision above.
  *[M2-M6 review correction: closed; CC-BY-SA-3.0 from Skyrat-tg since `65aab0b387`, see above.]*

## Playtest fixes (2026-09-19)

- **IPCs take no Airloss-group damage, ever (owner decision, reverses P5-D5/P5-D5b).** `Bloodloss` came off `SiliconWolfmed` and the IPC bloodstream's `bloodlossDamage`/`bloodlossHealDamage` are empty: Bloodloss reads as oxygen loss on the analyzer and an IPC does not breathe. Consequence: an oil leak currently costs an IPC nothing but the oil. Open: give low oil its own consequence (slowdown or overheating) if leaks should matter.
  *[M6 correction: a leak costs more than the oil. Consciousness reads oil as the blood input: 50 % or less Downs the
  chassis and 35 % or less shuts it down, cause Oil ("HYDRAULIC PRESSURE LOW", M1a).]*
- **Body damage ceiling.** `wolfmed.body_damage_cap` (default 600, absolute, 0 disables). Routed part damage past it is discarded (`WolfmedBodyPartSystem.ClampToBodyCap`, one marked hook in `WoundDamageRoutingSystem`). Absolute rather than a multiple of the dead threshold because an IPC dies at 100 while its limbs come off near 200. Infection and sepsis no longer damage corpses.
  *[M6 correction: the ceiling only ever applied to damage with no origin that was not an explosion, and M1b replaced
  it: a per-part ceiling (0.8 of the part's lowest destruction trigger) for that damage on the living, with the cut
  still carried as the hit's Overflow; 600 is now a corpse ceiling. An IPC does not die at 100: damage totals decide
  nothing on a wound host; a chassis dies of core failure, losing its core or head, or a gib.]*
- **One severed arm took both IPC arms.** `SharedBodySystem.PartAppearance` copied every marking in the limb's category (Arms spans both sides; IPC limbs are markings). Now filtered to the layer's own markings (marked).
- **Heartbeat kept looping after death.** `SharedAudioSystem.Stop` is a no-op on ticks that are not first-time-predicted; the client system now deletes its stream directly and reconciles every frame.
- **Infection tick crashed the server** ("Collection was modified"): infection, necrosis and frostbite ticks now buffer their targets.
- **Test harness trap:** a test that fails inside a whole-suite run can be reported as *Skipped* ("dirty-disposed") while the run says Passed. Always rerun skipped tests standalone.

## Playtest fixes, round 2 (2026-09-20)

- **A stopped bleed stays stopped.** Two causes of "gauze held for seconds" and "a bruise pack made it bleed": (1) `WoundBleedingSystem.ReduceBleeding` removed the bleeding component at zero, and `WoundSystem.SyncRuntimeComponents` re-rolled the bleed at full wound severity on the next severity change of any kind, healing included; (2) nothing ever set `Bandaged` for an ordinary wound, so no dressing showed. Now: gauze (`dressing: true`) keeps the component at zero marked `Bandaged`, and a wound only rolls for bleeding when it has grown since the last sync (`WoundComponent.LastSyncSeverity`, marked). A fresh hit still reopens it.
- **Bleeding slowed.** `wolfmed.bleed_rate` (default 0.6) multiplies every wound's rate where it is computed, so analyzer, spurts and bloodstream agree. Tests that assert Onyx's literal rates pin it to 1.
- **Splints take torso and head.** Ribs and skulls fracture and the procedure says to splint; the art has chest and head wraps.
- **Health analyzers need no power cell** (`PowerCellDraw`, `ToggleCellDraw`, `ActivatableUIRequiresPowerCell` commented out on `HandheldHealthAnalyzer`; the slot stays so fills and maps load).
- **Analyzer window geometry (REVIEW, 2026-09-22).** The analyzer window itself is 900x600 and resizable,
  two panes side by side, rather than the fixed 350x650 of §8.4-5. The wound panel, the treatment advice and
  the doll do not fit a single narrow column; §8.4-5's "window geometry untouched" is superseded for the
  window frame, and everything inside it is still Shitmed's own layout.
- **Analyzer doll geometry.** Shitmed laid the analyzer's doll buttons out at 2.5x over a doll `SetupIcon` draws at 3x. The XAML now carries the HUD targeting doll's exact 3x geometry and the highlight uses the HUD's mechanism (base part texture at 3x, centred).

## EMP and machine bodies (2026-09-20)

- `WolfmedEmpSystem` (server): an `EmpPulseEvent` reaching a non-organic `Woundable` part that is attached to a wound host deals Shock to it through the routing, which opens the W6 short-circuit wound (stun, sparks, cable coil). Parts are collected per body and resolved at the end of the tick so one pulse shares a budget: `wolfmed.emp_part_damage` (15) per part, `wolfmed.emp_body_damage` (45) per body per pulse. A lone cybernetic limb takes 15; a ten-part IPC takes 4.5 a part. Shitmed's own `CyberneticsSystem` still disables cybernetic parts for the pulse duration; this adds the damage. IPCs die at 100, so one EMP cannot kill a healthy one and three can.
  *[M6 correction: the shipped values are 40 per part and 120 per body. IPCs do not die of damage totals, so no number
  of EMPs kills one by itself; the Shock lands on the parts (pain, short circuits) and reaches the core only through the
  torso's reach line (M3).]*


## Evisceration (2026-09-20)

- **The torso's overflow buys disembowelment.** D9 never severs a torso, so `WoundableComponent.AmputationOverflow`
  past the torso's 250 cap had no consumer. `AmputationSystem.OnPartDamageOverflowed` now raises
  `WolfmedTorsoOverflowEvent` there (a marked three-line raise, no logic) and `WolfmedEviscerationSystem`
  (server) answers it.
- **Trigger, all data.** `wolfmedEviscerationProfile` (`WolfmedEviscerationDefault`), named by the torso's
  `WolfmedBodyPart.eviscerationProfile`. A part that names no profile can never be opened. `finishingDamage`
  is `Slash: 35`, so Blunt, Piercing and Heat can never do it however hard they land;
  `explosionFinishingDamage` (`Slash 25 / Heat 30 / Blunt 45`) is the blast table. Both are read against the
  OVERFLOW of one hit, which is the whole hit once the torso is at its cap. No CVar gate; alive, crit and
  dead all qualify; one evisceration per torso, held by `WolfmedEviscerationComponent`.
- **What comes out is data.** `organs` (stomach, liver, kidneys, intestines) always; `vitalOrgans` (heart,
  lungs) only for an explosion or on `vitalChance` 0.15, seeded through `WolfmedEviscerationSystem.ForcedRoll`
  in tests. The brain is in the head and a positronic one is deliberately absent from the chassis list.
- **The open belly IS the open incision.** The system grants Shitmed's own `IncisionOpen` and `SkinRetracted`
  to the torso, so every torso surgery that wants an open incision lists with no scalpel and no retractor,
  and the SHIPPED organ insertion surgeries are what put the organs back. It remembers whether it granted
  them, so a patient a medic had already cut open keeps their incision when the tear is closed.
- **Exit.** `SurgeryCloseEvisceration` (hemostat clamp, then cautery) removes `WolfmedEviscerationWound` and
  leaves a sutured `SlashWound` at the severity its `WolfmedClearedWoundBehavior` names. It completes with
  organ slots still empty on purpose: a closed patient needing a transplant beats an open abdomen.
- **IPCs do it too.** The same trigger on a mechanical torso makes `WolfmedChassisBreachWound` ("torn
  chassis"), ejects the chassis's own torso components, leaks the body's own reagent (oil), hisses instead of
  tearing and refreshes `WolfmedMachineSparkSystem`. `SurgeryWeldChassisBreach` (wrench, then welder) leaves
  the ordinary `WolfmedBreachWound`. An IPC torso had no damage cap at all, because it parents
  `BaseTorsoInorganic` rather than `BaseTorso`; `WolfmedBaseTorsoIpc` supplies one.
- **The procedure window can see an empty slot.** `HealthAnalyzerWoundDiagnostic.MissingOrgans` and the
  `OrgansRestored` step check, so "organs back in" greys itself.

## Aim scatter (2026-09-20)

Bullets no longer always land on the shooter's targeted part. `WolfmedAimScatterSystem` (shared, `_WF/Wolfmed/Targeting`)
projects the gun's current spread cone (`GunComponent.CurrentAngle`, floor `MinAngleModified`) out to the target's range and
compares its half-width to the part's size (`WolfmedBodyPart.aimSize`, defaults per part type): chance = size / stray,
clamped to `wolfmed.aim_worst_chance` (0.15) .. `wolfmed.aim_best_chance` (0.9). A miss goes to a weighted neighbouring part.
Only hits whose tool is a projectile are rolled; melee, thrown items and explicit `targetPart` calls are untouched. Hitscan
already lands on a random part (its origin is the gun, which has no targeting). `wolfmed.aim_scatter` turns it off.
Hook: one marked line in `WoundDamageRoutingSystem`. Test: `WolfmedAimScatterTest`.
*[M6 correction: the shipped clamps are 0.1 worst and 0.75 best, as "Dying view" below records.]*

## Blast dismemberment (2026-09-20)

Onyx's explosion severing reads one limb's share of the blast against that limb's totals, so it almost never fires. After a
blast is routed, `WolfmedExplosionSystem.TryBlastDismember` rolls on the whole blast (armour already applied): chance per limb =
(total - `wolfmed.blast_dismember_min` 30) / (`wolfmed.blast_dismember_full` 150 - min) x `wolfmed.blast_dismember_chance` 0.8,
rolling 1 + total/full random limbs through `AmputationSystem.TryAmputate`. Torso never; head only with
`wolfmed.blast_dismember_head`. `wolfmed.blast_dismember` turns it off. Test: `WolfmedExplosionTest.BlastSizeRollsLimbsOffTest`.
*[M6 correction: until M3 Onyx's own per-part explosion roll could still take the head with the CVar off (P27). M3's
veto in `AmputationSystem.HandlePartDamageApplied` makes this sentence true (`BlastHeadTest`).]*

## Dying view (2026-09-20)

Client-only `WolfmedDyingEffectsSystem` + `WolfmedDyingOverlay` (shader `WolfmedDying`, `Textures/_WF/Wolfmed/Shaders/dying.swsl`).
A level 0..1 starts at half the crit threshold, reaches 0.55 on going critical and 1 at the dead threshold, eased so it never pops.
It drives: colour draining to cold grey, double vision, a tunnel that squeezes on a heartbeat (pulse races toward crit, then slows),
eyes drifting shut every few seconds past 0.72, and a slow camera sway through `GetEyeOffsetEvent` on a client-only
`WolfmedDyingSwayComponent`. Sway honours `accessibility.reduced_motion` and the screen shake slider; `wolfmed.dying_effects`
(client) turns it all off. Aim scatter defaults moved to 0.75 best / 0.1 worst.

## Consciousness (2026-09-22)

The premise, from the owner: players are far too easy to kill. On a wound host (`WoundHostComponent`) damage
totals now decide nothing at all. Two meters replace the `MobThresholds` crit/dead decision; this package is
the first, CONSCIOUSNESS. LIFE is the BRAIN package and is not built yet. Mobs without `WoundHost` are
untouched and still cross their thresholds exactly as before.

- **The gate is one marked line.** `MobThresholdSystem.CheckThresholds` opens with
  `if (_wolfmedConsciousness.OwnsMobState(target)) return;`, where `OwnsMobState` is
  `wolfmed.consciousness && HasComp<WoundHostComponent>`. Nothing else in the threshold system changed:
  `CurrentThresholdState` simply never moves for a wound host, so `OnUpdateMobState` leaves `MobState.Invalid`
  and `ChangeState` no-ops. Consequence: **a wound host can no longer die of damage at all** until BRAIN
  lands. Dead is reachable only by the paths that never went through the thresholds (a destroyed brain in
  `OrganHealthSystem`, a gib, an admin). Defib and CPR still compare damage to the dead threshold and were
  left alone per the spec.
- **Three states.** Up, Downed (`WolfmedDownedComponent`, conscious and on the floor) and Unconscious
  (`MobState.Critical`, unchanged). Dead is untouched.
- **Four inputs, each normalised to "1 = this state".** Effective pain / (0.70 x soft cap) for Downed; pain
  before the soft clamp / (1.25 x soft cap) for Unconscious; blood volume against 0.60 and 0.45; both legs
  disabled or missing; and external pressures (0 none, 1 unconscious), which reach Downed at 0.7. The worst
  wins. Hysteresis `wolfmed.consc_hysteresis` 0.1 means leaving a state needs the reading back under 0.9.
  *[M6 correction: the shipped lines are pain 0.95 and 1.4 of the soft cap (`wolfmed.consc_pain_down`,
  `wolfmed.consc_pain_out`) and blood 0.5 and 0.35 (`wolfmed.consc_blood_down`, `wolfmed.consc_blood_out`). Since M1a
  the 1.4 pain line starts a bounded faint (20 s) rather than holding anybody Unconscious.]*
- **"Pain before the soft clamp" is the sum of the parts, not a new field.** `PainSystem.SetPain` clamps the
  part *and* the body to `SoftPainCap` (135), so the body's own value can never read past 1.0 of the cap.
  `WolfmedConsciousnessSystem.GetUncappedPain` sums `GetPain` over the body's parts instead. No `_Onyx` edit,
  and the vignette's own value is untouched.
- **Airloss is a pressure, not a threshold.** `Airloss / (MobThresholds Critical)` is pushed through
  `SetExternalPressure(body, "airloss", level)` on every `DamageChangedEvent`, so suffocation still reaches
  Critical with the thresholds gated off. BRAIN replaces this key with real brain oxygenation.
- **Blood is read on the bloodstream's own tick.** `BloodstreamComponent` raises no event when its volume
  changes, so `BloodstreamSystem.Update` carries one marked three-line call at the point where it has already
  computed the percentage. Everything else is event-driven (pain changed, damage changed, part functionality
  changed, a painkiller dose) plus a 0.5 s poll of bodies that still have a nonzero input.
- **Painkillers are four tiers and no healing.** `WolfmedPainReliefComponent` holds one dose per reagent,
  written by the `WolfmedPainRelief` metabolism effect. Weak subtracts pain from the Downed test only; Strong
  subtracts from both, masks the wound slowdowns (one marked hook in `FractureEffectsSystem.OnRefreshSpeed`)
  and builds sedation; Stimulant lifts Downed outright regardless of pain; Emergency lifts both for a window
  and then crashes (pain x1.3, Downed 10 s). **No tier touches blood, airloss or an external pressure.**
- **Sedation is the overdose.** It slows movement and, past 0.6, applies Asphyxiation scaled by how far past.
  That is a stand-in: BRAIN takes it over through `SetExternalPressure(body, "sedation", level)`.
- **The dying view reads consciousness, not damage.** `WolfmedDyingEffectsSystem.TargetLevel` returns
  `WolfmedConsciousnessComponent.Depth` for a wound host (0..0.35 approaching Downed, 0.35..0.55 Downed,
  0.55..1 Unconscious by blood and pressure). `Level` itself is unchanged and still serves everything else.
- **Rejuvenate is now the only thing that revives a wound host**, because the thresholds no longer do; the
  handler sets `MobState.Alive` itself and re-evaluates a tick later, once every other rejuvenate handler has
  run. *[M6 correction: no longer the only one. The defibrillator (BRAIN, the hand paddles and the pod) and the IPC and
  synth restart button (M2) revive a wound host too.]*

## Autodoc (2026-09-22)

A one-tile surgical pod, `MachineAutodoc`, running "S.A.M.". It performs REAL surgery: the pod is the
performer in Shitmed's own step machinery, so every wound, condition, sound and side effect of a
hand-performed surgery applies. It can only perform surgeries and push reagents; it never bandages, never
heals by fiat and never applies a topical.

- **The pod is the surgeon, through three marked hooks.** `SharedSurgerySystem.GetTools` asks the performer
  for its own toolset first (`WolfmedSurgeryToolsEvent`) and falls back to hands, so a machine with no hands
  can hold a scalpel; `IsLyingDown` and the operating-table condition both accept a pod occupant
  (`SharedAutodocSystem.OnOperatingPlatform`). All three bodies live in
  `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Autodoc.cs`; the upstream files carry six marked
  lines between them. `WolfmedPerformStep` is the entry point: it repeats every check `OnTargetDoAfter` makes
  and then raises the same `SurgeryStepEvent`, with no do-after and no hands.
- **A procedure ends on its own last step, or when the surgery stops being valid.** `GetNextStep` walks the
  requirement chain, and the closing step of most surgeries removes the incision its requirement opened, so a
  naive loop re-opens the patient forever. The pod stops when the queued surgery's own final step reads
  complete, or when a step it already started performing no longer validates - which is exactly what a
  surgeon sees when the entry leaves the menu after the fracture is mended.
- **The pod is sterile and slow.** `SanitizedComponent` is on the machine, so the unsterile-surgery poison
  never fires against a patient the pod is treating. Base step time is 1.25x a surgeon's, falling to 0.9x at
  the best manipulator tier; matter bins scale the reservoir; capacitors the draw. Malfunction is 2% a step
  scaled by machine damage, and the only result is one shallow `SlashWound` on the part being worked on.
- **Disks gate the advanced library, in data.** `autodocProgram` prototypes list surgery ids;
  `WolfmedAutodocProgramBase` ships on the machine and Transplant / Limb / Neuro / Cavity each need their
  disk in the slot. `autodocProcedure` prototypes (id = the surgery id) override the default 15u
  anaesthetic / 10u antibiotic per procedure.
- **One reagent list, not a flag on every reagent.** `autodocReagents` `WolfmedAutodocReagents` names every
  administrable reagent and its role. Anything else in a beaker stays in the beaker and the pod says so.
- **Downed can crawl in.** `WolfmedDownedReachableComponent` is a named exception to CONSC's "self only"
  interaction rule, carried by the pod. Unconscious is still blocked by the ordinary action blocker.
- **The UI reuses the analyzer.** The server sends the analyzer's own `HealthAnalyzerScannedUserMessage`
  inside the pod's BUI state (`HealthAnalyzerSystem.WolfmedBuildScanMessage`), so the window mounts the
  analyzer's `WolfmedDiagnosticPanel` verbatim and a new `WolfmedBodyDoll` control carrying the analyzer's
  exact 3x doll geometry.
- **One voice line, one file, one transcript.** `autodocVoice` maps 62 events to 74 line ids; each id has one
  ogg, one Fluent transcript and one priority, all written by `Tools/_WF/Wolfmed/gen_autodoc_voice*.py` from
  one table, so the spoken line and the chat line can never disagree. Generated with eSpeak NG. No
  `SoundCollection` is used: a collection would pick a file independently of the transcript.
- **The pod is never two voices at once (AUTODOC2).** Every line carries an `AutodocVoicePriority`. `Urgent`
  stops the playing stream and speaks now, `Info` waits in a queue of at most three, `Step` is dropped
  outright if anything is playing or waiting, and `Chatter` only starts after eight seconds of silence. Step
  announcements are one or two words and a procedure speaks at most one per `AutodocStepFamily`, so a long
  surgery says "INCISION. CLAMPING. SAWING. SUTURING." and not a sentence a step. Line length is read off the
  ogg, so the queue can never outrun the audio, and a tool sound plays at 0.6 gain while a line is running.
- **The pod is a bed, not a box (AUTODOC2).** Two sprite layers: the open pod as the base, the lid over it.
  With the lid open the sprite sits at `BelowMobs` and the occupant (`showEnts: true`, laid down through the
  standing-state appearance key) is drawn on top; closed, it goes to `OverMobs` and the lid covers them. The
  dim unpowered colour is applied and cleared by `AutodocVisualizerSystem`: a `GenericVisualizer` could only
  ever set a layer colour, never put it back, so one unpowered tick at map init dimmed the pod for good.
- **Emag is a threat, not a tool.** The lid locks, the lines switch to the sinister set and the pod queues an
  amputation of a random limb and runs it, repeating until the power goes or somebody pries the lid.
- **No revival.** `AutodocDefibModuleComponent` is recognised and reported in the window and does nothing;
  that is BRAIN's.

## Brain death (2026-09-22)

LIFE, the second meter CONSC left a hole for. On a wound host the heart either beats or it does not, and the
brain either has oxygen or it is running out of it. **There is no permanent unrevivable state from Wolfmed.**
Brain death IS `MobState.Dead` - the ghost, the corpse, the "YOU DIED" screen - and a dead body stays
revivable if somebody does the work. `RottingSystem.IsRotten` is still the only hard stop and is untouched.

- **Cardiac arrest** (`WolfmedCardiacArrestComponent`, networked, on the body). The body is Critical through
  `SetExternalPressure(body, "arrest", 1)`, examine and the analyzer say "no pulse", the crit heartbeat loop
  goes silent after one flat tone (`/Audio/_WF/Wolfmed/flatline.ogg`), the G2 spurts wait instead of firing
  and passive bleeding runs at `WolfmedLifeSystem.ArrestBleedFactor` (0.25) applied to the body's cached
  stream rates. Triggers: heart destroyed or removed; blood <= `wolfmed.arrest_blood` (0.30); brain
  oxygenation <= `wolfmed.arrest_oxygenation` (0.15); a pain shock while blood <= `wolfmed.arrest_shock_blood`
  (0.5); late sepsis at `wolfmed.arrest_sepsis` (80) with `wolfmed.arrest_sepsis_chance` (0.01/s); an
  electrocution of `wolfmed.arrest_shock_damage` (60) or more. Ends: a defibrillator, a rejuvenate, or - only
  when the heart was the cause - the heart back in the chest with blood above the arrest level. CPR never
  ends it. *[M6 correction: the pain-shock arrest is gone (`wolfmed.arrest_shock_blood` 0, M1a), the sepsis roll is gone
  (`wolfmed.arrest_sepsis_chance` 0: sepsis drains the brain and stops the heart through the oxygen trigger, M2), and
  the electrocution reads the shock after insulation (M2). Cold, toxins and heat stroke stop the heart too (M5).]*
- **The clock** (`WolfmedBrainComponent` on the brain organ, networked, `Oxygenation` 1 to 0). Drains by the
  worst of: heart stopped (full drain in `wolfmed.brain_arrest_seconds` 120 s), not breathing (airloss over
  the old crit threshold, or a sedation overdose, full drain in 180 s at 1.0), blood under 0.5 (linear to a
  full drain in 300 s at 0.3), late sepsis (600 s). Refills at the arrest rate x `wolfmed.brain_refill_factor`
  (0.5) when nothing drains. Multipliers: cold (`WolfmedBrainComponent.ColdSteps`, x0.5 under 30 C and x0.1
  under 20 C, scaled by `wolfmed.brain_cold_factor`), CPR (x0.25, and CPR answers for breathing and counts
  blood as at least 0.5), stimulants (x0.6). Under `wolfmed.brain_damage_oxygenation` (0.4) the brain ORGAN
  takes `wolfmed.brain_damage_rate` (0.1/s at zero oxygenation, linear from the threshold) of irreversible
  organ damage, and organ health 0 is death on the existing `OrganHealthSystem` path.
  *[M6 correction: "not breathing" read the whole Airloss group, Bloodloss included (P7), not airloss and sedation.
  M1a replaced it with real suffocation (the respirator's own reading) and sedation's depression; M3 added damaged
  lungs, M5 the toxic coma and heat stroke drains. The refill runs whenever no drain does.]*
- **Real timings, which are not the spec's own arithmetic.** With the rule as written (damage under 0.4
  oxygenation, 0.1/s at zero) an untreated arrest reaches 0.4 at 72 s, zero at 120 s, and brain death at
  about 246 s, not the 198 s the spec's summary line quoted. Under CPR the brain never reaches the damage
  band inside ten minutes. The rule was implemented; the illustrative figure was not.
  *[M6 correction: under CPR it does. The death rundown derives the damage band at about 289 s and catastrophic brain
  injury at about 534 s with CPR throughout.]*
- **Brain missing is event driven, never a poll.** `OrganRemovedFromBodyEvent` on `WolfmedOrganComponent`
  and `WolfmedPartAmputatedEvent`, and the amputation handler asks whether the part that came off was
  carrying the brain. A wound host that never had a brain (every brainless test fixture, including the one
  `PassiveDamageMechanismStillRoutesIfReenabledTest` uses) lives exactly as long as anything else.
- **Hypoxia is a pressure of its own.** CONSC's `"airloss"` stand-in is gone; the clock writes
  `"hypoxia"`, ramping from `wolfmed.brain_pressure_start` (0.75) to `wolfmed.brain_pressure_out` (0.45).
  Sedation's Asphyxiation stand-in is gone too: `WolfmedPainReliefSystem.GetRespiratoryDepression` is now
  both a `"sedation"` pressure and a breathing input to the clock.
- **Defibrillation** replaces the damage-threshold gate in `DefibrillatorSystem.Zap` with one marked block:
  brain present, blood > `wolfmed.defib_blood` (0.40), brain organ health > 0, then a roll of
  `wolfmed.defib_chance` (0.85) x lerp(`wolfmed.defib_oxygenation_floor` 0.15, 1, oxygenation). Success
  clears the arrest, leaves 0.35 oxygenation and hands the state back to consciousness; a body that had been
  Dead always comes back Critical, because it comes back on what the paddles put into it. A failed shock
  costs the zap damage and can be tried again. *[M6 correction: the gate is `wolfmed.defib_blood` 0.25 and a shock
  leaves `wolfmed.post_shock_oxygenation` 0.5 with a 45 s grace, since M1a (OD6); the refusals are shared with the pod
  in `WolfmedRevivalSystem.GetRefusal`.]*
- **Brain repair surgery** `SurgeryRepairBrain` (head incision, saw, `SurgeryStepRepairBrain` with a
  tending tool, seal) is the only thing that raises organ health, matching D7. It restores the organ to
  maximum and leaves `WolfmedBrainTraumaComponent` for `wolfmed.brain_trauma_minutes` (30), which feeds W3's
  concussion effects. It is gated on a DESTROYED organ (`destroyed: true` on the organ condition), so it
  never overlaps `SurgeryHealBrain`, which still handles a damaged but living brain.
- **CPR** marks the chest (`WolfmedCprComponent`) for the do-after's length and nothing else. One marked
  condition in `CPRSystem` skips the damage-threshold resuscitation on a wound host.
- **The autodoc's defib module works.** With `AutodocDefibModuleComponent` in the module slot the pod runs
  the same rule on an arrested occupant before the first procedure of a queue and again when the queue ends,
  with three new eSpeak NG lines (`defib-charge` "CLEAR.", `defib-success`, `defib-failure`).
  *[M6 correction: it also charges again every 5 s after a failed shock, up to `wolfmed.autodoc_defib_attempts` (5),
  as AUTODOC5 records.]*
- **Mechanical bodies have no clock.** `WolfmedShutdownComponent` (pressure `"shutdown"`) when the cell is
  pulled or flat (`SiliconChargeDeathEvent`) or the micro pump is destroyed or removed. "Mechanical" is a
  wound host that carries `SiliconComponent` AND runs no clock, not merely a body with no brain: the second
  half alone is the brainless-poll bug in a different costume and it shut down every brainless fixture. That is not death and
  nothing runs out. `PositronicBrain` and `OrganIPCPump` gained `WolfmedOrgan` + `OrganDamage`, so a
  destroyed positronic brain is death on the same organ path a fleshy brain uses, and the way back is the
  same: repair the brain, then a jolt. *[M6 correction: no surgery reached a positronic brain until M2. The way back is
  core repair (`SurgeryRepairCore` on an IPC's chassis, `SurgeryRepairSynthCore` on a synth's head), then the restart
  button, not a defibrillator. Since M6 the pod runs both with the neuro disk.]*
- **Shitmed's delayed death is gated off on a wound host** (one marked `continue` in
  `DelayedDeathSystem.Update`): a missing heart is arrest, not a countdown. Its defib refusal for a body
  with no heart or brain is kept.
- Wolfmed still never adds `UnrevivableComponent`. Other content that sets it is still honoured.

## Arrest looks dead, autofix, pod alarms (AUTODOC3, 2026-09-22)

### Cardiac arrest is a living body that looks dead

The owner's complaint was that shooting somebody and watching them gasp is not satisfying. The first cut of
this was to make arrest `MobState.Dead`; that was withdrawn, because metabolism, objectives, rot and every
other rule that reads a living body have to keep treating an arrested patient as alive. **Arrest stays
`MobState.Critical` exactly as BRAIN built it.** What changed is only what a person can perceive:

- **No gasping.** One marked condition in `RespiratorSystem.Update` skips the gasp emote while
  `WolfmedCardiacArrestComponent` is present. The body still suffocates and still takes the damage; the gasp
  was the only part of it anybody could see or hear. Every other emote and vocalisation is already blocked
  by `MobStateSystem`'s crit check, which the gasp bypassed with `ignoreActionBlocker: true`.
- **Examine reads as a corpse.** `WolfmedVisualInspectionSystem` adds `wolfmed-look-appears-dead-*` for an
  arrested body at any range, and the "is not breathing" note, which used to need the details range, now
  shows at a distance too. "Has no pulse" still wants a hand on the neck.
- **The medical HUD tells the truth.** `HealthIconWolfmedArrest` (a flatline drawn from the stock Critical
  icon, `_WF/Wolfmed/Interface/health_icons.rsi`) replaces the crit icon on an arrested body, through one
  marked hook in `ShowHealthIconsSystem.DecideHealthIcons` whose body is a `_WF` partial. The analyzer's
  CARDIAC ARREST line is unchanged.
- Death screen, banners, the crit heartbeat and the flatline tone are BRAIN's and are untouched.

### Autofix module and triage

- `AutodocTriagePrototype` (`autodocTriage`) is an ordered list of steps, each naming surgeries, categories,
  or the cardiac module. `WolfmedAutodocTriage` is the shipped order: defibrillate, arterial and internal
  bleeding, bleeding, evisceration, organ repair in place, bones, deep wounds, shallow wounds, mechanical
  repair, limbs and organs the tray already holds, and finally closing whatever somebody else left open.
- `AutodocSystem.Triage.cs` walks it against `GetAvailable`, so the planner can only ever queue a procedure
  a human could have queued from the same window. It drops anything the disks do not unlock and anything
  whose requirements want an item the delivery tray is not already holding.
- `requiresStarted: true` on a step queues it only when the body is already past the procedure's own
  requirements. Without it the last step would have queued a close on every intact limb, because closing an
  incision is a valid surgery on anybody: the pod would simply open one first.
- **PLAN** (base pod, no module) writes that queue and says "I HAVE A PLAN."; the operator still presses
  START. In self-service the two are one **FIX ME** button. **AUTO** needs `AutodocAutofixModuleComponent`
  in its own slot and plans and starts on its own every `autoPlanInterval` seconds while somebody is in the
  pod, re-planning when a queue ends and saying "NOTHING MORE I CAN DO." once when the plan comes back
  empty. An emagged pod ignores AUTO and keeps its own plans. *[Playtest 3 SAM correction: an AUTO run re-plans the
  moment its queue drains, without a word, and says AUTO ENGAGED and QUEUE COMPLETE once each; AUTO plans the whole
  triage in self-service too. See "Playtest 3, S.A.M. round".]*

### Vital alarm

`AutodocAlarm` reads the occupant once a tick: Flatline (brain gone) beats Arrest beats Critical beats Dying
(the oxygenation clock draining). Arrest beeps every 2 s, everything else every 4 s, with
`/Audio/Machines/quickbeep.ogg`; a flatline is one `flatline.ogg` and then silence, because there is nothing
left to call anybody for. The first beep of each escalation carries a line ("PATIENT IS DYING." for the
clock, the existing critical line otherwise). `wolfmed.autodoc_alarm` turns the whole thing off.

### Lid and eject

The open art and the closed art are both whole pod sprites, so drawing them together showed the bed through
the lid: the visualizer now draws exactly one layer per state, and the body container's `showEnts` follows
the lid, server side, with the client re-occluding the occupant itself because the engine only recomputes
container occlusion on a parent change. An eject only says "EMERGENCY EJECT" while something is running;
from Idle or Complete it is "GOODBYE." and no abort.

### No procedure may loop (AUTODOC4)

The owner's pod repeated "Repair damaged tissue" for ever on a torso with a lodged round in it. Four things
were wrong, and all four are fixed:

- **The two hand-only treatments are surgeries now.** `SurgeryRemoveEmbeddedObjects` (open incision ->
  `SurgeryStepExtractEmbedded` -> seal) and `SurgeryRelocateJoint` (one toolless step) call
  `WolfmedEmbeddedRemovalSystem.TryRemoveOne` and `WolfmedDislocationSystem.TryRelocate`, the very code the
  hand do-afters call, so the pod leaves what a medic's hemostat would leave and the verbs are untouched.
  Both are base-library programs; the extraction lists only while the part still holds something and the
  relocation only while the limb carries a `WolfmedDislocationWound`.
- **The tend check read the whole body (HOOK 26).** Shitmed's `OnTendWoundsCheck` cancels while the BODY has
  damage of the group, so tending a torso could never finish while a hand had a scratch. On a wound host the
  check reads the part, and reads its wounds rather than its damage figure. HOOK 24 got the same treatment on
  the listing side, which is what stopped the planner queueing a tend on every limb.
- **A tend pass is worth the incision (HOOK 27).** The step's damage removal reached a wound through the
  routing's `HealingMultiplier`, a tenth of what came off; tending now treats the part's matching wounds
  directly at `wolfmed.surgery_tend_strength` (15) a pass, the way a suture does, so a moderate cut closes in
  two or three passes. Wounds nothing closes and wounds refusing treatment are untouched, because
  `TreatWound` raises the same attempt event a dressing does.
- **The stall guard.** A step whose completion check still fails and whose part looks exactly as it did after
  the previous run made no progress; `wolfmed.autodoc_step_retries` (3) of those and the pod says
  "THIS IS NOT WORKING.", drops the procedure, records it in `FailedProcedures` for this occupant, closes the
  patient if it was the pod that opened them, and moves on. The signature deliberately leaves out pain and
  bleed rates, which move on their own every tick.
- **The planner.** It never queues anything in `FailedProcedures`, it puts `SurgeryRemoveEmbeddedObjects`
  first after the defib and `SurgeryRelocateJoint` before the bones, and it queues nothing else on a part
  that still has something lodged in it. AUTO re-plans at most `AutoReplanLimit` (2) more times against an
  unchanged body and then idles with "NOTHING MORE I CAN DO."

### What else the playtest found (AUTODOC4)

- **One anaesthetic per queue, not per procedure.** A twenty-item plan used to push 15u of opiate twenty
  times, walking sedation to 100% and stopping the patient's breathing. The pod doses once for the run, tops
  up only when the painkiller has under `AnaestheticTopUp` (20 s) left, never past
  `wolfmed.autodoc_sedation_cap` (0.5, against a 0.6 depression threshold) and says "SEDATION AT LIMIT."
  instead. While the anaesthetic is in them the occupant is held under with `ForcedSleepingComponent`, which
  is what silences Shitmed's surgery scream, and is woken when the queue ends, aborts or they leave. Past the
  threshold the pod pushes 5u of a dexalin-class chem if the reservoir holds one.
- **AUTO stopped eating hand-written queues.** `TryPlan` no longer clears the queue when the plan is empty,
  and the module runs a queue somebody typed instead of replacing it every few seconds.
- **The reservoir takes bottles and jugs.** The three slots whitelisted `FitsInDispenser` only, and the code
  read only that solution; both now fall back to `DrainableSolution`.
- **A clothed patient no longer faults the pod.** `StepInvalidReason.Armor` is answered with
  "REMOVE YOUR CLOTHING." and a retry every tick, not with `Fault`.
- **A damaged brain has a procedure.** `SurgeryRepairBrain` was gated on a destroyed brain, so a patient at
  three per cent brain tissue had nothing listed at all; the condition takes `anyDamage: true` now, and the
  analyzer calls anything under 60% brain damage and anything under 25% critical.

## Pod blood, clothing, residual damage, UI fit (AUTODOC5, 2026-09-22)

- **Downed is a transition, not a state that can flicker.** Consciousness adds and removes
  `WolfmedDownedComponent` as its inputs cross the threshold, and the component downed and stood the body on
  every add and remove: on the edge, where a stun leaves you, that was a body-fall sound several times a
  second. Downed now only downs a body that is standing with no knockdown on it, only stands one that is
  down with no knockdown left, and `WolfmedConsciousnessComponent.DownedUntil` keeps a body on the floor for
  at least two seconds whatever the inputs do. Hysteresis is a band and the inputs cross it; a dwell is not.
  The dwell needs `WasUp`: a body still being assembled has no legs yet, which reads as both legs gone, and
  holding one down for two seconds before it ever stood up broke every test that spawns a mob and uses it in
  the same breath.
- **Going down drops what you were holding**, the way a knockdown does. Run from `WolfmedDownedSystem`'s
  Update on the first tick of being down, not from the component's startup: startup runs inside the
  consciousness evaluation, where a hand's container will not give its item up. Picking things back up while
  Downed is still allowed, so the CONSC rule "self only" is unchanged. *[M6 correction: it was not; the interaction
  block refused the floor (P16). Since M1a a Downed body picks up loose items within `wolfmed.downed_reach` (1.5 m).]*
- **A wound does not own the damage it was made from.** The part's `DamageableComponent` carries it and the
  body totals every part, so surgery that closes a wound directly (HOOK 27's tend, embedded removal) left
  ghost brute nothing but a brute pack could clear. `WolfmedWoundDamageSyncSystem` lowers each wound-backed
  damage type on a part to what its remaining wounds account for, and never raises one, so damage nothing
  models is untouched and no path can invent healing.
- **The pod cuts clothing rather than waiting on it for ever.** AUTODOC4 had it ask and retry, which is a
  deadlock when there is nobody to ask. A conscious patient is told to undress or press CUT; one who cannot
  is cut for by AUTO after five seconds. Only the outer layer and the jumpsuit, which are the slots the
  surgery access rules read - the pod has no business with an ID or a backpack. *[Playtest 3 SAM correction: the
  rules also read the head slot for the head, the gloves for a hand and the shoes for a foot. The pod still cuts only
  the suit and the jumpsuit and takes those off whole; see the slot table in "Playtest 3, S.A.M. round".]*
- **A BoxContainer that runs out of room clamps its last children to zero.** That is why the window's
  headless test passed while the owner's bottom row was missing: nothing can hang below the window, it just
  stops existing. The controls row moved onto the window's own column so the layout serves it before the
  body, and the test now measures the row's height at two UI scales instead of asking about overflow.
- **A corpse gets the flat defibrillator chance once it has been repaired.** The oxygenation scaling is what
  makes speed matter on an arrested body still on the clock; a dead body has no circulation and therefore no
  way to raise that number, so the medic who repaired the brain and put the blood back was being told "no
  response" at 13% a shock for a reason nothing on the body showed. CPR on a corpse now also refills
  oxygenation slowly, because chest compressions circulate.
- **"Charging again" has to be true.** The pod retries a failed shock every five seconds up to
  `wolfmed.autodoc_defib_attempts`, then says it cannot restart the heart. A gate refusal (no blood, brain
  damage) never charges at all: it says which gate, once, and again only when the reason changes.
- **Asphyxiation is capped at `wolfmed.airloss_cap` (200); Bloodloss is not.** The body damage cap only ever
  saw part damage, so suffocation counted past 700 on a body that cannot die of the number. Bloodloss is left
  alone because decapitation and the other vital losses deal a fixed lethal figure through it. BRAIN's hypoxia clock carries the
  lethality; the reading stops at the old death line, which is also what stops a blood pack being spent on
  bloodloss damage that could never come down. *[M6 correction: that figure no longer kills anything. Losing a vital
  part is death through the amputation handler (the brain's part, or the last head), and Bloodloss damage decides
  nothing on a wound host; blood volume is the route.]*
- **One close chain serves every part, so its bone step is named for no part.** "Mend ribcage" read wrong on
  a head. Renamed "Mend bone" rather than split into a per-part chain: `SurgeryCloseIncision` is named as a
  requirement by a dozen surgeries and splitting it is a change of its own shape.

## Synthetic HUD (2026-09-22)

A mechanical body gets its own damage presentation. The organic red vignette and the dying view are gated
off for it (`WolfmedSyntheticHudOverlaySystem.OwnsView`, checked in `DamageOverlay` and in four places in
`WolfmedDyingEffectsSystem`); organic bodies and non-wound-host silicons are untouched.

- **A body is mechanical when its torso is.** `WolfmedSyntheticHudSystem.IsMechanicalBody` asks W6's
  `WolfmedWoundTraitSystem.IsMechanical` about the torso only, so a human with one cybernetic arm is still
  flesh and still gets the vignette.
- **Pushed, not pulled.** Wound entities are server-only, so the server fills a networked
  `WolfmedSyntheticHudComponent` (faults, integrity, fluid, power, servos, shutdown, core, advice) twice a
  second and dirties it only when something moved. Twelve lines maximum.
- **Every line is data.** `syntheticHudLine` prototypes map a wound id or a condition to a locale line, a
  tag, an escalation severity and a one-line advice; an unmapped wound whose `analyzerCategory` is
  `Mechanical` hits the fallback row. Nothing in the overlay hardcodes a fault string.
- **Newest first.** A fault the readout did not carry last tick goes to the top, worst tag leading;
  everything already on screen keeps its order. The advice line is always the top fault's.
- **Four tiers off one number.** `Strain` is the worse of CONSC's `Depth` and lost chassis integrity;
  0.08 / 0.25 / 0.55 separate Idle (a corner glyph; *[playtest 3 IPC: removed, it read as four stray dots]*), Light (SYSTEM block), Moderate (DIAGNOSTICS and a
  still cyan rim) and Heavy (pulsing rim, one-pixel jitter, an occasional torn slice, and the integrity
  banner). Downed, shutdown/unconscious and dead override the banner with crawl mode, standby and a kernel
  panic into `CORE OFFLINE`.
- **Integrity is the parts, not the damage total.** Each part is read against its own amputation
  thresholds, torso and head weighted double, so losing an arm costs less than a cracked chassis.
- **It can never take a click.** The readout is a plain screen-space `Overlay` with no controls at all, and
  `WolfmedSyntheticHudLayout` keeps its three blocks in the top 32 % of the screen, clear of the hotbar,
  alerts column, chat pane and targeting doll at 1920x1080 and 1280x720.
- **Reduced motion and two CVars.** `accessibility.reduced_motion` drops the slide-in, the jitter and the
  glitch and leaves the tint; `wolfmed.synthetic_hud` puts a chassis back on the organic presentation and
  `wolfmed.synthetic_hud_scale` sizes the text.

## Playtest fixes: IPC decapitation, readout placement (2026-09-22)

- **A vital part taken off is death.** `WolfmedLifeSystem` killed a body only when the severed part
  carried a brain. An IPC keeps its positronic brain in the torso, so a decapitated chassis walked on.
  The prototype already calls a head vital (`BodyPartComponent.IsVital`, which upstream only ever turned
  into bloodloss damage an inorganic damage container does not carry), so the amputation handler now also
  kills a body that has lost its last part of a vital type. Taking the positronic brain out by surgery was
  already death on the organ path and now has a test.
- **The readout drew a viewport away from its corners.** `OverlayDrawArgs.ViewportBounds` is the viewport
  control's draw box in global physical pixels, while a screen-space overlay's handle is already
  translated to that control's top-left. `WolfmedSyntheticHudLayout.Screen` takes the global origin back
  off and `.Scale` multiplies the player's text setting by the control's UI scale, because those pixels
  are physical ones. `WolfmedDeathBannerOverlay` read the same bounds the same wrong way and is fixed with
  it. The fault list is now also cut to `MaxLines`, so a large text scale shortens the list instead of
  running it under the game HUD.
- **STANDBY over a walking machine.** Two causes, both fixed. `MobIPC` had no `Critical` state, so CONSC's
  Unconscious could never reach the mob state: pain or a pulled pump left the chassis on its feet with the
  readout saying STANDBY. Critical is back in `allowedStates` (marked); `MobThresholds` still names no
  Critical rung, so damage totals cannot put it there. And `WolfmedShutdownSystem.Refresh` skipped writing
  the pressure whenever the flag already agreed, so anything that clears every pressure (a rejuvenate)
  left the flag behind: it now writes both halves every time and re-reads the cause once a second for
  bodies that are already down.

## Playtest fixes: defib on arrest, autofix stops (2026-09-22)

Four owner findings from the Wolfmed playtest.

- **Nothing would shock an arrested patient.** `wolfmed.defib_blood` (0.40) sat above every arrest trigger
  that involves blood (arrest at 0.30, a pain shock under 0.50), so the patients who arrest in practice were
  all refused at the gate. The gate is now strictly under the threshold, the refusal is popped up to the
  medic as well as spoken, and the pod transfuses out of its reservoir before it charges and again whenever
  blood is what is blocking it. A plain arrest with blood in the body was always shockable; nobody had one.
- **AUTO looped.** Surgery leaves an incision, a suture and a cautery burn behind it, so the body always
  looked different and always had "work" on it. Wounds that appear while the pod operates carry
  `WolfmedPodWoundComponent`; the body signature ignores them, and a triage step marked `ignorePodWounds`
  skips a part whose every wound the pod made. QUEUE COMPLETE is spoken once per run.
- **The pod held for a patient it was asked to operate on because they were dead.** Repairing a brain means
  operating on a corpse. The vitals hold now only fires for a death that happens mid-run, says
  "PATIENT IS DEAD. PROCEEDING." once for one that was already dead, and marks the death as handled so the
  operator's RESUME carries on instead of stopping again on the same body.
- **The queue reorder buttons did nothing.** The window worked out what could move from the row it was
  drawing rather than from the pod's state, so the buttons around the running procedure were live and the
  server threw away every message they sent. `AutodocQueueRules.FirstMovable` is the one rule both ends use.

## M1a B: causes, faint and alerts (2026-09-23)

Plan: `WOLFMED_DEATH_PLAN.md` §5.1, §5.2 (without the explanation card and the sedation warnings), §3.1, §2.3,
§3.11 (M1a parts), §5.6.

**What was built.**
- **Cause and Blockers.** `WolfmedConsciousnessComponent` carries networked `Cause` (`WolfmedCause`),
  `CauseSource` (`WolfmedCauseSource`: the hypoxia drain, the arrest trigger, the shutdown reason) and
  `Blockers` (`WolfmedCauseFlags`, one bit per cause). `Evaluate` keeps every input under its cause (pain,
  pain faint, blood or oil, one per pressure key, legs, crash) and picks the cause with the plan's tie order
  (Arrest > Shutdown > Blood > Oil > Hypoxia > Sedation > Other > PainFaint > Pain > Legs > Crash). Critical
  states read the Unconscious line, Downed the Downed line. `Apply` raises `WolfmedConsciousnessChangedEvent`
  on any change of state, cause or blockers.
- **How a cause is picked, precisely.** An input past its line (level ≥ 1) names the state before one that is
  only inside its leave band (≥ 0.9). Everything at or past its leave line, other than the cause, is a
  blocker. So a faint at 40% blood is Cause PainFaint, Blockers Blood (blood is in its band); at 34% blood the
  cause becomes Blood and the faint is the blocker. The plan's `OverlappingCausesTest` reads this way.
- **Pain faint** (plan §3.1). Summed part pain at or over the faint line (1.4 × 135 = 189) faints for
  `wolfmed.pain_faint_seconds` 20; nothing extends it. Waking records the summed pain as the baseline, starts
  `wolfmed.pain_faint_cooldown` 30 and disarms. It re-arms under the leave line (170.1) or on a rise of
  `wolfmed.pain_faint_rise` 40 over the baseline, never inside the cooldown. A Strong or Emergency dose ends a
  faint and blocks the next (`WolfmedPainReliefSystem.EndsFaint`, which reads the doses, so a stimulant on top
  of an opiate still counts). Mechanical bodies never faint. Pain no longer holds anybody Unconscious; only the
  faint does, and it is `MobState.Critical` and breathing (package A).
- **One pain number (P13).** Marked `PainSystem.SetPain` edit: a body's pain is min(soft cap, Σ parts) after
  every change, and a direct set on the body is rederived from the parts. Suppression shares are part ÷ Σ
  parts, so the body's suppression comes off once. The Downed test still reads the capped body value less
  relief.
- **Pain shock and adrenaline (OD5).** The shock's constants are CVars (`wolfmed.pain_shock_threshold` 130,
  `wolfmed.pain_shock_rearm` 110, `wolfmed.adrenaline_seconds` 30, `wolfmed.adrenaline_crawl_multiplier` 1.5).
  `GetPain` no longer multiplies by 0.7, so adrenaline stands nobody up; while it runs a Downed body crawls
  ×1.5 and loses the 1.5× do-after penalty (`WolfmedDownedSystem`). Start and end are told to the patient
  through the broadcast `WolfmedAdrenalineEvent` raised by `WolfmedBodyPainSystem`, the `_WF` half of the edit.
- **Alerts.** `MobThresholdSystem.SetTriggersAlerts` (marked, beside `SetAllowRevives`) turns the stock health
  alerts off on a wound host at startup when `wolfmed.consciousness` is on. `WolfmedConditionAlertSystem` then
  owns the Health category: the stock Alive alert (`HumanHealth`/`BorgHealth`) while Up with its severity from
  the worst Downed-level input, a per-cause alert while Downed or Critical, and the stock Dead alert. Bodies
  that are not wound hosts are untouched. `HumanCrit` and `HumanHealth` readers were grepped: the thresholds'
  own dictionary and `PainNumbnessSystem` only.
- **Pain numbness decision.** The Up doll does not raise `BeforeAlertSeverityCheckEvent`. Its one reader,
  pain numbness, pins the doll at full health, which would hide blood loss too; numbness already zeroes pain
  in the inputs the severity reads.
- **Lines.** One popup plus a chat line (Notifications channel) per transition: down, out (faint, arrest and
  shutdown included), waking, standing, the heart restarting ("… You are still held down by {cause}: {help}"),
  adrenaline start and end. Every line that names a cause and a state adds "Still holding you down: …" while
  Blockers is not empty. The last line is kept in `LastConditionLine` for tests and admins.
- **Cause prototypes.** `wolfmedConsciousnessCause`, one per M1a cause in
  `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml`, id = enum name; text in `consciousness.ftl`.
- **IPC.** `WolfmedShutdownComponent.Reason` (Power or Pump, networked) is set in `WolfmedShutdownSystem.Refresh`;
  an empty cell wins when both are missing. The blood input on a mechanical body is cause Oil. The synthetic
  HUD's banner is the cause's `syntheticHudLine` (`WolfmedSyntheticHudComponent.CauseLine`) instead of the
  blanket STANDBY: "CELL EMPTY: SHUTDOWN. AWAITING POWER.", "COOLANT PUMP OFFLINE: SHUTDOWN",
  "HYDRAULIC PRESSURE LOW", "MOBILITY LOST: FRAME DAMAGE", "MOBILITY LOST: ACTUATORS OFFLINE". The crit
  heartbeat is silent for any body with the synthetic readout and for a faint.

**Differs from the plan, and why.**
- The plan calls `PainFaintUntil` and `PainFaintArmedBelow` existing fields; neither existed. All four faint
  fields are new: `PainFaintUntil`, `PainFaintArmed` (a flag rather than a stored line), `PainFaintBaseline`,
  `PainFaintCooldownUntil`.
- Hypoxia sub-sources are Airway, Lungs, Circulation, Sepsis and Sedation: the drains that exist in M1a
  (blood below 50% and sedation depression are drains too). Toxin and Heat arrive with M5.
- `WolfmedCause.Other` names a pressure key no cause claims (admin and test keys); stock alerts, generic lines.
- Alert tooltips are static in the engine, so they cannot change with the blockers. Every alert is written
  conditionally ("… unless something else is holding you down") and clicking one (`WolfmedConditionAlertEvent`)
  pops up and prints the full text with the blockers; the transition lines carry the blockers too.
- The prototype has Downed and Critical forms of the help (`help`/`helpBlocked`, `helpOut`/`helpOutBlocked`),
  titles, and a mechanical Downed alert and help for pain (`WolfmedDownedFrame`: painkillers do nothing for a
  chassis). The plan's field list had one `help`.
- Sedation plus hypoxia: with air back and the overdose still in the body, the cause stays Hypoxia with source
  Sedation, not Sedation, because depression is its own brain drain (plan §3.4) and keeps the brain under the
  hypoxic line. The text names the overdose ("no oxygen (breathing slowed)", blocker "overdose"). When the dose
  ends the patient wakes as sedation falls and the brain refills: 59 s in the test.
- The arrest help does not yet say "You can choose to let go": Succumb is package C's.
- The heartbeat gate reads the synthetic HUD component rather than `OwnsView`, which is false when the player
  turned the readout off.

**Numbers.** Faint 20 s, rise 40, cooldown 30; shock 130 / re-arm 110; adrenaline 30 s, crawl ×1.5; the faint
line stays `wolfmed.consc_pain_out` 1.4. Measured: `SustainedFireFaintTest` (10-stack fire, Blunt 6 every 2 s,
2 min) spent 18.5 s Critical in one faint, Downed for the rest, so the 30 s cooldown holds the 40 s budget
without the 50 s fallback. IPC 10-stack fire (M4 input, `IpcShutdownScenarioTest` output): chassis peak 1112 K
at about 50 s, about 155 s above 383 K and about 125 s above 500 K.

**Art debt.** Every new alert reuses an icon: `downed.rsi` for the Downed ones, the stock critical, bleed,
breathing, dead and borg-critical icons for the rest.

**Test fixtures that changed with the faint.** Pain past 189 now faints where the pain shock's 0.7 adrenaline
discount used to keep it under: `WolfmedAutodocLoopTest.LongQueueDosesOnceAndWakesThePatientTest` (two broken legs)
is made pain-numb, and package A's `PainShockNoArrestTest` hits the head with 40 instead of 60 so the shock fires on
a conscious body (a fainted, Critical body takes no paralysis).

**Package A's state.** Package A stopped before committing; its work was committed unchanged as a `wip:` checkpoint
before B started, and B builds on it. A has no report or DECISIONS section of its own.

**Left for later packages.** The analyzer's "FAINTED: pain" and "SHUTDOWN: no power" lines (D; the scenario
tests assert the patient's titles instead). Succumb in the scenario tests (C). The synthetic HUD's pain/sensor
and core-temperature rows (§5.6) were not added.

## M1a C: honest endings and crawling (2026-09-23)

Plan: `WOLFMED_DEATH_PLAN.md` §5.4, §5.3, §2.2, and the autodoc rows of §7.2.

**What was built.**
- **Crit actions stripped** (§5.4 item 1). The new shared `WolfmedCritActionsSystem` removes the `MobState.Critical` entry
  from `MobStateActionsComponent.Actions` at `ComponentStartup` on wound hosts while `wolfmed.consciousness` is on,
  on both client and server. It removes the whole list, so Fake Death goes with it; Play dead is M2 (OD20). The
  `MobStateActionsComponent` + `ComponentStartup` pair was grepped and is free. The list is replaced with a copy
  rather than edited in place.
- **Succumb and Last Words** (§5.4 items 2-4, OD2 (a), OD3). `ActionWolfmedSuccumb` and `ActionWolfmedLastWords`
  are in `Actions/dying.yml` and raise the new `WolfmedSuccumbActionEvent` and `WolfmedLastWordsActionEvent`
  (Last Words' cap is `maxLength: 30` on the event). `WolfmedDyingActionsSystem` grants them from `StartArrest` and
  removes them from `EndArrest` and from consciousness's death handler. All three are direct calls. Both actions
  open the Succumb dialog. Last Words first whispers the capped text with "..." added. On confirm, the brain
  organ's health goes to 0 where it sits, then `Kill`, then `EndArrest` on the corpse, then
  `OnGhostAttempt(canReturnGlobal: true)`. The body is dead by then, so the ghost is returnable. The mind stays
  owned by the body, and brain repair plus a defibrillator brings the same person back.
- **The dialogs.** `WolfmedChoiceEui` (server) and its client window show the exact text with two buttons. Closing
  the window counts as no. Wording is in `death.ftl` ([OD1 wording], OD1 (b)): "Let go?" / "Let go" / "Keep
  fighting", naming the arrest cause and the minutes until the body rots (from `PerishableComponent`; a body
  that does not rot gets the text without the time). The "Leave your body?" dialog reads "Your character will be
  left alive but empty. You cannot return to this body." with "Leave" / "Stay". A dialog still open when the
  heart restarts is withdrawn. A "leave" confirmed after the body has gone into arrest opens Succumb instead.
- **The ghost command** (§5.4 item 5). The marked `GhostSystem.OnGhostAttempt` hook hands a wound host's own
  `ghost` command (`viaCommand && !forced && canReturnGlobal`) to `WolfmedDyingActionsSystem.TryOpenGhostDialog`.
  In arrest that is the Succumb dialog. In any other living state it is the leave dialog, which ghosts with
  `canReturn = false` and does not touch the body. The hook returns true, so the command prints no "denied"
  (the plan's "to confirm": `GhostCommand` prints the denial only on false). A second marked condition keeps a
  wound host out of the kill-crit branch altogether, so the Asphyxiation top-up never applies and a Critical
  wound host never gets upstream's free returnable ghost.
- **Pod return prompt** (§5.4 item 6). `WolfmedRevivalSystem.OfferReturn` opens the stock `ReturnToBodyEui` for
  a ghost whose body the pod has just revived. `AutodocSystem.TryDefibrillateOccupant` calls it on success. The
  hand defibrillator already opened the prompt.
- **Pickup within reach** (§5.3, P16, OD7 (b)). `WolfmedDownedSystem.OnInteractionAttempt` also lets a Downed body
  reach an `ItemComponent` that is not anchored, not in a container, and within `wolfmed.downed_reach` (1.5 m:
  the body's own tile and the neighbouring ones, diagonals included). Guns still cannot fire and throwing is
  still blocked.
- **Call for help** (§5.3). The `ActionWolfmedCallForHelp` action (`Actions/downed.yml`) exists only while
  Downed. `WolfmedCallForHelpSystem.Refresh` is called directly from consciousness's `Apply` and death handler. The
  action opens a one-line dialog; an empty line shouts "Help! I'm down!". The line is capped at `maxLength: 60`
  on the event and said aloud at normal range, where the exclamation makes it a shout. The call sets
  `WolfmedCallForHelpComponent.FlagUntil` for `wolfmed.call_for_help_seconds` 60 and `CooldownUntil` for
  `wolfmed.call_for_help_cooldown` 30. Both fields are networked. The client's `WolfmedCallForHelpIconSystem`
  shows `HealthIconWolfmedCallForHelp` to anyone with a medical HUD while the flag lasts. The flag outlasts
  Downed on purpose, so a caller who goes under is still marked.
- **Autodoc and faints** (§7.2). `WolfmedConsciousnessSystem.InFaint` means Critical with cause PainFaint; the
  head blow joins it in M3. `MaintainAnaesthesia` now anaesthetises a fainted occupant, and `GetAlarm` does not
  raise `AutodocAlarm.Critical` for a faint. Rot, `Unrevivable` and no heart were already in the shared
  `GetRefusal` (package A), so the pod refuses in the hand defibrillator's words; the tests now pin it.
- **Arrest text.** The arrest help and alert now end with "You can choose to let go." Package B had held this
  back until Succumb existed.

**Differs from the plan, and why.**
- **Succumb order.** The plan's order is `EndArrest`, then `Kill`. This build runs `Kill` first and then ends
  the arrest on the corpse. Ending the arrest on a living body re-evaluates it for a moment as having a heartbeat.
  That changes its cause and plays "Your heart lurches back into rhythm" to a patient who is letting go. The end
  state is the one the plan asks for: Dead, brain at 0, no arrest component. The plan's real constraint, that
  `Kill` runs before `OrganHealthSystem`'s next update, still holds.
- **Hook placement.** The plan puts the whole hook inside the kill-crit branch. The dialog is placed earlier
  instead: after upstream's `PreventGhosting` and `CanGhostInteract` checks and before `UnVisit`. From there it
  does not depend on the `ghost.killcrit` CVar and runs before any side effect. The branch itself gets a one-line
  "not a wound host" condition. Upstream's `GhostAttemptHandleEvent` would have needed no hook, but it carries no
  `viaCommand` or `forced`, and cryosleep calls the attempt with `viaCommand: true`, so it cannot tell the
  player's own command apart.
- **The dialog is a new EUI.** `QuickDialog` has no body text and no yes/no. Tests answer through
  `WolfmedDyingActionsSystem.Confirm`, the same method the window's buttons call.
- **"Editable" Call for help** is a one-line dialog each time with a default. The length caps (30 and 60) are
  fields on the action events, not CVars.
- **The HUD flag** is drawn by its own client system. It reads the medical HUD state through a `WolfmedHudActive`
  accessor added to the `_WF` partial of `ShowHealthIconsSystem`. Subclassing `EquipmentHudSystem` would
  duplicate its component subscriptions.

**Found on the way (not changed).**
- **Going Downed stuns briefly.** The fall adds `KnockedDown` and a short `Stunned` for about 2 s, and `Stunned`
  cancels every interaction, including reaching the body's own tile. `DownedPickupTest` waits it out.
- **A succumbed corpse bleeds faster.** It has no arrest component, so it bleeds passively at the full rate. A
  body that died of untreated arrest keeps the component and bleeds at ×0.25. Worth a look in M2, when corpse
  bleeding is revisited.
- **An upstream helper is broken.** `SharedHandsSystem.TrySelectEmptyHand` never selects anything, because it asks
  `IsHolding(null)`, which is always false. The test selects an empty hand itself.

**Numbers.** `wolfmed.downed_reach` 1.5 m, `wolfmed.call_for_help_seconds` 60, `wolfmed.call_for_help_cooldown`
30; Last Words 30 characters, a call 60 characters.

**Art debt.** Call for help reuses the stock critical health icon on the right, and the action uses the scream
icon. Succumb and Last Words use the upstream crit action icons.

**Left for later.** IPC thermal shutdown's Succumb (M4). The Succumb and leave dialogs have no client test that
presses their buttons; the EUI reaching the client is tested, and the answer is tested on the server.

## M1a A: breathing and the clock (2026-09-22; written up by package D)

Package A stopped before it wrote anything down; package B committed its tree unchanged as `8f71617774`. This
section records that work from the code and its comments, so the M1a record is complete. Plan: §4, §3.2, §3.3,
§7.1, §7.2 (M1a rows).

**What was built.**
- **Who breathes.** `WolfmedBreathingSystem` (server, `_WF/Wolfmed/Life`). The one marked hook in
  `RespiratorSystem` asks `BreathingSuppressed` instead of `IsIncapacitated`. A wound host stops breathing only
  when dead or in arrest. A body that is not a wound host, or any body while `wolfmed.consciousness` is off,
  keeps the upstream rule. The `DebrainedComponent` and gasp-in-arrest guards are untouched.
- **The breathing input (§4.3).** `SuffocationLevel` is 0 unless the respirator is suffocating now, and then it
  is Asphyxiation ÷ `wolfmed.airloss_full` (100). "Suffocating" means as many short cycles in a row as the
  respirator's own alert needs (`SuffocationCycleThreshold`), not one: a body getting its breath back dips
  under the line for a single cycle. Bloodloss is never read (P7). The refill runs whenever every live drain is
  0, however much Asphyxiation or Bloodloss is left.
- **Vital signs.** `Assess` gives what the chest is doing and why: None (dead, arrest, no brain, no lungs),
  Gasping (no air), Depressed (sedation past its line) or Normal. `GetBloodBand` gives the circulation band:
  pale at `wolfmed.blood_band_pale` 0.8, weak at the Downed line, barely palpable at the Unconscious line,
  none in arrest or death. Both are networked on `WolfmedConsciousnessComponent` (`Breathing`,
  `BreathingSource`, `BloodBand`); there is no new component. The life tick writes them and dirties them only
  on a change.
- **The post-shock course (§7.1).** A successful shock ends the arrest. On the first shock of an episode,
  oxygenation becomes max(current, `wolfmed.post_shock_oxygenation` 0.5); a body that had died gets exactly
  0.5. `WolfmedPostShockComponent` opens a `wolfmed.post_shock_grace_seconds` (45) grace that holds off the blood
  and oxygen triggers; the drains still run. Another success within `wolfmed.post_shock_repeat_seconds` (300)
  only restarts the heart. The patient comes round on consciousness's own lines, so a patient whose blood was
  not the cause is Downed at once. `RepairBrain` uses the same 0.5.
- **The two transfusion numbers.** `GetTransfusionGuidance`: N = units to `wolfmed.post_shock_blood_target`
  (35%) plus the current bleed rate × the grace; M = units to `wolfmed.brain_blood_start` (50%).
  `GetPostShockAdvice` feeds the analyzer's "Revived: … Transfuse ≈ N u within 45 s …; ≈ M u to 50% …" line
  (`WolfmedPostShockText`, shared).
- **Revival refusals (§7.2).** One `WolfmedRevivalSystem.GetRefusal` for the hand defibrillator and the pod: rot,
  other content's `Unrevivable`, no brain, a destroyed brain, no heart (only for a species whose body prototype
  carries a Wolfmed heart), pulse present, and the blood gate `wolfmed.defib_blood` 0.25 (strictly under).
  `LocalizeLine` puts the patient's numbers into the paddle line ("Shock refused: blood 24% … Transfuse ≈ 33 u
  first; ≈ 78 u to reach 50%"). No line says a shock will work.
- **Data.** `wolfmed.arrest_shock_blood` 0: the pain-shock arrest is gone.
- **P22.** `WoundInternalBleedingSystem` bleeds once a second, so each amount is well above FixedPoint2's 0.01
  step (marked Onyx edit).
- **Tests.** `Scenarios/WolfmedBreathingClockTest.cs`: `BleedingScenarioTest`, `RepeatedShockTest`,
  `PostShockOxygenTest`, `OxygenScenarioTest`, `InternalBleedTickTest`, `PainShockNoArrestTest`,
  `NonWoundHostCriticalStillDoesNotBreatheTest`. The scenario helper `WolfmedScenario` drives them. The
  migrations are in `WolfmedBrainTest`, `WolfmedArrestLooksDeadTest` and `WolfmedPlaytestFixesTest`.

**Differs from the plan, as far as the code shows.**
- The plan says `DefibrillatorSystem.cs` is not edited. A made one small marked edit there: the hand
  defibrillator's refusal is localised with the patient's numbers. The rot and `Unrevivable` checks stayed in
  the hand defibrillator, as the plan says, and are also in `GetRefusal`.
- "Suffocating" needs the respirator's alert threshold of short cycles, not `SuffocationCycles > 0` (above).

## M1a D: medic lines, conformance, closure (2026-09-23)

Plan: §5.5 (the M1a lines and the defib verdict), §1.5, §9.1, §11, §12 M1a.

**What was built.**
- **The analyzer's vitals block.** `WolfmedVitalsReport` (shared, networked) rides on
  `HealthAnalyzerWoundDiagnostics.Vitals`, one marked Onyx field. `WolfmedVitalsText` holds the words, shared so
  the panel and the tests read the same text. `HealthAnalyzerSystem.Vitals.cs` (server, `_WF`) builds it:
  - the state (Up, Downed, Faint, Unconscious, Arrest, Shutdown, Dead), cause, sub-source and blockers from
    consciousness;
  - breathing and the blood band read fresh from `WolfmedBreathingSystem.Assess` and
    `WolfmedLifeSystem.GetBloodBand`, not the networked copy the life tick writes once a second, so the pulse
    words always agree with the blood % on the same line;
  - blood %, its trend, and the units to the brain-safe line (`wolfmed.brain_blood_start`, 50%). The trend is
    net regeneration against every bleed; "falling fast" starts at a net loss of `wolfmed.analyzer_blood_fast`
    (new, 1 u/s);
  - the defib verdict, from the shared `GetRefusal`.
- **What it says.**
  - State: "DOWNED: blood loss", "FAINTED: pain", "UNCONSCIOUS: no oxygen (no air)", "CARDIAC ARREST: blood",
    "SHUTDOWN: no power", "DEAD: catastrophic brain injury" [OD1 wording]. Blockers follow as "(also: …)",
    minus the one an arrest already names.
  - Breathing: "normal", "depressed: sedation 72%", "none: no air, gasping", "none: cardiac arrest", "none: no
    working lungs".
  - Circulation: "pulse weak and rapid; blood 45%, rising; transfuse ≈ 14 u to 50%".
  - Verdict (Faint, Unconscious, arrest and dead only): "Defib: shock indicated", or "Defib: refused: …" with
    pulse present, "no heart, transplant first", "blood 24%, transfuse ≈ 33 u first (≈ 78 u to 50%)", "brain
    destroyed, brain repair surgery first", "no brain", "body decayed", or other content's reason. It never
    says "will work".
  - Machines: "ONLINE", "SHUTDOWN: no power" or "coolant pump offline", "DOWNED: frame damage" or "hydraulic
    pressure low", "CORE FAILURE"; "Cooling: pump running/offline" in place of breathing; "Hydraulics: oil
    100%, steady" in place of circulation; no verdict (the restart button is M2).
- **The panel.** The block is the first banner row of the wound tab. Its text is redrawn on every scan, and
  the row is rebuilt only when the state changes. The pod mounts the same panel.
- **Examine** (`WolfmedVisualInspectionSystem`) reads the networked `Breathing` and `BloodBand`:
  - "is not breathing": arrest, death, no lungs, no brain. It shows at a distance too while arrested, as
    before.
  - "is gasping for air"; "is breathing slowly and shallowly" for sedation past 0.6, which used to read "not
    breathing".
  - "looks pale" (≤ 80% blood), "is pale and clammy, with a weak, rapid pulse" (≤ 50%), "has a barely palpable
    pulse" (≤ 35%), "has no pulse" (dead).
  - These are close-examination findings only, like a hand on the neck. Machines get none of them.
- **Vitals on organ insertion.** Any organ going in now refreshes the vital signs (the heart already ticked the
  body). A body being assembled gets its heart before its lungs, so it read "not breathing: no lungs" for up to
  a second after spawning; the new examine test found it. A lung transplant now reads as breathing at once.
- **Synthetic HUD rows (§5.6, left by B).** SENSOR (the chassis's pain against its soft cap) and CORE (chassis
  temperature in K, the M4 core-heat input) in the SYSTEM block.
- **Species conformance, report mode (§9.1).** `WolfmedSpeciesConformanceTest.KnownGapsAreExactlyTheReportTest`
  spawns every round-start species. Checks:
  - it is a wound host;
  - organics: the brain has `WolfmedBrain` and `WolfmedOrgan`, heart and lungs have `WolfmedOrgan`, 29% blood
    arrests, and removing the brain kills;
  - machines: core and pump have `WolfmedOrgan`, pulling the cell shuts the chassis down, and removing the core
    kills.

  The result is 98 gaps across 32 species, checked in as `KnownGaps` in the test file and grouped as §9.2
  groups them. It matches §9.2's prediction, with two things §9.2 did not say:
  - ProtoThaven's lungs lack data too;
  - every protogen subspecies is group C.

  IPC and the full-data group (Human, Oni, Dwarf, Chitinid, Resomi, Thaven, Vox, Avali) conform.

**Differs from the plan, and why.**
- **The defib verdict ships in M1a.** The brief asks for it; §5.5 had put it in M2.
- **The analyzer reads breathing and the blood band fresh** rather than the networked field (above). Examine,
  which is shared code, reads the networked field as the plan says.
- **Not in M1a, and not built:** AVPU, blue lips, weeping burns, pupils. They are §5.5 signs outside M1a's
  scope list. A strong pulse is not an examine finding: adding it to every healthy body would have replaced
  "You see no injuries".
- **One new CVar** for the trend words: `wolfmed.analyzer_blood_fast`.
- **Test names.** The conformance test is class `WolfmedSpeciesConformanceTest`, method
  `KnownGapsAreExactlyTheReportTest`, because a C# method cannot share its class's name. `AnalyzerStateLinesTest`
  is in `Scenarios/WolfmedMedicLinesTest.cs`. Its hypoxic case lowers oxygenation directly; the airless
  "Breathing: none: no air" is asserted in `OxygenScenarioTest`.
- **The analyzer lines B and A left out are now asserted where the plan put them:** "DOWNED: blood loss" in
  `BleedingScenarioTest`, "UNCONSCIOUS: no oxygen" and "none: no air" in `OxygenScenarioTest`, "FAINTED: pain"
  in `PainScenarioTest`, "SHUTDOWN: no power" in `IpcShutdownScenarioTest`.
- **The self-aid "hold pressure on your own wound" (§3.2, "to confirm")** needs no new verb. It is gauze or a
  bandage on yourself, which a Downed body can already use at 1.5× time.

**Not done in M1a (found in the §11 audit).** The stock `LowOxygen` alert is not replaced on wound hosts by
"Can't breathe: {source}" (§3.3). The per-cause hypoxia alerts ("Short of breath", "Unconscious: no oxygen")
carry the cause instead. Replacing the respirator's own alert needs a marked upstream edit; it goes with M2's
alert work.

**Still red, not caused by M1a.** `WolfmedPlaytestFixesTest.AutofixRunsOnceAndThenOnlyForSomethingNewTest`
failed about half its solo runs before M1a as well: package B measured 3 of 5 at the base `8e4a5cd15b`. Two things
were mixed in it:
- **The vacuum.** The test map is a vacuum, and barotrauma kept landing new damage on the patient. The test now
  gives the map station air.
- **A real autodoc gap.** The failures left show it every time. In the runs that fail, the pod abandons
  `SurgeryStopBleeding` on the fractured arm (the stall guard). It then finishes its queue with its own incision
  still open, and the planner wants `SurgeryCloseIncision` there straight after QUEUE COMPLETE. The likely
  trigger is the crush rule's chance of an internal bleed at Blunt ≥ 30 (plan §8, M3 makes it deterministic).
  `TryQueueClosure` does not queue the closure at that moment. Accepting an open `IncisionOpenComponent` there
  was tried and did not change it, so it was reverted.
- **What is left for the autodoc work.** Find why the closure is not valid when the stop-bleeding procedure is
  abandoned, and why that procedure stalls on an internal bleed at all. The test's failure message now names
  the abandoned procedures, the state and the queue.

## M1a complete: what a player now experiences (2026-09-23)

M1a changes no damage model. It changes who breathes, what every state is called, and what the choices do.

- **Getting hurt.** Pain puts you on the floor (body pain ≥ 128.25) and knocks you out for at most 20 s when the
  summed pain crosses 189. Nothing extends a faint. Another needs a rise of 40 over the pain at waking and
  never comes within 30 s. A strong or emergency painkiller ends a faint. A pain shock (130) drops and stuns
  you and gives 30 s of adrenaline: crawl ×1.5 and no do-after penalty. It no longer stands you up or stops a
  bleeding heart. Two minutes of fire and blows cost 18.5 s of unconsciousness. Pain never kills. Machines are
  only ever Downed by damage.
- **Unconscious bodies breathe.** Only arrest and death stop the chest. The brain reads real suffocation (no
  air, no lungs), never Bloodloss. With no air: Downed at about 188 s, Unconscious at about 205 s, arrest at
  about 259 s. Air back at the Unconscious line wakes you in about 7 s.
- **Bleeding.** Downed at 50%, Unconscious at 35% and still breathing, arrest at 30%. The defibrillator refuses
  under 25% and says how much blood to give. A shock gives oxygenation 0.5 and a 45 s grace. The analyzer
  asks for N u within the grace (to 35% plus the bleed) and M u to 50%. Repeated shocks without blood do not
  help.
- **Every state names its cause.** The patient gets per-cause alerts and one line per transition ("The pain
  takes you under"), each with what else is holding them down. The IPC HUD says CELL EMPTY, COOLANT PUMP
  OFFLINE, HYDRAULIC PRESSURE LOW or MOBILITY LOST, and shows sensor load and chassis temperature. Machines
  and faints get no heartbeat.
- **The medic reads it.** The analyzer heads its panel with the state and cause, breathing, circulation (blood %,
  trend, units to 50%) and the defib verdict. Examine shows the chest (not breathing, gasping, slow and
  shallow) and the pulse (pale; pale and clammy with a weak, rapid pulse; barely palpable).
- **Crawling.** Downed, you can pick up loose items on your own tile and the ones next to it (1.5 m). You still
  cannot fire. Call for help shouts your line and flags you on medical HUDs for 60 s, with a 30 s cooldown.
- **Honest endings.** Succumb and Last Words appear only in cardiac arrest. Each opens a dialog that says
  exactly what happens: catastrophic brain injury, the brain stays in the body, a returnable ghost, and revival
  by brain repair plus a defibrillator until the body rots. Typing `ghost` in arrest opens the same dialog.
  Anywhere else it offers "left alive but empty" with no return and no damage. The pod offers the way back
  after a revival, as the hand defibrillator does. The pod keeps a fainted occupant anaesthetised and does not
  sound its Critical alarm for a faint.
- **Species.** The scenarios run for Human and IPC. The report-mode conformance test lists the 98 known gaps
  in 32 other round-start species until M4 closes them.

## M1a review fixes (2026-09-23)

Two reviewers read the M1a commits. What changed:

- **"Left alive but empty" can no longer come back returnable.** If the body died some other way while that
  dialog was open, confirming it used to ghost with `canReturnGlobal` = dead, so the ghost could return,
  against the dialog's "you cannot return". Now death withdraws any open dialog (`Revoke` on death withdraws
  both kinds; heart restarts still withdraw only Succumb), `LeaveAlive` refuses a dead body, and it always
  ghosts with no way back. The player ghosts from the corpse the ordinary way. Test: a new branch in
  `HonestEndingScenarioTest`.
- **A recovered patient's next arrest is a new episode.** Plan §7.1 item 6 grants the restore and the grace once
  per arrest episode, and the code defined the episode only by `wolfmed.post_shock_repeat_seconds` (300 s)
  since the last restore. A patient who got up and walked off, then arrested again from a new wound within
  300 s, got no restore and no grace. Now the post-shock record also ends once the grace is spent and the
  patient is Up, not in arrest, with blood at or above `wolfmed.brain_blood_start` (the line where blood stops
  draining the brain, also where the analyzer's post-shock advice stops). A patient still down, or up with low
  blood, keeps the 300 s window, so the plan's repeat-shock rule is unchanged for a continuing episode. No new
  CVar. Test: a new branch in `RepeatedShockTest`.
- **Asphyxiation readers the M1a test-migration table names but no package touched.** `WolfmedSpeciesSpawnTest`
  (`IpcLeaksOilAndTakesNoAirlossTest`) and `WolfmedVitalLimitsTest` only assert the raw `Asphyxiation` and
  `Bloodloss` entries in `DamageableComponent.Damage` (IPC takes none; the Airloss cap). Neither reads the
  brain's breathing input or consciousness, so neither needed migrating. Both run, and pass, in the full filter.
- **Package A's paperwork.** `plan/p7/wp/A-report.md` now exists as a standalone record, reconstructed from
  commit `8f71617774`, the code and the `## M1a A` section above, and labelled as a reconstruction.
- **Review pathspec.** In this repository the code lives in `Content.Server`, `Content.Shared`, `Content.Client`
  and `Content.IntegrationTests`; a pathspec of `Content` matches none of them. Diff M1a with
  `git diff 8e4a5cd15b..HEAD -- Content.* Resources Docs`. No code change.

## M1a playtest 1 fixes (2026-09-23)

The owner's first M1a playtest, eight findings in priority order (`plan/p7/PLAYTEST1-spec.md`).

**1. Bleeding nerfed.** `wolfmed.bleed_rate` 0.6 → 0.3 (every wound bleed ×0.5; the arterial wound keeps its own
0.8 stage rate, about four times a critical slash, so it stays the worst bleed). Internal bleeding does not read
that knob, so its rate is halved in data (`InternalBleedingWound` 0.02 → 0.01 per severity per second, marked
Onyx YAML). The words follow the rates: the examine bleed bands (`look.yml`) and the gore system's major-bleed
rate (`sfx.yml` 0.9 → 0.45) are halved too, so a cut artery still reads "spurting". Regeneration (0.33 u/s),
the 300 u pool and the lines (50/35/30%) are unchanged. Measured by the new `BleedTimingTest` (human, full blood,
station air, untreated):

| | before (0.6) | after (0.3) |
|---|---|---|
| Arterial arm cut (Slash 25), rate at the cut | 2.75 u/s | 1.38 u/s |
| Downed / Unconscious / arrest | 64 / 91 / 100 s | 186 / 253 / 274 s |
| Plain cut (Slash 15), rate at the cut | 0.30 u/s | 0.15 u/s |
| Plain cut clots / lowest blood | 30 s / 99.7% | 30 s / 99.8% |

The arterial cut now takes 4.6 minutes from Up to arrest (the owner's floor is 4), with about three minutes on
the feet to tie a tourniquet. The test pins the shipped rate and asserts arrest ≥ 240 s, within +20% of 274 s,
Downed within ±20% of 186 s, and a plain cut clotted inside a minute having cost under 5% of the blood.
`InternalBleedTickTest` expects 0.1 and 0.2 u/s now. `BleedingScenarioTest` needed no band change; it reads the
blood band fresh at the Unconscious line, because the slower bleed lands there between two life ticks.

**Found on the way (not changed): vacuum bleeds.** A human in vacuum bleeds 1.8 u/s by two minutes in, more than
an arterial cut: barotrauma's Blunt opens bleeding blunt wounds on every part. Downed by pain at about 55 s, by
blood at about 150 s, arrest at about 205 s. Worth a look with M1b's caps.

**2. Painkillers you can feel.**
- Why a drink "did nothing": a swallowed dose waits the stomach's `DigestionDelay` (20 s) before it reaches the
  blood. Now any reagent with a `WolfmedPainRelief` effect waits `wolfmed.painkiller_absorb_seconds` (new, 4 s)
  instead: `WolfmedOralAbsorptionSystem` (`_WF`) answers the stomach's one marked line. Measured: the analgesic
  pill takes hold 5 s after swallowing and stands a pain-Downed patient up at once; 5 u of opiate ends a pain
  faint inside the same 5 s.
- The tiers reach the tests as designed: weak 22 lifts any pain-Downed body (body pain is capped at 135, and
  135 − 22 = 113 is under the 115.4 stand line); strong ends a faint; neither lifts a blood-Downed body.
- The sedation cost: the opiate adds 0.018 sedation a second while a dose runs and metabolises at 0.1 u/s, so
  5 u runs about 54 s and peaks at 74% sedation, past the 60% line where breathing slows. That is the designed
  overdose and was left alone; the guidebook already says so.
- Lines: `WolfmedPainReliefSystem` raises `WolfmedPainReliefTierChangedEvent` when the strongest tier changes, and
  the condition alert system tells the patient "The painkiller takes the edge off." / "The opiate takes hold." /
  "The stimulant kicks in." / "The stim hits. You have seconds on your feet." on reaching a tier, "The stronger
  painkiller wears off." on a drop, and "The painkiller wears off." at none. The stim's end is the crash's own
  line. Machines hear none of it.
- Pens (`painkillers.yml`, the stim pen's medipen sprite recoloured, CC-BY-SA-3.0): `WolfmedAnalgesicPen` 10 u
  analgesic (price 10) and `WolfmedOpiatePen` 3 u opiate (price 20). 3 u is about the most opiate one pen can
  hold and stay under the breathing line: measured 26 s of strong relief, 46% peak sedation. The analgesic pen:
  54 s, no sedation. Both inject on use-in-hand, which a Downed body may do to itself. Vended in `wolfgate.yml`
  (10/6), `wallmed.yml` (4/2) and `civimed.yml` (infinite/40). Guidebook line in `WoundTreatment.xml`.

**3. The defibrillator's "No response".** `WolfmedRevivalSystem.TryDefibrillate` used "No response" for both the
failed roll and the "this system does not own the body" fallback. Now the roll alone says "No response. Charge
again." and the fallback is `wolfmed-defib-not-monitored` ("Shock refused: no vital signs this device can
read."); the pod treats it as a gate and says it once, and the analyzer hides the verdict for it. What the owner
hit: the gate passes a transfused patient in arrest, and the paddles and the analyzer read the same `GetRefusal`.
The odds are what bite: `GetChance` scales `wolfmed.defib_chance` 0.85 by the brain's oxygen, down to
`wolfmed.defib_oxygenation_floor` 0.15. After about two minutes of arrest the oxygen is near 0 and a shock takes
at 13-19% (18.5% in the test, at oxygenation 0.08), so several "No response" in a row is the expected run. Left
as data for the owner: a floor of 0.4 would make the worst case 34%. A body that has died of the brain meanwhile
is refused with "Brain flatlined. Repair the brain first.", and the analyzer says "brain repair surgery first".
Shocks repeat freely while the heart is stopped; the hand defibrillator's zap delay is the only limit.

**4. Message spam in space.** The remembered words are one line: "You feel your wounds painfully close!"
(`bloodstream-component-wounds-cauterized`). Onyx's bleeding system printed it on every damage event whose Heat
trimmed a bleed on the part, with no limit, so any Heat arriving every second on a bleeding body (a fire, hot gas,
a fight somewhere hot) printed it every second. It now goes through
`WolfmedCauterySystem.TryAnnounceWoundsClosing`, once per `wolfmed.cautery_popup_seconds` (new, 10 s) per body.
Measured: 19 lines in 20 s unlimited, 5 in the next 40 s. In pure vacuum it did not fire at all (barotrauma's
Blunt outweighs its Heat in the bleed modifier sum), and the condition lines were one per real change: 2 in the
first minute, 6 over four minutes (down, adrenaline, blood loss, out, arrest). No Downed/Up flapping was found;
the Downed dwell and the hysteresis already hold.

**5. The "Let go" dialog.** The window opened centred on an empty body and grew off the bottom when the text
arrived, and `OpenCentered` measures a window before it has a parent, at whatever UI scale it last had. The EUI
now opens (or re-centres) when the state arrives, measures again in place and re-centres. The body text wraps at
420 units with no vertical expand, the old 200-unit minimum height is gone, and the margins match the treatment
window. `WolfmedChoiceWindowLayoutTest` at UI scale 1 and 1.25: on screen, centred, text wrapped and unpadded,
both buttons inside (one unit of slack for the pixel grid at 1.25).

**6. IPC crawl on low power.** Not zeroed on the server: an IPC at charge state 1 (12%) Downed by frame pain moves
at walk 0.34, sprint 0.61 (crawl 0.3 × low power 0.45). The Silicon speed table's `0: 0.00` entry is never read
(the lookup floors at key 1). What was wrong is prediction: `SiliconComponent.ChargeState` was never networked,
so the client predicted every IPC at full charge and its crawl snapped back. It is networked now (marked EE
edit), dirtied only on change. A cell under 5% rounds to charge state 0, which the Silicon death system treats as
empty: that IPC shuts down (Unconscious) and cannot crawl. That is the Silicon design and was not changed.

**7. Rejuvenate and a missing cell.** `WolfmedShutdownSystem.RestoreCell` on `WolfmedRejuvenateEvent`: a chassis
whose cell slot is empty gets its slot's `startingItem` (the prototype's own cell) inserted past the slot lock,
and the charge state is pushed at once, so the shutdown clears on the same tick.

**8. The resolve errors.** All 149 came from one upstream path: `SharedGunSystem.OnRevolverGetState` sending a
revolver's `AmmoSlots` with a deleted casing in them. Mono's spent-casing despawn (30 s) gave a fired casing a
`TimedDespawn` while it was still in the cylinder, so it was deleted in its slot. No Wolfmed system was involved.
`SetCartridgeSpent` takes a `despawn` flag (marked), the revolver passes false when it fires, and
`EmptyRevolver` arms the timer when it drops a spent casing on the floor.

**Review lows folded in.** `OnMobStateChanged`'s death branch now goes through `Apply`, so a death raises
`WolfmedConsciousnessChangedEvent` like any change (the alert system says nothing for the dead) instead of
repeating Apply's side effects by hand. The `MathF.Max(outLevel, faintLevel)` in `Evaluate`'s depth is not dead:
`faintLevel` is summed pain against the faint line, which deepens a Downed body's view as its pain climbs; the
PainFaint input only puts 1 in `outLevel` during a faint, when the body is Unconscious. Kept, with a comment.

**Numbers.** `wolfmed.bleed_rate` 0.3, internal bleed 0.01, `wolfmed.painkiller_absorb_seconds` 4,
`wolfmed.cautery_popup_seconds` 10; pens 10 u analgesic and 3 u opiate.

## M1b (2026-09-23)

Burns and caps. Plan: `WOLFMED_DEATH_PLAN.md` §12 M1b, §6, §3.7, §2.5/§6.3, OD11 (yes), OD12 as the owner answered
it on 2026-09-23 (appendages can crumble after long charring, head and torso never), P31.

**The measurement, first.** `WolfmedBurnScenarioTest.FireMeasurementTest`, a human in a 10-stack fire in station
air, never patting it out.
- Before any M1b code, with `wolfmed.body_damage_cap` 0: the fire lasts 100 s (the stacks fade 0.1 a second).
  It lands 1614 Heat, 1485 stored and 129 cut by the torso's 250. Nearly all of it lands in the first 100 s:
  1485 stored by 120 s and no more after. Total burn severity is 1615 at 120 s and 1647 at 300 s, from body heat.
  The critic's estimate of Heat "on the order of 2,000" was high. Arms and legs stored 181 to 200 Heat, past the
  new 152 ceiling but under the 250 ash line, so this fire would not have ashed a limb even uncapped.
- The same fire under M1b: Total Heat 1615 (every hit's Total, stored or not). Stored 1272 to 1290 and 324 to 343
  not stored. Burn severity is about 1700 by 120 s: the ceiling cuts nothing from the wounds, and a burn at its
  200 escalates into charring. Fluid loss runs at 1.36 u/s.
- **The rate set against it:** `fluidLossPerSeverity` **0.0008** u/s per point (plan start 0.002). At 0.002 the
  measured burns would lose about 3.4 u/s. The heart would stop about two minutes after ignition, before a
  medic could reasonably arrive. At 0.0008, from ignition untreated: Downed by blood at about 195 s, Unconscious
  at about 245 s, arrest at 255 to 270 s (`BurnScenarioTest`: arrest 260 s against 258 s derived from the
  measured burns). That is close to the arterial cut's 274 s (playtest 1).
- A dressed full-body burn loses 1.06 → 0.26 u/s, under regeneration (0.33 u/s), so a dressed patient's blood is
  stable. A graft takes it to 0.

**What was built.**
- **One event per hit (§6.2).** `PartDamageAppliedEvent` gets `Overflow` (what a ceiling cut from this hit) and a
  computed `Total` (Applied + Overflow). Its `Damage` stays the stored amount (Applied). In routing, one
  `PartDamageAppliedEvent` is raised per hit, including one that stored nothing. `PartDamageOverflowedEvent`
  (evisceration, tear-off pressure) still goes first and still carries only the torso cap's overflow. The ambient
  ceiling's cut never counts as tear-off pressure. The dispatcher (`OrganDamageSystem.OnPartDamageApplied`)
  hands `Total` to wounds, bleeding and the organ roll, once. Fractures and amputation get Applied. The D27
  accumulator adds Applied only.
- **The ceilings (§6.1).** `WolfmedBodyPartSystem.ClampToBodyCap(body, part, damage)` now returns what it cut:
  - *Per-part ceiling* for damage with no origin that is not an explosion:
    `wolfmed.ambient_part_cap_fraction` 0.8 × the part's lowest Destructible trigger. That gives arm and leg 152,
    hand and foot 120, and a human head 400. IPC parts get 152, the head included, because their base carries
    the limb triggers. `WolfmedPartCeilingSystem` (server) answers the new `WolfmedPartDestructionThresholdEvent`
    from the part's `DestructibleComponent`. The torso's 400 trigger gives 320, so its own 250 cap governs, as
    the plan says.
  - *Corpse ceiling:* `wolfmed.body_damage_cap` 600 now applies only to Dead bodies.
  - *Admin bypass (P31):* `WolfmedBodyPartSystem.WithCeilingBypass`, used by the part form of `damage`. `_WF` on
    both ends, no hook.
- **Burn fluid loss (§3.7, OD11).**
  - `WolfmedFluidLossSystem` runs once a second. Every wound whose prototype declares `WolfmedFluidLossBehavior`
    loses severity × `perSeverity` × `wolfmed.burn_fluid_rate` (1) once it is at `from` or above.
  - Declared on `BurnWound` (`from` 20, marked Onyx YAML) and on `WolfmedCharringWound` (`from` 0).
  - The volume is split out of the blood solution and discarded, so there is no puddle. A sub-hundredth
    remainder carries to the next tick.
  - Dead bodies lose nothing. Arrest does not slow it. `WolfmedBurnFluidLossComponent` on the body carries the
    rate for the analyzer and examine.
- **Dressing and graft.**
  - A healing item that removes Heat (ointment, burn packs, gel) dresses the resolved part's weeping wounds
    (`WolfmedDressedComponent`), from `HealingSystem.Wolfmed.cs`: × `wolfmed.burn_dressed_fluid_factor` 0.25.
  - The existing graft surgery step (`SurgeryStepGraftSkin`) carries the new `WolfmedSurgeryGraftBurnsEffect`.
    It grafts every weeping wound on the part: × 0.
  - A dressed or grafted wound that grows `wolfmed.burn_treatment_lost_severity` (15, the burn's reopen line)
    past where it was treated loses the treatment.
- **Burn infection (P20).** Infection profile `dressedMultiplier` 0.15. A wound with no bleeding treatment that
  is dressed or grafted progresses at 0.15. Measured 3.29 against 21.95 over three minutes (0.150).
- **A wound at its cap escalates (§6.3).** Heat on a part whose burn is at its 200 becomes charring:
  `wolfmed.char_escalation` 1 point per point of Heat, up to the charring's 120.
- **OD12, crumbling.** A hand or foot whose charring sits at its maximum (120) crumbles to ash after
  `wolfmed.char_crumble_seconds` 180 of Heat still arriving. "Still arriving" means gaps of at most
  `wolfmed.char_crumble_gap_seconds` 10. Arms and legs take × `wolfmed.char_crumble_limb_multiplier` 2. Head and
  torso never crumble. The crumble goes through Shitmed's `BurnPart`, a tick later: no stump wound (cauterised),
  organs dropped, the ordinary part-loss handling, and "The left hand crumbles to ash!". It is wired through
  `WolfmedPartHitSystem`, the dispatcher's one-line hand-off, which sees each hit once before the wounds do.
  `CharCrumbleTest` with the clock pinned at 20 s: the hand crumbled 20 s after charring through and the arm 39 s
  after. The head and torso, under the same 10 Heat a second, charred through at 30 s and were still attached at
  120 s, the head storing at most 400.
- **Extinguishing while Downed (to confirm).** Confirmed, no change. `FlammableSystem.Resist` needs
  `CanInteract(uid, null)`, and `WolfmedDownedSystem` blocks only interactions with a target. Only the fall's own
  2 s stun blocks it. `DownedCanPatOutFireTest` pins it.
- **What people read.**
  - Analyzer: "Fluid loss from burns: slow/fast (x u/s)" under the circulation line; fast from
    `wolfmed.analyzer_burn_fast` 0.5 u/s. The blood trend and the post-shock transfusion number now count burn loss
    (`WolfmedLifeSystem.GetVolumeLossRate`).
  - Examine, close up: "has weeping burns".
  - The patient: "Your burns are weeping fluid. Dress them; you will need fluids." when it starts.

**The pain faint's cooldown, 30 → 50 s (plan §3.1's fallback).** Burns now grow past where the old 600 stopped
them, so pain keeps rising through a fire, and each +40 over the pain at waking re-arms a faint. At 30 s, a pure
10-stack fire fainted the patient three times in two minutes (60 s Critical). `SustainedFireFaintTest` (fire plus
blows) measured 53 s in one full run and 37.5 s in another. Plan §3.1 names the fix if that test fails: raise the
cooldown to 50, which caps any two minutes at two faints by the timers alone. With 50:
- the burn scenario faints at 15 s and 85 s: 40 s in the fire's two minutes;
- `SustainedFireFaintTest` read 40.5 s at its half-second samples, two faints of 20 s, the second 50 s after
  waking.

**Differs from the plan, and why.**
- **Burn rate 0.0008, not 0.002.** Set against the measurement, as the plan asks.
- **`BurnScenarioTest` sees two faints, not "one faint".** The M1a re-arm rule (+40 over the pain at waking)
  refaints a patient whose burns keep growing. The test asserts each faint ≤ 20 s and at most 40 s Critical in
  the fire's two minutes (§2.3).
- **`fluidLossFrom`/`fluidLossPerSeverity` are one wound behavior**, `WolfmedFluidLossBehavior { from, perSeverity }`,
  declared once on `BurnWound`'s base behaviors (a single marked YAML block) instead of on each stage. The values
  are the same at every stage, so one declaration is the smaller Onyx edit. The charring wound weeps too.
  - Why: the plan puts the fields on "the Wolfmed burn prototypes (burns.yml)" as well.
  - The effect: the overflow that escalates into charring keeps adding fluid loss. The plan's AmbientCeilingTest
    asks for this ("grows the burn wound and its fluid loss").
- **The graft is the existing charring surgery.** A burn under 80 never chars, so it has no graft to receive. A
  dressing leaves it at × 0.25, which at those severities is under 0.04 u/s. There is no graft item outside
  surgery ("burn kit" in the plan).
- **A new hand-off method.** `WolfmedPartHitSystem.OnHit` is one more line in the same marked dispatcher method
  (inventory #8). The plan's §6.3 escalation and the owner's OD12 crumble both need each hit's Total once, and
  only one system may subscribe the event. It also carries the test seam (`Observer`). `SaturatedTorsoTest` and
  `AmbientCeilingTest` read the Total the dispatcher hands to wounds and organs there. Organ damage itself is
  M3's to assert.
- **Treatment is lost on regrowth** (`wolfmed.burn_treatment_lost_severity`, new). The plan says nothing about a
  dressed burn burned again. Without this, a grafted burn would never weep again.
- **The patient's "Burns weeping fluid" is a popup and a line when it starts, not a status alert.** An alert would
  need a prototype and an icon. The analyzer and examine carry the ongoing state.
- **The analyzer's burn line is its own row** under circulation, not appended to a bleed-rate figure. The panel
  has no bleed-rate figure.
- **`TryApplyPartDamage` returns false for a hit that stored nothing**, although its event is raised and its
  wounds grow. It reports what was stored, as before.
- **IPC heads get a 152 ambient ceiling**, not 400. Their part base carries the MajorLimb triggers (190/210).

**Test migration.**
- `DamageTotalsNeverCritAWoundHostTest`: comment only. The 600 it names is no longer a body-wide cap on the
  living.
- `WolfmedDamageCommandTest`: new `DamageCommandPassesTheAmbientCeilingTest`. The command stores 200 Heat on an
  arm; the same Heat with no origin stores 152.
- `WolfmedBurnWoundTest`: new `BurnAtItsCapEscalatesIntoCharringTest`.
- `WolfmedEviscerationTest`: new `OverflowHitFollowsTheTearTest`. The hit past the cap is one event, raised after
  evisceration has had its overflow, with Applied 0 and Overflow the whole cut.
- `WolfmedInfectionTest`: a dressed burn infects at under a quarter of an open one.
- `WolfmedSpeciesSpawnTest`:
  - `BodyDamageIsCappedTest` now asserts each living part at its per-part ceiling. It sends 300 Heat a part,
    past the torso's 250.
  - `CappedCorpseCanStillBeDismemberedTest` kills the body first, since the corpse ceiling is what it tests.
  - `DionaLimbIsDestroyedNotSeveredTest` and `IpcLimbSeverabilityAndGibCeilingTest` give their 195 Blunt an
    attacker, because ambient damage no longer destroys limbs.
- `WolfmedAmputationTest`:
  - `OverflowAmputationMechanismIsInertOnShippedLimbsTest` gives its head hits an attacker. 16 × 25 ambient
    Blunt sat exactly at the head's 400 ceiling.
  - `GunsAndLasersAmputateOverThresholdLimbsTest` gives its bullets and lasers a shooter. A hand's ambient
    ceiling (120) is under its 200 thresholds.
- These tests were skipped in every full run, which hid the failures. Each failed alone until migrated.
- `SustainedFireFaintTest` pins the shipped 50 s cooldown and checks it on the server clock (`PainFaintCooldownUntil`).
  It now times everything on the server clock too. Counting half-second loops ran about 6% slow, because every
  `WaitPost`/`WaitAssertion` runs ticks of its own: the M1a version read a real 50 s gap as 47 s, and 30 s as
  28.5 s. It asserts at most two faints in the two minutes and at most 21 s each as sampled (≤ 42 s in all).
  Two 20 s faints read 40.5 s and 41.6 s in two runs.

**Tests.** Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 424 total, 414 passed, 0 failed,
10 skipped; each skipped test passes alone.

**Numbers.** `wolfmed.ambient_part_cap_fraction` 0.8, `wolfmed.body_damage_cap` 600 (corpse), `burn_fluid_rate` 1,
`burn_dressed_fluid_factor` 0.25, `burn_treatment_lost_severity` 15, `char_escalation` 1, `char_crumble_seconds`
180, `char_crumble_limb_multiplier` 2, `char_crumble_gap_seconds` 10, `analyzer_burn_fast` 0.5,
`pain_faint_cooldown` 50; `fluidLossPerSeverity` 0.0008 (burn from 20, charring from 0); infection
`dressedMultiplier` 0.15.

**For the M3 merge.**
- M3's organ hand-off in `OrganDamageSystem.OnPartDamageApplied` must read `total` (the hit's Total), not `args`.
- Barotrauma routes through `ClampToBodyCap(body, part, damage)` like any damage with no origin.
- `WolfmedPartHitSystem.OnHit` runs first in that method.

**Found on the way.**
- **Mono's grid cleanup deletes long scenario grids.** In two of three full runs, `PainScenarioTest` failed with
  its body gone ("does not have WolfmedConsciousnessComponent"). `GridCleanupSystem` had deleted the test grid
  during the ten-minute stretch: no player nearby, no powered APC, low value. The test passes alone. The new
  `WolfmedScenario.KeepGrid` gives the grid `CleanupImmuneComponent`. `PainScenarioTest` and the M1b burn
  scenarios call it.
- The dressing hook in `HealingSystem.Wolfmed.cs` is exercised only through its `Dress` seam. No test drives a
  real ointment do-after.

## Playtest 2: faint timer, fire helplessness (2026-09-23)

The owner's second playtest (`plan/p7/PLAYTEST2-spec.md`): "There needs to be some kind of indication of when you'll
come round from pain crit" and "I stood in fire and my hands stopped working / I couldn't crawl at all".

**1. The faint counts down.**
- The faint alert (`WolfmedFaintPain`) carries the engine's alert cooldown from the faint's start to
  `PainFaintUntil` (new server field `PainFaintStart`), so the icon ticks down to waking. It is shown only while the
  faint is all that holds the body under (cause PainFaint, no blockers). A faint that ends early (a strong
  painkiller) changes the state or cause, and the alert that replaces it has no cooldown.
- The condition text says "Coming round in {N} s." (`wolfmed-cause-pain-faint-help-timed`) in place of "You come
  round in seconds." while unblocked; the blocked form is unchanged ("something else keeps you under" plus the
  blockers). The "out" line at the faint's start carries the seconds too.
- The analyzer: "FAINTED: pain, {N} s" (`FaintSeconds` on the vitals report); a blocked faint keeps
  "FAINTED: pain (also: …)" with no seconds.

**2. What took the hands and the crawl: Shitmed's limb switch-off, not Wolfmed.** Reproduced in
`FireHelplessnessTest` before any change (10-stack fire, station air, sampled every 5 s):
- `SharedBodySystem.CheckBodyPart` switches a part off (`BodyPartComponent.Enabled = false`) when its stored damage
  reaches `IntegrityThresholds[CriticallyWounded]`, 90, whatever the damage is. M1b's ambient ceilings let a
  burning arm or leg store up to 152 and a hand or foot 120, so every limb crossed 90 during the fire.
- A switched-off arm or hand raises `BodyPartDisabledEvent` and `HandsSystem` removes the hand: 2 hands at 20 s,
  1 at 25 s (right arm off at 103), 0 at 35 s (left arm off at 98). No hand, no pen, no pickup.
- A switched-off leg leaves `BodyComponent.LegEntities`; `UpdateMovementSpeed` averages over the enabled legs, so
  one leg off halved the base speed (crawl 0.188 at 40 s) and both off set it to 0 (walk 0.000 from 50 s on, to
  the end of the run). Nothing Wolfmed could multiply brought it back.
- Not the cause: Onyx's `BodyPartFunctionalityState` stayed Functional throughout (P2-3 keeps it off); Wolfmed's
  limb penalties never went past ×0.65 movement (charring stayed at 20-32, its Moderate stage, with no penalty);
  the faints (15-30 s, 85-100 s) and the pain shock's stun were short and ended. `CanInteract` and Call for help were
  intact at every Downed sample; the hands were simply gone.

**The fix, to the plan's rule (§2.2: a Downed body can always crawl, use its carried items on itself and Call for
help).**
- **Burns never switch a limb off.** Marked edit in `CheckBodyPart`: on a wound host, the switch-off and
  switch-on lines read the part's damage less its Burn group (Heat, Cold, Shock, Caustic;
  `WolfmedLimbIntegrity.ForEnable`). Brute past 90 still switches a limb off exactly as before; the targeting doll
  still reads the full damage. Applies to legs too, so the plan's "painkillers still lift a badly burned patient"
  holds.
- **Burns slow the hands instead.** `BurnWound` gets `WolfmedLimbPenaltyBehavior` manipulation ×1.25 at Severe (50)
  and ×1.5 at Critical (80) (marked Onyx YAML); charring keeps its own ×1.3/×1.6 and movement penalties. Measured:
  an arm burned to 152 and its hand to 120 do a pen's do-after at ×2.06.
- **Crawl floor** (`wolfmed.crawl_floor` 0.35, new, replicated). `WolfmedCrawlSystem` answers a new
  `WolfmedSpeedFloorEvent`, raised by a marked line in `MovementSpeedModifierSystem.RefreshMovementSpeedModifiers`
  after every other modifier. While Downed a wound host never crawls under 0.35 × lying-down 0.3 × the default base
  (2.5 walk, 4.5 sprint): 0.263 walk. With no working leg it drags itself on its arms at exactly that (×1.5 while
  adrenaline runs); with no working arm and no working leg it cannot move. "Working" is attached, switched on and not
  Onyx-Disabled. Because Shitmed gives a body with no enabled leg a base speed of 0, a second marked line in
  `SharedBodySystem.UpdateMovementSpeed` gives a legless wound host the default base instead and refreshes the
  modifiers against it; the floor then sets the real speed. An arm switching on or off refreshes the speed on the
  next update (`WoundableComponent` + `BodyPartEnableChangedEvent`).
- **Legs switched off Down the body.** `WolfmedConsciousnessSystem.LegsGone` counts a leg Shitmed has switched off,
  since that is what drops the body; before, only Onyx-Disabled or missing legs counted, and a body with both legs
  broken past 90 lay on the floor while Wolfmed called it Up.
- **The penalty is told.** When a wound penalty first slows the hands or the legs the patient hears "Your hands are
  badly burned; everything takes longer." / "Your legs are badly burned; moving is slow." (or "…are hurt…" for other
  wounds), once until it clears; a body that was out hears it on coming round. The same line sits in the condition
  text while it lasts. `WolfmedWoundTraitSystem.GetBodyLimbPenalty` reads the worst penalty and whether a burn is
  behind it; the condition alert system answers the broadcast `WolfmedWoundLifecycleEvent` and checks once a tick.

**After the fix**, the same fire: both hands at every sample, the pen picked up and injected at 60 s (the analgesic
then stood the body up, as the plan's standing decision says), crawl 0.375-0.563 at every Downed sample, Call for
help at every Downed sample, faints at 15-30 s and 85-100 s unchanged.

**Differs from the spec, and why.**
- The spec suspected Wolfmed's limb penalties or the M1b escalation. Neither disabled anything; the cause was the
  Shitmed switch-off that DECISIONS P2-3 deliberately left running, reached by M1b's higher ambient ceilings.
- Burns are excluded from the switch-off on every limb, not only hands and arms: a burned leg switched off would
  make the body Downed by legs, which no painkiller lifts.
- Burns carry no movement penalty of their own; charring's existing one stays.
- `FireHelplessnessTest` accepts a sample where the pain shock's 2 s stun blocks movement (speed is still above 0).

**Numbers.** `wolfmed.crawl_floor` 0.35 (floor 0.263 walk, 0.473 sprint for a human); BurnWound manipulation ×1.25
(Severe), ×1.5 (Critical).

**Tests.** Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 428 total, 421 passed, 0 failed, 7
skipped; each skipped test passes alone.

## M2 (2026-09-23)

Arrest, revival and medic information. Plan: `WOLFMED_DEATH_PLAN.md` §12 M2, §5.2-5.5, §3.3 (the breathing alert),
§3.4, §7.2 (M2 rows), §11 (M2 rows); the owner's answers of 2026-09-23: OD7 (c), OD8 (b), OD10 (b) with no trauma on a
repaired core, OD14 (a new antagonist), OD15 (sepsis half), OD17, OD20 (a). Branch `Wolfmed-m2`, from `bedaf99945`.

**What was built.**
- **Sedation model (§3.4, P15).** The `WolfmedPainRelief` effect's per-second `sedation` is gone; `sedationPerUnit`
  replaces it. Each metabolism tick the dose asks for (units of that reagent still in the blood) × per unit; the body's
  sedation moves toward the sum at `wolfmed.sedation_rise` 0.05/s and falls toward it at the existing 0.035/s, so a
  steady dose levels off and a finished one decays. Opiate 0.22/u, tramadol 0.11/u, oxycodone 0.12/u (marked Onyx YAML).
  - Measured (`SedationModelTest`, one standard dose: the 3 u opiate pen, 5 u tramadol or oxycodone): peaks 0.46, 0.46
    and 0.49. Doubled: 0.92, 0.90 and 0.97, each Downed with cause Sedation. Three doses reach full sedation.
  - Warnings (self popup and chat, once each as sedation climbs; a line re-arms once sedation falls back under it):
    "You feel heavy and drowsy." at `wolfmed.sedation_warn` 0.4, "Your breathing slows. Another dose could stop it." at
    the component's `SedationAirlossThreshold` 0.6 (so it moves with the depression line), "You can barely stay
    awake." at `wolfmed.sedation_warn_heavy` 0.8. Measured firing at 0.41, 0.62 and 0.83.
    `WolfmedSedationWarningSystem` (server) tells them through the condition alert system.
  - Depression past 0.6 is still its own brain drain and never Asphyxiation (`SedationOverdoseTakesAirTest` keeps that
    assertion).
- **Naloxone (OD14).** `WolfmedNaloxone` ("naloxone"), `WolfmedReverseSedation` effect: each unit metabolised takes
  `sedationReversePerUnit` 0.3 off the sedation and holds the target at zero for 4 s past the last tick, so the opioid
  still in the blood cannot pull it back up meanwhile. Metabolism 0.5 u/s; it wears off before the opioid does, so a
  large overdose can come back. Recipe Inaprovaline + Ammonia → 2 (no conflict with an existing reaction). The 5 u
  `WolfmedNaloxonePen`: NanoMed 2 and the Wolfgate vendor 4, wallmed 1, civimed 40. Measured: a full overdose was out
  of Unconscious 0.5 s after the pen and under 0.6 within the pen's run.
- **The autodoc under the new model.** The pod pushes only the units that keep the occupant's sedation target under
  `wolfmed.autodoc_sedation_cap` 0.5, at the strongest sedation per unit among the anaesthetics it may push, and tops
  up as the dose is used. Its 15 u opiate default would have asked for 3.3 and overdosed every patient.
- **Stim on strong (P30).** `MasksSlowdown` reads the doses, as `EndsFaint` already did: a stimulant on top of a strong
  painkiller no longer brings the wound slowdowns back just because Stimulant outranks Strong.
- **Sepsis on a clock (OD15, data).** `wolfmed.arrest_sepsis_chance` 0.01 → 0. Sepsis at 80 drains the brain at 1/600
  a second and the heart stops through the oxygen trigger; that arrest is named "sepsis" when sepsis is the drain behind
  it. Measured 511 s from full, both runs (derived 510 s).
- **Cold (P24).** Tissue loss below 0.4 oxygenation is multiplied by the same cold factor as the drain; so is the
  analyzer's brain-death estimate. Measured 0.100 of the warm loss at 280 K.
- **Electrocution (P28).** The arrest reads shock × the siemens coefficient the electrocution attempt left after
  insulation, the same figure it deals as damage. No upstream edit was needed (below).
- **Sutures (P20).** `WolfmedSutureComponent` on `MedicatedSuture` (marked YAML); `HealingSystem.Wolfmed.cs` marks the
  treated part's open wounds that share the item's treated types with `WolfmedSuturedComponent`. Infection reads the
  profile's existing `Sutured` rate (0) for them until the wound grows `wolfmed.suture_treatment_lost_severity` 15 past
  where it was sutured. Measured: 6 minutes, sutured 0.00 against open 36.46.
- **Executions and suicide (OD17, P10, P29).** HOOK 13 rewritten: the execution raises `WolfmedEndingEvent` on the
  victim, and `WolfmedDyingActionsSystem.EndDeliberately` sets the brain (or positronic core) to 0 where it sits, kills,
  and ends any arrest on the corpse: the order Succumb uses. A suicide raises the same event from the self-execution
  branch, and `SuicideSystem.Suicide` calls the same method after its own events (marked line). The ghost from a
  suicide is upstream's, non-returnable. Brain repair plus a shock revives an executed body.
- **Restart button (§7.2).** Marked line in `DeadStartupButtonSystem`: on a wound host
  `WolfmedRevivalSystem.TryRestart` decides instead of the damage total. `GetRestartRefusal`: rot, other content's
  `Unrevivable`, no head, no core, core destroyed, no pump, no power; each with a line ("buzzes: core destroyed. Core
  repair surgery first."). A restart goes to Critical and lets consciousness decide, as a shock does.
- **Core repair (OD10).** `SurgeryRepairCore` on the torso, listed while the `posbrain` slot's core is damaged: unbolt
  the housing (wrench, `WolfmedHullPlate`), re-flash the core (multitool, new `WolfmedCoreProbe`, marked YAML on
  `Multitool`), weld the housing shut (welder, `WolfmedHullWeld`); `WolfmedCoreHousingOpen` marks the step in between.
  The repair step is `WolfmedSurgeryBrainRepairEffect` with a new `slot` field (`posbrain`). `RepairBrain` gives a
  machine no `WolfmedBrainTrauma`; `WolfmedCoreRestoredComponent` puts "CORE RESTORED: DIAGNOSTICS" (Info) on the
  synthetic HUD for `wolfmed.brain_trauma_minutes`. The analyzer's core lines name core repair, and a dead core's banner
  reads "CORE FAILURE - core destroyed. Core repair surgery, then the restart button."
- **The vitals block, completed (§5.5).** Two new lines and one verdict:
  - "Getting worse: bleeding (pressure, gauze, tourniquet); sepsis (antibiotics)", or "Getting worse: nothing now".
    `WolfmedLifeSystem.GetActiveRoutes` lists every drain on the brain (arrest, airway, lungs, sedation, circulation,
    sepsis), tissue loss, bleeding, internal bleeding and burn fluid loss.
  - "After a restart: arrest cause blood. Still present: yes. Transfuse ≈ N u within 45 s to stop the heart stopping
    again; ≈ M u to 50% to stop the brain injury." `WolfmedArrestMemoryComponent` is written whenever a living heart
    restarts and kept `wolfmed.arrest_cause_memory_seconds` 300. "Still present": blood under the brain-safe line,
    still suffocating or overdosed, the heart still failed, sepsis still past its line. The M1a post-shock banner hides
    while this line carries the same numbers.
    *[M2-M6 review correction: M3's damaged lungs count too, for "Still present" after an oxygen arrest and for the
    lungs route; see "M2-M6 review fixes".]*
  - A dead chassis: "Restart: ready" or "Restart: refused: core destroyed, core repair surgery first".
  - The brain line and the arrest countdown were already their own rows; the defib verdict shipped in M1a.
- **Examine (§5.5, §5.3).** Close up, others only: AVPU from the state and cause ("awake and answers you" while Downed,
  "drowsy and responds only to voice" at `wolfmed.sedation_warn` or Downed by sedation, "stirs only to pain" in a faint,
  "unresponsive" out cold or in arrest; nothing for somebody up and clear-headed); "lips are blue" under
  `wolfmed.examine_cyanosis_oxygenation` 0.54; "pupils are pinpoint" with the sedation; "pupils are unequal, and they
  are confused" with a brain injury (the brain organ under its concussion line, or a repaired brain's trauma). A head
  wound's own concussion adds nothing: `InternalFindingsNeverShowTest` keeps a mild one invisible. At range, a Downed
  body playing dead "appears lifeless".
- **The explanation card (§5.2, §2.3).** `WolfmedExplanationCard` (shared) builds the lines from the cause prototype:
  title, "Also holding you down: …", symptom, the help that wakes you in its blocked form while something else holds
  you (so a blocked faint never says "shortly"), "Someone is giving you CPR.", "A medic is examining you.". While Dying,
  a ten-cell bar of the rescue window with no seconds. `WolfmedCardSystem` (server) keeps `WolfmedCardComponent`
  (networked) on unconscious bodies: the bar in tenths of the untreated window the brain had when the arrest was first
  read (CPR refills it), CPR, and an analyzer read within `wolfmed.card_examined_seconds` 3. The client's
  `WolfmedExplanationCardSystem` draws it low on the screen; a machine whose synthetic HUD owns the view gets none.
- **Dying view (§5.2).** The Unconscious depth follows the route toward arrest (shared `WolfmedDyingDepth`: blood 35% to
  30%, oxygenation 0.45 to 0.15, whichever is further), not `pressure − 1`, which was dead code. A faint draws no dying
  view; it gets its own white-out.
- **"Can't breathe" (§3.3).** A marked line in `RespiratorSystem` shows "Can't breathe: no air" in place of the lung's
  stock low-gas alert on a wound host; `WolfmedBreathingAlertSystem` shows "Can't breathe: no lungs" for a body with no
  working lungs, which the respirator never alerts because it alerts once per lung. Same Breathing category, so the
  respirator's own clear takes them off.
- **Crawling stage (§5.3).** `WolfmedCrawlActionsSystem`, granted from consciousness's `Apply` beside Call for help:
  Check yourself (Up or Downed: the condition text and the self look, to your own chat) and Play dead (Downed only,
  OD20; moving, speaking or doing anything to anything ends it; being dragged does not). Adjacent aid (OD7 (c)):
  `WolfmedDownedSystem.CanAidAdjacent` lets a Downed body interact with a Downed neighbour within `wolfmed.downed_reach`
  while the item in its active hand is tagged `Gauze`; the Downed do-after penalty still applies.
- **Wait as a ghost (OD8 (b)).** `WolfmedDormantSystem`: Unconscious or shut down, alive, not in arrest, not a faint, no
  drain on a brain and no route running is stable; after `wolfmed.dormant_offer_seconds` 90 of that without a break the
  body is flagged in distress on medical HUDs (the Call for help icon, once) and gets the "Wait as a ghost" action. It
  opens a dialog with the plan's text; yes spawns a returnable ghost from the living body through the ghost system's own
  spawn (a visit, not `OnGhostAttempt`). Anything getting worse withdraws the flag, the action and an open dialog; a
  waiting ghost is told "Your body is getting worse: {route}. You can return to it now." once per route. When the body
  wakes the ghost is offered the return prompt.

**Numbers.** `wolfmed.sedation_rise` 0.05, `sedation_warn` 0.4, `sedation_warn_heavy` 0.8,
`examine_cyanosis_oxygenation` 0.54, `arrest_cause_memory_seconds` 300, `dormant_offer_seconds` 90,
`card_examined_seconds` 3, `suture_treatment_lost_severity` 15, `arrest_sepsis_chance` 0; `sedationPerUnit` opiate 0.22,
tramadol 0.11, oxycodone 0.12; naloxone 0.3 per unit, 0.5 u/s, 5 u pen.

**Differs from the plan, and why.**
- **The standard dose "targets about 0.45"** is read as reaching about 0.45. With the target falling as the reagent is
  used, a 0.45 target reaches only about 0.35 at the planned rise, and two doses would not Down anybody. The per-unit
  figures are set so the realised peaks match the plan's ladder: one dose about 0.46, two about 0.9, three full.
  The to-confirm per item: `WolfmedOpiatePen` delivers 3 u; tramadol and oxycodone ship in no item, so 5 u (the
  syringe's smallest setting) is their dose, and a full 15 u syringe is three doses.
- **The antagonist also holds the target at zero** while it is in the blood. "Lowers sedation by 0.3 a unit" alone
  would be undone at 0.05/s by the opioid still there.
- **No marked edit in `ElectrocutionSystem` (inventory #10).** The electrocuted event already carries the coefficient
  the insulation left, so the fix is `_WF`-only.
- **Execution goes through an event, not a call.** `SharedExecutionSystem` is shared and the ending is server code;
  `WolfmedEndingEvent` has one server subscriber. The old `_woundRouting` dependency and its `using` left the upstream
  file with it; `WoundDamageRoutingSystem.TryApplyLethalDamage` now has no caller (left in place, vendored Onyx).
  *[M6: removed, with the routing's MobThresholdSystem dependency it alone used.]*
- **Suicide's kill is in `Suicide()`, after upstream's own events,** rather than in the default damage handler, so an
  environmental suicide (a microwave, a gun) kills a wound host too.
- **Core repair has no incision requirement**; the three chassis steps are the whole surgery, as the chassis breach
  weld is. The plan said "a copy of SurgeryRepairBrain".
- **"CORE INTEGRITY {word}" is M3's** (the core-injury input); M2 adds only CORE RESTORED.
- **The routes** add bleeding, internal bleeding and tissue loss to the plan's list, because each is something getting
  worse with its own first aid. Wait as a ghost uses the same list, so any bleeding blocks the offer.
- **AVPU and the pupils are close-up findings for others only.** Nobody sees their own pupils; "you are unresponsive"
  to yourself is meaningless. Nothing is added for a healthy body, as M1a D did for the strong pulse.
- **Play dead reads "appears lifeless" at range only.** Close up the medic sees them breathing and answering.
- **Adjacent aid is gauze only, onto a Downed neighbour only.** "Pressure" has no verb of its own (M1a D: pressure is
  gauze or a bandage); an unconscious neighbour is not "Downed" in the plan's terms. Widening it to anyone on the floor
  is one condition in `CanAidAdjacent`, for the owner's playtest.
- **The distress flag is on medical HUDs only** and reuses the Call for help icon. There is no robotics HUD to put it on.
- **"Can't breathe: lungs damaged"** is not built: it is M3's lung route. The two alerts M2 ships are "no air" and "no
  lungs".
- **The rescue line "A medic is examining you"** comes from any vitals build: the hand analyzer and the pod's panel.
- **Test names:** `AdjacentDownedAidTest` is new (the plan lists no test for OD7 (c)); Check yourself is asserted in
  `PlayDeadTest`. `StimOnStrongTest` asserts the masking through `MasksSlowdown`, not a measured walk speed, because a
  fracture's grade is a roll.

**Test migration.**
- `WolfmedConsciousnessTest.SedationOverdoseTakesAirTest`: a target of 1.5 and 16 s instead of 0.5 a second; keeps
  "no Asphyxiation". `StackedPainkillersHitTheReliefCapTest` needed nothing: its doses ask for no sedation.
- `Scenarios/WolfmedCauseScenarioTest.OverlappingCausesTest`: the overdose asks for 1.5 and gets 22 s to reach it.
- `WolfmedDyingLevelTest`: new `DepthFollowsTheRoute`.
- `WolfmedBrainTest.ArrestClockRunsOutTest`: the cold brain has lost no more tissue than the warm one;
  `MechanicalShutdownAndDeathTest`: a repaired core carries no trauma and the restart refuses a chassis with no pump.
- `Scenarios/WolfmedMedicLinesTest`: a Downed patient's vitals block gains "Getting worse: nothing now" (four lines).
- `WolfmedLocaleCoverageTest`: the route and restart-verdict enum families.
- grep `arrest_sepsis_chance`: no test pinned it.

**Tests.** New in `Scenarios/WolfmedRevivalTest.cs`: `RestartHookTest`, `CoreRepairTest`, `ExecutionAndSuicideTest`,
`SepsisDeterministicTest`, `ColdBrainTest`, `InsulatedShockTest`, `SutureInfectionTest`. New in
`Scenarios/WolfmedMedicInfoTest.cs`: `AnalyzerVitalsTest`, `ExplanationCardTest`, `SedationModelTest`,
`StimOnStrongTest`, `WaitAsGhostTest`, `PlayDeadTest`, `AdjacentDownedAidTest`.
Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 443 total, 435 passed, 0 failed, 8 skipped; each
skipped test passes alone.

**Found on the way.**
- `BloodstreamSystem.TryModifyBleedAmount` does nothing on a wound host (GUARD E3), so tests that need a bleed make a
  wound.
- A test that raises a `SurgeryStepEvent` by hand has to pass the tools: Shitmed applies a step's add and remove only
  when one of them carries the step's tool.
- **Fixed (M1a):** `SetExternalPressure` ignored any change under 0.001, so a ramp whose last step onto 1 was smaller
  stalled just short (hypoxia held at 0.9994) and never crossed its line; with the new sedation sitting at exactly 1,
  `OverlappingCausesTest` named Sedation instead of Hypoxia. A step onto or off the full line now always lands.
- **Fixed (test):** `WolfmedTreatmentRestrictionTest.SuturesTreatCutsAndBleedingNotBruisesTest` failed alone about one
  run in four: its 10 Blunt bruise rolled BluntWound's 25% bleed, which the suture then stopped. The bruise is 7 now,
  under the bleed's `minimumSeverity: 8`.

**Art debt.** Play dead reuses the Fake Death icon, Check yourself the eye icon, Wait as a ghost the ghost icon; the
distress flag the Call for help icon; the naloxone pen the medipen sprite recoloured; the "Can't breathe" alerts the
stock low-oxygen icon.

**For the M3 merge.**
- `WolfmedCause` and `WolfmedConsciousnessCausePrototype` are untouched; the card reads any cause M3 adds from its
  prototype (a head-blow faint gets the white-out through `WolfmedCauses.IsFaint`).
- M3's lung route should add a `WolfmedCantBreatheLungsDamaged` source to `WolfmedBreathingAlertSystem.SuffocationAlert`
  and a route bit if it drains through its own source.
- M2 edited `WolfmedLifeSystem` (tissue loss cold factor, sepsis naming, routes, arrest memory, core restore),
  `WolfmedConsciousnessSystem` (the Downed actions call in `Apply`, the depth function, the pressure dead band) and
  `WolfmedSyntheticHudSystem` (one CORE RESTORED line after the core block).

## M3 (2026-09-23)

Consequences keep mattering. Plan: `WOLFMED_DEATH_PLAN.md` §12 M3, §8, §3.6, §3.7 "stumps" (P19), P23, P27, OD15 as the
owner answered it (bands for crush internal bleed and electrical heart damage; sepsis is M2's).

**What was built.**
- **Organ reach (§8.1-3).** `WolfmedBodyPartComponent.OrganReach` (per damage type) on the torso (Piercing 10, Slash 18,
  Blunt 30, Heat 20, Shock 15), the head (8, 15, 15, 20, 15) and, restated, the IPC torso (OD10: penetrating torso hits
  reach the core). `WolfmedOrganThresholdSystem` (server, `_WF/Wolfmed/Body`) takes each hit's **Total** once: for every
  type over its line, organ damage = (hit - line) × the organ's `damageMultipliers` entry × its `selectionWeight` share
  of the working organs in the part × `wolfmed.organ_damage_scale`, capped at `wolfmed.organ_hit_cap` (5) per organ per
  hit. No roll: `hitChance`, `maxDamageFraction` and the profile chances are left to Onyx's roll, which still runs for a
  part without reach lines. The hand-off is one marked line in `OrganDamageSystem.OnPartDamageApplied` (inventory #13),
  after M1b's `WolfmedPartHitSystem.OnHit`, reading `total` (the M1b note). No new subscription.
- **A destroyed organ leaves the split.** An organ at 0 is skipped at once and is deleted a tick later, so the share
  is always over the organs that still work. An eviscerated or shot-out chest sends the next hits into what is left.
- **Graded bands (§8.5).** `WolfmedOrganComponent.ImpairedBelow` (0.5) and `Band` (OK / impaired / failed).
  - Lungs: a breathing input, (0.5 - health fraction) / 0.5 × `wolfmed.lung_damage_factor` (1), into the same drain as
    suffocation, hypoxia sub-source Lungs. Breathing reads `Laboured` ("short of breath"; examine and analyzer).
  - Heart: `impairedRegenFactor` 0.5 halves blood regeneration (one marked line at the bloodstream's regeneration step,
    `BloodstreamSystem` → `WolfmedLifeSystem.BloodRegenFactor`); a networked `PulseIrregular` for examine
    ("has an irregular pulse") and the analyzer.
  - Brain: below `wolfmed.consc_brain_down` (0.25) the new `injury` pressure (`wolfmed.injury_down_pressure` 0.75)
    holds the patient Downed, cause Brain ("Downed: head injury"), never Unconscious. Examine: "has unequal pupils and
    seems confused".
  - IPC core: the same pressure from the brainless branch of the life tick under `wolfmed.consc_core_down` (0.25), cause
    Core, never a shutdown by itself. HUD: "CORE INTEGRITY CRITICAL: MOBILITY LOST".
  - The analyzer: an "Organs:" vitals line naming every impaired or failed organ with its effect ("lungs impaired
    (short of breath); heart impaired (irregular pulse, blood slow to recover)"); the organ tab adds "impaired" or
    "failed" after the health.
- **Head blow (§3.6).** Blunt ≥ `wolfmed.head_knockout_blunt` (30) in one hit to the head (the whole hit, after armour)
  knocks the patient out for `wolfmed.head_knockout_seconds` (5), cause HeadBlow, checked inside the organ hand-off. It
  is a faint on M1a's machinery: `HeadBlowUntil`/`HeadBlowStart` beside the pain faint's fields, `WolfmedCauses.IsFaint`,
  `InFaint` (the pod keeps a knocked-out occupant anaesthetised and raises no Critical alarm), the countdown alert and
  "Coming round in N s", the analyzer's "FAINTED: head blow, N s", no heartbeat. A blow during the knockout never
  lengthens it. Machines are never knocked out (OD9).
- **OD15 bands.** Electrical: every hit's Shock (whole, after armour) over `wolfmed.electric_heart_from` (15) takes
  `wolfmed.electric_heart_factor` (0.2) per point off the heart, on flesh only (the parts the internal burn forms on),
  read off the broadcast `WolfmedPartDamageEvent`. The 35/50/75% rolls and their fields are gone; the spasm stays.
  Crush: `WolfmedRuleCrushInternalBleed` fires on every Blunt hit of 40 or more (was 40% from 30), a `_WF` rule edit.
  The lodged-round chances stay (flavour).
- **Stumps (P19).** Marked YAML in `Body/Parts/base.yml`: `MajorLimb` Blunt 190 → 400, Heat 250 → 350; `MinorLimb`
  Blunt 150 → 270, Heat 230 → 320. Each is 100 over the highest sever threshold of the limbs that share it (leg Blunt
  300, arm/leg Heat 250, foot Blunt 170, foot Heat 220). And limbs now finish on Blunt 10
  (`dismembermentFinishingDamage`, a separate limb anchor in `parts.yml`; the head keeps the host's 50), so a club
  finishes a limb beaten past its threshold and it comes off with a bleeding stump.
- **Barotrauma (P23).** Marked edit: on a wound host the pressure damage has no origin, so routing lands it on a part
  by weight and M1b's ambient ceiling applies (`ClampToBodyCap`), not the victim's own doll pick.
- **Blast head (P27).** One marked line at the top of `AmputationSystem.HandlePartDamageApplied`: an explosion hit on a
  head does nothing there unless `wolfmed.blast_dismember_head` is on (`WolfmedBodyPartSystem.BlastMaySever`). That one
  place covers both the Onyx per-part explosion roll and an ordinary finishing hit from a blast share.

**Numbers.** `wolfmed.organ_damage_scale` 3.4, `organ_hit_cap` 5, `lung_damage_factor` 1, `consc_brain_down` 0.25,
`consc_core_down` 0.25, `injury_down_pressure` 0.75, `head_knockout_seconds` 5, `head_knockout_blunt` 30,
`electric_heart_factor` 0.2, `electric_heart_from` 15; organ `impairedBelow` 0.5, heart `impairedRegenFactor` 0.5;
crush internal bleed from Blunt 40; limb rungs above; limb Blunt finishing 10.

**Measured** (`WolfmedConsequencesTest`, the shipped values pinned).
- **Calibration, rifle round (Piercing 14) to an unarmoured chest**, organ health after each hit (of 15):

  | Hit | Lungs | Heart | Liver | Stomach | Kidneys |
  |---|---|---|---|---|---|
  | 1 | 13.12 | 14.06 | 13.44 | 14.27 | 14.34 |
  | 4 | 7.48 (impaired) | 11.24 | 8.76 | 12.08 | 12.36 |
  | 8 | 0 (failed) | 7.48 (impaired) | 2.52 | 9.16 | 9.72 |
  | 10 | 0 | 4.68 | 0 | 6.98 | 7.74 |
  | 12 | 0 | 0.06 | 0 | 3.40 | 4.48 |
  | 13 | 0 | 0 (failed: arrest) | 0 | 1.61 | 2.85 |

  Two bodies took identical damage hit for hit. A hit at the line (Piercing 10) reached nothing, and five rounds on a
  torso already holding its 250 did exactly what they did to a fresh one. In play the chest's other routes arrive
  first: destroyed lungs are real suffocation (hit 8), and each destroyed organ leaves its internal bleed.
- **Head:** a rifle round is (14 - 8) × 0.42 × the brain's 77% share × 3.4 = 6.6, capped at 5, so three rounds to the
  head destroy the brain (catastrophic brain injury), and the Downed rung (under 25%) is passed over. A Blunt-30 blow
  takes 4.5 off the brain and knocks out; four such blows destroy it.
- **IPC:** the core (54% of the chassis torso's organ weight) takes 3.08 a rifle round: under 25% (Downed, cause Core)
  on hit 4, core failure on hit 5, with the pump at 2.5 of 15. *[Playtest 3 correction: the core is 40 health and the
  pump 25 now; see "IPC core sizing" below.]*
- **Lungs at 30%:** breathing level 0.40; Downed (hypoxia, source Lungs) at 206 s and Unconscious at 247 s in station
  air (derived 207 and 248).
- **Head blow:** the patient was up again 5.8 s after a Blunt-30 blow, measured at half-second polls, with a second
  heavy blow 2 s in; a Blunt-29 blow knocked nobody out.
- **Heart impaired (40%):** 3 u of blood regenerated in 20 s against 6 u for a healthy heart.
- **Electrical:** Shock 35 to an arm took exactly 4 off the heart on three bodies; Shock 15 took nothing.
- **Stumps:** a bat (Blunt 15) took the arm off on hit 18 at 270 stored (sever 250, destruction 400), the leg on hit
  21 at 315 (300 / 400), the hand on hit 11 at 165 (150 / 270), the foot on hit 13 at 195 (170 / 270); a laser (Heat
  16) took the other arm off on hit 17 at 272 (250 / 350). Each left a bleeding stump on its parent.
- **Barotrauma:** 84 vacuum ticks on a body whose doll was on the left arm landed on all 10 parts, the left arm 7 to
  12 times and the torso 17 to 23 (three runs).

**Differs from the plan, and why.**
- **Scale 3.4, not 4.** The plan derived its calibration (lungs impaired after about 4 rifle rounds, heart failing
  after 13-14) with every organ's share fixed. Here a destroyed organ leaves the split, so the survivors take more:
  at 4 the heart failed on the 11th round, outside the test's 12-16. 3.4 gives lungs impaired on hit 4 and the heart
  failing on hit 13, each one hit inside its band. Keeping destroyed organs in the split would have needed their
  weights remembered after deletion; the concentrating rule is also the one that makes an eviscerated or shot-out
  chest dangerous.
- **The head blow uses M1a's faint machinery** (the orchestrator's instruction, which replaces the brief's "its own
  pressure/timer"): its own `HeadBlowUntil`/`HeadBlowStart` beside the pain faint's fields, and every faint reader
  (`IsFaint`, `InFaint`, the countdown, the timed help, the analyzer) covers it. The pain faint's re-arm, cooldown and
  painkiller rules do not apply: a concussion is not ended by an opiate, and the knockout never lengthens.
- **The timed faint text is prototype data** (`helpOutTimed`); playtest 2 had the pain faint's key hard-coded in the
  condition alert system.
- **Blast head veto in `AmputationSystem`, through `WolfmedBodyPartSystem`, not `WolfmedExplosionSystem`.** Confirmed:
  Onyx's per-part explosion roll raises no event `_WF` can cancel (inventory #15's "to confirm"). The candidate pick in
  `WoundDamageRoutingSystem` is not the only way a blast severs a head (a blast share can also be an ordinary finishing
  hit on a severable head), and `HandlePartDamageApplied` sees both. `AmputationSystem` is shared, so the check lives in
  the shared `WolfmedBodyPartSystem` rather than the server-only explosion system.
- **The regeneration seam (inventory #16) lands in M3, not M5.** The impaired heart halves blood regeneration, which
  needs exactly the line M5's radiation marrow route needs. `WolfmedLifeSystem.BloodRegenFactor` is the `_WF` end; M5
  multiplies its own factor in there.
- **Liver clearance and IPC pump cooling bands are not built.** Neither route exists yet: toxin clearance is M5 (which
  the plan says reads "the liver band") and chassis cooling is M4's core-heat route. `WolfmedOrganComponent.Band` and
  `ImpairedBelow` are what they read; the factor fields (`impairedClearanceFactor`, `impairedCoolingFactor`) belong
  with their consumers, so no inert tunable ships.
- **Electrical band per hit, on `WolfmedPartDamageEvent`.** The old roll answered the internal burn's lifecycle, which
  does not carry the hit's Shock. The band reads the broadcast per-hit seam on flesh parts (the parts the internal burn
  can form on). The heart slot is a constant; the behaviour's `organDamageChance`/`organDamage`/`organSlot` are removed.
- **Blunt finishing 10 on limbs.** The plan raises the destruction rungs; alone that leaves clubs (10-20 Blunt) unable
  to finish a severable limb against Onyx's Blunt 50, so they would pound on to the new rung and still destroy it. The
  head keeps 50 (its own anchor).
- **The ambient ceilings follow the rungs.** M1b's per-part ceiling is 0.8 × the lowest Destructible trigger, which is
  now Slash (210 limbs, 180 hands and feet): arm and leg 168 (was 152), hand and foot 144 (was 120), head 400 as before.
  Fire still cannot destroy or sever a limb (Heat 168 of a 250 sever threshold, 350 destruction).
- **Diona limbs** (never severable, `amputationThresholds: {}`) now need Blunt 400 / Heat 350 / Slash 210 to be
  destroyed, like any organic limb.
- **IPC parts** keep their own Destructible (Blunt 190, Slash 210, no ash; `PartIPCBase`), so an IPC limb is still
  destroyed rather than severed by Blunt: P19 is about bleeding stumps and the plan's edit is to `Body/Parts/base.yml`.
- **The pod closes up after a cut-short procedure** (below). Not in the plan; the test that exposed it is red otherwise.
- **New analyzer, examine and HUD text** the plan names only as effects: the "Organs:" vitals line, "impaired"/"failed"
  in the organ tab, "has an irregular pulse", "has unequal pupils and seems confused", Breathing "laboured: lungs
  damaged", the HUD's "CORE INTEGRITY CRITICAL: MOBILITY LOST" (a fixed line; the HUD line takes no word).

**Test migration.**
- `WolfmedOrganTest`: the T-ORG-CAP torso declares an empty `organReach`, since Onyx's roll runs only on a part
  without reach lines; new `OrganDamageFollowsTheReachLineTest` (the line, the formula, the per-hit cap).
- `WolfmedBluntWoundTest.CrushCanBleedInternallyTest`: the band, every Blunt 40 torso blow bleeds inside and no Blunt 35
  crush does, instead of "at least one in 24 at 40%".
- `WolfmedBurnWoundTest.ShockBurnsInsideAndSpasmsTest`: the heart has already lost the band (at least 5) to the Shock-40
  hit; no roll to drive by hand.
- `WolfmedAmputationTest`: comments only (the new rungs; limbs finish on Blunt 10). No assertion moved.
- `WolfmedExplosionTest`: unchanged. `BlastHeadTest` covers the veto.
- The M1b ceiling tests follow the new rungs: `AmbientCeilingTest` (an arm holds 168, not 152; the crossing tick starts
  at 166, not 150; the admin command destroys a limb with Blunt 410, over the 400 rung), `DamageCommandPassesTheAmbientCeilingTest`
  (168), `DionaLimbIsDestroyedNotSeveredTest` (Slash 215 over the unchanged Slash rung, instead of Blunt 195), comments
  in `WolfmedPlaytestTwoTest` and `WolfmedSpeciesSpawnTest`.
- `WolfmedPlaytestFixesTest.AutofixRunsOnceAndThenOnlyForSomethingNewTest`: no change to the test; the pod fix below.
- Tests whose head blow is now a knockout: `PainShockNoArrestTest` and `WolfmedPainTest.PainShockStunsAtThresholdTest`
  put their second Blunt 40 / 60 on an arm instead of the head (a knocked-out body is Critical and takes no stun), and
  `DamageTotalsNeverCritAWoundHostTest` aims its 250 and 350 Blunt at the torso (routed to a random part, it could land
  on the head and knock the patient out, which is the blow's doing, not a damage total's).
- `WolfmedBluntWoundTest.ConcussionFadesOnlyWithTimeTest`: its Blunt-25 head hit now also takes 3 off the brain, and
  a brain under 90% slurs on its own; the test puts the brain back to full so it still reads the wound's stages alone.
- `WolfmedCrawlingActionsTest.CallForHelpTest`: station air, and the call waits 3 s after the fall. Going Downed stuns
  for about 2 s, and a stun stutters (a Goob edit to `TryStun`), which mangled "airlock" in the shouted line at
  random: the test passed alone at the base and failed alone on M3, whose extra random draws (barotrauma's part pick)
  moved the stutter onto the word.

**Found on the way, and fixed: the pod left incisions open.** `AutofixRunsOnceAndThenOnlyForSomethingNewTest` (Blunt 60
on an arm) failed about half its runs before M3 (M1a D traced it to the crush rule's 40% internal bleed). The band makes
the bleed certain, so it failed every run, which let it be traced. Every wound surgery ends with a seal step that closes
the incision its requirement opened, but when the problem it treats goes away before that step (the internal bleed
stopped, the bone mended, the tissue repaired) the surgery stops being valid, the pod completes it without the seal,
and the incision stays open. The next procedure on the part then found the open incision as its bleeding wound and
stalled on it, and the run ended with the arm open. Now a procedure cut short that way queues `SurgeryCloseIncision`
straight after itself (`AutodocSystem.FinishStep`, the same `TryQueueClosure` an abandoned procedure uses). The test
passes alone and in the full run. A closure the pod adds this way (or after an abandoned procedure) is marked a
continuation (`AutodocQueued.Continuation`) and takes no fresh anaesthetic: the pod tops up at every procedure while
the relief has under 20 s left, so two extra closures pushed a third dose in `LongQueueDosesOnceAndWakesThePatientTest`
(45 u against its 30).

**Found, not changed.** `SurgeryStopBleeding` on a bleeding crush wound still stalls after three clamps: the stall guard
compares severities, not bleed rates, so a clamp that is lowering the rate reads as no progress. The pod abandons it,
closes up and goes on; the wound is later repaired by tending. Worth a look with the autodoc.
*[M6: fixed. The signature carries each wound's bleeding severity, which only treatment lowers.]*

**For the owner's playtest.** Three rifle rounds to the head are catastrophic brain injury, and five to an IPC's torso
are core failure. Both follow from the plan's reach lines and the calibration scale; if either is too quick, the head's
and the IPC torso's `organReach` (data in `parts.yml` and `species_parts.yml`) or `wolfmed.organ_hit_cap` are the knobs.

**Tests.** Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 439 total, 433 passed, 0 failed,
6 skipped (dirty-disposed: `SlipOpensOneSmallWoundTest`, `CutClothingUnblocksTheProcedureTest`,
`VisualStateFollowsTheLidTest`, `PodChargesAgainAfterAFailedShockTest`, `AutofixStopsReplanningABodyItIsNotChangingTest`,
`FaintedOccupantIsAnaesthetisedTest`); each skipped test passes alone. Two tests failed once each in earlier full runs
and pass alone and in the final run: `OxygenScenarioTest` (M1b saw the same) and `HeartbeatTracksLocalPlayerCritTest`.
Mid-session another worktree rebuilt the shared RobustToolbox, and a run failed to load types until this project was
rebuilt; the final run is on a fresh build.

## M4 (2026-09-24)

Species and IPC death. Plan: `WOLFMED_DEATH_PLAN.md` §12 M4, §9, §3.11, §11 (M4 rows); the owner's answers of 2026-09-24:
OD16 (a) parity with Synth **mechanical**, OD10 (b) (the core-heat route; core repair shipped in M2), OD3 (b) (Succumb in
thermal shutdown). Branch `Wolfmed-m4`, from `7baf456a14`.

**The measurement, first** (`IpcFireScenarioTest`'s trace; the plan asked for the core-heat rate to be set against it).
- Under M3 the overheat pulse (20 Heat a second from 383 K, 30 after the IPC's ×1.5 Heat modifier) crossed the torso's Heat
  reach line (20) and went into the core: an IPC's core was destroyed about 30 s into a 10-stack fire, by a hidden second
  route and before any thermal shutdown could exist.
- The chassis in a 10-stack fire in station air: 501 K at 4 s, a peak of about 1140 K at 48 s, then down as the stacks
  fade. Above 500 K for about 125 s untreated, 108 s when put out at 60 s, 88 s at 40 s and 56 s at 20 s.
- So a chassis-temperature line (the plan's "once the chassis passes 500 K") cannot give the plan's target: untreated and
  put out at a minute differ by 15% in time over the line, and the chassis passes 500 K four seconds after ignition, before
  the IPC is even Downed, so "the IPC can pat itself out while still Downed, before 500 K" was impossible.

**What was built.**
- **The core-heat route** (`WolfmedOverheatSystem`, plan §3.11). The positronic core has its own temperature
  (`WolfmedCoreHeatComponent`, shared and networked, ensured on every mechanical wound host). Each second it closes
  `wolfmed.ipc_core_heat_soak` (0.02) of its gap to the chassis, and a working, powered coolant pump takes
  `wolfmed.ipc_pump_cooling` (5 K/s) off it, down to body temperature. The pump's M3 band finally has its consumer: an
  impaired pump cools at its organ's `impairedCoolingFactor` (0.5), a destroyed, missing or unpowered one not at all.
  - Over `wolfmed.ipc_core_heat_k` (500 K) the core loses `wolfmed.ipc_core_heat_rate` (0.2) health a second for every
    100 K it is over the line, and the chassis is in **thermal shutdown**: the `coreheat` pressure, Unconscious (Critical),
    cause `CoreHeat` (tie order Arrest > CoreHeat > Shutdown), Dying. Under `wolfmed.ipc_core_heat_wake_k` (450 K) it
    ends and the chassis comes round on consciousness's own inputs (Downed by pain, in a fire). A core at 0 is core failure
    through the organ path, as before.
  - Steady state (derived): with a working pump the core passes its line only while the chassis holds over
    500 + 5 / 0.02 = 750 K; with an impaired pump 625 K; with no pump or no power, 500 K.
  - **Measured** (`IpcFireScenarioTest`, shipped values): Downed by pain at 6 s, CORE TEMP CRITICAL on the readout before
    the shutdown, thermal shutdown at 41 s, core failure at 102 s untreated. Put out at 60 s: thermal shutdown from 42 s to
    125 s, the core left at 2.8 of 15 (19%: Downed, cause Core, core repair needed). Patted out from 12 s: the core peaked
    at 486 K and never shut down.
- **The pulse no longer reaches the organs.** `WolfmedOrganThresholdSystem.WithoutOrganReach` wraps the pulse, so heat
  reaches the core only by the core-heat route (principle C). The pulse still burns the parts, which is what Downs a
  burning chassis by pain; it never kills (`OverheatBurnsInsteadOfKillingTest`).
- **Succumb and Last Words in thermal shutdown** (OD3 (b), plan §5.4). `IsDying` is arrest or thermal shutdown; the
  overheat system grants and revokes the actions by direct call, as arrest does. Succumb keeps its order: the core to 0
  where it sits, `Kill`, the arrest ended, the thermal shutdown ended on the corpse, a returnable ghost. The dialog: "Your
  core is overheating. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a
  technician can bring you back with core repair and the restart button." The `ghost` command in thermal shutdown opens it.
  Wait as a ghost is never offered in thermal shutdown (Succumb covers it).
- **What people read.** The readout's banner is the cause's line, "THERMAL SHUTDOWN: CORE DAMAGE"; before it, a
  "CORE TEMP CRITICAL" fault from `wolfmed.ipc_core_heat_warn_k` (400 K, core or chassis); the SYSTEM block's CORE row is
  now the core's own temperature. The analyzer: "THERMAL SHUTDOWN: core overheating", "Temperature: core N K, chassis M K"
  while either is over the warning line, and the route "core overheating (put the fire out, cool the chassis)". Examine, at
  any range: "is smoking, too hot to touch". The alert `WolfmedOutCoreHeat`. Coming out: "Core temperature back under the
  line. Systems online.", or "… Still down: {cause}." when something else holds the chassis. The card's bar (for a
  player who turned the readout off) is what is left of the core.
- **Organ data (plan §9.2).** Marked parent edits: A′ lungs (Feroxi, Harpy, Goblin, Hydrakin); B hearts (`OrganAnimalHeart`,
  `OrganArachnidHeart`) and the arachnid's `OrganAnimalLungs`; C brains, hearts and lungs (protogen, which covers every
  Proto- subspecies and ProtoThaven; skrell; the diona's nymph brain and lungs; the slime's `SentientSlimeCore` as its brain,
  and its gas sacs). **A balance change to announce:** moths, reptilians, vulpkanin, canines, felionoids, tajaran, rodentia,
  arachnids (and anything else on the animal heart) can now arrest from heart trauma, and the group C species arrest, run a
  brain clock and die when the brain is taken.
- **Circulatory collapse** (OD16, C′). `WolfmedConsciousnessComponent.Heartless` (networked) is set when an arrest starts on
  a body whose prototype has no heart in any slot (Diona, the slimes). The arrest is the same state with the same triggers
  and routes; its words are the `CirculatoryCollapse` cause prototype's (`WolfmedCauses.PrototypeId`): "Circulatory
  collapse: blood loss", "Your circulation has collapsed.", "Your circulation returns.", the Succumb dialog, the analyzer's
  "CIRCULATORY COLLAPSE: blood" and "Breathing: none: circulatory collapse", the alert `WolfmedOutCollapse` and the banner
  "YOUR CIRCULATION HAS COLLAPSED". A human whose heart is taken out is not heartless: that is arrest, cause heart.
- **A slime survives decapitation** (plan §9.3). Its head is marked `vital: false`, since its core is in the torso.
- **Group D wound hosts** (OD16). Shadekin and ProtoKin get `WoundHost` and `PainShockTarget` (ProtoKin also `LayingDown`
  for the crawl); they are not reparented, because `BaseMobSpeciesOrganic` would give a respirator to species built without
  lungs. For parity with the organic base: their passive regeneration is neutralised (D29), their Blunt gib rises 400 → 1500
  (D22, the body total is the sum of the parts), and their body-level Heat 1500 ash is removed: the living per-part ceilings
  sum past 1500, so a long fire would have burned the body away, which OD12 rules out.
- **Synth, the machine ladder** (OD16, the plan's M4-mechanical branch). The parts take the IPC wound profile
  (`WolfmedPartIpc` first in `PartSynth`'s parents; the torso and head keep the organic reach lines and caps), the ccu is
  the positronic core (`WolfmedOrganPositronicBrain`), the heart is the coolant pump (`WolfmedOrganIpcPump`), and the mob
  gets `WoundHost`, `PainShockTarget` and the restart button (`DeadStartupButton`). `IsMechanical` recognises the
  `SynthBattery`; `HasPower` reads its `Unpowered` flag, polled once a second by the shutdown system because the battery
  raises nothing another system may subscribe; the readout's power reads the cell in the battery organ slot. Its core sits
  in the head's brain slot, so it gets its own core repair on the head (`SurgeryRepairSynthCore`: wrench, multitool,
  welder, the same steps as the IPC's) and the organic brain repair is excluded on a synth.
- **The conformance test is strict** (plan §9.1). `WolfmedSpeciesConformanceTest.EverySpeciesConformsOrIsExcusedTest`: the
  `KnownGaps` array is gone. The checks are an enum (`WolfmedSpeciesCheck`); the deliberate differences are
  `wolfmedSpeciesException` prototypes (`Body/species_exceptions.yml`: the species id, whether it runs the machine ladder,
  the checks it is excused, the reason). The test fails, naming the species, on a failure not excused, on an excuse the
  species no longer needs, on a species on a ladder its exception does not name, and on an exception for a species that is
  not round-start.

**The species matrix, after M4.** 41 round-start species. 35 organic species conform with no exception at all. IPC and
Synth conform on the machine ladder, which their exceptions record. Diona, ProtoDionae, SlimePerson and ProtoSlimePerson
are excused one check, Heart: they have none and collapse instead (plan §9.3). Every gap §9.2 predicted and M1a's report
listed (98 across 32 species) is closed.

**Differs from the plan, and why.**
- **A core temperature, not the chassis's.** The plan triggers thermal shutdown on the chassis at 500 K. Measured, that
  cannot separate an untreated fire from one put out at a minute, and it leaves no time to pat the fire out (above). The
  core soaks up the chassis's heat instead; the plan itself names "chassis and core temperature" for the medic and a core
  temperature row for the HUD. The CVar names and lines (500 K, 450 K) are the plan's, read on the core.
- **The rate scales with how far over the line the core is** (0.2 HP/s per 100 K), not a flat HP/s. Simulated on the
  measured trace, a flat rate leaves 78% between the put-out and the untreated core loss, a proportional one 63%, so both
  outcomes keep a margin of about 20%. Hotter cooks faster, which the readout's CORE row shows.
- **The pump cools, and needs power.** Nothing cooled a chassis before; the plan's pump band ("cooling × 0.5, overheats
  sooner") needed a consumer (M3 left `impairedCoolingFactor` for it). A pump with no power behind it does not pump, so a
  chassis whose cell is out while it burns soaks its heat unopposed.
- **The overheat pulse reaches no organ** (above). Not in the plan; the plan's single-route principle needs it.
- **The lung check applies to a body that breathes.** The M1a report flagged a body with no lungs; Shadekin and ProtoKin
  are built with no lungs and no respirator, deliberately, so that is not a gap. A body with a respirator and no lungs
  still fails.
- **The exception list names the machine ladder.** IPC and Synth fail no check, but their entries say they run the
  machine ladder, and the test holds each species to the ladder its entry names (plan §9.3 lists both).
- **Synth core repair and the restart button.** Not in the plan's Synth branch; without them a synth's core failure was
  permanent, against the owner's "no one is unrevivable".
- **Group D parity edits** (passive regeneration, the gib line, the Heat ash) are not in the plan; each applies a rule an
  earlier package set for every organic body (D29, D22, OD12).
- **Numbered for the parallel M5:** `WolfmedCause.CoreHeat` is 20 and `WolfmedRoutes.CoreHeat` is `1 << 15`, so M5's new
  causes and routes can take 15 upwards and bit 10 upwards without a clash.
- **`wolfmed-vitals-cause-shutdown-mechanical` ("shutdown").** A shutdown as a blocker ("THERMAL SHUTDOWN: core overheating
  (also: shutdown)") read "(also: unknown cause)".

**Numbers.** `wolfmed.ipc_core_heat_k` 500, `ipc_core_heat_wake_k` 450, `ipc_core_heat_rate` 0.2 (per 100 K over),
`ipc_core_heat_soak` 0.02, `ipc_pump_cooling` 5, `ipc_core_heat_warn_k` 400; pump `impairedCoolingFactor` 0.5.

**Test migration.**
- `WolfmedSpeciesConformanceTest`: report mode to strict (above).
- `WolfmedOverheatTest.OverheatBurnsInsteadOfKillingTest`: unchanged assertions (the pulse never kills); its summary says
  the core-heat route is the lethal one now.
- `WolfmedSpeciesSpawnTest.ProtogenIsAWoundHostTest`: flipped as its comment asked: the protogen brain, heart and lungs
  carry `OrganDamage`, the rest still do not.
- `WolfmedSyntheticHudTest.SystemBlockCarriesSensorsAndCoreTemperatureTest`: the CORE row is the core's temperature, set
  through the core-heat route.
- `WolfmedSpeciesProfileTest`: nothing needed changing.
- `Scenarios/WolfmedCauseScenarioTest.OverlappingCausesTest`: the IPC branch deferred from M1a (cell pulled in thermal
  shutdown, then cooled).

**Tests.** New: `Scenarios/WolfmedIpcDeathTest.cs` (`IpcFireScenarioTest`, `ThermalShutdownSuccumbTest`),
`Scenarios/WolfmedSpeciesTest.cs` (`SpeciesArrestTest`, `SynthBranchTest`). Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`), final run: 458 total, 451 passed, 0 failed, 7 skipped (all autodoc or blast fixtures, disposed dirty); each skipped test passes alone. An earlier full run had the same counts with a different set of 7 skips, each also passing alone. Measured in the final run: Downed 5.2 s, thermal shutdown 41.3 s, core failure 102.3 s; put out at 60 s the core was left at 3.1 of 15; patted out, the core peaked at 484 K.

**For the M5 merge.**
- `WolfmedCause`, `WolfmedCauseFlags`, `WolfmedCauses.Priority`, `WolfmedRoutes` and consciousness's `PressureCause` switch
  each gained one M4 entry at the end (CoreHeat); M5's entries go beside them.
- Synth is mechanical now, but its damage container is still HardLight's `Synth` (Brute, Burn, Toxin, Genetic, Bloodloss),
  so it takes Poison: M5's toxin route should skip `IsMechanical` bodies, as the plan says for IPCs. Synth also
  regenerates its fluid through HardLight's `SynthBloodstream`, not the bloodstream step M5's radiation factor hooks.
- Diona, the slimes, the protogens and skrell run a brain clock now, so M5's toxin and heat drains reach them.

**Found, not changed.**
- The autodoc knows neither `SurgeryRepairCore` (M2) nor `SurgeryRepairSynthCore`: a pod will not repair a machine's core.
  *[M6: fixed, both on the neuro disk.]*
- A synth's nanite self-repair (`SynthBloodstream`) heals Brute and Burn over time while it has fluid and hunger, which no
  other wound host does; it is HardLight's species feature and was left.
- The plan's "temperature bar" on the readout is the CORE row's number; no graphic bar was drawn.
- A synth's `SynthBlood` is its fluid on the machine ladder, so losing it reads as the oil cause ("hydraulic pressure
  low") at the same 50% and 35% lines. A synth's own fire has not been measured: its chassis has a thermal regulator and a
  lower specific heat than an IPC's, and the core-heat route applies to it unchanged.
- The Release YAML linter was not run: a Release build writes into the RobustToolbox junction this worktree shares with
  the main checkout, which the brief forbids. Every touched YAML file was parsed, (kind, id) pairs checked for duplicates,
  and every prototype loads in the integration server.

**Art debt.** `WolfmedOutCoreHeat` reuses the borg critical icon, `WolfmedOutCollapse` the human dead icon.

## M5 (2026-09-23)

The remaining causes: toxins, radiation, cold and heat. Plan: `WOLFMED_DEATH_PLAN.md` §12 M5, §3.8-3.10, P21, the §11
rows marked M5; the owner's OD13 (a) of 2026-09-24 (toxins and radiation get lethal routes, liver clearance is the one
passive-healing exception, infection and sepsis no longer deal Poison); the fire grace rules as revised. Branch
`Wolfmed-m5`, from `7baf456a14`, in parallel with M4.

**The measurement, first** (`WolfmedTemperatureTest.ColdRoomMeasurementTest`, run on the base before any M5 code). A
naked human's surface temperature, the `TemperatureComponent` reading the atmosphere and the regulator move:
- In air it settles about 22 K over the air within a minute: 293 K air holds 308 K, 273 K holds 295 K, 253 K holds
  275 K, 233 K holds 254 K, 213 K holds 235 K, 173 K holds 195 K. The maps' freezers are 235 K air (about 256 K).
- **In space it reaches 16 K within a minute** (224 K two seconds after spawning, 96 K at 12 s, 49 K at 22 s). The
  default test map behaves the same: it has no atmosphere, so every test body on it is in space.
- A 10-stack fire in station air peaks at 832-836 K at 30-35 s and burns out at 100 s (about 440 K); the surface is
  under 318 K 30 s later. **Put out at its hottest (30 s), it takes 55 s** to get under 318 K.

Read straight off that surface, the plan's lines (for a human 290, 275 and 262 K, below) would stop the heart of
anyone in space within about 3 s, and of a cook in a 235 K freezer within about 35 s: the surface reaches equilibrium in
a minute, so the air alone would decide the state. The plan assumed a core that takes time to cool (§3.10 names a "core
temperature" and asks for this measurement before the lines are picked). Hence the core model below.

**What was built.**
- **Toxins (§3.8, OD13).** `WolfmedToxinSystem` (server). The load is the body's systemic Poison
  (`SystemicDamageComponent`), never wound damage. Consciousness reads it as cause Toxin against two lines: Downed at
  `wolfmed.consc_toxin_down` 60, a toxic coma at `wolfmed.consc_toxin_out` 120 (Unconscious, breathing). The ordinary 0.9
  hysteresis puts waking under 108 and standing under 54. In the coma the brain drains at 1/`wolfmed.brain_toxin_seconds`
  (600) a second, the sepsis shape, and the heart stops through the oxygen trigger, named "toxin" ("CARDIAC ARREST:
  poisoning"). Clearance: a working liver (the organ carrying `LiverComponent`) takes `wolfmed.toxin_clearance` 0.1
  Poison a second out in the life tick; an impaired one × its `impairedClearanceFactor` (new organ field, 0.5 on
  `WolfmedOrganLiver`); a failed or missing one nothing. A liver with no Wolfmed organ data counts as working.
- **Pools separated (OD13).** A spreading infection and sepsis deal no Poison. The profile's `spreadingPoisonPerMinute`
  and `sepsisPoisonPerMinute` are removed rather than left inert. Fever stays; sepsis kills through its own brain drain
  (M2), so a septic patient is not driven twice.
- **Radiation (§3.9).** `WolfmedRadiationSystem` (server). Systemic Radiation at `wolfmed.rad_marrow_stop` 40 stops blood
  regeneration: a × 0 inside `WolfmedLifeSystem.BloodRegenFactor`, the seam M3 built on the bloodstream's marked line. At
  `wolfmed.rad_marrow_bleed` 100 the marrow also costs `wolfmed.rad_marrow_rate` 0.1 u/s, split out of the blood solution
  in the life tick (no puddle) and counted in the analyzer's trend and transfusion numbers. At `wolfmed.consc_rad_down`
  80 the patient is Downed, cause Radiation ("radiation sickness"), never Unconscious; it kills through the blood route.
  Machines: no marrow and no radiation cause. A chassis does carry Radiation (its container takes it; the test's IPC
  kept 50 of 100 after its modifier); it stays the chassis's slowdown and pain.
- **Cold and heat (§3.10).** `WolfmedBodyTemperatureSystem` (server, its own one-second update).
  - **Lines per species** from the `TemperatureComponent` damage thresholds (a protecting container's while inside
    one): hypothermic (Downed) under cold + `wolfmed.hypothermia_down_offset` 30 K; Unconscious, breathing, under
    + `hypothermia_out_offset` 15 K; the heart stops, cause "cold", under + `hypothermia_arrest_offset` 2 K. Heat
    exhaustion (Downed) over heat − `wolfmed.hyperthermia_down_offset` 7 K; heat stroke (Unconscious) over the heat
    threshold, with a brain drain of 1/`wolfmed.hyperthermia_brain_seconds` (300) a second and an arrest through the
    oxygen trigger named "heat". A human: 290, 275 and 262 K; 318 and 325 K.
  - **The core temperature** the lines read (deviation 1): it follows a warmer surface at once, and above the normal
    temperature it follows the surface down at once; below normal it closes the gap to a colder surface over
    `wolfmed.core_cooling_seconds` (900). A body starts with a normal core. A container that protects from cold damage
    (the cryo pod) holds it, so a cryo patient is neither hypothermic in the pod nor arrested on the way out (read
    from `ParentColdDamageThreshold`, which the pod's `ContainerTemperatureDamageThresholds` sets; no test drives a
    real pod).
  - **Fire grace**, exactly as revised: granted on catching fire unless the body already holds a heat cause; it holds
    while burning and `wolfmed.heat_fire_grace_seconds` after the first time the fire goes out; re-ignition never moves
    that end; it only delays entering a heat cause and never clears one; a new one comes only once the grace is over and
    the core is back under the heat exhaustion line. Default 60 s, not 30 (deviation 3).
  - An input is the core's offset from normal over the line's offset from normal (1 at the line), with the ordinary
    0.9 hysteresis, and nothing at or under `wolfmed.temperature_input_floor` (0.5) of the way: a body a couple of
    kelvin off normal in ordinary air gets no notch on the health doll and costs no polling.
  - Cold arrest keeps the brain's existing cold protection (× 0.1 on the drain and on tissue loss). The paddles and the
    pod refuse a core still under the arrest line ("Shock refused: core temperature 250 K. Rewarm above 262 K first.");
    a heart restarted there would stop again on the next tick (addition 5). A corpse's core keeps following its surface
    for that gate.
- **The causes.** `Toxin` 16, `Radiation` 17, `Cold` 18, `Heat` 19, in the plan's tie order (… Sedation > Toxin > Cold >
  Heat > Radiation > … Brain). Prototypes in `Consciousness/remaining_causes.yml`, alerts in `Alerts/remaining_alerts.yml`
  (stock toxin-gas and temperature icons, art debt), text in `remaining-causes.ftl`: "Downed: poisoned", "Unconscious:
  toxic coma", "Downed: radiation sickness", "Downed: hypothermic", "Unconscious: hypothermia", "Downed: heat exhaustion",
  "Unconscious: heat stroke", each with symptom, help, blocked forms and transition lines. Hypoxia gains the sources
  toxic coma and heat stroke; arrest gains cold, poisoning and heat stroke.
- **What the medic reads.** Analyzer: "Toxins: 72, high; liver clearing" (low / high / comatose, brain at risk; the liver
  clearing / impaired, clearing slowly / failed / missing), "Radiation: 120, marrow failing: blood not regenerating,
  losing 0.1 u/s" (or "marrow suppressed"), "Core temperature: 271 K, hypothermic" (or "overheating") while the core
  counts, and "Defib: refused: core 250 K, rewarm above 262 K first". Routes: "toxic coma (antitoxin)", "heat stroke (cool
  them, now)", "marrow failing (anti-radiation drugs, blood)", "still cooling (warm them)"; they also withdraw "wait as a
  ghost" and tell a waiting ghost. Examine, close up, while the cause holds the body: retching and sweating; grey and
  sickly, bruising at the gums; cold and stiff to the touch; skin hot and dry.
- **P21, acid residue.** `WolfmedChemicalBurnSystem` marks the part and the wound the residue is biting through
  (`ResidueTarget`) while it deals its damage, and `WolfmedWoundRuleSystem` puts that damage into that chemical burn
  instead of a plain burn. The residue deepens its own burn, whose stages bite harder, until it is washed off.
- **Guidebook.** Infection no longer "takes toxin damage" (Wounds.xml, WoundTreatment.xml, the treatment card); one
  paragraph on the four causes.

**Numbers.** `wolfmed.consc_toxin_down` 60, `consc_toxin_out` 120, `brain_toxin_seconds` 600, `toxin_clearance` 0.1,
`rad_marrow_stop` 40, `rad_marrow_bleed` 100, `rad_marrow_rate` 0.1, `consc_rad_down` 80, `hypothermia_down_offset` 30,
`hypothermia_out_offset` 15, `hypothermia_arrest_offset` 2, `hyperthermia_down_offset` 7, `hyperthermia_brain_seconds`
300, `heat_fire_grace_seconds` 60, and new: `core_cooling_seconds` 900, `temperature_line_max_share` 0.6,
`temperature_input_floor` 0.5; liver `impairedClearanceFactor` 0.5.

**Measured** (the M5 tests, shipped values pinned).
- **Poison.** 60: Downed at once. 130 with no liver: Unconscious and breathing, the brain drained 0.100 in 60 s, and the
  heart stopped at 511 s, cause "toxin" (derived 510 s). A working liver cleared 6.00 in 60 s, an impaired one 3.00.
  130 on the liver alone: awake at 221 s (load 107.9; derived 220 s), under the Downed line at 761 s (derived 760 s).
  15 u of dylovene at 130: awake at 20 s (load 107.0), standing at 67 s (load 53.3).
- **Radiation.** Sixty seconds at 90% blood: no radiation +21.0 u, radiation 40 +0.0 u, radiation 100 −5.8 u (derived
  −6.0 u), the chassis at 100 +0.0 u of oil. 15 u of hyronalin took the dose under 40 in 60 s, and the blood then came
  back 7.0 u in 20 s. Derived from those rates for a human at radiation 100 from full blood: Downed by blood at 25 min,
  Unconscious at 32.5 min, arrest at 35 min; Downed by radiation sickness at once.
- **Hypothermia.** The 235 K freezer (surface 256 K), fast-forwarded: Downed at 419 s, Unconscious at 943 s, arrest
  (cause "cold") at 1980 s, each the second the core's cooling derives; the arrested brain drained at exactly a tenth of
  the arrest rate; the paddles refused until the core was rewarmed, then shocked; a patient rewarmed to 305 K woke the
  next second and stood. **Space, real time** (`SpaceColdSmokeTest`): hypothermic at 69 s, Unconscious from cold at
  126 s, cold arrest at 172 s (derived from the recorded surface 75, 125 and 171 s). The re-run measurement: in 173 K air
  Downed at about 195 s, in 213 K air just after five minutes; 233 K and warmer air do not Down a naked human within five minutes.
- **Heat stroke.** At 330 K the brain drained 0.200 in 60 s and the heart stopped at 256 s, cause "heat" (derived 255 s).
  The heat cause came 61 s after a fire went out, at the shipped 60 s grace; re-igniting 15 s into the grace moved nothing;
  a heat stroke caught fire and stayed; a still-hot body's second fire got no grace, a cooled one's did.
- **Acid residue.** Ten residue ticks: the chemical burn 20 → 35, the plain burn on the same torso 30 → 30.
- **Species lines.** Reptilian (cold threshold 285 K, normal 310 K): 300.1 / 292.5 / 286.0 K. Avali (normal 261 K, heat
  threshold 310 K): 303 / 310 K, fever ceiling 282 K. Human, reptilian and avali stand in station air.

**Differs from the plan, and why.**
1. **A core temperature, not the surface reading.** See the measurement. `wolfmed.core_cooling_seconds` 900 gives space
   about a minute before hypothermia, two before unconsciousness and three before the cold arrest, close to the blood
   arrest barotrauma gives (M1a playtest 1: about 205 s) but on a route that protects the brain; and a naked cook seven
   minutes in a freezer before going down. Warming follows the surface at once, so rewarming wakes a patient as soon as
   they are somewhere warm; heat reads the surface as it is, so the measured fire numbers hold.
2. **The offsets shrink for a species whose normal temperature is near a threshold** (`wolfmed.temperature_line_max_share`
   0.6: no Downed line further than 60% of the way from the threshold towards normal, the other lines scaled alike).
   At the plan's fixed offsets a reptilian, an asakim or a Proto reptile (cold threshold 285 K, normal 310 K) would be
   hypothermic at its own normal temperature (Downed under 315 K). A human's lines are exactly the plan's.
3. **Fire grace 60 s, not 30.** A fire that burns out leaves a surface under the heat exhaustion line 30 s later; one put
   out at its hottest takes 55 s. At 30 s, putting someone out promptly, the right first aid, earned them heat stroke 30 s
   later. The rules are exactly as revised; the test pins the shipped 60 and checks the grace ends on it.
4. **A fever never Downs, for every species.** The infection's fever climbs to the profile's 313 K, which for a species
   with a normal temperature of 261 K (avali, some protogen subspecies; heat threshold 310 K) is heat stroke. It now stops
   at the point where the species' heat starts to count (the input floor), which leaves the human fever at 313 K.
5. **Additions the plan implies but does not name:** the TooCold defib refusal; heat stroke's arrest named "heat" (the
   plan's list of arrest causes has cold and toxin); the analyzer's core temperature line; four routes (toxic coma, heat
   stroke, marrow, still cooling), so "wait as a ghost" is withdrawn for them as §5.4 asks; examine signs for each cause.
6. **Enum values leave room for M4.** `WolfmedCause` 15 and `WolfmedRoutes` bit 10 are unused, for M4's core heat, so the
   parallel branches do not collide on a number. Sources: hypoxia `Toxin` 10 and `Heat` 11; arrest `ArrestCold` 30,
   `ArrestToxin` 31, `ArrestHeat` 32.
7. **No marked edit.** Inventory #16 landed in M3; the radiation factor multiplies into the same method.
8. **The cold and heat inputs tick on their own one-second update, not the life tick**, because the surface they follow
   moves only in real time; `WolfmedBodyTemperatureSystem.Tick` is the test seam. Toxin clearance and the marrow loss run
   in the life tick (`WolfmedScenario.Advance` fast-forwards them).
9. **Tests the plan does not list:** `ColdRoomMeasurementTest` (the opening measurement, records only),
   `SpaceColdSmokeTest` (the milestone's real-time smoke test), `SpeciesLinesTest`.

**Test migration.**
- `WolfmedInfectionTest`: the spreading stage runs a fever and deals no Poison; `SepsisPoisonsAndAntibioticsClearItTest`
  is now `SepsisShowsAndAntibioticsClearItTest` and asserts no Poison.
- `WolfmedBurnWoundTest.ChemicalBurnsTickUntilWashedTest`: unchanged and green; `AcidResidueTest` carries P21's assertion.
- `WolfmedBleedingLifecycleTest`: reads no regeneration; nothing to migrate (the seam was M3's).
- `WolfmedLocaleCoverageTest`: the route and dormant-route families pick up the four new routes by themselves.

**Found on the way.**
- **A test's own output can vanish.** `TestContext.Out` written inside a server callback, while other tests run in
  parallel, sometimes lands in no test's output. The M5 tests collect their lines and write them from a `[TearDown]`.
- **The default test map is space for temperature.** Every existing test body on it cools like a spaced crewman. The
  full filter stayed green: none holds a body there long enough to reach the hypothermia line (about a minute).

**Tests.** New: `Scenarios/WolfmedRemainingCausesTest.cs` (`ToxinScenarioTest`, `RadiationScenarioTest`,
`SepsisNotToxinTest`, `AcidResidueTest`), `Scenarios/WolfmedTemperatureTest.cs` (`ColdRoomMeasurementTest`,
`HypothermiaScenarioTest`, `HeatStrokeScenarioTest`, `SpeciesLinesTest`, `SpaceColdSmokeTest`).
Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 463 total, 456 passed, 0 failed, 7 skipped
(dirty-disposed: `AcidResidueTest`, `AutofixStopsReplanningABodyItIsNotChangingTest`,
`BrainDeadOccupantIsOperatedOnWithoutHoldingTest`, `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`,
`PainkillerPenTest("WolfmedAnalgesicPen",Weak)`, `StalledProcedureIsAbandonedAndNeverReplannedTest`,
`VisualStateFollowsTheLidTest`); each passes alone. The run before the 60 s grace and the fever ceiling: 462 total,
454 passed, 0 failed, 8 skipped, each passing alone.

**For the merge with M4.**
- `WolfmedCause` 15 and `WolfmedRoutes` bit 10 are free for CoreHeat. Both branches append to the tie order array, the
  arrest source switches in `WolfmedLifeSystem.ArrestSource` / `GetRestartMemory` and `WolfmedConsciousnessSystem.GetSource`,
  `WolfmedOrganComponent` (M5 `ImpairedClearanceFactor` beside M4's `ImpairedCoolingFactor`), `organs.yml` and the CVars.
- Mechanical bodies get none of the M5 inputs (`WolfmedShutdownSystem.IsMechanical`); a Synth made mechanical in M4 is
  covered by the same check. The IPC core-heat route is M4's and reads nothing here.
- Species conformance (M4) does not check temperature lines; `SpeciesLinesTest` covers the three species it names.

**For the owner's playtest.** A chemistry poisoning (dylovene wakes a toxic coma in about 20 s), a radiation leak (at 100
the blood starts going; 40 already stops it coming back), a freezer (naked: down in 7 minutes, out in 16, the heart at
33), space (down in about a minute, the heart about 3 minutes in, cold arrest protects the brain: rewarm, then shock), a
hot room, and a fire (no heat stroke, whether it burns out or is put out). Knobs: `wolfmed.core_cooling_seconds`, the
offsets, `wolfmed.heat_fire_grace_seconds`.

## M6 (2026-09-24)

Leftovers. Plan: `WOLFMED_DEATH_PLAN.md` §12 M6, §11 (M6 rows), P17, P25, P30, the rundown's Appendix A; the owner's
OD18 (a) of 2026-09-24. Also the small leftovers earlier milestones flagged: the pod and a machine's core (M4), a
synth's Poison (M4), Onyx's uncalled `TryApplyLethalDamage` (M2), the pod's stop-bleeding progress check (M3). Branch
`Wolfmed-m6`, from `a76ecd98f5`.

**What was built.**
- **Being hit interrupts (OD18, P17).** `WolfmedDoAfterInterruptSystem` (server) is called once per hit from
  `WolfmedPartHitSystem.OnHit`, the dispatcher's existing hand-off. A hit whose Total (after armour, stored or not)
  is `wolfmed.doafter_interrupt_damage` (10) or more cancels the do-afters the hit body is performing: treatment
  (`HealingDoAfterEvent`, the tourniquet) and surgery steps (`SurgeryDoAfterEvent`) always, and anything that asks to
  break on damage (cuffing, prying, injecting, Wolfmed's splint, embedded removal, cautery and joint relocation, which
  all asked for it and never got it on a wound host). The hit carries the caller's `interruptsDoAfters` on
  `PartDamageAppliedEvent.InterruptsDoAfters` (one marked Onyx field), so the ticks upstream already marks as not
  interrupting (fire, temperature, barotrauma, the bloodstream) never count; systemic damage (Bloodloss, Poison) lands
  on no part and never counts. The IPC overheat pulse now passes false like fire; an explosion passes true, as it does
  upstream (it passed false before).
- **The routed pass keeps the caller's arguments (P25).** `BeforeDamageChangedEvent` carries `IgnoreResistances`,
  `InterruptsDoAfters` and `PartMultiplier` (one marked upstream line at construction, one at the record). Routing
  passes the first two to its routed pass instead of the defaults, skips the part's armour (`PartDamageModifyEvent`)
  when resistances are ignored, and scales the localized damage that reaches the part by Shitmed's part multiplier,
  once, before the armour.
- **Fracture grade (P30).** A fracture is created at the trauma that graded it (the hit plus the part's earlier Blunt ×
  `accumulationMultiplier`), not at the hit alone, which sat under the fracture's own lowest grade: a fracture from
  accumulated damage had no grade, no penalty, could not be treated and shadowed any other limb penalty on the part.
  One marked Onyx line in `WoundFractureSystem.HandlePartDamageApplied`.
- **Necrosis risk multiplier (P30).** A wound's `riskMultiplier` divides its onset
  (`WolfmedWoundTraitSystem.GetPartNecrosisOnset`, the soonest over the part's wounds); it was only ever read as "is
  there a risk". Data unchanged, so frostbite frozen through (risk 1.5, onset 300 s) kills the limb in 200 s and
  charring (risk 1, 480 s) keeps its 480 s. Tourniquets and late reattachment are unchanged.
- **The pod repairs a machine's core** (M4's leftover). `SurgeryRepairCore` and `SurgeryRepairSynthCore` are on the
  neuro disk beside brain repair, in the neuro category and in the planner's organ-repair step, with no anaesthetic
  or antibiotic (`autodocProcedure`), and the pod carries a multitool for the re-flash step. The pod test found that
  both surgeries went invalid the moment the core was whole, so neither the pod nor a surgeon could reach the weld that
  closes the housing: the organ condition's new `validWhile` (the open-housing marker) keeps them listed until it is
  welded.
- **The pod sees a clamp working** (M3's leftover). The stall guard's part signature carries each wound's bleeding
  severity, which does not drift (only a treatment lowers it and only a new injury raises it), so a clamp that is
  closing a bleed is progress; a bleed needing five clamps is closed in one procedure instead of abandoned after three.
- **A synth's Poison runs no toxin route** (M4's leftover): confirmed, no change needed. Consciousness, the analyzer and
  liver clearance all skip `IsMechanical` bodies, and a synth has no brain clock to drain. The Poison itself still lands
  (HardLight's `Synth` container takes the Toxin group) and stays, since nothing clears a machine's Poison.
- **Onyx's `TryApplyLethalDamage` is removed**, with the routing's `MobThresholdSystem` dependency it alone used: its
  one caller (HOOK 13) went in M2.
- **DECISIONS.md corrections** (the rundown's Appendix A and what later milestones changed): marked inline as
  *[M6 correction: …]* where each stale statement stands, so the history reads as it was decided and the note says
  what the game does now. The stale comments the appendix names are corrected in place (the tourniquet's Asphyxiation
  in `_WF/Wolfmed/Damage/containers.yml`, the IPC profile's "inert" Cold, Caustic and organ damage in Onyx's
  `wounds.yml`, the `"airloss"` pressure key in the consciousness system and component).

**Appendix A, row by row.**

| Topic | Where | Now |
|---|---|---|
| Consciousness thresholds | "Consciousness" | corrected inline: pain 0.95 / 1.4, blood 0.5 / 0.35 |
| EMP 15/45, three EMPs kill an IPC | "EMP and machine bodies" | corrected inline: 40/120, no damage total kills an IPC |
| IPCs die at 100 | "Playtest fixes", "EMP" | corrected inline: core failure, core or head lost, gib |
| Body cap scope | "Playtest fixes" | corrected inline: no origin, not an explosion; per-part ceiling and corpse ceiling since M1b |
| Aim scatter 0.15/0.9 | "Aim scatter" | corrected inline: 0.1/0.75 |
| Why Bloodloss is uncapped | "AUTODOC5" | corrected inline: the figure kills nothing; vital-part loss is death through the amputation handler |
| Heavy round always lodges | "Final stages" | corrected inline: 35 % / 20 % rolls, kept as flavour (OD15) |
| Blast head only with the CVar | "Blast dismemberment" | true since M3 (inline note) |
| IPC oil costs nothing | "Playtest fixes" | corrected inline: oil 50 % Downs, 35 % shuts down (M1a) |
| IPC pain | "Phase 5" U1 | the entry was right, the CONSC report wrong; pain only Downs a machine (M1a) |
| Pickup while Downed | "AUTODOC5" | true since M1a (inline note) |
| CPR never reaches the damage band | "Brain death" | corrected inline: 289 s, CBI 534 s |
| Pod shocks | "Brain death" | corrected inline: also every 5 s, up to 5 |
| IPC brain: repair, then a jolt | "Brain death" | corrected inline: core repair, then the restart button (M2), pod since M6 |
| Rejuvenate the only revival | "Consciousness" | corrected inline: defibrillator and restart button too |
| "Not breathing" | "Brain death" | corrected inline: the Airloss group; replaced in M1a |
| Overheat kills the pump first | code comment, manifest | the code comment was rewritten in M4; the manifest row is annotated |
| Tourniquet Asphyxiation on the part | `containers.yml` comment | corrected: it goes to the systemic pool |
| IPC organ damage, Cold, Caustic inert | Onyx `wounds.yml` comments | corrected: live |
| Acid residue cannot deepen a second burn | `burns.yml` | true since M5 (P21) |
| `"airloss"` pressure key | consciousness comments | corrected: gone since BRAIN |

Also annotated inline, because M1a, M2 and M5 changed them: the arrest triggers (pain-shock arrest and sepsis roll
gone, electrocution after insulation, cold, toxin and heat added), the defib gate (0.25) and post-shock oxygenation
(0.5), D34 (closed by OD18), and M2's, M3's and M4's "left in place" / "not changed" notes this milestone closes.

**§11, the M6 rows.** P17 fixed (OD18). P25 fixed. P30: fracture grade and necrosis multiplier fixed; the stage
functionality and the `wolfmed.consciousness` toggle stay dropped, as the plan decided. The deferred list stays deferred,
because the plan makes each "if still wanted" and nobody asked: P26 (targeting), the zombie flicker (P29), airway
obstruction, and the Cellular route (P12). On P12, for the owner: content does deal Cellular (unstable reagents, some
gases, a Mono projectile with Cellular 5), and on a wound host it still has no route.

**Numbers.** `wolfmed.doafter_interrupt_damage` 10 (new). Necrosis onsets now effective: frostbite 200 s, charring
480 s (data unchanged).

**Differs from the plan, and why.**
1. **Break-on-damage do-afters are interrupted too**, not only self-treatment and surgery, at the same one-hit line
   (upstream's own default threshold is 1 per damage event). P17 names cuffing, D34 had accepted losing exactly this, and
   Wolfmed's own treatments already asked to break on damage. A hit under 10 interrupts nothing on a wound host.
2. **"Self-treatment" is the treatment the hit body is doing**, to itself or to someone else: a medic shot while
   bandaging is interrupted. A hit on the patient does not stop the surgeon's step, because a do-after belongs to the
   one performing it (upstream's rule).
3. **Inventory #18 is one field on the Onyx event**, `PartDamageAppliedEvent.InterruptsDoAfters`, and its argument where
   routing raises the event; the handler is `_WF`. That is the plan's "to confirm".
4. **P25 needed an upstream edit** in `DamageableSystem.cs` as well as inventory #17's Onyx lines: routing takes the hit
   from `BeforeDamageChangedEvent`, which carried none of the three arguments.
5. **The part multiplier scales damage, not healing.** Shitmed scales both; on a wound host the only healing multiplier
   that is not 1 is the Shitmed tend's 2.5, which HOOK 27 already replaced with direct wound treatment.
6. **Balance consequences of P25, stated plainly.** A heavy (wide) melee swing deals its part multiplier (0.5) to a
   wound host, as Shitmed meant. Callers that ignore resistances now skip the part's armour too: explosions (whose
   armour is the explosion resistance applied before the damage arrives; wound hosts got part armour on top, a second
   reduction), EMP Shock on chassis parts, temperature and barotrauma damage (which also stop taking the species'
   modifier set, as upstream intends), and the admin `damage` command with its ignore-resistances flag.
7. **The overheat pulse and explosions' `interruptsDoAfters`** changed (false and true), so each is what it is: a tick
   and a hit.
8. **Fracture: fixed, not removed.** Necrosis multiplier: fixed, not removed; the data keeps its values, so frostbite is
   faster than it was (200 s against 300 s), as the field always said it should be.
9. **Core repair's `validWhile`** is not in the plan; without it the pod left every housing unbolted, and so would a
   surgeon working from the menu.
10. **Where the tests live.** The plan's migration targets: `RoutingPassesIgnoreResistancesTest` in
    `WolfmedDamageBridgeTest`, `FractureGradeTest` in `WolfmedBluntWoundTest`, `NecrosisRiskTest` in
    `WolfmedInfectionTest`; `HitInterruptsSelfTreatmentTest` and the leftovers' tests in
    `Scenarios/WolfmedLeftoversTest.cs`. The milestone's real-time test is `PodRepairsACoreTest` (the pod runs the
    surgeries tick by tick).

**Test migration.**
- `WolfmedDamageBridgeTest`: new `RoutingPassesIgnoreResistancesTest`. The existing armour and penetration tests pass
  unchanged.
- `WolfmedBluntWoundTest`: new `FractureGradeTest` (a certain-creation copy of `WolfmedFractureProfile` as a test
  prototype, so the grade is not a roll).
- `WolfmedInfectionTest`: new `NecrosisRiskTest`; `TourniquetClockSurvivesATreatedWoundTest` asserts the freeze's
  onset is 200 s.
- `WolfmedTreatmentProcedureTest`, `WolfmedWoundSurgeryTest`: nothing to migrate for the interruption. The surgery
  tests raise step events directly, past the do-after, and the procedure tests deal no damage during a do-after.
- `WolfmedMechanicalWoundTest` (not in the plan's table): its damage helper ignores resistances, and
  `ShortCircuitAndServoDamageTest` (a 21-severity short from the IPC's Shock × 2.5) and
  `OverheatingCoolsRatherThanHealsTest` (48 from Heat × 1.5, then a partial cooling from the chassis's Cold modifier)
  counted on the modifier set applying anyway, which was P25. Those three hits now take the resistances they expect.
- `WolfmedWoundSurgeryTest.SurgeryFractureLadderReducesThenMendsTest` (not in the table): its second Blunt 75 lands on
  an arm already holding 75, so the new fracture starts at 135 (P30), not 75. It is driven down to 15 from wherever it
  starts, and the test asserts it started above 75.
- `Scenarios/WolfmedRemainingCausesTest.AcidResidueTest` (M5's, flaky, not caused by M6): it ran in the default map's
  vacuum, where the body's surface passes the cold damage line about two seconds in; a cold tick under the frostbite
  rule's 8 lands as a plain `BurnWound`, and on the torso that read as the residue's. It failed alone once in the final
  M6 run. It now gets station air, as the other M5 scenarios do, and passed alone three times running.

**Tests.** New: `HitInterruptsSelfTreatmentTest`, `RoutingPassesIgnoreResistancesTest`, `FractureGradeTest`,
`NecrosisRiskTest`, `PodRepairsACoreTest`, `PodClampProgressTest`, `SynthRunsNoToxinRouteTest`.
Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt, on the final code): 474 total, 468
passed, 0 failed, 6 skipped (dirty-disposed: `PodClampProgressTest`, `CutClothingUnblocksTheProcedureTest`,
`AutofixStopsReplanningABodyItIsNotChangingTest`, `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`,
`VisualStateFollowsTheLidTest`, `BrainDeadOccupantIsOperatedOnWithoutHoldingTest`); each passes alone. (The first solo
run of `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest` ended in a test-host "Internal CLR error"; it passed in
three solo reruns after.) Earlier full runs: 474 / 465 / 1 failed / 8 skipped (the short-circuit migration), then
474 / 469 / 0 / 5 skipped; the failures and solo failures they showed are the migrations listed above.

**Found, not changed.**
- A synth's Poison has nowhere to go: no route, no clearance, and HardLight's `Synth` container keeps taking it, so a
  poisoned synth keeps the systemic damage (and its pain) until something heals Toxin on a synth. Whether a machine
  synth should take Poison at all is the owner's species call.
- Acid residue ticks are 1 to 3 Caustic, under the interrupt line, so they never interrupt; a future stronger residue
  would need `interruptsDoAfters: false` (the routing's part entry point takes none).
- The Cellular route stays deferred (above).

**For the owner's playtest** (plan §12 M6). Bandage yourself while being shot (a hit of 10 or more stops it; fire does
not); a heavy swing against a wound host does half; admin `damage` with ignore-resistances passes armour; a pod with
the neuro disk repairs an IPC's or a synth's core; read this file against what the game does.

## M2-M6 review fixes (2026-09-24)

Four read-only reviews of `ef4fbdb11d..ce4e281421` (reports in `<plan>/p7/review/`).
Three findings, all confirmed against the code and fixed. Branch `Wolfmed-fixes3`, from `ce4e281421`.

- **Damaged lungs are a cause still present (HIGH).** Since M3, `DrainRate` folds `LungDamageLevel` into the same breath
  input as suffocation and sedation, so lungs under their impaired line can stop the heart on their own, and the arrest
  is named "oxygen". `GetRestartMemory`'s "oxygen" arm read only suffocation and sedation, so after the shock the
  analyzer said "Still present: no" while the lungs went on draining the brain. It now reads the lungs too.
  - **Also fixed, same cause (not in the reviews):** `GetActiveRoutes` set the lungs route only for a suffocating body
    with no lungs, so the same patient read "Getting worse: nothing now". The lungs route ("lungs failing (air,
    internals, lung surgery)") now also shows while the lungs are under their impaired line. M2's "For the M3 merge"
    note had asked for this. Wait as a ghost was never affected: `IsStable` already refuses any drain on the brain.
  - Measured (`WolfmedConsequencesTest.LungArrestRestartMemoryTest`, new): lungs at 10% (level 0.8) in station air
    stopped the heart at 191 s (derived 191 s), cause "oxygen". After the shock: "Still present: yes" and the lungs
    route. With the lungs healed: "Still present: no" and no lungs route.
- **Heartbeat licence (MEDIUM).** `65aab0b387` set `attributions.yml` to CC-BY-SA-3.0 (Skyrat-tg), but "Final stages"
  still called the licence pending in two places. Both now carry an inline correction note, as M6 marked its
  Appendix A items. M2's restart-memory bullet has a note too.
- **`GetPartNecrosisRisk` removed (LOW).** Nothing in the game has called it since M6. It picked the wound with the
  highest risk and returned that wound's raw onset, which disagreed with `GetPartNecrosisOnset`, the reader in use. The
  review missed one caller, a test: `WolfmedBurnWoundTest.FrostbiteNumbsAndThenRisksTheLimbTest` now reads
  `GetPartNecrosisOnset` (the frostbite's onset divided by its risk, and zero once thawed). The manifest row that names
  the method is annotated.

**Known flakes (not changed).**
- `HeartbeatTracksLocalPlayerCritTest` failed once in the full run on `ce4e281421` (30 minutes, with four reviewers
  running on the machine) and once in M3's runs, and passes alone. It forces a mob state on a healthy body and holds it
  for up to 20 tries of 10 ticks. The failure log has no detail and the cause is not a fixed tick count or a wait
  shorter than the heartbeat's own update, which runs every client frame. One possibility, not confirmed: the default
  test map is a vacuum, so pressure and cold damage keep making consciousness re-assert the mob state against the forced
  one. It passed in this run.
- `AcidResidueTest` passed in the full run. `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest` was skipped
  (dirty-disposed) and passed alone first time, with no test-host crash.

**Tests.** Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt): 475 total, 469 passed, 0 failed,
6 skipped (dirty-disposed: `AutofixStopsReplanningABodyItIsNotChangingTest`, `AutofixModuleIdlesWithNothingToDoTest`,
`EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`, `PowerLossPausesAndRestoreResumesTest`,
`SlipOpensOneSmallWoundTest`, `VisualStateFollowsTheLidTest`). Each passed alone.

## Synth takes no poison (2026-09-24)

The owner's answer to M6's open call: a Synth is synthetic with organic parts, not the other way round, and the one
organic weakness M4 left dangling goes. HardLight's `Synth` damage container accepts the Toxin group, but since OD16
made the Synth mechanical the toxin route, liver clearance and consciousness all skipped it, so Poison landed, hurt,
and never mattered or cleared. The `Synth` damage modifier set now has `Poison: 0`, the same coefficient the IPC set
carries, so gas, stings and venom do not land on a Synth at all. Radiation (Synth 0.8, IPC 0.5) is untouched: it has
no machine route either, and the owner has not ruled on it.

`SynthRunsNoToxinRouteTest` became `SynthTakesNoPoisonTest`: the dose now goes through resistances and the load must
stay zero; the human control still ends in a toxic coma.

## Playtest 3 fixes (2026-09-24)

The owner's third playtest (`plan/p7/PLAYTEST3-spec.md`) asked for three things: the explanation card should carry
the time and what is wrong ("COMING ROUND IN 22 S in big caps, with icons … Infinity if not"), a Downed body should
not crawl onto tables, and the analyzer's vitals block said far too much and printed `1.2999999523162842`. Branch
`Wolfmed-fixes4`, from `a17563de1b`.

**1. The explanation card counts down.**
- **One window.** `WolfmedConsciousnessSystem.GetWakeWindow` is when the body comes round by itself: Unconscious, the
  cause a timed faint (pain faint, head blow), and nothing untimed holding it too; with both faints running, the later
  end. The faint alert's cooldown (`WolfmedConditionAlertSystem.GetFaintCountdown`) and the card both read it.
  `WolfmedCardComponent` gains networked `WakeStart`/`WakeEnd` (the component's private timers stay server-only);
  `WolfmedCardSystem.Refresh` sets or clears them. The condition alert system's change handler refreshes the card in
  the same tick the body goes out, so the card's countdown starts with the alert's rather than on the card's
  once-a-second pass.
- **Timed helplessness found:** the pain faint and the head blow only. `DownedUntil` is the 2 s Downed dwell and the
  card never shows while Downed; blood, hypoxia, sedation, toxins, cold, heat, arrest and shutdown have no clock (a
  shutdown lasts as long as its reason: no power, no pump, low oil).
- **The row.** `WolfmedExplanationCard.Countdown(…, now)`: "COMING ROUND IN {N} S" (N the remainder rounded up, never
  negative) with the bar at remainder / (end − start); "COMING ROUND: ∞" with an empty bar when nothing times the wake
  (a faint something else holds included, as its alert already showed no countdown); null while Dying, where the M2
  brain bar takes the row, drawn exactly as before (ten cells, whole tenths, red, its label). The client computes the
  row every frame from the networked window and its own clock; the countdown bar is the same ten cells in the accent
  colour, draining smoothly through the cell it is in.
- **Icons.** The cause's `alertOut` icon (its `alertDowned` for a cause that has only that) left of the title at the
  alerts bar's size, 64 px × UI scale; each blocker's icon at half that before "Also holding you down: …". Resolved from
  the alert prototype's sprite specifier through the resource cache, as the alerts bar's sprite view resolves it (the
  top severity's icon for a severity alert, animated on the clock). An alert, RSI or state that cannot be found draws
  nothing; the engine's error sprite is never used. No new textures.
- **Look.** A dark translucent panel (#0d0f14 at 0.8) with a 3 px × UI scale accent stripe down the left from the new
  optional `cardColour` on `wolfmedConsciousnessCause`, default #b8704e (terracotta): amber #e3a33c on PainFaint and
  HeadBlow, red #d0343c on Arrest, CoreHeat and CirculatoryCollapse, steel blue #4f86b8 on Sedation, grey #8b9098 on
  Shutdown. Title in caps, bold 17; countdown bold 24; rows regular 14; the help line brighter, the rescue lines green.
  Fonts are built at the pixel size they are drawn at, so the text is sharp at any scale. The scale is the viewport's
  height / 1000 (0.75 to 1.5) × the UI scale. Placement (`WolfmedExplanationCardLayout`): centred, at most 74% of the
  screen wide (clear of the action buttons at 12% and the alerts column at 94%), bottom edge at 83% of the height (just
  above the hotbar band). The fade (`Alpha`) is unchanged; padding (12 × scale) and the greedy word wrap are as before.
- **What's wrong, fully.** Rows: title, help (under the bar), blockers, symptom, then what the patient can feel of the
  breath and the blood, from the networked `Breathing` and `BloodBand`: "Breathing laboured / slowed", "Gasping for
  air", "Not breathing"; "Blood low / very low / critically low". Organic bodies only (a chassis's card, when its HUD
  is off, skips them). Then CPR and "examined". No numbers anywhere but the countdown.

**2. Downed cannot climb onto tables.**
- **What the owner hit.** `ClimbSystem.TryClimb` already refused a Downed body climbing by itself: its `CanVault`
  asks `CanInteract(user, table)`, and Wolfmed's Downed rule cancels that `InteractionAttemptEvent` (the table is not
  the body, carried, a reachable pod or a loose item), before `AttemptClimbEvent` is ever raised; the drag-drop outline
  never offered the table either. The route was lying down: `StandingStateSystem.Down` takes `MidImpassable`
  (= `TableLayer`) off every lying body's masks "to allow going under certain entities like flaps and tables", nothing
  changes the draw depth, and a Downed body crawled through a table and lay drawn on top of it. Measured with the fix
  switched off: pushed east at a table for 60 ticks, the body ended at x 2.75, inside the table's tile (2.0 to 3.0);
  with it, at x 1.70, against the table's edge.
- **`WolfmedDownedClimbSystem`** (shared, new):
  - `ClimbableComponent, AttemptClimbEvent`: cancelled when the climber is Downed or Unconscious under Wolfmed and is
    its own user, with "You can't climb while you're down." (`wolfmed-downed-cant-climb`, a predicted client popup). A
    lift by somebody else (user ≠ climber) passes. Today the Downed interaction rule refuses first, with the stock
    "can't interact" line; this is the rule itself, for any path that reaches the event.
  - The crawl route, blocked the same way: `WolfmedDownedSystem` calls `HoldTables` as the body goes Downed (the
    tables' layer back on the fixtures lying down changed) and `ReleaseTables` when Downed ends with the body still
    lying (a stun, or out cold: upstream's crawl-under-tables again). Not while the body is climbing (a medic lifted it
    onto a table): the climb owns those masks, and `WolfmedDownedComponent, EndClimbEvent` puts the layer back when it
    comes off.
- **Checked, nothing to block:** there is no bump-to-climb in this tree; the only other `TryClimb` caller is NPC
  steering, through the same `CanVault` and event. `CrawlUnderObjectsSystem` (_DV, the HardLight rewrite) shrinks
  circles and refuses climbs while sneaking, going down ends sneaking, and it never strips table bits. (Its own
  `CrawlUnderObjectsComponent, AttemptClimbEvent` subscription can never fire, since the event is raised on the
  climbable; not ours, left alone.) One-subscriber rule: neither new pair had a subscriber.
- **Side effect:** a Downed body pulled by somebody now stops at tables like a standing one, where it used to slide
  under them. Knocked-down, voluntarily lying and unconscious bodies keep upstream's behaviour.

**3. The analyzer's vitals block says less, and rounds.**
- `WolfmedVitalsText.Lines`: the state line (unchanged), one vitals line, "Do first", then only when they apply the
  after-restart memory, the defib verdict and the restart-button verdict. The owner's state now reads:
  - `DOWNED: blood loss (also: pain)`
  - `Breathing laboured · Pulse weak, rapid · Blood 43% ↓ · Lungs impaired · Burn fluid loss 1.3 u/s`
  - `Do first: dress the burns and give fluids; lung surgery; transfuse ≈ 20 u`
- **Vitals line** (`VitalsLine`), items joined by the locale's ` · `, only what is not normal: breathing; the pulse
  band ("Pale", "Pulse weak, rapid", "Pulse barely palpable", "No pulse"); blood % with the trend arrow when the band
  is not normal or the blood is falling (`wolfmed-vitals-trend-*`: steady nothing, ↑, ↓, ↓↓, a locale choice);
  impaired and failed organs ("Lungs impaired", "Heart failed"); burn fluid loss with its rate; toxins with their band
  ("No liver" when there is none; an impaired or failed liver is already an organ item); radiation with the marrow's
  stage; the core temperature. A chassis: "Cooling offline", oil like blood (also while under the refill line), its
  organs, and M4's core and chassis heat. The M4 and M5 temperature lines are items now. "Vitals normal" when nothing
  is abnormal.
- **Do first** (`DoFirstLine`, replaces `RoutesLine`): each running route's aid (`wolfmed-vitals-aid-*`, replacing
  `wolfmed-vitals-route-*`), a few words each, deduplicated, in the routes' bit order, the order `RoutesLine` used. The
  circulation route's aid carries the transfusion units, which the vitals line no longer repeats; a chassis under its
  oil line gets "refill oil ≈ N u" in the same place, route or not (the old hydraulics line's guidance). "Do first:
  nothing; stable" for a body that is down with nothing running; no line for somebody up with nothing to do, or the
  dead.
- **Rounding.** `WolfmedVitalsText.Number` (invariant culture, at most one decimal, no trailing zero, never "-0"),
  `Units` (whole, rounded up) and `Whole` (percent, kelvin, dose, load). Every Fluent argument in the block, the
  post-shock banner (`WolfmedPostShockText`) and the card's countdown is a string. The screenshot's digits were
  `MathF.Round` returning a float that Fluent formatted as a double. Examine formats no floats (its only number is the
  scar count, an integer plural).
- **The pod** mounts the same panel: the block is one banner row whose `RichTextLabel` word-wraps to the panel, so a
  long vitals line wraps and nothing is cut; the block is shorter than before.

**Differs from the spec, and why.**
1. **"Do first" order.** The rule (the routes' order, as `RoutesLine` used) and the spec's example (transfusion first)
   disagree; the rule is followed, so the owner's state reads burns, lungs, transfusion. A brain-first order is one
   ordering list in `DoFirstLine` if the owner wants it.
2. **The ⚠ "marker".** No text marker exists: the ⚠ in the screenshot is the vitals banner row's warning icon, one per
   block, vertically centred, which is why it sat beside the fluid-loss line. It is unchanged and still heads the
   block. No text ⚠ was added, as the spec's own example line 3 carries none.
3. **Aid wording.** The two the example gives ("dress the burns and give fluids", "lung surgery") are used; the rest
   are new short forms ("gauze or tourniquet the bleeding", "CPR, then the defibrillator", "air or internals", …).
4. **Breathing words.** An arrest (or a circulatory collapse) reads plain "Not breathing": the state line names it.
   `wolfmed-vitals-breathing-none-collapse` is gone.
5. **Radiation** loses the marrow's loss rate (the blood's arrow shows the loss); **burn fluid** loses "fast"/"slow"
   (the rate says it). `BurnFluidFast` and `wolfmed.analyzer_burn_fast` are left in place, now unread by the text.
6. **The crawl route** is fixed as well as the climb event, which alone changed nothing a player could do; the spec's
   "if one does, block it the same way and say so" covers it.
7. **Card placement:** bottom at 83% of the screen (was 88%) and at most 74% wide, so it clears the hotbar as well as
   the alerts column and the chat at UI scale 1 and 1.25.
8. **A timed faint keeps its help line** ("You come round in seconds.") under the countdown, as the spec keeps help;
   the condition text keeps its timed form.
9. **Both faints running:** the later end, and the alert gains the countdown in that case too (before, a head blow
   blocking a pain faint showed none).
10. **The card title is drawn in caps**; `WolfmedExplanationCard.Lines()[0]` keeps the alert title's case, which
    `ExplanationCardTest` compares.

**Tests.** New in `Scenarios/WolfmedPlaytestThreeTest.cs`: `CardCountdownTest`, `DownedCannotClimbTest`,
`VitalsBlockIsCompactTest`, and `CardLooksTest` (not in the spec: the layout clear of the alerts, chat, hotbar, doll and
action buttons at UI scale 1 and 1.25 on four resolutions; every cause's icon resolves on the client; an unknown alert
draws nothing). Measured: the countdown read "COMING ROUND IN 20 S" (20.00 s left, bar 1.000) and 3 s later "COMING
ROUND IN 17 S" (bar 0.848).

**Test migration** (each keeps what it proves; line indexes follow the new three lines):
- `WolfmedMedicLinesTest.AnalyzerStateLinesTest`: each rung's vitals line (normal breathing unlisted, pulse and blood
  items, "Vitals normal" for the healthy and the unpowered chassis), the transfusion on "Do first", "Do first: nothing;
  stable" for the Downed-by-pain patient; `VitalsWordsResolveTest`: the new keys, the pulse family without Normal.
- `WolfmedMedicInfoTest.AnalyzerVitalsTest`: `DoFirstLine` with each route's aid and the transfusion's units.
- `WolfmedLocaleCoverageTest`: the `wolfmed-vitals-aid-` family replaces `wolfmed-vitals-route-`.
- `WolfmedAnalyzerTest.VitalsBlockHeadsThePanelTest`: the panel's row carries "Pulse weak, rapid" and "Do first:
  transfuse ≈", no breathing.
- `WolfmedBreathingClockTest`: the Downed bleed reads "Pulse weak, rapid · Blood 49|50% ↓"; the suffocated body
  "Not breathing: no air".
- `WolfmedConsequencesTest`: "Lungs impaired", "Breathing laboured", "Heart impaired"; the healthy body lists nothing
  impaired.
- `WolfmedIpcDeathTest`: the core and chassis heat on the vitals line, the core-heat aid on "Do first".
- `WolfmedRemainingCausesTest`: "Toxins 60, high", "No liver", "Liver impaired", the marrow's aid.
- `WolfmedTemperatureTest`: "Core temperature 320 K", the heat-stroke aid.
- `WolfmedSpeciesTest`: the collapsed Diona's vitals line starts "Not breathing" and names no heart.
- `WolfmedLeftoversTest`: the synth shows no "Toxins"; the human control does.

Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt): 479 total, 471 passed, 0 failed, 8 skipped
(dirty-disposed: `PodClampProgressTest`, `AutofixStopsReplanningABodyItIsNotChangingTest`,
`EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`, `SelfServiceOccupantIsTreatedTest`,
`AutofixModuleIdlesWithNothingToDoTest`, `FixMePlansAndStartsInSelfServiceTest`,
`DeathDuringAProcedureHoldsAndResumesTest`, `PodChargesAgainAfterAFailedShockTest`); each passed alone.

## Playtest 3, round 2 (2026-09-24)

- **Let go before last words.** The Last Words action used to take the whisper first and open "Let go?" after it, so
  a player could whisper their last words and then keep fighting. Now the action opens "Let go?" first; a yes opens
  the whisper prompt (optional: cancel or an empty line still lets go), and the whisper is followed by Succumb. A body
  revived while the prompt is open says nothing and stays. `WolfmedDyingActionsSystem.OpenSuccumbDialog(body, wordsMax)`
  carries the request; `SayLastWords` now whispers and lets go. `HonestEndingScenarioTest` reads the whisper through
  the defib shock's stutter.
- **The oxygen icon.** `WolfmedOutHypoxia` showed the generic critical icon (a figure on the floor); it now shows the
  low-oxygen icon like its Downed twin. On the card, a blocker's icon is its Downed alert's, not its critical one, so
  blood, pain and oxygen blockers each show their own picture.

## IPC core sizing (playtest 3, 2026-09-24)

The owner, as an IPC, died to three captain's sabre hits (Slash 32) to the torso: Critical and Dead in the same
second. Every organ had Onyx's 15 health, but a chassis keeps its core in the torso with only the pump beside it, so
the core took 54% of every torso split (a human heart takes about a quarter, behind lungs, liver, stomach and
kidneys), and a heavy blade's share hit the 5-a-hit cap: 3 × 5 = 15. A human chest under the same blade loses the
heart to arrest around hit 5, which is Dying with a rescue window; a destroyed core is death outright.

- `WolfmedOrganPositronicBrain` (IPC core and, through `OrganSynthBrain`, the synth core) is 40 health; the core now
  fails on the rifle round the human heart does (hit 13, Downed under 25% from hit 10), and a sabre needs eight hits
  with two of warning after the Downed line.
- `WolfmedOrganIpcPump` (IPC pump, synth heart) is 25: it fails around the round the human lungs are gone.
- `wolfmed.ipc_core_heat_rate` 0.2 → 0.5333 (× 8/3), so the M4 fire timeline is unchanged: thermal shutdown ~41 s,
  core failure ~102 s untreated, standing when put out at 60 s.
- Core repair and the restart button read `MaxHealth`, so nothing else moves. Tests set the core by fraction.

## Playtest 3, IPC round (2026-09-24)

The owner played an IPC (`plan/p7/PLAYTEST3-IPC-spec.md`) and asked for three things: no circle diagram in the middle of
the screen, a SYSTEM panel that does not clip, no stray dots, and a slower death to fluid loss. Branch `Wolfmed-fixes5`,
from `ed7a0575b3`.

**1. The synthetic HUD.**
- **The centre diagram is gone.** `DrawOptics` (the broken concentric arcs and their calibration ticks) and the overlay's
  `Tier` field, which only fed it, are removed. Nothing is drawn over the player.
- **Why the SYSTEM panel clipped.** Its height was a constant, `SystemRows` 7 (heading, four gauges, fault count, status),
  from the first HUD. M1a D added SENSOR and M4 CORE to the list without the box growing, and the status line was still
  drawn at the fixed seventh row: the advice printed over whichever row landed there (FAULTS, or CORE when it shows), and
  the rows after it ran under the frame's bottom edge.
- **Sized from its rows now.** `WolfmedSyntheticHudLayout.SystemPanel(screen, scale, gauges, adviceRows)` returns the box
  and every row's rectangle (`WolfmedSystemPanel`): the title, one row per system, a blank half-row, `FAULTS n`, then the
  advice on its own row, a second when it is long. Every row is one line high inside the padding the title uses, and the
  box is the rows plus that padding, anchored where the old one was, so it grows downwards. Width: 28 monospace columns
  (`SystemColumns`), in which the longest advice in the locale ("ADVICE: DIAGNOSTICS RUNNING. NO ACTION NEEDED", 45
  characters) wraps whole into two rows. `WrapAdvice` breaks at words, indents the continuation under the text after
  "ADVICE: ", and ends a third row's worth in "...". `FitScale` reserves the largest panel (six gauges, two advice rows).
- **Tidy.** One text size for every row; the label at the left, the fixed-width bar `[######]` at column 9, the value
  (`98%`, `450 K`, the fault count) right-aligned to the panel's padding, so the percentages line up; the spinner
  right-aligned on the title row; the advice in the accent amber, the idle flavour line dim. The amber-and-red mono
  readout and the bracket frame are unchanged. The row locale keys hold the label only now
  (`wolfmed-synthetic-row-core` is new, `wolfmed-synthetic-row-core-temp` is the value, `-row-faults` lost `$count`).
- **The four stray dots were the idle glyph.** A chassis at the Idle tier (undamaged, up) drew
  `wolfmed-synthetic-glyph`, "::", left-aligned in the warning banner's box (32% across, 25% down the viewport) at half
  alpha, with no frame or label: two colons in the mono face are four dots in a square, in the upper left of the play area.
  The "Synthetic HUD" section above called it "a corner glyph"; it was never laid out in a corner. It went away within a
  second of the first damage (the tier rises and it fades), which is why it looked like a stray artefact. Every other draw
  call was checked for a zero or unlaid-out box: all of them take their boxes from the viewport `Screen()` gives (the
  panels, the banner, the compact strip, the standby, panic and death screens), none can reach the origin, and the removed
  arcs were centred on the screen. The glyph and its locale key are removed: a healthy chassis sees a clean screen, as a
  healthy body does. `WolfmedSyntheticHudLayout.Visible` is the one rule for which blocks show.

**2. Fluid pressure loss.**
- **Measured first** (`IpcFluidLossTest`, the owner's hits: seven Piercing 15 to the torso over 8 s, then untreated, an
  IPC and a human side by side; seconds from the first hit):

  | | IPC before | human | IPC after |
  |---|---|---|---|
  | pool | 250 u oil | 300 u blood | 250 u oil |
  | torso wounds after the hits | breach 84 (10.1 u/tick), chassis 105 (2.5) | piercing 105 (11.0) | breach 84 (4.0), chassis 105 (1.0) |
  | drain | 10 u/tick cap (3.33 u/s) | 10 u/tick cap + 0.35 u/s internal | 5.0 u/tick (1.68 u/s) |
  | fluid at 20 / 40 / 61 / 92 / 123 s | 81 / 55 / 30 / 0 / 0% | 82 / 58 / 37 / 20 / 9% | 92 / 80 / 69 / 53 / 37% |
  | Downed line (50%) | 46.4 s | 49.5 s | **97.1 s** (1.96×) |
  | shutdown / Unconscious (35%) | 57.8 s (Oil, Critical) | 61.9 s (Blood); arrest ~70 s | **127.0 s** (2.05×, Oil, Critical) |

  The owner was Critical 59 s after the first hit; the "before" run gives 57.8 s. Both bodies are Downed by pain from the
  hits at once, so the lines are the fluid and blood crossings.
- **What it was.** Not the pool: 250 u against 300 u is 17%. The bloodstream takes at most `MaxBleedAmount` 10 u a 3 s
  tick and puts 1 u a tick back; the IPC's wounds asked 12.6 u a tick and the human's 11, so both bled at the cap and the
  chassis died about as fast as a human. A profile multiplier does nothing until it brings the chassis under the cap
  (below about 0.8), and the tick's 1 u of regeneration eats any leak under it: at 0.11 (tried) the chassis lost 13% in ten
  minutes and never went down.
- **Set.** `IpcBodyPartProfile.bleedingMultiplier` 1 → **0.4** (a marked Onyx YAML line). One lever, data only; no CVar was
  needed. It is the IPC chassis and, through `WolfmedPartIpc`, the synth's parts; cybernetic limbs on flesh keep their own
  profile (0.5). The pool stays 250 u, so refills, the pod's transfusion and the analyzer's "refill oil ≈ N u" are
  unchanged in units, and the analyzer's hydraulic numbers read the same fraction (asserted). Refilled above the Downed
  line, a shut-down chassis comes back (Downed by its pain, not shut down, not Critical).
- **What a small hit does.** One spear hit leaks under the tick's regeneration, before and after (about 1.1 and 0.4 u a
  tick against 1): an IPC never went down from a single wound's leak and still does not.

**Differs from the spec, and why.**
1. **The dots' cause** is the idle glyph, not a zero box (above). The fix removes the glyph rather than moving it.
2. **Fluid:** the multiplier, not the pool: the pool explains 17%, the cap the rest. 0.4 lands on the spec's "about twice"
   for both lines.
3. **"Downed" and "Unconscious" in the fluid test are the line crossings,** each checked against the state and cause at
   that moment (Unconscious for the oil or the blood), because both bodies are Downed by pain from the first hits.
4. **The advice's second row is indented** under the advice text, so it reads as one item.
5. **Panel width** is 28 columns (the old box was about 31); the bars, values and advice all fit in it.

**Tests.** New `Scenarios/WolfmedIpcFluidLossTest.IpcFluidLossTest` (order with a 1.5× margin, ±20% bands on the four
measured times, the out causes Oil and Blood, the chassis Critical, the analyzer's fraction and "refill oil", the refill
bringing it back). New `WolfmedSyntheticHudTest.SyntheticHudPanelFitsTest` (four viewports, UI scale 1 and 1.25, five and
six gauges, 0, 1 and 6 faults, the longest advice, the owner's advice and an idle line: every row inside the padding and
one line high, no two rows overlapping, the half-row gap, the advice under FAULTS, the box ending one padding under the
last row, the panel inside its largest size, no drawn box within the margin of the viewport's origin or off it; a healthy
chassis draws nothing; an over-long advice ends in "..."). `EveryMechanicalWoundHasALineTest`: the key list loses the
glyph and gains the sensor and core keys.

Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt): 481 total, 473 passed, 0 failed, 8 skipped
(all autodoc fixtures: `AutoCutsClothingOffAHelplessPatientTest`, `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`,
`PodRelocatesADislocatedJointTest`, `EjectOnlyAlarmsWhileRunningTest`, `PowerLossPausesAndRestoreResumesTest`,
`SlipOpensOneSmallWoundTest`, `VisualStateFollowsTheLidTest`, `DeathDuringAProcedureHoldsAndResumesTest`); each passed alone.

## Heat stroke is timed (playtest 3, 2026-09-24)

The owner, in a hot room: "I existed for like 4 seconds." M5 had the core follow a hotter surface at once, and the
atmosphere moves the surface fast, so the heat exhaustion line (the heat damage threshold less 7 K; 318 K for a body
with the 325 K threshold) was crossed within seconds of the air being hot. "It should act like normal heatstroke, a
timed limit before you overheat."

- Above normal the core now chases a hotter surface over `wolfmed.core_heating_seconds` (240) and comes back toward
  normal over `wolfmed.core_recovery_seconds` (60) when the air is cooler; warming up from below normal is still at once,
  so rewarming stays responsive, and cooling below normal keeps `wolfmed.core_cooling_seconds` (900).
- Measured (`HeatStrokeIsTimedTest`, 330 K air, 318/325 K lines): Downed after about 2 min, heat stroke after about
  5.5 min, back out of heat stroke within a minute of the air cooling to 300 K. The fire grace is unchanged.
- The M5 scenario tests hold a surface and expect the core there at once; they now pin both new cvars to 0.

## Playtest 3, IPC round 2 (2026-09-24)

The owner, playing an IPC, hit two things (`plan/p7/PLAYTEST3-IPC2-spec.md`): after a pod run the synthetic HUD's torso row
still read 89.8, and a welder set the IPC's own spilled oil on fire. Branch `Wolfmed-fixes6`, from `36ed230814`.

**1. Why the tend surgeries left the chassis wound.** Measured on this branch before the change, with a throwaway fixture
(an IPC in a pod, torso hit, the planner's own queue run to the end).
- **Not the treatment capabilities.** HOOK 27's `WoundSystem.TryHealWounds` and the step's `SurgeryStepDamageEvent` run with
  no capability scope; `WoundDamageRoutingSystem.WithTreatmentCapabilities` only scopes the hand welder and reagent effects.
  A tend that ran did close the generic wound: one deep brute tend took a Blunt 60 torso's `IpcMechanicalDamageWound` from
  60 to 9.1 and its dent from 54 to 33.1.
- **Three reads of one wound that disagree.** `IpcMechanicalDamageWound` lists Blunt, Slash and Piercing (Brute) and Heat,
  Cold and Caustic (Burn). HOOK 24 (listing) and HOOK 26 (completion) read wounds, so the one wound counts under both
  groups on the same part. The upstream step, `OnTendWoundsStep`, returns before HOOK 27 unless the body or the part
  carries damage of its own group. Measured:
  - Blunt only (60 or 90 on the torso): the burn tend lists, does nothing three times, "THIS IS NOT WORKING.", and goes
    into `FailedProcedures` for that occupant.
  - Heat only (90, 135 after the IPC's ×1.5; a chassis that burned): the deep and the normal brute tend both list, stall
    and fail the same way; only the burn tend works.
  - Piercing 60 + Heat 30: the brute tend closed the whole generic wound through its brute entries, whatever made it; the
    burn tend queued behind it found no wound, its first step was not performed, and the pod **faulted**
    (`FinishStep`: not performed with no step done is a fault), which stops the queue.
  - The deep tends' window (100 and up) ends the procedure as the group falls under 100 and leaves the rest to a re-plan.
- So whether a chassis came out clean depended on which damage made its wounds and on the queue order, and a fault or a
  failed-procedure mark leaves the generic wound standing with nothing planning for it: the owner's 89.8. The owner's exact
  state was not reproduced (the damage history is unknown); these are the three ways found.

**2. The pod welds and rewires a chassis.**
- **`SurgeryWeldChassis`** ("Weld Chassis"): no requirement, one repeatable step, `SurgeryStepWeldChassis` (tool
  `WolfmedHullWeld`, the welder the pod already carries; 3 s, the hand welder's `DoAfterDelay`; the welder sprite; WELDING
  from the tool). A pass is the hand welder's own call: the tool's `WeldingHealing` spec (Blunt, Piercing, Slash −25)
  through `WoundDamageRoutingSystem.TryApplyPartDamage` inside `WithTreatmentCapabilities({Mechanical})`, ignoring
  resistances, healing wounds. When that removes no damage (the part has none of the welder's types left, or the wound
  was never backed by damage), the pass works on the wounds themselves with the same spec, the rule hand topicals already
  follow (`WoundHealingSystem.TryApplyHealing`). Then `WolfmedWoundDamageSyncSystem.SyncPart`. It repeats until the part
  carries none of `IpcMechanicalDamageWound`, `WolfmedDentWound`, `WolfmedBreachWound` or `CyberneticMechanicalDamageWound`;
  a breach at severity 0 is removed, so its bleed goes with it. No pain, no scream, no fuel: the hand welder costs neither
  of the first two, and the pod cannot refuel its welder.
- **`SurgeryRewireChassis`** ("Rewire"): `SurgeryStepRewireChassis` (tool `WolfmedServoKit`, the cable coil the pod already
  carries; REWIRING). A 3 s pass is five uses of the coil's own `Healing` block through `WoundHealingSystem.TryApplyHealing`,
  exactly a hand coil's use (0.6 s each). It repeats until the part carries no `WolfmedShortCircuitWound` and no
  `ElectricalWound`, and on the way it takes Heat and Shock damage and the generic wound's Heat share, as the coil does.
- **Gating.** `WolfmedSurgeryWoundCondition` gains `woundPrototypes` (any one of them lists the surgery; `woundPrototype`
  still works) and `mechanical` (a machine part only, `WolfmedWoundTraitSystem.IsMechanical`). The weld lists on its four
  wounds; the rewire on its two, machine parts only, since `ElectricalWound` is flesh's too.
- **The organic tends stay off machine parts.** HOOK 24's body (`WolfmedWoundWindowFails`, a `_WF` partial) fails on a
  machine part, so neither the planner nor a surgeon lists a tend on a chassis or a cybernetic limb.
- **The pod.** Both on the Mechanical category (breach, weld, rewire, servo), so the planner's triage keeps them at step
  11; both in the base program; `autodocProcedure` entries with no anaesthetic and no antibiotic. A leaking chassis still
  gets `SurgeryStopBleeding` at step 4 first.
- **The stall guard.** The M6 part signature carries every wound's severity (whole numbers) and the part's damage total.
  A weld pass moves the wound by 25 to 75 or the damage by 25 a type, a rewire pass by 2 or more, so every pass reads as
  progress; `PodWeldsChassisTest` ends with nothing in `FailedProcedures`, including a 75 `ElectricalWound` the coil closes
  over several passes.
- **The pod's welding never lights anything.** Confirmed in the code: the pod's tools are spawned into its own container
  and nothing in the autodoc toggles them; a welder's `IgnitionSource.Ignited` is set only by its item toggle
  (`ItemToggleHot`) or by being on fire. The test samples the pod's welder every tick of the run: never lit.

**3. Hydraulic fluid.**
- **`WolfmedHydraulicFluid`** (`_WF/Wolfmed/Reagents/hydraulics.yml`): "hydraulic fluid", dark amber (`#8a5a14`), oily, no
  flammability, no tile reaction, no metabolism (Oil has none either, so drinking it does what drinking oil does:
  nothing), group Biological as `SynthBlood` is. A spill has WeldingFuel's slip values (`requiredSlipSpeed` 3.5, friction
  0.4), mops up like any spill and does not evaporate, as Oil does not.
- **Every place it replaced oil.**
  1. `MobIPC`'s `bloodReagent` (marked). The pool stays 250 u, so every refill number is unchanged.
  2. `BaseIPCOrgan`'s organ solution, 10 u (marked): a chassis's components hold its own fluid.
  3. The pod: `WolfmedAutodocReagents` lists it as a Fluid with the new `machine: true`. `DrawFluid` gives a machine fluid
     only to a body that runs on it, and gives such a body nothing else. Before, nothing on the list was an IPC's
     (`Oil` never was), so the pod "refilled" a chassis from saline or blood: `TryModifyBloodLevel` adds the body's own
     reagent whatever was drawn. That was the only way oil went back into an IPC.
  4. By hand: nothing refilled a chassis (there is no oil can, and a blood pack's `Healing` block is for `Biological`
     containers only). New `WolfmedHydraulicFluidPack`: the blood pack's bag (`bloodpack-empty`) with its greyscale
     `bloodpack-liquid-1..5` fill tinted by the contents, 200 u, drainable and refillable. `WolfmedFluidPackSystem`: used on
     a body (or in hand, on yourself) it moves up to 25 u of the body's own fluid every 2 s until the body is full or the
     pack is empty; a pack holding anything the body does not run on is refused whole.
  5. Chemistry: a new reaction, Oil + Silicon → 2 hydraulic fluid (a silicone fluid). Oil's own sources are untouched.
  6. Stock: the Wolfgate vendor (4, in its Wolfmed block: it sells no blood packs), the NanoMed wall vendor (2, beside its
     blood packs, marked), CiviMed (infinite, beside its blood packs, marked); the debug crate.
  7. Words: the analyzer's "Oil 43%" is "Fluid 43%" (the HUD's FLUID row); "refill oil ≈ N u" is "refill hydraulic fluid
     ≈ N u"; "weld the oil leak" is "weld the fluid leak"; the hydraulic cause's help lines and Downed alert say "refill the
     hydraulic fluid". Every "hydraulic pressure" line and the HUD's FLUID row are unchanged, and so are the locale keys and
     the cause's code name (`WolfmedCause.Oil`).
  8. The guidebook: a torn chassis leaks hydraulic fluid (it said oil), a line on the pack and on oil being refused, and
     the autodoc page names welding and rewiring a chassis.
- **Left on oil:** Frontier's Arcadia robots (Shredder, Hijacked Hologuardian, Mobile Blaster Unit) still bleed `Oil`:
  hostile drones, not wound hosts, nobody repairs them, and lighting their trail is part of fighting them. The synth keeps
  `SynthBlood`. `PlayerSiliconHumanoidBase`'s commented-out Oil bloodstream is untouched.
- **Oil in a chassis is foreign.** The pack refuses it ("The hydraulic fluid pack holds something Urist McPositronic does not run on."); the pod leaves it
  in the beaker (it is not on the list) and says its reagent-ignored line, "THAT IS NOT MEDICINE. I WILL NOT USE IT.", when a
  run starts; neither pumps it. **What the analyzer reads:** the fluid figure is the fill of the whole pool
  (`WolfmedLifeSystem.GetBlood` → `GetBloodLevelPercentage`), so oil that got in some other way (an admin solution edit)
  would read as fluid and nothing would flag it; nothing in play puts it there, and the bloodstream regenerates hydraulic
  fluid only.
- **Why nothing burns now.** A puddle writes its reagents' flammability to its tile (`PuddleSolutionFlammability`), and a
  lit welder's hotspot ignites a tile with no plasma only through that. Measured (`HydraulicFluidTest`, in a 3×3 room with
  real atmospherics): the chassis's puddle, 61.5 u of hydraulic fluid, left the tile at 0; a lit welder repaired the breach
  standing in it for 20 s with no hotspot and nobody on fire; the same welder over 60 u of Oil in the same room lit a
  hotspot within 5 s.

**Differs from the spec, and why.**
1. **The organic tends are kept off machine parts** (not asked). Without it they still list on a chassis, stall or fault as
   above, and AUTO could not finish in one clean run with one QUEUE COMPLETE.
2. **`mechanical` on the condition**, beside the asked-for `woundPrototypes`: the rewire's `ElectricalWound` is also an
   organic wound.
3. **The rewire also clears `ElectricalWound`**, which a Shock hit puts on a chassis beside the short circuit, so a rewired
   arm is not left with a wiring wound the pod will not touch.
4. **A weld pass heals by the tool's own numbers** (25 a type), not a fixed 15, and works on the wounds directly once no
   damage is left, as a hand topical does; the hand welder has no such fallback (below).
5. **No fuel and no flame for the procedure,** by hand as well as in the pod, as the existing breach and core welds.
6. **Oil in this tree is not slippery** (no `slipData`), so "slippery like oil" is taken as the intent: the fluid is a slip
   hazard. A leaking chassis's puddle was not one before.
7. **"refill oil" and "Oil N%" now name the fluid.** The spec kept the "hydraulic pressure" and "refill" wording; telling a
   medic to refill oil the pack and the pod both refuse would be wrong.
8. **Names in the file's title case:** "Weld Chassis", "Rewire".

**Found, not changed.**
- **A hand welder on a chassis wound with none of its damage types behind it loops** (read from the code, not run): its
  `CanRepairPart` accepts the wound (the generic wound lists Blunt), the pass removes nothing, and the do-after repeats,
  spending 5 fuel a pass until the tank is empty. A generic wound built from Heat alone is one. The pod's weld does not,
  because of its topical fallback.
- **A queued procedure whose problem an earlier one already solved faults the pod** when its first step cannot be
  performed. On a chassis the tends were the case and are gone; on flesh it is unchanged and worth a follow-up.
- **`SurgeryStopBleeding` still sutures a leaking chassis** at step 4 (the organic step on a breach). Left: it stops the leak
  first, and the weld closes the breach after.
- **Outside the filter, red but not from this change** (run once for the new prototypes; each failure is in code or data this
  change does not touch; not rerun on the base commit):
  `TryAllReactionsTest.TryAllTest` stops at `Oxycodone` (Onyx: Tramadol + Ethanol + Epinephrine, Plasma catalyst), whose
  reactants `WolfmedOpiate` (Tramadol + Ethanol) takes first; `CargoTest.NoCargoOrderArbitrage` finds the
  `WolfmedAutodocPrograms` crate selling for 2880 against a 2200 cost; `EntityTest.AllComponentsOneToOneDeleteTest` logs
  `AutodocSystem.OnMapInit` locking an item slot on an entity with no `ItemSlots`; `GuideEntryPrototypeTests` logs
  `<GuideEntityEmbed Entity="Saline"/>` in `WoundTreatment.xml` (a reagent, not an entity). The rest of that run (cargo, vending,
  fill-level and item sprites, prototype save, chemistry, localization, fluids, entity spawns) passed, the new pack and
  reagent included. `TryAllTest` stops at its first failure, so the new reaction is checked in `HydraulicFluidTest` instead
  (10 oil and 10 silicon make 20).

**Tests.** New: `Scenarios/WolfmedPodWeldsChassisTest.PodWeldsChassisTest` (the spec's four wounds made directly, and a
second IPC hurt by Blunt 60, Piercing 30 and Shock 30; both clear, their FRAME DAMAGE, PANEL DEFORMED, FLUID LEAK and WIRING
SHORTED rows gone, QUEUE COMPLETE once each counted tick by tick, nothing in `FailedProcedures`, the pod's welder never lit,
no tend planned on a chassis, and the human's plan keeps its tends and has no machine work).
`Scenarios/WolfmedHydraulicFluidTest.HydraulicFluidTest` (the reagent, the reaction and the stock; the puddle, its tile, slippery; the lit
welder's repair with no fire and the oil control that lights; the pack refilling 78% → 100% with what left the pack
arriving; the oil pack refused by hand and by the pod, with the line; the hydraulic pack refilling in the pod, 50% → 90%;
no hydraulic fluid into a human).
Migrated: `WolfmedSpeciesSpawnTest` (the IPC's reagent and its spill are hydraulic fluid), `WolfmedEviscerationTest` (the
breach leaks hydraulic fluid), `Scenarios/WolfmedIpcFluidLossTest` ("refill hydraulic fluid"). No test pumped Oil into an IPC:
the scenario's `Transfuse` adds the body's own reagent.

Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt), final run on the final code: 483 total, 474
passed, 0 failed, 9 skipped (dirty-disposed autodoc fixtures: `PodRepairsACoreTest`,
`EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`, `SelfServiceOccupantIsTreatedTest`,
`AutofixModuleIdlesWithNothingToDoTest`, `FixMePlansAndStartsInSelfServiceTest`,
`RequirementFlowWaitsForTheLimbAndRefusesTheWrongOneTest`, `BrainDeadOccupantIsOperatedOnWithoutHoldingTest`,
`QueueMoveReordersTest`, `PodChargesAgainAfterAFailedShockTest`); each passed alone. Two earlier full runs: 479 passed,
0 failed, 4 skipped (before the reaction check and the guidebook lines); and 474 passed, 1 failed, 8 skipped, the failure
`HonestEndingScenarioTest` ("the last words were not whispered", a client chat-history read ten ticks after the whisper, in a
run slowed to 23 minutes against 5), which passed in the other two runs and alone; every skip passed alone.

## Playtest 3, S.A.M. round (2026-09-24)

The owner's four pod reports (`plan/p7/PLAYTEST3-SAM-spec.md`). Branch `Wolfmed-fixes7`, from `36ed230814`. Each was
reproduced before it was fixed; a throwaway diagnostic test (removed) drove the pod through a dozen patients at test and
at real step speed to find what the reports were made of.

**1. Two patients in one pod.**
- **The cause: the second patient was lying on the pod, not in it.** Nothing ever put two bodies in the body container
  (a `ContainerSlot`; `TryInsert`, the Climb in verb and the drag-drop all refuse while it is occupied, and the panel,
  the alarm and the appearance all read the container). But Mono gives every `ConstructibleMachine` a `Climbable`
  (`Machines/base_structuremachines.yml:90`), so the old eject's `ForciblySetClimbing(body, pod)` really did climb the
  body onto the pod. A body that does not move stays there: ejected unconscious, the patient lay at the pod's centre
  for as long as the test ran (without the climb, physics pushes even an unconscious body off the machine's fixture
  within five ticks, to 0.85 tiles south of its centre). A body on the pod's tile is
  drawn on the bed while the lid is open and under the lid while it is closed, so with B inside, A looked like a second
  occupant; when B's run ended and the lid opened there were two people on the bed.
- **Found on the way: a real way in.** A body dropped on an occupied pod fell through the pod's drag-drop to
  construction's (Goob's `InteractUsing(user, body, pod)`), which puts the body in the delivery tray whenever the tray is
  open, that is whenever the pod is waiting on material. The client normally hides the drop, but only when the pod's
  drop check runs before the climb's, which nothing ordered.
- **Fixed.** Eject slides the body to the first open floor tile beside the pod: its front (south), then east, west and
  north, skipping space and anything a mob bumps into; boxed in or off a grid it stays where the container put it. The
  pod refuses every climb onto it (`AutodocComponent, AttemptClimbEvent`, "The pod is for lying in, not on."). A body
  dropped on the pod is the pod's to handle, in or nowhere, before construction's drag-drop and the climb; the drop
  check answers before the climb's; the tray refuses whole bodies (`ItemSlotInsertAttemptEvent`).

**2. AUTO is one run.**
- **What the owner heard, measured.** Every drained queue said QUEUE COMPLETE; every plan after it said AUTO ENGAGED,
  the start line (OPERATOR ACKNOWLEDGED) and the anaesthetic line again, because `TryStart` starts a new dose. Three
  things made a patient take several queues: in self-service (the owner climbing in by himself) the plan was cut to one
  procedure, so AUTO ran exactly one procedure a queue ("it queues 1 item"); a part with something lodged in it gets
  only the removal, and the rest of the part waits for the next plan; and a deep wound's tend stops being valid once it
  drops under the deep window, so the shallow tend waits for the next plan too.
- **The run.** It starts with the module's first plan: AUTO ENGAGED, the start line and the anaesthetic line, once.
  When the queue drains the pod plans again on the spot, with the same bounded planner the module's tick uses
  (`AutoPlan`: `AutoSignature`, `AutoReplans`, `AutoReplanLimit`), and carries on without a word, the patient still
  asleep and the lid still locked. Only a re-plan that finds nothing is the end: QUEUE COMPLETE once, the patient woken,
  the lid unlocked. That QUEUE COMPLETE is the run's last word; the module's next empty look stays quiet ("NOTHING MORE I
  CAN DO" is kept, once per patient, for a patient the module never found any work on). An abort, AUTO switched off, an
  emag, lost power or the patient leaving ends the run. A queue somebody typed that AUTO runs is the start of a run too.
- **Per-procedure announcements are unchanged**: one step line per step family per procedure.
- **AUTO plans the whole triage in self-service.** FIX ME still gives self-service one procedure.
- **Follow-ups the planner can see.** Only for states the pod's own work brings about: (a) work on a part held back only
  because something is lodged in it is queued straight after the removal when the removal is in the same plan; (b) a
  tend of a severity window (the deep tends, `minWoundSeverity`) queues the tend of the window below it on the same
  group and part, since tending only ever lowers a wound. A follow-up (`AutodocQueued.FollowUp`) is checked against the
  planner's own rules at its turn (still listed, not given up, nothing lodged, and for a step that skips the pod's own
  work, more than the pod's own wounds left on the part) and dropped quietly, with no antibiotic
  and no line, if they fail. Nothing else is foreseen: a tend waiting on an artery still waits for the next plan, which
  the run now makes silently.
- **Found on the way: the round's hole was the pod's.** Pulling a lodged round out replaces its wound with a new
  gunshot wound entity, and every wound that appears while the pod works is marked the pod's own
  (`WolfmedPodWoundComponent`), which the `ignorePodWounds` triage steps skip. So a run that pulled a round out of a
  deep chest wound ended with the gunshot untreated. `WolfmedEmbeddedRemovalSystem` now broadcasts
  `WolfmedWoundReplacedEvent`; the pod carries the old wound's standing over to the replacement.

**3. "THIS IS NOT WORKING" while it was working.**
- **What tripped it: a tend pass that could not reach a wound.** Shitmed's tend step returns before HOOK 27 unless the
  body or the part still carries damage of its group. On a wound host that damage is not what the step is for: brute
  packs, ointment and the pod's own damage sync take it off while the wounds stay, the completion check (HOOK 26) and
  the listing (HOOK 24) read the wounds, and the upstream listing condition is satisfied by any damage on the body or an
  open incision. So the tend listed, ran, closed nothing, and after three passes the pod gave it up and said so, with
  every procedure round it visibly working. Reproduced by `NoStallWhileWorkingTest` (packs used before the pod: Stall 1
  and an untreated burn without the fix).
- **Nothing else tripped it.** Forty random patients at test speed and seven owner-like patients (rifle to the chest
  and head, sabre, club, fire) at real step speed: no step other than that tend ever reached a second idle pass.
- **Fixed.** `WolfmedTendUndamaged` (a marked hook in `OnTendWoundsStep`, body in `_WF/Wolfmed/Surgery`): on a wound host
  with no damage of the group on the body or the part, the step tends the part's wounds (HOOK 27) and skips the damage
  half. **The counting** (`NoteStall`): a pass counts only when it was performed, its completion check still says
  "not complete", and the part's signature is the same after the pass as right before it, measured in the same tick.
  The first pass of a step used to count as one whatever it did, so two idle passes after a working one were enough;
  now it takes three idle passes. A pass that changes anything resets the count. `PartSignature` and M6's bleeding
  severity in it are unchanged; a step waiting on a tool, the tray or clothing is never performed and never counted.
- **And the listing** (`WolfmedJudgedByWounds`, a marked condition in `OnWoundedValid`): the tends' "some damage on the
  body, or the part open" gate does not apply to a wound host, which HOOK 24 already lists by wounds a tend can close.
  Without it the same packed patient got no tend at all whenever nothing else on the body happened to carry damage
  (`NoStallWhileWorkingTest` failed that way when run alone). Non-wound-hosts are unchanged.

**4. Stuck on WAITING: Clothing.**
- **The armour check, read** (`SharedSurgerySystem.CanPerformStep`, `.Steps.cs:889-900`, applied in
  `OnToolCanPerform` `:283-293`): a garment in any of the part's slots refuses every step on it with
  `StepInvalidReason.Armor`.

  | Part | Slots that block it |
  | --- | --- |
  | Head | head |
  | Torso | outer clothing, jumpsuit |
  | Arm | outer clothing, jumpsuit |
  | Hand | gloves |
  | Leg | outer clothing, legs (unused in this fork) |
  | Foot | shoes |
  | Tail, other | none |

  Eyes, mask, ears, neck, belt, back, suit storage, pockets and ID never block anything. The owner's goggles, gas mask,
  IFF strobe, headset, back-slot hardsuit, belt and tank were never the problem; the tactical gloves (a hand) and the
  combat boots (a foot) were, and the pod only ever cut the suit and the jumpsuit, so the first hand or foot procedure
  waited for ever and nothing queued after it ran.
- **The pod undresses the blocked part** (`AutodocSystem.Undress.cs`, `ArmorSlots` mirrors the table). The suit and the
  jumpsuit are cut and destroyed as before; everything else in the part's slots comes off whole, into the delivery tray
  when it is empty and onto the floor beside the pod otherwise (the eject's tile rule), and is never destroyed. Before
  taking anything off the pod meets the refusals a person would: the slot container's `CanRemove` (unremoveable items)
  and `BeingUnequippedAttemptEvent` asked as the pod (locked or unremovable clothing, a helmet still attached to its
  suit). A hardsuit helmet comes off with its suit, which is a cut slot. Only the blocked part's slots are touched:
  a hand procedure no longer cuts the suit. The CUT button does the same. Voice: the new `removing` line, "REMOVING
  { $item }." (spoken "Removing.", Info), from the voice table; `Cutting` as before.
- **Something it cannot take off.** The WAITING line names it: "WAITING: <ITEM> ON <SLOT>" (every clothing wait now
  names the first thing in the way). The clothing line is said once; a second procedure held by the same garment is not
  announced again. An AUTO pod that has waited `ClothingCutDelay` after finding it cannot take the garment off gives the
  procedure up the way a stall does (recorded as failed, closed up if the pod opened it, the rest of the queue goes on),
  without the stall's line.

**Differs from the spec, and why.**
1. **Double occupancy's test.** A never "still lies on top and free to climb off": the cause is a body on top looking
   like an occupant, so, as the spec allows for that case, eject slides the body off to the front tile, and the test
   asserts A is off the pod's tile, not in a container, not climbing, and cannot climb back on.
2. **The pod refuses climbing onto it altogether**, and a body dropped on it is the pod's to handle. Not in the spec;
   both are the same bug by other routes (one of them a real second body in the tray).
3. **"Once per patient"** is once per run. A run lasts until a re-plan finds nothing, which for everything the spec
   describes is the whole stay; a patient who gets something new after QUEUE COMPLETE gets a new run with its own AUTO
   ENGAGED and QUEUE COMPLETE.
4. **"Greeting"**: the spec's greeting is taken as the insertion greeting (said once per patient already) and the plan
   line as the start line (OPERATOR ACKNOWLEDGED); AUTO never said I HAVE A PLAN. Both are asserted once.
5. **"Three procedure lines"**: a procedure's announcement is its first step line (`ProceduresAnnounced`); a closure the
   pod adds after a cut-short procedure belongs to that procedure. The fixture (a round lodged in a deep chest wound)
   needs four procedures, not three, because the gunshot bleeds; the test asserts one announcement per procedure.
6. **AUTO plans the whole triage in self-service**, which the spec did not ask for; it was the owner's "queues 1 item".
7. **The unremovable case uses a helmet, not a mask.** The armour check never reads the mask slot for the head, so a
   locked mask blocks nothing; an unremovable helmet is the same case on the slot that does block.
8. **Abandoning for clothing** says nothing more than the clothing line (no THIS IS NOT WORKING).
9. **Found and fixed outside the four:** the round's replacement wound (item 2), the tray taking a body (item 1), the
   tends' listing on a wound host with no damage (item 3).
10. **The follow-ups foresee two states only** (the lodged object out, the deep window tended down). A tend waiting on
    an artery that is still pumping is not foreseen; the run's silent re-plan picks it up.
11. **The stall's reproduction is a packed patient**, the one step found to trip the guard. The spec's other candidates
    (retract, a dry clamp, a seal that completes on the next pass, a step still running, a pass while waiting) were
    checked in the code and in the diagnostic runs and never counted twice.

**Test migration.** `WolfmedAutodocLoopTest.EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`: nothing on the
torso runs ahead of the removal, and the rest of the torso's work is queued after it as follow-ups (it used to assert
nothing else was queued at all); by the end of the queue the bruise has been tended, so no tend is left to plan (it
used to plan it in a second queue).

**Tests.** New in `Scenarios/WolfmedPlaytestThreeSamTest.cs`: `PodHoldsOneTest`, `AutoIsOneRunTest`,
`NoStallWhileWorkingTest`, `PodUndressesWhatItCannotCutTest`, `PodUndressesWhatItCannotCutLockedTest`. Each failed
before its fix: A lay at the pod's centre after the old climbing eject; the old AUTO said AUTO ENGAGED, the start line
and QUEUE COMPLETE per queue (diagnostic transcript); the packed patient drew THIS IS NOT WORKING and an abandoned burn
tend; with only the two cut slots handled, the gloves and boots held all six hand and foot procedures. Measured in
`AutoIsOneRunTest`: removal > stop bleeding > deep tend > tend (> closure); the deep tend comes from the run's silent
re-plan, since it cannot list while the round is in; AutoEngaged, start line, anaesthetic line, greeting and
QueueComplete once each, four procedure announcements. In `PodUndressesWhatItCannotCutTest` the gloves went into the
tray and the boots onto the floor beside the pod; in the
locked case the line read "WAITING: HELMET ON HEAD" and both head procedures were given up, the torso treated.

Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt, final code): 486 total, 481 passed, 0 failed,
5 skipped (dirty-disposed: `AutofixModuleIdlesWithNothingToDoTest`, `DeathDuringAProcedureHoldsAndResumesTest`,
`FixMePlansAndStartsInSelfServiceTest`, `QueueMoveReordersTest`, `SelfServiceOccupantIsTreatedTest`); each passed alone.

## Playtest 3, pod atmosphere (2026-09-24)

The owner: "Autodoc needs to have its own safe atmosphere for the patient; outside atmosphere gets in when the pod is
broken." The occupant sat in a plain container, so breathing, pressure and temperature all fell through to the pod's
tile: a patient in a pod standing in vacuum, plasma or a fire got exactly what the room had.

- **Sealed means** an occupant under the lid, the pod powered, the hull intact and the pod not emagged
  (`AutodocSystem.GetSeal`, which answers with the worst reason: Breached, then Open for an empty pod, Unpowered,
  Vented, and otherwise Sealed). It is built the way sealed entity storage is: `WolfmedAutodocOccupantComponent` goes
  on the body on insertion and comes off on eject, and answers `InhaleLocationEvent`, `ExhaleLocationEvent` and
  `AtmosExposedGetAirEvent` with the pod's own mix while the pod is sealed. Unsealed, the three events fall through to
  the tile exactly as before. A patient breathing from their own tank keeps it.
- **The mix.** `WolfmedAutodocAtmosphereComponent` on the pod: 400 L (a coffin's worth) at 101.325 kPa and 293.15 K,
  21% oxygen and 79% nitrogen, all prototype fields. It is put back to those figures every time the sealed pod hands it
  out (every breath, every exposure update, every pressure check), which scrubs what the occupant breathed into it and
  keeps a hot or cold room off them. It is server state and is not networked.
- **The temperature hold is the exposure itself.** The surface temperature M5's core follows is exchanged with the
  pod's 293.15 K air, so the pod sets no `ParentColdDamageThreshold`: that would also replace the occupant's own
  temperature damage thresholds and move every M5 line for as long as they lay in the pod. Measured over 60 s, sealed:
  in vacuum the surface stayed at 310.4 K and the core at 310.2 K while a body outside fell to 16 K; in 400 K air the
  same 310.4 / 310.2 K while the body outside reached 385 K and took 70 Heat.
- **Unsealed**: no power (fans off and the seals slack; power loss already pauses a run and says "POWER LOST. DO NOT
  MOVE."), the lid open or forced (forcing it ejects the patient anyway), the hull breached, or the pod emagged. The
  emag rules already make an emagged pod hostile ("Emag is a threat, not a tool"), so an emagged pod vents its patient
  to the room on purpose. Measured in vacuum: suffocating 8 to 9 s after the seal goes (the respirator's 2 s cycle and
  its three short cycles), breathing normally again 3 to 6 s after it comes back.
- **The broken state.** The pod had none: `faultDamage` (60) only stops a running procedure, and the Destructible
  threshold at 150 turns the pod into a machine frame. A new threshold at 100 acts `Breakage`;
  `WolfmedAutodocAtmosphereComponent.Broken` is set on `BreakageEventArgs` and cleared by any damage change that takes
  the total back under that line (`DestructibleSystem.DestroyedAt`, which is the lowest breakage or destruction
  threshold). With somebody inside, the pod says "HULL BREACH. OUTSIDE ATMOSPHERE." (a new Urgent line, 2.05 s, one ogg
  and one transcript from the generator's table). The sprite takes a scorched tint on whichever layer shows
  (`WolfmedAutodocAtmosphereVisuals.Breached`), and examine says so whether or not anybody is inside.
- **Repair** is a welder: 5 s and 5 fuel through the stock `Repairable`, which takes all the damage off. Mono's
  `BaseStructure` gives every structure a `Repairable` that only accepts the nanite applicator, so the pod lists both
  Welding and Applicating, the way windows do.
- **The readout** is a label after the status in the window's header: SEALED (green), LID OPEN (dim), UNSEALED: NO
  POWER, HULL BREACH: OUTSIDE ATMOSPHERE and UNSEALED: VENTING (red), sent in the BUI state as `Seal`. The window
  closes without power (`ActivatableUIRequiresPower`), so UNSEALED: NO POWER is in the state and on examine but a player
  never sees it in the window. Examine of the pod says in one line whether the patient's air is protected.
- **The voice.** A breach with an occupant says the new line once, whatever the pod was doing. Power loss mid-run keeps
  its existing line. The emag keeps its own.

**Differs from the spec, and why.**
1. **Regenerated on every read, not on the atmos tick.** The partial cannot have an `Update` of its own and the pod's
   only one is in `AutodocSystem.Procedure.cs`, which another branch is rewriting. The occupant never sees the pod's
   air except through those reads, so what they breathe and feel is the same.
2. **No `ParentColdDamageThreshold`** (above): the sealed air is the hold, and it covers heat as well as cold.
3. **The breach line is spoken on any breach with an occupant**, not only mid-run: a patient lying in an idle pod loses
   their air all the same.
4. **The in-window readout never shows NO POWER** because the window closes without power; the state carries it and
   examine says it.

**Found on the way.**
- Upstream `EntityStorageSystem`, `CryoPodSystem` and `MechSystem` subscribe to the by-ref `InhaleLocationEvent` and
  `ExhaleLocationEvent` with by-value handlers, so the gas they set lands on a copy: a welded locker, a cryo pod and a
  mech never change what their occupant breathes (their `AtmosExposedGetAirEvent` handlers are by ref and work). Not
  touched here; the pod's handlers take the events by ref.
- In this fixture an assertion that fails inside a pair callback can come back as Skipped ("dirty-disposed"), not
  Failed. The rerun-alone rule is what tells a real failure from an ordering skip.
- The power net writes `Powered` every 0.5 s (Mono), so a test that waits ten ticks for a fresh pod's power can miss
  the first update. The new tests wait on `IsPowered`.

**Tests.** New `PodAtmosphereTest`: `SealedPodInVacuumTest` (60 s in vacuum: no suffocation, no damage, normal
temperature, a control outside suffocating and frozen, exhaled gas scrubbed on the next read, the marker gone after
the eject), `PowerCutUnsealsThePodTest`, `BrokenPodUnsealsUntilWeldedTest` (the breach, its line, readout, sprite key
and examine; suffocation; a real welder repair through `InteractUsingEvent`; breathing again),
`SealedPodInHotAirTest` (400 K air), `OrganicQueueCompletesInsideTheSealedPodTest` (a fracture mend runs to
Complete in a sealed pod in vacuum), `EmaggedPodVentsItsPatientTest`.

Run (DebugOpt): `Autodoc|PodAtmosphere` 48 total, 44 passed, 0 failed, 4 skipped (`LongQueueDosesOnceAndWakesThePatientTest`,
`AutofixModuleIdlesWithNothingToDoTest`, `EjectOnlyAlarmsWhileRunningTest`, `PodMendsAFractureThroughTheRealStepsTest`,
dirty-disposed, not rerun alone at the orchestrator's request). The full filter's one attempt ran 183 tests (176 passed,
0 failed, 7 skipped) before its test host crashed; the orchestrator runs the full suite after the merge.

## Main merged, module standards (2026-09-25)

`origin/main` (44 commits, up to #81) merged into `Wolfmed` in the cloud checkout. One conflict: main moved the
Wolfgate vendor inventory to `Resources/Prototypes/_WF/Traders/Catalog/VendingMachines/Inventories/wolfgate.yml`
and renamed it `WFWolfgateVendInventory`; the Wolfmed stock (field medicine, autodoc disks and modules) went into
the moved file and `WolfmedHydraulicFluidTest` reads the new id.

Main's #70 and #72 brought AGENTS.md's module rules and `Tools/_WF/Ci/modules.py`, which CI runs (`--check`, and
`--pr-check` against the PR base). The module is now laid out and marked the way they ask:

- **Folders.** `Tools/_WF/wolfmed` and `Resources/Locale/en-US/_WF/wolfmed` are `Wolfmed` (PascalCase in every
  area); the design docs moved from `Docs/Wolfmed` to `Docs/_WF/Wolfmed`. Path references in the generator
  scripts, generated headers and the analyzer test follow.
- **Markers.** Every Wolfmed marker outside `_WF` (upstream files and the ported `_Onyx` tree, about 700 lines)
  is in the generator's grammar: `// WOLFGATE(Wolfmed): reason`, `// WOLFGATE(Wolfmed) START: reason` ...
  `// WOLFGATE END`. The old tags (`(P3-D14)`, `(M1a)`, `(BRAIN)`, `(playtest 3)`, ...) are kept as the
  first words of the reason, so a marker still points at its DECISIONS section. Ported files that carried no
  marker (Onyx components, the vendored `StatusEffectNew` framework, four Onyx locale files) got one at the top:
  `ported from Onyx for Wolfmed` or `StatusEffectNew framework vendored from upstream SS14`. `Resources/Textures/_Onyx/`
  and the autodoc placed in `Resources/Maps/_NF/POI/medical.yml` are `unmarked` entries in
  `Tools/_WF/Ci/modules.yml`, since sprites and maps cannot carry a comment. `_Onyx` stays where it is, as
  AGENTS.md allows for content ported from another fork.
- **README.** `Content.Server/_WF/Wolfmed/README.md`, with a hand-written overview and the generated file list
  and non-modular edit list (251 files outside `_WF`, each with its reasons). That generated list is now the list
  of record for the module's upstream edits; `WOLFMED_MANIFEST.md` stays as the port-time manifest and is not
  maintained further. `Docs/_WF/NONMODULAR.md` regenerated.
- **Style.** `[Dependency]` fields in `_WF/Wolfmed` lost their `readonly` (201 fields, AGENTS.md's form). No
  license headers were present. Namespaces that do not follow the folder are all partials of upstream systems,
  which keep the upstream namespace by the rule.
- **Not done: prototype IDs.** AGENTS.md wants a `WF` prefix on Wolfgate's own prototype IDs; the module's are
  `Wolfmed*` (268) and unprefixed autodoc, surgery and HUD ids (about 190). Renaming them touches saved data,
  `migration.yml` and every test and locale key, on a branch three playtests deep, so it is left for a decision
  by the owner rather than done in passing.

`python Tools/_WF/Ci/modules.py --check` and `--pr-check origin/main` both pass.

## The full filter in 16 GB: test pairs that never died (2026-09-25)

The full Wolfmed filter had never finished: the owner's run died at 183 tests, and the first cloud run died at
exactly the same count with "Out of memory". Each test that fails inside a pair callback ("dirty-disposed",
reported as Skipped) throws its pair away and the pool boots a new one, about 2 GB a pair, and the thrown-away
pairs never left memory. Two probes with a full dump and `gcroot` found three holders, all fixed on the
`Wolfmed` branch:

- **`SymphonyHubSystem`'s timer** (`Content.Server/Symphony`, main's hub module). Its `System.Threading.Timer`
  sits in the runtime's global timer queue, and the callback held the system, so a stopped server stayed
  reachable with everything in it. The engine's test pool stops a server through `BaseServer.Cleanup()`, which
  never runs entity-system `Shutdown()`, so the dispose in `Shutdown()` never ran. The callback now reaches the
  system through a `WeakReference` and disposes the timer once the system is gone. This holds on main too.
- **The fixture object.** NUnit keeps every per-test fixture object until the run ends, and `GameTest`
  (`Content.IntegrationTests/Fixtures`) kept its pair through `Pair` and the injected sided dependencies
  (`_serverCfg` was the chain's entry). `DoTeardown` now clears every instance field of the fixture, from the
  test class up to `GameTest`, in a `finally` of its own, because a dirty dispose throws and a first attempt
  placed after the dispose never ran.
- **The autodoc font cache** (`AutodocStyle.Fonts`) was keyed by each client's `IResourceCache`, and a `Font`
  reaches its client through the font manager's sawmill, so every client that had built the pod window or the
  synthetic HUD stayed in memory. Both ends of the cache are weak now; a font lives as long as a control uses it.

What it looks like after the fixes: RSS still climbs to about 11.5 GB, because the collector lets garbage
accumulate up to the 12 GB hard limit (75% of the box) before a full collection, and then falls back; a dump
at that point shows the dead pair with zero roots. Three runs were killed early on that climb before the dump
proved it harmless. Run 6, left alone: 495 tests, 485 passed, 1 failed (`OxygenScenarioTest`, a 4 s drain
bound measured at 5 s while a dump analysis competed for the CPU), 9 skipped (dirty-disposed), 19.9 minutes.
The pool-reuse skips move around between runs (twenty distinct tests over six runs) and every one rerun alone
has passed; `HonestEndingScenarioTest` failed its whisper assertion in two parallel runs and passed alone.
Those order-dependent failures are the next thing to chase; none is a wound-system regression.

Also fixed on the way: `WolfmedHydraulicFluidTest` reads main's renamed `WFWolfgateVendInventory`.

## AGENTS.md audit (2026-09-25)

After the module layout, an audit of the whole module against AGENTS.md: eight Opus auditors, one per rule family
(markers, layout and generated docs, code style, Fluent, engine traps, one-subscriber-per-pair, upstream edits,
docs and tests), each finding then judged by two more auditors told to refute it. 123 findings, 97 confirmed,
26 refuted, 0 duplicate-subscription problems. Everything confirmed was applied, in six agent passes on a branch
of its own, then built and run:

- **Markers, made precise.** A single-line marker had been standing in for whole blocks: a braced `if`, a new
  method, a rewritten `DoAfterArgs`, an added enum member, a re-indented upstream line. About forty files outside
  `_WF` now have `START`/`END` blocks where the edit spans lines, a marker on every added `using` and brace,
  one-clause reasons with the notes below them, and every rewritten or deleted upstream line kept as a comment
  inside its block (the PassiveDamage blocks of the organic base, the shadekin and the protogen subspecies, the
  tourniquet's Healing keys, the analyzer window's doll and buttons, `SetActiveBodyPart`'s body, the surgeries'
  conditions, the threshold system's lines). A pseudo-block written as `start`/`end` reasons is a real block.
- **New types in upstream namespaces.** `DamageDealtEvent`, `OrganGotInsertedEvent`/`OrganGotRemovedEvent`, the
  stun and entity-prototype compat extensions and `PartStatusSystem`/`PartDamageSeverity` were Wolfgate's own
  types declared in `Content.Shared.Damage.Systems`, `Content.Shared.Body`, `Content.Shared.Stunnable`,
  `Content.Shared.StatusEffectNew` and `Content.Shared._Onyx.Targeting`, which the rule allows only for a partial
  of an upstream type. They live in `Content.Shared._WF.Wolfmed.Compat` now, with marked usings at their callers;
  `ArmorPartModifier` has its own file in `_WF/Wolfmed/Armor`.
- **A sandbox violation.** `PartModifiers = [];` on the armour component, a collection expression assigned to a
  `List<T>` in Shared, which the client's sandbox check rejects and the compiler does not. It is `new()`.
- **Fluent.** Nineteen analyzer-panel keys had been appended to the upstream analyzer locale file, and eleven
  Wolfgate-authored keys sat in `_Onyx` locale files; they are in `_WF/Wolfmed` (`analyzer-panel.ftl` and
  friends) and the upstream file is main's again. Missing keys defined (the brain-death and cardiac-arrest
  banners, the targeting popups), hard-coded strings localised (the chassis tool names, the pod's "S.A.M.", the
  reservoir units, the wound count, the organ a procedure requires), and the locale coverage test guards the
  new keys.
- **Summaries.** 49 public types, enums and methods got their one-line `/// <summary>`; 71 summaries of four
  lines or more were cut to one line with the design notes kept as `//` lines below.
- **A test bound.** `OxygenScenarioTest` asserted that the drain stops within 4 s of the internals going on;
  the respirator breathes every 2 s and a pooled pair's respirator is mid-cycle, so the documented 3 to 6 s is
  the bound now.

Not changed, on purpose: the `Wolfmed*` prototype ids (the `WF` prefix rule; a rename touches saved data), the
`_Onyx` folder (ported content may keep its fork's folder), and the `HealthChange` effect, whose rewritten
argument is used by its healing test.

Run 8, the audited tree, full filter: 495 tests, 485 passed, 0 failed, 10 skipped (dirty-disposed), 19.2 minutes,
peak RSS 9.7 GB; every one of the ten passed alone. `modules.py --check` and `--pr-check origin/main` pass.

**`HonestEndingScenarioTest`, found.** Not order-dependent after all: the body still stutters from the defibrillator
shock and the repaired brain when it whispers its last words, and the engine's stutter drops a consonant about 3%
of the time per consonant, so "crew of the" lost a letter in roughly one run in six. The old `Unstutter` helper
undid repeats but not a dropped letter. The test now removes the stutter in the same server step as the whisper
and reads only the chat its own section adds, since a pooled pair's client keeps its history; thirty repeats
passed. `CriticalHearingTest` reads the shared history the same way and could pass falsely, never fail; left as is.

## The WF prefix, and the chassis banners (2026-09-25)

**Prototype ids.** AGENTS.md gives Wolfgate's own prototype ids a `WF` prefix. The module's 334 own ids now carry it,
mechanically: `WolfmedSplint` is `WFWolfmedSplint`, `MachineAutodoc` is `WFMachineAutodoc`, `SurgeryMendFracture` is
`WFSurgeryMendFracture`, `SyntheticHudDent` is `WFSyntheticHudDent`. A mechanical prefix keeps the module's name in
the id and cannot collide with anything upstream; the 99 concrete entity ids have `migration.yml` entries (inside the
`WOLFGATE(Prototypes)` block) so saved ships and maps keep loading, and the medical POI's pod is renamed in place.
The 26 abstract ones (base parts, organs, the disk and organ-step bases) get no entry: nothing abstract is ever on a
map, and `MapMigrationSystem` asserts that every target is an indexed entity prototype, which an abstract one is not.
No renamed kind is saved in profiles, loadouts or consent rows, so `WFLegacyPrototypeIds` needs nothing.

Kept as they were, on purpose, because their ids are keys into another prototype's namespace or come from code:
- `wolfmedTreatmentProcedure`: the id is the wound's id (`BluntWound`, `WFWolfmedGrazeWound`), or `Cond` plus the
  condition (`CondInternalBleeding`), with `Mechanical` appended, built by `WolfmedTreatmentAdvice.ProcedureId`.
  The ones that mirror a Wolfmed wound followed that wound's new id.
- `autodocProcedure`: the id is the surgery's id, looked up by it; the ones for Wolfmed's surgeries followed them.
- `wolfmedConsciousnessCause`: the enum member's name (`Blood`, `Hypoxia`, `CoreHeat`).
- `wolfmedSpeciesException`: the species id (`IPC`, `Synth`, `Diona`).
- `Spaceacillin`: the upstream reagent id, put back after upstream dropped it, so old solutions still hold it. Its
  bottle is the module's own entity and is `WFSpaceacillinChemistryBottle`.
- The three guide entries (`WFWounds`, `WFWoundTreatment`, `WFWolfmedAutodoc`) were renamed by hand: `Wounds` is
  a word, and a mechanical replace would have hit the `_Onyx.Wounds` namespace.
Onyx's ported prototypes (`_Onyx`) keep their ids, as AGENTS.md allows. The Docs folder was left as history.

A blind replace also renames every other word that equals an id. Five component names do (`WolfmedSplint`, `WolfmedSkinGraft`, `WolfmedHitSplatter`, `AutodocDefibModule`, `AutodocAutofixModule`: the entity and the component that marks it share a name), so their `- type:` lines and the pod's module-slot whitelist were put back; the YAML linter caught the first, a YAML walk over every changed file the rest. Component names stay unprefixed: they come from the class name, and AGENTS.md's rule is about prototype ids. The autodoc's voice picks a line by the tool's
component name too (`case "WolfmedSkinGraft"`), so that string went back as well.

The id list was built one kind per id, so an id that names two prototypes kept only the kind seen last. Ten fell
through that way: six chassis surgeries (`SurgeryRepairCore`, `SurgeryWeldChassis`, ...) whose `autodocProcedure`
shares the id, and four burn wounds (`WolfmedCharringWound`, ...) whose treatment procedure does; nine entities that
are also a lathe recipe or a construction graph got no migration entry for the same reason. A walk over every id
defined under the module's prototypes, against the map, found them; all are renamed and listed now.

Locale keys built from an id leave the prefix out. `WolfmedTreatmentAdvice.Slug` kebab-cases a wound id into the tail of its advice key, and a blind slug of `WFWolfmedGrazeWound` is `w-f-wolfmed-graze-wound`; the slug now skips a leading `WF`, so the 80-odd `wolfmed-treatment-short-*` keys keep their names and `WolfmedTreatmentAdviceTest`, which pins the derived key, keeps passing. The surgery step popups go the other way: `surgery-popup-step-{id}` is built from the raw id in `_Shitmed`, so those keys were renamed with the ids.

**The chassis banners.** A chassis shutdown row and a positronic core failure row reused the `cardiac-arrest` and
`brain-death` conditions, so their tooltip and treatment window read "Cardiac arrest" and "Brain death". A banner
row can now carry a label apart from its condition: the two read "Shutdown" and "Core failure"
(`health-analyzer-wound-banner-shutdown`, `-core-failure`), and they open the treatment window as mechanical, so a
`...Mechanical` procedure is used where one exists and the generic one otherwise. The condition itself is unchanged,
so the window's defibrillator and brain-repair advice still applies.

## CI's full suite (2026-09-26)

The pull request runs every integration test, not the module filter, and six of them were red on Wolfmed's account; all are fixed rather than noted. `AllComponentsOneToOneDeleteTest` adds each component alone, so the pod's map-init locks its tray only when the entity has slots. `WolfmedBleedSpurtSystem` asked a bloodless trader NPC for its blood level and logged an error every tick; it checks for a bloodstream first. The autodoc program crate cost 2200 against contents that sell for 2880; it costs 3000. The improvised splint's construction node named an entity without a `Construction` component; the entity carries one now. `WFWolfmedOpiate` was tramadol and ethanol, a subset of Onyx's oxycodone, and `TryAllReactionsTest` pours one reagent at a time, so the opiate fired before the oxycodone could; it is tramadol and sugar. `SynthRechargeTest` emptied the synth's own cell before the drink, and a chassis at zero charge is shut down here and cannot finish the DoAfter; the test leaves it five percent. The last is the one real behaviour change of the module that test showed: a synth that runs fully dry can no longer drink its way back on its own.

The job itself then died twice at about fifty-five minutes with the runner shut down and its log gone, which is what GitHub does when the VM runs out of memory. CI runs the integration tests under server GC, which grows the heap freely; locally, under workstation GC, the module's filter peaks at nine gigabytes on a sixteen-gigabyte machine. The test step now sets `DOTNET_GCHeapHardLimit` to twelve gigabytes, so the collector works before the runner dies and a real overflow fails the test host with a message instead of taking the log with it.

**The pool's dirty disposes were failures all along.** Eight to ten Autodoc tests came back "Skipped" in every shared run and passed alone; the skip was the pool's `Test was dirty-disposed` warning, which CI maps to a failure, and the assertion behind it never reached the report. Instrumenting the fixture showed it: `autodoc.TryStart` refused a freshly built pod because `IsPowered` read the receiver's `Powered` flag, and Monolith's power net refreshes that flag only every half second, so on a reused pair (whose clock is anywhere in that half second) a pod spawned ten ticks earlier was still "unpowered". A receiver that needs no power now counts as powered at once.

**Two CI jobs.** The integration suite runs as `core` and `wolfmed` matrix jobs: spawning every entity prototype beside a full pool of pairs needs more than one runner has once Wolfmed's mobs carry parts and wounds, and the module's half is the size of main's whole suite anyway. The heap limit stays, at fourteen gigabytes.

**Three tests that passed alone.** The module's job then ran to the end with three failures, each order-dependent. Two of them lost their message: NUnit rebuilds a test's message from its recorded assertions once the pair's dirty-dispose warning adds one, so a failure raised as an exception (any assertion inside `WaitAssertion`) vanished from the report, and only "Test was dirty-disposed" was left. The fixture now prints the failing test's message and stack into its output before the pair is disposed; that printout is what found the other two. `PodChargesAgainAfterAFailedShockTest` killed the occupant and ran ten ticks before forcing the roll, but the pod shocks a dead occupant on its own tick, on whatever roll the previous test left in `WolfmedRevivalSystem.ForcedRoll`, and a shock that took reset the count the test then read; the roll is forced and the pod's next shock pushed out before the death. `DownedCanPatOutFireTest` polled for Downed once a second and then waited three more at ten fire stacks, and on some seeds the pain climbed from the Downed line to a faint inside that window; it polls five times a second and cuts the fire to one stack once the patient is down. `HeartbeatTracksLocalPlayerCritTest` set `MobState.Critical` on the component, but consciousness owns a wound host's mob state and writes it on every evaluation, so an evaluation landing on the same server tick undid the change before the client ever saw it; the helper puts the body under through an outside pressure, which is Critical for as long as it is set.

**Spawn-all, one at a time.** With the suite split, the `core` job still went out of memory at the fourteen-gigabyte limit, six minutes in: `SpawnAndDeleteAllEntitiesOnDifferentMaps` and `SpawnAndDeleteAllEntitiesInTheSameSpot` started within three seconds of each other on the two workers, and each holds every entity prototype at once, about six gigabytes now that a mob carries parts, organs and wounds. Everything after them failed on a pool the overflow had broken. `EntityTest` is `[NonParallelizable]`: NUnit runs it in its own shift, with nothing else in flight, so the peak is one spawn-all beside idle pairs. That was not enough either: run locally under CI's settings, the core filter reached thirteen gigabytes when the first spawn-all test ran on top of a pool that had served eighteen hundred tests, and the kernel killed the host. `EntityTest` is a third matrix job, `entity`, where the pool holds only the pairs it needs; the heap limit goes back to twelve gigabytes, since a sixteen-gigabyte runner also has to hold the process's non-heap memory and the OS.

**The lathe's orphan recipes.** `AllTechPrintableTest`, which the overflow had kept from running, failed next: the medical lathe listed the pod's program disks and modules as a dynamic pack, and no technology, tech disk or blueprint unlocked them, so a lathe could never print them. The six recipes are unlocked by `AdvancedTreatment`, the tier-two civilian technology that already unlocks the surgery tools. The local core filter also failed two map tests on a pool race that is RobustToolbox's, not this repository's: a clean-returned pair can be taken by the other worker before the returning test's `await using` disposal runs, and that disposal then kills it under the new owner. It needs a slow, loaded machine to show and is left alone.

**What the different-maps spawn-all found.** With a runner to itself the fixture ran, and `SpawnAndDeleteAllEntitiesOnDifferentMaps` showed two things nothing else had. On the runner, a cockroach gibbed by its own destruction trigger flung its `food` solution entity: a gib dumps every container on the body, a solution lives in a `solution@` container, and a solution entity has no physics, so the fling logged an error. Gibbing now skips `solution@` containers; a solution goes with its owner. Locally, the same test hit a debug assertion in the deletion phase: entities are deleted in query order, so a body's blood solution can go before its heart, and the organ-removed hook then asked the bloodstream for a level through a cached solution that was gone. `WolfmedLifeSystem.GetBlood` checks the solution is still registered before reading it. That test needs about nine gigabytes of heap on its own now (it holds every prototype alive at once, and a mob is many entities), so it fits the twelve-gigabyte limit only with the pool it gets in the `entity` job.

**A fake power loss, undone by a real event.** `PowerLossPausesAndRestoreResumesTest` raised its power-loss event ten ticks after spawning the pod, and on some runs the power net's first refresh, which flips a new receiver to powered and raises the real event, landed in the two seconds after it and resumed the pod. The test waits a full second before the fake loss.

## CI split reverted (2026-09-26)

The owner asked for `build-test-debug.yml` back as main has it: one integration job, no matrix, no heap limit. The
three-job split (`core`, `wolfmed`, `entity`) was a memory workaround from before main's #88 fixed the leaked test pairs,
and it changed CI for every PR in the repo. Kept: `EntityTest`'s `[NonParallelizable]` (two spawn-all tests at once are
the single job's memory peak) and `GameTest`'s failure print (a failed assertion survives the dirty-dispose warning).
If the single job runs out of memory again, the fix is the spawn-all tests' footprint, not the workflow.

## IPC organs take less per hit (playtest 3, 2026-09-26)

The owner, as an IPC in front of a ballistic turret (Piercing 22 rounds, seven in two seconds), went into shutdown
(COOLANT PUMP OFFLINE) on about the sixth round. Measured (`IpcTorsoLastsAsLongAsAHumanChestTest`): a human chest under
the same rounds loses its lungs on hit 3 and its heart on hit 5, so the chassis was level with flesh, but its torso
holds only the core and the pump, so both took the full `wolfmed.organ_hit_cap` (5) every round, and the core, which
is death rather than arrest, went on hit 8. `WolfmedOrganComponent.HitCap` now overrides the global cap per organ; the
positronic core and the coolant pump (IPC and synth) set 2.5. Turret rounds: pump on hit 10, core on hit 16. Standard
rifle rounds (Piercing 14): core on hit 16 (was 13), human heart unchanged at 13. Humans and headshots are untouched.

## The Debug client and gear it arrived wearing (playtest 4, 2026-09-26)

The owner's client closed on a slot click right after `spawnoutfit`, and once in playtest 2, with
`HideLayerClothingSystem.SetLayerVisibility` asserting that the item's `InSlot` is set. Measured
(`WolfmedSpawnedGearUnequipTest`): a client that first sees a mob already dressed never gets the equip for that
gear. The engine applies the mob's container state before the mob's `InventoryComponent` is initialised, so
`InventorySystem.OnEntInserted` finds no slot definition, raises no `GotEquippedEvent`, and `ClothingComponent.InSlot`
stays null on the client (the visuals come from `ClothingVisualsSystem.InitClothing` instead). Every player mob reaches
every other client that way, and the local player's own mob when it spawns with gear. Taking a layer-hiding item off
then reaches the assert; Release builds compile it out and carry on with `SlotFlags.NONE`. Not a Wolfmed regression:
nothing in the chain is ours. The marked fix returns early from `SetLayerVisibility` when the slot is unknown, since
the server's `HiddenLayers` state carries the change anyway. Raising the equip events at inventory init on the client
was rejected: it would fire `DidEquipEvent` on every system for every mob the client meets.

## Limbs hold less pain (playtest 4, 2026-09-26)

The owner asked whether an appendage's pain is capped: it should not be possible to reach pain crit from a run of
bullets into one arm. Measured: Onyx clamps every part to its own `SoftPainCap`, which is the body's 135 on every
part, so one arm alone reached 135 effective body pain, past the Downed line (0.95 x 135 = 128) and the pain shock
(130), though never the faint (1.4 x 135 = 189 on the summed parts). Piercing counts 0.67 pain a point, so nine
unarmoured 22-Piercing rounds into one arm did it. Now `WolfmedBodyPainSystem` sets a limb's soft cap when the part
gains its `PainComponent`, from `wolfmed.part_pain_cap_arm` (80), `_hand` (50), `_leg` (90) and `_foot` (50); head and
torso keep 135. One arm tops out at 80, so it can never Down anyone by itself; an arm and a leg (170 summed, 135
effective) still Down; four limbs at their caps (270) still faint. Existing tests put at most 50 pain on a limb.
The line is read once at init, so a changed CVar applies to bodies made after it (`WolfmedPartPainCapTest`).

## The Wolfmed range (playtest 4, 2026-09-26)

The owner asked for a map with every species lined up for testing, loaded by `loadmap`: a walled square with room,
medical supplies, the pod, guns, melee and armour. `Resources/Maps/_WF/Wolfmed/wolfmed_range.yml` is generated, not
hand-drawn: `WolfmedRangeMapGenerator` (an `[Explicit]` test, run with `WOLFMED_RANGE_OUT` set to the file) builds a
48 x 48 steel room with solid walls, inherent gravity, a white ambient map light and standard air on the map
atmosphere, saves it uninitialised (so `NoSavedPostMapInitTest` stays green) and writes the YAML out. Player mobs are
`save: false` (`BaseMobSpecies`), so the file cannot hold them: rows along the
north wall carry one `WFWolfmedRangeSpawner` marker (a self-deleting `RandomSpawner`) per species prototype whose
player mob has a `HumanoidAppearance` (the cyborg "species" has
none), each holding that mob, sorted by id, so a new species needs only a rerun. Along the west wall: tables of medkits, the analyzer,
defibrillator, blood, tourniquets, splints, dressings, ephedrine, hydraulic fluid, a welder and cable, a body bag
and the surgery tools; two Wolfmed debug crates; two beds and an operating table; two pods powered by an RTG into a
substation into an APC with low-voltage cable under the pods (`ApcPowerReceiver.NeedsPower` is not a data field, so
"always powered" cannot be saved). Along the south wall: an AK with a magazine box, an Mk58 with a box, a Kammerer,
two laser carbines, a Mosin, and a knife, machete, fire axe, spear, energy sword and crowbar. Along the east wall:
basic, riot, heavy and bulletproof armour, the basic and security hardsuits with helmets, and the SWAT helmet. The
generator skips any prototype that is missing or categorised DoNotMap and prints the list. `WolfmedRangeMapTest`
loads the committed file, counts one humanoid per playable species and checks both pods are powered, so the map
cannot fall behind the species list quietly. In game: `loadmap 100 /Maps/_WF/Wolfmed/wolfmed_range.yml`, then
teleport to map 100.

*[Correction the same evening: the owner loaded the range with `loadmap` and found it dark, unpowered and empty.
`loadmap` builds its options as `new DeserializationOptions { StoreYamlUids }`, so the map arrives uninitialised: the
species markers never fire (they spawn on MapInit) and the power net never forms, and nothing in a map file can
change that. `wolfmedrange` (`Content.Server/_WF/Wolfmed/Range/WolfmedRangeCommand.cs`, AdminFlags.Mapping) loads the
file with `InitializeMaps = true` and moves the caller to the middle of the grid, so it is the way in. The room is now
34 x 34 (32 floor tiles across, the species in three rows of sixteen), with an always-powered wall light every six
tiles on each wall turned to face in, over a dim `#303030` ambient so nothing is ever pitch black.]*

*[Second correction: still too big, and no oxygen. The room is now 16 x 16 (14 floor tiles across, the species in
four rows of fourteen, tables holding six supplies or two arms each), with a wall light every three tiles on every
wall over a bright `#C0C0C0` ambient. The vacuum: a grid's tiles breathe the grid's own `GridAtmosphere`, not the map
atmosphere, and the generator's grid got one (the mass rule in `AutomaticAtmosSystem`) with every tile empty. The
generator now runs `fixgridatmos` on the grid before saving, so the file carries standard air on every tile, and
`WolfmedRangeMapTest` asserts oxygen at both pods.]*

## Organs take nine heavy hits (playtest 4, 2026-09-26)

On the range a kobold with a machete (Slash 32, 36.8 on a Skrell at 1.15) put the owner into arrest in three hits.
Measured (`WolfmedMacheteTest`): organs held 15 under `wolfmed.organ_hit_cap` 5, so any organ a heavy hit reached
died on the third of them. A human chest shares the reach damage across five organs by a roll, so three hits cost it
the lungs and liver and left the heart at 7.7; a Skrell's torso carries Wolfmed data on its heart and lungs only, so
the same damage fell on two organs at the full cap each and the heart went on hit 3. About eighty-five species organ
prototypes (Vox, Thaven, Resomi, Shadekin, Hydrakin, Goblin, Avali, Arachnid, Dwarf, Yowie, Synth, the Protogen
subspecies and more) lack the data the same way. The owner chose balance over parity: species are allowed to differ,
but no torso should fall to three hits. The Wolfmed torso bases (`WFWolfmedOrganHeart`, `Lungs`, `Liver`, `Stomach`,
`Kidneys`) now set 25, and `wolfmed.organ_hit_cap` is 3 (was 5), so a heavy hit needs nine of its kind to fail the
organ it reaches, whatever else shares the torso. The brain and eyes keep the component's 15. The IPC core (40) and
pump (25) keep their own `hitCap` 2.5. Measured after: machete to the chest, human heart on hit 8 to 10 (the roll),
Skrell on hit 9, both Downed by pain on hit 3 with the heart intact; turret rounds (Piercing 22), human lungs and
heart on hit 9, pump on hit 10, core on hit 16 (`IpcTorsoLastsAsLongAsAHumanChestTest` now asks only that the pump
outlast the heart); rifle rounds (Piercing 14), lungs impaired on hit 10 and failed on hit 14, the heart still beating
at 4.4 when the routing stops taking hits on that torso around hit 19 (`OrganCalibrationTest`: a rifle takes the
lungs, never the heart). Rifles were already killing by blood and pain long before either.

## Treated pain fades (playtest 4, 2026-09-26)

"Pain should decay far faster when a wound is fixed." Onyx recovers a part's pain at one ninth a second whatever its
source, down to the floor its open wounds set, so a bandaged or sutured wound kept its patient hurting, and often
Downed, for minutes. `PainSystem.RecoverPain` now sheds the share of a part's pain that no open wound backs (its
value over `WoundPain`) at `wolfmed.pain_loose_recovery` (3) a second instead, a marked edit: a hit still spikes and
settles onto its wound's floor within seconds, the floor holds while the wound is open, and once treatment clears
the floor the rest goes within half a minute (`WolfmedLoosePainTest`). Painkillers' recovery multiplier still
applies on top of Onyx's rate; the loose rate is a floor on the rate, not a multiplier.

## The pod is for lying in (playtest 4, 2026-09-26)

A body that was down and tried to get into the pod was told "You can't climb while you're down", the playtest 3
table guard, instead of the pod's own "The pod is for lying in, not on" (every constructible machine is climbable).
Entering the pod was never a climb (the verb and the drag both insert, and Downed is allowed), so only the popup was
wrong. `WolfmedDownedClimbSystem` now subscribes after `SharedAutodocSystem`, so the pod's refusal comes first and
the table guard sees the attempt already cancelled.

## Sidearms from the floor (playtest 4, 2026-09-26)

"When in crawl mode, you should be able to fire sidearms." OD7 (b) had every gun out while Downed.
`WolfmedDownedSystem.OnShotAttempt` now lets a shot through when the gun carries the `Sidearm` tag, which
`BaseWeaponPistol` and `BaseWeaponRevolver` give every pistol and revolver, and the small energy guns (taser,
disabler, laser and energy revolvers, the pulse and antique lasers) carry themselves; rifles, shotguns, SMGs and the
rest stay cancelled. Melee, throwing and pulling while Downed are unchanged. `WolfmedDownedSidearmTest` fires the
attempt for a pistol, a revolver, a rifle and a shotgun.

*[Correction the same evening: the Mk-58 still would not fire. `SharedGunSystem.AttemptShoot` asks
`ActionBlockerSystem.CanAttack(user)` before it ever raises the shot attempt, and Downed cancelled every
`AttackAttemptEvent`. That gate now passes when the attempt names no target and no weapon (the gun system's shape)
and the active hand holds a `Sidearm`; melee attempts carry both and stay cancelled. The test now picks the gun up
and asks `CanAttack` as well as raising the shot attempt.]*

## Playtest 4, SEPSIS (2026-09-27)

"Sepsis should have more effects such as vomiting, coughing, and so on. Also not sure if sepsis actually kills any
more, I was playing with systemic infection 100%. I think it should slowly damage organs at 100?", and "we need more
emotes with sound and such, like coughing, choking, etc depending on the wound." Branch `Wolfmed-p8-sepsis` from
`ca7d821e2e`.

**Does it kill: measured first.** `WolfmedSepsisTest.SepsisKillsTest`, real game time with every system running and
the shipped CVars: a human in station air, warm, no wounds, sepsis pinned at 100 every second. On the base, before
any change: Up at 240 s (oxygenation 0.6), Downed by 300 s (0.5), Unconscious by 360 s (0.4, where brain tissue
starts to go), cardiac arrest named "sepsis" at 511 s, dead at 625 s. The M2 clock holds (derived 510 s). Nothing in
the drain was broken: no CVar sits at 0, the M5 refill runs only while no drain does, and the oxygen trigger fires.
After this package: arrest at 511 s and death at 625 s again, the kidneys and liver at 0.85 of 25 when the body died,
the lungs and stomach at 8.9, the heart at 12.9. How a body at 100% lives anyway: sepsis holds only while a source is
alive (a wound at the spreading stage or past it, or a necrotic limb). Once the last source closes or is treated it
falls 8 a minute, is under the 80 line two and a half minutes later, and the brain refills from then; a 100% set by
hand (VV) with no source does the same. And the clock is long: eight and a half minutes to the arrest from 100, more
than ten to death. The drain is left as it is; the organ damage below is the second route the owner asked for.

**Organ damage.** `WolfmedInfectionSystem.TickSepsis`: from `wolfmed.sepsis_organ_damage_from` (90) sepsis takes
`wolfmed.sepsis_organ_damage_per_minute` (1.5) a minute, times the infection profile's new `sepsisOrganWeights` (by
organ slot: kidneys 1.5, liver 1.5, lungs 1, stomach 1, heart 0.75; an unlisted slot 1), off every
`WolfmedOrganComponent` organ in an organic torso, through `OrganHealthSystem.ChangeHealth`. The bands, the analyzer's
organ items and each organ's own effects follow: a failed organ is destroyed and leaves its internal bleed, as when a
hit fails it. Never on the dead; under the line it stops at once, so antibiotics that pull the sepsis under 90 stop
it. The share each 5 s tick owes is carried per organ (`WolfmedSepsisComponent.OrganDamageOwed`): organ health is in
hundredths, and the heart's 0.094 a tick would truncate to 0.09 and add 4% to its clock.
- Measured (`SepsisOrganDamageTest`, brain drain off by `wolfmed.brain_sepsis_seconds` 0, the infection tick
  fast-forwarded 5 s at a time): kidneys and liver fail at 665 s (derived 667), lungs and stomach at 1000 s (1000),
  and the heart has 6.19 of 25 left then (derived failure at 1333 s), the last torso organ standing. After two minutes
  at 100, three units of spaceacillin (sepsis 82) and the kidneys lose nothing more over three minutes.
- With the brain drain on (the default) the brain gets there first, as the spec wanted: death at 625 s with no organ
  failed yet.

**The analyzer.** A new route, `WolfmedRoutes.SepsisOrgans` (bit 10, free since M4 put core heat on bit 15), runs while
the damage does, brain or no brain. "Do first" shows "antibiotics now, sepsis is damaging the organs" in place of the
plain "antibiotics" (that aid gives way, so the line never says antibiotics twice); a waiting ghost is told "sepsis is
damaging your organs"; the sepsis banner reads "SEPSIS - systemic infection at 100%, damaging the organs" (the client
reads the route off the vitals report, so the vendored diagnostics type is untouched). The organ items ("Kidneys
impaired", "Liver failed") were already on the vitals line.

**Condition emotes.** `WolfmedConditionEmoteSystem` (server, no subscriptions). Every `wolfmed.condition_emote_interval`
(8 s) each wound host is checked; the conditions, in priority order:
- **Choke** (`WFWolfmedChoke`, "chokes!"): the brain's largest drain is the airway (suffocating with lungs in place),
  or blood in the airway: an open internal bleed on the torso while a lung in place is impaired or failed.
- **Cough**: `WFWolfmedCoughBlood` ("coughs up blood!") when the lungs are the reason (a lung impaired or failed, or
  none left: the analyzer's "no lungs"); otherwise `WFWolfmedCough` ("coughs.") for an open internal bleed on the torso
  or sepsis at `wolfmed.condition_cough_sepsis` (40).
- **Wheeze** (`WFWolfmedWheeze`): the lungs are the largest drain and oxygenation is under
  `wolfmed.condition_wheeze_oxygenation` (0.6).
- **Retch** (`WFWolfmedRetch`): sepsis at `wolfmed.condition_retch_sepsis` (60), or the toxin load past the Downed line
  (the analyzer's "high"). At sepsis `wolfmed.condition_vomit_sepsis` (90) or in the toxin coma band the retch also
  vomits through `VomitSystem.Vomit` (upstream's 40 hunger and 40 thirst, a puddle, the sound), at most once per
  `wolfmed.condition_vomit_interval` (60 s), and only with a stomach.
- **Shiver** (`WFWolfmedShiver`): a fever running (sepsis, or an infection at the spreading stage) or the core under the
  hypothermia Downed line.

Each condition that holds rolls `wolfmed.condition_emote_chance` (0.35) in that order and the first success plays, one
emote a check; the emote played last check is passed over while another condition holds. Nothing for the dead, a body
in cardiac arrest (it has to look dead), a body in a pod mid-procedure (`AutodocSystem.IsRunning`), sedation at
`wolfmed.sedation_warn_heavy` (0.8), or a body whose torso is not organic (`WolfmedWoundTraitSystem.IsOrganic`, the
infection system's gate; owner, 2026-09-27: a chassis has its own fault lines). The emote goes through
`ChatSystem.TryEmoteWithChat` at `ChatTransmitRange.Normal` with `ignoreActionBlocker` and `forceEmote`: an
unconscious body still chokes and coughs, and the menu whitelist does not decide. The six emotes are ordinary Vocal
emotes in `Resources/Prototypes/_WF/Wolfmed/Voice/condition_emotes.yml` (whitelist `Respirator`, in the emote menu), with
no sounds and no chat triggers, so typing "coughs" still plays upstream's Cough alone.
- Measured (chance 1, real game time): failed lungs cough up blood on every check (three in 24 s); sepsis 95
  alternates cough and retch (eight emotes in 64 s) and vomits once; a healthy human, a dead one with sepsis 95 and
  failed lungs, and an IPC with sepsis 95 play nothing in 60 s.

**Numbers.** `wolfmed.sepsis_organ_damage_from` 90, `sepsis_organ_damage_per_minute` 1.5,
`condition_emote_interval` 8, `condition_emote_chance` 0.35, `condition_vomit_interval` 60, `condition_cough_sepsis`
40, `condition_retch_sepsis` 60, `condition_vomit_sepsis` 90, `condition_wheeze_oxygenation` 0.6;
`sepsisOrganWeights` kidneys 1.5, liver 1.5, lungs 1, stomach 1, heart 0.75.

**Differs from the spec, and why.**
1. **A sixth emote id,** `WFWolfmedCoughBlood`, for "coughs up blood!": an emote's chat line is picked at random from
   its own list, so the bloody cough needs its own id. It should get the cough's sounds.
2. **Lower conditions still show.** Every condition that holds rolls, in order, and last check's emote is passed over
   while another holds. Read strictly (the top condition only), a septic patient past 40 would cough and never retch,
   and the spec's own retch-and-vomit test at chance 1 could not pass.
3. **The airway is read fresh** from `WolfmedLifeSystem.DrainRate`, not from `WolfmedConsciousnessComponent.HypoxiaSource`,
   which keeps the last drain's source while the brain refills, so a cleared airway would go on choking.
4. **"A lung internal bleed"** has no model of its own; it is read as an open internal bleed on the torso while a lung
   in place is impaired or failed. A failed lung is destroyed on the next organ tick, so a body whose lungs failed
   coughs up blood (the spec's test) rather than choking.
5. **Unconscious bodies emote; a body in arrest does not.** The spec's exclusions are dead, a working pod and strong
   sedation; arrest is added because the arrest must look like death (AUTODOC3). "Strong sedation" is the existing
   `wolfmed.sedation_warn_heavy` line ("You can barely stay awake"), not a new CVar.
6. **`SepsisOrganDamageTest` fast-forwards the infection tick** (as `WolfmedInfectionTest` does) rather than waiting
   seventeen game minutes; `SepsisKillsTest` runs in real game time (one to six minutes of wall time depending on the
   machine's load).

**Tests.** New `Scenarios/WolfmedSepsisTest.cs` (`SepsisKillsTest`, `SepsisOrganDamageTest`) and
`Scenarios/WolfmedConditionEmoteTest.cs` (`FailedLungsCoughTest`, `SepsisRetchesAndVomitsOnceTest`,
`NoEmoteWithoutACauseTest`: healthy, dead and IPC). Run with the nearby fixtures (`WolfmedLocaleCoverageTest`,
`WolfmedInfectionTest`, `WolfmedMedicInfoTest`, `WolfmedRevivalTest`, `SepsisNotToxinTest`): 37 total, 37 passed,
0 failed, 0 skipped.
## Playtest 4, VISUALS: the pain HUD, wound and rot overlays (2026-09-27)

Three sprite features from the Bobstation septic icons, converted by `Tools/_WF/Wolfmed/gen_wolfmed_overlays.py` (rerun
it to rebuild all three RSIs) and drawn on the humanoid through the existing `PartDamageVisualsComponent` path.

**The pain HUD.** `WFWolfmedPain`, an alert in a category of its own (`WFWolfmedPain`, ordered straight under
Health so it never replaces the health doll). `WolfmedPainAlertSystem` sets severity round(effective body pain / soft
cap x 7), where effective pain is what the Downed line reads (the body's value, min(135, sum of parts), less relief);
8 (the `paindd` icon) while a pain faint holds the body; hidden at or under `wolfmed.pain_hud_from` (5) and on the
dead. At the 135 cap the bands are: 0 from 5 to 9.6, 1 to 28.9, 2 to 48.2, 3 to 67.5, 4 to 86.8, 5 to 106.1, 6 to
125.4 and 7 above it (Downed at 128). It takes no subscription: `WolfmedConsciousnessSystem.Apply`, which already runs
on every pain change, relief poll and faint, calls it at the end, beside the health doll's own refresh. A machine
shows the same icons as `WFWolfmedPainMechanical`, whose text is sensor overload; the Downed alerts are unchanged.
The icons are Bob's bare 32 x 32 glyphs composited onto the backing the Crescent health alerts draw inside their own
sprites (the alert control draws none): alternating rows of `#0D0D0F` and `#17171B` with the four corners
transparent, read row by row off the side margins of `bleed.rsi/bleed3`, which the drop never reaches. Hover text:
the analyzer prints pain only as a number per part, so there is no analyzer band to reuse; the text uses the light /
strong / terrible / agony words of the self-examine lines (0-1, 2-3, 4-5, 6-7) and "passed out" at 8.
`AlertControl` now passes the alert's severity to its description (one marked line) so the text can follow it.

**The spec's premise about `bulletwound.dmi` does not hold.** It is one 3 x 3 wound glyph with a drip under it,
drawn in the frame's corner (x 0-2, y 24-31), and each `_south/_north/_east/_west` state has pixels only in its own
facing: Bob placed it by pixel offset in code (the file is not referenced by any Bob code today). The `bullet`,
`1bullet` and `2bullet` sets sit at the same spot; they differ in pace, not height: `bullet` holds 1.5 s then drips in
three 0.1 s frames, `1bullet` drips in 0.2 s frames, `2bullet` is a continuous trickle (0.5, 0.2, 0.2 or 0.3, 0.2 s);
`obullet` is the glyph alone. So the generator bakes the glyph onto each of the ten human parts in each direction, at
the pixel nearest the centre of the part's eroded visible area (the share no later layer draws over; a part fully
hidden in a direction, the far arm side-on, gets an empty frame). Glyph centres (south, north, east, west): Chest
(15,16) (15,16) (17,17) (14,17); Head (15,6) (15,6) (16,7) (15,7); RArm (9,15) (21,15) (13,14) hidden; LArm (21,15)
(9,15) hidden (18,14); RHand (8,19) (21,19) (13,19) (11,19); LHand (21,19) (8,19) (20,19) (17,19); RLeg (12,25)
(17,25) (13,25) (14,27); LLeg (17,25) (12,25) (15,25) (16,25); RFoot (11,30) (18,30) (15,30) (14,30); LFoot (18,30)
(11,30) (19,30) (18,30). The per-part set choice became: `drip` is `bullet` on the torso and head and the slower
`1bullet` on limbs (the two drips on one body fall out of step); `stream` is `2bullet` on any part once the part
bleeds at `wolfmed.wound_overlay_stream_rate` (2 u/s: a 20-severity slash bleeds 0.6, a 60 one 3.6, a stump 18);
`old` is `obullet`, shown while the part has an open wound whose bleed has clotted or been dressed (the bleeding
component stays, at rate 0) or an open `DismembermentWound` stump. Grey: luminance normalised so the brightest pixel
(Bob's `#970004`, luminance 45.6) is white, alpha kept; the client multiplies it by the body's blood reagent colour,
which the server writes as `PartDamageVisualsComponent.WoundColor` because `BloodstreamComponent` is server-only
(`#800000` for a body with no bloodstream).

**Rot.** `rot_parts.dmi` in its own colour, 4 directions x 10 frames at 0.15 s, renamed `<Part>_rot`; `rot_chest` and
`rot_groin` are composited into `Chest_rot`, since Wolfgate has no groin. Checked against the human parts: every frame
lands on its own part (most 100 %, the worst 80 %), except Bob's `rot_r_hand` north frame, a copy of its south frame
sitting on the left hand (0 of 14 pixels on the right); it is replaced by `rot_l_hand` north mirrored (10 of 14). Shown
while the part is necrotic or any wound on it is at `WolfmedInfectionStage.Septic`; cleared when antibiotics pull the
infection under the stage, when the part comes off, or with the body.

**One system, one path.** `WolfmedWoundOverlaySystem` (server) writes `Wounds`, `Rot` and `WoundColor` beside
`Degradation` and `Treatments`, for organic parts only (`IsOrganic`: a chassis keeps its struts and wiring). It
refreshes a body on the wound lifecycle broadcast and on `WoundBleedingChangedEvent` (held on
`WoundBleedingComponent`, a free pair; the treatment overlay holds it on `WoundComponent`), and sweeps every wound host
each `wolfmed.overlay_refresh_seconds` (1) for what raises nothing: an infection reaching Septic, a wound closing, a
drug removing a bleed. Unchanged bodies send nothing. The client draws both in `DamageVisualsSystem.Wolfmed.cs`.

**Draw order.** On each limb: the limb, its degradation, its rot, its wound, then the clothing. The torso, head, arms
and legs sit under the jumpsuit (a shirt hides a chest wound and its rot); hands and feet, which Wolfgate draws over
the jumpsuit, sit under the gloves and shoes. The dressing overlay stays where G3 put it, over the jumpsuit and under a
coat. Whichever overlay is created first, the next inserts above the ones below it and an insert under it pushes it
up, so the order holds.

**Species.** Nothing per species offsets humanoid layers today; species height is the mob's sprite scale (dwarf
1 x 0.8, goblin 0.8 x 0.7, rodentia, tajaran and felionoid 0.8, harpy 0.9, avali 0.91, thaven 1 x 1.05, oni 1.2), and
the per-character height and width sliders scale the same sprite. Overlays are layers of that sprite, so they scale
with it and need nothing. What does differ is where a species' own part art sits in the frame, so
`WFWolfmedOverlayOffsets` (`Resources/Prototypes/_WF/Wolfmed/Damage/overlay_offsets.yml`) holds a per-species,
per-part pixel shift, generated by `Tools/_WF/Wolfmed/gen_overlay_offsets.py`: the median vertical difference between
the centre of the species' part sprite and the human one over the four directions, left and right measured together.
Only y is measured, because a layer offset applies in every direction and a sideways shift flips between south and
north. All 41 round-start species are listed; 16 are zero, among them human, dwarf, goblin, oni, IPC, Rodentia,
Vulpkanin and WFCanine. The largest shifts: Resomi (and ProtoResomi) head, chest and arms down 3, hands
down 2; Diona arms down 3; Hydrakin arms and hands down 2, legs up 2; Thaven head up 2.
## Playtest 4, IV (2026-09-27)

"We need to add IVs ... It should act exactly like the one on Nova code. SAM should also have a blood reservoir." Nova's
drip is tgstation's (`code/game/machinery/iv_drip.dm`; Nova's override only turns off mouse-drop). The owner then
corrected the blood half: one blood type, the existing `Bloodpack` stack, no bag variants and no mismatch logic.

**What already existed.** No IV drip anywhere in the tree (no `iv_drip`, `IVDrip` or IV bag in any fork layer). No
blood typing either: a hand-applied `Bloodpack` adds 30 u (`Healing` `ModifyBloodLevel`) through
`BloodstreamSystem.TryModifyBloodLevel`, which always adds the patient's own `BloodReagent`, so the pack is universal;
the pod's beaker transfusion (`DrawFluid`) takes only the patient's own reagent or saline; `Blood` in the chemical
stream only heals a Bloodsucker (its Medicine metabolism), so another species' blood injected from a beaker does
nothing to anyone else.

**The drip** (`WFWolfmedIvDrip`, `Content.Server/_WF/Wolfmed/Medical/WolfmedIvDripSystem.cs`), tg's behaviour:
- One container slot, filled by clicking with the item: a `Bloodpack` stack (tag), or anything with `FitsInDispenser`
  or `DrainableSolution` (tg's `drip_containers`). The flow, mode, container and fill show on the sprite (Nova's
  `iv_drip.dmi`: the stand by mode and flow, `beakeridle`/`beakeractive`, the `reagentNN` overlay at tg's thresholds
  0, 10, 25, 50, 75, 80, 90, tinted by the contents; a pack's fill is the stack's units left against a full stack, in
  the patient's blood colour) and on examine.
- Attach: a verb per body with blood in reach of the stand, or drag the stand onto the patient; one second's do-after
  (`wolfmed.iv_attach_seconds`), "begins attaching". Detach: a verb, or the patient more than a tile away, which rips
  the needle out: `wolfmed.iv_rip_damage` (3) Piercing into a random arm (the chest without one) through the routing,
  a LargeCaution popup to the patient and a line to the room. 3 Piercing alone opens a puncture at severity 3, and a
  puncture bleeds only from 9, so the torn wound is raised to `wolfmed.iv_rip_wound_severity` (10), the way tg adds a
  moderate pierce wound on top of its 3 brute. A patient put in a pod, a locker or any container is
  detached cleanly.
- Inject: a pack gives the patient's own blood reagent at the flow, spending one pack of the stack every
  `wolfmed.iv_units_per_pack` (30, the hand-applied pack's figure), and nothing into a full bloodstream. A beaker or jug
  is injected as a hypospray does (Injection reaction, into the chemical stream); the part of it that is the patient's
  own blood reagent goes back into their blood instead. Take: the patient's blood reagent into a beaker or jug at the
  flow; full, the drip "pings." (a chat emote and a sound) and drops the flow to the minimum, as tg does; under
  `wolfmed.iv_beep_below` (0.85, tg's BLOOD_VOLUME_SAFE) it "beeps loudly." at `wolfmed.iv_beep_chance` (0.025) a
  second, tg's 5% a two-second tick. A pack cannot be refilled: take mode with a pack hung moves nothing and examine says
  why.
- Blood packs follow their own damage-container rule (`Healing` `damageContainers`, Biological), so a chassis is never
  given blood; examine says so.
- Flow: `wolfmed.iv_rate_default` 5, `_min` 0, `_max` 15 (Nova's tripled maximum), `_step` 0.01 (tg's rounding), units
  a second, moved once a second (tg moves on its two-second machine tick).

**The pod's blood reservoir** (`AutodocSystem.Blood.cs`): an `autodoc_blood` slot for a `Bloodpack` stack. A
`transfuse: true` triage step heads `WFWolfmedAutodocTriage`; when the planner reaches it (PLAN, FIX ME, AUTO) with the
occupant under `wolfmed.pod_transfuse_below` (0.85) it starts a transfusion of the occupant's own blood at
`wolfmed.pod_transfuse_rate` (5 u/s) that runs beside the queue until `wolfmed.pod_transfuse_to` (0.95, the last push cut
to the target) or the stack is gone. It queues nothing, so a patient short only of blood gets no procedure and no
NOTHING MORE I CAN DO while it runs. Every place the pod already topped blood up from its beakers (`TryTransfuse`: before
each procedure, the defib's blood gate, the end of the queue) schedules it too, and with the autofix module on the pod
watches the blood every second. With no pack and no fluid in the beakers the pod could use, it says "NO BLOOD LOADED."
(a new Urgent line, 1.31 s, generated like the rest) once and the readout shows NO BLOOD LOADED after the state, never
beside TRANSFUSING; the fault is the pod waiting on blood, so a stack loaded afterwards starts the transfusion by itself.
The reservoir panel gets a fourth row, "blood: blood pack x5", against a full stack's units. Unpowered or emagged, the
transfusion holds. A body packs cannot help (a chassis) is left to the beakers as before, with no fault.

**Measured.** Human at 0.6 with a stack of five on the drip at 5 u/s: 0.9 after 19 s (18 predicted; the first push is a
second after the needle goes in), 90 u given and exactly three packs spent. Take mode into an empty 60 u beaker: full
after 12 pushes, the ping 14 s after the needle went in (on the first push that finds no room), the patient down
exactly 60 u, the flow at 0. The rip: a `PiercingWound` at severity 10 on an arm bleeding 0.45 u/s on a human (at 3
without the raise, and not bleeding). A Slime person on a pack: Slime 180 to 215 u in 8 s and no Blood. The pod: 0.6
to 0.95 in 21 s (21 predicted), 105 u from three and a half packs, stopping at 0.950.

**Differs from the spec, and why.**
1. **The owner's correction** replaced the bag variants, `WFWolfmedBloodBag` and the mismatch path with the existing
   `Bloodpack` stack; test 4 is the Slime patient getting Slime; the pod's only fault is NO BLOOD LOADED. No blood bag
   sprite was shipped, since there is no bag entity.
2. **The flow is verbs, not a slider**: a Flow rate category with Stop (the minimum), Slow (half the default),
   Normal (the default) and Fast (the maximum), each labelled with its u/s. A BUI would have doubled the package.
3. **Alt-click is tg's right-click** (take the needle out, else take the container down, else switch the mode), not
   tg's alt-click (flow to minimum or maximum); the mode verb is always there, as tg's is.
4. **No "IV connected" alert, no line drawn to the patient and no tug** toward the stand: none was asked for.
5. **Not buildable**: no crafting recipe, lathe entry or map placement; spawn `WFWolfmedIvDrip`.
6. **Nothing is spent into a full bloodstream**, where tg keeps pushing blood past normal.
7. **The torn wound is raised to severity 10** so it bleeds, as the spec's "opens a real wound and bleeds" asks; 3
   Piercing on its own opens one that does not.
## Playtest 4, SOUNDS: the Bobmed sound pack and the emote audit (2026-09-27)

The owner handed over 43 sounds from Bobstation reignited (`modular_septic/sound/{emotes,gore,effects}`, matched by
hash at commit `7850ebd9f8`) and asked for each one in a place. They live under `Resources/Audio/_WF/Wolfmed/`
(`Gasp`, `Emotes`, `Bleeding`, `Bone`, `Melee`, `Pill`), each with an `attributions.yml`; 31 of them were stereo and
were downmixed to mono so they attenuate like every other positional sound, the two WAVs became Ogg, and
`tendon_snap2` (96 kHz) was resampled to 44.1 kHz. Every sound a system plays is a `WFWolfmed` collection in
`SoundCollections/bobmed.yml`.

**Flesh only.** Every body noise here plays only for flesh. `WolfmedOrganicSoundSystem.IsOrganicBody` (shared) lets the
torso decide, as the synthetic HUD does, through `WolfmedWoundTraitSystem.IsOrganic`: a human with a cybernetic arm is
flesh and an IPC is not. Anything without a `Body` (a wall, an item) and a borg chassis are not flesh; a body with no
wound-tracked torso is flesh unless it carries `Silicon`. The drips and the emote voices use it on the body, the crack
and the snap use `IsOrganic` on the part itself.

1. **Dying gasp.** `MaleGasp` / `FemaleGasp` (`SoundCollections/gasp.yml`, marked) now hold the owner's 7 and 13 files;
   the ids stay, so all 18 `MaleGasp` and 14 `FemaleGasp` references in the species sets follow. `DefaultDeathgasp` is
   untouched. `gasp_female2` is 7.5 s long, the rest 0.2-3.8 s. A mechanical `WoundHost` body's `Gasp` emote is
   swallowed before any voice is asked (item 2's handler), so any mechanical body whose set maps `Gasp` stays quiet.
2. **Sneeze, and how the new emotes get a voice.** Upstream already has `Sneeze` and `Snore` (`disease_emotes.yml`),
   with sounds in the human sets, but no trigger words, no whitelist and no icon, so no player can use them. The owner's
   sneeze is the new `WFWolfmedSneeze` ("sneezes!", Vocal, in the menu with the cough icon, `*sneeze`). The emote sound
   sets cannot be extended without editing each one (a prototype id lives in one file, and there are 87 sets in 13
   files), so the voices sit in two `_WF` sets, `WFWolfmedMaleEmotes` / `WFWolfmedFemaleEmotes`
   (`Voice/bobmed_emote_sounds.yml`), and `WolfmedBodySoundSystem` plays from them: it subscribes
   `<WoundHostComponent, EmoteEvent>` after `MutingSystem` and before `VocalSystem` / `MumbleAccentSystem`. For an
   emote in those sets, or `Gasp`, a mechanical body is swallowed (Handled, no sound); a flesh body whose own species
   set maps the emote keeps its own sound; otherwise the Wolfmed set for its sex plays. Sex is read the way
   `VocalSystem.LoadSounds` reads it (`HumanoidAppearanceComponent.Sex`); Female picks the female set and everything
   else the male one, as an unsexed human uses `MaleHuman`. The ported emotes blacklist `BorgChassis` and `Silicon`,
   so an IPC cannot type them at all. A muzzled body sneezes unmuffled: `MumbleAccentSystem` only muffles what the
   species set voices.
3. **Pill swallow.** `Pill` (`chemistry.yml`, marked) swallows with `WFWolfmedPillSwallow`. Its 19 direct children (15 in
   `healing.yml`, `randompill.yml`, the `_Mono`, `_NF` and Wolfmed painkiller pills) inherit it and none sets its own;
   `PillCanister` is not a `Pill`. `eatMessage: food-swallow` is unchanged.
4. **Drips.** The analyzer has no rate words for bleeding: its chip is on or off, and its vitals line words the net
   blood flow, "falling" against "falling fast" at `wolfmed.analyzer_blood_fast` 1 u/s (a summed wound rate of about 3
   per 3 s bloodstream tick, past an arterial cut). The one place Wolfmed words a bleed by rate is the inspection
   (`look.yml`): "oozing blood" under 0.5, "bleeding freely" from 0.5, "spurting" from 2.5. So `wolfmed.drip_sound_below`
   is 0.5, read against every open wound's `CurrentRate` summed over the body. `WolfmedBleedDripSystem` keeps a
   `WolfmedBleedDripComponent` clock on bodies that qualify, built like the spurt system (re-evaluated off
   `PartBleedingChangedEvent`, whose `WoundableComponent` pair the spurt system holds, so this one takes the
   `BodyPartComponent` pair, and again at every drip). A drip is one of `blood1-6` every `wolfmed.drip_sound_interval`
   (4 s) at `wolfmed.drip_sound_volume` (-6 dB), PVS. Never while dead, never from a chassis, and never while G2 has a
   spurt source (an arterial bleed, an open stump, any wound at 0.45 or more), so the drip is the tier under the spurt.
   Measured: a fresh severity-12 slash runs 12 x 0.1 x `wolfmed.bleed_rate` 0.3 = 0.36 and drips; a severity-15 slash
   is about 0.45 and spurts. A body summing 0.5 or more from wounds that each stay under 0.45 does neither; lowering the
   CVar narrows the drip band, raising it closes that gap. The owner's `blood1-6` replaced NovaSector's `blood1-3`
   under the same names, and those three were NovaSector's slash sounds, so they left `WFWolfmedWoundFlesh`.
5. **Bone crack.** `WolfmedBodySoundSystem` subscribes `<WoundableComponent, FractureGradeChangedEvent>` (Onyx's
   `FractureEffectsSystem` holds the `WoundFractureComponent` pair) and plays `WFWolfmedBoneCrack` when the grade rises;
   a new fracture comes in from `None`, so creation and a worsening break are one test. Hit or no hit (a fall, a blast).
   The SFX profile used to voice a hit fracture with `WFWolfmedWoundBone` through its hit gate, which would have
   doubled the crack, so its `BoneFractureWound` entry is now claimed with no sound (`WolfmedWoundSoundEntry.Sound` is
   optional); dislocations keep the old bone snap. One crack per body per tick, so a blast breaking four bones is one
   crack. Plays at the body: a part lives in the body's container.
6. **Stab.** The hand-off is a marked edit at both `PlayHitSound` calls in `SharedMeleeWeaponSystem`: when the hit
   event carries no `HitSoundOverride`, `WolfmedOrganicSoundSystem.GetHitSound` supplies one, which
   `MeleeSoundSystem.PlayHitSound` plays predicted exactly as it plays an override today (a target's own
   `MeleeSound` still comes first). A held weapon (`meleeUid != user`) whose hit's largest damage type is Piercing plays
   `WFWolfmedStab`, whatever its own sound. Natural attacks keep theirs, or every cat, bat and spider bite (62 prototypes)
   would have become a stab. Wolfgate's knives are Slash (kitchen 16, combat 22, machete 32), so they keep
   `bladeslice`; the stabs are the 45 item weapons whose hit is mostly Piercing: the spear, scissors, crossbow bolts, darts, the fork,
   needle, heels and haycutters, the drills, the wirecutter and the energy cautery. (Upstream's
   `GetHighestDamageSound` never updates its running maximum and returns the last positive type; the helper takes the
   true largest.)
7. **Melee.** A weapon with no `soundHit` plays its `soundNoDamage` on every hit, the `WeakHit` collection unless the
   weapon names another (`MeleeSoundSystem`'s damage-type switch, welder / `WeakHit` / `MetalThud`, is unreachable
   because that branch always plays first). On flesh, a mostly Blunt or Slash hit from such a weapon now plays
   `WFWolfmedMelee`: 615 Blunt prototypes (gas tanks, instruments, improvised objects) and 25 Slash ones (the
   retractor, arm blade, hatchet, bladed caps). Everything that names a hit sound keeps it: `Punch` (221 prototypes: the species'
   fists, books), `MetalThud` (337: bats, clubs, chairs), `smash.ogg` (155: toolboxes, extinguishers),
   `bladeslice` (36 Slash blades), `genhit2`, bites and claws, and the toys.
8. **Tendon snap.** Onyx has no tendon wound; the tendon cut is Wolfmed's `WFWolfmedTendonCutWound` (W2). Its opening
   is the `Created` kind of the broadcast `WolfmedWoundLifecycleEvent`, which plays `WFWolfmedTendonSnap` at the body
   when the part is flesh. The chassis's cut actuator is a different wound and stays silent.
9. **Sounds for SEPSIS's emotes.** Shipped: `WFWolfmedCoughMale` (Bob's 16) / `WFWolfmedCoughFemale` (Bob's 12),
   `WFWolfmedChokeMale` / `WFWolfmedChokeFemale` (Bob's one each; split by sex rather than one `WFWolfmedChoke`, because
   they are a man's and a woman's voice), and `WFWolfmedRetch` (tgstation's `gag1-5`, via NovaSector). No wheeze and no
   shiver sound exists in tgstation, NovaSector or Bobstation (tg's wheeze and shiver emotes are silent), so
   `WFWolfmedWheeze` is not shipped. The mapping belongs in the two `_WF` sets, which also makes the emotes flesh
   only: male `WFWolfmedCough: WFWolfmedCoughMale`, `WFWolfmedChoke: WFWolfmedChokeMale`,
   `WFWolfmedRetch: WFWolfmedRetch`; female the same with the `Female` collections; `WFWolfmedWheeze` and
   `WFWolfmedShiver` unmapped. The emotes need `category: Vocal` (or the default General) for the handler to voice them.
10. **The emote audit.** Every emote prototype in Wolfgate, and whether a human hears it (`MaleHuman` / `FemaleHuman`,
    the Wolfmed sets, and `GeneralBodyEmotes` for hand emotes). "Typeable" means it has trigger words, which is also
    what the emote menu requires.

| Emote | Name | Layer | Typeable | Human sound |
|---|---|---|---|---|
| `Awoo` | Awoo | _DV | yes | no |
| `Bagawk` | Bagawk | _Goobstation | yes | no |
| `Bark` | Bark | _DV | yes | no |
| `Beep` | Beep | Voice | yes | no |
| `Belch` | Belch | _NF | yes | yes: Belch |
| `Boop` | chat-emote-name-boop | _EinsteinEngines | yes | no |
| `Bubble` | Bubble | _Impstation | yes | no |
| `Buzz` | Buzz | Voice | yes | no |
| `Buzz-Two` | Buzz Two | Voice | yes | no |
| `Call` | Call | _Moffstation | yes | no |
| `CatHisses` | Cat Hisses | Voice | no | yes: CatHisses |
| `CatMeow` | Cat Meow | Voice | no | yes: CatMeows |
| `Chime` | Chime | Voice | yes | no |
| `Chirp` | Chirp | Voice | yes | no |
| `Chitter` | Chitter | Voice | yes | no |
| `Clap` | Clap | Voice | yes | yes: Claps |
| `ClapSingle` | Single Clap | Voice | yes | yes: ClapSingle |
| `Click` | Click | Voice | yes | no |
| `Cough` | Cough | Voice | yes | yes: MaleCoughs / FemaleCoughs |
| `Crack` | Crack Knuckles | _Impstation | yes | yes: Cracks |
| `Crying` | Crying | Voice | yes | yes: MaleCry / FemaleCry |
| `DefaultDeathgasp` | Deathgasp | Voice | yes | yes: MaleDeathGasp / FemaleDeathGasp |
| `Flip` | Do a flip | _Goobstation | yes | no |
| `Gasp` | Gasp | Voice | yes | yes: MaleGasp / FemaleGasp |
| `Gnash` | Gnash | _DV | yes | no |
| `GoblinMutter` | Mutter | _NF | yes | no |
| `GoblinThroatSinging` | Sing | _NF | yes | no |
| `Growl` | Growl | Nyanotrasen | yes | no |
| `HarpyBang` | Bang | _DV | yes | no |
| `HarpyBeep` | Beep | _DV | yes | no |
| `HarpyCaw` | Caw | _DV | yes | no |
| `HarpyHonk` | Honk | _DV | yes | no |
| `HarpyPew` | Pew | _DV | yes | no |
| `HarpyRev` | Rev | _DV | yes | no |
| `HarpyRing` | Ring | _DV | yes | no |
| `Hew` | Hew | Voice | no | yes: Hew |
| `Hiss` | Hiss | Nyanotrasen | yes | no |
| `Honk` | Honk | Voice | yes | yes: BikeHorn / CluwneHorn |
| `Howl` | Howl | _DV | yes | no |
| `Jump` | Jump | _Goobstation | yes | no |
| `Laugh` | Laugh | Voice | yes | yes: MaleLaugh / FemaleLaugh |
| `Marr` | Marr | _HL | yes | no |
| `Meow` | Meow | Nyanotrasen | yes | no |
| `Mew` | Mew | Nyanotrasen | yes | no |
| `MonkeyDeathgasp` | Deathgasp | Voice | no | no |
| `MonkeyScreeches` | Monkey Screeches | Voice | no | yes: MonkeyScreeches |
| `Ping` | Ping | Voice | yes | no |
| `Pop` | Pop | _Impstation | yes | no |
| `Purr` | Purr | Nyanotrasen | yes | no |
| `ReptilianHiss` | Hiss | _Impstation | yes | no |
| `ReptilianSnicker` | Snicker | _Mono | yes | no |
| `RMCSkrellAnger` | Trill angrily | _RMC14 | yes | no |
| `RMCSkrellPeep` | Peep | _RMC14 | yes | no |
| `RobotBeep` | Robot | Voice | no | yes: RobotBeeps |
| `Salute` | Salute | Voice | yes | yes: Salutes |
| `Scream` | Scream | Voice | yes | yes: MaleScreams / FemaleScreams |
| `Scree` | Scree | _Moffstation | yes | no |
| `Sigh` | Sigh | Voice | yes | yes: MaleSigh / FemaleSigh |
| `SiliconDeathgasp` | Deathgasp | _EinsteinEngines | yes | no |
| `Snap` | Snap | Voice | yes | yes: Snaps |
| `Snarl` | Snarl | _DV | yes | no |
| `Sneeze` | Sneeze | Voice | no | yes: MaleSneezes / FemaleSneezes |
| `Snore` | Snore | Voice | no | yes: Snores |
| `Spin` | Spin | _Goobstation | yes | no |
| `Squawk` | Squawk | _Moffstation | yes | no |
| `Squeak` | Squeak | Voice | yes | no |
| `Squish` | Squish | Voice | yes | no |
| `Thump` | Thump Tail | Voice | yes | no |
| `Trill` | Trill | _Goobstation | yes | no |
| `Warble` | Warble | _Goobstation | yes | no |
| `Weh` | Weh | Voice | no | yes: Weh |
| `WFWolfmedGulp` | Gulp | _WF | yes | yes: WFWolfmedGulp |
| `WFWolfmedSneeze` | Sneeze | _WF | yes | yes: WFWolfmedSneezeMale / WFWolfmedSneezeFemale |
| `WFWolfmedSniff` | Sniff | _WF | yes | yes: WFWolfmedSniffMale / WFWolfmedSniffFemale |
| `WFWolfmedSnore` | Snore | _WF | yes | yes: WFWolfmedSnore |
| `Whimper` | Whimper | _DV | yes | no |
| `Whine` | Whine | _StarLight | yes | no |
| `Whirr` | chat-emote-name-whirr | _EinsteinEngines | yes | no |
| `Whistle` | Whistle | Voice | yes | yes: Whistles |
| `Wurble` | Wurble | _Goobstation | yes | no |
| `Yawn` | Yawn | Voice | yes | yes: MaleYawn / FemaleYawn |
| `Yip` | Yip | _StarLight | yes | no |

82 emotes, 28 of them with a human sound (24 before this package). The "no" rows are the species and animal noises
(each species' own set voices them), the silicon noises (the silicon sets) and the goob animations.

NovaSector's emotes that carry a sound, against that table. `code/modules/mob/living/emote.dm` (tgstation core):
gasp, laugh, scream, sigh, cough, whistle and deathgasp already have a human sound here; **sneeze** and **snore** had a
sound but could not be typed; **sniff** did not exist. Its choke, wheeze, gag, burp, shiver and gurgle are silent in tg
too. `modular_nova/modules/emotes/code/emotes.dm`: burp (Wolfgate's `Belch`), clap and clap-once (`Clap`,
`ClapSingle`) and snap already exist with sounds; **gulp** did not exist; esigh is a second sigh, and blush, wink and
blink are comic stings, not body noises, so they were left; beep is a synthetic noise; the rest (peep, awoo, nya,
weh, squeak, yip, gecker, fwhine, merp, bark, squish, bubble, pop, meow, hiss, mchitter, bawk, caw, bork, hoot, growl,
woof, baa, wurble, rattle, cackle, warble, trills, rpurr, purr, moo, honk, mggaow, mrrp, prbt, gnash, thump, flutter,
awuff, arf, coyhowl, wolfhowl, dwhine, dgrowl, aggrobark, dcomplain, meowdeep, the three tesh chirps, mar, quill) are
species or animal noises.

**Ported: four emotes**, `WFWolfmedSneeze` (the owner's Bob sneezes), `WFWolfmedSnore` (Bob's snore1-7),
`WFWolfmedSniff` (Bob's one sniff per sex) and `WFWolfmedGulp` (Nova's gulp1-2, CC-BY-4.0 from freesound), plus the
sounds for three of SEPSIS's five (cough, choke, retch). Whistle and clap already had sounds; cough has its upstream
sound and gets Bob's through SEPSIS's `WFWolfmedCough`.

Tests: `WolfmedBobSoundsTest` (5): every `WFWolfmed` collection's files exist and the gasp pair is exactly the owner's
7 and 13, `Pill` and `PillDexalin` swallow with the new file; the sneeze resolves to the male and female Bob sets as
`Gasp` resolves to `MaleGasp` / `FemaleGasp`, plays for both, and is silent from an IPC; the melee pick (10 cases) and
the flesh-only guard against an IPC and a wall; 75 Blunt cracks a human arm once and an IPC arm never, a tendon cut
snaps once on a human and never on an IPC; a 0.36 slash drips within two 1 s intervals, an arterial cut and an IPC
with the same slash never do. `WolfmedWoundSfxTest` now expects the fracture entry to be silent.

## Playtest 4 batch, the merge (2026-09-27)

The four packages (SEPSIS, VISUALS, IV, SOUNDS; specs and reports under `<plan>/p8/`)
landed as one linear commit each on `Wolfmed`, in that order, with the shared files (`WolfmedCVars.cs`, this file,
the manifest, the module README) merged by keeping both sides. The SEPSIS emotes were shipped silent and the SOUNDS
package shipped their voices without the emotes, so the orchestrator wired `WFWolfmedCough` and `WFWolfmedCoughBlood`
to `WFWolfmedCoughMale/Female`, `WFWolfmedChoke` to `WFWolfmedChokeMale/Female` and `WFWolfmedRetch` to
`WFWolfmedRetch` in `bobmed_emote_sounds.yml`; `WFWolfmedWheeze` and `WFWolfmedShiver` stay silent (no source has a
wheeze or a shiver). Along the way: the sidearm attack gate (`WolfmedDownedSystem.OnAttackAttempt`) asked
`GetActiveItem` of bodies without hands, which logged and failed `DownedReachesOnlyItselfTest`; it now checks for
hands first. The owner's mid-batch corrections (one blood type, the existing `Bloodpack`, no bag variants; body
sounds and overlays organic-only, the pain HUD for machines too) are in the specs and the packages.

## Stumps hurt, the pain HUD flashes, the flatline is quiet (playtest 4, 2026-09-27)

- **"Strange that stump trauma from a freshly cut off stump doesn't cause any pain."** It did not: the severed part
  took its pain with it, and neither stump wound carried a `WoundPainBehavior`. Marked Onyx YAML: `DismembermentWound`
  (the open stump) gets a one-time spike of its severity (an arm 120, a hand 80, on the part that keeps the
  stump), and `AmputationConsequenceWound` a floor of its severity (`amputationConsequenceSeverity` 35) until the
  stump is treated: losing an arm floors you until the spike fades. `WolfmedStumpPainTest`: an arm off leaves
  the torso over 100 at once and settles on 35.
- **"Make the pain HUD border flash red when in Downed pain, and even more rapid when in pain crit."** Two new
  states from the generator: `paindowned` (the top glyph, a two-pixel red border on every other frame, 0.4 s) at
  severity 8 while pain holds the body Downed, and `paindd` now flashes the border at 0.12 s over its own blink for
  the faint (severity 9). `WolfmedPainAlertSystem.DownedSeverity` / `FaintSeverity`, both alerts carry the extra
  icon, the hover text names both bands.
- **"The flatline tone very quiet too."** `WolfmedCritHeartbeatSystem` plays it at -18 dB (was -6).
- **Second round of Bob sounds (same morning).** Fists: the `Punch` collection now lists the owner's `punch1-3`
  (marked; it replaces the Skyrat punches WOLFGATE(Weapons) had put there). The chop is not a weapon's sound but
  a hit's: "for super heavy hits that things like the axe would inflict, a meaty chop" (encoded at 0.6 gain, "same
  for the heavy hit SFX"), so
  `WolfmedOrganicSoundSystem.PlayHitOverlays` plays `WFWolfmedChop` (`chop2`, `chop4`, `chop5`; the owner dropped 3
  and 6) as a second sound over the weapon's own whenever a Blunt plus Slash hit on flesh reaches
  `wolfmed.chop_sound_damage` (40: a wielded fire axe at 45 chops, a machete at 32 does not, a stab never). Every
  weapon keeps its own `soundHit` ("the thud should still play"); the owner first asked for one merged file per
  hit, then for the chop to be its own sound keyed on weight, so the mixes were dropped. Crowbars (`BaseCrowbar`,
  six prototypes) carry `WolfmedHitOverlaySoundComponent` with `WFWolfmedCrowbarHit` (`crowbarhit1-2`, encoded at
  0.6 gain: "notched down 30 %"), played over
  their thud on flesh the same way. Both overlays are predicted like the hit sound and never play on a chassis or a
  structure. Arterial sprays: `artery3` joins the splatter (the owner dropped `artery1`).
  `WolfmedBobMeleeSoundsTest` pins the crowbar overlay, the axe's and spear's untouched sounds and the chop line.
- **Bullet impacts on flesh.** The Skyrat flesh impacts WOLFGATE(Weapons) ported (`MeatBulletImpact`, nine files)
  are the owner's `ric_flesh1-4` now, under `Resources/Audio/_WF/Wolfmed/Impacts` (marked in `gun_impacts.yml`),
  encoded at 0.6 gain, 3 dB under the mono originals: "quieten the bullet hit sounds by maybe 30 %" (0.7 only bought
  2 dB because the source peaks are clipped).

## Gibs on the deck (playtest 4, 2026-09-27)

"Port these decals from Escape From Nevado's `icons/effects/blood.dmi`: gibmid1, gib1-6, as effects for extreme
traumatic events (dismemberment, disembowelling, etc.); the red should be the blood colour and the flesh-coloured
bits in gib2 and gib6 the mob's skin colour." `Tools/_WF/Wolfmed/gen_wolfmed_gibs.py` splits each state into
layers by hue: the reds become a greyscale blood mask (`WFWolfmedGib_<state>`, tinted with the blood reagent's
colour like the floor splats), the peach and orange a greyscale flesh mask (`_flesh`, tinted with
`HumanoidAppearance.SkinColor`, a default skin for bodies without one), and the magenta-pink innards in gib1, gib2
and gib4 keep their own colours (`_meat`). Four-direction states become four states. Everything is a cleanable
decal (`Resources/Prototypes/_WF/Wolfmed/Decals/gibs.yml`, generated) stacked in that order.
`WolfmedGibDecalSystem` (server, GORE) throws them within `wolfmed.gib_spread` (1.5) tiles, on tiles that exist,
organic bodies only: a limb off (`WolfmedPartAmputatedEvent`) leaves `wolfmed.gibs_dismemberment` (2) from the
splats and streaks; an opened belly (the torso overflow after `WolfmedEviscerationSystem` has marked it)
`wolfmed.gibs_evisceration` (3) from the ones with guts; a gibbed body (`BeingGibbedEvent` on the wound host)
`wolfmed.gibs_gib` (7) from all of them. `wolfmed.gib_decals` turns the lot off. `WolfmedGibDecalTest`: an arm off
leaves two, colours as above and cleanable; the gib adds seven; an IPC leaves none.

## The tourniquet holds (playtest 4, 2026-09-27)

"I've been trying to apply a tourniquet to my right leg, and it says it applies and stops, but it continues
bleeding." `TourniquetSystem.Apply` clamped the bleeds it found and then, as Onyx has it, hurt the part
(Blunt 5, "tourniquets hurt"); `WoundBleedingSystem.OnWoundChanged` clears any treatment on a wound whose severity
rises, so the strap's own blow reopened what it had just clamped, and so did every later hit and the comminuted
fracture's crawling. Three marked edits: a `Clamped` treatment survives new damage while the part is tied off
(`WoundBleedingSystem.IsTiedOff`, the part or one above it carrying `WolfmedTourniquetComponent`); a bleed that
opens under a tourniquet starts clamped; and the strap ties off the parts below it (a leg's foot, an arm's hand).
Every other dressing still comes off a wound that reopens. Removal is unchanged: taking the strap off unclamps
(`WolfmedNecrosisSystem`). `WolfmedTourniquetHoldsTest`.

## The flatline is heard (playtest 4, 2026-09-27)

"Still don't hear the beep tone when cardiac arrest happens." The trigger was never the problem: the client's
`WolfmedCritHeartbeatSystem` re-checks every frame and sees the networked arrest marker. The file was: the flatline
ogg peaked at -23.5 dB, and the earlier "very quiet too" note was a complaint, not a request, so dropping the play
volume to -18 dB buried it entirely (-41 dB peak against the heartbeat's -4). The file is normalised to -1.5 dB peak
and plays at -6 dB again, a shade under the heartbeat. `WolfmedCritHeartbeatTest.FlatlineMarksTheArrestTest` pins
that the client reads the arrest (`Flatlined`) the moment the server starts one, and drops it when the arrest ends.

## The arteries on the sprite (playtest 4, 2026-09-27)

"Did you port all of the arteries for each stump? They should always stop when that stump stops profusely/arterial
bleeding. Also, it should animate intermittently, specifically when the blood sprays happen." Bob's artery.dmi has a
still (0) and a three-frame spray (1) for the head, the neck and each limb; all ten pairs are ported as a blood mask
tinted with the blood colour, and ride `PartDamageVisualsComponent.Arteries` beside the wounds and rot.
- **"The arterial bleed overlay is black."** The first cut greyed the art by luminance normalised over the whole set,
  and the legs' one orange highlight (luminance 127) dragged every pure red (65) to a third of white, so the tint landed
  near black. `blood_mask` in the generator takes the strongest channel instead, so the reddest pixel is white.

- **A site per part.** A cut artery (`WolfmedArterialBleedBehavior`) on an attached part is that part's site; a
  dismemberment wound is the site of the part it is the stump of (`WolfmedStumpComponent`, tagged off the amputation
  event by `WolfmedStumpTagSystem`, since a torso can hold a neck's and a shoulder's stump at once); a head off is the
  neck. A part that is back on takes its stump site with it, and a hand's stump leaves with the arm.
- **The look is the blood spurts' rule.** Bleeding while `WolfmedBleedSpurtSystem` would throw from it: a stump
  bleeding untreated, a cut artery bleeding at all. Anything else open is the still artery, so a dressed, tied-off or
  clotted stump stops spraying the moment the spurts do.
- **The spray plays per spurt, not on a loop.** The layer rests on the still frame. Each spurt with a spurting artery
  stamps the body's `ArterySprayAt`, and the client plays the spray once (a sprite flick to `_artery1` and back to
  `_artery0`) on every site that is Bleeding when the stamp moves and is under a second old. A stamp older than that
  (a body first seen mid-bleed, one back from out of view, the review's catch: the client keeps a detached entity's
  component, so a counter played on sight) plays nothing. The looks are recomputed at the spurt, so the stamp goes out
  with the looks it was decided on.
- **Review round, fixed:** `spurting &= bleeding!.Treatment == None` does not short-circuit, and a stump whose bleed
  has run out (burns, the hemostat step) has no bleeding component: a null reference every sweep, which stalled every
  overlay refresh after it. Bob's sheets also copy two frames onto the wrong side (l_foot north is r_foot's, r_arm
  west is its own east, on the chest); the generator now checks each limb frame against the human limb masks and
  mirrors or blanks the strays, for the arteries and the stumps alike. Left as is: a held item picked up after the
  layers were made draws above them.
- **On top of everything.** The spray leaves the body, so the layers are appended above hair, helmets and collars
  rather than tucked under the clothing like the wound glyphs; each takes its own limb's species shift, the neck the
  head's.

`WolfmedWoundOverlayTest.ArteryOverlayTest` walks the head, an arm, a hand then its arm, and the neck, on the server
and the client, and reads the spray flick off the client's animation player.

## The gibs, checked from the client (playtest 4, 2026-09-27)

"Haven't been seeing the gibs." `WolfmedGibDecalTest` only counted the decals on the server. It now stands the test
client's player in the body, and asserts the client received the chunk with the gib decals and resolves every gib
decal's art at 32x32.

## Stumps (playtest 4, 2026-09-27)

"I've actually found the proper stump overlays (combine with arterial/drip bleeding): Escape From Nevado's
modular_septic stump.dmi. Recoloured again to the relevant species; there is a small white bone visible in these so
make sure only the flesh is rendered to the species blood type. I suppose that also means that the chest should have
5 possible stumps?" Yes: the neck, two shoulders and two hips all sit on the torso, and the wrists and ankles on the
arms and legs; each is its own site, the same sites the arteries use.

- **Art.** `stumps.rsi`, generated: `<site>_stump` is the flesh as a blood mask (tinted the blood colour on the
  client), `<site>_stump_bone` the bone and the outline in their own colours, `<site>_stump_drip` and `_stump_stream`
  the bulletwound drip and trickle glyphs hung from the bottom of each stump. stump_head, stump_groin and the headshot
  states are not used: no head site, no groin part.
- **When.** `PartDamageVisualsComponent.Stumps`, from the tagged stump wounds beside the arteries: the art whenever the
  stump is open, the drip or trickle by the part's stream rate while it bleeds, nothing hung once dressed or clotted. A
  stump wound no longer feeds the torso's wound glyph at the chest's centre, which is where the first cut drew it.
- **Stacking per site, bottom to top:** flesh, bone, drip, artery, all above the sprite's own layers (a sleeve must not
  cover a missing arm), and the four keep that order whichever is created first.

`WolfmedWoundOverlayTest.StumpOverlayTest` walks an arm off, its dressing and a head off, on the server and the client.
## Playtest 4, INFECTION SPREAD (2026-09-27)

"Infection should spread smartly, if you get an infection in the hand, once it's in the spreading mode, it should move
to the arm etc, once in the chest/head, that's where sepsis/septic shock can occur ... And there doesn't need to be a
wound to travel. And is septic shock a thing? If not, it should be what happens when you get sepsis." Branch
`wolfmed-infection` from `1be3727edf`; spec `<plan>/p9/INFECTION-spec.md`.

**Before.** A contaminated wound ran 0 to 100; at Septic (100) it created sepsis on the body, and every spreading wound
and every necrotic part anywhere fed it 12 a minute. Where the wound was did not matter: a fingertip cut went septic as
fast as a chest wound, and nothing travelled.

**Infection lives on the parts.** New networked `WolfmedPartInfectionComponent` (`Progress` 0 to 100, `Stage`) on
organic woundable parts only (`WolfmedWoundTraitSystem.IsOrganic`), added on a part's first progress and removed at 0.
Stages off the profile's new `partLocalAt` (25) and `partSpreadingAt` (60); 100 is Septic. Each 5 s tick
(`WolfmedInfectionSystem.TickParts`, after the wounds, minutes scaled by `wolfmed.infection_rate`), per part:
- each wound on it at Spreading feeds it `partFromWoundPerMinute` (10), a Septic wound twice that. The wound's own
  stages are unchanged; a Septic wound no longer creates sepsis.
- a necrotic part (`WolfmedNecrosisComponent.Necrotic`) is pinned at 100.
- each child part (the part hanging off it: a hand for its arm) at Spreading or Septic feeds it `partSpreadPerMinute`
  (8). This is the travel: hand to arm to torso, foot to leg to torso, head to torso, with no wound on the receiving
  part. Towards the torso only; the torso seeds nothing, sepsis is "the rest of the body". The stages are read as the
  pass starts, so an infection moves at most one part a tick whatever order the parts come in.
- nothing feeding it: it recovers `partRecoveryPerMinute` (4).
- antibiotics (`Treat`) take `partAntibioticPerUnit` (8) a unit off every part; antiseptic (`Clean`) does nothing to
  a part.

The part's own effects: from Local it takes `localPainPerMinute` (4) as a local wound does; from Spreading the body
runs the fever. A limb that comes off takes its infection with it and infects nothing (it has no parent); a limb put
back keeps what it carries. A chassis part cannot carry one, so a cybernetic arm between an organic hand and the
torso stops the travel.

**Sepsis starts in the core.** `WolfmedSepsisComponent` is created (living bodies, `wolfmed.sepsis_enabled`) and fed
only by the torso and head at Spreading or Septic: `sepsisPerMinute` (12) per core source, 0 to 2. Spreading wounds and
dead limbs no longer feed it directly; they feed their part and it has to travel. Recovery (8 a minute), antibiotics,
the alert, the analyzer line, the brain drain, the organ damage and the condition emotes all still read the same
`Progress`. `HasFever` counts part stages beside wound stages, so an infected limb with no wound shivers.

**Septic shock** is sepsis's named late stage: `wolfmed.septic_shock_at` (80, SERVERONLY), "sepsis progress from which
the patient is in septic shock: the brain drain (`wolfmed.arrest_sepsis`) and the organ damage
(`wolfmed.sepsis_organ_damage_from`) are its effects". `WolfmedInfectionSystem.InSepticShock(body)` reads it live.
Nothing new kills: the drain at 80 and the organ damage at 90 are what shock does. Surfaced:
- the `WFWolfmedSepsis` alert: severities 0 and 1 (min 0, max 1), 1 is shock. Its icon `septicshock` is the sepsis icon
  with the red border `gen_wolfmed_overlays.py` draws for `paindowned`, flashing at the same 0.4 s (`build_sepsis`,
  `--sepsis-only`; the hand-made `sepsis.png` is the source and is not rewritten). Name and description select on
  `$severity` ("Septic shock"); upstream's `AlertControl` passed the severity to the description only, so the name
  gets it too (one marked block).
- the analyzer: `WolfmedVitalsReport.SepticShock`; the sepsis banner reads "SEPTIC SHOCK - systemic infection at N%"
  (and ", damaging the organs"), titled "Septic shock"; "Do first" says "antibiotics now, septic shock" and
  "antibiotics now, septic shock is damaging the organs" (`WolfmedVitalsText.Aid(route, mechanical, shock)`).
- examine: "grey and clammy, skin blotched" in place of "flushed and sweating". The examine runs in shared code and
  the line is a SERVERONLY CVar, so the server stamps `WolfmedSepsisComponent.Shock` (networked) on its tick and on
  an antibiotic dose.

**The analyzer's part line.** `HealthAnalyzerWoundDiagnostic.PartInfection` (a marked optional parameter; it counts as
a finding, so an infected arm with no wound gets a card): the part's own stage from Local. The card shows an infection
chip "tissue: spreading" (headline "tissue infection: spreading") beside the wound's "infection: spreading"; its
advice and procedure are the wound infection's spreading one (septic for Septic), because antiseptic never reaches a
part, and the procedure's "infection cleared" step waits for the part too. The card's accent counts it. The existing
`Infection` field is the worst wound stage, now through `GetWorstWoundStage`; `GetPartStage` is the part's own.

**Numbers** (5 s ticks, the shipped profile; `WolfmedInfectionSpreadTest` measures the first three). An untreated
risk-1 cut on a hand: the wound spreads at 10 minutes, the hand at 16.0, the arm at 23.5, the torso at 31.0 and
sepsis starts there (septic shock about 6.7 minutes later). A cut on the chest or the head: sepsis at 16.0 (it used to
be 16.7, when the wound went septic). A dead arm (tourniquet, late reattachment): sepsis 7.6 minutes after it dies; a
dead hand: 15.2 (it used to be about a minute, off the contaminated necrosis wound). An arm at 35 when its hand came
off is clear 8.8 minutes later. 13 units of spaceacillin clear every part (100 / 8).

**Differs from the spec, and why.**
1. **One fever rise per body per tick.** `Fever` used to run once per spreading wound and once more for sepsis each
   tick; with parts added it would have run three or four times for one infection. Each body with any source now
   rises once (3 K a minute to the same ceiling), so several wounds no longer warm it faster.
2. **A part's pain and fever need it attached.** A severed limb keeps ticking (its wounds still feed it) but hurts
   nobody and infects nothing.
3. **Guidebook and advice.** `Wounds.xml`, `WoundTreatment.xml` and the sepsis and infection advice lines now say the
   infection travels to the chest and that sepsis starts there, and name septic shock.
4. **Single long `Update` calls** move an infection one part per call. Tests that handed a dead limb twenty minutes in
   one call now step 5 s at a time (`WolfmedInfectionTest.Run`).

**Tests.** New `WolfmedInfectionSpreadTest`: `HandInfectionTravelsToTheTorsoTest` (the hops and their times, sepsis
on the torso's tick and never before, no wound on the arm or torso, the arm's analyzer card),
`AmputationStopsTheTravelTest`, `HeadInfectionStartsSepsisTest`, `AntibioticsClearThePartsTest` (and antiseptic does
not), `SepticShockTest` (`InSepticShock` at 79.99 and 80, alert severity, the shock flag, the analyzer flag and aid,
the examine line, and the flip when the CVar moves to 95), `MachineNeverCarriesAPartInfectionTest` (an IPC with
wounds forced septic). Updated: `WolfmedInfectionTest.SepsisShowsAndAntibioticsClearItTest` (the torso part, cleared
by the dose), `TourniquetLeftOnKillsTheLimbTest` (no sepsis at 7 minutes, sepsis by 8),
`RejuvenateClearsSepsisAndNecrosisTest` (10 minutes of 5 s ticks; the heal clears the part infections),
`LateReattachmentKillsTheLimbTest` (a dead hand: arm spreading and no sepsis at 14.5 minutes, sepsis by 16),
`AnalyzerAndNamesExistTest`; `WolfmedSepsisTest.SepsisOrganDamageTest` and `WolfmedMedicInfoTest.AnalyzerVitalsTest`
(the shock aids); `WolfmedConditionEmoteTest.InfectedLimbShiversTest` (new: a spreading arm, no wound, shivers);
`WolfmedLocaleCoverageTest` and `WolfmedVisualInspectionTest` (the new keys). `WolfmedRejuvenateSystem` clears
`WolfmedPartInfectionComponent`.

## Playtest 5: the Downed icon, a lighter hand on blood and pain (2026-09-28)

"Change the crawling/downed HUD icon to two arrows pointing downwards with shading and the same background as the
rest of the HUD icons. Nerf bloodloss just a bit, you should last longer in fights, same with pain amounts; pain should
rapidly fall if the wound isn't getting worse. Adjust it slightly at first."

- **The icon.** `downed.rsi` is generated now (`gen_wolfmed_overlays.py build_downed`, `--hud-only`): two chevrons
  pointing down, lit on the top row and shaded on the bottom with a dark edge under each, on the Crescent health
  alerts' backing the pain icons use.
- **Blood.** `wolfmed.bleed_rate` 0.3 to 0.25: every bleed a sixth slower, an untreated arterial arm cut about six
  minutes to arrest instead of five. The breathing-clock timing test pins the new figures.
- **Pain amounts.** `wolfmed.pain_scale` (0.85) on every gain and every wound floor, through the
  `ModifyPainGainEvent` Onyx already raises; a high pain threshold trait multiplies on top.
- **Pain falls when the wound is not getting worse.** A part's wound floor used to hold all its pain for as long as
  the wound was open. Now `WolfmedPainSettleComponent` records when the floor last rose (a new wound, a wound
  getting worse), and `RecoverPain` recovers toward the floor scaled from whole to `wolfmed.pain_floor_rest` (0.6)
  over `wolfmed.pain_floor_settle_seconds` (20): a hit hurts in full, then settles to three fifths within twenty
  seconds unless it gets hit again, which puts the whole floor back. A floor set by hand (a test) holds whole.
  `WolfmedPainSettleTest` pins both.

## Wounds under the fur (playtest 5, 2026-09-28)

"Some species don't have their stump/bleed overlays: yowies, reptilians, rodentia, resomi." The overlay logic was
species-blind, and a headless sweep over every organic round-start species (`WolfmedSpeciesOverlayTest`) drew the
lot. The difference in play is the markings: `HumanoidAppearanceSystem.ApplyMarking` inserts a marking straight above
its body part, and the wound glyph, the rot and the degradation layers were inserted there too, so on any species
wearing a full-body fur, scale or feather marking the wound went under the pelt. Those layers now go above the limb's
marking layers (`DamageVisualsSystem.MarkingTop`), still under the clothing, and a marking applied after the wound was
drawn puts the wound back over it. The stump, drip and artery layers were already on top of everything. The sweep
loads each species' default profile so the default markings are on, and pins the chest glyph above them.
## Playtest 5, ITEMS (2026-09-28)

"Add all of the new items to medical vending machines, and add them to relevant medkits; the standard medkit should
come with a tourniquet and a splint and some painkillers in a bottle ... perhaps an IPC medical kit sold at the vending
machines with a bunch of synthetic specific stuff. People should be able to get this stuff readily. And make sure
there's an autodoc flatpack in John Wolfgate." Branch `wolfmed-items` from `c520d1fa76`; spec
`<plan>/p9/ITEMS-spec.md`, inventory `ITEMS-scout.md` beside it. "John Wolfgate" is the
trader `WFTraderWolfgate`, whose shop sells `WFWolfgateVendInventory`.

**What the model reads.** `WolfmedPainRelief` (the CONSC tier) is on Wolfmed's three rungs and on all four Onyx
painkillers: ibuprofen (Weak, 14, and a slow brute heal), ketorolac (Weak, 26, thins the blood from 12 u), tramadol
(Strong, 45) and oxycodone (Strong, 80); each also carries Onyx's `SuppressPain`. Osteogen's `MendFractures` knits a
Simple or lesser fracture and is a step of the `BoneFractureWound` and `CondFracture` procedures. None of those five
had a form outside the DEBUG medbox, so each got one by the ladder's own rule (`painkillers.yml`): a pill for a weak
rung, a bottle for a strong one.

**New items** (`_WF/Wolfmed/Entities/`, names inline):
- `painkillers.yml`: `WFWolfmedAnalgesicPillCanister` (7 analgesic pills), `WFWolfmedIbuprofenPill` (10 u) and its
  canister (5), `WFWolfmedKetorolacPill` (10 u, under the 12 u bleed line) and its canister (5),
  `WFWolfmedTramadolChemistryBottle` and `WFWolfmedOxycodoneChemistryBottle` (30 u).
- `medicine.yml`: `WFWolfmedOsteogenPill` (15 u: 1 severity a second at the default 0.5 u/s rate, a whole Simple
  break), `WFWolfmedOsteogenPillCanister` (5), `WFWolfmedOsteogenChemistryBottle` (30 u).
- `synthetic_kit.yml`: `WFMedkitSynthetic` "synthetic repair kit" (BaseStorageItem like `MedkitCombat`, the unused
  `multikit` state and inhands, 6x2 grid, maxItemSize Small, Item Normal `0,0,2,1`, tag Medkit) and
  `WFMedkitSyntheticFilled`: CableApcStack10, WelderMini, Wrench, Multitool, two WFWolfmedHydraulicFluidPack, 11 of 12
  cells. The tools are the ones Wolfmed already gave the synthetic surgery steps (servo kit, hull weld, hull plate,
  core probe); the fluid pack is the only synthetic consumable. The empty kit is in `EmptyMedkitsStatic` with a recipe
  parented to `Medkit`'s (Plastic 300), named by `lathe-recipe-WFMedkitSynthetic-name` in the new `items.ftl`.
- `flatpacks.yml`: `WFMachineAutodocFlatpack` and `WFWolfmedIvDripFlatpack`, `BaseNFFlatpack` with the
  `medical_lathe` box Frontier's stasis bed and cryo pod flatpacks wear. `BaseNFFlatpack` replaces the base's
  overlay layer, so the board-colour convention only applies to flatpacker-made packs; a prototype flatpack picks its
  box. Unpacking needs only fixtures on the target, so the wheeled drip unpacks onto the tile like the janicart does
  (`FlatpacksUnpackTest` does both).

**Standard medkit.** `Medkit`'s grid is 6x2 (`0,0,5,1`, a marked block keeping the old line); every typed kit
inherits it. `MedkitFilled` adds Tourniquet, WFWolfmedSplint and the analgesic canister ahead of the tricordrazine:
Brutepack, Ointment, Gauze, Tourniquet and the splint stand in five columns, the two canisters share the sixth, 12 of
12 cells.

**Other kits, beyond the spec's one kit.** The owner asked for "relevant medkits", and the wider grid makes room:
- `MedkitAdvancedFilled` gets back the tourniquet PROTO E put there and WP12-9 withdrew when the 4x2 grid overflowed
  (the withdrawal comment said to re-add it with a bigger grid). 10 of 12.
- `MedkitBruteFilled` gets the osteogen canister beside its splint: the kit for blunt force, which is what breaks
  bones. 9 of 12.
- `MedkitCombat` has its own 4x2 grid and is full; untouched. The burn kit's and brute kit's cell-count comments are
  updated.

**Vendors** (counts; CM `4294967295` = infinite):

| item | NanoMed Plus | NanoMed wall | CiviMed | Wolfgate shop | Robotech |
|---|---|---|---|---|---|
| Tourniquet | 4 | 2 | inf | 6 | |
| MedicatedSuture, RegenerativeMesh | 2 each | | | 2 each | |
| WFWolfmedAnalgesicPillCanister | 3 | 2 | inf | 4 | |
| WFWolfmedIbuprofenPillCanister, WFWolfmedKetorolacPillCanister | 2 each | 1 each | inf | 3 each | |
| WFWolfmedTramadolChemistryBottle | 2 | | | 3 | |
| WFWolfmedOxycodoneChemistryBottle | 1 | | | 2 | |
| WFWolfmedOsteogenPillCanister | 3 | 2 | inf | 4 | |
| WFWolfmedOsteogenChemistryBottle | 2 | 1 | | 3 | |
| Cautery, Hemostat, Bonesetter, BoneGel | 1 each | | inf | 2 each | |
| WFWolfmedIvDripFlatpack | 2 | | inf | 3 | |
| WFMedkitSyntheticFilled | 2 | | inf | 3 | 2 |
| WFMachineAutodocFlatpack | | | | 2 | |

The strong bottles follow `WFWolfmedOpiateChemistryBottle`: NanoMed Plus and the shop only, never the wall unit or
CiviMed, which stocks no bottles at all. The osteogen bottle sits in the wall unit beside its epinephrine bottles.

**The autodoc flatpack is shop-only.** Flatpacks are lathe-printed here only for board-less structures (janicart, ore
box, solar assembly); every board-built machine's flatpack comes from the flatpacker, which already takes
`WFAutodocMachineCircuitboard` from `MedicalBoardsStatic`. A lathe recipe for the finished pack would skip the board's
two manipulators, two matter bins and capacitor.

**Prices.** A canister's or kit's appraisal leaves out its contents, so the vend price would have been a few credits
for seven pills; the canisters carry `vendPrice` (analgesic 300, ibuprofen 200, ketorolac 250, osteogen 300, against
a loose analgesic pill's 60 at a x3 NanoMed) and the synthetic kit 400, with `price: 0` so the sale value is still the
contents. The autodoc flatpack's StaticPrice is 3200, above the pod's own 2500 plus its board and parts, or buying one
and selling the unpacked pod pays; `FlatpacksUnpackTest` checks that for both flatpacks.

**Fixed in passing.** `WFWolfmedAnalgesicPill` sets `pillType: 6`: the client's `PillSystem` redraws a pill as
`pill{pillType + 1}` on every state, so it showed as `pill1` in hand.

**Left out.** No lathe recipe for either flatpack (above) and no IV drip at a lathe (the spec did not ask; the vendors
cover it). No spare `OrganIPCPump` in the synthetic kit: an organ is a surgery part, not field kit. No Onyx
painkiller in the NanoMed wall unit beyond the two mild canisters. No map change: Caelestinus Central still has no
autodoc; the shop's flatpack is the route.

**Tests.** `WolfmedAvailabilityTest`: `ObtainableItems` has every new id plus the naloxone pen, fluid pack,
tourniquet, makeshift patch, autodoc and drip, and counts an item reachable when a vended or printed item holds it
(a StorageFill entry or a flatpack's target); `Fills` adds MedkitFilled, MedkitAdvancedFilled, the brute kit's
canister and the synthetic kit, and now checks every certain entry of each fill, not only the named one; new
`PillCanistersSpawnFullTest` (each canister's count, each pill's reagent, each bottle's 30 u) and
`FlatpacksUnpackTest` (a multitool unpacks each onto the grid, the pack is used up, the pack costs at least what it
builds).

**Review round (2026-09-28).** The makeshift patch came back out of the four vendors: it is sold empty, nothing can fill
it (no refillable or injectable solution), and once stuck on a patient it cannot be removed (the Unremoveable trap the
patch test records), so it treated nothing. It stays craftable from cloth until the patch itself is fixed. The new
canisters and bottles, and the opiate and spaceacillin bottles, now carry their own names ("osteogen pill canister",
"tramadol bottle"): the trader shop names rows from the prototype, not the label, so they all read "pill canister" and
"bottle" there. John Wolfgate sells the autodoc flatpack at his shop's normal markup (32,000 credits on the 3,200
price); the board route at the lathe stays the cheap one.

**A coma vomits the antitoxin (2026-09-28, found by a flaky test).** `ToxinScenarioTest` failed one run in three: 15 u of
dylovene at Poison 130 left the load at 109.7 instead of waking the patient. The condition emotes retch at the toxin
Downed band and vomit at the coma band on a random roll, and upstream's `VomitSystem.Vomit` splits part of the
bloodstream's chemicals into the puddle, so the injected dylovene went on the floor. The test turns the roll off
(`wolfmed.condition_emote_chance` 0). Whether an unconscious patient should lose an injected antitoxin to a vomit is
the owner's call; left as it plays.

## Playtest 5 bugs: the pod keeps things, the mop cannot clean (2026-09-28)

- **"Stuff gets stuck inside the autodoc."** A limb, an organ, a spent round or a garment the tray could not take,
  taken off the patient inside the pod, drops where the patient is: the pod's own tile, under the pod's sprite and
  out of reach. The pod now sweeps its tile once a second (`SweepTile`) and puts anything loose there beside itself
  with the same placement an ejected patient gets. The delivery tray, which the pod locks while it works, empties
  onto the deck when the patient leaves (`ReturnTray`), so a stowed garment never stays locked in.
- **"Blood splatters cannot be cleaned."** Space cleaner took both halves of a splat, but a mop only carries water,
  and water had no tile reaction at all, so mopping the blood puddle left the wall splat and the floor decal. Water
  now runs `WolfmedCleanSplats`, which takes the wall splats and, new, Wolfmed's own floor decals (ids starting
  `WFWolfmed`) and nothing else, so a spilled cup does not wash a mapper's cleanable paint; space cleaner still takes
  everything. `WolfmedGoreTest.WaterWashesTheBloodOffTheTileTest` and `WolfmedAutodocTest.WhatComesOffInsideLeavesThePodTest`
  pin both.

## Playtest 5 basics (2026-09-28)

- **"Medipens filled 5/15 u, showing a used sprite."** The Wolfmed pens inherit `ChemicalMedipen`'s 15 u chamber and
  hold their dose (3, 5 or 10 u) in it; the fill visual has one level, and 5/15 rounds to empty, so a fresh naloxone or
  opiate pen drew as a spent one and examined as 5/15. Each pen's chamber is now its dose, and its injector pushes
  exactly that.
- **"An IPC used to heal with the nanite applicator."** The applicator's `WeldingHealing` lists the damage containers it
  repairs, and the Wolfmed half of `WeldingHealableSystem` checks the *part's* container: an IPC's parts sit in
  `WFInorganicWolfmed`, which the welder lists and the applicator did not, so it did nothing on any IPC limb. Listed.
- **"Welding a chassis breach doesn't fix it."** The repair went to the part the aiming doll pointed at, and only there:
  with the doll on the chest (the default) and the breach on a leg, the welder did nothing and said nothing. The analyzer
  says "weld the fluid leak" without saying where. The doll still decides when it points at something to repair;
  otherwise the tool goes to the part with the most to repair, and a chassis with nothing to repair now says so.
  `WolfmedWeldingRepairTest.ToolFindsTheBreachWhereverTheDollAimsTest` pins both, with the applicator and with a chassis
  repairing itself.
- **"Infinite blood if you use two blood packs."** The pod and the IV drip spent a pack from the stack only when it ran
  dry, and remembered how much of the top pack was given in their own component, which a stack taken out and put back
  reset: two stacks swapped in and out gave a fresh pack every swap. A pack now leaves the stack the moment it is opened
  (`WolfmedIvDripSystem.TransfuseFromPack`), and what is left of it belongs to the device (`PackOpened` on the drip and on
  the pod's blood component), runs on with the slot empty and shows as "opened pack" on the readout and on examine. A
  stack split or merged elsewhere carries nothing extra, since nothing opened travels with it.
  `PodBloodTest.AnOpenedPackCannotBePutBackFullTest` pins two packs at two packs' worth.
- **"Cannot properly queue more than one surgery manually."** A patient who climbs in by themselves puts the pod in
  self-service, which is one procedure at a time with no queue, and the flag outlived them: the medic who then came to
  the console inherited it, and every ADD replaced the queue. Self-service is now the occupant choosing for themselves;
  anybody else who opens the terminal, presses a button or adds a procedure is an operator with a queue (`NoteTerminalUser`).
  A procedure the subject's condition no longer allows is refused out loud ("THAT DOES NOT APPLY TO THE SUBJECT NOW")
  instead of silently. `WolfmedAutodocTest.OperatorQueuesMoreThanOneAfterTheOccupantClimbedInTest`.
- **"The autodoc won't fix its own mistakes without being removed and reinserted."** Everything a run makes on the
  occupant is marked the pod's own (`WolfmedPodWoundComponent`) so the planner does not read a suture as a new problem,
  and the marks lasted the whole stay: a burn a cautery left, an incision a stalled procedure left open, were invisible
  to every plan until the patient was taken out and put back, which clears the marks. Once a run is over and the grace
  window has passed (`ScheduleLeftoverRelease`, `TickLeftovers`), the marks come off and the next plan, the module's or
  PLAN's, sees the leftovers as the patient's. Twice per occupant (`LeftoverPassLimit`), since a run always leaves
  something behind and one that listed again every time would have the pod operate for ever.
  `WolfmedPodLeftoversTest` pins the release and the bound.
- **"The 'delivery tray' meaning is unclear."** It is the slot on the pod's side that takes the limb, organ or tool a
  step asks for, and that the pod puts what it takes off the patient into. Renamed the parts tray on the terminal, the
  slot, the examine line, the guidebook and the three voice lines that ask for something, with a tooltip on the terminal
  row that says what goes in and what comes out.
- **"'That is not medicine, I will not use it' never uses any of the meds; won't fix infections even with antibiotics."**
  Two things. The pod only ever pushed an antibiotic as the dose after a surgery that opens the body, so an infected
  patient with Spaceacillin in the reservoir and nothing to cut got none: there was no infection step at all. There is
  now an antibiotic course beside the queue, the way the blood reservoir runs (`AutodocSystem.Antibiotics.cs`,
  `WolfmedAutodocAntibioticComponent`): the triage's `antibiotics` step and the autofix module start it on an occupant
  infected past contamination or septic (`WolfmedInfectionSystem.HasInfection`), it pushes `wolfmed.pod_antibiotic_dose`
  (5 u) every `wolfmed.pod_antibiotic_interval` (30 s) until nothing is infected or `wolfmed.pod_antibiotic_course`
  (40 u) is spent, and with nothing usable loaded it says NO ANTIBIOTIC LOADED once and shows it on the readout, the
  fault clearing when a beaker goes in. A unit of Spaceacillin is 5 metabolism ticks and every tick treats a unit's
  worth, so one 5 u dose is 300 wound progress: a spreading wound clears in seconds. And the reservoir row's tooltip
  for a beaker of anything else now names what the pod does use (`ReservoirAccepted`), so "NOT MEDICINE" reads as "not
  medicine I use: opiate, analgesic, Spaceacillin..." rather than a refusal of medicine in general. The pod still pushes
  nothing it has no role for; bicaridine in a beaker stays there. `PodAntibioticsTest` pins the course, the fault and
  the cure.
- **"The tissue rupture won't go away after sorting it (from a cut-off arm)."** The stump wound a torn-off limb leaves
  on its parent (`DismembermentWound`, "tissue rupture") never heals by itself, by design (`healingMultiplier: 0`), and
  nothing closed it when the limb went back on, so it outlived the reattachment and the Stop Bleeding surgery for ever.
  Onyx never removed it either. `WolfmedStumpTagSystem.CloseStumps`, off the lifecycle system's part-added hook, takes
  the stump tagged with the attached part's type and side off the parent when that part, or a replacement for it, is
  put back; the amputation consequence wound stays for its surgery. `WolfmedReattachTest.ReattachmentClosesTheStumpTest`.
- **"Infections need to be far slower to spread and cause sepsis."** The profile's shape, not the rate cvar:
  `progressPerMinute` 6 to 4 (a cut spreads at 15 minutes instead of 10), `partFromWoundPerMinute` 10 to 4 (a part
  spreads 12 minutes after its wound does instead of 6), `partSpreadPerMinute` 8 to 3 (a 20-minute hop between parts
  instead of 7.5) and `sepsisPerMinute` 12 to 4 (septic shock 20 minutes after sepsis starts instead of 7). A chest cut
  now starts sepsis at about 27 minutes untreated (was 16) and reaches shock at 47 (was 23); a hand cut starts it at
  67 (was 31). The tests that walk the clock were moved with it.
- **A limb has to be septic before it infects the next one (owner, 2026-09-28).** A part used to feed its parent as
  soon as it was spreading (60), the same moment it first showed a fever on the analyzer, so there was no window where
  a limb was visibly infected but still safe. `partTransferStage: Septic` (`WolfmedInfectionProfilePrototype.PartTransferStage`)
  makes a septic limb the amputation decision: an untreated hand cut now spreads to the arm at 52 minutes and to the
  torso at 86 (was 47 and 67). A torso or head still starts sepsis at spreading, since that is the bloodstream, and a
  dead limb is pinned at 100 and transfers at once. `WolfmedInfectionSpreadTest` walks the new clock.

- **Septic shock puts the patient out; only the organs kill (owner, 2026-09-28).** "Chests that get wounds and then
  infections cause sepsis fast: a quick death, against our wounding-over-death mindset." The chest clock stays (an
  untreated chest gunshot starts sepsis at about 20 minutes; the trunk is the bloodstream), the end changes. Past
  `wolfmed.septic_shock_at` sepsis holds a consciousness pressure (`WolfmedInfectionSystem.ShockPressure`, cause
  `SepticShock`, Unconscious the way arrest is) instead of draining the brain (`wolfmed.brain_sepsis_seconds` 600 to
  0, kept as an opt-in), and `sepsisPerMinute` 4 to 2 (shock 40 minutes after onset with one source). The only death
  left in sepsis is the organ damage past 90: kidneys and liver fail at 11 minutes, the lungs are impaired from 8 and
  their hypoxia drain grows with the damage, so the brain hits the arrest line about 15 minutes after the damage
  starts and brain death follows 4 minutes after that, on the analyzer the whole time (organ damage, "lungs failing");
  antibiotics under the line wake the patient at once. Same gunshot: sepsis at 20 minutes, out at 60, organ damage
  from 65, arrest about 80, brain death 84. `WolfmedSepsisTest.SepsisKillsTest` walks it; `SepsisDeterministicTest`
  pins the old drain back on.
- **Push death back (owner, 2026-09-28).** "Cardiac arrest onsets too fast still. People dying is now way more work
  to fix than before." Arrest itself stays where it was (30% blood, 15% oxygen, bleed rates untouched); the windows
  around it doubled. `wolfmed.brain_arrest_seconds` 120 to 180 and `wolfmed.brain_damage_rate` 0.1 to 0.05: brain
  death about 7.5 minutes after an arrest from a full brain, was 4. `wolfmed.brain_blood_seconds` 300 to 600: a
  patient held at the blood arrest line with the bleeding stopped arrests at about 9 minutes, was 4.5.
  `wolfmed.brain_refill_factor` 0.5 to 0.75 so the refill after a shock stays what it was. And CPR holds:
  `wolfmed.brain_cpr_floor` (0.4, the damage line) is where compressions hold a living arrested brain, refilling to it
  at the corpse rate if it had already dropped under, so a rescuer who keeps going keeps the brain alive for as long
  as they keep going and the analyzer's countdown goes while they do. `WolfmedArrestClockTest` walks the shipped
  clocks; the older fixtures pin the M2 figures and still derive from them.
- **TEMPORARY: `healmeimbroken` (owner, 2026-09-28).** "I don't want to be a dick by leaving a possibly game-breaking
  bug over night." Any player can run `healmeimbroken`; a window asks what broke, the answer rejuvenates their body
  (`RejuvenateSystem`, so the Wolfmed rejuvenate hooks run) and files an ahelp in their own channel through the
  ordinary path, Discord relay included, carrying the reason, the analyzer's state and vitals lines, its "Do first"
  advice and every open wound by part, plus an admin log at High impact. Five-minute cooldown, `wolfmed.bug_rescue_*`
  to switch it off or retune. Every text says it is temporary and that misuse is a ban. **Remove after the playtest:**
  `Content.Server/_WF/Wolfmed/Commands/` (`HealMeImBrokenCommand`, `WolfmedBugRescueSystem`, `BwoinkSystem.Wolfmed.cs`),
  `healmeimbroken.ftl`, the two cvars, `WolfmedBugRescueTest` and this bullet.
- **A synth's parts sit in the IPC part container (2026-09-28).** "Why can't I weld myself as a synth? It says nothing
  needs fixing." `PartSynth` chains `WFWolfmedPartIpc` for the chassis wound profile but took its `Damageable` from
  `BasePart`, whose Inorganic container is on no repair tool's list, so `CanRepairPart` refused every part and the
  welder said "nothing needs the welder" over three chassis breaches. `PartSynth` now carries `WFInorganicWolfmed`
  like `PartIPCBase`; the welding fixture runs a synth through the welder and the applicator.
- **A shut-down chassis may let go (owner, 2026-09-28).** "I don't have a ghost button when COOLANT PUMP OFFLINE:
  SHUTDOWN." The honest ending gave Succumb only to the Dying, and a power or pump shutdown runs nothing out, so it
  had no way out at all; but nobody may come. `WolfmedShutdownSystem.Refresh` grants Succumb and Last Words when a
  shutdown starts and revokes them when power or a pump comes back, `IsDying` counts the shutdown, the dialog names
  the reason (no power, coolant pump offline) and says core failure, and a yes is the same core-at-zero, revivable
  death as thermal shutdown. `WolfmedHonestEndingTest` flips its shut-down assertion; `PowerShutdownSuccumbTest`
  walks the grant, the dialog, the death and the revoke.
- **The pod cuts clothing on every run (owner, 2026-09-28).** "Autodoc should cut clothing automatically." AUTODOC5
  had it cut only under AUTO and only for a patient who could not undress; an awake patient, or any manual run, sat on
  WAITING: CLOTHING until somebody pressed CUT. `TickClothing` now cuts once `clothingCutDelay` (5 s) is out whoever
  the patient is and however the run started. The awake patient is still told first ("REMOVE YOUR CLOTHING NOW, OR I
  WILL CUT IT."), the helpless one hears the old "I WILL CUT", and the CUT button still cuts at once. A locked
  garment is given up after the same delay on every run. `PodCutsClothingOffAnAwakePatientAfterItsDelayTest`;
  the CUT-button test holds the delay long so the button is what unblocks it.
- **Spurts are for arteries (owner, 2026-09-28).** "Lower the blood spurts to be for arterial bleeds only. Otherwise the
  smaller ones are just the drips." `WolfmedBleedSpurtSystem.HasSpurtSource` no longer counts a wound past
  `bleedSpurt.majorRate` as a source; a cut artery or an untreated stump is one, nothing else, and the drip tier takes
  the rest. `majorRate` stays in the spec for the evisceration profile check. `WolfmedGoreTest` pins a heavy slash
  against a cut artery.
- **No brain-death gate on the paddles (owner, 2026-09-28).** "Brain death (needing brain surgery to be revived)
  should be removed. It should allow you to be revived with major brain damage, never preventing revival; brain damage
  should make you dumb, speak dumb and clumsy." Supersedes the "Brain death" section's repair-first rule for organic
  brains. Death at brain zero stays (the body, the ghost, the screen), but `WolfmedRevivalSystem.GetRefusal` no longer
  refuses a destroyed brain and `GetChance` no longer zeroes on it; `Revive` sets the organ to
  `wolfmed.revive_brain_floor` (0.1 of its maximum) before the mob state moves, so the patient comes back under the
  brain's `SevereAt` line: blurred, stuttering, dropping things, and (new) slurring, for as long as the brain stays
  damaged. `WFSurgeryRepairBrain` is the cure, not the ticket. A destroyed positronic core still needs core repair: a
  chassis has no damaged-but-running state to come back in, and `wolfmed-defib-brain-dead` now says so. Succumb and
  the analyzer's brain-death advice say what actually happens. `WolfmedBrainTest` and `WolfmedPlaytestOneTest` walk it.
- **Brain trauma is shorter, visible and self-clearing (2026-09-28).** "I had died for quite a while and was brought
  back; I stutter and have bad eyesight, all organs are fine." The half-hour trauma a repaired brain carried showed
  nowhere: the analyzer read every organ whole, so it looked permanent. `wolfmed.brain_trauma_minutes` 30 to 10; the
  analyzer's brain line appends "brain trauma from the repair, about N min left" (`BrainTraumaSeconds` on the wound
  diagnostics); the patient is told when it starts and when it lifts; and `WolfmedConcussionSystem` recomputes every
  concussion from its sources on its own tick, so one whose sources went without an edge clears itself within a
  second. `StaleConcussionClearsItselfTest` and the brain test's trauma tail pin it.
- **An infection needs a reason (owner, 2026-09-28).** "Sepsis seems to happen too often, on wounds that don't make
  much sense. An infection has to have an actual reason to start, like being exposed to the air outside a suit."
  `WolfmedInfectionSystem.HasReason`: a wound's infection only grows while something dirty went into it
  (`Contamination`), or the wound is dirty by nature (`WolfmedInfectionRiskBehavior.dirty`: the bite, the open
  abdomen, a lodged round, shrapnel, dead tissue), or its part is exposed (`IsExposed`: no pressure-tight suit in the
  outer slot, or no such helmet for the head). Under a hardsuit a clean cut holds where it is; nothing recedes for
  being covered, and a dressing or antiseptic works as before. `wolfmed.infection_needs_reason` turns the old model
  back on. `InfectionNeedsAReasonTest` walks the four cases.
- **Nothing systemic before the chest (owner, 2026-09-28).** "The effects of sepsis shouldn't start before it's hit
  the chest either." A spreading limb wound or limb used to run the fever (the temperature climb, the shivers and
  sneezes of `WolfmedConditionEmoteSystem`), which read as sepsis from a hand cut. `fevered` is now fed only by a
  spreading wound or part on the torso or head, and by sepsis itself; `HasFever` reads the same. A limb's infection is
  pain and the analyzer's tissue line until it gets there. `InfectedChestShiversTest` and the hand-travel test pin it.
- **The antiseptic spray sprays people (2026-09-28).** "Targeting yourself with antiseptic spray makes you drink it."
  The spray bottle base is also a `Drink`, and on a click on a person the drink handler ran before the spray's.
  `WolfmedAntisepticSpraySystem` (on `WolfmedAntisepticSprayComponent`) takes the click on any wound host, and the
  use in hand, ahead of both: one press's worth goes onto the target by the same touch reaction the vapor uses, with
  the spray sound and a popup, and nothing is swallowed. A click on the world still sprays a cloud, and the Drink
  verb is still there for anyone who insists. `WolfmedAntisepticSprayTest` covers self, use in hand and another patient.
- **The pod never pushes past a reagent's safe line (2026-09-28).** "When fixing a brain the patient gets poison
  damage." The anaesthetic top-up was bounded only by the sedation cap, which the analgesic does not raise, so a long
  brain repair kept topping it up past the 20-unit poison line. `AutodocReagentEntry.safeUnits` (opiate 12, analgesic
  18, stim 8, spaceacillin 22, each under its overdose line) and `PushReagent` reads what is already in the blood and
  fills only the room left, one reagent of the role at a time. `WolfmedPodReagentSafetyTest` pins it.
- **Bloodloss is capped like Asphyxiation (2026-09-28).** "Someone somehow got 800 O2 damage." The bloodstream deals
  Bloodloss every second under 90% blood (2.5 a second at 30%) for as long as the model keeps the body alive, and
  nothing in Wolfmed reads it; a patient at 30% blood for the nine-minute window carried hundreds of Airloss, the
  group Bloodloss counts in. AUTODOC5's `wolfmed.airloss_cap` (200) ceiling on Asphyxiation, applied where routed
  systemic damage lands, now covers Bloodloss too; it was left out so a vital loss could kill through it, and BRAIN
  moved that death to the life system. `WolfmedBodyDamageCeilingTest` pins both types at the cap.
- **What a reagent deals from inside is toxin load (2026-09-29).** "Radiation medicine causes lots of body damage that
  the new medical system takes to overdrive." Arithrazine deals 1.5 Brute a tick beside its radiation healing, and every
  `HealthChange` tick went through routing like a hit: a cut, a puncture and a bruise on a random part, 60 times over a
  30-unit dose, each bleeding and hurting. A wound is an injury with a site; a chemical in the blood has none, and the
  model's currency for chemical harm is the toxin pool (OD13). `WolfmedReagentDamageSystem.ForWoundHost`, called from
  HOOK 9 for a metabolism (`EntityEffectReagentArgs.Method == null`) on a wound host, turns every positive localized
  amount (brute, burn, cold, shock, caustic) into `wolfmed.reagent_toxin_factor` (0.5) systemic Poison and leaves
  healing and the systemic types alone; a reaction on the skin, an injection or an ingestion keeps its method and still
  wounds. A full arithrazine bottle is 45 toxin, sick but standing (Downed at 60), which the liver or dylovene clears,
  where vanilla's 90 Brute stood next to crit. Machines do not metabolise. `WolfmedReagentDamageTest` pins both halves.
- **A lodged round stops the welder, and says so (2026-09-29).** Peter, playtest 5: "welding will infinitely go on if
  you don't do it in surgery mode", "you will have to do surgery to stop bleeding", "don't be an IPC or have
  cybernetics". A round or a fragment in a breach refuses every treatment, damage removal included (W2), but
  `GetHealingPotential` counted the breach as work, so `CanRepairPart` stayed true, each pass removed nothing and
  `OnWoundRepairFinished` queued the next until the tank was empty; the leak never closed, and surgery, which pulls
  the round first, was the only way. Three changes. `GetHealingPotential` asks each wound the refusal `HealWounds`
  asks (`RefusesTreatment`, one marked line and a helper), so a refused wound is no potential for the welder, the
  applicator, a topical's repeat or the part scoring. `CanRepairPart` refuses a part with anything lodged, and the
  welder says "something is still lodged, pull it out first" (`wolfmed-repair-embedded`) instead of "nothing needs
  the welder". A pass that changed neither the part's damage nor a wound's severity is the last, costs no fuel and
  says so (`wolfmed-repair-no-progress`). `WelderStopsOnALodgedRoundAndSaysSoTest`: the welder does not start on the
  breach, starts once the round is out, closes it in one pass, stops, and spends one pass of fuel.
- **A repair chain is one line, and an instant one lands in the click (2026-09-29).** "The nanite applicator spams 40
  messages in one tick." An admin ghost carries `InstantDoAfters`, so every pass `OnWoundRepairFinished` started
  finished inside `StartWoundRepair`, re-entering the handler: a pass, a sound and "you repair" per level, forty deep
  for a torso with three wound types at their caps. A player got the same forty lines three seconds apart. Now the
  handler notes when a pass it started has already finished (`_repairChains`, `_repairInstant`) and the frame that
  started the chain applies the rest itself (`ApplyRepairPass`, which also pays the fuel and reports a pass that
  changed nothing), so the chain is one sound and one line; a timed chain keeps its end sound per pass and speaks
  once at the end: repaired, `wolfmed-repair-fuel` when the tool ran dry first, or `wolfmed-repair-no-progress`.
  `InstantRepairChainLandsInOneClickTest`: an instant user's 100-damage breach closes in the click, four passes paid,
  nothing queued.
- **Avali stasis on a wound host (owner, 2026-09-29): hold, close, halve, never a bone.** "How does the Avali stasis
  ability interact with our medical system?" Starlight's `StasisSystem` works on the flat damage total: its bleed stop
  is a bloodstream write the wound projection refuses (GUARD E3), its 2-a-second healing is routed damage removal that
  takes 15% off a wound, and its "resistance" heals back half of the total after the wound is made. On a wound host
  all three were nothing. `WolfmedStasisSystem` (server, `_WF/Wolfmed/Stasis`) owns stasis on a wound host: it marks
  the body `WolfmedStasisHoldComponent` while `IsInStasis` (polled, since the stock system holds the enter and exit
  subscriptions) and the bleeding partial's `GetTreatmentMultiplier` returns 0 for every wound on a held body, so the
  bleed stops and comes back with the hold; once a second it thins the parts' stored damage by the component's
  amounts with wound healing off, then spends the same amounts on the body's wounds at topical strength
  (`TreatWound`), first part first, skipping anything topicals never close or that refuses; and on the routed pass of
  a hit (`BeforeDamageChangedEvent` after routing) it keeps `wolfmed.stasis_damage_factor` (0.5) of every positive
  amount before it is a wound. The stock heal-back and update return on a wound host (two marked lines). The owner's
  rule: stasis never fixes a broken bone. `BoneFractureWound` lists no damage types, and `CloseWounds` skips
  `WoundFractureComponent` outright. `WolfmedStasisTest` pins the hold, the half hit against a control, the topical
  close, the stored-damage thinning, the untouched fracture (same wound, grade, severity, no treatment) and the bleed's
  return after exit.
- **A coagulant reaches the wounds (2026-09-29).** "Medicines don't do what they're advertised to, tranexamic acid in
  particular." `ModifyBleedAmount` wrote the bloodstream's bleed figure, which on a wound host is only the wounds'
  projection and refuses every other writer (GUARD E3). Tranexamic acid, bicaridine, inaprovaline, polypyrylium,
  pulped banana peel, stasizium, vitamins, ichor and space glue did nothing to a bleed, and ketorolac's overdose
  worsened none. Onyx's own effect called `ModifyBodyBleeding` on a wound host; the port lost that line. A marked block
  in the effect now calls `WoundBleedingSystem.ApplyReagentBleeding` for a wound host, and the upstream write stays for
  everything else. A coagulant takes its amount times `wolfmed.bleed_rate` off the body's bleed rate (the knob every
  wound's rate carries, so a dose clots the severity it clotted in Onyx), worst bleed first, and never touches a
  treatment: a dressed cut keeps its dressing (`ModifyBodyBleeding` resets it to None, which on a dressed wound
  quadruples the rate before the cut), and a clamped or tourniqueted wound bleeds nothing, so it is skipped. An artery
  gets the topicals' stance, slow and never stop: `WolfmedArterialBleedBehavior.CoagulantFloor` (0.5) is the share of
  the wound's severity no drug clots below, so tranexamic acid halves a pumping artery, which still refuses every
  treatment and still needs a tourniquet or the table; the evisceration wounds carry the same floor. A dose that
  worsens bleeding opens or deepens a systemic bleed, as Onyx's did. One tick of tranexamic acid takes 0.375 off the
  rate; the emergency medipen's 3 u is 15 ticks. `WolfmedReagentBleedingTest` pins the tick, the stopped cut, the
  artery at its floor with and without gauze, the kept dressing and tourniquet, ketorolac's systemic bleed and a
  mouse's bloodstream figure taking the upstream write.
- **An overdose written as airloss is toxin load (2026-09-29).** Found auditing the reagents behind the tranexamic
  acid report. A metabolising reagent's Asphyxiation and Bloodloss are bookkeeping on a wound host: Asphyxiation is
  read only while the respirator is suffocating, Bloodloss never, both capped at `wolfmed.airloss_cap`. So every
  overdose the flat model wrote as airloss cost a breathing patient nothing: tranexamic acid's (Bloodloss 3 a tick past
  15 u), dexalin's, dexalin plus's, epinephrine's, bicaridine's, dermaline's, polypyrylium's, celoxradine's,
  rhymatine's, fentanyl's respiratory depression, amoxla's in a non-Avali and the dexalin family's Avali poisoning in
  part, and the poisons that work only through airloss (lexorin, heartbreaker toxin, histamine's share, BZ, nitrium).
  `WolfmedReagentDamageSystem.ForWoundHost` now converts positive Asphyxiation and Bloodloss the way it converts
  localized damage, at `wolfmed.reagent_toxin_factor`. Healing is untouched, so dexalin still takes airloss off a
  suffocating patient, and a reaction on the skin keeps its method as before. Sedation's respiratory depression is its
  own route and deals no Asphyxiation, so nothing is counted twice. `WolfmedReagentDamageTest.MetabolisedAirlossIsToxinTest`
  pins ten ticks each of the tranexamic acid and dexalin overdoses as 35 toxin and none of either type, and dexalin's
  heal.
- **Reagent descriptions say what a wound host gets (2026-09-29).** Texts that promised what the model does not do.
  Dexalin, dexalin plus and cryoxadone "treat bloodloss": their Bloodloss healing is bookkeeping, and lost blood is the
  blood level, which none of them raises (blood, saline, the IV and amoxla for an Avali do), so they now say they do
  not replace lost blood. Tranexamic acid "causes heavier bleeding on overdose": it says it slows every bleed, stops the
  lesser ones, only slows an artery, and is poisonous on overdose. Ultravasculine's overdose "causes extreme pain",
  rhymatine trades cellular damage "for cold and shock damage" and stasizium's overdose "can tear the body apart", all
  toxin load since the reagent-damage decision above, and each now says so. Marked lines in the upstream, Mono and
  Goobstation locale files; no effect changed. Puncturase's "slight amount of tissue damage" (0.04 toxin a tick) was
  left as it reads.
- **The reagent audit (2026-09-29).** Every medicine with a bleed, blood, airloss or bloodloss effect, and the ones the
  report named, read against what the model reads. Working, and now driven on a real body from each reagent's own
  prototype by `WolfmedReagentAuditTest`: saline's blood reaches the blood level the circulation clock reads; dexalin
  plus's airloss healing lowers a suffocating body's hypoxia input; osteogen knits a simple break and leaves a
  comminuted one alone; leporazine's heat rewarms the core at once. Read and left: epinephrine is a Stimulant tier, so
  it slows the brain's drain (`wolfmed.brain_stimulant_factor` 0.6) and lifts Downed, and in Unconscious (Critical) its
  brute, burn and toxin healing lands; inaprovaline's crit airloss healing counts only while the body suffocates, since
  an unconscious wound host breathes, and its bleed reduction is the coagulant above; Bloodloss healing anywhere
  (cryoxadone, necrosol and omnizine through the Airloss group, ichor, nanites) is bookkeeping. The healing reagents
  (bicaridine, dermaline, lacerinol, puncturase, sigynate, insuzine's shock) heal through HOOK 9 and close wounds at
  the wound's healing multiplier, and their side damage is toxin load. `AvaliChemistryTest` still expected ammonia to
  burn a human, which the reagent-damage decision made toxin load; it now reads the Poison. Left open: hemophilia (Mono
  trait) still adds no bleeding on a wound host (GUARD E4).
- **A makeshift tourniquet torn from a jumpsuit (owner, 2026-09-29).** "We need a makeshift tourniquet that can be made
  out of a jumpsuit." `WFWolfmedMakeshiftTourniquet` (`_WF/Wolfmed/Entities/tourniquet.yml`, parented to `Tourniquet`,
  its sprite recoloured to grey cloth in `Medical/makeshift_tourniquet.rsi`) is crafted by hand from any jumpsuit with
  suit sensors: construction `WFWolfmedMakeshiftTourniquet`, one `component: SuitSensor` step, 3 s, Tools. It clamps a
  limb exactly as the real strap does and is worse in two ways the model already had. It takes 3 s to tie against the
  real one's 0.5, and it slips: the tied part's `WolfmedTourniquetComponent.SlipDamage` takes the item's
  `WolfmedMakeshiftTourniquetComponent.slipDamage` (15; null for the real strap, which holds through anything since
  playtest 4), and `WolfmedTourniquetSlipSystem`, called from `WolfmedPartHitSystem.OnHit` before the wounds see the
  hit, knocks it loose on one hit of 15 or more on the strapped part (after armour, and only a hit that could interrupt
  a do-after: fire and bleeding ticks never count). The strap's own 5 Blunt and 5 Asphyxiation stay under the line, and
  so does a punch or a 10-damage knock; a round or a heavy swing does not. A slip is a loosen nobody asked for
  (`WolfmedNecrosisSystem.Slip`): the bleeding starts again, the necrosis clock stops, and everyone near sees "The
  makeshift tourniquet on the right leg is knocked loose!". Slipping was chosen over holding a smaller share of the
  bleed because a partial clamp would read as a working tourniquet on the analyzer while the patient kept bleeding;
  a slip is visible and its answer (tie another, or fetch a real one) is obvious. Crafting takes a jumpsuit in hand
  first, but upstream's crafting also takes worn items, and the jumpsuit slot comes before the backpack, so with none in
  hand it tears the one being worn (the banana clown suit and ID card recipes behave the same); the recipe and the
  guidebook say to hold the one to tear. Fixed in passing: loosening a strap released only the strapped part, so a
  leg's strap left the foot's bleed clamped with nothing holding it; loosen, slip and the necrosis clock now cover the
  parts below it (`TiedParts`), as applying one always did. The guidebook and the arterial and bleeding advice name
  the makeshift strap. `WolfmedMakeshiftTourniquetTest`: the held jumpsuit is torn, not the worn one, in 3 s; the strap
  is not on after 1 s and is after 4; a 10 hit leaves it on; a 20 Slash slips it and the leg bleeds again while the
  real strap on the other leg holds; `SlippedStrapReleasesTheFootTest` pins the foot.
- **Sutures anyone can get (owner, 2026-09-29).** "We need more accessible sutures, ones that are found in more places
  (a downgraded one of the medicated ones, that don't require research, and makeshift variants of them)." Two tiers
  under `MedicatedSuture`, which stays the top one (`_WF/Wolfmed/Entities/sutures.yml`, sprites from EscapeFromNevado's
  `stack_medical.dmi` in `Medical/sutures.rsi`, each stack drawn by its count through `layerStates`, the medicated
  suture's in-hands; stacks of 15 like it). The **suture** (`WFWolfmedSuture`, blue) closes cuts and punctures and
  counts as sutured for infection (`WolfmedSuture`) exactly as the medicated one does, at half the closing a use
  (Brute -30, so 10 Slash and 10 Piercing, against -60), bloodloss -6 against -10, and 3 s against 2. It needs no
  research: a lathe recipe in `TopicalsStatic` (one per print, 25 Steel and 50 Cloth, beside the bruise pack and
  gauze), the four medical vendors (NanoMed Plus 4, NanoMed 2, CiviMed infinite, the Wolfgate shop 4), and the
  common and classy medical loot spawners and both dungeon meds spawners (marked lines). Its stack price is the bruise
  pack's 15, printed from about the same materials. The **makeshift suture** (`WFWolfmedMakeshiftSuture`, tarred) is
  a metal rod and a cloth, crafted anywhere in 4 s into five (`WFWolfmedMakeshiftSuture5`); -18 (6 and 6), bloodloss
  -4, 5 s, stack price 1, and dirty. The owner's infection rule (an infection needs a reason) already had "something
  dirty went into the wound" (`Contaminate`, the knife dig), so a dirty tool is one more caller: the item carries
  `WolfmedDirtyTreatmentComponent`, and `WoundHealingSystem.TryApplyHealing` (one marked block, the helper in the
  `_WF` partial) calls `WolfmedInfectionSystem.ContaminateTreated` on the part once the item has done anything, which
  contaminates every open, infectable wound the item treats (the same choice `MarkSutured` makes). It does not carry
  `WolfmedSuture`: a sutured wound infects at the profile's Sutured rate, 0, so a dirty stitch that counted as one
  could never infect at all. A makeshift-closed cut keeps the dressed rate (0.15) times the contamination (2.5), has a
  reason to infect even under a hardsuit, and clears with antiseptic or a proper suture over it; the analyzer's
  procedure leaves its Suture and Clean rows open, and the advice says why. The advice names sutures generally
  instead of "medicated sutures" throughout, the slash, piercing, gunshot and bleeding procedures name the tiers, and
  the guidebook's Biological tissue section lists both. `WolfmedSutureTiersTest`: one use each on the same 15 Slash
  cut removes medicated > plain > makeshift, closes the wound in the same order, and the delays climb 2, 3, 5; through
  the do-after the plain suture marks the cut sutured and leaves it clean, the makeshift one leaves it contaminated
  (past 1) and not sutured. The treatment matrix lists both new items against every wound, and
  `WolfmedAvailabilityTest` finds the suture in a vendor or lathe and the makeshift one in a construction recipe.
- **More to keep someone alive in the kits and belts (owner, 2026-09-29).** "Medkits and EMT belts need to have more
  lifesaving stuff in them (tourniquets, and whatever else that can temporarily save your life)." What holds a patient
  for the ten minutes to real care: a tourniquet for a limb bleed, a suture for a cut, a splint so they can move, a
  painkiller pen that gets a downed patient up at once, and an epinephrine pen (the emergency medipen: 12 epinephrine,
  which slows the brain clock, and 3 tranexamic acid) where the kit's tier warrants it. The combat and advanced kits stay
  ahead of the standard one. `Medkit`'s grid goes 6x2 to 7x2 in its marked block (every typed kit inherits it) and
  `MedkitCombat`'s own 4x2 to the same 7x2 (new marked block): the standard kit was full at 12 and the combat kit at 8,
  and neither could take what it lacked otherwise. The belts' 8x2 had room.
  - `MedkitFilled`: bruise pack, ointment, gauze, tourniquet, splint, analgesic canister, tricordrazine canister, and
    now a suture. 14 of 14.
  - `MedkitBruteFilled`, the trauma kit: bruise pack, gauze, iron and copper canisters, splint, osteogen canister, and
    now a tourniquet and a suture. 13 of 14.
  - `MedkitAdvancedFilled`: medicated suture, regenerative mesh, two blood packs, tourniquet, and now a splint, an
    emergency medipen and an analgesic pen. 14 of 14.
  - `MedkitCombatFilled`: medicated suture, regenerative mesh, ephedrine and saline syringes, brute and burn
    auto-injectors, and now a tourniquet (it had none), a splint and an emergency medipen. 13 of 14.
  - `ClothingBeltMedicalFilled`: two bruise packs, ointment, blood pack, gauze, emergency medipen, and now a tourniquet
    and a suture. 15 of 16.
  - `ClothingBeltMedicalEMTFilled`: bruise pack, ointment, blood pack, gauze, three emergency medipens, and now a
    tourniquet, a suture and an analgesic pen. 16 of 16.
  - Left alone: the burn, toxin, oxygen and radiation kits (none of them is for a bleed; the burn kit is 8 of 14 now),
    the stimkit, and the CMO's webbing, which carries a medicated suture already.
  Prices: the plain suture's stack price drops from 15 to 5 a unit (75 a full stack), a little over the 4 its print
  costs, because the kits and the contractor loadout's free filled belts now each carry a stack; a tourniquet
  appraises at 0 and the pens at their reagents. The medkit crates in `cargo_medical.yml` are Frontier-abstract and
  cannot be ordered. `NoShipyardShipArbitrage` (every vessel's mapped kits and belts), `NoCargoOrderArbitrage` and the
  storage fill tests pass; `WolfmedAvailabilityTest.FillsContainWhatTheyDeclareTest` spawns all six fills and finds
  every certain entry in each, so a fill that stops fitting fails there.
- **Corpses spawn dead with their injuries (2026-09-29).** "SOME dead bodies aren't actually spawning as dead bodies."
  Measured on the build: `SalvageHumanCorpse` and its family (`MobRandom*Corpse`, `DungeonHumanCorpse*`, every salvage,
  expedition and dungeon corpse spawner) stood up Alive with no injuries, and of sixteen medical-bounty corpses none was
  dead, three had nothing at all to treat (instantly redeemable) and the poisoned ones were in a toxic coma. A corpse
  prototype carries its injuries as preset `Damageable` damage; the projection resets a wound host's damage to its
  parts at map init, and the Dead threshold that preset crossed does not decide a wound host (CONSC). A medical bounty
  deals its roll at `ComponentStartup`, before `SharedBodySystem` builds the parts, so every brute and burn point went
  nowhere and only the systemic types landed. The dead mouse was never affected: it is no wound host.
  `WolfmedSpawnInjurySystem` (`MapInitEvent` on `WolfmedConsciousnessComponent`, after the body and the projection)
  lays the prototype's preset damage, plus whatever `Defer` queued, on the built body: Bloodloss as missing blood
  (`wolfmed.spawn_bloodloss_blood`, 0.4% a point: the salvage corpse's 49 leaves 80%), the rest spread over every part
  with variation over the head, torso, arms and legs through `TryApplyDistributedDamage` with no origin, so the ambient
  ceilings keep every limb on (hands and feet left out so a salvage corpse's 56 Slash is cuts, not ten scratches), and
  the bleeding stopped (a body found like this stopped bleeding a while ago). A total at the body's Dead threshold is
  `WolfmedLifeSystem.Kill`, the threshold's own rule, revivable like any Wolfmed death. The bounty hands its roll to
  `Defer` (a marked block in `MedicalBountySystem`); anything not a wound host keeps the old call. The wound sounds and
  the tendon snap stay quiet while a body takes its spawn injuries, since nothing hit it. `WolfmedCorpseSpawnTest`:
  three corpse prototypes dead, cut, missing a fifth of their blood, not bleeding, all six parts on; the dead mouse
  still dead; three fixed bounties dead with their injuries over three or more parts and over the redemption line;
  twenty-four random bounties, none redeemable at spawn.
- **A begun procedure stays open until its closing step (2026-09-29).** A healmeimbroken ticket: "surgery fails at the
  graft the burned tissue step saying it requires a previous step which is already complete". Reproduced through the
  surgery window's own message with the tools in hand and the do-afters run out: the graft takes the charring, and
  the step after it, Seal the wound, is refused without a word, because `WolfmedSurgeryWoundCondition` lists Graft
  Burned Tissue only while charring exists. The patient is left open under "Requires: Open Incision". Every Wolfmed
  procedure whose treatment removes its own reason had the same dead end for a surgeon: tendon, artery, servo,
  internal bleeding, amputation stump, embedded objects, fracture mending and the organ heals; the pod was never
  stuck because it stops a procedure that stops validating and closes up itself (AUTODOC). The existing
  `validWhile` (core repair) was no answer: an incision marker would list every wound procedure on any open part.
  `WolfmedSurgeryProgressComponent` on the part records a Wolfmed-conditioned procedure when a surgeon's step of it
  runs (`WolfmedStepDone`, one marked line after `OnTargetDoAfter` raises the step) and drops it on the procedure's
  last step; the wound, fracture, organ and embedded conditions hold while it is recorded; an incision closed any other
  way (Close Incision) clears the part's list. The pod's own path does not record, so the pod is unchanged.
  `WolfmedSurgeryClosingStepTest`: the graft and the embedded-object removal each run from the window to their own
  seal, the torso closes and the procedure leaves the menu; an open torso without charring still does not offer
  the graft.
- **A minor wound is not infected by the air (owner, 2026-09-29).** "Minor burns cause sepsis after a while? I think
  minor injuries shouldn't cause infections." Every infectable wound carries its risk at every stage, and open air is
  a reason, so a severity-10 scald on an unsuited arm went local, crept up its 15, spread and reached the chest.
  `HasReason` now refuses the air as a reason for a wound whose current stage is Minor (`WolfmedInfectionSystem.IsMinor`:
  under 25 on every organic wound, the analyzer's own word), behind `wolfmed.infection_spares_minor` (true). A dirty
  wound (a bite, a round or shrapnel left in, dead tissue) and a contaminated one (a makeshift suture, a dirty
  object) are still reasons at any size; charring, an operative incision and a stump have no Minor stage, so they
  are never spared. A wound that already carries infection and heals down to minor holds where it is.
  `InfectionNeedsAReasonTest` adds a minor cut and a minor burn in the open for thirty minutes (both at zero) and a
  minor bite (it goes bad); the spread fixtures' cut is 30 now, moderate, so they still test what they tested.
- **Dying asleep wakes the body (2026-09-30).** "Someone went to sleep while dead and can't wake up when fixed."
  A body in cardiac arrest is Critical, looks dead, and may still sleep (only Dead refuses it). Falling asleep puts a
  timerless `StunnedComponent` and `KnockedDownComponent` on the body, and only `SleepingSystem.Wake` takes them off,
  with the Wake action; `OnMobStateChanged` removed `SleepingComponent` bare on death, so a revived patient stayed
  down for good and the Wake action had no sleep to end. A marked block now calls `Wake` there.
  `WolfmedSleepThroughDeathTest`: asleep in arrest, killed, revived: no sleep, stun or knockdown left.
- **Executions and weapon suicides leave gore by the weapon's strength (owner, 2026-09-30).** "Suicide via methods
  like shooting ones self in the head should kill you, in a gory way ... a weak gun should just kill + artery your
  head; a medium one shoots a hole in your head and your brain flies out; a heavy gun (shotgun) blows your head off.
  Lasers can do similar-ish things." A gun execution, and the gun Execute on yourself, did not kill a wound host at
  all: the DV system dealt one origin-less head hit and the brain lost 3 of 15. `WolfmedExecutionSystem` (Life) now
  owns both: `Measure` reads the weapon, `Apply` kills with `EndDeliberately` (unchanged, on every tier and species)
  and then marks the head. Ballistic: weak a gunshot wound and an arterial bleed; medium a critical gunshot wound and
  the brain torn out and thrown; heavy the head destroyed. Energy: weak a burn and charring, no bleed; medium deep
  charring, the brain left where it sits; heavy the head burned to ash. Blade: weak the artery; medium the artery and
  a severe cut; heavy a clean decapitation through `TryAmputate`. Everything is a direct call (a wound, an organ out,
  an amputation), never a damage number: one hit can neither sever nor ash a head. Every `Apply` that kills or
  mutilates writes `LogType.Damaged` at Extreme with attacker, victim, weapon, kind, tier and outcome.
  `WolfmedExecutionTest`: `GunExecutionTiersTest`, `EnergyHeavyAshesTheHeadTest`, `BladeTiersTest`.
- **How a weapon is measured (2026-09-30).** One trigger pull or one swing, on the damage types that land on a body
  part (Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic): Structural and Radiation never count, so a 12 gauge slug
  is 34 and not 234. Guns: the round's own damage (`SharedGunSystem.GetBulletDamage`, which reads a cartridge that
  fires a hitscan; the DV code read .357 FMJ as zero), times its pellet count, times the gun's damage modifier with
  `GunDamageModifierEvent`; never the x9 execution modifier. Lines `wolfmed.execution_medium` 30 and
  `wolfmed.execution_heavy` 60, and a spread of pellets is heavy whatever it sums to. Blades: one ordinary swing by
  that user (`SharedMeleeWeaponSystem.GetDamage`, so a wielded fire axe counts 45), read with the execution's own x9
  switched off; lines `wolfmed.execution_blade_medium` 22 and `wolfmed.execution_blade_heavy` 40. Energy is Heat,
  Shock, Cold and Caustic making more than half. Non-lethal is a round under 5, or one that carries a stamina, stun
  or explosive component, has no Piercing or Slash and is 20 or less: rubber, beanbag, disabler, taser, and a
  launcher's shell, whose Blunt 7 is a carrier for the blast and would otherwise read as a weak bullet. Both tests run
  on the single projectile before pellets and modifier, so a practice shell's six 1-point pellets stay non-lethal.
  A non-lethal round, a spent casing and an empty gun fall through to the old head hit. A blade is never non-lethal:
  a glass shard already slits a throat upstream. The gun hook measures the round the shot actually took
  (`MeasureRound`), not a peek, so what is chambered at that moment decides; `Measure` peeks, and reads a revolver's
  chamber under the hammer itself because the shared peek reads the one behind it. `MeasureTest`.
- **A heavy energy weapon ashes the head (owner, 2026-09-30).** "or turning it to ash if laser is used that is big
  enough." This overrides OD12 (the head and torso never crumble to ash) for a deliberate execution or suicide with a
  heavy energy weapon only; ambient burning still never ashes a head. `BurnPart` and `GibPart` raise no
  `WolfmedPartAmputatedEvent` and so leave no stump, no neck overlay and no death bookkeeping, so the head comes off
  through `TryAmputate` first and the loose head is destroyed afterwards: its organs dropped beside the body, then
  `Ash` and a cauterised stump for a laser, or the `Gib` decal pool for a bullet. A chassis head leaves no ash and
  no gibs. `EnergyHeavyAshesTheHeadTest`.
- **The brain is never deleted by an execution (2026-09-30).** Wolfmed's rule is no permanent unrevivable state. The
  brain stays in the head (weak, energy medium), lies thrown on the deck (ballistic medium) or is dropped where the
  head was (heavy), so surgery or a replacement head is still a way back, and `UnrevivableComponent` is never added.
  An execution victim's mind rides the brain when it leaves, as it does on any decapitation today; a suicide has
  ghosted first. `GunExecutionTiersTest` asserts the loose brain on the medium and heavy tiers.
- **Species the plan does not fit (2026-09-30).** The kill always happens; a gore step that cannot apply is skipped,
  never thrown. A chassis and a slime keep the core in the torso, so nothing is thrown from the head. A diona's brain
  is not torn out (out of the body it becomes a living nymph): its medium tier is the wound alone. A head with no
  arterial or gunshot wound in its profile takes its own equivalent: slime and plant piercing, slash and burn wounds,
  a breach or overheating on a chassis. Heavy takes the head off anything that has one. `SpeciesTest`.
- **A Downed body can be executed (owner, 2026-09-30).** "if someone is downed and the other player wishes to execute
  them." Both Execute verbs required `CanInteract(victim, null)` to fail, and a Downed body passes it once its fall
  stun ends. `WolfmedDownedComponent` now counts as incapacitated in `SharedExecutionSystem.CanBeExecuted` and the DV
  `CanExecuteWithAny`; everything that passed before still passes. `DownedIsExecutableTest`.
- **The gun Execute on yourself is a suicide (2026-09-30).** On a wound host with a lethal round it raises
  `SuicideEvent` and `SuicideGhostEvent` before the gore, as the knife path does, so the ghost is out and cannot
  return. The `SuicideEvent` goes out already handled: the gun is the method, and unhandled it runs the tongue-bite
  default and the sweep for a nearby microwave. `OnYourselfKillsTest` covers a body with no player,
  `GunOnYourselfGhostsTest` a player's body, the spent round and the ghost.
- **Execution hooks (2026-09-30).** `WolfmedEndingEvent` carries the attacker and the weapon from its two marked raise
  sites, and its one subscriber applies the Blade tier instead of a bare `EndDeliberately`. The DV gun completion has
  two marked edits: the round is measured before the switch that spends or deletes it, and
  `TryGunExecution` runs in front of the old damage line, which stays for everything it refuses. No new
  subscription.
- **"Are you sure?" on both Execute verbs (owner, 2026-09-30).** "shows the executor a popup saying 'Are you sure?'
  or something." `Verb.ConfirmationPopup` is client-only, says a hard-coded "Confirm" and is compiled out in DEBUG,
  so the question is the server's: `WolfmedChoiceEui`, the window Succumb already uses, with no new client class. For
  an executor with a player behind it the verb opens the dialog and starts nothing. A yes checks again everything
  the verb menu checked (that weapon still the one in the active hand, the victim in reach, `CanBeExecuted` or
  `CanExecuteWithGun`) and only then starts the same do-after as before; a no, a closed window or a disconnect does
  nothing. One question per executor: a second Execute replaces it, and it is withdrawn on round restart and when
  the executor dies or goes Unconscious (a broadcast `MobStateChangedEvent`; Unconscious is Critical on a wound
  host). An executor with nobody behind it is not asked and starts the do-after directly, as it always did. The text
  names the victim through `Identity.Entity`: "Kill X with the knife? This cannot be taken back." On yourself:
  "End your own life with the pistol? You will not be able to return to this body.", or a plain "Shoot yourself in
  the head?" where that promise would not hold (a gun on a body Wolfmed does not own, and the roulette shotgun, whose
  next shell the dialog must not give away). `ConfirmationTest` answers through `Confirm`, `Decline` and `GetPending`.
- **A weapon that will not kill is refused (2026-09-30).** A player whose weapon measures non-lethal against a wound
  host (a disabler, rubber, a beanbag, an empty gun) gets "The disabler won't kill anyone." and no dialog and no
  do-after, at the verb and again at the yes. Only there: against a body Wolfmed does not own the old x9 head hit can
  still kill, so that Execute is asked and runs as before, and an executor with no player still takes the old
  path. The roulette shotgun is never refused: saying its next shell is a dud is the one thing it must not do.
  `NonLethalIsRefusedTest`.
- **How the dialog reaches the do-afters (2026-09-30).** Both start methods are private, and the melee one is shared
  and predicted. Each got one marked block after its own checks that returns when the server asks first, and a `_WF`
  partial of the same class with `StartConfirmedExecution`, which runs the method again with the question switched
  off; the upstream lines are untouched. The melee partial raises `WolfmedExecutionAskEvent` (shared code cannot name
  the server system) and answers "asked" on a client without raising it: a client's own executor is always asked, so
  the do-after is never predicted and comes down from the server like the gun's. The executor's "You ready the
  knife" line is a predicted popup the client no longer shows, so the confirmed start sends it from the server.
- **The suicide command uses the weapon in hand (owner, 2026-09-30).** "Suicide via methods like shooting ones self
  in the head should kill you, in a gory way." No gun handled `SuicideByEnvironmentEvent`, so the command with a gun
  in hand bit the tongue. `WolfmedExecutionSystem` takes that pair: on a wound host, a gun in the active hand that
  measures lethal is fired the way the Execute do-after fires it (`ShotAttemptedEvent`, `AttemptShootEvent`, one
  round taken and spent, the shot sound, the DV "shoots themselves in the head" line) and `Apply` leaves that round's
  gore. A gun that will not fire, an empty one, a less-lethal round or a body Wolfmed does not own leaves the event
  unhandled and the command's default runs and still kills. The command also raises the event on whatever stands
  within reach, so only the gun in the active hand answers: a mounted gun next to the body does not. With a blade,
  upstream's own handler now raises `WolfmedEndingEvent` (a third marked raise site), so the blade's tier is applied
  there too. `Suicide()` still calls `EndDeliberately` afterwards, which refuses a body that is already dead.
  `SuicideCommandWeaponTest`: a pistol (round spent, gunshot wound and artery), an empty shotgun (dead, no gunshot
  wound), a claymore (head off, which no single hit's damage can do); every one a ghost that cannot return.
- **A blade Execute on yourself applies once, after the ghost (review, 2026-09-30).** The do-after raises
  `SuicideEvent` unhandled, `SuicideSystem` forwards it to the blade in the active hand, and upstream's
  `OnSuicideByEnvironment` then ran the suicide command's marked raise as well: the tier landed while the mind was
  still in the body, and again after the ghost. A kitchen knife's artery merged to 40, and a claymore took the head
  off with the player in it, who sat in the brain and could come back. That raise is now skipped while the blade is
  `Executing`; the do-after's own raise, after `SuicideGhostEvent`, is the only one. The suicide command is unchanged:
  it ghosts first. `BladeOnYourselfGhostsTest` (a player's body: artery 20, head off, a ghost that cannot return, no
  mind in the brain); `OnYourselfKillsTest` now asserts the artery's severity.
- **An explosive charge marks a carrier shell only (review, 2026-09-30).** The explosive marker made every explosive
  round of 20 or less non-lethal, which also caught rounds that burn: the 6.8 mm caseless plasma round (Heat 15), the
  Lawbringer's explode bolt and the fireball. Every launcher shell it was added for is Blunt alone (rocket, 40 mm,
  cannonball, seismic charge: 7), so the marker now counts only on a round with no energy share; the stamina and stun
  markers are as before. The plasma round reads Energy weak. `MeasureTest`, which also takes a flechette shell
  (6 x 7 = 42) so that "a spread is heavy whatever it sums to" is pinned by a load under the heavy line.
- **Blunt weapons execute (owner, 2026-09-30).** "Blunts should be able to execute." A fourth kind, Blunt, beside
  Ballistic, Energy and Blade. `MeasureMelee` (was `MeasureBlade`) reads one ordinary swing through
  `SharedMeleeWeaponSystem.GetDamage` outside the `Executing` window, wield bonus included, Structural left out: more
  than half Blunt is Blunt, anything else is still a blade. Lines: `wolfmed.execution_blunt_medium` 20,
  `wolfmed.execution_blunt_heavy` 40. Weak (crowbar 16, a bat in one hand 15) cracks the skull: a blunt wound and a
  Simple fracture on the head. Medium (a bat or a sledgehammer in both hands 25, a shovel 24) caves it in: a severe
  blunt wound and a Comminuted fracture, the brain left where it is at 0, nothing thrown. Heavy (a breaching hammer
  in both hands 65, the shock maul 75) crushes the head, which is exactly the Ballistic heavy result: head destroyed,
  organs dropped, stump on the torso, gib decals. The kill is `EndDeliberately` first, as on every kind. `MeasureTest`,
  `BluntTiersTest`.
- **Blunt executions on other species (2026-09-30).** The same rule as the rest: the kill always happens and a step
  that cannot apply is skipped. A slime, a diona and a chassis have no fracture profile, so nothing breaks; the head
  takes its own blunt wound (slime, plant) or a dent (chassis, with the machine popups), and a head that will not
  come off steps heavy down to medium. A cybernetic head has a frame to break and breaks it. `SpeciesTest` now runs
  the Blunt kind and asserts the dent and the absence of a fracture.
- **A fracture made outright (2026-09-30).** Fractures were only ever rolled from a hit
  (`HandlePartDamageApplied`), and `SetGrade` is private. `WoundFractureSystem.Break(part, grade)` is a `_WF`
  partial of the Onyx class: it creates the profile's fracture wound at that grade's threshold, or raises an
  existing break up to it, and returns null where the part has no fracture profile. The Onyx file is untouched.
- **Which blunt weapons carry Execution (owner, 2026-09-30).** "Blunts should be able to execute." Every held weapon
  or tool whose swing is mostly Blunt and at least 10 Blunt, in one hand or wielded, gets the component in YAML: the
  crowbars, wrench, shovel, rolling pin, jaws of life, maintenance jack, the six toolboxes and the grey one, the
  robust toolbox, fire extinguisher, both mops, the seclite and tac-lite, baseball bat, sledgehammer, Mjollnir,
  singularity hammer, pickaxe, kanabou, breaching hammer, shock maul, the Goob hammer, caveman club and the gorilla
  gauntlet (34 prototypes, 22 more by inheritance). On a base prototype only where every child qualifies, so the
  crowbars and toolboxes carry it one by one (`BaseCrowbar` has the pocket crowbar at 6, `ToolboxBase` the weapon
  cases and the cow toolbox). Left out: stun and stamina weapons (stun prod, truncheon, cane, the Overseer mace),
  guns and the crusher (a `Gun` already has the DV Execute verb), worn gear (gas tanks, jetpacks, magboots,
  gauntlets, glasses), instruments, logs, the clipboard, the desert stone, weapon and document cases, mech equipment,
  structures and the debug weapons.
- **A blunt weapon's lines are its own (2026-09-30).** The upstream melee lines slit a throat. Each blunt weapon's
  `Execution` component sets all eight `LocId` fields to `wolfmed-execution-bludgeon-*` ("raises the bat over X's
  head", "brings the bat down on X's skull", and the self variants), with the same three variables. `MeasureTest`
  sweeps every prototype that carries `Execution`: all eight lines resolve, and none whose swing is mostly Blunt
  still uses a throat line. Two cult staves inherit the shovel's component and cut (Slash 13): they speak as
  bludgeons and are measured as blades. A pickaxe in one hand is half Piercing and measures as a blade; in both
  hands it is Blunt.
- **A blunt execution takes its do-after (owner, 2026-09-30).** "It should also have a doafter timer." The melee
  Execute already runs `ExecutionComponent.DoAfterDuration` (5 s, broken by moving or by damage) once the question
  is answered, and a blunt weapon goes through the same verb, the same dialog and the same do-after with no new
  code. `BluntDoAfterTest` pins it with a player and a bat: asked first, a do-after of exactly the component's
  length, the victim alive and unmarked at four seconds and dead at seven, and a second one called off when the
  executor steps away.
- **The suicide command and Execute on yourself with a blunt weapon (2026-09-30).** Both reach `Apply` through
  `WolfmedEndingEvent`, whose subscriber measures the weapon, so the kind follows the weapon: a crowbar or a bat
  cracks the skull and opens no artery. `BluntOnYourselfTest` (a Simple fracture, a blunt wound, no artery, no cut,
  a ghost that cannot return).
- **A swing that does nothing is not lethal (2026-09-30).** Melee was never non-lethal, and with blunt weapons in
  the foam caveman club (Blunt 0) inherits the component from the real one. A melee weapon whose swing adds up to
  nothing now measures NonLethal and is refused like a disabler; anything above zero still kills, as a glass shard
  always did. `MeasureTest`.
- **The suicide command with a weapon that harms nobody (2026-09-30).** The foam caveman club (Blunt 0) inherits
  `Execution` from the real one, and `SharedSuicideSystem.ApplyLethalDamage` shares the lethal amount out over the
  weapon's total: zero over zero, a `DivideByZeroException` after the player had been ghosted for good, the body left
  alive. `SharedExecutionSystem.OnSuicideByEnvironment` now takes a copy of the weapon's damage without Structural
  and leaves the event unhandled when nothing is left, so the command's default runs and kills, on a wound host and
  on any other body. `BluntOnYourselfTest`, `SuicideCommandElsewhereTest`.
- **The lethal amount is not shared with Structural (2026-09-30).** The same upstream method takes its total with
  Structural in and then removes Structural, so only the rest of the lethal amount lands. A wound host never noticed
  (`EndDeliberately` kills it), but a body Wolfmed does not own did: a monkey with a breaching hammer (Blunt 15,
  Structural 50) took 47 of its 200 and lived, its player gone. Upstream had this with the fire axe alone; every
  blunt weapon that carries Structural brought it along. The copy without Structural is what is passed on, so the
  whole lethal amount lands. `SuicideCommandElsewhereTest` (breaching hammer, fire axe, bat, foam club on a monkey).
- **The blunt heavy line is 50 (2026-09-30).** At 40 a maintenance jack in both hands (12 + 33 = 45) crushed a head,
  and the engineering vendor gives jacks away. The data has a gap between it and the breaching hammer's 65, so
  `wolfmed.execution_blunt_heavy` is 50 and the jack caves the skull in instead. `MeasureTest` pins the jack and
  names the only blunt weapons that reach the heavy line (the breaching hammer and the shock maul, in both hands),
  so another one arriving is a decision and not an accident.
- **An even split is a bludgeon, and the cult staves speak as blades (2026-09-30).** Replaces the last two sentences
  of "A blunt weapon's lines are its own". `MeasureMelee` counts at least half Blunt as Blunt, so a pickaxe in one
  hand (Blunt 5, Piercing 5) cracks the skull its lines bring it down on. `WizardStaffMeleeBlood`, and the dark bolt
  staff under it, cut (Slash 13): they restate the upstream throat lines over the shovel's bludgeon ones.
  `MeasureTest` now spawns every prototype that carries `Execution` and measures it, in one hand and in both where
  it has a wield bonus: a weapon measured Blunt that slits a throat fails, and so does one measured Blade that is
  brought down on a skull. Upstream's test prop (Slash 5, Blunt 5, default lines) is the one named exception.
- **A tier is a floor under the swing (2026-09-30).** The melee Execute still makes its upstream swing first, nine
  times the weapon's damage with resistances bypassed, on the part the executor aims at; the tier is applied after
  it. Aimed at the torso, where a fresh body aims, the head carries the tier alone, and that is what `BluntTiersTest`
  and `BladeTiersTest` pin exactly. Aimed at the head the swing lands there too: a crowbar's 144 Blunt leaves a
  Comminuted fracture, a crush injury and a concussion, `Break(Simple)` leaves the worse break alone, and the weak
  tier's "skull cracks" understates it. Kept as it is: the swing is upstream's own and a blade's lands the same way,
  the victim is dead on every tier, and the ladder still orders (the brain stays in at weak and medium, only heavy
  takes the head off). `BluntTiersTest` runs a crowbar and a wielded bat aimed at the head and asserts the floor.

## Pre-merge review (2026-10-01)

A static review of the whole branch before the merge to main: Opus and Sonnet agents over every subsystem, scripted
sweeps for the mechanical rules, no local build (CI is the build). What it changed:

- `healmeimbroken` and the bug-rescue cvars are gone, as the PR promised.
- Routing hands the inner pass a copy of the caller's `DamageSpecifier`, and stasis scales a copy. The respirator's
  own damage was halved in place on every stasis hit, so an Avali in vacuum stopped suffocating.
- The autodoc empties a garment's containers onto its tile before cutting it; keeps an antibiotic course waiting at
  the safe line instead of re-arming it every tick; spills only its listed containers when destroyed, so the built-in
  tools stay with the frame; drops a queued entry that stopped applying before its first step instead of faulting;
  lets ABORT clear FAULT; enforces its lock on the server; and gives a conscious body lifted in by somebody else a
  few seconds to walk away first, like a cryo pod. A downed, unconscious, dead or sleeping body goes straight in.
- A corpse shocked again inside the post-shock window is a fresh episode and gets its oxygenation and grace back.
- A body leaving Downed stays strapped to its bed, and one passing out is left to Critical instead of being stood up
  and dropped.
- Tend surgeries record progress like the Wolfmed conditions, so a surgeon can finish the closing step by hand.
- A melee execution needs the victim still in reach when the swing lands.
- A chassis keeps its reagent damage as damage: it has no liver for a toxin load.
- Housekeeping: personal paths out of the docs and generator scripts, one provenance header on every vendored
  StatusEffectNew file, medical HUD glasses that list Silicon list the IPC container too, two comment-only upstream
  edits reverted, four dead members and a duplicate test removed.

Left for after the merge: the merge and trim list for the tests, the duplicated armour coverage and execution blocks,
the Fluent pass over prototype names, the inert Onyx part-status and circulatory stream code, the paddles refusing a
destroyed heart, and the gun suicide that spends a round the peek did not measure.
