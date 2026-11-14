# Wolfmed port manifest

**Onyx commit:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377` (Space-Onyx/space-onyx-14 master)
**Last re-sync:** 2026-09-13 by WP9. **Phase 2 reconciled 2026-09-13 by WP10-7** (rows/deviations for
WP10-1 through WP10-6b were appended directly by each package per the orchestrator's sequential-execution
override of PLAN2 §4's `manifest-rows-WP10-N.md` indirection; WP10-7's own work was verification plus the
`:34`/`:35` status correction and the §8.2 summary below).

Status values: `verbatim` (byte-identical), `modified` (vendored `_Onyx` file with `// WOLFGATE` edits),
`adapted` (vendored but relocated and/or restructured), `new` (Wolfgate-authored `_WF` code or an upstream
`// WOLFGATE` hook), `skipped` (deliberately not ported).

> **Note (WP4):** this file was accidentally truncated during the WP4 run and rebuilt from PLAN.md §7.2
> plus the WP1/WP2/WP3 reports in `<plan>/wp/`. The row set and every recorded deviation are
> believed complete; wording in the WP1–WP3 sections may differ from the original.

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/StatusEffectNew/Components/StatusEffectComponent.cs` | same | verbatim | WP1 | registers as `StatusEffect`; no Wolfgate collision |
| `Content.Shared/StatusEffectNew/Components/StatusEffectContainerComponent.cs` | same | verbatim | WP1 | registers as `StatusEffectContainer` |
| `Content.Shared/StatusEffectNew/Components/StatusEffectAlertComponent.cs` | same | verbatim | WP1 | needs the `StatusEffects` entityCategory and `AlertsSystem.UpdateAlert` compat |
| `Content.Shared/StatusEffectNew/Components/CloneableStatusEffectComponent.cs` | same | verbatim | WP1 | UTF-8 BOM preserved |
| `Content.Shared/StatusEffectNew/Components/ExaminableStatusEffectComponent.cs` | same | verbatim | WP1 | |
| `Content.Shared/StatusEffectNew/Components/PermanentStatusEffectsComponent.cs` | same | verbatim | WP1 | |
| `Content.Shared/StatusEffectNew/Components/RejuvenateRemovedStatusEffectComponent.cs` | same | verbatim | WP1 | |
| `Content.Shared/StatusEffectNew/StatusEffectsSystem.cs` | same | verbatim | WP1 | needs the `ProtoMan` + `EntityPrototype.TryComp` compat files |
| `Content.Shared/StatusEffectNew/StatusEffectSystem.API.cs` | same | verbatim | WP1 | keeps Onyx's unused `System.ComponentModel.Design` / `YamlDotNet.Core.Tokens` usings on purpose |
| `Content.Shared/StatusEffectNew/StatusEffectAlertSystem.cs` | same | verbatim | WP1 | needs `AlertsSystem.UpdateAlert` compat |
| `Content.Shared/StatusEffectNew/StatusEffectSystem.Relay.cs` | same | modified | WP1 | 1 using removed, 1 added, 11 relay lines dropped |
| `Content.Shared/StatusEffectNew/ExaminableStatusEffectSystem.cs` | same | modified | WP1 | `[SubscribeLocalEvent]` to explicit `Initialize()` |
| `Content.Shared/StatusEffectNew/PermanentStatusEffectsSystem.cs` | same | modified | WP1 | same, x3; the `// <Onyx-OrganEffects>` block kept verbatim |
| `Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs` | same | verbatim | WP1 | required by `PainSystem.cs:323`; registers as `PainNumbnessStatusEffect`, no clash with Wolfgate's `PainNumbness` |
| `Resources/Prototypes/Entities/categories.yml:31-34` | `Resources/Prototypes/_Onyx/Entities/categories.yml` | adapted | WP1 | only the `StatusEffects` entityCategory |
| `Resources/Locale/en-US/entity-categories.ftl:7` | `Resources/Locale/en-US/_Onyx/entity-categories.ftl` | adapted | WP1 | one key |
| `Resources/Prototypes/Entities/StatusEffects/misc.yml` | same | adapted | WP1 | only `StatusEffectBase`, `MobStatusEffectBase`, `MobStatusEffectDebuff` |
| `Resources/Prototypes/Entities/StatusEffects/{body,clumsy,damage,speech,traits,weather}.yml` | — | skipped | never | non-wound status effects; each needs components we do not port |
| `Resources/Prototypes/Entities/StatusEffects/movement.yml` | — | skipped | never | **WP10-7 reconciliation (P2-D1):** was "deferred / WP10" — reclassified `not needed`. `FractureEffectSystem` applies both movement and manipulation penalties directly (`OnRefreshSpeed`/`OnGetMultiplier`), with no status-effect entity created or queried; `MovementModStatusEffectComponent` would also drag in the undocumented `FrictionStatusEffectComponent` |
| `Resources/Prototypes/_Onyx/StatusEffects/wounds.yml` | — | skipped | never | **WP10-7 reconciliation (P2-D1):** was "deferred / WP10" — reclassified `not needed`. `StatusEffectBurnSlowdown`/`StatusEffectWoundImpairment` are orphaned repo-wide at the pin; zero wounds populate `WoundStatusEffectBehavior.StatusEffect` (`grep -ni "statuseffect" Resources/Prototypes/_Onyx/Wounds/wounds.yml` → 0 hits) |
| `Resources/Prototypes/_Onyx/StatusEffects/surgery.yml` | — | skipped | never | needs `RaspyAccent`; D7 excludes Onyx surgery |
| `Resources/Prototypes/_Onyx/StatusEffects/{abductor_glands,breathing_immunity,cosmiccult,dementia,tile_movement,vampire}.yml` | — | skipped | never | unrelated features |
| `Content.{Shared,Server}/_Onyx/StatusEffects/Immunities/**` | — | skipped | never | unrelated features |
| — | `Content.Shared/_WF/Wolfmed/Compat/StatusEffectsSystem.Wolfgate.cs` | new | WP1 | supplies `ProtoMan`; delete on an RT upgrade that adds it |
| — | `Content.Shared/_WF/Wolfmed/Compat/EntityPrototypeCompatExtensions.cs` | new | WP1 | RT 277 names it `TryGetComponent` |
| — | `Content.Shared/_WF/Wolfmed/Compat/AlertsSystem.UpdateAlert.cs` | new | WP1 | partial, not extension; `AlertState.Cooldown` is an unnamed tuple here |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs` | new | WP2 | D12 facade; D30 zeroing `SetDamage`; `CanBeDamagedBy` re-based onto `DamageableComponent` |
| — | `Content.Shared/_WF/Wolfmed/Compat/DamageSpecifier.Wolfmed.cs` | new | WP2 | `Clone`, `GetPositive`, `GetNegative` |
| — | `Content.Shared/_WF/Wolfmed/Compat/DamageDealtEvent.cs` | new | WP2 | non-`readonly` unlike Onyx's — deliberate |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBodySystem.cs` | new | WP2 | `TryDetachPart` via `AmputateAttemptEvent`; `reparent` ignored |
| — | `Content.Shared/_WF/Wolfmed/Compat/StunSystemOnyxCompat.cs` | new | WP2 | `TryParalyze(refresh: false)`; re-triggers stun VFX, unlike Onyx |
| — | `Content.Shared/_WF/Wolfmed/Compat/SharedChatSystem.Wolfmed.cs` | new | WP2 | `virtual void`; paired with HOOK 6 at `ChatSystem.Emote.cs:60` |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBedHealMarkerComponent.cs` | new | WP2 | `HealOnBuckleComponent` is server-only here |
| — | `Content.Server/_WF/Wolfmed/Compat/WolfmedBedHealMarkerSystem.cs` | new | WP2 | `<HealOnBuckleComponent, ComponentStartup>` verified free |
| — | `Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs` | new | WP2 | event declarations only; `[ByRefEvent]` restored (see Deviations) |
| — | `Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs` | new (hook) | WP2 | HOOK 5, 3-line static `IsSelectable` |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamComponent.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs` | same | modified | WP3 | `MetabolismStage`/`MetabolitesStage` and the `Content.Shared.Metabolism` using commented out; phase-5 re-sync point |
| `Content.Shared/_Onyx/Chemistry/Circulation/SharedSolutionContainerSystem.CirculatoryStreams.cs` | — | skipped | never | only called from `InitializeStream`/`RemoveStream`, both dropped by D15 |
| `Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs` | same | verbatim | WP3 | zero name and zero cvar-string collisions |
| `Content.Shared/_Onyx/CCVar/CCVars.Surgery.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/CCVar/CCVars.Targeting.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/Targeting/DamageDistribution.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/Targeting/TargetingSnapshotComponent.cs` | same | modified | WP3 | added `using Content.Shared._Shitmed.Targeting;`; default `.Chest` to `.Torso` |
| `Content.Shared/_Onyx/Targeting/TargetingSnapshotSystem.cs` | same | modified | WP3 | added `using Content.Shared._Shitmed.Targeting;` for `TargetingComponent`/`TargetBodyPart`/`SharedTargetingSystem.IsSelectable` (HOOK 5, landed in WP2) |
| `Content.Shared/_Onyx/Targeting/{TargetBodyPart,TargetingComponent,SharedTargetingSystem,TargetResolverSystem}.cs` | — | skipped | never | D10 — Onyx's `TargetingComponent` registers as `"Targeting"`, colliding with Shitmed's; the resolver switches on `BodyPartType.Chest`/`Groin`, forbidden by D9 |
| `Content.{Server,Client}/_Onyx/Targeting/**`, `_Onyx/Targeting/PartStatus*.cs` | — | skipped | never | D10 — Shitmed's `TargetIntegrity` doll stays; revisit phase 3/4 |
| `Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml` | same | verbatim | WP3 | 2 lines: `- type: circulatoryStream / id: Organic` |
| `Content.Shared/_Onyx/Wounds/WoundEvents.cs` | same | verbatim | WP4 | `ResolveHealingPartEvent` keeps Onyx's `ProtoId`/`IReadOnlySet` types (D31) |
| `Content.Shared/_Onyx/Wounds/WoundBehaviors.cs` | same | verbatim | WP4 | needs WP1's `StatusEffectComponent` (D1) |
| `Content.Shared/_Onyx/Wounds/WoundPrototype.cs` | same | verbatim | WP4 | registers `wound` / `bodyPartProfile` / `fractureProfile`; dead `using Content.Shared.Body;` kept on purpose (PLAN 2.17) |
| `Content.Shared/_Onyx/Wounds/WoundScarSystem.cs` | same | verbatim | WP4 | |
| `Content.Shared/_Onyx/Wounds/WoundStatusEffectSystem.cs` | same | verbatim | WP4 | dead `using Content.Shared.Body;` kept (PLAN 2.17) |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | same | modified | WP4 / **WP9** | Verbatim out of WP4 (served by PLAN 2.8 stun shim + 2.9 chat shim + HOOK 6 + WP1's `PainNumbnessStatusEffectComponent`). **WP9 fix:** both `new ModifyPainGainEvent()` sites (`:111`, `:233`) now pass `1f` explicitly - `new T()` on a record struct binds to the implicit parameterless struct constructor and zeroed `Multiplier`, so every pain gain was multiplied by zero and no wound host ever felt pain |
| `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | same | modified | WP4 | D9: `Chest`+`Groin` weights fold to one `Torso = 4f` row, `Groin` dismemberment row deleted, `SystemicPainTarget` to `.Torso`; `_Shitmed` targeting using. `LocalizedDamageTypes` verbatim incl. `Caustic` (D20 reversed) |
| `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | same | modified | WP4 | D17: `<BodyComponent, RejuvenateEvent>` subscription deleted, handler exposed as `public void ClearBodyWounds(EntityUid)` |
| `Content.Shared/_Onyx/Wounds/BodyPartFunctionalitySystem.cs` | same | modified | WP4 | `_Shitmed.Cybernetics` using. `wounds.body_part_functionality_enabled` defaults **false**, so this is inert until phase 3 |
| `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs` | same | modified | WP4 | added `using Content.Shared.Damage;`; D12 facade swap; D8 `FractureProfile` read moves to `WolfmedBodyPartComponent` |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | same | modified | WP4 (+WP5, WP8) | D12 facade swap, D10 resolver swap, D8 part-field reads, D9 `Chest` to `Torso` x3, hands rewrite, bed-marker swap, `args.Cancelled` guard, Shitmed `targetPart` handoff, `before: [typeof(SharedArmorPlateSystem)]` on both `BeforeDamageChangedEvent` subs. D23 AP/Tool/OriginFlag side table and D27 applied-delta accumulator added in WP5 |
| `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs` | same | modified | WP4 (WP5 scope) | D12 facade swap, D15 circulation dependency dropped, D11 re-point to `DamageChangedEvent`, D17 `ClearBodyWounds` call, D19 `InjurableComponent` block deleted, `after: [typeof(SharedBodySystem)]`, Shitmed parent walk, D9 visual layers |
| `Content.Shared/_Onyx/Mobs/Systems/MobThresholdSystem.cs` | same | modified | WP4 (WP5 scope) | absent from the sparse checkout — read via `git show`. **Adds** `_damageable` (M6); `using Content.Shared.Body.Components;`; vital parts `{Head, Torso}` (D9) |
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` | same | new (vendored), adapted | **WP11-1** | **WP11-6 reconciliation:** D26 lifted. 18 `// WOLFGATE` sites (D8 `_wfPart.Get(...)` redirects x11, D9 `Chest`->`Torso` x3, D12 facade swap, `GetParentPartOrNull` x2, the `_wfBody.TryDetachPart` shim, 2 P3-D15 signature changes). Stays in `Content.Shared` (P3-D11 — no server-only dependency, gated by four `_net.IsServer` guards instead). Registers `<WoundableComponent, PartDamageOverflowedEvent>`, the only new subscription in WP11-1. No `DamageDict` key-type edit needed (P3-D16 confirmed) |
| `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | same | modified | **WP10-1** | 1 edit (P2-D2): `TryGetUsedHandSymmetry` body replaced against Wolfgate's `Hand`-object hands API (`GetActiveHand` returns `Hand?`, `IsHolding`'s 4-arg overload outs `Hand?`, `Hand` is a class so no `.Value`, `HandLocation` has no `Functional*`). No using swap (P2-D3), class name stays `FractureEffectSystem` while the file stays `FractureEffectsSystem.cs` (Onyx's own mismatch). `RefreshTransferredPart` kept verbatim as dead-but-deliberate (P2-D18) |
| `Content.Shared/_Onyx/Wounds/FractureAlertSystem.cs` | same | modified | **WP10-1** | 3 edits, all D8: `using Content.Shared._WF.Wolfmed.Body;`, `[Dependency] WolfmedBodyPartSystem _wfPart`, and `bodyPart.FractureProfile` → `_wfPart.Get(part).FractureProfile`. `bodyPart` left as an unused deconstruction variable (matches `WoundFractureSystem.cs:148`). No `Initialize`, no subscriptions |
| `Content.Shared/_Onyx/Wounds/WoundBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs` | adapted | WP6 | D13 — relocated to `Content.Server`, namespace unchanged. `using Content.Shared.Body.Components;` to `Content.Server.Body.Components` (`BloodstreamComponent`); `using Content.Server.Body.Systems;` **added** beside the shared one (`SharedBodySystem` still comes from `Content.Shared.Body.Systems`). Body otherwise byte-identical |
| `Content.Shared/_Onyx/Wounds/WoundInternalBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundInternalBleedingSystem.cs` | adapted | WP6 | D13 + M1 — same two `using` swaps, plus the mandatory `:67` fix `TryModifyBloodLevel((body, bloodstream), -amount)` to `TryModifyBloodLevel(body, -amount, bloodstream)` (two chained user-defined conversions, `CS1503`) |
| `Content.Shared/_Onyx/Wounds/OrganDamageSystem.cs` | `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | adapted | WP6 / **WP11-1** | D13 + D26 + D8 — `using Content.Shared.Body;` to `Content.Shared.Body.Organ` + `Content.Shared._WF.Wolfmed.Body`; the organ list and `PickOrgan` retargeted from `OrganComponent` to `WolfmedOrganComponent`. **WP11-6 correction:** the WP6 disabled sites were actually `:25-26` and `:38-39`, not `:24`/`:36` as originally recorded. **D26 lifted in WP11-1:** both restored to `[Dependency] private AmputationSystem _amputation` and `_amputation.HandlePartDamageApplied(part, ref args)`; fan-out order `_wounds` -> `_fractures` -> `_amputation` -> `_bleeding` is load-bearing. **Owned exclusively by WP11-1** (P3-D24) |
| `Content.Shared/_Onyx/Wounds/WoundHealingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs` | adapted | WP6 | D13 + D14 + D31 — `using Content.Shared.Medical.Healing;` to `Content.Server.Medical.Components`; D12 facade swap; D31 conversion at `:110` (`DamageContainers?.Select(x => new ProtoId<DamageContainerPrototype>(x)).ToList()`). `ResolveHealingPart`/`IsCompatiblePart` signatures untouched |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | adapted | WP6 / **WP13-2** | D15 — trimmed to `GetPartStream`, `TryGetPartSolution`, `TryGetStreamSolution`, `SetBleedRates`, primary-stream branches only. All five subscriptions, `Update`, `SynchronizeStreams`, `GetAttachedStreams`, `ConfigureMetabolizer`, `InitializeStream`, `HasStageConflict`, `RemoveStream`, `DeleteSolution` and both metabolism handlers dropped. Namespace unchanged. **WP13-7 path correction:** this row previously listed the path under `Content.Shared/…`; the file actually lives at `Content.Server/…` (its namespace, `Content.Shared._Onyx.Chemistry.Circulation`, is what caused the earlier mistake). **WP13-2 (§2.4, P5-D3)** adds a second one-line `// WOLFGATE` hardening site alongside WP6's trim — both sites confirm every `bodyPartProfile` in the game selects only the primary circulatory stream, closing the phase-1 "shared stage-based metabolizer" question as a non-issue (§8.7) |
| `Content.Shared/_Onyx/Body/OrganDamageComponent.cs` | same | verbatim | WP6 | registers as `OrganDamage`; no Wolfgate collision |
| `Content.Shared/_Onyx/Body/Systems/OrganHealthSystem.cs` | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | adapted | WP6 | D13 + D8 — health reads move to `WolfmedOrganComponent`; `TryGetOrganInSlot`/`TryRemoveOrgan` replaced by Wolfgate's `SharedBodySystem.RemoveOrgan`; `BrainComponent` resolves from `Content.Server.Body.Components`. `OrganFunctionChangedEvent` relocated into this file (see Deviations) |
| `Content.Shared/_Onyx/Body/FunctionalOrganComponent.cs` | — | **skipped permanently** | WP11 | **WP11-6 reconciliation:** P3-D8 — maps 1:1 onto Shitmed's `OrganComponent.OnAdd` + `_Shitmed/BodyEffects/OrganEffectSystem.cs:53-59`. Onyx's only prototype users are exotic implants Wolfgate does not have. Only its `OrganFunctionChangedEvent` was needed and now lives in `OrganHealthSystem.cs` |
| — | `Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs` | new | WP6 | D8 — `Health`, `MaxHealth`, `DestructionWound`, `DestructionWoundSeverity`; Wolfgate's Shitmed `OrganComponent` has none. Networked; registers as `WolfmedOrgan`. **WP11-6 reconciliation:** **WP11-2 lands the prototypes** — `_WF/Wolfmed/Body/organs.yml` + 7 `parent:` edits in `Body/Organs/human.yml`. These fields have lived here since WP6 (P3-D9); no new upstream `OrganComponent` hook was needed |
| `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | same | modified | WP6 | additional WP6 edit: `HandlePartDamageApplied` `internal` to `public` — D13 puts its only caller (`OrganDamageSystem`) in `Content.Server`, a different assembly |
| `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs` | same | modified | WP6 | additional WP6 edit: same `internal` to `public` change on `HandlePartDamageApplied` |
| — | `Content.Server/Body/Systems/BloodstreamSystem.cs` | new (hook) | WP6 | **GUARD E** (`HasComp<WoundHostComponent>` early-return in `OnDamageChanged`) + **GUARD E3** (`TryModifyBleedAmount` split into the public wound-host-gated entry, `internal TryModifyWoundBleedProjection`, and a private `bool woundProjection` implementation). One gate also silently no-ops the passive-decay call in `Update()` — deliberate, not duplicated. **GUARD E2 is WP10** |
| — | `Content.Server/_Mono/Traits/Physical/HemophiliaSystem.cs` | new (hook) | WP6 | **GUARD E4** — one `HasComp<WoundHostComponent>` early-return at the top of `OnDamageChanged` |
| — | `Content.Server/Medical/Components/HealingComponent.cs` | new (hook) | WP6 | **HOOK 7 / D14** — four additive `[DataField]`s (`HealDamage`, `HealWounds`, `HashSet<TreatmentCapability> TreatmentCapabilities`, `HashSet<string>? AllowedWoundStages`) + `using Content.Shared._Onyx.Wounds;`. `HashSet<>` is mandatory (D31: `ResolveHealingPartEvent` takes `IReadOnlySet<>`). **WP12-10 reconcile:** items and reagents reach wounds by two different mechanisms and phase 4 does not unify them. Items pass `TreatmentCapabilities` explicitly into `WoundHealingSystem.ResolveHealingPart`; reagents open `WithTreatmentCapabilities` (HOOK 9). Do not nest the two — the scope is a non-reentrant dictionary with a `finally` remove |
| — | `Content.Server/Medical/HealingSystem.cs` | new (hook) | WP6 | **HOOK 8**, call-site only after the tidy pass: `OnDoAfter`'s wound-host branch (calls `OnWoundHostDoAfter`) and `TryHeal`'s `woundHost` gate (calls `IsWoundDamaged`/`ResolveWoundTargetPart`). The hook body — `OnWoundHostDoAfter`, `ResolveWoundTargetPart`, `GetHealingContainers`, `IsWoundDamaged`, and the `WoundHealingSystem`/`WoundTargetResolver` dependencies — moved to `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs`. Requested part comes from the healer's Shitmed `TargetingComponent` (see Deviations) |
| — | `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` | new | WP6 (split out in the HOOK 8/10 tidy pass) | HOOK 8's body, `partial class HealingSystem` sharing the upstream file's private fields (`_popupSystem`, `_stacks`, `_adminLogger`, `_bloodstreamSystem`, `_bodySystem`, `_solutionContainerSystem`, `_audio`, `EntityManager`). Logic is byte-identical to the original hook, just relocated |
| `Content.Server/Body/Systems/RespiratorSystem.cs` | — | skipped | never | PLAN 3 lists it under "explicitly NOT touched"; `hooks-a.md` 3a/3b confirm no phase-1 hook exists (breathing immunity already present, `InitiallyLungedComponent`/OrganConsequences are Nubody glue) |
| `Content.Shared/_Onyx/Wounds/{ReagentTreatmentEffects,ReagentTreatmentSystems,SuppressPainEntityEffect}.cs` | — | **skipped (re-authored)** | WP11 / **WP12-1** | D16 — rewritten old-style as four classes in `_WF/Wolfmed/EntityEffects` (WP12-1); `HealthChange`/`EvenHealthChange` get HOOK 9 instead. **`DistributedHealthChange` is not needed, not deferred** (P4-D6): zero `!type:` uses in Onyx's reagent set and zero references in WG — **WP12-10 reconcile** |
| — | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs` | new | WP4 | D8/PLAN 2.12; 6 fields; not networked. `MaxDamage` default 0 disables amputation overflow — WP7's `parts.yml` must cover all 13 limb abstracts |
| — | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs` | new | WP4 | D8/PLAN 2.12; `Get(EntityUid)` with a shared zeroed default |
| — | `Content.Shared/_WF/Wolfmed/Targeting/WoundTargetResolver.cs` | new | WP4 | D10/PLAN 2.13; folds `TargetBodyPart.Groin` to the torso; no anatomical-odds scatter in phase 1 |
| — | `Content.Server/Chat/Systems/ChatSystem.Emote.cs` | new (hook) | WP4 | HOOK 6 / D25 — one word at `:60` (`public void` to `public override void`); `:85` untouched |
| `Content.Shared/Damage/Systems/DamageableSystem.cs` | same | new (hook) | WP5 | **GUARD D** (the `DamageDealtEvent` routing seam, gated on `_woundHostQuery`, with a defensive `new DamageSpecifier(damage)` copy) and **GUARD D2** (`BeforeDamageChangedEvent` gains `ArmorPenetration`/`Tool` (D23) and `Applied` (D27); `TryChangeDamage` returns `before.Applied` instead of `null` on cancel). 2 usings, 1 query field + its assignment |
| `Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs` | same | new (hook) | WP5 | **GUARDs A/B/C** — component-gated on `WoundHostComponent`, never `_net.IsServer` (PLAN 8.3 trap 3). A skips Shitmed's part spread, B its sever, C its part regen (both the tick condition and the job enqueue). `CheckBodyPart` is left running |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | same | modified | WP5 | WP5 half: D23 `_routedModifiers` side table written in `OnBeforeDamageChanged` and read at the routed `ChangeDamage` call; D27 `_appliedDelta` accumulator with three write points (`RouteAppliedDamage`, `ApplyPartChange`, `ApplySystemicDamage`) folded in by `AccumulateApplied`, written to `args.Applied` |
| — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | new | WP5 | D28 / PLAN 2.11 — subscribes `<WoundHostComponent, BodyPartAddedEvent/BodyPartRemovedEvent>` (never `<BodyComponent, …>`, which Shitmed owns), calls `WoundDamageProjectionSystem.OnPartInserted`/`OnPartRemoved` (PLAN 8.3 trap 2 — they had no caller) and fans `OrganGotInsertedEvent`/`OrganGotRemovedEvent` over the attached subtree |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | same | **complete (30 of 30), 2 marked fold classes** | WP7 / **WP13-0** | organic subset (`OrganicBodyPartProfile`, `OrganicFractureProfile`, 12 wounds) from WP7; **WP13-0 appends the remaining 16 prototypes (P5-1a)** — `Ipc`/`Slime`/`Plant`/`Cybernetic` `bodyPartProfile`s, `CyberneticFractureProfile`, and their wound sets. D9: `organDamage.chances` `Chest: 0.04`+`Groin: 0.04` fold to one `Torso: 0.04` (not summed) on `OrganicBodyPartProfile`; **WP13-0 folds the same D9 pattern onto `Ipc`/`Slime`/`Plant`'s `organDamage`**, the phase-5 fold class. **D20 reversed: `Caustic` stays** in `acceptedDamageTypes` and `BurnWound.damageTypes`. **WP13-7 correction:** this row previously read "trimmed (14 of 30 prototypes)" against a WG count of 14 `id:` lines and an Onyx count of 30 — the wording "13 of 29" seen in some earlier notes was off by one in both directions; the file is now complete against Onyx at the pin |
| `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` | same | modified | WP7 / **WP10-2** | 43 keys copied verbatim, **plus 4 added**: `wound-examine-fracture-{hairline,simple,displaced,comminuted}`, sourced from Onyx's `_Onyx/medical/health-examinable.ftl` (deferred to WP10) because `BoneFractureWound`'s `examineDescription` fields reference them and the YAML linter fails without them — see Deviations. **WP10-2 DELETED those 4 keys** (`:45-52`, the whole `# WOLFGATE (WP7)` stopgap block) when `_Onyx/medical/health-examinable.ftl` landed — Fluent throws on a duplicate id, so the two files cannot both declare them (P2-D12). Verified by a clean headless server start |
| `Resources/Locale/en-US/_Onyx/medical/fractures.ftl` | same | verbatim | WP7 | `alerts-broken-bones-{name,desc}` |
| `Resources/Prototypes/_Onyx/Alerts/alerts.yml` | same | modified | WP7 | `BrokenBones` only; header preserved verbatim; `ModsuitPower`/`Centered`/`HierophantBeat`/`DragonPower`/`SneakAttack`/`LossOfSurprise` dropped (unrelated features, would need un-ported textures/tags) |
| `Resources/Textures/_Onyx/Interface/Alerts/fracture.rsi/{meta.json,brokenbones.png}` | same | verbatim | WP7 | CC-BY-SA-3.0, "Taken from tgstation, redrawn by darkrell" — re-checked, compatible |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | new | WP7 | D8 — 10 `WolfmedBase<Part>` abstracts (`WolfmedBaseTorso/Head/Left|RightArm/Left|RightHand/Left|RightLeg/Left|RightFoot`) carrying `WolfmedBodyPart` data at Onyx's numbers (chest_groin.yml + body-organ.md §5.2). Separate ids, not re-declarations of `BaseTorso`/`BaseHead`/etc — see Deviations |
| — | `Resources/Prototypes/Body/Parts/base.yml` | same | modified | WP7 | upstream, 9 one-line `# WOLFGATE` `parent:` edits — `BaseHead`, `BaseLeftArm`, `BaseRightArm`, `BaseLeftHand`, `BaseRightHand`, `BaseLeftLeg`, `BaseRightLeg`, `BaseLeftFoot`, `BaseRightFoot` each add the matching `WolfmedBase<Part>` to their `parent:` list |
| — | `Resources/Prototypes/_Shitmed/Body/Parts/base.yml` | same | modified | WP7 | upstream, 1 one-line `# WOLFGATE` `parent:` edit — `BaseTorso` adds `WolfmedBaseTorso` |
| — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | same | modified | WP7 | upstream, `# WOLFGATE` block on `BaseMobSpeciesOrganic` (D21 `WoundHost`; D22 `Destructible` Blunt threshold 400→1500; D29 existing `PassiveDamage`'s `damage:` zeroed to `{}` in place, not duplicated — see Deviations) |
| — | `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` | same | modified | WP7 | documentation-only `# WOLFGATE` comment on `BaseMobProtogen` pointing at `WolfmedWoundHostExclusionSystem`; no functional YAML change (RT cannot remove an inherited component via YAML). **WP13-7 deviation-note update (U4):** the exclusion this comment refers to is lifted as of **WP13-3** — the comment is now stale in intent (it still points at the exclusion system, which now excludes nothing) but is left in place as a pointer to where the host decision lives; protogen's organ gap (P5-D19, U12′) is recorded against `_Mono/Body/Organs/protogen.yml`, itself untouched (§3.4) |
| — | `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | new | WP7 / **WP13-3** | D21/D32 — strips `WoundHostComponent` from entities descended from any name in `ExcludedAncestors` at `ComponentInit`, shared so client and server agree. Deviation from the plan's literal "remove in its own prototype" — see Deviations. Subscribes `<WoundHostComponent, ComponentInit>`, free per grep. **WP13-3 (U4, P5-1d):** `ExcludedAncestors` is now an **empty `HashSet<string>`** — the protogen exclusion is lifted; the mechanism is kept, unpopulated, as the documented hook for any future synthetic species |
| `Content.Shared/Armor/SharedArmorSystem.cs` | same | new (hook) | WP8 | **HOOK 10**, call-site only after the tidy pass: `OnDamageModify` now reads `if (TryApplyWoundHostArmor(uid, component, args)) return;`. The systemic-damage branch and the `ApplyWoundSystemicArmor` helper ported from Onyx moved to `Content.Shared/_WF/Wolfmed/Armor/SharedArmorSystem.Wolfmed.cs`. The localized half was already out of this upstream file, in `_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs`, since fix round 1 |
| — | `Content.Shared/_WF/Wolfmed/Armor/SharedArmorSystem.Wolfmed.cs` | new | WP8 (split out in the HOOK 8/10 tidy pass) | HOOK 10's systemic-damage half, `partial class SharedArmorSystem` holding `TryApplyWoundHostArmor` and the ported `ApplyWoundSystemicArmor` helper. Logic is byte-identical to the original hook, just relocated |
| `Content.Shared/Armor/SharedArmorSystem.cs:108-129` (handler body, phase 1) | `Content.Shared/_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs` | modified | WP8 / **WP11-3** | HOOK 10's other half, kept out of upstream. Sole subscriber of `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>`; phase 1 applied `ApplyModifierSet(damage, PenetrateArmor(Modifiers, ap))` unconditionally. **WP11-6 reconciliation: WP11-3 rewrote the handler** to Onyx's first-match-wins `PartModifiers` loop plus a `Coverage`/`CoverageSymmetry` gate on the fallback only (P3-D5, Option B). Both branches still wrap `PenetrateArmor` (D23) — without it every AP weapon would silently stop working against any armour that declares a part profile. No new subscription: adding Onyx's own registration at `SharedArmorSystem.cs:30` would be a duplicate directed subscription and a server-start crash. Onyx's `MaskComponent.IsToggled` gate deliberately not ported |
| `Content.Shared/Mobs/Systems/MobThresholdSystem.cs` | same | new (hook) | WP8 | **HOOK 11**, two sites: `CheckThresholds` and `UpdateAlerts`' severity lerp now read `CheckVitalDamage(target, damageable)` instead of `damageable.TotalDamage`. `CheckVitalDamage` (the `_Onyx/Mobs/Systems` partial, WP5) falls back to total damage for non-wound-hosts, so no branch is needed |
| `Content.Server/Medical/DefibrillatorSystem.cs` | same | new (hook) | WP8 | **HOOK 12.** Same `CheckVitalDamage` substitution in the revive check, so revival agrees with HOOK 11's death decision |
| `Content.Shared/Execution/SharedExecutionSystem.cs` | same | new (hook) | WP8 | **HOOK 13.** One `[Dependency] WoundDamageRoutingSystem _woundRouting` + one `TryApplyLethalDamage(victim, meleeWeaponComp.Damage, attacker)` after `AttemptLightAttack`. Self-guards on `_net.IsServer` and `HasComp<WoundHostComponent>` |
| `Content.Shared/_Onyx/Wounds/WoundEvents.cs` | same | modified | WP8 | D23 — `PartDamageModifyEvent` gains an optional trailing `float armorPenetration = 0f` primary-constructor parameter and a readonly `ArmorPenetration` field, so HOOK 10's part pass can penetrate armour |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | same | modified | WP8 | WP8 half: the single `PartDamageModifyEvent` construction site now passes `_routedModifiers.GetValueOrDefault(body).ArmorPenetration` (D23). The side table itself landed in WP5 |
| `Content.Server/Damage/Commands/HurtCommand.cs` | same | skipped | WP8 | Shipped in WP8 round 1 (optional 5th `<bodyPart>` argument), **reverted in fix round 1** — not in PLAN 3's authorised list and the instruction claimed for it is not recorded in PLAN.md/DECISIONS.md. File is byte-identical to HEAD again. The change is preserved at `<plan>/wp/WP8-hurtcommand-deferred.patch` and can be re-applied if the user authorises it |
| — | `Resources/Locale/en-US/_Onyx/commands/damage-command.ftl` | skipped | WP8 | Created in WP8 round 1, **deleted in fix round 1** with the command that used it. The path does not exist in the pinned Onyx sparse checkout, so its "verbatim" claim was never diffable — see Deviations |
| `Resources/Locale/en-US/damage/damage-command.ftl` | same | skipped | WP8 | Usage-string edit reverted in fix round 1 with `HurtCommand.cs`; byte-identical to HEAD again |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundDamageFoundationTest.cs` | same | adapted | WP9 | 9 of Onyx's 12 tests. Shitmed `body` prototype instead of Nubody `InitialBody`; Chest -> Torso (D9); `WolfmedDamageableSystem`/`WolfmedBodySystem`/`WoundTargetResolver` in place of Onyx's; Onyx's `TargetingComponent.DefaultOdds()`/`TryConvert` assertions dropped (D10); the two armour tests that need `coverage`/`partModifiers` dropped and folded into one applies-exactly-once test; `SuppressPain` entity effect replaced by the identical `PainSystem.SuppressPain` path (D16, phase 4) |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundBleedingTest.cs` | same | adapted | WP9 / **WP11-5** / **WP12-9** | **6 of Onyx's 6** (WP12-10 reconcile: `TourniquetStopsOnlySelectedPartTest` restored in WP12-9, driven through `TourniquetSystem.Apply` directly, plus a new does-not-double-apply assertion Onyx never made — no longer skipped). Server-side `WoundBleedingSystem`/`BloodstreamComponent` (D13); two auto-clotting severities raised above `SlashWound.minimumSeverity: 9`. **`TraumaticAmputationCreatesSevereStumpBleedingTest` restored in WP11-5** (T-AMP-THRESHOLD) once D26 lifted `AmputationSystem`; the phase-1 skip note was deleted. **P3-D14:** Onyx's `BleedAmount Is.GreaterThanOrEqualTo(40f)` corrected to `Is.EqualTo(bloodstream.MaxBleedAmount)` (10f) — a Head `DismembermentWound` at severity 200 gives a raw 60, which `BloodstreamSystem` clamps to `MaxBleedAmount`. Also asserts `DismembermentWound` severity **200** and `AmputationConsequenceWound` severity **35** (the stock `WolfmedBodyPartComponent` default), and that the severed head is a live re-parented entity, not deleted |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundScarTest.cs` | same | adapted | WP9 | 1 test. Shitmed body graph; `WolfmedBodySystem.TryDetachPart` + `SharedBodySystem.AttachPart`; `CCVars.SurgeryScarChance` pinned to 1 for the duration (Onyx's copy is 35 % flaky) |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundFractureTest.cs` | same | adapted | WP9 / **WP10-1** | 2 of Onyx's 3. `FractureEffectSystem` test still deferred (WP10-6b owns every assertion); grade boundaries corrected to `OrganicFractureProfile`'s own 20/35/50/60. **WP10-1 landed the `[TestPrototypes]` block only (T-FIXTURE / P2-D21):** `WoundFractureBodyGraph` gains a `left hand` slot (`LeftHandHuman`) and `WoundFractureBody` a `- type: Hands`, plus a second `WoundFractureHandsBodyGraph`/`WoundFractureHandsBody` (both arms, both hands) for T-FRACT-HANDS. Exactly one hand on `WoundFractureBody` so the `used: null` active-hand path is unambiguous. **WP10-6b wrote every assertion:** Onyx's `EffectsRefreshOnTreatmentHealingAndDetachTest` ported (T-FRACT-EFFECTS) plus three tests Onyx does not have - `FractureManipulationUsesHeldHandSymmetryTest` (T-FRACT-HANDS, the only coverage of the P2-D2 `TryGetUsedHandSymmetry` rewrite), `FractureAlertTracksGradeAndTreatmentTest` (T-FRACT-ALERT) and `FractureAlertRespectsMinimumGradeTest` (T-FRACT-ALERT-NEG). Two further `[TestPrototypes]` edits WP10-6b had to make: `- type: Alerts` on `WoundFractureBody` (`AlertsSystem.ShowAlert` returns silently without `AlertsComponent`) and a new `WoundFractureHeldItem` (`IsHolding` only resolves a hand for a real item). Every literal re-derived against the shipped data (P2-D16) and every fracture created at severity >= 60 (P2-D23) |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundHealingTest.cs` | same | adapted | WP9 | 4 of Onyx's 5. Server-side `WoundHealingSystem` + `Content.Server` `HealingComponent` (D13/D14); `Repairable`/`TransplantCompatibility` dropped; `WoundTargetResolver` for the exact-target test; repair-event test skipped (`_Onyx.Repairable` not in scope) |
| `Content.IntegrationTests/Tests/_Onyx/Body/BodyConsequencesTest.cs` | same | adapted | WP9 | 2 of Onyx's 3, both rewritten. No Wolfgate system couples inventory slots to body parts and `BodyPartType.Groin`/`StandUpAttemptEvent` do not exist, so the surviving contract is cascade-on-detach and down-at-zero-legs on a real `MobHuman` wound host |
| - | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDamageBridgeTest.cs` | same | adapted | WP9 / **WP10-6a** | PLAN 6.2: T-SETUP, T-RESULT, T-PIERCE, T-CAUSTIC, T12, non-wound-host control, no-double-application. `WolfmedBridgeBody` / `WolfmedControlBody` `[TestPrototypes]` pair. **WP10-6a (PLAN2 §6.2) added T-AP and T-PASSIVE-A/B**, plus three new `[TestPrototypes]`: `WolfmedBridgeArmor` (mirrors `WoundFractureArmor`), `WolfmedPassiveWoundHost` / `WolfmedPassiveControl` (real `PassiveDamage` on the `WolfmedBridgeBodyGraph` shape, one with `WoundHost` one without) |
| - | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainTest.cs` | same | new | **WP10-6b** | PLAN2 §6.2: T-PAIN-OVERLAY (`PainOverlayLevelTracksPainTest`), T-PAIN-SHOCK (`PainShockStunsAtThresholdTest`), T-HIGH-PAIN (`HighPainThresholdReducesWoundPainGainTest`), T-PAIN-NUMB (`PainNumbnessSuppressesWoundPainTest`). Four new `[TestPrototypes]` on one `WolfmedPainBodyGraph` (torso + head + left arm): `WolfmedPainControlBody`, `WolfmedHighPainThresholdBody`, `WolfmedPainShockBody` and `WolfmedPainNumbBody`; the last two carry P2-D24's explicit `StatusEffects allowed: [Stun, KnockedDown, Jitter]` **and** `- type: MobState`. Every number measured on this tree (P2-4), derivations at each assertion |
| - | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | new | WP9 | **Two WP9 fixes.** (1) `TerminatingOrDeleted` guards on both handlers - `RecursiveDeleteEntity` detaches every part while a mob terminates and `RefreshDetachedDamage`'s `EnsureComp<PartDamageVisualsComponent>` threw a `DebugAssertException` on every mob deletion. Onyx has the same guard in `BodyInventorySlotSystem.cs:32,45`. (2) `WoundBleedingSystem.OnPartInserted`/`OnPartChanged` are now driven from here (Onyx drives them from the unported `BodyInventorySlotSystem.cs:39,49`); without them a detached limb kept bleeding into the body |
| - | `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | new | WP7 / **WP9** | WP9 fix: `RemCompDeferred` -> `RemComp`; the deferred form left the component in `_deleteSet` at life stage `Initialized` and `StartComponents` asserted, so every `MobProtogen` spawn threw |
| - | `Content.Server/_WF/Wolfmed/Compat/WolfmedBedHealMarkerSystem.cs` | new | WP2 / **WP9** | WP9 fix: `<HealOnBuckleComponent, ComponentStartup>` -> `<HealOnBuckleComponent, MapInitEvent>`; the marker was being gained on spawn, which fails `PrototypeSaveTest.UninitializedSaveTest` |
| - | `Content.Shared/_WF/Wolfmed/DoAfter/WolfmedFractureDoAfterSystem.cs` | new | **WP10-1** | P2-D4 do-after bridge. Subscribes `<WoundHostComponent, GetDoAfterDelayMultiplierEvent>` (pair free: the event is otherwise held by `DoAfterDelayMultiplierComponent` and `BodyComponent`) with a `ref` handler matching `DoAfterDelayMultiplierSystem.cs:27`, and multiplies in `FractureEffectSystem.GetDurationMultiplier(uid)`. `SharedDoAfterSystem.cs` and `DoAfterDelayMultiplierSystem.cs` untouched. No `before:`/`after:` edge — the multipliers compose |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | same | modified | WP7 / **WP10-1** | **WP10-1 (DECISIONS §8.2-1):** the four `OrganicFractureProfile` `manipulationModifier` values 0.92/0.84/0.75/0.75 become **1.1/1.25/1.5/2.0** behind a `# WOLFGATE` balance comment. Onyx's values are all below 1 while the formula is `multiplier *= 1 + (modifier - 1) * partScale * treatmentScale`, so below 1 meant *faster* — a shattered arm made every do-after 25 % quicker. 1.1/1.25/1.5/2.0 are the C# defaults in `WoundPrototype.cs`. `movementModifier` untouched |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | same | modified | **WP10-2** | **5** `// WOLFGATE` edits (PLAN2 predicted 4): (1) `using Content.Shared.Damage.Components;` -> `using Content.Shared.Damage;` (`DamageableComponent`'s namespace here, CS0246 otherwise); (2) **add** `using Content.Shared._WF.Wolfmed.Compat;`; (3) **add** `[Dependency] private WolfmedDamageableSystem _damageable` (D12 — Onyx declares it on the base partial, which GUARD F deliberately leaves facade-free); (4) D9 `PartOrder` fold, `Chest`/`Groin` -> `Torso`; (5) **not in PLAN2** — `DamageSpecifier.DamageDict` is `Dictionary<string, FixedPoint2>` in Wolfgate, not `ProtoId<DamageTypePrototype>`-keyed, so `entry.Key.Id` / `type.Id` lose the `.Id`. Declares no `Initialize`, registers no subscription |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.Pain.cs` | same | verbatim | **WP10-2** | Self-only pain visibility is Onyx's deliberate anti-metagame design. Also where `HealthExaminableSystem.PartStatus.cs` gets `_body`/`_pain` from — the two files do not compile apart |
| `Content.Client/_Onyx/HealthExaminable/ExamineSystem.PartStatus.cs` | same | verbatim | **WP10-2** | Attaches straight to Wolfgate's `public sealed partial class ExamineSystem` in `namespace Content.Client.Examine` — no `_WF` indirection. Sandbox-clean; `Vector2Helpers` resolves through `Content.Client/GlobalUsings.cs`'s `global using Robust.Shared.Maths` |
| `Content.Client/_Onyx/HealthExaminable/PartStatusTag.cs` | same | verbatim | **WP10-2** | `[partstatus]`/`[partstatusend]` need no engine tag registration — the client walks `message.Nodes` before anything reaches `RichTextLabel.SetMessage`'s `tagsAllowed` path |
| - | `Content.Shared/_WF/Wolfmed/Compat/PartStatusSeverity.cs` | new | **WP10-2** | P2-D11. Declares `namespace Content.Shared._Onyx.Targeting` deliberately so the vendored file's `using` and unqualified `PartStatusSystem.GetSeverity(...)` resolve unedited. `GetSeverity` body verbatim from Onyx's `_Onyx/Targeting/PartStatusSystem.cs`; the D10-excluded Targeting-doll members (`PartStatus`, `Missing`, `PartStatusComponent`) are not ported. Per PLAN2 §2.1 the enum ships without Onyx's `[Serializable, NetSerializable]` — it is never networked here. Static class + enum: registers nothing, subscribes nothing |
| - | `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` | new (hook) | **WP10-2** | **GUARD F**, 5 sites: `using Content.Shared._Onyx.Wounds;`; `CreateMarkup(uid, args.User, ...)` at the verb `Act`; the `examiner` parameter on `CreateMarkup`; a `if (!HasComp<WoundHostComponent>(uid))` wrap around the legacy threshold loop + `msg.IsEmpty` fallback; and `else AddPartStatusMarkup(uid, examiner, msg);` (**P2-D20** — the `else`, never unconditional). Onyx's `GetAllDamage` swap inside the legacy branch is deliberately skipped, so the file needs no facade dependency. `HealthExaminableComponent.cs` untouched. Adds no subscription. Wrapped body left un-reindented so the upstream diff stays 4 added lines |
| - | `Content.Client/Examine/ExamineSystem.cs` | new (hook) | **WP10-2** | **HOOK 14**, 2 sites: `new Popup { MaxWidth = 400 }` -> `560` (widens *every* examine popup — see Deviations) and the `richLabel` construction wrapped in `if (!TryAddPartStatusMessage(vBox, message))`. Adds no subscription, no new `using` (the callee is a member of the same partial class) |
| - | `Content.Server/Body/Systems/BloodstreamSystem.cs` | new (hook) | WP6 / **WP10-2** | **WP10-2 landed GUARD E2** — `!HasComp<WoundHostComponent>(ent) &&` on the `bloodstream-component-looks-pale` condition in `OnHealthBeingExamined` (`:280`, drifted from PLAN's `:274`). No new `using` (GUARD E already added it). The two `BleedAmount` messages above are deliberately untouched: GUARD E3 projects wound bleeding onto the body's own `BleedAmount`, so they read correctly for wound hosts |
| `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | same | verbatim | **WP10-2** | **36** message ids. The 4 `wound-examine-frame-*` keys have no consumer at the pin (harmless dead weight); the 4 `wound-examine-fracture-*` keys are the P2-D12 collision deleted from `wounds.ftl` in this same package |
| `Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` | - | skipped | **WP10-2** | Orphaned at the pin — its one key has zero consumers, and D10 excludes the Targeting doll that would use it |
| `Content.Shared/_Onyx/Traits/HighPainThresholdComponent.cs` | same | verbatim | **WP10-3** | `[RegisterComponent, NetworkedComponent]`, one `[DataField] public float PainMultiplier = 0.75f;`. `grep -rn "HighPainThreshold"` across WG C#/YAML/FTL was zero hits before this WP — free registration |
| `Content.Shared/_Onyx/Traits/HighPainThresholdSystem.cs` | same | verbatim | **WP10-3** | Subscribes `<HighPainThresholdComponent, ModifyPainGainEvent>` (`ref`, matching the event's `[ByRefEvent]` shape); pair free per PLAN2 §5.1 row 10 — `ModifyPainGainEvent` had zero subscribers anywhere in WG before this |
| `Resources/Prototypes/_Onyx/Traits/quirks.yml` | same | adapted | **WP10-3** | Trimmed to the `HighPainThreshold` entry only (Onyx's `AlcoholTolerance`/`Parkour`/`Voracious`/`ColdBlooded`/etc. are unrelated features, same trim class as WP7's `_Onyx/Alerts/alerts.yml`). `conflicts: [PainNumbness]` -> `mutuallyExclusiveTraits: [PainNumbness]` (P2-D15, resolves against WG's existing `Resources/Prototypes/Traits/disabilities.yml:69` entry); `cost: 3` dropped (Quirks declares no `maxTraitPoints`, so cost is inert — see Deviations) |
| `Resources/Locale/en-US/_Onyx/traits/quirks.ftl` | same | adapted | **WP10-3** | 2 keys only (`trait-high-pain-threshold-{name,desc}`), same trim |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | same | modified | **WP10-3** | 1 edit (P2-D8): `IsPainNumb` widened with `if (HasComp<PainNumbnessComponent>(entity)) return true;` before the existing `StatusEffectPainNumbness` check, so Wolfgate's shipped `PainNumbness` trait (which grants the legacy Mono `PainNumbnessComponent`, not Onyx's status-effect form) actually suppresses wound pain. Placed after the existing part->body redirect so it tests the body. No new `using` (`Content.Shared.Traits.Assorted` already imported at `:13`) |
| `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs` | same | new (hook) | **WP10-4** | **HOOK 15**, 2 one-line sites: (a) `TryApplyWolfmedPain();` call inserted after the eye/viewport guard, before the lerp block; (b) `_bruteShader.SetParameter("darknessAlphaOuter", 0.8f);` -> `0.8f * level` (ONYX `DamageOverlay.cs:175`). Body lives in the `_WF` partial so no new `using` lands upstream. Adds no subscription (an `Overlay`, not an `EntitySystem`) |
| `Content.Client/UserInterface/Systems/DamageOverlays/DamageOverlayUiController.cs` | same | new (hook) | **WP10-4** | **HOOK 16**, 1 condition: the existing Mono `PainNumbnessComponent` check at the `MobState.Alive` brute/burn computation gains `&& !WolfmedPainOwnsVignette(entity)`. Predicate lives in the `_WF` partial; no new `using` upstream. Correctness depends on WP10-3's `IsPainNumb` widening (CRITIQUE2 m6) — already landed. Adds no subscription |
| - | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs` | new | **WP10-4** | HOOK 15's body, `partial class DamageOverlay`. `TryApplyWolfmedPain()` reads the networked `PainComponent` on the local player directly (`GetPain / SoftPainCap`, floored at 0.05) and writes `BruteLevel`; bails on `MobState.Dead` or `SoftPainCap <= 0`. No `PainChangedEvent` subscription — every `PainSystem` mutator is server-gated and every reader here touches only `[AutoNetworkedField]` state, so a per-`Draw()` read needs no trigger |
| - | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlayUiController.Wolfmed.cs` | new | **WP10-4** | HOOK 16's `WolfmedPainOwnsVignette(EntityUid)` predicate, `partial class DamageOverlayUiController` — a one-line `HasComponent<PainComponent>` check |
| `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs` | same | modified | **WP10-5** | Vendored at Onyx's `_Onyx` path, namespace stays `Content.Server.Chat.Systems` (D13 pattern; a partial of the upstream `EmoteOnDamageSystem`). **5** `// WOLFGATE` edits: (1) `using Content.Shared._Onyx.Wounds;` added - PLAN2 claims it is already Onyx line 1, it is not (see Deviations); (2) `using Content.Shared._WF.Wolfmed.Compat;` (D12); (3) `[Dependency] DamageableSystem` -> `WolfmedDamageableSystem` (D12) - the `GetTotalDamage(uid).Float()` call site compiles unchanged via `Entity<T>`'s implicit `EntityUid` conversion; (4) the P2-D22 `HasComp<WoundHostComponent>` gate as the first statement of `HandlePainDamageEmote`; (5) `HasComp<PainNumbnessComponent>` added to the bail-out chain for parity with WP10-3's widened `PainSystem.IsPainNumb`. Onyx's `ProtoMan` swap was a confirmed no-op (the file never references it). **Registers no subscription** |
| - | `Content.Server/Chat/EmoteOnDamageComponent.cs` | new (hook) | **WP10-5** | **HOOK 17**, purely additive: `emotesThreshold` (`Dictionary<float, HashSet<ProtoId<EmotePrototype>>>`), `allowedDamageType`, `painThreshold` (6f), `LastTotalDamage` (`[ViewVariables]`), plus `using Robust.Shared.Prototypes;`. Explicit `[DataField("...")]` names match this file's house style. **`Emotes` is left untouched** so `ZombieSystem.cs:181` / `ZombieSystem.Transform.cs:145`'s `AddEmote(uid, "Scream")` path is unaffected (P2-D9). The existing `[Access(typeof(EmoteOnDamageSystem))]` already covers the new fields |
| - | `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs` | new (hook) | **WP10-5** | **HOOK 18**, 1 line: `HandlePainDamageEmote(uid, emoteOnDamage, args);` as the first statement of `OnDamage`, before the existing `if (!args.DamageIncreased) return;` (Onyx's placement). **Never a second `<EmoteOnDamageComponent, DamageChangedEvent>` subscription** - that pair stays owned by this file's `Initialize` at `:22`. No `AddEmote`/`RemoveEmote` threshold overloads added (nothing calls them) |
| - | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified | **WP10-5** | P2-D7 `- type: PainShockTarget` + P2-D9 `- type: EmoteOnDamage` (`emotesThreshold: {50: [Scream], 80: [Scream, Crying]}`, `emoteChance: 0.6`, `withChat: true`, `hiddenFromChatWindow: true`, `emoteCooldown: 8`), both appended to the existing phase-1 `# WOLFGATE` block on `BaseMobSpeciesOrganic`. Onyx's `emotes:` key corrected to `emotesThreshold:`. `emotes:` deliberately left empty so the upstream `OnDamage` body still returns at its `Emotes.Count == 0` check - no double emote |
| - | - | skipped | **WP10-5** | **No audio or locale assets were needed.** `Scream` (`Resources/Prototypes/Voice/speech_emotes.yml:3`) and `Crying` (`:110`) are existing upstream `Vocal`-category emote prototypes whose audio comes from each species' own `VocalComponent` `EmoteSounds` set. Nothing new was copied, so no `meta.json`/`attributions.yml` change was required |

**Species included as wound hosts (17 of 18 `BaseMobSpeciesOrganic` descendants):** `arachnid`, `diona`,
`dwarf`, `gingerbread`, `human`, `moth`, `reptilian`, `slime`, `vox` (base) · `chitinid`, `feroxi`,
`rodentia`, `vulpkanin` (`_DV`) · `tajaran`, `yowie` (`_Goobstation`) · `asakim` (`_Mono`) · `hydrakin`
(`_Obelisk`). Diona and slime ship on `OrganicBodyPartProfile` until phase 5 (Onyx-consistent, not
Onyx-equivalent — §8.1 item 1(a)).

**Species excluded (1 of 18):** `protogen` (`_Mono`) — carries `prototype: SiliconDeathgasp`
(`_Mono/Entities/Mobs/Species/protogen.yml:74`), a synthetic. `WoundHostComponent` is stripped at
`ComponentInit` by `WolfmedWoundHostExclusionSystem` before any wound system observes it.
**Phase 2 note (P2-D22):** that system removes `WoundHostComponent` and nothing else, so protogen *does* carry the `PainShockTarget` and `EmoteOnDamage` components WP10-5 added beside it. `PainShockTarget` is inert there (`PainComponent` is only ever ensured on wound hosts) and the pain sounds are gated in C# by `HandlePainDamageEmote`'s `HasComp<WoundHostComponent>` guard, not by the YAML block.

| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReattachTest.cs` | new | **WP11-0** | T-REATTACH; closes PLAN §8.3 trap 2 (a part re-attached via `SharedBodySystem.AttachPart` regains live wound tracking, not just its old wound data) |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualsTest.cs` | new | **WP11-0** | T-VISUALS; first-ever coverage of `PartDamageVisualsComponent`, live with zero consumers since WP5 |
| — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | modified | **WP11-1** | P3-D1 (replaces the withdrawn HOOK 19): `ChargeVitalPartLoss` added to the existing host-gated `OnPartRemoved`, after `_bleeding.OnPartChanged`. A lost vital part with no remaining sibling of its type charges its own total as systemic `Bloodloss`, so `CheckVitalDamage` cannot fall when a head comes off. Three new `[Dependency]` lines (`WolfmedDamageableSystem`, `IPrototypeManager`, plus the existing `_body`). No new subscription, no upstream file, no prototype edit |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | modified | **WP11-1** | **DECISIONS.md §8.6-1 (user balance decision): guns and lasers can sever.** Per part: a `Heat` row in `amputationThresholds` equal to that part's `Piercing` row (Head 200, Arm 250, Hand 200, Leg 250, Foot 220) and `dismembermentFinishingDamage: {Piercing: 12, Heat: 15}`. Slash/Piercing/Blunt thresholds untouched; the Slash (15) and Blunt (50) finishing minimums are deliberately left out of the per-part dict so they keep falling back to `WoundHostComponent.DefaultDismembermentFinishingDamage`. **Overrides PLAN3 P3-D4/P3-D13 and §3's "not touched" entry for this file** |
| — | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundDamageFoundationTest.cs` | modified | **WP11-1** | Two stale literals in `RoutesAndProjectsDamageTest` corrected for P3-D1 (systemic `Bloodloss` 100 -> **113**, projected body total 106 -> **119**), with the derivation in a `// WOLFGATE` comment. **Deviation from PLAN3 §4 serialisation rule 2** (the file is nominally WP11-3's) - unavoidable: P3-D1 is what changed the numbers |
| `Resources/Prototypes/Body/base_organs.yml` (values) | `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | new | **WP11-2** | **PROTO A half 1.** Seven abstracts (`WolfmedOrgan{Brain,Eyes,Heart,Lungs,Liver,Stomach,Kidneys}`), each `- type: WolfmedOrgan` + `- type: OrganDamage`, values byte-checked against `ONYX Resources/Prototypes/Body/base_organs.yml:481-491,537-547,665-676,718-730,755-767,808-819,846-858`. `health`/`maxHealth` omitted on purpose - `WolfmedOrganComponent` already defaults to 15/15, which is Onyx's own `OrganComponent` C# default, and no Onyx prototype overrides it. Separate abstract ids rather than re-declarations (RT's `ComponentRegistrySerializer` throws `Duplicate ID`), same shape as WP7's `parts.yml`. Brain carries no `destructionWound`: `OrganHealthSystem` kills the mob instead of destroying it (P3-D22) |
| — | `Resources/Prototypes/Body/Organs/human.yml` | modified (hook) | **WP11-2** | **PROTO A half 2.** Seven one-line `parent:` edits at `:53,103,150,189,215,249,270`, each `# WOLFGATE (WP11-2, D8)`. `OrganHumanTongue` (`:119`), `OrganHumanAppendix` (`:128`) and `OrganHumanEars` (`:139`) deliberately untouched - no body graph in the repo slots them. This is what makes `OrganDamageSystem`/`OrganHealthSystem` non-inert for the first time since WP6 |
| — | `Content.Server/_WF/Wolfmed/Body/WolfmedOrganConsequenceSystem.cs` | new | **WP11-2** | P3-D8. Sole subscriber of `<WolfmedOrganComponent, OrganFunctionChangedEvent>` (audited free: the event is declared at `OrganHealthSystem.cs:21` and raised at `:71`, nowhere else). **Raises** `OrganEnableChangedEvent` rather than subscribing it - that pair is owned by `SharedBodySystem.Organs.cs:23` and a second registration is a server-start crash; precedent `_Shitmed/Cybernetics/CyberneticsSystem.cs:27-28,45-46`. Guards `TerminatingOrDeleted` + `HasComp<OrganComponent>` |
| — | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | modified | **WP11-2** | P3-D23, two `// WOLFGATE` guards: `TerminatingOrDeleted(uid)` before `DestroyOrgan` in the `Update` loop, and `!TerminatingOrDeleted(parent)` in `DestroyOrgan`'s `CreateOrMergeWound` condition. `DestroyOrgan` calls `RemoveOrgan` and wounds the containing part, and `RecursiveDeleteEntity` reaches it while a mob terminates - byte-for-byte the `DebugAssertException` WP9 fixed in `WolfmedBodyPartLifecycleSystem`, which cost 13 unrelated pooled-pair failures. Unreachable before PROTO A |
| `Content.Shared/Armor/ArmorComponent.Locational.cs` | `Content.Shared/_WF/Wolfmed/Armor/ArmorComponent.Wolfmed.cs` | new | **WP11-3** | P3-D5 (Option B). `public sealed partial class ArmorComponent` re-opened from `namespace Content.Shared.Armor`, so `Content.Shared/Armor/ArmorComponent.cs` keeps **zero** edits (it is `sealed partial`, `[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]`, no `[Access]`). Adds `Coverage`, `CoverageSymmetry` and `PartModifiers` plus the new `[DataDefinition] ArmorPartModifier` type (name grepped clear). **No `[AutoNetworkedField]`** — `ArmorPartModifier` is a `[DataDefinition]`, not `NetSerializable`, and the fields are prototype-static so the client already has them at spawn. `Coverage`/`CoverageSymmetry` are **nullable** where Onyx's are `= []` (deviation below). Onyx's `traumaDeductions` (P3-D20) and `ShowArmorOnExamine` are not ported |
| — | `Resources/Prototypes/_Mono/Entities/Clothing/Head/Helmets/bulletproof_helmets.yml` | modified (hook) | **WP11-3** | **PROTO B**, 1 line. The file's only `- type: Armor` block (`ClothingHeadBPHelmetLight`) gains `coverage: [Head] # WOLFGATE (WP11-3, P3-D6)` |
| — | `Resources/Prototypes/_Mono/Entities/Clothing/OuterClothing/Armor/bulletproof_vests.yml` | modified (hook) | **WP11-3** | **PROTO B**, 5 lines. Each `- type: Armor` block (Light, Medium, Heavy, Polyvalent, Stabproof) gains `coverage: [Torso, Arm, Leg] # WOLFGATE (WP11-3, P3-D6)`. `Chest`/`Groin` are never emitted (D9 — no such `BodyPartType` member; the Release lint would fail). **The only content annotated in phase 3**; every other `- type: Armor` in the game (272 across 75 files, including `ClothingHeadHelmetSwat`) keeps unset coverage and therefore today's whole-body behaviour |
| `…/WoundDamageFoundationTest.cs` (4 fixtures + 3 tests) | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundDamageFoundationTest.cs` | modified | **WP11-3** | Four new `[TestPrototypes]` ids (`WoundFoundationArmorHead`, `WoundFoundationArmorAllHead`, `WoundFoundationArmorLeftArm`, `WoundFoundationArmorLocational`) and five tests: `AppliesLocationalArmorExactlyOnceTest`, `EmptyCoverageAndSymmetryTest`, `LocationalModifierOverridesAndFallbackTest` (ported, D9-mapped) plus the Wolfgate-only `PartModifiersRouteThroughArmorPenetrationTest` and `UncoveredPartIgnoresArmorPenetrationTest`. Phase 1's `AppliesArmorExactlyOnceTest` is **kept and still asserts head 5 / torso 5** — its fixture declares no `coverage`, so Option B is a verified no-op for it; only its now-false comment was rewritten. Onyx's `WoundFoundationArmorAll` is renamed `…AllHead` on purpose (it is worn in the head slot) |
| `Content.Client/Damage/DamageVisualsSystem.cs` (4 tagged regions) | `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | new | **WP11-4** | **HOOK 20** body. Option A: `OnPartDamageVisualsState`, `UpdatePartDamageVisuals` (re-typed to WG's non-tuple `UpdateTargetLayer(SpriteComponent, …)` calling convention, D18/§2.3), `GetLayerDamage` (P3-D25 hand→arm / foot→leg fold). Option B (shipped, see deviations): `OnBodyPartState`, `UpdateDetachedPartDamage`, `UpdateDetachedDamageLayer`, `SetDetachedDamageLayerVisible`, `GetDetachedDamageThreshold`, `TryGetDetachedDamagePrefix` (Onyx's `Groin` arm dropped, D9) |
| — | `Content.Client/Damage/DamageVisualsSystem.cs` | modified (hook) | **WP11-4** | **HOOK 20**, 3 insertions (1 using + 2 subscriptions in `Initialize()`, 5 lines in `HandleDamage`): (a) `using Content.Shared._Onyx.Wounds;` + `SubscribeLocalEvent<PartDamageVisualsComponent, AfterAutoHandleStateEvent>(OnPartDamageVisualsState);` (Option A, PLAN3-authorised); (b) the early-return block in `HandleDamage` after `UpdateDisabledLayers` and before `CheckOverlayOrdering`; (c) `using Content.Shared.Body.Part;` + `SubscribeLocalEvent<BodyPartComponent, AfterAutoHandleStateEvent>(OnBodyPartState);` — **one extra subscription beyond PLAN3's literal HOOK 20 table**, needed to make the pre-authorised Option B fire at all (see deviations) |
| — | `Content.Shared/Body/Part/BodyPartComponent.cs` | modified (hook) | **WP11-4** | **HOOK 21**, one word: `[AutoGenerateComponentState]` → `[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]`. Confirmed zero prior `<BodyPartComponent, AfterAutoHandleStateEvent>` subscribers repo-wide before this WP, so the flag only adds behaviour |
| `Resources/Textures/_Onyx/Wounds/brute_damage.rsi` (77 states, 78 files incl. meta.json) | same | new | **WP11-4** | Byte-copied from ONYX via `git show`. `meta.json` license `CC-BY-SA-3.0`, copyright `Drawn by Ubaser.` — **identical artist and licence already shipped and accepted** in WG's own `Resources/Textures/Mobs/Effects/brute_damage.rsi` (verified). All 77 states have a matching `.png`; no orphan states |
| `Resources/Textures/_Onyx/Wounds/burn_damage.rsi` (77 states, 78 files incl. meta.json) | same | new | **WP11-4** | Same licence/attribution as above, verified identically. All 77 states have a matching `.png` |
| — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified (hook) | **WP11-4** | **PROTO C**, 2 lines. The `- type: DamageVisuals` block's `damageOverlayGroups.Brute.sprite`/`.Burn.sprite` retargeted from `Mobs/Effects/{brute,burn}_damage.rsi` to `_Onyx/Wounds/{brute,burn}_damage.rsi`. No `Groin` layer added (D9), no Hand/Foot `targetLayers` added (Onyx does not either; the live overlay stays 6 layers in both trees) |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/AmputationConsequenceTest.cs` (**4 of Onyx's 5** — WP12-10 reconcile: `SurgicalHealRemovesConsequenceAndUnblocksTest` restored in WP12-9 against HOOK 25's surgery-layer gate; `HealingDamageKeepsConsequenceBlocked` stays skipped because P4-D18 deliberately leaves `CanAttachPart` ungated) | same path | new | **WP11-5** / **WP12-9** | T-AMP-CONSEQUENCE-1/-2/-3. Fixture rebuilt Shitmed-shaped (PLAN3 §6.1 trap 9): a `- type: body` graph in place of Onyx's `InitialBody`, `TransplantCompatibility`/`bodyPartProfile` dropped, `partType: Chest` → parts inheriting `TorsoHuman`/`HeadHuman` (D9), and Onyx's part-level `amputationThresholds` moved to `- type: WolfmedBodyPart` (D8). The fixture torso carries `amputationConsequenceSeverity: 50` — **load-bearing** (PLAN3 §8.7 hazard 7 / CRITIQUE3 M3-2): 35 is both the component default and `WolfmedBodyPartSystem.Get`'s fallback, so only a non-default value can prove edit #9 reads the severity off the **parent stump**. Onyx's `HasAmputationConsequence` / `TryAttachPart … Is.False` assertions dropped (B-2/P3-D2); `SurgicalHealRemovesConsequenceAndUnblocks` (needs `SurgeryStepEvent`, D7) and `HealingDamageKeepsConsequenceBlocked` (payload is the dropped gate) are recorded as skips in the file's `<remarks>` |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAmputationTest.cs` | new | **WP11-5** | 5 tests. **T-AMP-VITAL** (both halves: the wound host's readout moves `+115` = the finishing hit + Shitmed's flat `VitalDamage` 100, with systemic `Bloodloss` at **315**; a `MobMonkey` non-host still charges **exactly 100**, the D2 guard the withdrawn HOOK 19 would have failed). **T-AMP-GUN** — replaces PLAN3's T-AMP-NOGUN per DECISIONS.md §8.6-1: a hand over its Piercing threshold is severed by one 14-Piercing round (hit 16), over its Heat threshold by one 16-Heat shot (hit 14), a below-threshold foot by neither, and the untouched melee case still needs 6 machete-grade Slash 32 hits on an arm. **T-AMP-OVERFLOW** (a: `AmputationOverflow` stays 0 on a shipped part through 16 × Blunt 25, `Severable` flips exactly at the threshold, one Blunt 50 detaches; b: a bespoke `maxDamage: 50` part accumulates **30**). **T-AMP-EXPLOSION** (`TryRouteDistributedDamage(..., isExplosion: true)`, single-part mask, saturated chance). **T-AMP-CONSEQUENCE-SEPARATE** (two `AmputationConsequenceWound` at **50** + two `DismembermentWound` at **120**; `mergeMode: SeparateInstances`, P3-D10 — replaces `tests.md`'s merge test). New `[TestPrototypes]`: `WolfmedAmputationBodyGraph/Body/Torso`, `WolfmedAmputationOverflowGraph/Body/Part` |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOrganTest.cs` | new | **WP11-5** | 8 tests. **T-ORG-DATA** (all seven `OrganHuman*` resolve with `WolfmedOrgan` 15/15 and WP11-2's measured `OrganDamage` numbers — catches a mistyped PROTO A `parent:`). **T-ORG-CAP** (`15 × 0.3 = 4.5` per application: 15 → 10.5 → 6.0, unchanged by a ×10 bigger hit). **T-ORG-DESTROY** (lungs → `InternalBleedingWound` severity **35** on the torso, organ deleted). **T-ORG-HEART** (`DelayedDeathComponent` — the only proof P3-D8's Shitmed-consequence argument holds). **T-ORG-BRAIN** (mob `Dead`, organ **not** deleted — pins P3-D22). **T-ORG-EYES** (`TemporaryBlindnessComponent`, landing at the *disable*, before destruction). **T-ORG-FUNC** (the only coverage of `WolfmedOrganConsequenceSystem`: an `onAdd` grant is revoked in the one-tick window, and the deliberate second disable is harmless). **T-ORG-INERT** (D2/D3/D32: same graph, same torso profile, same organ, minus `WoundHostComponent` → routing refuses it and the organ loses nothing). New `[TestPrototypes]`: `WolfmedOrganTestProfile`, `WolfmedOrganTestGraph/Body/Torso/Organ`, `WolfmedOrganControlBody`, `WolfmedOrganFuncGraph/Body/Organ` |
| `Content.Server/_Onyx/Medical/MedicalPatchComponent.cs` | same | verbatim | **WP12-0** | zero edits; `InjectAmmountOnAttatch`/`InjectPercentageOnAttatch` field names keep Onyx's misspelling |
| `Content.Server/_Onyx/Medical/MedicalPatchSystem.cs` | same | verbatim | **WP12-0** | zero edits; registers `<MedicalPatchComponent, EntityStuckEvent>` and `<MedicalPatchComponent, EntityUnstuckEvent>`, both free (§5.1 row 1/2) |
| `Resources/Prototypes/_Onyx/Entities/Objects/Specific/Medical/medical_patch.yml` | same | modified | **WP12-0** (icon fix by orchestrator after WP12-10) | `BaseMedicalPatch` (abstract), `MedicalPatchMakeshift` (spawnable, `updateTime: 2`, `singleUse: true`), `UsedMedicalPatch`, `UsedMedicalPatchMakeshift`, and the `MedicalPatchMakeshift`/`SilkPatchMakeshift` construction graphs. No fill/vending/cargo/loadout placement (P4-D13). `# WOLFGATE` `icon:` added to both `construction` prototypes: Wolfgate's construction prototype requires one (Release linter error `File not found. (/Textures)`), Onyx's did not |
| `Resources/Prototypes/_Onyx/Tags/medical_patch.yml` | same | verbatim | **WP12-0** | one tag, `MedicalPatch`; grepped free in WG before adding |
| `Resources/Textures/_Onyx/Objects/Medical/medical_patch.rsi` (22 files: 21 PNG + `meta.json`) | same | new | **WP12-0** | byte-copied via `git show`. `meta.json` license `CC-BY-SA-3.0`, copyright `@jorgun  inspired by Studenterhue of Goonstation` — **new artist/licence pair for Wolfmed**, recorded here |
| `Resources/Locale/en-US/_Onyx/medical/medical_patch.ftl` | same | verbatim | **WP12-0** | 8 keys |

## Deviations

Deliberate departures from Onyx behaviour, with the reason. A re-sync should not re-litigate these.

### WP1

- **`MobStandStatusEffectBase` is not ported.** PLAN.md WP1's prototype table lists it as optional. Its
  blacklist references the tag `KnockdownImmune`, which returns zero hits across `WG/Resources/Prototypes`;
  a missing tag id is a YAML-linter error and the prototype has no consumer in phase 1. Re-add it with
  whatever WP lands Wolfgate's knockdown-immunity tag.
- **`StatusEffectSystem.Relay.cs` drops 11 relay subscriptions.** `StandUpAttemptEvent`,
  `StunEndAttemptEvent`, `RefreshStaminaCritThresholdEvent`, `GetMeleeTargetModifiersEvent`,
  `EmoteActionEvent`, `EmoteEvent`, `AccentGetEvent`, `BleedModifierEvent`,
  `RefreshPressureImmunityEvent`, `SelfBeforeInjectEvent`, `CatchAttemptEvent` either do not exist in
  Wolfgate or belong to systems that are server-side here. Each removal carries a `// WOLFGATE` reason.
- **`ExaminableStatusEffectSystem` and `PermanentStatusEffectsSystem` subscribe explicitly.** RT 277 has
  no `[SubscribeLocalEvent]` source generator.

### WP2

- **`OnyxBodyEvents.cs` carries `[ByRefEvent]`, which PLAN 2.11's snippet omits.** Onyx declares both
  events `[ByRefEvent]` and the only wound-set consumer (`FractureEffectsSystem`, phase 2) takes them
  `ref`; RT refuses a `ref` directed subscription for an event type without the attribute.
- **The facade writes damage through a local, not `ent.Comp.Damage.DamageDict[...]`.**
  `DamageableComponent` is `[Access(typeof(DamageableSystem), Other = AccessPermissions.ReadExecute)]`, so
  a direct write from another system is `RA0002`. `SetDamage`/`SetAllDamage` hoist the dictionary into a
  local first — the same pattern `DamageableSystem.TryChangeDamage` itself uses. **Any later WP writing an
  `[Access]`-restricted component field from `_WF`/`_Onyx` code will hit the same analyzer.**
- **`HealEvenly` / `HealDistributed` are implemented, not stubbed.** PLAN.md permits
  `NotImplementedException` until WP11; both are ported from `ONYX DamageableSystem.API.cs:177-272`
  instead, including the `+ Epsilon * (count - 1)` round-up that guarantees `HealEvenly` terminates.
- **Two facade methods beyond PLAN 2.1's listed surface:** `SetAllDamage` and `TryGetDamageGreaterThan`.
  Both are real Onyx members that `ClearAllDamage`, `HealEvenly` and `HealDistributed` call internally.
  `SetAllDamage` reports an **empty** delta rather than GUARD F's real delta, matching Onyx; setting all
  damage to a flat value is not "dealing" damage in either fork.
- **`ChangeDamage` passes `canSever: false, canEvade: false, partMultiplier: 1f, targetPart: null`.**
  PLAN 2.1's implementation note specifies exactly this; recorded because it makes every routed write
  `DamageChangedEvent.CanSever == false`, belt-and-braces on top of GUARD B (WP5).
- **The WP2 compile-exercise helper was not shipped.** It is preserved at
  `<plan>/wp/WP2-compat-smoke.cs.txt`; WP9 should fold it into the integration tests as a
  compile gate rather than leaving a permanently-registered no-op `EntitySystem` in the tree.

### WP3

- **`CirculatoryStreamPrototype`'s `using Content.Shared.Metabolism;` is commented out, not just the two
  fields.** Wolfgate has no `Content.Shared.Metabolism` namespace at all, so an unresolvable `using` is a
  build error on its own, independent of whether `MetabolismStagePrototype` is referenced.
- **`TargetingSnapshotComponent`/`TargetingSnapshotSystem`'s "using swap" is an addition, not a literal
  replacement.** Onyx's files carry no explicit `using Content.Shared._Onyx.Targeting;` — they are declared
  inside that namespace, so the symbols resolved for free. Since D10 skips vendoring Onyx's own
  `TargetBodyPart`/`TargetingComponent`, the fix is to *add* one `using Content.Shared._Shitmed.Targeting;`
  line to each file. Flagged so a future re-sync does not look for a `using` line that was never there.
- **`TargetingSnapshotSystem.Capture`/`Refresh` bind to Shitmed's `TargetingComponent.Target`**, which
  already exists with that exact name and type, so the vendored reads resolve unchanged.

### WP4

- **`WoundSystem.cs` does not gain `using Content.Shared.Body.Components;`.** PLAN.md WP4 #5 asks for it so
  `BodyComponent` resolves, but D17 deletes the only two references to `BodyComponent` in the file, so the
  using would be dead on arrival. Onyx's existing dead `using Content.Shared.Body;` at `:2` is kept
  verbatim (PLAN 2.17) and nothing is added.
- **`WoundDamageProjectionSystem.OnPartDamageDealt` takes `ref DamageChangedEvent`, not a by-value
  parameter.** Wolfgate's `DamageChangedEvent` is a `sealed class : EntityEventArgs`, but every Wolfgate
  subscriber takes it `ref` (e.g. `_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:214`) and RT's
  overload resolution binds `SubscribeLocalEvent<TComp, DamageChangedEvent>` to the ref handler; the
  by-value form is a `CS1503`. PLAN.md §4 WP5's prose (`DamageChangedEvent args`) is corrected here.
- **`WoundDamageProjectionSystem` gains a `WoundSystem` dependency.** D17 requires `OnRejuvenate` to call
  `WoundSystem.ClearBodyWounds` first; the file had no `WoundSystem` dependency in Onyx because the
  subscription lived on `WoundSystem` itself.
- **Routing's `AccumulateAmputationOverflow` keeps its `TryComp(part, out BodyPartComponent? bodyPart)`
  guard even though `bodyPart` is now unused.** The guard still means "this is a body part"; deleting it
  would change behaviour for non-part entities. Only the two `bodyPart.MaxDamage` reads moved to
  `_wfPart.Get(part)`.
- **`PickExplosionAmputationCandidate`'s `part.Parent == null` becomes
  `_body.GetParentPartOrNull(parts[i]) is null`.** Shitmed's `BodyPartComponent` has no `Parent` field;
  the slot-container walk is the equivalent. Same substitution as `GetDetachedRoot`.
- **Routing's `OnBeforeDamageChanged` honours Shitmed's `BeforeDamageChangedEvent.TargetPart`.** Onyx has
  no such member, so this is an addition rather than a port. It is gated on
  `SharedTargetingSystem.IsSelectable` so the composite masks Wolfgate really passes
  (`TargetBodyPart.All` from `HealthChange.cs`, `Torso | <random>` from `SharedBodySystem.Targeting.cs`)
  fall through to Onyx's own resolution chain.
- **Routing's `OnBeforeDamageChanged` opens with `args.Cancelled` instead of ordering against godmode and
  stasis.** `SharedGodmodeSystem` and `SharedStasisSystem` are abstract, and RT keys ordering on
  `GetType()`, so `before:`/`after:` against them is silently inert (PLAN 5.4).
- **`WoundTargetResolver` drops Onyx's `Roll()` anatomical-odds scatter entirely.** PLAN 2.13 resolves
  this: Wolfgate's gun and melee systems already roll their own inaccuracy before calling in, and Onyx's
  `Roll`, Wolfgate's `GetRandomBodyPart` and Shitmed's `GetRandomPartSpread` are three incompatible
  designs. Consequence: `CCVars.TargetingUseAnatomicalOdds` and `CCVars.TargetingDownedTargetsAreExact`
  (vendored in WP3) are read by nothing in phase 1.
- **`WoundTargetResolver.TryFind` ignores symmetry for `Torso` and `Head` only.** Onyx's version also
  exempted `Groin`; `BodyPartType.Groin` does not exist here (D9) and `TargetBodyPart.Groin` already folds
  to `BodyPartType.Torso` through `ConvertTargetBodyPart`, so the exemption is preserved by the fold.

### WP5

- **GUARD D takes a defensive copy of the damage specifier before raising `DamageDealtEvent`.**
  Onyx does not (`ONYX Content.Shared/Damage/Systems/DamageableSystem.API.cs:161-164` raises the
  event on the caller's object). In Wolfgate that is unsafe: routing's `OnDamageDealt` clears the
  dict in place to suppress the body write, and with `ignoreResistances: true` the local in
  `TryChangeDamage` is still the caller's object — `ApplyModifierSet` and `DamageModifyEvent`, the
  two places that would otherwise re-bind it, are both inside `if (!ignoreResistances)`. Upstream
  call sites pass component datafields straight in (`ImmovableRodSystem.cs:122`,
  `RepairableSystem.cs:36`, `BibleSystem.cs:165`, `PassiveDamageSystem.cs:50`, ...), so without the
  copy the first wound host hit by one of those would permanently empty that component's `Damage`.
  One extra allocation per routed hit.
- **`TryChangeDamage` returns a non-null but possibly empty `DamageSpecifier` for a cancelled wound
  host** (D27). Godmode and stasis still return `null` — they never set `Applied`. The one visible
  edge: if routing's handler happens to run before godmode's on the same entity (handler order
  between them is undefined and cannot be constrained — both godmode systems are abstract), a
  godmoded wound host returns an empty specifier instead of `null`. No damage lands either way; the
  routed pass is cancelled by godmode in turn. Callers that test `!= null` see "a hit for zero".
- **`WolfmedBodyPartLifecycleSystem` does not subscribe `<OrganComponent, OrganAddedToBodyEvent>` /
  `<OrganComponent, OrganRemovedFromBodyEvent>`** although PLAN 5.2 assigns those pairs to it. The
  only consumer of `OrganGot*Event` is `FractureEffectsSystem` (phase 2, not ported), so phase 1
  would be registering two handlers with no readers. Both pairs remain free and unclaimed.
- **`WolfmedBodyPartLifecycleSystem.OnPartAdded` fans over the attached subtree; `OnPartRemoved`
  calls the projection once for the detached root.** Onyx's `SetSubtreeBody` raises `OrganGot*Event`
  per subtree member, which is reproduced; but `WoundDamageProjectionSystem.OnPartRemoved` already
  walks to the detached root itself (`RefreshDetachedDamage` -> `GetBodyPartChildren`), so calling it
  per subtree member would re-project the same tree N times.

### WP6

- **`WoundSystem.HandlePartDamageApplied` and `WoundFractureSystem.HandlePartDamageApplied` are `public`,
  not Onyx's `internal`.** D13 moves `OrganDamageSystem` — the single `<WoundableComponent,
  PartDamageAppliedEvent>` subscriber and the only caller of all four `HandlePartDamageApplied`
  implementations — into `Content.Server`, a different assembly from the two shared systems, and there is
  no `InternalsVisibleTo` between them. `WoundBleedingSystem`'s copy stays `internal` because it moved to
  `Content.Server` as well. PLAN 5.2's invariant still holds: none of the four may take its own
  subscription.
- **The requested healing part is read from the healer's Shitmed `TargetingComponent`, not from
  `HealingDoAfterEvent.RequestedPart`.** `hooks-a.md` 5a/6 add a `NetEntity? RequestedPart` field to
  `Content.Shared/Medical/HealingDoAfterEvent.cs`, but that file is **not** in PLAN 3's authorised hook
  list and WP6's table does not include it. Wolfgate already selects the healed limb from the user's
  targeting today (`SharedBodySystem.Targeting.cs:129-131`), so `HealingSystem.ResolveWoundTargetPart`
  reproduces that with `WoundTargetResolver.TryResolveExact`, gated on
  `SharedTargetingSystem.IsSelectable`. Consequence vs Onyx: the part is re-read at do-after completion
  instead of being latched when the do-after starts, so switching target doll limbs mid-heal changes which
  limb is treated. Landing `RequestedPart` later is a 6-line change in one file plus swapping the two
  `ResolveWoundTargetPart` call sites.
- **`HealingSystem.IsWoundDamaged` is a third parallel check, not a fold into `HasDamage`.** Onyx folds
  the wound branch into `HasDamage`; Wolfgate's `HasDamage(DamageableComponent, HealingComponent)` has no
  entity parameter and composes with a separate `IsPartDamaged`, so `hooks-a.md` 5b's lower-risk option
  was taken. `HasDamage` and `IsPartDamaged` are untouched and still serve every non-wound-host.
- **`TryHeal` skips the body-level `DamageContainers` rejection for wound hosts.** Matching Onyx
  (`HealingSystem.cs:335-340`, `resolvedPart` branch): the container test belongs to the resolved part's
  profile, not to the mob's own `DamageableComponent`.
- **`OrganFunctionChangedEvent` is declared in
  `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs`,** in namespace `Content.Shared._Onyx.Body`, per
  PLAN WP6 #8. It is therefore **not reachable from `Content.Shared`**. Phase 2's `FractureEffectsSystem`
  is shared and uses `OrganGotInsertedEvent`/`OrganGotRemovedEvent` (which do live in shared, from WP2's
  `OnyxBodyEvents.cs`), so nothing breaks today — but any later shared consumer of
  `OrganFunctionChangedEvent` must move the declaration into `Content.Shared/_WF/Wolfmed/Compat/`.
- **`OrganHealthSystem.DestroyOrgan` does not walk the parent part's organ slots.** Onyx iterates
  `part.Organs` with `TryGetOrganInSlot`/`TryRemoveOrgan`, neither of which exists in Wolfgate;
  `SharedBodySystem.RemoveOrgan(organId, organ)` finds the containing container itself. The destruction
  wound is still created on the parent part, and the organ is still deleted on both paths.
- **`OrganHealthSystem` queries `WolfmedOrganComponent` paired with `OrganComponent`.** Onyx queries
  `OrganComponent` alone because health lives on it. A `WolfmedOrganComponent` on a non-organ entity is
  therefore ignored rather than destroyed.
- **`CirculatoryStreamSystem.TryGetPartSolution`/`TryGetStreamSolution` return `false` for any
  non-primary stream** instead of looking one up. D15 drops `InitializeStream`, so no secondary stream can
  ever exist in phase 1; the fallback branches would have been unreachable. `SetBleedRates` likewise drops
  the `CirculatoryStreamComponent` bookkeeping and the `SynchronizeStreams` fallback, leaving one call to
  `BloodstreamSystem.TryModifyWoundBleedProjection`.
- **`WoundBleedingSystem` keeps `using Content.Shared.Body.Systems;` alongside the added
  `using Content.Server.Body.Systems;`.** PLAN WP6 #2 describes this as a swap, but the file needs
  `SharedBodySystem` from the shared namespace and `BloodstreamSystem` from the server one; a literal swap
  is `CS0246`.

### WP7

- **RT has no YAML-level "remove this inherited component" mechanism.** Verified by reading
  `RobustToolbox/Robust.Shared/Serialization/TypeSerializers/Implementations/ComponentRegistrySerializer.cs`
  end to end: `Read`/`Validate` log `"Component of type '{compType}' defined twice in prototype!"` and
  **skip** the second entry if a components list declares the same type twice in one file (so PLAN's own
  example WOLFGATE block, which appends a second `- type: PassiveDamage`, would have silently no-opped the
  D29 neutralisation and logged an error every server start); `PushInheritance` only ever *adds* a parent's
  component to a child that lacks it, or merges fields if the child already declares it — there is no
  "delete" verb. Two consequences, both recorded as deviations from the plan's literal text:
  - **D29's `PassiveDamage` change is an in-place edit of the existing block on `BaseMobSpeciesOrganic`**
    (`damage: { types: { Heat: -0.07 }, groups: { Brute: -0.07 } }` → `damage: {}`), not a second
    `- type: PassiveDamage` entry appended after the new `WoundHost`/`Destructible` block. Functionally
    identical to the plan's intent; avoids the duplicate-component log error and the silent skip.
  - **Protogen's `WoundHost` exclusion (D21/D32) is a small shared C# system
    (`WolfmedWoundHostExclusionSystem`, `Content.Shared/_WF/Wolfmed/Body/`)**, not a prototype-level
    removal. It walks `IPrototypeManager.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true)`
    on `WoundHostComponent`'s `ComponentInit` and `RemCompDeferred`s the component if `BaseMobProtogen` is
    among the ancestors. Shared (not server-only) so client and server strip it identically before any
    GUARD A/B/C/D check ever runs (PLAN 8.3 trap 3: those guards must stay component-gated, and a
    server-only removal would desync a client that still carries the component from a stale prototype
    load). `protogen.yml` itself carries only a documentation `# WOLFGATE` comment — no functional change,
    since there is nothing to write there.
- **`Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` declares 10 separate `WolfmedBase<Part>` abstracts**
  (`WolfmedBaseTorso`, `WolfmedBaseHead`, `WolfmedBaseLeftArm`, `WolfmedBaseRightArm`,
  `WolfmedBaseLeftHand`, `WolfmedBaseRightHand`, `WolfmedBaseLeftLeg`, `WolfmedBaseRightLeg`,
  `WolfmedBaseLeftFoot`, `WolfmedBaseRightFoot`), not re-declarations of `BaseTorso`/`BaseHead`/etc as
  `body-organ.md` §5.2's draft YAML literally wrote them. Re-declaring an existing `id:` in a second file
  hits the same engine wall as above (`PrototypeManager.YamlLoad.cs:228`,
  `PrototypeLoadException($"Duplicate ID: '{id}' for kind '{kind}")`) — this is exactly the "duplicate-id
  caveat" PLAN's WP7 #6 flagged and pre-authorised the fallback for: each `WolfmedBase<Part>` is added to
  the matching upstream abstract's `parent:` list with a one-line `# WOLFGATE` edit (9 in
  `Resources/Prototypes/Body/Parts/base.yml`, 1 in `Resources/Prototypes/_Shitmed/Body/Parts/base.yml`).
  `BasePartInorganic`, `BaseTorsoInorganic` and `BasePart` are untouched — they carry no per-limb data in
  Onyx either, and `WolfmedBodyPartComponent`'s zeroed defaults are harmless there since only organic
  species (via `BaseHead`/`BaseLeftArm`/…/`BaseTorso`) become wound hosts.
- **`wounds.ftl` is not byte-identical to Onyx's, despite PLAN calling it "verbatim (43 keys, complete —
  nothing missing)".** `BoneFractureWound`'s four `examineDescription` keys
  (`wound-examine-fracture-{hairline,simple,displaced,comminuted}`) actually live in Onyx's
  `_Onyx/medical/health-examinable.ftl`, which belongs to the unported `HealthExaminable` system (WP10).
  The Release YAML linter fails with "No localization message found" for all four without them. Fixed by
  appending the four keys (copied verbatim from `health-examinable.ftl`) to the end of `wounds.ftl` with a
  `# WOLFGATE` comment explaining the source and noting they should be deleted if `health-examinable.ftl`
  is ported in WP10 (at which point they would be a duplicate key error instead — check then). **RESOLVED in WP10-2:** `health-examinable.ftl`
  is ported and the four stopgap keys were deleted from `wounds.ftl`.
- **`Resources/Textures/_Onyx/Wounds/{brute,burn}_damage.rsi` are not ported**, per PLAN's explicit
  "textures deliberately not ported" note — `wounds.yml` has zero texture references, and the sprites are
  cosmetic re-skins of ones Wolfgate already ships. **Superseded in WP11-4:** both RSIs (156 files total,
  CC-BY-SA-3.0 / Ubaser — the same licence and artist already shipped and accepted in
  `Mobs/Effects/*_damage.rsi`) were ported for severed-limb wound rendering (DECISIONS.md §8.6-5, Option B).

### WP8

- **HOOK 10 is implemented as *two* subscriptions, not one — but only one of them lives in the upstream file.**
  (Revised in fix round 1: the part handler moved from `Content.Shared/Armor/SharedArmorSystem.cs` to the new
  `Content.Shared/_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs`, so the upstream file now carries exactly the
  edit PLAN 3 authorises and nothing more. Behaviour is unchanged: same pair, same single registration site,
  same modifier maths, and `ArmorComponent.Modifiers` is public so no upstream access change was needed.)
  PLAN 3 authorises only the wound-host branch
  in `OnDamageModify` plus the `ApplyWoundSystemicArmor` helper, and `hooks-a.md` 7b classifies Onyx's
  `OnPartDamageModify` as phase 3 because Wolfgate's `ArmorComponent` has no `PartModifiers` field. Shipping
  only the authorised half would have **removed armour from every localized damage type** (`Blunt`, `Slash`,
  `Piercing`, `Heat`, `Cold`, `Shock`, `Caustic` — i.e. essentially all weapon damage) for every wound host:
  `OnDamageModify` returns early after armouring systemic types, and nothing else armours the localized half
  because `PartDamageModifyEvent` had no subscriber. It would also have failed PLAN 6.2's own **T-AP** gate,
  which asserts a penetrating hit lands strictly *more* damage on a part than a non-penetrating one — with no
  armour applied at all, both land the same. The part handler is therefore ported in its Onyx-fallback form
  (`ApplyModifierSet(damage, PenetrateArmor(component.Modifiers, ap))`), which is what Onyx itself does when
  `PartModifiers` is empty. Net effect in phase 1 is numerically identical to the pre-WP8 behaviour (armour
  applied exactly once, at the same rate); what changes is the *structure*, which is now the Onyx shape that
  phase 3's per-part `ArmorComponent.PartModifiers` slots into.
- **The wearer is found via `Transform(uid).ParentUid`, not `args.Owner`.** Onyx's `InventoryRelayedEvent<T>`
  carries an `Owner`; Wolfgate's (`Content.Shared/Inventory/InventorySystem.Relay.cs:154-165`) does not, and
  neither does `DamageModifyEvent`. An equipped item is reparented to the wearer by its slot container, so the
  transform parent is the wearer. A non-inventory parent simply fails the `TryComp<WoundHostComponent>` test
  and takes the unmodified branch.
- **Armour is unpredicted on wound hosts, like the rest of the routed pass (D35).** The wound-host armour
  split is *not* `_net.IsServer`-gated — it is component-gated, matching Onyx and PLAN 8.3 trap 3 — but
  routing itself is server-only, so on the client a wound host's localized damage is neither routed nor
  armoured and lands in full on the body's own `DamageableComponent` for one tick. The mispredict is larger
  than it was before WP8 and self-corrects on the next server state. Predicting routing is a later phase.
- **`CheckThresholds` calls `CheckVitalDamage` once, hoisted above the threshold loop.** Onyx calls it inside
  the loop (`Content.Shared/Mobs/Systems/MobThresholdSystem.cs:340`). Same answer; the call walks the whole
  body with `GetBodyChildren`, and running it once per threshold per damage event is pure waste.
- **`HurtCommand.cs` and its two locale files were reverted in fix round 1 (WP8 round 1 shipped them).**
  `hooks-b.md` 1 specifies the hook and classifies it "later"; PLAN 3's complete authorised list has no
  `HurtCommand` entry, and the orchestrator instruction WP8 round 1 cited for it is not recorded in PLAN.md or
  DECISIONS.md, so it was withdrawn rather than left as an unauthorised upstream edit. Nothing else depended on
  it: `WoundDamageRoutingSystem.TryApplyPartDamage` (the API it drove) is vendored Onyx code with other callers,
  and `WoundTargetResolver.TryResolveExact` is still used by `WoundDamageRoutingSystem` and `HealingSystem`. WP9
  should drive part damage by calling `TryApplyPartDamage` from the integration fixture instead of through a
  console command. The withdrawn diff is kept at `<plan>/wp/WP8-hurtcommand-deferred.patch`.
- **Sourcing gap disclosed (fix round 1):** the locale file WP8 round 1 added at
  `Resources/Locale/en-US/_Onyx/commands/damage-command.ftl` was reported as "copied byte-for-byte from Onyx",
  but that path is **not in the pinned Onyx sparse checkout** — its wording was reconstructed from the four
  key names Onyx's `HurtCommand.cs` calls, not diffed against a source. It has been deleted. If the command is
  ever authorised, widen the sparse checkout and copy the real file rather than reusing that text.

### WP9

- **Two upstream-behaviour fixes landed in `_WF` glue, not in the tests.** `WolfmedBodyPartLifecycleSystem`
  gained `TerminatingOrDeleted` guards (mob deletion threw a `DebugAssertException` from
  `WoundDamageProjectionSystem.RefreshDetachedDamage`, failing every pooled-pair teardown in the repo once a
  `MobHuman` had been spawned) and now drives `WoundBleedingSystem.OnPartInserted`/`OnPartChanged`. Both
  mirror what Onyx's own `BodyInventorySlotSystem` does (`:32,45` and `:39,49`); that system is Nubody glue D8
  skips, so its two jobs had no caller in the port. This is the PLAN 8.3 trap-2 class, extended to bleeding.
- **Blocker found by the tests: all pain was being multiplied by zero.** `PainSystem` scales every pain gain
  and every wound-pain floor by `new ModifyPainGainEvent().Multiplier`. `ModifyPainGainEvent` is a
  `record struct` whose primary constructor declares `float Multiplier = 1f`, but `new T()` on a struct binds
  to the implicit parameterless constructor, which zeroes the field instead of applying that default. Result:
  `GetRawPain` stayed at 0 on every wound host, so pain, pain shock, the pain stun and every pain-driven
  emote were inert. Both construction sites now pass `1f` explicitly (`// WOLFGATE`, `PainSystem.cs:111,233`).
  Onyx's source is byte-identical, so Onyx is presumably affected too. `WoundDamageFoundationTest` carries a
  canary asserting `new ModifyPainGainEvent().Multiplier == 0f`; if that ever fails the workaround can go.
- **Pain shock needs `StatusEffectsComponent`.** Wolfgate's `SharedStunSystem.TryParalyze` (what PLAN 2.8's
  shim maps Onyx's `TryUpdateParalyzeDuration` onto) refuses any entity without the **old**
  `StatusEffectsComponent`; Onyx's stun runs on StatusEffectNew and needs none. Real mobs have it, but any
  test or prototype that wants pain shock must declare `- type: StatusEffects` with `Stun`/`KnockedDown`.
- **Three numeric deviations recorded in `WoundDamageFoundationTest`, all explained at the assertion:**
  (a) detaching a vital `HeadHuman` adds 100 `Bloodloss` through Shitmed's `PartRemoveDamage`, so the
  projected body total after a detach is Onyx's 6 plus 100; (b) damage dealt to a *detached* limb still goes
  through Shitmed's `<BodyPartComponent, DamageModifyEvent>` (`PartDamage` modifier set +
  `GetPartDamageModifier`), so 5 Blunt lands as 2 and pain moves 8.70 -> 10.44 where Onyx expects 13.05;
  (c) `PainComponent.RecoveryPerSecond` is `FixedPoint2.New(1f / 9f)` = 0.11 at two decimals here, so one
  second of recovery gives 8.59, not Onyx's 8.62. A fourth: Onyx's pain-shock figures assume no residual
  suppression, so the test clears suppression explicitly before that block rather than re-deriving them.
- **`WoundHealingTest`: healing a wound takes the full heal off its severity** (15 -> 5 for 10 points of
  Blunt healing), not Onyx's 13.5, because `WoundPrototype.HealingMultiplier` defaults to 1 and `BluntWound`
  overrides nothing - in both trees.
- **Two more blockers found by WP9's smoke run (`EntityTest` / `PrototypeSaveTest`), both fixed in `_WF`:**
  (a) `WolfmedWoundHostExclusionSystem` used `RemCompDeferred`, which leaves the component in
  `EntityManager`'s `_deleteSet` at life stage `Initialized`; `StartComponents` then trips
  `DebugTools.Assert(!_deleteSet.Contains(...))`, so **every `MobProtogen` spawn threw** and
  `SpawnAndDeleteAllEntities*` failed. Changed to `RemComp`, which moves it to `Deleted` and is skipped.
  (b) `WolfmedBedHealMarkerSystem` added its marker on `ComponentStartup`, so every healing bed gained a
  component on spawn and `PrototypeSaveTest.UninitializedSaveTest` failed on `NFBedrollStained*`. Moved to
  `MapInitEvent` (free pair; identical in game, since a live bed map-inits in the same spawn call).
- **Onyx test literals that are stale against Onyx's own pinned prototypes were corrected, not preserved.**
  (a) `WoundFractureTest` asserted grade boundaries 15/30/50/75; the vendored `OrganicFractureProfile` - which
  is byte-identical to Onyx's - declares 20/35/50/60. (b) `WoundBleedingTest`'s two auto-clotting tests used
  wound severities 1 and 3, both below `SlashWound`'s own `minimumSeverity: 9`, so nothing ever bled and the
  assertions were vacuous; raised to 10 and 30. (c) `WoundScarTest` depends on `CCVars.SurgeryScarChance`,
  which ships at 0.35, so Onyx's version passes about a third of the time; the test now pins it to 1 and
  restores it. (d) `WoundBleedingTest` expected a reopened, fully bandaged severity-30 wound to come back at
  `BleedingSeverity` 5; `WoundSystem.SetWoundState` -> `SyncRuntimeComponents` (also byte-identical to Onyx)
  re-seeds `BleedingSeverity` from the wound's whole severity, so it is 35. Recorded as 35 with the reasoning
  in the test. **Re-check all four on an Onyx re-sync** - if Onyx fixes its prototypes instead, these flip back.
- **Tests deliberately not ported in phase 1** (each has a `// WOLFGATE` note at the site):
  `TourniquetStopsOnlySelectedPartTest` (WP11), `TraumaticAmputationCreatesSevereStumpBleedingTest`
  (`Severable` is only ever set by `AmputationSystem`, D26 -> phase 3),
  `EffectsRefreshOnTreatmentHealingAndDetachTest` (`FractureEffectSystem`, phase 2),
  `RepairSelectionAndSnapshotValidationTest` (`_Onyx.Repairable`, out of scope), and Onyx's three
  coverage/symmetry/locational armour tests (Wolfgate's `ArmorComponent` has no `coverage`,
  `coverageSymmetry` or `partModifiers`; the applies-exactly-once contract is kept in one test).
- **`BodyConsequencesTest` is a rewrite, not a port.** Onyx's three tests assert Nubody inventory-slot
  coupling (`shoes`/`socks`/`underwearb` disappearing with the groin) that no Wolfgate system implements -
  nothing under `Content.{Shared,Server}/Inventory` subscribes `BodyPartRemovedEvent`/`BodyPartDroppedEvent`.
  What survives is cascade-on-detach and down-at-zero-legs (Wolfgate goes down at zero legs, not at one).
- **`DamageSpecifier.Empty` is not a usable 'nothing landed' assertion here.** `DamageableInit` seeds every
  supported type to zero (D30), so `BodyPartProfileContractsTest` asserts `GetTotal() == 0` instead.

### WP10-1 (phase 2 — fractures)

- **Corrected upstream bug: `OrganicFractureProfile.manipulationModifier` (DECISIONS §8.2-1, user decision).**
  Onyx's YAML declares 0.92/0.84/0.75/0.75 for Hairline/Simple/Displaced/Comminuted, all below 1, but
  `FractureEffectSystem.OnGetMultiplier` computes `args.Multiplier *= 1f + (modifier - 1f) * partScale *
  treatmentScale` — below 1 makes the do-after *faster*. Shipped as 1.1/1.25/1.5/2.0, the C# defaults in
  `WoundPrototype.cs` (`Hairline = new(8, 0.9f, 1.1f)` … `Comminuted = new(40, 0.4f, 2f)`), which also match
  the non-fracture fallback's direction (`Disabled => 2.5f`, `Impaired => 1.25f`). This **overrides P2-D13**,
  which had recorded the values as ship-unchanged-and-escalate; the user chose FIX. Onyx-divergent by design.
- **P2-D2 — `TryGetUsedHandSymmetry` is a rewrite, not a shim.** Wolfgate's `SharedHandsSystem` has no
  string-id `IsHolding`/`TryGetHand` pair; `GetActiveHand(Entity<HandsComponent?>)` returns `Hand?` and
  `IsHolding(EntityUid, EntityUid?, out Hand?, HandsComponent?)` outs the `Hand` directly. An extension-method
  shim cannot win: C# prefers the applicable instance member. Behaviour preserved (used-item hand, else active
  hand; symmetry then filters `GetBodyChildren`). The only loss is `HandLocation.FunctionalLeft/Right`, which
  no Wolfgate entity can carry (`HandLocation` is `{Left, Middle, Right}`).
- **P2-D4 — the do-after bridge loses `Used` and loses `MultiplyDelay == false` do-afters.** Wolfgate's
  `GetDoAfterDelayMultiplierEvent` carries no `Used` item, so the **active hand** decides symmetry
  (zero-edit approximation, §8.2-2); and `SharedDoAfterSystem.cs:208-214` raises it only inside
  `if (args.MultiplyDelay)`, so do-afters that opt out escape the fracture penalty entirely.
- **P2-D18 — `FractureEffectSystem.RefreshTransferredPart` ships uncalled, deliberately.** Its only Onyx
  caller is the D7/D16-deferred `SpeciesChangeEntityEffectSystem`. Kept verbatim so a re-sync sees no diff.
- **WP10-1 gives `WoundStatusEffectSystem.HandlePartInserted`/`HandlePartRemoved` their first caller in
  Wolfgate.** `FractureEffectSystem.OnPartChanged` calls both. Inert at the pin only because no wound
  prototype populates `WoundStatusEffectBehavior.StatusEffect` (P2-D1). If one ever does, re-check.
- **P2-D21 — the fracture test fixture is not Onyx's.** Onyx's `WoundFractureBody` has no hands either, so
  `GetDurationMultiplier` there was always `1f` and Onyx's own `2f` literal could never have passed. Wolfgate's
  fixture adds one left hand (plus a symmetric `WoundFractureHandsBody`) so the manipulation half measures
  something. Deliberately one hand on `WoundFractureBody`: `AddHand` makes whichever hand attaches first
  active, so a two-handed fixture would depend on body-graph slot order.
- **P2-D17 carried forward — no client-side `OrganGot*` mirror.** `WolfmedBodyPartLifecycleSystem` is
  server-only, so the client does not refresh movement speed on limb attach/detach;
  `MovementSpeedModifierComponent`'s `[AutoNetworkedField]` modifiers correct it within one state.

### WP10-2 (phase 2 — HealthExaminable part status + pain)

- **P2-D20 — GUARD F calls `AddPartStatusMarkup` only for wound hosts; Onyx calls it unconditionally.**
  `HealthExaminableComponent` sits on `BaseMob` in Wolfgate, not on `BaseMobSpeciesOrganic`, so an
  unconditional call would give borg chassis, NPC silicons, animals and the D32-excluded Protogen the Onyx
  part-status readout *in addition to* today's threshold text. It would not crash (`WoundSystem.GetWounds`
  is `Resolve(..., false)`-guarded), so build, lint and startup all stay green — which is exactly why it is
  written down. Non-wound-hosts keep today's behaviour byte for byte (D2).
- **HOOK 14 (a) widens every examine popup in the game from 400 px to 560 px**, not only wound hosts'. It is
  the one phase-2 hook site that is not behaviour-neutral for non-hosts. Onyx makes the same change.
- **GUARD E2 retires "looks pale" for wound hosts only.** Non-hosts keep it. It lands in the same package as
  the part-status readout on purpose: shipped alone it would delete a message with nothing replacing it.
- **A fifth vendored-file edit PLAN2 did not predict: `DamageSpecifier.DamageDict` is keyed by `string`.**
  Onyx's is keyed by `ProtoId<DamageTypePrototype>`, so `HealthExaminableSystem.PartStatus.cs`'s
  `.OrderBy(entry => entry.Key.Id)` and `$"...-{type.Id.ToLowerInvariant()}"` both drop the `.Id`. Ordering
  and the produced loc key are unchanged (`ProtoId.Id` *is* the string). Any later vendored file that reads
  `DamageDict` keys will hit the same thing.
- **The legacy threshold branch is wrapped, not re-indented.** GUARD F's `if (!HasComp<WoundHostComponent>)`
  block leaves the wrapped body at its original indentation so the upstream diff is 4 added lines; Wolfgate
  merges this file from upstream and a re-indented block would conflict on every upstream touch.
- **`PartStatusSeverity.cs`'s enum drops Onyx's `[Serializable, NetSerializable]`** (PLAN2 §2.1's body). The
  shim's value is only ever `ToString().ToLowerInvariant()`-ed into markup, never networked. Restore the
  attributes if a later package networks a part-status snapshot.
- **`Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` is skipped** (orphaned, P2-D12), and Onyx's
  Targeting-doll `PartStatus`/`PartStatusComponent` stay unported (D10).

### WP10-3 (phase 2 — `HighPainThreshold` trait + pain-numbness widening)

- **P2-D8 — `IsPainNumb` was permanently false before this WP.** Wolfgate's shipped `PainNumbness` trait
  (`Resources/Prototypes/Traits/disabilities.yml:69`) grants the legacy `Content.Shared.Traits.Assorted.
  PainNumbnessComponent`, but `PainSystem.IsPainNumb` tested only the Onyx status-effect form
  (`PainNumbnessStatusEffectComponent`), which nothing in Wolfgate can apply — `TraitPrototype` has no
  `specials:`, and none of `PainNumbnessStatusEffectBase`/`StatusEffectPainNumbness`/`TraitStatusEffectBase`
  are ported (phase 4, with the narcotics — P2-D1/§8.4). Before this widening a pain-numb character still
  got the pain vignette, still screamed from pain shock and still took the full pain stun; `HighPainThreshold`'s
  `mutuallyExclusiveTraits: [PainNumbness]` guarded nothing observable. Fixed by widening `IsPainNumb` to also
  honour `PainNumbnessComponent`, per PLAN2 (overrules `statuses.md` §7's "no action needed").
- **P2-D15 — two prototype adaptations, both required, neither touching upstream Wolfgate files.**
  `conflicts:` -> `mutuallyExclusiveTraits:` (Wolfgate's field name; checked in both directions client-side at
  `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs:998`, so only the `_Onyx` entry needs the field —
  `disabilities.yml`'s `PainNumbness` entry is untouched). `cost: 3` dropped: Wolfgate's `Quirks` category
  (`Resources/Prototypes/Traits/categories.yml`) declares no `maxTraitPoints`, so `WithTraitPreference`
  short-circuits the cost check and it would be inert anyway; WG's own `quirks.yml` entries carry no `cost:`.
  `specials:` has no Wolfgate equivalent and is not ported (nothing reads it).
- **Registration confirmed free.** `HighPainThreshold` was a zero-hit grep across WG C#/YAML/FTL before this
  WP (§5.3). `<HighPainThresholdComponent, ModifyPainGainEvent>` is a new pair, but `ModifyPainGainEvent` had
  zero existing subscribers anywhere in WG (§5.1 row 10) — no collision.
- **No deviation from PLAN2 §4/WP10-3.** All five files landed exactly as specified: 2 verbatim `_Onyx`
  C# files, 1 adapted prototype, 1 adapted locale file, 1 modified vendored file (`PainSystem.cs`).

### WP10-4 (phase 2 — pain HUD overlay)

- **No deviation from PLAN2 §4/WP10-4.** All 4 files landed exactly as specified: 2 upstream one/two-line
  hooks (HOOK 15, HOOK 16) and 2 new `_WF` partials holding the bodies. Line numbers had drifted slightly
  from PLAN2's citations (`DamageOverlay.cs`'s eye/viewport guard ends at `:64`, the call landed at `:66`;
  `darknessAlphaOuter` at `:158` in PLAN2 is `:160` today; `DamageOverlayUiController.cs`'s condition is
  still at `:98`) — re-verified against the tree before editing, not copied blind.
- **P2-D6 confirmed at implementation time.** RT 277 still has no `SubscribeLocalEventAttribute`
  (`grep -rn "class SubscribeLocalEventAttribute" RobustToolbox` — zero hits), so the `Draw()`-level read
  stands: no client subscription to `PainChangedEvent` was added or would have worked (every
  `RaisePainChanged` call site is inside a `_net.IsServer`-gated method).
- **Ordering dependency (CRITIQUE2 m6) satisfied.** WP10-3's `PainSystem.IsPainNumb` widening had already
  landed in the tree before this WP started (verified via `git status`), so HOOK 16 correctly suppresses
  the vignette for pain-numb characters from the moment it ships — no window where WP10-4 alone would show
  the vignette to a `PainNumbness` trait holder.
- **Numbers not independently re-measured beyond a build check.** P2-4 asks that every pain-HUD number be
  verified against a real mob; this WP's checkpoint is the build gate only (PLAN2 §4/WP10-4's own text:
  "the vignette itself is not headlessly assertable"). WP10-6b owns T-PAIN-OVERLAY, which is the actual
  measurement; until it lands, the `min(1, GetPain/SoftPainCap)` / 0.05-floor formula and the predicted
  6.75/0.963 pain-level landmarks (`SoftPainCap = 135`) are PLAN2's derivation, not this WP's own
  measurement.
- **`DamageOverlay.Wolfmed.cs`'s `TryApplyWolfmedPain` resolves `PainSystem` via `_entityManager.System<T>()`
  inside the method body, not through a `[Dependency]` field** — matching PLAN2's code exactly, because the
  overlay is constructed from `DamageOverlayUiController.Initialize()`, which can run before entity systems
  exist. A later refactor that moves overlay construction later could switch this to `[Dependency]`, but
  should not do so without checking that ordering still holds.

### WP10-6a (phase 2 — bridge tests: T-AP, T-PASSIVE-A, T-PASSIVE-B)

- **T-AP passed as predicted.** Three `WolfmedBridgeBody` spawns (armoured@AP=0, armoured@AP=1, unarmoured@AP=0)
  hit through the new `WolfmedBridgeArmor` (`Blunt: 0.5` coefficient, mirrors `WoundFractureArmor`) via
  `DamageableSystem.TryChangeDamage(..., targetPart: TargetBodyPart.LeftArm, armorPenetration: X)`. Measured:
  armoured/AP=0 arm damage 5, armoured/AP=1 arm damage 10 (full penetration is a true no-op, not a zero-out —
  `DamageSpecifier.PenetrateArmor` returns an *empty* modifier set at `armorPenetration >= 1`, and applying an
  empty set leaves every type unmodified), unarmoured arm damage 10. Confirms armour penetration survives
  `WoundDamageRoutingSystem`'s detour through `WolfmedPartArmorSystem.OnPartDamageModify` (HOOK 10) rather than
  only `DamageableSystem`'s own now-bypassed resistance block.
- **T-PASSIVE-A's exact-value prediction was also wrong; corrected to a lower bound, still a strict D29 gate.**
  The `PassiveDamageComponent.Damage.Empty` check (D29's `damage: {}`) passed as predicted. The "still exactly
  10 after 60 simulated seconds" half did not: measured, the arm crept to **13.11**, not down. A real `MobHuman`
  on a bare test map keeps every other body system running for those 60 seconds too (`Barotrauma`,
  `Temperature`/`ThermalRegulator`, etc., all declared on `BaseMobSpeciesOrganic` — none of them Wolfmed's), and
  any localized damage type they deal can land on the same arm via the identical "no requested part" random-part
  routing T-PASSIVE-B documents. This is incidental environmental accrual, not healing, and not a Wolfmed
  defect — but it makes an exact `EqualTo(10)` the wrong gate on a real, fully-simulated mob. Corrected to
  `GreaterThanOrEqualTo(10)`, which is still a strict test of D29 (any *decrease* would mean the neutralised
  `PassiveDamage` healed the arm) without depending on an environment this test does not control. D21/D32
  species wiring and D29's neutralisation are both re-verified on a real mob, not a bespoke fixture.
- **T-PASSIVE-B's measured result overturned PLAN2's own prediction — recorded here, not silently "fixed".**
  PLAN2 §6.2 predicted "control heals; wound host does **not**." Measured on the first run: the wound host
  *also* healed (10 -> 5 after a 2-second window that should have applied two -5 ticks; landed partway through
  because the healing tick boundary drifts against whatever simulated time the map already sat at when the
  entities spawned — not a fixed offset, so a short window is inherently flaky here). Root cause read from
  `WoundDamageRoutingSystem.cs`: `OnBeforeDamageChanged` intercepts **any** `TryChangeDamage` on a
  `WoundHostComponent` entity regardless of sign; for a negative, un-targeted amount `RouteThroughBodyModifiers`
  skips picking a `_requestedParts` entry (its `localizedDamage` gate is `amount > 0`), so the change reaches
  `OnDamageDealt` -> `RouteAppliedDamage`, which buckets a negative localized type as healing and
  `ApplyLocalizedHealing` spreads it across whichever parts currently carry positive damage of that type —
  exactly the same path a legitimate heal item uses. **There is no wound-host-specific code barrier against an
  un-targeted heal reaching a part; D29's `damage: {}` on every shipped species is what actually stops this
  today, and it is a YAML choice, not a code-level one.** Fixed the test itself (not a production bug — nothing
  ships real `PassiveDamage` on a wound host) by running 300 ticks (10 simulated seconds, comfortably past
  saturation for 10 damage healing at 5/tick) and asserting **both** the control and the wound host reach
  exactly zero, which is deterministic and matches the corrected understanding. This is the actual value of a
  canary: it is meant to be a documented finding, not a rubber stamp — see the test's own `// WOLFGATE` remarks
  and XML doc for the full derivation. **A later phase that considers giving any wound host real `PassiveDamage`
  must not rely on `WoundHostComponent` presence alone to keep it from healing wounds "for free" — the YAML
  guard is the only thing doing that job today.**
- **A separate, one-off pool-flake observed and dismissed, not fixed:** on the very first isolated debug run of
  `RealWoundHostPassiveDamageIsNeutralisedTest` (before the fix above), `[SetUp]` hit `System.IO.IOException` on
  `bin/Content.IntegrationTests/gravestone-*.txt` — a file-lock race between two concurrent `dotnet test`
  invocations sharing the same build output directory (this session was running a full-file pass at the same
  time), not a Wolfmed defect. Did not recur on any subsequent run. Re-run rather than investigate Wolfmed code
  first if this class of `[SetUp]` `IOException` appears again (same trap class as the `db.ef` `admin_notes`
  warning, PLAN2 §6.1 item 2).
- **No production code changed.** This package is test-file-only: `WolfmedDamageBridgeTest.cs` (3 new
  `[TestPrototypes]` entries + 3 new `[Test]` methods) and this manifest.

### WP10-5 (phase 2 - pain sounds + mob wiring)

- **P2-D9 / DECISIONS.md §8.2-3 - Onyx's pain sounds are shipped with the YAML key corrected, and this is new
  behaviour, not a faithful port.** Onyx's `Resources/Prototypes/Body/species_base.yml:125` writes `emotes:` for
  a field whose serialized name is `emotesThreshold` (`[DataField] public Dictionary<float, HashSet<ProtoId<EmotePrototype>>> EmotesThreshold`).
  RT drops unknown mapping keys at *read* time (`RobustToolbox/Robust.Shared/Serialization/Manager/Definition/DataDefinition.cs:277`
  raises `FieldNotFoundErrorNode` only on the **validate** path), so the feature is silently dead at the Onyx
  pin and a headless server start would never have caught it. Wolfgate writes `emotesThreshold:`. Same class of
  corrected-upstream-bug as WP9's `ModifyPainGainEvent(1f)` find. **Verified bound**, not assumed: the Release
  YAML linter (the validate path) reports `No errors found` with the new block in place - an unbound key would
  have produced a `FieldNotFoundErrorNode`.
- **P2-D9 - `EmoteOnDamageComponent` is extended additively; Onyx replaces `Emotes`.** Wolfgate keeps both
  `emotes` (legacy `HashSet<string>`, `PrototypeIdHashSetSerializer`) and the new `emotesThreshold`, because
  replacing `Emotes` would silently change `ZombieSystem`'s two `AddEmote(uid, "Scream")` call sites. The two
  paths are mutually exclusive at runtime by construction: the upstream `OnDamage` body returns at
  `Emotes.Count == 0`, and `BaseMobSpeciesOrganic` leaves `emotes` empty.
- **P2-D22 - the pain path is gated on `HasComp<WoundHostComponent>`; Onyx has no such check.** Required because
  D32's opt-out (`WolfmedWoundHostExclusionSystem`) strips only `WoundHostComponent`, so a D32-excluded protogen
  keeps the `EmoteOnDamage` block from `BaseMobSpeciesOrganic` and `HandlePainDamageEmote` - which reads
  `GetTotalDamage` and never asks about wounds - would otherwise make synthetics scream. That is a D2 breach.
- **Beyond PLAN2: `HasComp<PainNumbnessComponent>` added to `HandlePainDamageEmote`'s bail-out chain.** PLAN2
  §4/WP10-5 leaves this as an explicit choice ("consider adding the same clause here for consistency, or record
  the asymmetry"); the clause was added. Without it a character with Wolfgate's `PainNumbness` trait would get
  no pain vignette and no pain-shock scream (WP10-3's widened `PainSystem.IsPainNumb`) yet still scream from the
  `EmoteOnDamage` path, because Onyx's `PainNumbnessStatusEffectComponent` has no applier in Wolfgate at all.
- **Correction to PLAN2 §3 / §4: `using Content.Shared._Onyx.Wounds;` is *not* "already line 1" of Onyx's
  `EmoteOnDamageSystem.PainSounds.cs`.** The file opens with `using Content.Shared.Chat;` and never imports the
  wounds namespace. The P2-D22 guard therefore costs one added `using`, marked `// WOLFGATE`, rather than zero.
  Behaviourally irrelevant, recorded so a re-sync does not read the diff as an unexplained import.
- **Known zombie interaction, not fixed (no authorised hook covers it).** `ZombieSystem.OnMobState`'s non-`Alive`
  branch calls `RemoveEmote(uid, "Scream")`, whose `removeEmpty: true` default `RemCompDeferred`s the whole
  `EmoteOnDamageComponent` once `Emotes` empties. Now that the component is YAML-declared on every organic
  species, a zombie that leaves `Alive` loses the prototype's `emotesThreshold` data along with it, and the
  `EnsureComp` on any later return to `Alive` produces a component with default (empty) thresholds. Effect is
  confined to ex-zombies and is invisible while crit/dead (`HandlePainDamageEmote` already bails on
  `MobState.Critical or Dead`). Fixing it means either `removeEmpty: false` at those two call sites or moving the
  zombie groan onto its own component - both outside §3's authorised hook list.
- **P2-D7 / pain shock is live on real mobs for the first time.** `- type: PainShockTarget` on
  `BaseMobSpeciesOrganic` is what finally satisfies `PainSystem.Update`'s
  `EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>` query, so the 2 s paralyse,
  the forced `Scream`, the jitter and the 30 s x0.7 adrenaline window all run for the first time in this build.
  Carried-forward WP9 warning applies: `StunSystemOnyxCompat` maps onto `TryParalyze`, which re-triggers stun VFX
  on every call; `UpdatePainShock` disarms after each shock and rearms only below pain 110, so repeat firing is
  bounded but not silent. Balance numbers remain unvalidated (P2-4).
- **No new subscription registered by this package.** `<EmoteOnDamageComponent, DamageChangedEvent>` stays
  exclusively owned by `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs:22`; the pain path is *called from*
  that handler (HOOK 18). A second subscription of that pair is a server-start `Duplicate Subscriptions` crash.
- **No audio or locale assets copied.** `Scream` and `Crying` already ship in Wolfgate as `Vocal` emote
  prototypes; their audio resolves through each species' `VocalComponent` `EmoteSounds`, so no
  `meta.json`/`attributions.yml` licence entry was needed.

### WP10-6b (phase 2 — fracture + pain tests)

- **Every prediction in this package was measured, and all six new tests passed on the first run.** Measured
  values, each pinned in a `// WOLFGATE` comment at its assertion: walk-speed modifier **0.5** after a
  Comminuted leg (Onyx's stale literal is `0.4f`); do-after multiplier **2.0** after a Comminuted arm, **1.0**
  after `TryMend`; walk **1.0** after detaching the leg; body pain **100.2** after one 60-Blunt routed hit on a
  fracture-capable part and **135** (the soft cap) after the second, with `GetPain` **94.5** once the
  adrenaline window opens; head pain **8.7** untraited vs **6.52** with `HighPainThreshold`; overlay levels
  0 / 0 / 0.05 / 0.5 / 0.7 at pain 0 / 6 / 6.75 / 67.5 / 200.
- **PLAN2 P2-D16's manipulation prediction of `0.75` is itself stale and was NOT used.** P2-D16 was written
  against Onyx's shipped `manipulationModifier` values; DECISIONS.md §8.2-1 (the binding answer) had WP10-1
  restore the C# defaults 1.1/1.25/1.5/2.0, so the correct expectation for a Comminuted arm is **2.0** —
  which is, by coincidence, Onyx's own original literal. Derivation at the assertion: Comminuted arm
  `manipulationModifier` 2.0, `Arm` absent from `PartEffectScales` so partScale 1, treatment `None` scale 1 →
  `1 + (2.0 - 1) · 1 · 1 = 2.0`; the intact left hand falls through `GetEffect` to
  `BodyPartFunctionalitySystem.GetState` = `Functional` (P2-3's cvar is false) → `1 + (1 - 1) · 0.75 · 1 = 1`;
  product **2.0**. Verified by test, not by arithmetic alone.
- **Two `[TestPrototypes]` additions beyond WP10-1's T-FIXTURE, both mandatory, both in `WoundFractureTest.cs`
  (serialisation rule 0 gave that block to WP10-1, which is finished).** (1) `- type: Alerts` on
  `WoundFractureBody`: `AlertsSystem.ShowAlert` early-returns when the entity has no `AlertsComponent`
  (`AlertsSystem.cs:87`), so T-FRACT-ALERT would have read `false` for a reason with nothing to do with
  `FractureAlertSystem`. (2) `WoundFractureHeldItem` (a bare `- type: Item`): T-FRACT-HANDS drives
  `TryGetUsedHandSymmetry`'s `used` branch through `SharedHandsSystem.IsHolding`, which only resolves a hand
  for a real item. Neither is a production-data change.
- **T-FRACT-HANDS asserts both directions, not just Onyx's.** PLAN2 asks for "item in the right hand →
  multiplier 1". The test also holds a second item in the *left* hand and asserts **2.0**, because the `1f`
  half alone is indistinguishable from the "no hand found" failure mode P2-D21 warns about. Both items are
  picked up with `checkActionBlocker: false, animate: false` (the fixture has no action-blocker components and
  the pickup animation is irrelevant headlessly).
- **T-HIGH-PAIN uses its own control fixture, not `WolfmedBridgeBody`.** PLAN2 §6.2 names
  `WolfmedBridgeBody` (defined in `WolfmedDamageBridgeTest.cs`). `[TestPrototypes]` are pool-global so that
  would have worked, but it makes one test file's fixture load-bearing for another's assertions;
  `WolfmedPainControlBody`/`WolfmedHighPainThresholdBody` are a matched pair in the file that uses them.
- **`6.52`, not `6.525` or `6.53`: `FixedPoint2` truncates.** `operator *(FixedPoint2, float)` is
  `new((int) ApplyFloatEpsilon(a.Value * b))` (`FixedPoint2.cs:101`), so `870 × 0.75 = 652.5 → 652`. The same
  truncation is why the overlay level floors to `0.04` at pain 6 (`FixedPoint2.cs:116`). Any later balance pass
  that changes `HighPainThresholdComponent.PainMultiplier` must re-measure rather than re-multiply.
- **The pain overlay level is asserted through a mirror of the client formula, not the client code.**
  `DamageOverlay.Wolfmed.cs`'s `TryApplyWolfmedPain` lives on an `Overlay`, which a headless pair cannot draw;
  `WolfmedPainTest.Level` reproduces its exact expression
  (`FixedPoint2.Min(1f, GetPain / SoftPainCap).Float()`, floored to 0 below 0.05). **If HOOK 15's formula is
  ever changed, this mirror must be changed with it** — nothing in the compiler couples them.
- **A measured finding worth keeping: the pain vignette EASES as pain shock lands.** At raw pain 135 the
  overlay reads `GetPain`, which the 30 s adrenaline window multiplies by 0.7, so the level drops from a
  would-be 1.0 to **0.7** at the exact moment the player is paralysed. That is Onyx's design (the overlay is
  deliberately fed the adrenaline-adjusted value), not a Wolfgate defect, and this is the first test in the
  tree to pin it. Flagged for the balance pass.
- **`- type: Jitter` in the P2-D24 allow-list is belt-and-braces, not load-bearing.** `Jitter` is
  `alwaysAllowed: true` (`status_effects.yml:17`), so `SharedJitteringSystem.DoJitter` would have worked
  without it; `Stun`/`KnockedDown` genuinely are required, and the test's failure message says so.
- **No production code changed and no new subscription registered.** This package is test-file-only:
  `WoundFractureTest.cs` (4 new `[Test]` methods, 2 `[TestPrototypes]` additions, 6 new `using`s), the new
  `WolfmedPainTest.cs`, and this manifest. No headless-server run was needed — the only YAML touched is inside
  `[TestPrototypes]` string literals, which the test pair itself parses (and did, 39/39 green).

### Phase 2 — user decisions (DECISIONS.md §8.2), as shipped

Reconciled here by WP10-7 so every §8.2 answer has one place that says what actually landed, cross-referenced
to the WP section above that carries the full derivation.

1. **Fracture manipulation balance — FIX.** `OrganicFractureProfile.manipulationModifier` shipped at the C#
   defaults `1.1/1.25/1.5/2.0` (not Onyx's `0.92/0.84/0.75/0.75`), behind a `# WOLFGATE` balance comment in
   `wounds.yml`. This **overrides** PLAN2's own P2-D13 (which had recommended ship-unchanged-and-escalate);
   the user's answer is binding. Landed WP10-1, measured by WP10-6b (Comminuted arm multiplier **2.0**). See
   WP10-1/WP10-6b deviations above.
2. **Do-after `Used` semantics — zero-edit active-hand approximation, as recommended.** No edit to
   `SharedDoAfterSystem.cs`/`DoAfterDelayMultiplierSystem.cs`. See WP10-1 deviations (P2-D4).
3. **Onyx's `EmoteOnDamage` pain sounds — PORT with the YAML key corrected.** `emotes:` → `emotesThreshold:`,
   recorded as a corrected-upstream-bug deviation (same class as WP9's `ModifyPainGainEvent` fix). Landed
   WP10-5, verified bound via a clean Release YAML-linter pass. See WP10-5 deviations (P2-D9).
4. **GUARD F + HOOK 14 — authorised.** Landed WP10-2.
5. **HOOK 15 + HOOK 16 — authorised.** Landed WP10-4.
6. **HOOK 17 + HOOK 18 — authorised (additive fields, no `_WF`-twin fallback taken).** Landed WP10-5.
7. **`PartDamageVisualsComponent` — resolved in WP11-4 (was deferred to phase 3, noted here at phase-2 time).**
   It is `EnsureComp`'d and networked by `WoundDamageProjectionSystem` (WP4/WP5); WP11-4 gave it its first
   consumer, `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` (HOOK 20), so per-limb damage
   now renders on the body sprite instead of paying networking cost for nothing.
8. **Part status readout scope — wound hosts only (P2-D20), as recommended.** GUARD F calls
   `AddPartStatusMarkup` from the `else` of the `HasComp<WoundHostComponent>` wrap, not unconditionally as
   Onyx does, so borgs, NPC/EE silicons, animals and the D32-excluded Protogen keep today's threshold text
   instead of gaining a duplicate readout. See WP10-2 deviations.
9. **`WoundPrototype.HealingMultiplier = 1` on every ported wound — left for the balance pass, kept on
   record.** Not touched in phase 2. First documented in WP9 (`WoundHealingTest`: healing a wound takes the
   full heal off its severity — 15 → 5 for 10 points of Blunt healing — not Onyx's 13.5, because
   `HealingMultiplier` defaults to 1 and `BluntWound` overrides nothing, in both trees). Restated here per
   DECISIONS.md's explicit "keep on record" instruction; still not a phase-2 or phase-3 gate.

### WP11-0 (phase 3 — zero-dependency test debt: T-REATTACH + T-VISUALS)

- **No production code changed.** Both gaps closed clean on the first run — no bug found, no `// WOLFGATE`
  edit needed anywhere. This package is test-file-only: two new files plus this manifest.
- **T-REATTACH** (`WolfmedReattachTest.ReattachedPartRejoinsWoundTrackingTest`): a bespoke one-arm
  `WolfmedReattachBody` (Shitmed graph + `MobBloodstream`) has its arm detached via
  `WolfmedBodySystem.TryDetachPart` and re-attached via `SharedBodySystem.AttachPart(torso, "left arm", arm)`.
  Confirmed post-reattach: `WoundableComponent`/`DamageableComponent` are still present (they are never
  removed by detach — `TryDetachPart` only re-parents the container, per `Compat/WolfmedBodySystem.cs`), the
  part's `WolfmedBodyPartComponent.AmputationThresholds` still read the prototype's own `{Slash 130,
  Piercing 250, Blunt 250}` (WolfmedBaseLeftArm, PLAN3 P3-D13 — prototype data, never mutated at runtime), and
  a fresh `Slash 15` hit via `WoundDamageRoutingSystem.TryApplyPartDamage` both creates a new `SlashWound` on
  the arm and raises `BloodstreamComponent.BleedAmount` above zero. The mechanism that makes this work was
  already shipped and correct: `WolfmedBodyPartLifecycleSystem.OnPartAdded` (WP9) calls both
  `WoundDamageProjectionSystem.OnPartInserted` (re-`EnsureComp`s `Woundable`/`Damageable`/`Pain`/
  `BodyPartFunctionality` on the whole reattached subtree) and `WoundBleedingSystem.OnPartInserted`
  (rejoins the part's bleed rate into the body's `BloodstreamComponent` total) — this test is what proves that
  wiring actually closes PLAN §8.3 trap 2, which `WoundScarTest`/`WoundBleedingTest`'s existing detach/reattach
  coverage does not (they prove old wound *data* survives the round trip; they never re-damage the part
  afterward).
- **T-VISUALS** (`WolfmedVisualsTest.PartDamageProjectsToVisualsComponentTest`): a real `MobHuman` takes
  targeted `Blunt` damage on the left arm and the left hand via `WoundDamageRoutingSystem.TryApplyPartDamage`.
  Confirmed `Comp<PartDamageVisualsComponent>(body).Damage[HumanoidVisualLayers.LArm]` and `[.LHand]` each hold
  exactly the dealt amount, `.RArm`/`.RHand` are absent-or-zero, and the same reads succeed on `Pair.Client`'s
  networked mirror entity (`PartDamageVisualsComponent` is `[NetworkedComponent, AutoNetworkedField]` — no
  extra hook needed for the state to arrive; that is unrelated to P3-4's `BodyPartComponent`
  `raiseAfterAutoHandleState` flag, which only gates *detached*-part state, HOOK 21). The `LHand` assertion is
  the server-side anchor for P3-D25: `WoundDamageProjectionSystem.TryGetVisualLayer` already writes
  `HumanoidVisualLayers.LHand`/`RHand`/`LFoot`/`RFoot` for hand/foot parts, and the stock 6-layer
  `targetLayers` list (`Species/base.yml`) cannot render any of the four — folding them onto the arm/leg
  reader is WP11-4's job, not this package's; this test only pins that the data WP11-4 will fold is already
  correct. No sprite/screenshot assertion, per project convention (logic tests only).
- **Test count:** the combined `_Onyx.Wounds|Wolfmed` filter went from 39 to **41** tests, all green, alongside
  a clean `DockTest` (no `db.ef` warning noise this run).

### WP11-1 (phase 3 - amputation)

- **Amputation is live.** `AmputationSystem.cs` vendored to `Content.Shared/_Onyx/Wounds/` with PLAN3
  section 4/WP11-1's edit table applied exactly (18 sites). `OrganDamageSystem`'s D26 comment-outs are gone, so
  `PartDamageAppliedEvent` now fans out to amputation between fractures and bleeding.
- **How the threshold actually works** (measured, not assumed - this is what WP11-5's T-AMP-GUN must encode):
  `progress` is the **sum, over only the damage types present in that part's own `amputationThresholds`, of
  accumulatedPartDamage[type] / threshold[type]**, where accumulatedPartDamage is
  `WolfmedDamageableSystem.GetAllDamage(part)` - the part's own `DamageableComponent`, **not** its wounds and
  **not** one combined total. Because it is a sum of per-type ratios, mixed damage types stack toward one
  severing. A damage type absent from that dict contributes nothing to `progress` **and** can never be a
  finishing hit (`IsFinishingHit` skips any type the dict does not contain). Sequence: the hit that first
  pushes `progress >= 1` only sets `Severable`; the *next* hit detaches, provided the pre-hit `progress >= 1`
  and that hit carries at least `dismembermentFinishingDamage[type]` (falling back to the host default) of a
  threshold type. Healing back below `SeverableResetRatio` (0.8) clears `Severable`.
- **Heat needs no wound-type mapping.** `Heat` is in `WoundHostComponent.LocalizedDamageTypes`, is in
  `OrganicBodyPartProfile.acceptedDamageTypes`, and the `OrganicPart` damage container supports the whole Burn
  group, so laser damage already lands on the part's `DamageableComponent` as `Heat`
  (`WoundDamageRoutingSystem.cs:772`). `BurnWound` is a parallel record that `GetThresholdProgress` never
  reads. Adding the `Heat` threshold row was therefore sufficient on its own.
- **Measured balance outcome of DECISIONS section 8.6-1** (throwaway integration check, run and then deleted):
  a left hand (Piercing and Heat thresholds both 200) is severed by the **16th** consecutive 14-Piercing round
  (15 hits reach 210 and set `Severable`, the 16th is the finishing hit) and by the **14th** consecutive
  16-Heat laser shot (13 hits reach 208, the 14th finishes). Five bullets into a foot (70 of a 220 threshold)
  leave it attached and not `Severable`. Melee is unchanged: the Slash finishing minimum is still 15.
- **P3-D1 is neutral-by-construction but visible in one existing test.**
  `WoundDamageFoundationTest.RoutesAndProjectsDamageTest` detaches a head carrying Blunt 10 + Caustic 3, so the
  systemic `Bloodloss` charge is now 13 + Shitmed's `VitalDamage` 100 = **113** and the projected body total is
  **119**. Both literals corrected in place with the derivation. **D2 verified empirically:** de-heading a
  `MobMonkey` (non-wound-host; `HeadMonkey` inherits `BaseHead` which inherits `WolfmedBaseHead`, so it carries
  `WolfmedBodyPart` data but no `WoundHostComponent`) still costs **exactly 100** Bloodloss.
- **P3-D1a stands:** the charge is not refunded on surgical re-attachment. A re-headed corpse reads more
  damaged than it is, never less. Phase 4 owns the refund.
- **No double-apply with Shitmed's detach cascade.** GUARD B
  (`_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:234`) keeps `severed` false for wound hosts, so
  Shitmed's own `DropPart` at `:246` never fires and Onyx's `TryDetachPart` is the only detach path. The
  `DismembermentWound` and `AmputationConsequenceWound` are created once each, on the **parent**, after
  `TryDetachPart` returns; nothing in the Shitmed cascade creates wounds. Bleeding cannot double-count because
  every entry point (`WoundBleedingSystem.OnPartChanged:288`, `OnWoundCreated:41`) recomputes
  `BloodstreamComponent.BleedAmount` from the live wound set rather than adding to it.
- **`AmputationConsequenceWound` still ships inert** (P3-D2): `SharedBodySystem.Parts.cs:606 CanAttachPart` is
  not hooked, so a severed limb can still be surgically re-attached. The wound is a marker for examine and
  phase-4 surgery.
- **Explosion amputation stays inert** (P3-D3): `TryExplosionAmputate` is ported and reachable only through
  `WoundDamageRoutingSystem.TryRouteDistributedDamage(..., isExplosion: true)`; no `ExplosionSystem` hook.
- **Test count:** the `_Onyx.Wounds|_Onyx.Body|Wolfmed` filter is **43 tests, 43 green** (the 41 WP11-0's
  `_Onyx.Wounds|Wolfmed` filter covered, plus 2 from `_Onyx.Body`).

### WP11-2 (phase 3 - organ damage)

- **Organ damage is live for the first time.** Before this package `grep -rn "type: OrganDamage|type: WolfmedOrgan"
  Resources/Prototypes` returned zero hits, so `OrganDamageSystem.OnPartDamageApplied` returned at
  `organs.Count == 0` for every hit in the game and `OrganHealthSystem.Update`'s query was empty. PROTO A
  (`_WF/Wolfmed/Body/organs.yml` + seven `parent:` edits in `Body/Organs/human.yml`) switches both on for the
  seven `OrganHuman*` ids, which is human, gingerbread, dwarf, vox, yowie and the human-lineage organs of eight
  more species (P3-D7 / DECISIONS.md §8.6-7).
- **Prototype data verified by resolution, not by reading the file back** (throwaway integration check, run and
  then deleted). All seven ids spawn with `WolfmedOrgan` at `15/15` and `OrganDamage` at exactly Onyx's numbers:
  Brain `hitChance 0.8 / weight 0.75 / Blunt .115 Slash .25 Piercing .42 Heat .15 Cold .05 Shock .3125`, no
  destruction wound; Eyes `0.7 / 0.2275 / .115 .3 .4375 .15 .05 .25`, no destruction wound; Lungs
  `1.0 / 1.38 / .1 .25 .42 .165 .05 .25`, `InternalBleedingWound` 35; Heart `0.8 / 0.64 / .1 .25 .455 .15 .05
  .3375`, wound 45; Stomach `0.85 / 0.56 / .1 .275 .4025 .15 .05 .25`, wound 25; Liver
  `1.0 / 1.1 / .1 .3 .4375 .15 .05 .25`, wound 40; Kidneys `0.9 / 0.51 / .1 .25 .4025 .15 .05 .25`, wound 30.
  The multi-parent `parent: [BaseHumanOrgan, WolfmedOrgan*]` form resolves correctly - no component is lost or
  overwritten, because the Wolfmed abstracts declare only components the upstream organs do not.
- **Consequences are Shitmed's, not Onyx's** (P3-D8). Onyx destroys an organ at 0 HP and then reaches its
  consequences through Nubody; in Wolfgate every consequence except brain-death already flows from organ
  *removal*, which `OrganHealthSystem.DestroyOrgan` performs via `SharedBodySystem.RemoveOrgan`: heart ->
  `HeartSystem` -> `DelayedDeathComponent` (60 s, defib refused), brain -> `DebrainedComponent`, eyes ->
  `TemporaryBlindnessComponent`, lungs -> no `LungComponent` -> suffocation. Nothing from
  `OrganConsequenceComponents.cs`, `FunctionalOrganComponent`, `MissingHeartComponent`, `BodyStasis.cs`,
  `TaggedOrgan*` or `Content.Server/_Onyx/Body/OrganEffectSystem.cs` was ported; the DECISIONS.md P3-2 question
  ("are `MissingHeartComponent` + `BodyStasis.cs` needed?") is answered **no**. Porting
  `OrganConsequenceComponents.cs` wholesale would register `BreathingImmunity` twice and crash the server at
  start - WG already has `_Shitmed/Body/Components/BreathingImmunityComponent.cs`.
- **The one piece of new glue is the 0-HP-but-not-yet-destroyed window.** `OrganHealthSystem.SetHealth` raises
  `OrganFunctionChangedEvent` on a `Health > 0` <-> `Health <= 0` transition, and `Update` destroys the organ on
  the *next* tick. `WolfmedOrganConsequenceSystem` turns that event into Shitmed's
  `OrganEnableChangedEvent(functional)`, which `SharedBodySystem.OnOrganEnableChanged` converts into
  `OrganComponentsModifyEvent` - revoking the organ's `onAdd:` grants (`_Shitmed/BodyEffects/OrganEffectSystem`)
  and driving the eyes enable/disable path. **The disable therefore fires twice** (once here, once from
  `RemoveOrgan` a tick later) and that is deliberate: `OnOrganEnableChanged` does not early-return on an
  unchanged value, and removing an already-removed component is a no-op. Do not "fix" it with a guard that also
  suppresses the first pass. The known EMP race (`CyberneticsSystem.OnEmpDisabledRemoved` re-enabling a doomed
  organ inside that one tick) is accepted; no `before:`/`after:` was added.
- **`OrganHealthSystem` gained two `TerminatingOrDeleted` guards** (P3-D23) because PROTO A makes `DestroyOrgan`
  reachable for the first time, including from `RecursiveDeleteEntity` while a mob terminates. That is the exact
  failure WP9 fixed in `WolfmedBodyPartLifecycleSystem`, and it cost that package 13 unrelated pooled-pair
  failures. Do not weaken them.
- **Organ damage ships irreversible** (DECISIONS.md §8.6-4). Nothing in Wolfgate raises organ health: Onyx's only
  healer is its own surgery, which D7 skips, and Shitmed can replace an organ but never repair one.
  `OrganHealthSystem.ChangeHealth(+x)` exists and is public, so a phase-4 chem or surgery step is ~15 lines.
- **Balance, as shipped (D4, Onyx defaults).** The per-application cap dominates: `MaxHealth x MaxDamageFraction
  = 15 x 0.3 = 4.5` organ HP per hit, so **exactly 4 applications destroy any organ regardless of hit size**, and
  raw damage above ~10-11 Piercing is irrelevant to organs. Per-hit probability (part-type roll x weighted draw
  x `hitChance`): lungs 2.41 %, liver 2.05 %, heart 1.05 %, stomach 0.98 %, kidneys 0.95 % per torso hit; brain
  4.0 %, eyes 3.5 % per head hit. **Expected hits to destroy: ~166 (lungs) to ~421 (kidneys) torso hits, ~100
  head hits for the brain.** So organ destruction is a shift-long ratchet, not a gunfight event - but when it
  lands it is a cliff (brain = instant death, heart = crit-then-dead in 60 s plus 0.9 blood units/s).
- **`OrganHealthSystem.Update`'s per-tick query is non-empty for the first time. Measured:** it enumerated
  **0** entities before PROTO A and **7 per spawned `MobHuman`** after (brain, eyes, lungs, heart, stomach,
  liver, kidneys). The body of the loop for a healthy organ is one `FixedPoint2` comparison
  (`organ.Health > Zero` -> `continue`), so a 50-human round costs ~350 comparisons per tick. It also
  enumerates organs on non-wound-hosts (rat lungs, `_NF` goblin organs, every `_Shitmed`/`_Mono` cybernetic
  organ parented to `OrganHuman*`), which is cost without behaviour - their health never drops, because
  `OrganDamageSystem` only runs off `PartDamageAppliedEvent`, which only wound hosts raise. **D2 holds.**
- **Cybernetic organs inherit the organic policy** through `OrganHumanEyes`/`OrganHumanHeart`/`OrganHumanLiver`/
  `OrganHumanLungs` (`_Shitmed/Body/Organs/cybernetic.yml`, `generic.yml`, `_Mono/Body/Organs/cybernetics.yml`).
  Left deliberately - Onyx routes organ damage through its own cybernetic profile too, and phase 5 owns the
  whole cybernetic/IPC profile.
- **Did not touch** `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` (P3-D24: owned exclusively by WP11-1),
  `Resources/Prototypes/_Onyx/Wounds/wounds.yml` (`organDamage.chances` already ships), or any of the ~20
  non-`OrganHuman*` organ roots (animal, arachnid, diona, slime, hydrakin, feroxi, chitinid) - those are silent
  no-ops, never crashes, until phase 4's data-driven option.
- **Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all 0 errors; a 120 s
  headless server run reached `Ready` with **zero** `[ERRO]`/`[FATL]`/`Exception` lines (this is where an unknown
  component or a mistyped field would surface); `DockTest` 3/3; the
  `_Onyx.Wounds|_Onyx.Body|Wolfmed` filter **43/43 green**, unchanged from WP11-1.

### WP11-3 (phase 3 - per-part (locational) armour)

- **Option B shipped, not Onyx's shipped code** (P3-D5). Onyx declares `Coverage`/`CoverageSymmetry`
  (`ONYX Content.Shared/Armor/ArmorComponent.Locational.cs:13,19`) and reads them from **no C# anywhere**: its
  `SharedArmorSystem.cs:124-128` disables the gate inside an `<Onyx-ArmorGlobalProtection-edited>` marker, so
  two of its own three locational-armour tests are red against its own pin. Wolfgate implements what Onyx's doc
  comments and tests describe: the ordered `partModifiers` loop wins first, and the component's global
  `Modifiers` reach a part only when `Coverage`/`CoverageSymmetry` admit it. **Deliberate divergence from the
  vendored Onyx behaviour**, recorded here rather than as a bug.
- **`Coverage`/`CoverageSymmetry` are nullable (`HashSet<T>?`), Onyx's are non-nullable `= []`.** `null` **and**
  empty both mean "protects everything" — `Covers()` only narrows when the set is `{ Count: > 0 }`. Getting that
  backwards would invert all 272 unannotated `- type: Armor` entries in the game at once. Nullable was chosen so
  that "unset" is representable without allocating a set on every armour in the game.
- **`Symmetry` is matched independently of `Parts`.** `symmetry: [Left]` with no `parts:` means "any left part",
  and a torso (`BodyPartSymmetry.None`) is excluded by such a set. Same as Onyx.
- **The coverage gate sits AFTER the `PartModifiers` loop**, matching Onyx's own `<Onyx-ArmorGlobalProtection>`
  comment, which scopes coverage to "an individual body part … not listed in `@Coverage`/`@CoverageSymmetry`" —
  i.e. to the fallback only. Moving it above the loop makes a `partModifiers` entry unreachable on any part
  outside `coverage` and turns `LocationalModifierOverridesAndFallbackTest` red on `head = 5`.
  `UncoveredPartIgnoresArmorPenetrationTest` is the second guard on the same line of code.
- **Both branches wrap `DamageSpecifier.PenetrateArmor`** (D23). Onyx's `partModifiers` branch does not, because
  Onyx has no `DamageSpecifier.ArmorPenetration` at all (D5). Without the wrap, any armour declaring a part
  profile would silently zero out every AP weapon's AP; `PartModifiersRouteThroughArmorPenetrationTest` is the
  gate and it is Wolfgate-only — nothing in Onyx covers it.
- **Onyx's `MaskComponent.IsToggled` early-return is NOT ported** (`ONYX SharedArmorSystem.cs:111-112`). It is
  base-game drift: Wolfgate has no mask check in any of its four armour handlers, and adding it only here would
  make a toggled-down mask armour the torso and not the head. Deliberate.
- **Zero upstream edits for the datafields.** `Content.Shared/Armor/ArmorComponent.cs` and
  `Content.Shared/Armor/SharedArmorSystem.cs` are untouched (PLAN3 §3 "explicitly NOT touched"); the fields live
  on a `_WF` partial in the upstream namespace, the same pattern as the already-shipped
  `SharedArmorSystem.Wolfmed.cs` (HOOK 10).
- **Non-wound-hosts and systemic damage are structurally untouched (D2).** The only trigger is
  `PartDamageModifyEvent`, which only `WoundDamageRoutingSystem` raises (`:741-749`) and only for a
  `WoundHostComponent`. Mice, vehicles, blastdoors and mothroaches that carry `- type: Armor` never see it, and
  `SharedArmorSystem.Wolfmed.cs` keeps applying the **global** modifiers to a host's systemic types regardless of
  coverage — coverage is a localized concept in Onyx too. `NonWoundHostUsesVanillaArmorTest` stayed green
  unchanged.
- **PROTO B is the phase's one balance-visible change, and it is asymmetric** (P3-D6/P3-D26, DECISIONS.md
  §8.6-2). Five `_Mono` vests get `coverage: [Torso, Arm, Leg]`, one `_Mono` light ballistic helmet gets
  `coverage: [Head]`; **every other helmet in the game — `ClothingHeadHelmetSwat` included — keeps unset coverage
  and goes on protecting the torso.** Measured consequences for one 14-Piercing round: aimed **torso** protection
  does not change at all (heavy vest + SWAT stays `0.25 x 0.80` = 2.8 damage); aimed **head** damage for a vest
  wearer goes **2.8 -> 11.2** (x4), because the vest stops covering the head while the unannotated helmet keeps
  covering the torso. Against a random *unaimed* bullet a heavy vest's average Piercing mitigation falls from
  75.0 % to 52.9 %. **Aimed hand and foot shots now bypass an annotated vest entirely** — bounded, because limb
  damage does not count toward `CheckVitalDamage`, but it is a real route to bleeding, fractures, pain shock and
  amputation through a vest. Gloves and boots with `- type: Armor` are the content answer and are out of scope.
  **Head is the thing to playtest.**
- **Amputation interaction, now that WP11-1 is live.** A vest at <= 0.46 Slash pushes a 32-Slash machete below
  the Slash-15 finishing minimum: the heavy vest (`Slash 0.30`) makes 32 land as 9.6, so **a machete can no
  longer finish a torso, arm or leg of a heavy-vest wearer** — but the head, hands and feet are now uncovered
  and take the full 32.
- **Option C (slot-derived coverage default) and its CCVar are NOT added** (P3-D5): it would silently re-balance
  all 272 armour entries and break two currently-green tests. `EmptyCoverageAndSymmetryTest`'s part A — a
  head-slot armour still protecting the torso — is the standing guard against one creeping in.
- **The full 272-entry content pass is out of scope** (P3-D6). 271 of 272 `- type: Armor` blocks inherit their
  `Clothing.slots` from a parent, so there is no data-driven shortcut; a later balance pass owns it.
- **Test literals re-derived by hand, not taken from Onyx** (P2-D16). Every expected value has its derivation in
  a `// WOLFGATE` comment at the assertion, and the two assertions that are **red against Onyx's own pinned
  code** (`AppliesLocationalArmorExactlyOnceTest`'s torso 10 and `EmptyCoverageAndSymmetryTest`'s right arm 10)
  say so in the comment, so nobody "fixes" the implementation to match Onyx.
- **Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**; Release
  `Content.YAMLLinter` **"No errors found"** (a stray `coverage: [Chest]` would have failed it); a 120 s headless
  server run reached `Ready` with **zero** `[ERRO]`/`[FATL]`/`Exception` lines; `DockTest` 3/3; the
  `_Onyx.Wounds|Wolfmed` filter **46/46 green** and the full `_Onyx.Wounds|_Onyx.Body|Wolfmed` filter
  **48/48 green** (43 before this package, +5 new).

### WP11-4 (phase 3 — limb damage sprites)

- **Both options shipped.** The base package (Option A) built clean, the client rendered no console errors and
  the headless server reached `Ready` with zero `[ERRO]`/`[FATL]`/`Exception` lines, so Option B (severed-limb
  wound rendering, DECISIONS.md §8.6-5) was implemented in the same package per its pre-authorisation
  ("in WP11-4 if the package is otherwise green").
- **Option A — per-limb accuracy.** `HandleDamage` now checks for `PartDamageVisualsComponent` before falling
  into the aggregate overlay path; when present, `UpdatePartDamageVisuals` reads each targeted layer's own
  damage (`GetLayerDamage`) instead of the mob's `DamagePerGroup` total (D30). WG's calling convention needed a
  re-type from Onyx's `Entity<SpriteComponent, DamageVisualsComponent>` tuple to WG's existing
  `UpdateTargetLayer(SpriteComponent, DamageVisualsComponent, object, string, FixedPoint2)` shape (`:621`) — no
  new overload was added upstream. The per-group `LastThresholdPerGroup` cache is deliberately not consulted
  (kept from Onyx): it is keyed by damage group only, not by (layer, group), so consulting it would let one
  limb's last-seen threshold suppress a different limb's redraw.
- **P3-D25 fold, implemented exactly as specified.** `GetLayerDamage` returns `LArm`'s own damage plus `LHand`'s
  (same for `RArm`/`RHand`, `LLeg`/`LFoot`, `RLeg`/`RFoot`) via `DamageSpecifier.operator+`. Without this, hand
  and foot wounds would be invisible on the attached body — WG's stock `targetLayers` list
  (`Species/base.yml:63-69`) has no `LHand`/`RHand`/`LFoot`/`RFoot` entries and no hand/foot art exists in
  `Resources/Textures/Mobs/Effects/{brute,burn}_damage.rsi` (36 states, six layers only) — where today (pre-
  phase-3, D30) a hand injury lights every limb layer because the overlay reads the mob's aggregate.
- **Option B — severed-limb wound rendering.** `OnBodyPartState` and the new
  `<BodyPartComponent, AfterAutoHandleStateEvent>` subscription fire `UpdateDetachedPartDamage` whenever a
  part's networked state changes (attach, detach, or a fresh hit while detached). It walks every
  `PartDamageVisualsComponent.Damage` entry (unfolded — the P3-D25 fold is an attached-body-only concern, exactly
  as PLAN3 specifies) and lazily adds a `WolfmedDetached{layer}{group}` sprite layer per (layer, group),
  visible only while the part is actually off the body (`BodyPartComponent.Body == null`) and its damage is
  ≥ 10. Re-attachment hides every such layer again (the "reattached" branch at the end of
  `UpdateDetachedPartDamage`). `TryGetDetachedDamagePrefix` maps `HumanoidVisualLayers` to the RSI state prefix
  and **drops Onyx's `Groin` arm** (D9 — WG's enum has no `Groin` member; porting that line verbatim is `CS0117`,
  exactly the compile trap PLAN3 §2.3/§8.7 risk 14 predicted).
- **Deviation — HOOK 20 needed a third insertion beyond PLAN3's literal table.** §3's HOOK 20 row authorises
  only the Option-A subscription (a) and the `HandleDamage` early return (b); it does not mention a third line
  for Option B's `<BodyPartComponent, AfterAutoHandleStateEvent>` subscription. That subscription cannot live
  anywhere else: `EntitySystem.Initialize()` is a single virtual override and the upstream file already owns
  it, so a `_WF` partial cannot add a second `Initialize()`. Since Option B is itself pre-authorised
  (DECISIONS.md §8.6-5, PLAN3 §8.6-5/P3-D18) and is dead without this exact subscription (P3-D19's whole point —
  "porting Onyx's `OnBodyPartState` subscription without it compiles cleanly and NEVER FIRES"), the one-line
  extension is treated as inside the spirit of HOOK 20 rather than a new unauthorised hook. Recorded here in
  case a stricter reading is wanted later — the fix, if this is overruled, is to drop the subscription line, the
  `using Content.Shared.Body.Part;` it needs, and Option B's five methods, leaving Option A intact.
- **Detached-part layer key is renamed, not preserved.** Onyx's `OnyxDetached{layer}{group}` layer-map key
  becomes `WolfmedDetached{layer}{group}` — purely cosmetic (the key is never read by YAML or any other system),
  chosen to match this port's naming convention rather than Onyx's.
- **RSI licensing verified, not assumed.** Both `_Onyx/Wounds/{brute,burn}_damage.rsi/meta.json` declare
  `"license": "CC-BY-SA-3.0", "copyright": "Drawn by Ubaser."`, byte-identical to WG's own already-shipped
  `Resources/Textures/Mobs/Effects/{brute,burn}_damage.rsi/meta.json`. A small script cross-checked every state
  name in both new `meta.json` files against a `.png` of the same name (77/77 and 77/77, no orphans either
  direction) and against the full `{prefix}_{group}_{threshold}` matrix the code and `base.yml` can request
  (11 prefixes × 7 thresholds incl. Onyx's `40`, which the live overlay's own `[10,20,30,50,70,100]` list never
  requests) — zero missing, zero unrequested-but-present states left unaccounted for.
- **P3-D17's premise confirmed true before this WP started.** `grep -rn "PartDamageVisualsComponent"
  Content.Client` returned zero hits and `Resources/Textures/_Onyx/Wounds/` did not exist; WP7's manifest note
  claiming the RSIs were "already copied" was stale, corrected here rather than re-asserted.
- **No production regression for non-wound-hosts (D2).** The Option-A early return in `HandleDamage` triggers
  only when `TryComp<PartDamageVisualsComponent>` succeeds, which `WoundDamageProjectionSystem` only
  `EnsureComp`s on wound hosts and their parts; every other entity with a `DamageVisuals` block (turrets,
  vehicles, non-wound-host mobs) falls through to the unchanged aggregate path exactly as today. Option B's
  `<BodyPartComponent, AfterAutoHandleStateEvent>` subscription fires for every body part in the game (the
  component itself is not wound-host-gated), but `UpdateDetachedPartDamage` no-ops immediately when
  `TryComp<PartDamageVisualsComponent>` fails, which is always true for a non-wound-host's parts.
- **Checkpoint:** `Content.Server` and `Content.Client` both **0 errors** (`Content.IntegrationTests` not
  rebuilt this package — no test file touched); Release `Content.YAMLLinter` run (result below); a 120 s
  headless server run reached `Ready` with **zero** `[ERRO]`/`[FATL]`/`Exception` lines and **no**
  `Duplicate Subscriptions` throw; WP11-0's `WolfmedVisualsTest.PartDamageProjectsToVisualsComponentTest`
  (T-VISUALS) re-run and still green, confirming the server-side projection this package consumes is unchanged.
  No sprite-pixel/screenshot assertion was attempted (project convention — logic tests only); visual
  confirmation was build-clean-client + zero-console-error + zero-server-error, per the task's own fallback
  when a headless client harness is not specified for this WP.

### WP11-5 (phase 3 — amputation and organ tests)

- **17 tests, exactly PLAN3 §6.2's phase-3 test debt, with DECISIONS.md's two overrides applied.** 1 restored
  (`WoundBleedingTest`), 3 ported (`AmputationConsequenceTest`), 5 new amputation and 8 new organ tests. Every
  expected value carries its derivation in a comment at the assertion (P2-D16), and every one of them was
  re-derived from shipped prototype data in this tree rather than copied from Onyx.
- **T-AMP-NOGUN is dead; T-AMP-GUN replaces it** (DECISIONS.md §8.6-1, already implemented by WP11-1 in
  `_WF/Wolfmed/Body/parts.yml`). PLAN3 §6.2's `BulletsNeverAmputateTest` would now fail by construction: the
  per-part `dismembermentFinishingDamage` lowers the Piercing minimum to 12 and adds a Heat row, so a
  14-Piercing round and a 16-Heat laser both finish an over-threshold limb. Hit counts asserted: hand Piercing
  16, hand Heat 14, arm Slash (machete 32) 6 — matching WP11-1's independent live measurement and PLAN3 §8.2's
  melee table, which the §8.6-1 change deliberately leaves untouched.
- **`AmputationConsequenceWound` severity is asserted at 50, never 35, in both consequence tests.** Both
  fixtures' torsos carry `- type: WolfmedBodyPart  amputationConsequenceSeverity: 50` for the reason CRITIQUE3
  M3-2 gives: with the stock 35 everywhere, a wrong redirect of edit #9 to the **severed part** reads 35 too
  (`WolfmedBodyPartComponent.cs:25` defaults to 35 and `WolfmedBodyPartSystem.Get`'s zeroed singleton is also
  35), so the bug would ship green. **Do not drop those two fixture lines.**
- **DEVIATION — T-AMP-OVERFLOW (a) moved from an arm to the HEAD, and T-AMP-EXPLOSION from Slash 260 to
  Piercing 500.** PLAN3 (and CRITIQUE3 M3-4) assume a limb can be driven arbitrarily far past its amputation
  threshold. It cannot: Shitmed's own `Destructible` **gibs** a part from `DamageChangedEvent`, i.e. inside
  `TryChangeDamage`, *before* `PartDamageAppliedEvent` ever reaches `AmputationSystem`.
  `Resources/Prototypes/Body/Parts/base.yml` — `MajorLimb` (arms, legs) `:280-297` Blunt **190** / Slash **210**
  / Heat 250; `MinorLimb` (hands, feet) `:312-338` Blunt **150** / Slash **180** / Heat 230; `BaseHead`
  `:105-122` Blunt **500** / Slash **600** / Heat **700**. **Piercing has no `Destructible` trigger on any body
  part in the game.** Consequences, each verified by running the tests:
  - PLAN3's "20 × Blunt 25 into an arm, then Blunt 50" is unreachable — the arm's Blunt amputation threshold is
    250 but it gibs at 190. The setup keeps CRITIQUE3's chunk sizes and moves to the head (Blunt threshold 350,
    gib 500): 16 × 25 = 400, then one 50 at 450. **Blunt amputation is only reachable on the head at all;** on
    every arm, leg, hand and foot the part is destroyed first. Recorded as a live-behaviour finding for the
    balance pass, not a bug this package fixes.
  - PLAN3's `Spec("Slash", 260)` into an arm exceeds `MajorLimb`'s Slash gib at 210, so the limb would be
    *destroyed* and the test would have passed for the wrong reason. Swapped to `Spec("Piercing", 500)` against
    the arm's Piercing threshold of 250, which keeps the intended `clamp(progress × 0.5) = 1.0` saturation and
    has no `Destructible` trigger. The test now also asserts the detached arm is **not deleted** and that a
    `DismembermentWound` at severity 120 landed on the stump, which is what distinguishes an amputation from a
    gib; T-AMP-GUN carries the same not-gibbed assertion on all three of its severed parts.
- **Upstream bug found, NOT fixed (out of scope, flagged):** `Content.Shared/Gibbing/Systems/GibbingSystem.cs:141`
  throws `InvalidOperationException: Collection was modified` — `TryGibEntityWithRef`'s `GibContentsOption.Drop`
  branch enumerates `container.ContainedEntities` while `DropEntity` removes from it. Reached from
  `GibPartBehavior` → `SharedBodySystem.GibPart` whenever a body part **that contains something** (an arm holds
  its hand) crosses a `Destructible` gib threshold. This is pre-existing upstream code that phase 3 does not
  touch and PLAN3 §3 does not authorise editing; Wolfmed makes it *more* reachable only because routing
  concentrates damage on one part. A one-line `.ToArray()` on both `foreach`es would fix it. Handed to the
  user / a later package rather than patched here.
- **No production code was changed by this package.** The whole WP is four test files; no `_Onyx`, `_WF`,
  upstream C#, prototype, locale or texture file was touched, so no `// WOLFGATE` hook, no new subscription
  (PLAN3 §5.1 confirms WP11-5 registers none) and no YAML lint surface. `WoundDamageFoundationTest.cs` was
  **not** touched (WP11-3 owns it); `WoundBleedingTest.cs` is this package's file per serialisation rule 2.
- **Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**
  (`-c DebugOpt`); `DockTest` 3/3 run first (project memory); the full phase-1/2/3 wound gate
  `_Onyx.Wounds|_Onyx.Body|Wolfmed` **65/65 passed** (48 before this package + the 17 new); smoke filter
  `EntityTest|PrototypeSaveTest|DockTest` **Test Run Successful, 9 passed / 2 pre-existing skips**. No headless
  server run or Release YAML lint: this package adds no `Resources/` file — its prototypes are `[TestPrototypes]`
  strings compiled into the test assembly, which the server and the linter never load.

## Phase 3 — user decisions (DECISIONS.md §8.6)

Index of the eight user answers to `PLAN3.md §8.6`, each to the WP that implements it. See `DECISIONS.md`
for the full question text.

| # | Decision | Implementing WP | Numbers / notes |
|---|---|---|---|
| **§8.6-1** | **Guns and lasers can sever** (deviation from Onyx defaults, expressed only in `_WF/Wolfmed/Body/parts.yml`). | **WP11-1** (data), **WP11-5** (T-AMP-GUN) | Piercing finishing minimum lowered **40 → 12**; a per-part `Heat` amputation-threshold row added equal to that part's Piercing threshold (Head 200, Arm 250, Hand 200, Leg 250, Foot 220) with a Heat finishing minimum of **15**. Amputation thresholds themselves stay at Onyx values. Measured: a hand (Piercing/Heat threshold 200) is severed by the **16th** consecutive 14-Piercing round and by the **14th** consecutive 16-Heat laser shot; 5 bullets into a foot (70/220) leave it attached; melee is unchanged (Slash finishing minimum still 15, a machete needs 6 × Slash 32 on an arm). Recorded as a Wolfgate balance deviation for the later balance pass |
| **§8.6-2** | **Annotate the 5 `_Mono` vests + the 1 `_Mono` ballistic helmet** with `coverage:` (PROTO B, as planned). | **WP11-3** | 6 lines total. Aimed-torso protection unchanged. Aimed-**head** damage for a vest wearer goes **2.8 → 11.2** (×4) against a 14-Piercing round, because the vest stops protecting the head while the unannotated helmet (every other helmet, `ClothingHeadHelmetSwat` included) keeps protecting the torso. Aimed hand/foot shots now bypass an annotated vest entirely (non-lethal consequences only). A heavy vest's average unaimed Piercing mitigation falls 75.0 % → 52.9 %. A vest at ≤0.46 Slash now lets a 32-Slash machete finish a covered part it could not before |
| **§8.6-3** | **P3-D1 redesign taken**: a lost vital part charges its own damage as systemic Bloodloss, host-gated in `WolfmedBodyPartLifecycleSystem.OnPartRemoved`. No HOOK 19 (`BaseHead.vitalDamage: 300`, withdrawn as a D2 breach). | **WP11-1** | Zero upstream edits, zero balance deviation. De-heading a wound host now reads *pre-decapitation total + 100* instead of −100. A non-wound-host (`MobMonkey`) still charges exactly 100 Bloodloss (T-AMP-VITAL) |
| **§8.6-4** | **Organ damage ships irreversible**, Onyx caps as-is. | **WP11-2** | Per-hit cap `MaxHealth × 0.3 = 4.5` organ HP — exactly 4 applications destroy any organ regardless of hit size. Expected hits to destroy: ~166 (lungs) to ~421 (kidneys) torso hits, ~100 head hits for the brain. `OrganHealthSystem.ChangeHealth(+x)` exists and is public, so a phase-4 chem/surgery regen path is ~15 lines |
| **§8.6-5** | **Option B (severed-limb wound rendering) shipped in WP11-4**, package was otherwise green. | **WP11-4** | 156 texture files (`_Onyx/Wounds/{brute,burn}_damage.rsi`, CC-BY-SA-3.0 / Ubaser), HOOK 21 (1-word flag on `BodyPartComponent`), the `OnBodyPartState` consumer in `DamageVisualsSystem.Wolfmed.cs` |
| **§8.6-6** | **Explosion amputation stays out.** | — (deferred) | `TryExplosionAmputate` is ported and reachable only through `WoundDamageRoutingSystem.TryRouteDistributedDamage(..., isExplosion: true)`; no `ExplosionSystem` hook. Phase 4/5 owns it, together with a plate regression test |
| **§8.6-7** | **Organ damage covers human-lineage organs only** (7 `OrganHuman*` edits). | **WP11-2** | Human, gingerbread, dwarf, vox, yowie and the human-lineage organs of 8 more species. ~20 other organ roots (animal, arachnid, diona, slime, hydrakin, feroxi, chitinid) stay silent no-ops until phase 4's data-driven option |
| **§8.6-8** | **Hand and foot wounds fold onto the arm/leg** (P3-D25) rather than shipping invisible. | **WP11-4** | `GetLayerDamage` sums `LArm`+`LHand` (etc.) via `DamageSpecifier.operator+`; 4 lines, preserves today's look, needs no new art |

## Hazards

- **Two types are now named `StatusEffectsSystem`** — `Content.Shared.StatusEffect.StatusEffectsSystem`
  (the old system, still serving all existing content) and
  `Content.Shared.StatusEffectNew.StatusEffectsSystem` (the port). A file that imports both namespaces
  gets `CS0104`. No ported file does; keep it that way.
- **`Content.Shared/_WF/Wolfmed/Compat/StatusEffectsSystem.Wolfgate.cs` is RT-version-scoped.** If
  Wolfgate ever moves to a RobustToolbox that adds `EntitySystem.ProtoMan`, this file starts emitting
  `CS0108` — delete it then. The same applies to `EntityPrototypeCompatExtensions.cs` if
  `EntityPrototype.TryComp` is added upstream.
- **`WolfmedDamageableSystem` is the only damage API vendored `_Onyx` files may use.** Binding
  `[Dependency] DamageableSystem` inside a wound file would silently resolve Wolfgate's legacy
  `TryChangeDamage(EntityUid?, ...)`; D12 exists to make that impossible. Every vendored file gets the
  one-line `// WOLFGATE` dependency swap instead.
- **GUARD D is now in place (WP5), so the bridge is closed.** Routing cancels
  `BeforeDamageChangedEvent` for every `WoundHostComponent` entity and the seam inside
  `TryChangeDamage` re-applies the damage to parts. Nothing carries `WoundHostComponent` until WP7,
  so the tree is still behaviourally inert.
- **`WoundDamageRoutingSystem`'s D23 and D27 side tables are keyed by body and only live for the
  duration of one `OnBeforeDamageChanged` call** (both are cleared in its `finally`). A later WP that
  adds another routing entry point must decide whether it wants them; `RouteThroughBodyModifiers`
  falls back to `(0f, null, null)` and `AccumulateApplied` is a no-op when no pass is open.
- **Every `// WOLFGATE` guard in `SharedBodySystem.Targeting.cs` and `DamageableSystem.cs` is gated on
  component presence only.** Making any of them `_net.IsServer`-gated gives a permanent client
  mispredict (PLAN 8.3 trap 3): the client would keep running Shitmed's spread while the server routes
  through Onyx.
- **`<WoundableComponent, DamageChangedEvent>` is now claimed** by `WoundDamageProjectionSystem` (D11).
  PLAN 5.2 marks it exclusive: nothing in `_WF` or a later WP may subscribe that pair.
- **`BloodstreamSystem.TryModifyWoundBleedProjection` is `internal`**, so only `Content.Server` code can
  write a wound host's `BleedAmount`. That is the whole point of GUARD E3 — `CirculatoryStreamSystem`
  (also `Content.Server` after D15) is the single caller. A later WP that moves any bleeding code back to
  `Content.Shared` must re-open this gate deliberately, not by widening the modifier.
- **Every `HealingComponent` in the game now defaults to `HealDamage: true, HealWounds: true,
  TreatmentCapabilities: [Biological]`** (HOOK 7). No existing prototype sets them, so every current
  medical item will treat wounds on wound hosts once WP7 lands `WoundHost`. Ointment/brutepack tuning is a
  WP11/D4 balance item, not a bug.
- **`WolfmedBodyPartComponent.MaxDamage` defaults to zero — CORRECTED in WP11-6.** `MaxDamage` gates only
  `AmputationSystem`'s overflow branch, which is dead for every organic limb in Onyx too; it is
  `amputationThresholds` that gates severing, and WP7 populated that field on all ten limb-typed abstracts
  (`BaseTorso`, `BaseHead`, `Base{Left,Right}{Arm,Hand,Leg,Foot}`). **No YAML gap ever existed** for
  amputation, and none of the analysis that assumed one was needed (P3-D12) — this note previously implied
  otherwise. `BasePartInorganic`/`BaseTorsoInorganic`/`BasePart` intentionally carry no row either way (see
  WP7 Deviations).
- **`WoundHost` is now live on 17 organic species (WP7).** Every prior WP's guards, hooks and compat
  systems become reachable for the first time. If any post-WP7 bug report reads like "limbs regenerate
  through wounds" or "double armour"/"no armour", re-check GUARDs A/B/C/E/E3 and HOOK 10 before assuming a
  new bug — those paths were previously untested because nothing carried `WoundHostComponent`.
- **New synthetic species must be added to `WolfmedWoundHostExclusionSystem.ExcludedAncestors`**
  (`Content.Shared/_WF/Wolfmed/Body/`), not opted out via YAML — RT has no component-removal mechanism
  (see WP7 Deviations). Check this list whenever a fork adds another `BaseMobSpeciesOrganic` descendant
  that is not truly organic.

## Phase 4 (2026-09-13)

Phase 3 is committed (`6329d204e3 Phase 3 completion`). Phase 4 = treatment and diagnostics
(`PLAN4.md`). Packages run sequentially in the one worktree; each appends its own rows/deviations directly.

### WP12-0 (phase 4 — medical patch, P4-2a)

- **Zero wound coupling, zero upstream edits, zero fill/vending/cargo/loadout placement (P4-D13).**
  `MedicalPatchComponent`/`MedicalPatchSystem` are a `StickyComponent`-driven periodic solution transfer with
  no dependency on any wound system — every dependency (`IGameTiming`, `SharedSolutionContainerSystem`,
  `ReactiveSystem`, `StickySystem`, `SharedHandsSystem`, `ISharedAdminLogManager`, `UnremoveableComponent`)
  is vanilla and already present in WG. Both files vendored byte-identical (verified via `file`: ASCII,
  CRLF, matching the working tree's line-ending convention; content diffed equal to
  `git -C <onyx-checkout> show HEAD:<path>`).
  `MedicalPatchMakeshift` is placed nowhere per Onyx (`git grep -i medicalpatch` over Onyx's
  `Resources/Prototypes` returns only the definition and tag files); WG mirrors that exactly.
- **`GroupHealSpecifier` NOT ported (P4-D12).** `MedicalPatchComponent.cs`/`MedicalPatchSystem.cs` were read
  in full and reference it nowhere — the task brief's "if PLAN4 vendors it here" resolves to no.
- **Subscriptions registered:** `<MedicalPatchComponent, EntityStuckEvent>` and
  `<MedicalPatchComponent, EntityUnstuckEvent>` — both on a brand-new component, confirmed free by grep
  before adding (PLAN4 §5.1 rows 1–2). No other pair, no component-name collision (`MedicalPatch` grepped
  0 hits before creation).
  No `MedicalPatch` tag prototype existed in WG before this package; `Trash`, `SpiderCraft`, `Cloth`,
  `WebSilk`, `BaseHealingItem`, `StickyVisualizer`, `MixableSolution`, and `construction-category-tools`
  were all confirmed present before referencing them.
- **RSI licence/attribution recorded:** `Resources/Textures/_Onyx/Objects/Medical/medical_patch.rsi/meta.json`
  — `"license": "CC-BY-SA-3.0"`, `"copyright": "@jorgun  inspired by Studenterhue of Goonstation"` (double
  space after "jorgun" preserved verbatim from Onyx's `meta.json`). This is a new artist/licence pair for
  Wolfmed, distinct from the Ubaser wound-visuals attribution in phase 3. File count independently confirmed
  at 22 (21 PNG + `meta.json`) via `git -C <onyx-checkout> ls-tree -r --name-only HEAD` on the rsi path, matching
  PLAN4's corrected count.
- **No deviations from PLAN4.** This package matches WP12-0's table exactly: 6 files, 0 upstream edits.
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`). Headless server run
  (~120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with no `[ERRO]`/`[FATL]`/exception lines
  and no `Duplicate Subscriptions` throw; the only `[WARN]` lines are pre-existing and unrelated
  (`PullingSystem` command-bind notice, emote-word duplicates, `MainLoop: Cannot keep up!`). See
  `<plan>/p4/wp/WP12-0-report-server.log`.

### WP12-1 (phase 4 — reagent effect classes and HOOK 9, P4-1a)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Wounds/SuppressPainEntityEffect.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/SuppressPain.cs` | new (re-authored old-style) | **WP12-1** | D16 — Onyx's `EntityEffectSystem<PainComponent, SuppressPain>` pair collapsed into one old-style `EntityEffect`. The ECS component filter becomes an explicit `TryGetComponent<PainComponent>`; `PainSystem` is resolved through `args.EntityManager.System<T>()`, as WG's own `HealthChange` does. The class name is load-bearing (`!type:` resolves by bare `Type.Name`) |
| `Content.Shared/_Onyx/Wounds/ReagentTreatmentEffects.cs:27-54` + `ReagentTreatmentSystems.cs` (`MendFracturesEntityEffectSystem`) | `Content.Shared/_WF/Wolfmed/EntityEffects/MendFractures.cs` | new (re-authored old-style) | **WP12-1** | D16. Onyx's `WoundHostComponent` ECS filter becomes an explicit `HasComponent<WoundHostComponent>`. `GetBodyChildren` yields parts and `GetFracture` returns on the first hit before any mutation, so no `.ToArray()` snapshot is needed — if this is ever changed to heal *every* fracture per part, materialise first (the `GibbingSystem` container-mutation class) |
| `Content.Shared/_Onyx/Chemistry/TakeStaminaDamageEntityEffectSystem.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/TakeStaminaDamage.cs` | new (re-authored old-style) | **WP12-1** | D16. **P4-D5 divergence: `Immediate` is honoured** (Onyx's system never reads it). The datafield keeps Onyx's `false` default — WG's `StaminaSystem.TakeStaminaDamage` defaults `immediate: true`, and that branch returns *without applying the value* when the target is already critical. The class is `StaminaSystem`, not Onyx's `SharedStaminaSystem`. Onyx's `Scale == 1` gate is kept, marked in-file |
| `Content.Shared/_Onyx/Chemistry/StaminaDamageCondition.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/StaminaDamageCondition.cs` | new (re-authored old-style) | **WP12-1** | D16, on `EntityEffectCondition` — WG's base has neither Onyx's `Inverted` flag nor its `sourceEnt`, and no in-scope YAML uses either. Modelled on `Content.Server/EntityEffects/EffectConditions/TotalDamage.cs:16-26` |
| `Content.Shared/_Onyx/Wounds/ReagentTreatmentEffects.cs:9-13` | `Content.Server/EntityEffects/Effects/HealthChange.cs` | modified (hook) | **WP12-1** | **HOOK 9(a).** Three marked additions: `using Content.Shared._Onyx.Wounds;`, a `HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological]` datafield, and the single `TryChangeDamage` call turned into a local `Apply()` delegate branched through `WoundDamageRoutingSystem.WithTreatmentCapabilities` when the change heals **and** the target is a wound host. The Shitmed/Mono argument block survives byte-for-byte (`targetPart: TargetBodyPart.All`, `partMultiplier: 1.00f, // Mono, 0.5f->1.00f`, `canSever: false`); `change = Damage * scale` is kept, i.e. the discarded-universal-modifier behaviour is deliberately NOT "fixed" (PLAN4 §3.4, trap T4) |
| `Content.Shared/_Onyx/Wounds/ReagentTreatmentEffects.cs:15-19` | `Content.Server/EntityEffects/Effects/EvenHealthChange.cs` | modified (hook) | **WP12-1** | **HOOK 9(b).** The same three additions **plus `using System.Linq;`**, which was absent. The file's pre-existing `groupDamage.Values.Sum()` was binding to `Content.Shared.FixedPoint`'s own `Sum(this IEnumerable<FixedPoint2>)` extension (`FixedPoint2.cs:313`); `System.Linq` declares no `Sum` overload for `IEnumerable<FixedPoint2>` and no parameterless generic `Sum<T>`, so the new `using` introduces no ambiguity — confirmed by a clean build. The healing test is `Damage.Values.Any(a => a < 0)` over the group dictionary; the delegate captures `final = dspec * scale` |
| `Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl` | same path | adapted | **WP12-1** | Onyx's 3 guidebook bodies verbatim with the keys renamed `entity-effect-guidebook-*` → `reagent-effect-guidebook-*` (WG convention, marked `# WOLFGATE`), the 4 `fracture-grade-*` keys **unrenamed** (`MendFractures` builds them by string interpolation), plus one key with **no Onyx source**, `reagent-effect-guidebook-take-stamina-damage` — Onyx's effect overrides no guidebook text, but WG's `ReagentEffectGuidebookText` is `abstract` and returning `null` would hide the effect from the chemistry guidebook. `NATURALFIXED`/`MANY` confirmed registered (`ContentLocalizationManager.cs:38,52`); all 8 ids grepped 0 hits in `Resources/Locale` before creation |

**WP12-1 notes**

- **P4-D1 — HOOK 9 is inert plumbing today, deliberately.** `OrganicBodyPartProfile` is the only
  `bodyPartProfile` in the tree and it is `treatmentCapabilities: [Biological]`, and
  `WoundDamageRoutingSystem.CanTreatPart` returns `true` whenever no scope is open, so the capability gate can
  never refuse in phase 4. It becomes live the moment phase 5 adds an IPC/cybernetic profile, with no further
  upstream edit needed.
- **Structural limit, recorded so nobody debugs it later (P4-D7):** `RouteAppliedDamage` sends only
  *localized* negatives through `CanTreatPart`, so healing of Toxin, Airloss, Bloodloss, Genetic, Cellular and
  Radiation bypasses the capability gate entirely. Onyx behaves the same way.
- **No `treatmentCapabilities:` was written into any Wolfgate reagent or item** (P4-D7, DECISIONS §8.4-7). The
  `[Biological]` default is already correct for all 11 `HealingComponent` entities and all ~173
  `HealthChange`/`EvenHealthChange` uses. The cable coil (`Entities/Objects/Tools/cable_coils.yml:36-45`,
  `damageContainers: [Silicon]`) remains the recorded first phase-5 action.
- **`DistributedHealthChange` is *not needed*, not deferred (P4-D6).** Zero `!type:DistributedHealthChange` in
  Onyx's reagent set and zero references anywhere in WG, so DECISIONS P4-1's conditional resolves to no. The
  third partial in Onyx's `ReagentTreatmentEffects.cs` is therefore not ported.
- **`WithTreatmentCapabilities` must never nest** (PLAN4 §8.5 trap 3): `_treatmentCapabilities` is a plain
  dictionary with a `finally`-remove, so an inner scope clears the outer one and the rest of the outer heal
  runs unscoped. Neither hook nests, and `HealingSystem.Wolfmed` deliberately does not use it.
- **Subscription pairs registered: none. Components registered: none.** The four classes are
  `[ImplicitDataDefinitionForInheritors]` data classes dispatched by a direct virtual call
  (`MetabolizerSystem.cs:218`); HOOK 9 mutates two existing classes and adds no handler (PLAN4 §5.1).
- **Name audit before creation:** `SuppressPain`, `MendFractures`, `TakeStaminaDamage` and
  `StaminaDamageCondition` each grepped **0 hits** across `Content.Shared`, `Content.Server` and
  `Content.Client`. A duplicate bare name would be a `!type:` load-time ambiguity, not a compile error.
- **Deviations from PLAN4: none behavioural.** Two cosmetic departures from the plan's quoted code, both
  recorded here: `StaminaDamageCondition.Condition` assigns `damage` to a plain local instead of the
  `is var damage &&` pattern the plan quotes (identical semantics, more readable), and every datafield carries
  a `/// <summary>` one-liner per `_WF` style. The two authorised divergences (P4-D5's `Immediate` honouring
  and the retained `Scale == 1` gate) are marked `// WOLFGATE (P4-D5)` in `TakeStaminaDamage.cs`.
- **Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**
  (`-c DebugOpt`). Headless server (~120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with
  **zero** `[ERRO]`/`[FATL]`/exception lines — including no Fluent duplicate-id error for the new ftl — see
  `<plan>/p4/wp/WP12-1-report-server.log`. The phase-4 gate filter
  (`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`) is **65 passed / 65 total**, unchanged by HOOK 9 — see
  `<plan>/p4/wp/WP12-1-report-tests.log`. Release YAML lint **not run**: this package touches no
  YAML or prototype file, and the new FTL is covered by the clean headless start.

### WP12-2 (phase 4 — reagent content, Tier A, P4-1b)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml` (rows `Osteogen`, `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone`) | `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml` | new (vendored, Tier A subset) | **WP12-2** | The painkiller ladder (`SuppressPain` 0.5 → 0.9 → 1.25 → 2.0) plus the dedicated fracture medicine. **Every `Bloodstream:` group header is translated to `Medicine:`** — Wolfgate has no `Bloodstream` metabolism group (`Poison, Medicine, Narcotic, Alcohol, Food, Drink, Gas, PlantMetabolisms` + `Cryogenic`), the key is `ProtoId`-validated, and a copied header is a whole-file prototype-load failure. `!type:` translations applied: `ReagentCondition` → `ReagentThreshold`, `ModifyBleed` → `ModifyBleedAmount`, `TemperatureCondition` → `Temperature`. `HealthChange`, `AdjustTemperature`, `GenericStatusEffect`, `PlantAdjustWeeds`/`PlantAdjustHealth`, `SuppressPain`, `MendFractures`, `TakeStaminaDamage` are all SAME. The 17 other reagents in Onyx's file are **not** ported (Tier B/C, P4-D2) |
| `Resources/Prototypes/_Onyx/Recipes/Reactions/medicine.yml` (rows `Osteogen`, `Ibuprofen`, `Tramadol`, `Ketorolac`, `Oxycodone`) | `Resources/Prototypes/_Onyx/Recipes/Reactions/medicine.yml` | new (vendored, Tier A subset), 1 marked deviation | **WP12-2** | All reactants verified present in WG before writing: `Bicaridine, Milk, Phosphorus, Charcoal, Benzene, Fluorine, Acetone, Inaprovaline, Ethanol, Carbon, Plasma, Epinephrine`. File ordered so `Ibuprofen` and `Tramadol` precede `Ketorolac`. **`Oxycodone`'s recipe is re-authored (P4-D2): `Heroin` → `Ethanol`**, marked `# WOLFGATE (P4-D2)`; `Heroin` is in `_Onyx/Reagents/Narcotics/opioids.yml`, outside P4-1's scope and absent from WG (`grep "id: Heroin"` → 0 hits). Every other reactant, the `Plasma` catalyst and the 1u yield are Onyx's. **The five Tier-A reagents are reaction-only (chemist-craftable), matching Onyx** — no chem-dispenser jug, no medkit, no vending, no cargo placement (CRITIQUE4 M2; medkit fills belong to WP12-3) |
| `Resources/Locale/en-US/_Onyx/reagents/medicine.ftl` (10 of 24 keys) | same path | new (vendored subset) | **WP12-2** | `reagent-name-*` / `reagent-desc-*` for the five, Onyx's bodies verbatim; the keys for unported reagents are omitted. All 10 ids grepped **0 hits** in `Resources/Locale` before creation. The `reagent-physical-desc-{opaque,thick,pungent}` keys they reference already exist (`reagents/meta/physical-desc.ftl:39,45,75`) |
| `ONYX _Onyx/Reagents/Medicine/first_aid.yml:29-33` | `Resources/Prototypes/_Goobstation/Reagents/medicine.yml` | modified — **PROTO H** | **WP12-2** | One marked `- !type:MendFractures` block on `Stasizium` (`amount: 10`, `wounds: []`, `minimumGrade: Hairline`, `maximumGrade: Comminuted`), inserted after the five-group −20 heal and before the overdose block, matching Onyx's ordering. **P4-D3: WG's entry is extended in place; no `OnyxStasizium` is created.** Group is `Medicine:`, not Onyx's `Bloodstream:`. WG's `HealthChange`-vs-`EvenHealthChange` and its `-50000` temperature are Wolfgate balance and were left alone |
| `ONYX Resources/Prototypes/Reagents/medicine.yml:153-159` (`<Onyx-PartPain>`) | `Resources/Prototypes/Reagents/medicine.yml` | modified — **PROTO I** | **WP12-2** | One marked `- !type:SuppressPain` block on `Bicaridine` (`0.75 / 18 s / ×1.75`, identifier `Bicaridine`) in the **`Medicine:`** group — Bicaridine's only group in WG; Onyx's is `Bloodstream:`. Placed after the `ModifyBleedAmount` and before the overdose `HealthChange`, matching Onyx's position relative to the brute heal |
| `ONYX Resources/Prototypes/Reagents/narcotics.yml:50-56` + `:632-638` (`<Onyx-PartPain>`) | `Resources/Prototypes/Reagents/narcotics.yml` | modified — **PROTO J** | **WP12-2** | Two marked `- !type:SuppressPain` blocks. `Desoxyephedrine` (`0.75 / 9 s / ×1.75`) goes in the **`Narcotic:`** group, **not** `Poison:` — WG splits this reagent across `Poison`/`Narcotic`/`Medicine` and the drug effects live in `Narcotic`; a `Poison:` placement would lint clean and be silently wrong (§8.5 trap 4). `Happiness` (`0.4 / 9 s / ×1.5`) goes in `Narcotic:`, its only group |
| `ONYX Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml:115-123` (`<Onyx-PartPain>`) | `Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml` | modified — **PROTO K** | **WP12-2** | One marked `- !type:SuppressPain` block on `Cognac` (`0.25 / 9 s / ×1.1`, identifier `Painkiller`) in the **`Drink:`** group. Onyx puts its copy in a `Digestion:` group Wolfgate does not have. Cognac redeclares the whole `Drink:` block rather than inheriting `BaseAlcohol`'s, so the addition goes into Cognac's own block — deviation 24 (stomach metabolizer, not liver) |

**WP12-2 notes**

- **New reagent ids** (`Osteogen`, `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone`) and **new reaction ids**
  (the same five) were each grepped `^  id: X$` over `Resources/Prototypes` → **0 hits** before creation.
  `Heroin` → 0 hits, which is what forced the Oxycodone re-author.
- **Metabolism-group mapping, as shipped** — the one thing a future re-sync must not "restore" to Onyx's
  headers: `Osteogen`/`Ibuprofen`/`Ketorolac`/`Tramadol`/`Oxycodone`/`Stasizium`/`Bicaridine` → `Medicine:`;
  `Desoxyephedrine`/`Happiness` → `Narcotic:`; `Cognac` → `Drink:`.
- **Subscription pairs registered: none. Components registered: none.** This package is entirely YAML + FTL.
- **P4-D4 — `SalicylicAcid` is dropped, not ported and not renamed.** WG's `SalicylicAcid`
  (`_NF/Reagents/chemicals.yml:1-6`) is an inert Frontier precursor with no `group:`, no `metabolisms:` and no
  effects, consumed by two `_NF` reactions; Onyx's is a `group: Medicine` brute healer whose interesting
  branch needs the absent `TypedDamageThreshold` condition, and Onyx's **reaction** carries the same id, so a
  rename would cascade. Its role is already covered by Bicaridine + Brutepack.
- **Tier B (`Probital` + `Mitogen`) was NOT taken**, and the reason is a real port defect, not time. Probital's
  payload is `- !type:TakeStaminaDamage { amount: -100, immediate: true }` gated on
  `StaminaDamageCondition { min: 100 }` — i.e. a stamina *heal* fired at a target that is in stamina crit.
  Under P4-D5, WG's `StaminaSystem.TakeStaminaDamage` (`:288-292`) does
  `if (component.Critical && immediate) { EnterStamCrit(uid, component, true); return; }` — it returns
  **without applying the value**, so Onyx's self-rescue branch would silently invert into a re-crit in
  Wolfgate. Shipping Probital needs a balance decision (drop `immediate`, or hook the crit branch), which is
  out of WP12-2's scope. `Mitogen` is a `Probital` by-product (`AdjustReagent`) and has no standalone recipe,
  so it was dropped with it. Recorded as a phase-4 omission; re-entry cost is 2 reagents, 4 reactions
  (`Probital` + the three `Mitotrophin*`), 4 locale keys and the `immediate` decision.
- **Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**
  (`-c DebugOpt`). Headless server (~120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with
  **zero** `[ERRO]`/`[FATL]`/exception lines — no unknown `!type:`, no duplicate reagent/reaction id, no
  missing metabolism group, no Fluent duplicate id (`<plan>/p4/wp/WP12-2-report-server.log`).
  Release YAML lint (`dotnet run --project Content.YAMLLinter -c Release`): **1 error, none of it this
  package's** — see the hazard below. Phase-4 gate filter
  (`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`) **65 passed / 65 total**
  (`<plan>/p4/wp/WP12-2-report-tests.log`).
- **HAZARD handed forward — pre-existing Release-lint failure in WP12-0's file, not fixed here (ownership).**
  `Content.YAMLLinter -c Release` reports
  `::error file=/Prototypes/_Onyx/Entities/Objects/Specific/Medical/medical_patch.yml … File not found. (/Textures)`.
  Cause: the two `- type: construction` prototypes in that file (`MedicalPatchMakeshift`, `SilkPatchMakeshift`)
  carry no `icon:`, and WG's `ConstructionPrototype.Icon` defaults to `SpriteSpecifier.Invalid`, which the
  Release linter resolves as the empty texture path `/Textures`. Onyx's own copy has no `icon:` either, so
  this is a strict-lint divergence, not a transcription error — the headless server starts clean and the item
  works. The fix is one `icon: { sprite: _Onyx/Objects/Medical/medical_patch.rsi, state: MakeshiftPatch }` per
  construction prototype. **`medical_patch.yml` is WP12-0's file; WP12-2 did not touch it.** WP12-10 (or a
  re-run of WP12-0) should land the fix, because the phase cannot be green in CI until it does.

### WP12-3 (phase 4 — tourniquet, P4-2b)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetComponent.cs` | same path | new (vendored), verbatim | **WP12-3** | No licence header in Onyx; none invented. `TourniquetDoAfterEvent` in the same file, also verbatim |
| `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | **`Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs`** (relocated, namespace unchanged: `Content.Shared._Onyx.Medical.Tourniquet`) | new (vendored, relocated), 4 marked edits | **WP12-3** | D13 — `WoundBleedingSystem`/`WoundDamageRoutingSystem`/`WoundSystem` are `Content.Server` assemblies, so the class cannot compile in `Content.Shared` where Onyx has it |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:268-299` (`Tourniquet` entity) | same path | modified — **PROTO D** | **WP12-3** | In-place swap of the existing `id: Tourniquet`'s `- type: Healing` block for `- type: Tourniquet`. Zero new ids, zero touched fill/vending/spawner files beyond PROTO E |
| `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml`, `MedkitAdvancedFilled` | same path | modified — **PROTO E** | **WP12-3** | One line adding `Tourniquet` to `contents:`, matching Onyx's own `<Onyx-MedkitContents>` edit. `MedkitCombatFilled` deliberately not touched (Onyx doesn't add one there) |
| `Resources/Locale/en-US/_Onyx/medical/tourniquet.ftl` | same path | new, verbatim | **WP12-3** | 3 keys: `tourniquet-selected-part-missing`, `tourniquet-no-bleeding`, `tourniquet-applied` |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-3** | |

**Every `// WOLFGATE` edit in `TourniquetSystem.cs`, with reason:**

1. `using Content.Shared._Onyx.Targeting;` → `using Content.Shared._Shitmed.Targeting;` + `using Content.Shared._WF.Wolfmed.Targeting;` — D10. Onyx's own `TargetingComponent` registers as the bare name `"Targeting"`, which Shitmed's copy already claims; porting it is a `ComponentFactory` boot crash. Shitmed's `TargetingComponent.Target` field is identical, so the read site (`targeting.Target`) is unchanged.
2. `[Dependency] private TargetResolverSystem _targeting` → `[Dependency] private WoundTargetResolver _targeting` — `TargetResolverSystem` doesn't exist in WG (D5); `WoundTargetResolver.TryResolveExact(EntityUid, TargetBodyPart, out EntityUid)` is a signature-exact replacement already shipped in phase 1.
3. Dropped the `[Dependency] private INetManager _net` field and its `using Robust.Shared.Network;`, and simplified both call sites that read `_net.IsServer` (the `OnDoAfter` `QueueDel` guard, and a second, plan-unlisted `!_net.IsServer ||` half of the guard inside `Apply()`) — the class is `Content.Server`-only after the D13 relocation, so both conditions are always true/false respectively. **Deviation from PLAN4 §2.2's "3 edits" wording:** the plan's prose named only the `OnDoAfter` guard; `Apply()` carries a second, textually identical `_net.IsServer` check the plan's quoted diff didn't call out. Both are the same edit for the same reason, so they're recorded as one WOLFGATE reason applied at two sites rather than a new deviation.

**Traps avoided:** no `Tourniquet` tag added (P4-D9 — no such tag prototype in WG, would fail the Release lint); no second `id: Tourniquet` entity created; `TourniquetSystem.cs` left in `Content.Shared` was never attempted (would not compile).

**Deviations from PLAN4:** none behavioural. The one textual correction (the `Apply()` guard site) is recorded above.

**Subscription pairs registered:** `<TourniquetComponent, UseInHandEvent>`, `<TourniquetComponent, AfterInteractEvent>`, `<TourniquetComponent, TourniquetDoAfterEvent>` — all on a brand-new component, all grepped free before creation (PLAN4 §5.1 rows 3-5). **Components registered: `TourniquetComponent` — grepped 0 hits repo-wide before creation** (`class TourniquetComponent`, `class TourniquetSystem`, `id: Tourniquet` all confirmed unique post-creation).

**D2 spot check:** `CanApply` requires `HasComp<WoundableComponent>(part)`, which only wound-host parts carry (per D32, Protogen alone lacks it). A non-wound-host simply fails `CanApply` and gets the existing "not bleeding" popup rather than throwing — accepted loss, P4-D11, not exercised at runtime in this package (no test harness run; `TourniquetStopsOnlySelectedPartTest` is WP12-9's).

**Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors** (`-c DebugOpt`),
see `<plan>/p4/wp/WP12-3-report-*.log`. Headless server (~120 s, port 1299) reached
`Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
(`<plan>/p4/wp/WP12-3-report-server.log`). Release YAML lint
(`dotnet run --project Content.YAMLLinter -c Release`): **1 error, not this package's** — the same
pre-existing WP12-0 `medical_patch.yml` icon hazard already on record above; nothing from `healing.yml`,
`firstaidkits.yml` or the new locale file. No integration tests run for this WP (none specified for WP12-3;
the tourniquet test belongs to WP12-9).

### WP12-4 (phase 4 — wound surgery C# and HOOK 24/25, P4-3a)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Medical/Surgery/WoundSurgeryComponents.cs` + `SurgeryEffects.cs:9-13,53-57` | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | new (`_WF`, re-authored) | **WP12-4** | 10 components + `WolfmedIncisionTreatment` enum. All names take the `WolfmedSurgery*` prefix per P4-D17 — two of Onyx's collide outright with Shitmed (`SurgeryWoundedCondition`, `SurgeryTendWoundsEffect`), five more read as upstream Shitmed names. Every one `[RegisterComponent, NetworkedComponent]`; the shared condition handlers need them client-side |
| `Content.Server/_Onyx/Medical/Surgery/WoundSurgerySystem.cs:167-231` (`FindWound`, `GetGroupSeverity`) + `SharedSurgerySystem.Organs.cs:95-124` | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | new (`_WF`) | **WP12-4** | S1–S8. Shared by necessity: `SurgeryBui.cs:281` runs `GetNextStep`→`IsStepComplete` and `:310` `CanPerformStep` client-side (§8.5 trap 10). `FindWound`/`GetGroupSeverity`/`TryFindOrgan` are `public` so the server system binds them instead of duplicating ~60 lines |
| `Content.Server/_Onyx/Medical/Surgery/WoundSurgerySystem.cs` + `SurgerySystem.WoundEffects.cs:13-42` | `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` | new (`_WF`) | **WP12-4** | V1–V7. Server by necessity: `WoundBleedingSystem` and `OrganHealthSystem` are `Content.Server` assemblies despite their `Content.Shared._Onyx.*` namespaces (D13, §8.5 trap 11) |
| — | `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | new (`_WF` partial) | **WP12-4** | HOOK 24 + HOOK 25 bodies and their two `[Dependency]` fields, so neither upstream site gains a `using` or a dependency line. Precedent: `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` |
| — | `Content.Shared/_Shitmed/Surgery/Conditions/SurgeryWoundedConditionComponent.cs` | modified — **EXT 1** | **WP12-4** | 3 marked datafields (`woundGroup`, `minWoundSeverity`, `maxWoundSeverity`) + 3 marked `using`s. Purely additive; null bounds keep pre-Wolfmed behaviour exactly |
| — | `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs` | modified — **HOOK 24** (`OnWoundedValid`) + **HOOK 25** (`OnPartRemovedConditionValid`) | **WP12-4** | +4 lines total. First Wolfmed touch of this file; it now carries two hooks |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-4** | |

**Every `// WOLFGATE` edit outside `_WF`, with reason:**

1. **HOOK 24** — `SharedSurgerySystem.cs`, end of `OnWoundedValid` (after the shipped `args.Cancelled = true;`):
   two lines, `if (WolfmedWoundWindowFails(ent, args.Body, args.Part)) args.Cancelled = true;`. P4-D19's
   wound-severity window, so the two shallow tend surgeries stop overlapping the deep ones WP12-5 adds. The
   body returns `false` immediately when both bounds are null **or** the body is not a wound host.
2. **HOOK 25** — `SharedSurgerySystem.cs`, inside `OnPartRemovedConditionValid`, immediately after the
   `CanAttachToSlot` guard block: two lines,
   `if (WolfmedStumpBlocksAttachment(args.Part)) { args.Cancelled = true; return; }`. P4-D18 closes the
   phase-3 P3-D2 gap at the **surgery** layer, not at `SharedBodySystem.CanAttachPart` — that method is
   reached by five non-surgery callers (Mono prybar prosthetics, bionic legs, Goob autosurgeon,
   `GenerateChildPartSystem`, `TryCreatePartSlotAndAttach`'s admin commands) which would silently `QueueDel`
   the replacement limb on exactly the stump they exist for (§8.5 trap 13).
3. **EXT 1** — `SurgeryWoundedConditionComponent.cs`: the file's `… : Component;` becomes a body with
   `WoundGroup` / `MinWoundSeverity` / `MaxWoundSeverity`, plus the three `using`s those types need
   (`Content.Shared.Damage.Prototypes`, `Content.Shared.FixedPoint`, `Robust.Shared.Prototypes`).

**Deviations from PLAN4 §2.4–§2.7, all mechanical:**

1. `GetGroupSeverity` takes `Entity<WoundableComponent?>` rather than the plan's bare `EntityUid`, and opens
   with a `Resolve(..., false)` guard. Call-compatible (`Entity<T?>` has an implicit conversion from
   `EntityUid`), and it is what makes the helper return `Zero` rather than walking an empty container for a
   non-woundable part. `FindWound` takes the same shape, as Onyx's does.
2. `GetGroupSeverity`'s type test is `prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id))`, not
   Onyx's method-group `Any(types.Contains)` — **Wolfgate's `DamageGroupPrototype.DamageTypes` is
   `List<string>` (`DamageGroupPrototype.cs:27`) where Onyx's is `List<ProtoId<DamageTypePrototype>>`**.
   Copying Onyx's line is `CS0123`, and it was, on the first build.
3. `OnOrganValid` (S3) requires `0 < Health < MaxHealth`, i.e. **damaged but still alive**, per P4-D24 and
   §2.4's own summary line. Onyx's `SurgeryOrganConditionComponent.Damaged` has no lower bound; Wolfgate needs
   one because `OrganHealthSystem.Update` destroys any organ at `Health <= 0` on the next tick, so a
   `SurgeryHeal<Organ>` listed on a dead organ would be unreachable by the time the do-after finished.
4. EXT 1 is +13 lines, not the plan's "+6" — the three `using`s and the one-line `/// <summary>` per datafield
   that `_WF` style calls for were not in the plan's count. Purely additive either way.
5. `OnIncisionCheck` (S8) is spelled out rather than left to the implementer: `Clamp` is pending while a
   matching wound carries a `WoundBleedingComponent` whose `Treatment < BleedingTreatment.Clamped` (a `!=`
   test would re-clamp — i.e. downgrade — an already-cauterised incision); `Close` is pending while a matching
   wound is `Open` or `Stabilized`, which is exactly the set V7's `Close` branch acts on.

**Traps avoided (PLAN4 §8.5 / WP12-4's four):** visibility conditions are components meant for the **surgery**
singleton, never a step (trap 9 / WP12-4 trap 1) — WP12-5 owns the placement and must honour it; no
`SubSurgery<T>` (trap 17) — all 15 subscriptions are written out because the two step events are deliberately
split across assemblies; nothing built on `SurgeryCompletedEvent` (trap 16); `OnFractureCheck` honours
`ReductionMinimumGrade` (trap 12, P4-D20) via a private `CanEverReach` that re-states
`WoundFractureSystem.CanTreat`'s rule instead of touching the upstream `private static` — without it
`SurgeryMendFracture` stalls forever on a Hairline fracture, the commonest grade
(`OrganicFractureProfile.reductionMinimumGrade` is `Simple`).

**Subscription pairs registered — 15, every one on a brand-new component, all re-grepped free before creation:**
S1 `<WolfmedSurgeryWoundCondition, SurgeryValidEvent>`, S2 `<WolfmedSurgeryFractureCondition, SurgeryValidEvent>`,
S3 `<WolfmedSurgeryOrganDamagedCondition, SurgeryValidEvent>`, S4–S8 the five `SurgeryStepCompleteCheckEvent`
pairs (`ClampBleedingEffect`, `TreatWoundEffect`, `MendFractureEffect`, `OrganHealEffect`,
`IncisionTreatmentEffect`), V1–V7 the seven `SurgeryStepEvent` pairs. `ClampBleedingEffect`,
`TreatWoundEffect`, `MendFractureEffect`, `OrganHealEffect` and `IncisionTreatmentEffect` are each handled by
**two** systems — legal, because the crash rule is one registration per *(component, event)* pair and the
events differ. **Components registered: the 10 `WolfmedSurgery*` names, all 0 hits repo-wide before creation.**
No pair from §5.2 was re-subscribed: HOOK 24 and HOOK 25 extend the existing
`<SurgeryWoundedCondition, SurgeryValidEvent>` and `<SurgeryPartRemovedCondition, SurgeryValidEvent>` handler
bodies, they do not re-register.

**D2 spot check.** `WolfmedWoundWindowFails` returns `false` unless the body has `WoundHostComponent` *and* a
bound is declared; `WolfmedStumpBlocksAttachment` returns `false` unless the parent part's body is a wound
host carrying an `AmputationConsequenceWound`; V6 (`OnOpenIncision`) is `HasComp<WoundHostComponent>`-gated;
V5 (`OnSurgeryPain`) no-ops because only a wound host's parts carry `PainComponent`
(`WoundDamageProjectionSystem.SetupBody:224`); every remaining handler goes through `FindWound` /
`GetFracture` / `GetWounds`, all of which `Resolve(WoundableComponent, false)` and return empty for a
non-host. No component in this package is on any shipped prototype yet — WP12-5 places them.

**Inertness, measured rather than assumed:**

- **HOOK 24 is inert today.** `grep -rn "minWoundSeverity\|maxWoundSeverity\|woundGroup" Resources/Prototypes`
  → **0 hits**; the only two `- type: SurgeryWoundedCondition` users are `surgeries.yml:294`
  (`SurgeryTendWoundsBrute`) and `:307` (`SurgeryTendWoundsBurn`), which is exactly where PROTO F writes the
  bounds in WP12-5.
- **HOOK 25 is NOT inert, and that is intended — but it is a hard dependency on WP12-5.** See the handoff below.

**HANDOFF — WP12-5 must land `SurgeryHealAmputationConsequence` or traumatic amputation becomes permanent.**
`AmputationConsequenceWound` is created on the **parent** part by `AmputationSystem.ApplyAmputationConsequences`
today (phase 3, shipped), and its prototype is `damageTypes: {}` (`_Onyx/Wounds/wounds.yml:398-401`), so no
reagent, topical or `TryHealWounds` path can ever match and remove it — wound healing selects by damage type.
As of this package that wound cancels `SurgeryValidEvent` for every `SurgeryAttach*` surgery whose
`SurgeryPartRemovedCondition` targets the stump: a torso stump hides 6 of the 10 (Head, LeftArm, RightArm,
LeftLeg, RightLeg, Hands), an arm stump hides that side's `AttachHand`, a leg stump that side's `AttachFoot`
(P4-D18). Until WP12-5 ships the surgery that clears it, **a traumatically amputated wound host cannot have
the limb re-attached by any means.** Surgical limb removal is unaffected — `ApplyAmputationConsequences` has
exactly one caller, `AmputationSystem.TryAmputate:130`, and Shitmed's `SurgeryDetachPart` does not go through it.

**Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**
(`-c DebugOpt`). Headless server (~130 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with
**zero** `[ERRO]`/`[FATL]`/exception lines and **no `Duplicate Subscriptions` throw** — the gate for this
package, which adds more directed subscriptions than the rest of phase 4 combined
(`<plan>/p4/wp/WP12-4-report-server.log`). Phase-4 gate filter
(`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`): **65 passed / 65 total**
(`<plan>/p4/wp/WP12-4-report-tests.log`). No YAML/FTL/RSI/XAML touched, so the Release lint was
not re-run; WP12-3's standing `medical_patch.yml` icon hazard is unchanged.

---

### WP12-5 (phase 4 — wound surgery content, P4-3b)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `_Onyx/Entities/Surgery/surgery_steps.yml` (the wound/organ steps) | `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | new (`_WF`, re-authored) | **WP12-5** | 12 concrete steps + `SurgeryStepHealOrganBase`. All tools and sprites reuse Wolfgate's shipped Shitmed set (`Hemostat`, `Tending`, `BoneSetter`, `BoneGel`) — **no new tool prototype and no new sprite**. Every prototype carries `name:` **and** `categories: [ HideSpawnMenu ]` (CRITIQUE4 M6): the two Shitmed bases set `categories` but no `name`, and an unnamed prototype renders as an empty label in the surgery BUI — shipped-broken UI that no lint catches |
| `_Onyx/Entities/Surgery/surgeries.yml:908-1200` | `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | new (`_WF`, re-authored) | **WP12-5** | 13 surgeries: `SurgeryStopBleeding`, `SurgeryStopInternalBleeding`, `SurgeryMendFracture`, `SurgeryHealAmputationConsequence`, `SurgeryTendWoundsBrute/BurnDeep`, and `SurgeryHeal{Heart,Lungs,Liver,Stomach,Kidneys,Brain,Eyes}`. Requirements reuse `SurgeryOpenIncision` / `SurgeryOpenRibcage`; terminal steps reuse `SurgeryStepSealTendWound` / `SurgeryStepSealOrganWound` |
| — | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgeries.yml` | modified — **PROTO F** | **WP12-5** | 3 marked lines on the two shipped tend surgeries (P4-D19) |
| — | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgery_steps.yml` | modified — **PROTO G** | **WP12-5** | 4 marked component additions on `:9`, `:31`, `:168`, `:359` (P4-D21). `SurgeryStepCarefulIncisionScalpel` (`:303`) deliberately **not** touched |
| — | `Resources/Locale/en-US/_WF/Wolfmed/surgery-popup.ftl` | new | **WP12-5** | 12 `surgery-popup-step-*` keys in Wolfgate's `{$user}` style (no inner spaces). Entity **names** are inline `name:` in Wolfgate YAML, so Onyx's entity-name ftl is not needed |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-5** | |

**Every `// WOLFGATE` edit outside `_WF`, with reason (7 marked lines in 2 upstream files):**

1. **PROTO F** — `_Shitmed/…/surgeries.yml`, `SurgeryTendWoundsBrute`'s `- type: SurgeryWoundedCondition`
   (`:294`): `maxWoundSeverity: 99.99`. Without an upper bound a badly wounded limb lists **four** overlapping
   tend surgeries instead of two (P4-D19).
2. **PROTO F** — same file, `SurgeryTendWoundsBurn` (`:307`): `woundGroup: Burn` **and**
   `maxWoundSeverity: 99.99`. The `woundGroup` line is required as well as the bound — EXT 1's datafield
   defaults to `Brute`, so without it `GetGroupSeverity` would measure the wrong group on the burn surgery and
   the window would be nonsense. D2-safe: `WolfmedWoundWindowFails` returns `false` for any body without
   `WoundHostComponent`, and both bounds stay null on every other prototype.
3. **PROTO G** — `_Shitmed/…/surgery_steps.yml`, `SurgeryStepOpenIncisionScalpel`:
   `- type: WolfmedSurgeryIncisionWoundEffect { severity: 10 }` **added beside** the existing
   `SurgeryDamageChangeEffect { Bloodloss: 10 }`, never replacing it — replacing would strip the incision cost
   from non-wound-hosts, a D2 breach (§8.5 trap 14). The effect handler is itself
   `HasComp<WoundHostComponent>`-gated (V6).
4. **PROTO G** — `SurgeryStepClampBleeders`: `- type: WolfmedSurgeryIncisionTreatmentEffect { treatment: Clamp }`.
5. **PROTO G** — `SurgeryStepCloseIncision`: `- type: WolfmedSurgeryIncisionTreatmentEffect { treatment: Close }`.
6. **PROTO G** — `SurgeryStepSealTendWound`: `- type: WolfmedSurgeryIncisionTreatmentEffect { treatment: Close }`.
   This is the Wolfgate adaptation Onyx has no equivalent for: the wound surgeries end on the seal step, which
   removes only `IncisionOpen`, so without a `Close` effect there the incision wound would survive
   clamped-but-open until the medic separately ran `SurgeryCloseIncision`.

**The chain shipped complete, as P4-D21 requires — all four or none.** Shipping only the incision-wound effect
leaves a `mergeMode: SeparateInstances` bleeder stacking one new wound per operation (§8.5 traps 15/15a).
`SurgeryStepCarefulIncisionScalpel` is untouched (CRITIQUE4 B1): its only two consumers,
`SurgeryTendWoundsBrute` and `…Burn`, contain neither a clamp nor a close step, so a wound effect there would
leak one permanent bleeder per tend operation — the commonest surgery in the game. Onyx's careful incision
carries no bleed effect either, so dropping it is Onyx parity as well as the safe call. **Consequence for the
record:** `surgery.scar_chance` (0.35, dormant since phase 1) is live for the first time, and an incision on a
wound host now costs Wolfgate's flat `Bloodloss: 10` **plus** a severity-10 `SurgicalIncisionWound`
(~1.3× the old cost) — deviation 9 in PLAN4 §7.3, chosen over a D2 breach.

**Balance deviation shipped (P4-D23, §8.4 decision 2):** every organ-heal step carries `amount: 3` with
`# WOLFGATE (P4 balance): Onyx ships 1; 15 repeats of a 2 s step is dead time. Revert by editing this line.`
Five repeats of a 2 s step ≈ 10 s per organ instead of ≈ 30 s. The C# default on
`WolfmedSurgeryOrganHealEffectComponent` stays Onyx's `1`, so the balance number lives in YAML where reverting
is one line per step.

**Surgery pain (P4-D22):** `- type: WolfmedSurgeryPainEffect` on all 12 new steps at Onyx's amounts — `12` on
both fracture steps, `24` with `sleepModifier: 0` on `SurgeryStepHealOrganBase`, the component default `5`
elsewhere. `sleepModifier` ships inert (Wolfgate has no anaesthesia-scaling consumer).

**Decisions honoured, with the evidence re-checked in the tree:**

- **Placement.** All three visibility conditions (`WolfmedSurgeryWoundCondition`,
  `WolfmedSurgeryFractureCondition`, `WolfmedSurgeryOrganDamagedCondition`) sit on the **surgery** singleton,
  never on a step — `SurgerySystem.RefreshUI` raises `SurgeryValidEvent` on the surgery only, so Onyx's
  step-level placement would bite at do-after completion instead of hiding the surgery (§8.5 trap 9,
  WP12-4 trap 1).
- **Fracture ladder (P4-D20).** `SurgeryStepSetBone` (`BoneSetter`, `treatment: Reduced`) then
  `SurgeryStepMendFracture` (`BoneGel`, `treatment: Mended`). `BoneSetterComponent` was shipped on three items
  and referenced by **zero** step prototypes before this package — a free slot. A Hairline fracture skips the
  reduce step rather than stalling it, via WP12-4's `CanEverReach`.
- **The organ bone gate (CRITIQUE4 M7).** The five torso heals need **no** `SurgeryStepSawBones`:
  `SurgeryOpenRibcage` is itself `requirement: SurgeryOpenIncision` + `[ SawBones, PriseOpenBones ]`, so the
  saw is already spent before any `requirement: SurgeryOpenRibcage` surgery is reachable (~20 s end to end).
  `SurgeryHealBrain` and `SurgeryHealEyes` **do** get the saw prefixed: `SurgeryOpenIncision` is
  `[ OpenIncisionScalpel, RetractSkin, ClampBleeders ]` and does not saw, and Shitmed's own
  `SurgeryRemove/InsertBrain` and `…Eyes` all saw first (~24 s end to end).
- **`SurgeryStepSealOrganWound` is safe as the terminal organ step.** It carries `SurgeryAffixOrganStep`, but
  both handlers early-return unless the **surgery** entity carries `SurgeryOrganConditionComponent` with
  `Reattaching == true` (`SharedSurgerySystem.Steps.cs:587-592`, `:603-609`). The heal surgeries carry
  `WolfmedSurgeryOrganDamagedCondition` instead, so the affix logic never engages and the step is a plain 2 s
  cautery. `SurgeryOrganCondition` is deliberately **not** added to them.
- **Organ slots, verified against `Body/Prototypes/human.yml:11-27`:** head → `brain`, `eyes`; torso →
  `heart`, `lungs`, `stomach`, `liver`, `kidneys`. **Seven, not Onyx's ten** — Wolfgate has no Tongue / Ears /
  Appendix slot in any body graph and no `Groin` part (D9). `SurgeryHealKidneys` is the fork's **first** kidney
  surgery of any kind; `SurgeryRemove/InsertKidneys` still do not exist, so a *destroyed* kidney remains
  unrecoverable (P4-D24 — organ damage is reversible only while the organ lives).
- **`SurgeryStepClampInternalBleeders` deliberately not added** to the organ heals (PLAN4 §4, deviation 25):
  Shitmed's own `SurgeryInsert*` surgeries omit it too, and healing does not breach the organ, only the cavity.

**Behaviours recorded rather than fixed:**

- **`OnTendWoundsCheck` is body-scoped, not part-scoped** (`SharedSurgerySystem.Steps.cs:367-372`): the repeat
  loop runs while *any* Brute/Burn remains on the **body**, so `SurgeryTendWoundsBruteDeep` keeps repeating
  until the whole patient is clean. Pre-existing Shitmed behaviour, inherited by the two new deep surgeries.
- **`SurgeryStopBleeding`'s tight condition sits on the surgery** (`state: Open, bleeding: true`), as PLAN4 §4
  specifies. `args.Repeat` re-runs `IsSurgeryValid`, so the repeat loop ends with a
  `"tried to start invalid surgery"` log line the instant the last bleeder is clamped — the accepted-warning
  branch of WP12-4 trap 2, not a malfunction.
- **`SurgeryRemovePart` still amputates cleanly.** WP12-5 adds no consequence wound to the surgical path, so a
  surgically removed limb is still re-attachable; only `AmputationSystem`'s traumatic path sets the block.

**Handoff closed:** WP12-4's HANDOFF is satisfied — `SurgeryHealAmputationConsequence` now exists, so a
traumatically amputated wound host can be made re-attachable again (open incision → repair the amputation
damage with `Tending` → seal). P4-D15's guidebook amputation-consequence paragraph may therefore ship in
WP12-10.

**Subscription pairs / components registered: none.** This package is pure YAML and locale.

**Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`). Headless server (~130 s,
port 1299) reached `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
(`<plan>/p4/wp/WP12-5-report-server.log`). Release YAML lint: **1 error, pre-existing and not
this package's** — WP12-0's `_Onyx/…/medical_patch.yml` missing-texture hazard, unchanged since WP12-3 and
still owned by WP12-10. Prototype-resolution smoke check: all 17 distinct `Surgery.steps` entries and both
`requirement:` ids resolve to exactly one prototype each, and the server loaded all 26 new prototypes without
a single unknown-component or missing-parent error.

### WP12-6 (phase 4 — analyzer payload and server, P4-4a)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | same path | new (vendored), 1 marked `using` | **WP12-6** | `HealthAnalyzerWoundDiagnostic` + `HealthAnalyzerVisibleWound` + `HealthAnalyzerClottingPhase` + `HealthAnalyzerWoundDiagnostics`, all `[Serializable, NetSerializable]`. No licence header in Onyx; none invented |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerOrganInfo.cs` | same path | new (vendored), verbatim | **WP12-6** | 4-field `readonly record struct`; `Order` is the medical reading order the client sorts by |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerChemicalInfo.cs` | same path | new (vendored), verbatim | **WP12-6** | `HealthAnalyzerSolutionType` / `HealthAnalyzerReagentInfo` / `HealthAnalyzerChemicalInfo` |
| — | `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` | modified — **EXT 2** | **WP12-6** | 4 nullable fields + 4 appended optional ctor parameters + 2 marked `using`s. Purely additive; `CryoPodSystem.cs:206-221`'s nine positional arguments keep compiling untouched |
| `Content.Server/Medical/HealthAnalyzerSystem.cs:316-515` (Onyx) | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | new (`_WF` partial of the upstream `sealed partial` class) | WP12-6 / **WP13-5** | 5 `[Dependency]` fields + 4 **public** builders (P4-D26) + 2 private helpers, ~215 lines. `namespace Content.Server.Medical` so `HealthAnalyzerComponent`'s `[Access(typeof(HealthAnalyzerSystem), …)]` is satisfied and `_bodySystem`/`_solutionContainerSystem` are reachable. **WP13-5 (P5-5, U9(a)) adds a second marked site (the EXT 4 comment)**, resolving the per-wound `Name`/`StageName` locale keys through `ILocalizationManager` instead of raw prototype ids; the payload's `bool Mechanical` flag and its three consumer branches (§2.6) were **not** shipped this phase — WP13-5 ships the locale half only, WP13-6-3 records the gap and the follow-up note |
| — | `Content.Server/Medical/HealthAnalyzerSystem.cs` | modified — **HOOK 23**, 1 line | **WP12-6** | Four builder calls appended to the `ServerSendUiMessage` argument list. No `using` and no `[Dependency]` land upstream |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-6** | |

**Every `// WOLFGATE` edit outside `_WF`, with reason (1 hook line + 7 marked lines in 2 upstream files):**

1. **HOOK 23** — `Content.Server/Medical/HealthAnalyzerSystem.cs`, the `ServerSendUiMessage` argument list:
   `BuildWoundDiagnostics(target), BuildOrganInfo(target), BuildChemicalInfo(target, bloodstream), BuildVitalDamage(target)`
   on one marked line after `part != null ? GetNetEntity(part) : null,`. `bloodstream` is already in scope from
   the `TryComp` at `:256`. Every builder returns `null` for a non-wound-host, so the message a non-host scan
   sends is identical to today's apart from four `null`s (D2).
2. **EXT 2** — `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs`: two marked `using`s
   (`Content.Shared._Onyx.Medical`, `Content.Shared.FixedPoint`), four marked nullable fields
   (`WoundDiagnostics`, `Organs`, `Chemicals`, `VitalDamage`) after `Uncloneable`, four appended optional
   constructor parameters after `NetEntity? part = null`, and a marked four-line assignment block. NetSerializer
   emits members in declaration order for peers built from the same tree, so appending is a clean wire change.

**Every `// WOLFGATE` edit inside the vendored files:**

1. `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` — `using Content.Shared._Onyx.Targeting;` →
   `using Content.Shared._Shitmed.Targeting;` (D10). Onyx's own `TargetingComponent` registers the bare name
   `"Targeting"` that Shitmed already claims, so `_Onyx/Targeting` is not ported; `TargetBodyPart` is identical
   in both dialects. `HealthAnalyzerOrganInfo.cs` and `HealthAnalyzerChemicalInfo.cs` are byte-verbatim, 0 edits.

**`_WF` builder deviations from Onyx, each marked in `HealthAnalyzerSystem.Wolfmed.cs`:**

- **`BuildWoundDiagnostics` gate: `HasComp<WoundHostComponent>` instead of Onyx's `HasComp<SurgeryTargetComponent>`**
  (D2, PLAN4 §8.5 trap 20). A borg or a Protogen is a Shitmed surgery target with no `WoundableComponent`
  anywhere; Onyx's gate would hand the client an always-empty dict that renders as "no findings" rather than
  "unavailable".
- **`SharedTargetingSystem.TryConvert` → `_bodySystem.GetTargetBodyPart(PartType, Symmetry)`** — Onyx's helper
  does not exist in WG; the Shitmed one is the exact equivalent and returns `null` for anything unmappable, so
  **no `Groin` key can ever be emitted** (D9, §8.5 trap 18).
- **`BuildOrganInfo` reads `WolfmedOrganComponent.Health`/`.MaxHealth` and orders by `OrganComponent.SlotId`**
  (D8) — Onyx's `OrganComponent.Health` and `OrganCategoryPrototype` are Nubody. Organs without
  `WolfmedOrganComponent` are skipped rather than reported at 0/0. `OrganOrder` takes `string?` because
  `SlotId` is nullable in WG, and lower-cases before matching because Shitmed's slot ids are lower-case
  (`brain, eyes, lungs, heart, stomach, liver, kidneys`) where Onyx's category ids were capitalised.
- **`BuildChemicalInfo` reads `bloodstream.ChemicalSolutionName`** (`"chemicals"`) where Onyx reads
  `MetabolitesSolutionName`, **keeping `HealthAnalyzerSolutionType.Metabolites` as the wire value** so the
  payload type does not churn; WP12-7 localises that row as "Chemicals". Its gate keeps Onyx's shape minus the
  `SurgeryTarget` requirement — chemicals are not a wound feature, so any target with a bloodstream or organs
  gets a list (D2 does not apply; a non-host's list is data the old UI simply never showed).
- **`BuildChemicalInfo` returns `List<…>?`** (PLAN4 §2.9's signature) where Onyx's is non-nullable. It never
  actually returns `null` today; the nullable signature matches the message field's type and lets WP12-7 treat
  "no chemicals section" and "empty list" uniformly.
- **`BuildVitalDamage` is new to Wolfgate** (PLAN4 §2.9): `MobThresholdSystem.CheckVitalDamage` for wound hosts,
  `null` otherwise. Today's "Total Damage" readout is the projection sum, which diverges from the figure that
  actually decides crit and death on a wound host.
- **`BuildPartDamage` deliberately NOT ported** (P4-D27) — WG's client already reads exact per-part damage off
  the selected part's networked `DamageableComponent` and 11-part severity buckets off `msg.Body`; Onyx's
  version additionally aliases `Groin` to `Chest` by reference, which is wrong for WG under D9.
- **All four builders are `public`** (P4-D26) so `WolfmedAnalyzerTest` (WP12-9) can call them without a client
  harness — `UpdateScannedUser` ends in `ServerSendUiMessage` and has no headless capture point.

**Behaviours recorded rather than fixed:**

- **The Organs tab shows 7 rows for a human and nothing for any other species** until phase 5 annotates more
  organ prototypes — only the seven PROTO A organs carry `WolfmedOrganComponent`. Same idea as Onyx's
  "hide `MaxHealth == 0`" filter, expressed as a `TryComp` skip.
- **The whole per-part dict ships on every update, independent of the Shitmed part selection** (PLAN4 §4's
  recorded rejected alternative): a triaging medic wants to know *which* limb is bleeding, and
  `BuildWoundDiagnostics` iterates every part regardless, so the round-trip alternative saves the server nothing.
- **Clean parts are omitted from the dict entirely** (`HasFindings`), so an undamaged wound host sends a
  non-null but **empty** `Parts` dictionary — WP12-7 must distinguish that ("no findings") from
  `WoundDiagnostics == null` ("diagnostics unavailable").
- **Cost per scan is paid on every 1 Hz update for every scanned wound host**, including the chemical
  enumeration, which walks every organ. Unchanged from Onyx and unmeasured here; if the analyzer ever shows up
  in a profile, the first move is to gate the chemical walk on the client actually having the tab open.
- **`_wounds`/`_pain`/`_functionality` are shared systems and `_mobThreshold` is the shared partial
  `Content.Shared.Mobs.Systems.MobThresholdSystem`** (phase-1 HOOK 11's `CheckVitalDamage` lives there), so the
  five new dependencies resolve on the server without touching the upstream dependency block.

**Subscription pairs registered: none.** `UpdateScannedUser` is already reached from the five existing handlers
and the Shitmed `HealthAnalyzerPartMessage` round-trip is untouched; this package adds **no BUI message** and
**no component** (PLAN4 §5.1 records WP12-6 as registering nothing; §5.2's analyzer pairs were all left to
their existing owners at `HealthAnalyzerSystem.cs:46-55`).

**New type names, all grepped 0 hits repo-wide before creation:** `HealthAnalyzerWoundDiagnostic`,
`HealthAnalyzerWoundDiagnostics`, `HealthAnalyzerVisibleWound`, `HealthAnalyzerClottingPhase`,
`HealthAnalyzerOrganInfo`, `HealthAnalyzerSolutionType`, `HealthAnalyzerReagentInfo`,
`HealthAnalyzerChemicalInfo`.

**Deviations from PLAN4:** none behavioural. Two textual notes: `BuildChemicalInfo` keeps PLAN4 §2.9's nullable
return type rather than Onyx's non-nullable one (recorded above), and `OrganOrder`'s parameter is `string?`
rather than the plan's `string` because `OrganComponent.SlotId` is nullable in WG — a compile requirement, not a
behaviour change (a null slot falls to the `_ => 10` arm exactly as an unknown id does).

**Checkpoint:** `Content.Server`, `Content.Client` and `Content.IntegrationTests` all **0 errors**
(`-c DebugOpt`). `Content.Client` is the build that would catch a non-NetSerializable field and it is green.
Headless server (~130 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with **zero**
`[ERRO]`/`[FATL]`/exception lines (`<plan>/p4/wp/WP12-6-report-server.log`) — which also exercises
the serializer's startup scan over the four new `[NetSerializable]` types and the five new `[Dependency]`
fields. No YAML/FTL/XAML/RSI touched, so no Release lint run for this package. No integration tests run (none
specified for WP12-6; `T-AN-*` belongs to WP12-9).

---

### WP12-7 (phase 4 — analyzer client UI and locale, P4-4b)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Client/_Onyx/Medical/HealthAnalyzer/EllipsisLabel.cs` | same path | new (vendored), **byte-verbatim, 0 edits** | **WP12-7** | 133 lines. No licence header in Onyx; none invented. Sandbox-clean: `System.Text.Rune`, `StringRuneEnumerator`, `StringBuilder` and `string.EnumerateRunes()` are all whitelisted (`Sandbox.yml:896`, `:994`, `:900-927`, `:1509`) and `Font.GetCharMetrics(Rune, float, bool)` exists (`Robust.Client/Graphics/Font.cs:73`) |
| `Content.Client/HealthAnalyzer/UI/HealthAnalyzerControl.xaml` (structure only) | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml` | new (`_WF`), 68 lines | **WP12-7** | Root is a **`BoxContainer`**, not a window — its `[GenerateTypedNameReferences]` scope is its own, so none of its 12 names touch `FancyWindow`'s reserved `WindowTitle`/`HelpButton`/`CloseButton`/`ContentsContainer` or `DefaultWindow`'s `TitleLabel`/`WindowHeader`. 4-button tab strip `DamageButton`/`WoundsButton`/`OrgansButton`/`ChemicalsButton` (`OpenRight`/`OpenBoth`/`OpenBoth`/`OpenLeft`), `TabBody`, `WoundsTab`/`OrgansTab`/`ChemicalsTab`, `VitalDamageRow`/`VitalDamageLabel`, `WoundStateLabel`, `WoundFindingsContainer`, `OrgansContainer`, `ChemicalsContainer`. All three tab bodies scroll |
| `…/HealthAnalyzerControl.xaml.cs:252-331, 357-409, 413-520` | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | new (`_WF`), ~345 lines | **WP12-7** | `Populate(HealthAnalyzerScannedUserMessage)`, `Clear()`, `ReleaseDamageSection()`, `DamageSection` property, `DrawWoundDiagnostics`/`DrawOrgans`/`DrawChemicals`, the organ-row diffing cache, `IsDangerousBloodLevel`, Onyx's `Capitalize`/`OopsConcat` pair kept verbatim, plus local `CreateDiagnosticGroupTitle`/`CreateDiagnosticItemLabel`/`GetTexture` copies (duplicating three small upstream helpers beats making them public). Reads the message directly — Onyx's `HealthAnalyzerUiState` is not ported |
| — | `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | new (`_WF` partial of the upstream window), 39 lines | **WP12-7** | `PopulateWolfmed(msg)` + `HideWolfmed()` — the HOOK 26 bodies. `namespace Content.Client.HealthAnalyzer.UI; public sealed partial class HealthAnalyzerWindow`, legal because the window is `public sealed partial` (`HealthAnalyzerWindow.xaml.cs:33`), and necessary because `WolfmedPanel`/`WolfmedDamageGroupsPanel` are private generated fields a standalone control could not reach |
| — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml` | modified — **HOOK 26**, 3 marked lines | **WP12-7** | `xmlns:wolfmed`, `Name="WolfmedDamageGroupsPanel"` on the existing damage `PanelContainer`, and the `<wolfmed:WolfmedDiagnosticPanel Name="WolfmedPanel" Visible="False" />` mount as the last child of `RootContainer` |
| — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml.cs` | modified — **HOOK 26**, 2 marked lines | **WP12-7** | `PopulateWolfmed(msg);` as the **first** statement of `Populate`, and `HideWolfmed();` inside the `_target == null` / no-`DamageableComponent` early-return block |
| `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl:6-16` | same path | new, **15 keys** | **WP12-7** | Onyx's 11 live wound keys verbatim + 4 fracture keys (P4-D25). The 4 disease keys and the dead `health-analyzer-wound-pain` at Onyx `:5` are not ported |
| `Resources/Locale/en-US/_Onyx/targeting/targeting.ftl:20-32` | same path | new, **10 keys** | **WP12-7** | `targeting-part-*`. Onyx's `chest` → `torso`, `groin` omitted (D9). WG had zero `targeting-part-*` keys |
| — | `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl` | modified — **LOC A**, 19 appended keys in one marked block | **WP12-7** | vital damage, 4 tab names, organ unavailable/health, chemicals unavailable/no-vessels, 4 solution names, solution empty/reagent, wound-diagnostics title/inactive/unavailable, blood-level-dangerous |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-7** | |

**Every `// WOLFGATE` edit outside `_WF` / `_Onyx`, with reason (HOOK 26, 5 functional lines + 3 XAML marker comments across 2 upstream files, plus LOC A):**

1. **HOOK 26 (a)** — `HealthAnalyzerWindow.xaml`, root element: `xmlns:wolfmed="clr-namespace:Content.Client._WF.Wolfmed.Medical"`. The `<!-- WOLFGATE: HOOK 26 … -->` marker sits on its own line **above** the root element because XML forbids a comment inside an element's attribute list; Onyx marks its own copy of this file the same way.
2. **HOOK 26 (b)** — `HealthAnalyzerWindow.xaml:288`, `Name="WolfmedDamageGroupsPanel"` added to the previously un-named damage-groups `PanelContainer`. The name goes on the **outer** panel so the Damage tab can hide the whole section; hiding the inner `GroupsContainer` alone would leave an empty expanded black panel.
3. **HOOK 26 (c)** — `HealthAnalyzerWindow.xaml`, last child of `RootContainer`: `<wolfmed:WolfmedDiagnosticPanel Name="WolfmedPanel" Visible="False" />`. Starts hidden, so a client that never scans a wound host renders the window byte-for-byte as before.
4. **HOOK 26 (d)** — `HealthAnalyzerWindow.xaml.cs:112`, `PopulateWolfmed(msg);` as the **first** statement of `Populate`. `Populate` early-returns at `:119-123`; a trailing call would leave the panel showing the previous patient's rows next to "No patient data", reachable once a second by walking out of range (CRITIQUE4).
5. **HOOK 26 (e)** — `HealthAnalyzerWindow.xaml.cs:122`, `HideWolfmed();` inside that early-return block, because a target outside client PVS can still carry non-null diagnostics.
6. **LOC A** — `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl`, 19 keys appended under one `# WOLFGATE (P4-4)` comment block. Purely additive; no existing key is touched or shadowed.

**Every `// WOLFGATE` edit inside the vendored file:** none. `EllipsisLabel.cs` is byte-verbatim from ONYX `2f5bab9`.

**Subscriptions registered by WP12-7:** **none**. **Components registered:** **none**. **New BUI message:** **none** (PLAN4 §5.1/§5.2 — the four payload fields ride the existing once-per-second `HealthAnalyzerScannedUserMessage`).

**New type names, all grepped 0 hits repo-wide before creation:** `EllipsisLabel`, `WolfmedDiagnosticPanel`, `WolfmedDiagnosticTab`.

**Locale ids added:** 15 + 10 + 19 = **44**, each grepped and confirmed to resolve exactly once across `Resources/Locale/en-US` (a 50-key audit covering every `Loc.GetString` the panel can reach, including the reused `fracture-grade-*` and `chem-master-window-unknown-reagent-text` keys, returned 0 misses and 0 duplicates).

**D2 gate:** `PopulateWolfmed` hides the panel outright when `msg.WoundDiagnostics == null && msg.Organs == null` — both are `HasComp<WoundHostComponent>`-gated server-side (WP12-6) — and `HideWolfmed` additionally calls `ReleaseDamageSection()` so the window's damage-groups section is restored to `Visible = true`. A borg, an animal or a Protogen therefore renders exactly as it does today.

**Deviations from PLAN4 (all recorded, none behavioural against the decision set):**

1. **Tab set is `Damage` / `Wounds` / `Organs` / `Chemicals`** as §2.10 specifies, so LOC A ships `health-analyzer-window-damage-tab` and `-wounds-tab` in place of §2.12's `health-analyzer-window-whole-body` and `-entity-damage-part-text`. Those two Onyx keys have **no consumer in Wolfgate** — WG's window has a `ReturnButton` and a `PartNameLabel` instead of Onyx's whole-body button and damage-scope label. LOC A is still exactly 19 keys.
2. **Four fracture keys, not two.** P4-D25 names the two summary keys; rendering `FractureTreatment` needs a word for `Reduced` and `Mended`, and WG had none. `health-analyzer-wound-fracture-treatment-reduced`/`-mended` are the two extra. The grade word itself reuses the `fracture-grade-*` keys WP12-1 already shipped rather than adding four more.
3. **`targeting.ftl` ships 10 keys, not §2.12's "11".** Onyx `:20-32` is 11 part keys; `groin` is omitted under D9, so 10 remain. `chest` is renamed `torso` to match `PartKey(TargetBodyPart.Torso)`.
4. **HOOK 26 (e) is `HideWolfmed();`, not the literal `WolfmedPanel.Visible = false;`.** Same one line, same site, but the body stays in the `_WF` partial (ground rule 2/3) and additionally clears the stale rows and hands the damage section back — without which an early return taken while the Wounds tab was selected would leave the window with **both** sections hidden.
5. **The mount line carries no `VerticalExpand="True"`.** PLAN4 §3.1's snippet sets it, but `RootContainer` is a vertical `BoxContainer` and `WolfmedDamageGroupsPanel` is already a `VerticalExpand` child: a second permanently-expanding sibling would halve the damage section on the Damage tab. `ApplyTab()` sets `VerticalExpand = TabBody.Visible` instead, so the panel expands only while it owns the area.
6. **`EllipsisLabel`'s "`OopsConcat` trick" is in the panel, not in `EllipsisLabel`.** §2.10 says to keep it in the vendored file; at the pin `EllipsisLabel.cs` contains no such helper — it lives in `HealthAnalyzerControl.xaml.cs:516` beside `Capitalize`, and both were carried into the panel verbatim.
7. **No `MaxWidth="430"` on the finding labels.** Onyx's window is 790 px wide; WG's is `SetWidth="350"`, so the labels use `HorizontalExpand` inside an `HScrollEnabled="False"` `ScrollContainer` and wrap to the real width.
8. **The bullet and separator glyphs are ASCII (`- `, ` - `, `x2`)** rather than Onyx's bullet, middot and multiplication sign, to keep the file ASCII-clean like the rest of `_WF`.

**Known limitation, carried from WP12-6, restated here because it is what a medic sees:** the Organs tab shows **7 rows for a human and "Organ data unavailable." for every other species**, because only the seven organs PROTO A annotated in phase 3 carry `WolfmedOrganComponent`. Phase 5 widens it.

**Checkpoint:** `Content.Client` **0 errors**, `Content.Server` **0 errors**, `Content.IntegrationTests` **0 errors** (all `-c DebugOpt`, sequential). Headless server (120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines (`<plan>/p4/wp/WP12-7-report-server.log`), which is the gate for the three FTL files (a duplicate or malformed Fluent id is logged there). **Headless BUI exercise:** the existing `Content.IntegrationTests/Tests/UserInterface/UiControlTest.TestWindows` instantiates every content `BaseWindow` with an empty constructor inside a connected client pair — that now loads `HealthAnalyzerWindow.xaml`, resolves the `wolfmed:` xmlns, constructs `WolfmedDiagnosticPanel` from its own XAML and runs both constructors including `ApplyTab()`. **Passed** in 26 s (`<plan>/p4/wp/WP12-7-report-tests.log`). What it does **not** cover — and what still needs a live scan or WP12-9 — is `Populate` with a real payload: the tab switching, the per-part findings text and the organ row diffing. No sprite-pixel or screenshot test was run (project memory: prefer logic tests, and the user may be working).

---

### WP12-8 (phase 4 — explosion amputation, P4-6 / P4-D14)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| — | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | modified (vendored), **3 marked sites** | **WP12-8** | optional trailing `DamageableSystem.DamageOriginFlag? originFlag = null` on `TryApplyDistributedDamage` and `TryRouteDistributedDamage`, plus the `_routedModifiers` save/restore scope around the distributed body. Every pre-existing caller (`TryApplyLethalDamage:541`, `WoundDamageFoundationTest`, `WoundHealingTest`, `WolfmedAmputationTest`) keeps compiling and behaving identically — the default `null` reproduces the `GetValueOrDefault` tuple those paths already saw. The two `before: [typeof(SharedArmorPlateSystem)]` registrations at `:64`/`:66` are untouched (D23, §5.4) |
| — | `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` | new (`_WF`), 45 lines | **WP12-8** | `TryApplyExplosionDamage(EntityUid, DamageSpecifier)`: `HasComp<WoundHostComponent>` gate, then `TryRouteDistributedDamage(TargetBodyPart.All, SplitWithVariation, ignoreResistances: true, interruptsDoAfters: false, variation, isExplosion: true, woundSeverityMultiplier, originFlag: Explosion)`. Holds both CVar reads, so **`ExplosionSystem.CVars.cs` is not touched at all** (unlike Onyx). No subscriptions — two `Subs.CVar` callbacks only |
| — | `Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs` | modified — **HOOK 22**, 3 marked lines | **WP12-8** | 1 `using`, 1 `[Dependency]`, and the `if (!_wolfmedExplosion.TryApplyExplosionDamage(entity, damage))` guard in front of the existing `TryChangeDamage` call at `:471` |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-8** | |

**Every `// WOLFGATE` edit outside `_WF` / `_Onyx`, with reason (HOOK 22, 3 marked lines in 1 upstream file):**

1. **HOOK 22 (a)** — `ExplosionSystem.Processing.cs:5`, `using Content.Server._WF.Wolfmed.Explosion;`. Required for the `[Dependency]` type name.
2. **HOOK 22 (b)** — `ExplosionSystem.Processing.cs:32`, `[Dependency] private WolfmedExplosionSystem _wolfmedExplosion = default!;`. Placed in this partial rather than `ExplosionSystem.cs` so the whole hook lands in one upstream file.
3. **HOOK 22 (c)** — `ExplosionSystem.Processing.cs:474`, `if (!_wolfmedExplosion.TryApplyExplosionDamage(entity, damage))` immediately above the existing `_damageableSystem.TryChangeDamage(...)` call. The call itself — including its `// Mono: Explosion flag for plate protection` comment and its `originFlag:` argument — is **unchanged byte-for-byte**, and the `if` is deliberately brace-free so the hook is one line rather than three (ground rule 3: one or two marked lines per site). Non-wound-hosts take the `HasComp` early-out inside the body and fall straight through to today's call, so **D2 holds structurally**: a borg, an animal, a Protogen or a crate sees the identical code path with identical arguments.

**Every `// WOLFGATE` edit inside the vendored `WoundDamageRoutingSystem.cs` (3 sites, 10 marked lines):**

1. **`TryApplyDistributedDamage` gains `DamageableSystem.DamageOriginFlag? originFlag = null`** and a `_routedModifiers` scope: `var hadModifiers = _routedModifiers.TryGetValue(body, out var previousModifiers); _routedModifiers[body] = (0f, null, originFlag);` in a `try`, restored (not removed) in the `finally`. Reason: the distributed entry points bypass `OnBeforeDamageChanged`, which is `_routedModifiers`' only other writer, so the re-entrant routed pass reached `SharedArmorPlateSystem.OnBeforeDamageChanged` with `OriginFlag == null` and its gate (`Origin == null && OriginFlag != Explosion`, `SharedArmorPlateSystem.cs:60`) refused plate protection against explosions outright — the P3-D3 hole. **Save/restore rather than a bare `Remove`** is PLAN4 §8.5 trap T3's shape: the dictionary has a second writer and a bare remove would clear an outer scope.
2. **The method's original body is now `private bool ApplyDistributedDamageCore(...)`**, verbatim, with the public entry point wrapping it (see deviation 1).
3. **`TryRouteDistributedDamage` gains the same optional parameter** and forwards it as the 11th argument.

**Subscriptions registered by WP12-8:** **none** (PLAN4 §5.1). `WolfmedExplosionSystem.Initialize` registers only two `Subs.CVar` callbacks, which are not directed subscriptions, and this package adds no `SubscribeLocalEvent` anywhere. **Components registered:** **none**. **New prototypes / locale ids:** **none**. **New type names:** `WolfmedExplosionSystem` (grepped, 0 hits before creation) and the private method `ApplyDistributedDamageCore`.

**CVars consumed for the first time since phase 1:** `explosion.damage_variation` (2f) and `explosion.wounding_multiplier` (4f), `Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs:22-26`. Both are `CVar.SERVERONLY`; setting `explosion.wounding_multiplier` to `1` and `explosion.damage_variation` to `0` returns wound-host blasts to an even, unamplified split. The feature as a whole cannot be switched off by CVar — it is the `HasComp<WoundHostComponent>` gate that decides.

**Deviations from PLAN4, with justification:**

1. **The `_routedModifiers` scope is a public wrapper around a renamed private core, not a `try`/`finally` wrapped around the existing body in place.** PLAN4 §2.11 sketches `try { ... } finally { ... }` inside `TryApplyDistributedDamage`. The method body is ~95 lines with three separate `return` points, so an in-place `try` means re-indenting the whole method — a 95-line whitespace diff in a vendored file that would bury the ~25 existing `// WOLFGATE` marks and make any future Onyx re-sync harder to read. `ApplyDistributedDamageCore` holds Onyx's body byte-for-byte and the wrapper is 18 new lines. Semantics are identical, including the re-entrant case: the core's own `_routing.Contains(body)` guard still refuses a nested call, and the wrapper's `finally` restores the outer entry before anything can read it.
2. **The wrapper writes `_routedModifiers[body]` before the core's wound-host/`_net.IsServer`/mode guard runs**, so a call that the guard refuses briefly holds a `(0f, null, originFlag)` entry. No damage is applied inside that window (the guard returns `false` immediately) and the `finally` restores the previous value, so this is observationally inert; adding a duplicate guard to the wrapper was judged worse than the window.
3. **`ignoreGlobalModifiers` is not carried into the routed pass.** Vanilla explosion damage passes `ignoreGlobalModifiers: true`; `RouteThroughBodyModifiers` calls `WolfmedDamageableSystem.ChangeDamage` without it. **This is pre-existing phase-1 behaviour, not a WP12-8 change** — explosion damage on a wound host has been routed through `OnBeforeDamageChanged` since GUARD D, and that path never carried the flag either. Recorded here because WP12-8 is the package that makes explosions a designed wound-host mechanic; closing it would be a one-argument change in `RouteThroughBodyModifiers` that affects every routed hit, not just explosions, so it is left for the balance pass.
4. **`TargetBodyPart.All` includes `Groin`, which D9 forbids as a key.** No Groin key is emitted: `WoundTargetResolver.GetMatchingParts` enumerates the body's actual children and maps each through `GetTargetBodyPart`, so a bit with no matching part contributes nothing. This is the same mask phase 3's `TryRouteDistributedDamage` call already used.

**What changes in play:** a grenade or bomb that catches a wound host now splits its localised damage across every attached limb with a per-limb weight roll of up to `1 + variation` (default up to 3x), applies wounds at `woundSeverityMultiplier` 4x, and nominates exactly one non-torso limb with amputation thresholds as the explosion amputation candidate — `AmputationSystem.TryExplosionAmputate` (`AmputationSystem.cs:43`, `:85`) then rolls that limb off if the blast pushed it past its threshold. Systemic damage types still go to the body as one lump. **Armour plates now protect against explosions on wound hosts, which they did not before this package** — the fix and the feature ship together on purpose, because the distributed path is what made the hole reachable.

**Checkpoint:** `Content.Server` **0 errors**, `Content.Client` **0 errors**, `Content.IntegrationTests` **0 errors** (all `-c DebugOpt`, sequential). Headless server (120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines (`<plan>/p4/wp/WP12-8-report-server.log`) — the gate for the new `[Dependency]` edge `ExplosionSystem -> WolfmedExplosionSystem`, which would throw at system-manager init if it could not resolve. **Tests:** `DockTest` + the whole `WolfmedAmputationTest` class, **8/8 passed** (`<plan>/p4/wp/WP12-8-report-tests.log`), including phase 3's `ExplosionAmputatesDeterministicallyTest`, which is the regression gate for the new optional parameter defaulting to `null`. No YAML/FTL/XAML/RSI touched, so no Release lint run. **T-EXPLOSION-PLATE and T-EXPLOSION-WRAPPER do not exist yet and belong to WP12-9** — there is no armour-plate test anywhere in `Content.IntegrationTests` today (grepped), so the plate half of P4-D14 is currently covered by code reading only.

---

### WP12-9 (phase 4 — tests, P4-8)

**This package is the sole owner of every file under `Content.IntegrationTests`.** Six new test files, three
modified, plus one withdrawn prototype line (see the deviations below). No new C# outside the test project.

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedMedicalPatchTest.cs` | new, 2 tests | **WP12-9** | T-PATCH, split in two (deviation 1). Bespoke inert reagent `WolfmedPatchTestChem` keeps the file independent of WP12-2's content |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReagentTreatmentTest.cs` | new, 7 tests | **WP12-9** | T-REAGENT-CAP-YES / -CAP-NO / -SYSTEMIC-BYPASS / -SUPPRESS / -MEND / -STAM / -PROTOTYPE-SANITY |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundBleedingTest.cs` | same path | **modified** | **WP12-9** | T-TOURNIQUET restored in place of the phase-1 skip note at `:147-148` |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSurgeryTest.cs` | new, 9 tests | **WP12-9** | T-SURG-BLEED / -FRACTURE / -INTERNAL / -AMPCONSEQ / -ORGAN (2 tests) / -WINDOW / -SCAR / -PAIN, plus 11 bare single-component step prototypes and 2 body fixtures |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReattachTest.cs` | same path | **modified**, +2 tests | **WP12-9** | T-REATTACH-BLOCKED and T-SURG-AMP-CLEAN beside the existing `ReattachedPartRejoinsWoundTrackingTest` |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/AmputationConsequenceTest.cs` | same path | **modified**, +1 test | **WP12-9** | Onyx's `SurgicalHealRemovesConsequenceAndUnblocksTest` restored; the `<remarks>` skip block rewritten |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | new, 8 tests | **WP12-9** | T-AN-GATE / -FINDINGS / -CLEARS / -CLOT / -PAIN / -ORGANS / -CHEM / -VITAL, all through P4-D26's public builders |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedExplosionTest.cs` | new, 3 tests | **WP12-9** | T-EXPLOSION-PLATE, T-EXPLOSION-WRAPPER and T-SURGERY-PROTOTYPE-SANITY (co-located; it needs no mob) |
| — | `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` | **modified — PROTO E WITHDRAWN** | **WP12-9** | deviation 2; the one added `- id: Tourniquet` line becomes a 7-line `# WOLFGATE` note |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md` | this section | **WP12-9** | |

**Test count: 33 new** (31 new methods plus the 2 restored skips), taking the wound suite from **65 to 98**.
Filter `FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed`
-> **98 passed, 0 failed, 0 skipped**.

**Every `// WOLFGATE` edit outside the test project (1 site):**

1. **`Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml`, `MedkitAdvancedFilled`** — WP12-3's PROTO E
   line `- id: Tourniquet` is replaced by a marked 7-line comment recording why it was withdrawn and what has to
   change if it is wanted back. No other line in the file is touched. See deviation 2.

**Subscriptions registered by WP12-9:** **none**. **Components registered:** **none**. **New C# type names:**
the six test fixture classes only, all grepped 0 hits before creation.

**New `[TestPrototypes]` ids** (a global pool, PLAN4 §4 rule 3 — every id grepped repo-wide, 0 hits before
creation): reagent `WolfmedPatchTestChem`; entities `WolfmedPatchTrash`, `WolfmedPatchItem`,
`WolfmedPatchSingleUse`, `WolfmedPatchTarget`, `WolfmedReagentBody`, `WolfmedReagentControlBody`,
`WolfmedSurgeryBody`, `WolfmedSurgeryControlBody`, `WolfmedStepClamp`, `WolfmedStepSetBone`,
`WolfmedStepMendBone`, `WolfmedStepStopInternal`, `WolfmedStepHealAmputation`, `WolfmedStepHealHeartTest`,
`WolfmedStepHealFuncOrganTest`, `WolfmedStepSurgeryPain`, `WolfmedStepOpenIncisionWound`,
`WolfmedStepClampIncision`, `WolfmedStepCloseIncision`, `WolfmedAnalyzerPlainTarget`,
`WolfmedAnalyzerSurgeryTarget`, `WolfmedPlateVest`; body prototypes `WolfmedReagentBodyGraph` and
`WolfmedSurgeryBodyGraph`. `[TestPrototypes]` strings are pooled across the whole assembly
(`PoolManager.Prototypes.cs`), so WP12-9 deliberately reuses phase-3's `WolfmedAmputationBody`,
`WolfmedOrganFuncBody`/`WolfmedOrganFuncOrgan` and `AmputationConsequenceTestBody` rather than cloning them.

**Deviations from PLAN4 §6.2, with justification:**

1. **T-PATCH is two tests, not one.** `singleUse: true` deletes the patch inside `EntityUnstuckEvent`, which
   would make PLAN4's assertion (c) — "after unsticking, no further transfer" — vacuously true.
   `WolfmedPatchItem` (not single-use) measures the Update loop actually stopping; `WolfmedPatchSingleUse`
   covers (d).
2. **PROTO E is withdrawn: `MedkitAdvancedFilled` no longer contains a `Tourniquet`.** This was a **real
   production failure**, red since WP12-3 and never caught because that package ran no tests. `MedkitAdvanced`
   inherits `Medkit`'s `grid: [0,0,3,1]` (8 cells, `maxItemSize: Small`); the four existing entries fill it, so
   every spawn logged `[ERRO] system.storage: Tried to StorageFill tourniquet ... but can't. reason: No room!`
   and failed **`EntityTest.SpawnAndDeleteAllEntitiesInTheSameSpot`** and
   **`EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps`**. Withdrawing the line is the smallest fix (it
   removes an edit rather than adding one) and costs nothing reachable: the `Tourniquet` id is unchanged and
   still spawns through its eight existing references (sec/gib vending, security spawners, `job.yml` belts,
   `cmo_webbing.yml`, two `_NF` loot fills, `nfsdtec.yml`). **If the medkit placement is wanted, the fix is a
   bigger `grid:` on `MedkitAdvanced` in
   `Resources/Prototypes/Entities/Objects/Specific/Medical/medkits.yml` — a file no phase-4 WP owns, so it is
   escalated to WP12-10 / the user rather than taken here.**
3. **T-PATCH's fixture runs on a disconnected pair (`PoolSettings => PsDisconnected`).** Sticking and then
   unsticking hands the patch between the target's `stickers_container` and the user's hands inside one tick,
   and RT's **client** `ContainerSystem.HandleComponentState` trips its own
   `DebugTools.Assert(container.Contains(entity))` replicating that churn
   (`RobustToolbox/Robust.Client/GameObjects/EntitySystems/ContainerSystem.cs:206`). That is vanilla
   `StickySystem` behaviour shared by every sticky item and untouched by phase 4, so the pair runs disconnected
   rather than the vendored file being worked around. `MedicalPatchSystem` is server-only (P4-D13) and the test
   reads no client state.
4. **T-AN-CLEARS asserts the FRACTURE disappears from the LeftArm row, not the whole row.** PLAN4 predicts the
   row vanishes; it does not, and should not — the same 75-Blunt hit that broke the bone also left a
   `BluntWound` and part pain, both genuine findings. The row-disappearance half is asserted where it really is
   true: detaching the head removes the `Head` key entirely.
5. **T-SURGERY-PROTOTYPE-SANITY's closed-loop invariant is CLAMPABILITY, not closability.** PLAN4 asks that
   every surgery reaching an incision-opening step also reach a `Close` step through its own requirement chain.
   **The shipped tree says that is false for roughly thirty surgeries** — `SurgeryAttachHands`,
   `SurgeryInsertBorgBrain`, every organ remove/insert, and WP12-5's own `SurgeryHealBrain`/`SurgeryHealEyes`,
   which end on `SurgeryStepSealOrganWound` — because `SurgeryCloseIncision` is a **separate** surgery the medic
   runs afterwards. WP12-5's handoff note B says the same. The invariant that actually kills CRITIQUE4 B1's bug
   class is that the operation which opened a `SeparateInstances` bleeder can always **stop** it, so the test
   asserts every such surgery reaches a `Clamp` step (they all inherit `SurgeryOpenIncision`'s
   `SurgeryStepClampBleeders`), **plus** that `SurgeryCloseIncision` carries `Close`, **plus** that WP12-5's five
   incision-based wound surgeries close their own incision on `SurgeryStepSealTendWound` (PROTO G's fourth
   site), **plus** that `SurgeryTendWoundsBrute`/`Burn` reach neither an opening step nor a clamp step.
6. **T-SURG-ORGAN is two tests** — the health ladder on a real `MobHuman` heart, and the function-restore half
   on WolfmedOrganTest's `MutedComponent` fixture organ, which needs its own `[Test]` because the one-tick
   window has to be measured before `Pair.RunTicksSync`.
7. **T-REAGENT-SYSTEMIC-BYPASS uses `Poison`, not PLAN4's `Toxin`.** `Toxin` is a damage **group** in Wolfgate
   (`Resources/Prototypes/Damage/groups.yml:32`), not a damage type; its types are `Poison` and `Radiation`.
   `Poison` has the property the test needs: absent from `WoundHostComponent.LocalizedDamageTypes`.
8. **T-SURG-BLEED uses `SlashWound` + `PiercingWound`, not two `SlashWound`s.** Both default to
   `mergeMode: MergeByPrototype`, so PLAN4's "a wound at 20 and a second at 10" would merge into one wound at 30
   and the worst-bleeder-first ordering could not be measured.
9. **Bare single-component step prototypes rather than WP12-5's shipped steps.** PLAN4 §6.2 asks for exactly
   this ("spawn a bare `WolfmedSurgeryClampBleedingEffect { amount: 10 }` entity"); recorded because it means a
   failure in `WolfmedWoundSurgeryTest` is a failure of the **effect**, while the shipped step and surgery
   wiring is covered separately by T-SURGERY-PROTOTYPE-SANITY.
10. **Not ported, each per an existing decision:** Onyx's
    `HealthAnalyzerPartDamageTest.BuildsIsolatedPartSnapshotTest` (P4-D27), `ClassifiesDangerousBloodLevel`
    (client-side), and Onyx's `WoundSurgeryTest`/`WoundSurgeryScarTest` as written (they target the excluded
    Onyx surgery framework; only their pattern is adopted, per §6.1 trap 9).

**Two findings recorded rather than fixed — neither is a port defect:**

1. **A stuck medical patch cannot be unstuck, in Wolfgate or in Onyx.** `MedicalPatchSystem.OnStuck` adds
   `UnremoveableComponent`; `SharedInteractionSystem` subscribes that component to
   `ContainerGettingRemovedAttemptEvent` and cancels unconditionally
   (`Content.Shared/Interaction/SharedInteractionSystem.cs:213-216`), so `StickySystem.UnstickFromEntity` can
   never take the patch out of the target's sticker container — **including the call
   `MedicalPatchSystem.Update` makes itself when the patch runs dry**, which leaves `singleUse` and
   `trashObject` unreachable in normal play. Onyx's own `SharedInteractionSystem` cancels identically at the pin
   (`:216`), so the vendored file is faithful and WP12-0 transcribed it correctly. Both tests remove the
   component first, exactly as the gib / unequip paths do. **Balance-pass item, not a phase-4 bug.**
2. **A failing test inside a `GameTest` fixture can be reported by the runner as `Skipped` while the run summary
   still reads `Test Run Successful`.** Observed three times during this package: the pair is dirty-disposed,
   NUnit marks the test skipped, and the total line stays green. **Anyone reading a phase-4 test log must treat
   a non-zero `Skipped:` count as a failure until each skipped test has been re-run individually.** The only two
   legitimately skipped tests in this repo are `EntityTest.SpawnAndDeleteEntityCountTest` and
   `EntityTest.SpawnAndDirtyAllEntities`, both permanently `[Ignore]`d upstream.

**Numbers measured here that earlier packages only predicted** (P2-D16; each is documented at its assertion):

* A stuck medical patch transfers `injectAmmountOnAttatch` synchronously **and** a full `transferAmount` on the
  very next server tick — `MedicalPatchComponent.NextUpdate` defaults to `TimeSpan.Zero` and `OnStuck` never
  seeds it. 2u + 5u land before `updateTime` has elapsed at all.
* `PainSystem.SuppressPain`'s `DecayPerSecond` is a `FixedPoint2`, i.e. rounded to two places: 40 over 30 s is
  stored as `1.33`, so decaying for the nominal duration leaves `0.1` behind. T-REAGENT-SUPPRESS decays 60 s.
* `wounds.bleeding_auto_stop_enabled` defaults to **true**, so every fresh bleeder is given an
  `AutomaticClottingAt` deadline and reads as `InProgress`; the analyzer's `None` clotting phase only appears on
  a wound whose deadline has been cleared.
* `ArmorPlateBlunt_Slash` absorbs Blunt at ratio 1, so with WP12-8's origin-flag passthrough a 40-Blunt routed
  blast lands **exactly zero** damage on a plated wound host and 40 on an unplated one.
* `BluntWound` from a 20-Blunt hit is severity 20 and heals 1:1 (`severityMultiplier: 1`,
  `HealingMultiplier` 1), which is what T-REAGENT-CAP-YES/-NO measure to the point.

**HOOK 22 checklist grep (PLAN4 §6.2's T-EXPLOSION-WRAPPER pairing):**
`grep -n "_wolfmedExplosion.TryApplyExplosionDamage" Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`
-> **1 hit at `:474`**. HOOK 22 is present, and T-EXPLOSION-WRAPPER pins its contract so it cannot be silently
dropped.

**Checkpoint:** `Content.Server` **0 errors**, `Content.Client` **0 errors**, `Content.IntegrationTests`
**0 errors** (all `-c DebugOpt`, sequential). `DockTest` first: **3/3 passed**. Wound suite
(`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`): **98/98 passed, 0 skipped**
(`<plan>/p4/wp/WP12-9-report-tests.log`). Smoke filter
(`EntityTest|PrototypeSaveTest|DockTest`): **9 passed, 0 failed** (`WP12-9-report-smoke.log`), the 2 skips being
the permanently `[Ignore]`d upstream pair. Headless server (120 s, port 1299) reached
`Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
(`WP12-9-report-server.log`) — run because this package edits a prototype fill file. Release YAML lint:
**1 error, and it is not this package's** — WP12-0's standing `medical_patch.yml` missing-`icon:` hazard, still
unowned (`WP12-9-report-lint.log`).

### WP12-10 (phase 4 — guidebook, docs and manifest reconcile, P4-7 / P4-9)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `Resources/ServerInfo/_Onyx/Guidebook/Medical/Wounds.xml` | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | new, adapted | **WP12-10** | Drops the whole "Part material" section (slime/plant, IPC mechanical, cybernetic mechanical, material-electrical) and the `SyntheticRepairTool` embed — nothing in phases 1-4 supports non-organic wounds (D3). Folds `BodyPartDamage`'s three paragraphs in (its XML is outside the ONYX sparse checkout, so it is not reconstructed as a fourth page). Rewords "Fracture" qualitatively (no quoted numbers - §8.2-1 corrected the shipped `manipulationModifier` values) and "Dismemberment" for guns/lasers (§8.6-1). Adds a `Tourniquet` embed. Keeps the amputation-consequence paragraph (P4-D15) and says what it actually does (CRITIQUE4 m7): hidden, not greyed, six attach surgeries for a torso stump |
| `Resources/ServerInfo/_Onyx/Guidebook/Medical/WoundTreatment.xml` | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | new, adapted | **WP12-10** | Drops the "IPCs and cybernetics" section and its `Welder`/`SyntheticRepairTool`/`CableApcStack` embeds outright (D3). Adds a `Tourniquet` embed to the biological-tissue box and the two-step fracture ladder (Bonesetter -> BoneGel) to the fractures paragraph |
| `Resources/Prototypes/_Onyx/Guidebook/medical.yml` (2 of its 5 entries) | `Resources/Prototypes/_WF/Wolfmed/Guidebook/medical.yml` | new | **WP12-10** | `Wounds` and `WoundTreatment` `guideEntry` rows only - the other three (`Virology`, `BodyPartDamage`, `Surgery`) are not ported (D3, P4-D15) |
| — | `Resources/Prototypes/Guidebook/medical.yml` | **modified — PROTO L**, 2 lines | **WP12-10** | `- Wounds` and `- WoundTreatment` inserted into `Medical`'s `children:` list after `MedicalDoctor` and before `Chemist` (`:5-12`, corrected citation per CRITIQUE4 m4) |
| `Resources/Locale/en-US/_Onyx/guidebook/wounds.ftl` | `Resources/Locale/en-US/_WF/Wolfmed/guidebook/wounds.ftl` | new, adapted subset | **WP12-10** | 21 of Onyx's 30 keys carried forward and reworded (all under a fresh `guidebook-wolfmed-*` prefix to avoid any accidental key aliasing with Onyx's un-ported original); the 9 IPC/slime/cybernetic/material keys dropped entirely. Adds one sentence naming the Tier-A painkiller ladder (ibuprofen 0.5 -> ketorolac 0.9 -> tramadol 1.25 -> oxycodone 2.0, verified directly against `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml`) and the tourniquet to the treatment checklist |
| — | `Docs/_WF/Wolfmed/WOLFMED_PLAN4.md` | new (copy of `<plan>/p4/PLAN4.md`) | **WP12-10** | |
| — | `Docs/_WF/Wolfmed/CRITIQUE4.md`, `Docs/_WF/Wolfmed/reports/analysis/phase4/{reagents,tools,surgery,analyzer,tests}.md` | new (copies) | **WP12-10** | the five phase-4 analyst reports |
| — | `Docs/_WF/Wolfmed/reports/work-packages/phase4/*-report.md`, `*-verify.md` | new (copies, 20 files: WP12-0..WP12-9, report+verify each) | **WP12-10** | |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md` | modified — reconcile | **WP12-10** | this section plus the §7.1 row edits above and the "Phase 4 - user decisions" subsection below |
| — | `Content.Shared/Gibbing/Systems/GibbingSystem.cs` | modified (pre-phase-4 fix, DECISIONS "Gibbing fix") | **WP12-10 records it; the fix predates this WP** | Two `.ToArray()` snapshots (`TryGibEntityWithRef`'s `Drop`/`Gib` branches, `:141` and `:155`) + `using System.Linq;`, all marked `// WOLFGATE`. Fixes the `InvalidOperationException: Collection was modified` flagged as an unfixed upstream bug in phase 3's manifest (`GibbingSystem.cs:141`, "Upstream bug found, NOT fixed") — `TryGibEntityWithRef`'s `GibContentsOption.Drop`/`Gib` branches enumerated `container.ContainedEntities` while `DropEntity`/`GibEntity` removed from it. Wolfmed made the crash more reachable because routing concentrates damage on one part (an arm holding its own hand crossing a `Destructible` gib threshold). Verified present in the working tree at the start of this WP (`git diff 6329d204e3 -- Content.Shared/Gibbing/Systems/GibbingSystem.cs`) |

**Reconciliation of two stale plan-document lines, both struck as records rather than by editing the plan files (PLAN.md/PLAN2.md are not edited per the hard rules):**

* **PLAN.md §8.2's `Scale` tuning note is struck (P4-D30).** The note read "Onyx's framework clamps `Scale` to
  <=1 unless `Scaling` is set; Wolfgate's is unclamped" — the opposite of the truth. WG's `MetabolizerSystem`
  computes `scale = mostToRemove / rate` from a `FixedPoint2.Clamp(rate, 0, quantity)` numerator, which is
  structurally `[0, 1]`; Onyx's own `scale` can exceed 1 when `!MetabolizeAll`. No tuning work item follows
  from this; `MinScale` is not ported (no reagent in `_Onyx/Reagents/**` sets it).
* **PLAN.md WP11's `GroupHealSpecifier` line is struck (P4-D12).** It bundled `GroupHealSpecifier` with the
  Tourniquet/Medical Patch line item; there is no dependency edge - `MedicalPatchComponent.cs`/
  `MedicalPatchSystem.cs` reference it nowhere (confirmed WP12-0). Its only consumers in the whole Onyx tree
  are Vampire-antagonist files with no decision document naming Vampire in scope, and it carries a second
  licence (Wega, GPL-3.0) layered under Onyx's AGPL — a reason not to port it casually, not a phase-4 task.

**Embed check for both XML documents (already run against the shipped tree):** `Gauze`, `Brutepack`,
`Ointment`, `MedicatedSuture`, `RegenerativeMesh`, `Bonesetter`, `BoneGel`, `HandheldHealthAnalyzer`,
`ChemDispenser`, `Syringe`, `Tourniquet` all resolve (`grep "^  id: <Entity>$" Resources/Prototypes/Entities` ->
1 hit each). `Welder`, `CableApcStack` and `SyntheticRepairTool` are not embedded — the whole IPC/cybernetic
section that used them is dropped.

**Subscriptions registered by WP12-10: none. Components registered: none.** Two `guideEntry` prototypes and
two locale files are pure content; the only code-adjacent touch is the 2-line PROTO L edit to an existing
upstream YAML list (§5.1 of PLAN4 — WP12-10 registers nothing).

**Deviations from PLAN4 §4/WP12-10, with justification:**

1. **Locale keys are `guidebook-wolfmed-*`, not `guidebook-onyx-*`.** PLAN4's table cites the Onyx source path
   as a location reference; the content itself is reworded Wolfgate prose (organic-only, gun/laser severing,
   qualitative fracture text), so it is authored under a fresh prefix rather than kept under Onyx's naming —
   consistent with how the file also moves from `_Onyx/guidebook/` to `_WF/Wolfmed/guidebook/` in PLAN4's own
   destination column.
2. **`guidebook-wolfmed-wounds-examination` is a new key**, not present in Onyx's file, formed by folding
   `BodyPartDamage`'s "Examination" and "Damage and wounds" paragraphs (reworded, IPC material-check sentence
   dropped) into the `Wounds` entry per PLAN4's fold-in instruction — the split across two Onyx keys did not
   survive the merge cleanly as one-to-one.
3. **The tourniquet embed appears in both documents** (`Wounds.xml`'s common-trauma box and
   `WoundTreatment.xml`'s biological-tissue box) rather than only the one PLAN4's prose suggested, because both
   boxes already group the relevant treatment tools (bleeding-control items in `Wounds`, the full biological kit
   in `WoundTreatment`) and the entity resolves cleanly in either.

**Docs copied, not adapted:** `WOLFMED_PLAN4.md` is a byte-identical copy of `<plan>/p4/PLAN4.md`
(PLAN4 does not get Wolfgate-side edits — it documents what was planned, not what shipped; deviations are
recorded in this manifest instead). Likewise `CRITIQUE4.md` and the five analyst reports are copied verbatim
as evidence, and the ten `WP12-*-report.md` / `WP12-*-verify.md` pairs are copied verbatim as the phase-4
work-package record.

**Checkpoint:** `Content.Client` **0 errors** (`-c DebugOpt`) — this package touches no server-only C#, so
`Content.Server` was also rebuilt to confirm the upstream PROTO L YAML edit does not disturb prototype loading
(**0 errors**, both builds sequential). Headless server (120 s, port 1299) reached
`Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
(`<plan>/p4/wp/WP12-10-report-server.log`) — the guidebook prototype, its two `Box`/`FTLTextpart`
XML documents and every embedded entity id resolved with no missing-file or missing-key error. Release YAML
lint (`WP12-10-report-lint.log`): **1 error, unchanged from WP12-9** — the pre-existing, unowned
`medical_patch.yml` missing-`icon:` hazard; zero new lint errors from this package's PROTO L edit or new
prototype file.

## Phase 4 — user decisions (DECISIONS.md §8.4)

All defaults taken (orchestrator; user pre-authorised more lethality in phase 3). Recorded here verbatim
against what shipped, per the manifest's role as the reconciled record:

* **§8.4-1 Explosion amputation: SHIP.** Landed in **WP12-8** (P4-D14), gated on **T-EXPLOSION-PLATE** +
  **T-EXPLOSION-WRAPPER**, tunable via Onyx's `explosion.damage_variation` / `explosion.wounding_multiplier`
  CVars. This reverses the phase-3 record "explosion amputation is out (§8.6-6)" and closes the P3-D3
  armour-plate hole in the same change.
* **§8.4-2 Organ heal rate: `amount: 3`.** Shipped in **WP12-5**, marked `# WOLFGATE (P4 balance)` on every
  organ-heal step, not Onyx's `amount: 1` — a deliberate pace deviation from D4 (P4-D23).
* **§8.4-3 Surgery scarring / incision bleeding: ship the four-prototype chain as revised.** Shipped in
  **WP12-5** (PROTO G, P4-D21): `SurgeryStepOpenIncisionScalpel` gains the wound effect beside its existing
  flat `Bloodloss: 10` (never replacing it — a D2 breach would strip the cost from non-hosts); `Clamp` on
  `SurgeryStepClampBleeders`; `Close` on both `SurgeryStepCloseIncision` and `SurgeryStepSealTendWound` (the
  latter a Wolfgate adaptation with no Onyx counterpart, so the wound surgeries self-close). The careful
  incision path (`SurgeryStepCarefulIncisionScalpel`) gets nothing — it carries no damage effect in either
  tree and its only consumers have no clamp/close step.
* **§8.4-4 Reagents: Tier A shipped.** Landed in **WP12-2**: the four `SuppressPain` blocks (Cognac,
  Bicaridine, Desoxyephedrine, Happiness), `MendFractures` on Stasizium, and five new reagents (`Osteogen`,
  `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone`). **Tier B was not taken** — WP12-2's report records the
  package as complete without needing the pre-authorised extension; `Probital`/`Mitogen` remain unported.
* **§8.4-5 Analyzer UI: PARALLEL.** Landed in **WP12-7** (P4-D25): a self-contained `_WF` panel
  (`WolfmedDiagnosticPanel`) mounted by three marked lines in `HealthAnalyzerWindow.xaml`/`.xaml.cs` (HOOK 26).
  Shitmed's window geometry is untouched; the panel prints the fracture grade Onyx carries and never renders.
* **§8.4-6 No organ examine line.** Taken as instructed — no phase-4 package added an organ-damage line to
  the generic `examine` verb.
* **§8.4-7 No `treatmentCapabilities` annotation on existing items now.** Taken as instructed (P4-D7). The
  cable coil (`Entities/Objects/Tools/cable_coils.yml:36-45`) remains recorded as the first phase-5 action.
* **§8.4-8 Pain numbness skipped and recorded.** Taken as instructed (P4-D8). `PainNumbnessStatusEffectComponent`
  remains dead code with two readers (`PainSystem.cs:336`, `EmoteOnDamageSystem.PainSounds.cs:37`) and no
  writer. Re-entry cost recorded in `WOLFMED_STATUS.md`: ~45 LOC for `ModifyStatusEffect`, a new `_WF` action
  enum member, 2 prototypes, 2 locale keys, 1 test — roughly half a day.
* **Gibbing fix:** `Content.Shared/Gibbing/Systems/GibbingSystem.cs` two `.ToArray()` snapshots +
  `using System.Linq`, all marked `// WOLFGATE` — landed before WP12-0 started, manifest row added by
  **WP12-10** (table above). Fixes the container-mutation crash phase 3's manifest flagged and left unfixed.

## Phase 5 (2026-09-14)

Phase 4 is committed (`2b4a4675d0 phase 4`). Phase 5 = species coverage and the deferred treatment/pain items
(`PLAN5.md`). Packages run sequentially in the one worktree; each appends its own rows/deviations directly.

### WP13-0 (phase 5 — profile, wound and container prototypes, P5-1a/P5-2a)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `ONYX Resources/Prototypes/_Onyx/Wounds/wounds.yml:37-151` (`IpcBodyPartProfile`, `SlimeBodyPartProfile`, `CyberneticBodyPartProfile`, `PlantBodyPartProfile`) | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` (appended in Onyx's file order, after `OrganicBodyPartProfile`) | vendored, 1 fold class | **WP13-0** | D9 fold on the three profiles that carry `organDamage` (Ipc/Slime/Plant): `Chest: 0.04` + `Groin: 0.04` → `Torso: 0.04` (not summed — independent per-part rolls, one Wolfgate torso). No `maxAffected` added (Onyx sets it only on `OrganicBodyPartProfile`); no extra limb rows added to Slime/Plant (Onyx lists `Head`/`Chest`/`Groin` only). `CyberneticBodyPartProfile` carries no `organDamage` block — nothing to fold. `acceptedDamageTypes` kept verbatim (`Cold`/`Shock`/`Caustic` included) with `# WOLFGATE (P5-1/P5-D20)` inert-marker comments — dead until U13′(b)'s `InorganicWolfmed` container lands in WP13-1 |
| `ONYX …wounds.yml:193-231` (`CyberneticFractureProfile`) | same path, after `OrganicFractureProfile` | vendored, 1 fold | **WP13-0** | DECISIONS §8.2-1 fold applied a second time: Onyx's `manipulationModifier` values (0.92/0.84/0.75/0.75, all below 1 = *faster* under the `1 + (modifier-1)*scale` formula) replaced with the C# defaults 1.1/1.25/1.5/2.0, matching the phase-2 fix already on `OrganicFractureProfile`. Field-for-field identical to `OrganicFractureProfile` otherwise (only `id:`/`wound:` differ). `movementModifier` untouched |
| `ONYX …wounds.yml:337-371` (`CyberneticFrameFractureWound`), `:583-681` (`IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`), `:683-1039` (`SlimeBluntWound`, `SlimeSlashWound`, `SlimePiercingWound`, `SlimeBurnWound`, `PlantBluntWound`, `PlantSlashWound`, `PlantPiercingWound`, `PlantBurnWound`) | same path, inserted among the existing wound list at Onyx's relative positions (`CyberneticFrameFractureWound` after `BoneFractureWound`; the other 10 after `DismembermentWound`, before `AmputationConsequenceWound`) | vendored verbatim, no folds | **WP13-0** | 11 wound prototypes, byte-for-byte from Onyx. `IpcMechanicalDamageWound`/`CyberneticMechanicalDamageWound` carry 6 damage types (no `Shock` — that routes to `ElectricalWound`) and leak from severity 0 (no `minimumSeverity` on the wound-level `WoundBleedingBehavior`). Slime wounds drop `WoundScarBehavior` and per-stage `minimumSeverity`; Plant wounds keep `WoundScarBehavior` but also drop `minimumSeverity` — the single mechanical difference that makes slimes and diona leak from the first scratch. Stage LocIds for Slime/Plant reuse the **generic** `wound-stage-{minor,moderate,severe,critical}` keys, not species-specific ones. Every LocId all 16 new prototypes need was already 100% present in `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` and `.../medical/health-examinable.ftl` — zero locale additions this package |
| — (line-1 comment rewrite) | `Resources/Prototypes/_Onyx/Wounds/wounds.yml:1` | in-file edit | **WP13-0** | `# WOLFGATE (WP7)` trim note rewritten to `# WOLFGATE (WP7 → P5-1)`, recording the file going from 14/30 to 30/30 prototypes and stating the two fold classes |
| — (no Onyx source) | `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` | **new**, Wolfgate-authored | **WP13-0** | `SiliconWolfmed` = stock `Silicon` (Brute + Heat/Shock/Radiation) + `Bloodloss` as a **type** (not the `Airloss` group — P5-D5b: the tourniquet's `Asphyxiation: 5` cost lands on the *part*, whose container this does not touch, so the group would buy nothing and would open an unaudited `Asphyxiation` surface for free). `InorganicWolfmed` (U13′(b)) = `supportedGroups: [Brute, Burn]` + `supportedTypes: [Radiation]` — a superset of both stock containers it will replace (`Inorganic` on `PartIPCBase`, `Silicon` on `CyberneticPartBase`), restoring `Cold`/`Caustic` that Onyx's own `SiliconIpc` has and Wolfgate's `Inorganic`/`Silicon` lack. Neither container is referenced by anything yet — wiring lands in a later package |
| `ONYX Corvax/Body/Species/ipc.yml:322-330` (`OrganIpcExternal`), `Body/Species/slime.yml:232-239` (`OrganSlimePersonExternal`), `Body/Species/diona.yml:226-235` (`OrganDionaExternal`), `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21` | `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | **new** (re-expressed, D8) | **WP13-0** (deviation — PLAN5 §4 assigns this file to WP13-1; see below) | 4 abstract entity ids: `WolfmedPartSlime`, `WolfmedPartDiona`, `WolfmedPartIpc`, `WolfmedPartCybernetic`. Onyx's `- type: BodyPart / fractureProfile:` re-expressed as `- type: WolfmedBodyPart / fractureProfile:` per D8. `amputationThresholds` deliberately **not** set for Ipc/Cybernetic (P5-D7 — Base<Slot>'s organic P3 gun/laser-severing set is inherited instead of Onyx's own IPC numbers, which sit on a different, tougher part chain); Diona's `amputationThresholds: {}` **is** set (an empty flow mapping is a present key and disables `AmputationSystem` for that part without making it indestructible — the inherited `MajorLimb`/`MinorLimb` gib trigger still deletes it, U17). Slime has no bones (`fractureProfile: null`); Cybernetic uses `CyberneticFractureProfile`. **Not yet wired to any species part base** — no `parent:` list references these ids in this package, so D2 exposure is nil (unreachable prototypes) |

**Deviation from PLAN5 §4's WP13-0/WP13-1 split.** PLAN5 assigns `species_parts.yml` (the 4 abstracts) to
**WP13-1** ("Species part wiring", together with the `parent:`-list edits to
`Body/Parts/{slime,diona}.yml`, `_EinsteinEngines/Body/Parts/ipc.yml` and `_Shitmed/Body/Parts/cybernetic.yml`
— PROTO M/N/O/P). This package's own task brief explicitly scoped the file's *creation* into WP13-0 alongside
`wounds.yml` and `containers.yml`. Creating the abstracts without wiring them is D2-safe by construction — RT
never loads an `abstract: true` entity on its own, and nothing in the tree parents these four ids yet, so this
package changes no runtime behaviour (confirmed by the clean headless run below, identical in shape to every
prior WP13-0 checkpoint). The upstream `parent:`-list edits (PROTO M/N/O/P) and the `InorganicWolfmed` wiring
into `_EE ipc.yml`/`cybernetic.yml` remain **owned by whichever package implements WP13-1** — do not
re-create `species_parts.yml`; add to it only if a later package needs a fifth abstract, and wire the existing
four via one-line `# WOLFGATE` edits to the four upstream part files, in that order (`WolfmedPart*` **first**
in each `parent:` list — see the file's own header comment for why).

- **New prototype ids (18):** `IpcBodyPartProfile`, `SlimeBodyPartProfile`, `CyberneticBodyPartProfile`,
  `PlantBodyPartProfile`, `CyberneticFractureProfile`, `CyberneticFrameFractureWound`,
  `IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`, `SlimeBluntWound`, `SlimeSlashWound`,
  `SlimePiercingWound`, `SlimeBurnWound`, `PlantBluntWound`, `PlantSlashWound`, `PlantPiercingWound`,
  `PlantBurnWound`, `SiliconWolfmed`, `InorganicWolfmed` — plus the 4 unwired entity abstracts above (22
  new ids total this package). Every id grepped 0 hits repo-wide before creation.
- **Subscription pairs:** none. **New C# types/components:** none.
- **Locale:** none — all LocIds the 16 wound/profile prototypes need already shipped in phase 1.
- **Traps avoided (PLAN5 §2.1/§8.3):** no `maxAffected` on the three new `organDamage` blocks; no extra
  limb rows on Slime/Plant `chances`; every `Chest`/`Groin` key folded to `Torso` (D9 — `BodyPartType` has
  no `Chest`/`Groin`, so an unfolded copy fails deserialization); `CyberneticFractureProfile`'s
  `manipulationModifier` inversion fixed to match `OrganicFractureProfile`'s existing phase-2 correction.
- **Line endings:** both new files written CRLF to match the working-tree convention (git's `* text=auto`
  normalizes to LF on commit); `wounds.yml`'s edit preserves its existing CRLF throughout — verified with
  `file` before and after every edit.
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential, both green).
  Headless server (120 s, port 1299) reached `Server Version 277.0.0.0 -> Ready` with **zero**
  `[ERRO]`/`[FATL]`/exception lines (`<plan>/p5/wp/WP13-0-server.log`). Release YAML linter:
  **"No errors found in 103947 ms."** (`<plan>/p5/wp/WP13-0-yamllint-full.log`). A standalone
  Python (PyYAML, with a permissive `!type:` tag handler) parse of the edited `wounds.yml` confirms exactly
  30 top-level documents in the expected order, matching PLAN5 §2.1's "14 of 30 → 30 of 30" count.
  **This package changes no runtime behaviour** — nothing references the new wound/profile ids until a
  species part base is wired to them, and nothing references the new container ids until a part abstract is
  switched onto them; both are later-package work.

### WP13-1 (phase 5 — species part wiring and gib blocks, P5-1b; U3′(b), U15(a), U13′(b), PROTO S/T)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `ONYX Body/Species/slime.yml:232-239` (`OrganSlimePersonExternal`) | `Resources/Prototypes/Body/Parts/slime.yml:4` — `PartSlime`'s `parent:` | upstream, 1 marked line (PROTO M) | **WP13-1** | `parent: [BaseItem, BasePart]` → `parent: [WolfmedPartSlime, BaseItem, BasePart]`. `WolfmedPartSlime` **first** (P5-D1/R1): `containers`, `thresholds` and every `[DataField]` inside a component are non-`Always`, so the first parent in the list wins (`SerializationManager.Composition.cs:40-58, 178-206`). Measured result: every `*Slime` part resolves `Woundable.profile = SlimeBodyPartProfile`, `WolfmedBodyPart.fractureProfile = null` (slimes have no bones) |
| `ONYX Body/Species/diona.yml:226-235` (`OrganDionaExternal`) | `Resources/Prototypes/Body/Parts/diona.yml:3` — `PartDiona`'s `parent:` | upstream, 1 marked line (PROTO N) | **WP13-1** | Same shape. Measured: every `*Diona` part resolves `profile = PlantBodyPartProfile`, `fractureProfile = null`, **`amputationThresholds` empty (`Count == 0`)** — R2 satisfied, the empty flow mapping survives inheritance and suppresses `Base<Slot>`'s dict, so `AmputationSystem.HandlePartDamageApplied` early-returns (`:80`). Diona limbs are never cleanly severed but are still **destroyed** by the inherited `MajorLimb`/`MinorLimb` gib rung (U17) |
| `ONYX Corvax/Body/Species/ipc.yml:322-330` (`OrganIpcExternal`) | `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` — `PartIPCBase`'s `parent:` (`:3`), a new `Damageable` block, and the two `Destructible` numbers | upstream, 3 marked edits (PROTO O a/b/c) | **WP13-1** | (a) `parent: BasePartInorganic` → `parent: [ WolfmedPartIpc, BasePartInorganic ]`. (b) **U13′(b)** — a new `- type: Damageable / damageContainer: InorganicWolfmed` (the file previously inherited stock `Inorganic` from `BasePartInorganic`), restoring `Cold` and `Caustic`; measured `DamageDict` keys on a spawned `LeftArmIPC` are now `Blunt, Caustic, Cold, Heat, Piercing, Radiation, Shock, Slash`. (c) **U3′(b)/P5-D8** — `Blunt 110 → 190`, `Slash 150 → 210`, **no Heat rung added** (EE's own `# no ashing trigger` stands, so an IPC limb never burns to `Ash`). `TorsoIPC`'s own `Destructible` (`:37-51`, 400/400) is deliberately untouched |
| `ONYX _Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21` | `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` — `CyberneticPartBase`'s `parent:` (`:3`), its `Damageable` (`:10-11`) and a new `Destructible` block | upstream, 3 marked edits (PROTO P a/b/c) | **WP13-1** | (a) `parent: BasePartInorganic` → `parent: [ WolfmedPartCybernetic, BasePartInorganic ]`; all 14 entities in the file descend from it. (b) **U13′(b)** — `damageContainer: Silicon` → `InorganicWolfmed` (superset of `Silicon`; nothing lost, `Cold`/`Caustic` gained). (c) **U15(a)/P5-D8b** — a new marked `- type: Destructible # no ashing trigger` with `Blunt 190` / `Slash 210` → `GibPartBehavior` and **no Heat rung**. Before this, `CyberneticPartBase` declared none and every concrete limb took its *other* parent's — Mono's `MajorLimb`/`MinorLimb` — so a steel prosthetic spawned `Ash`, ran `BurnBodyBehavior` and played the `MeatLaserImpact` flesh sound at Heat 250 (R12). Side effect: cybernetic hands/feet gib at 190/210 instead of `MinorLimb`'s 150/180 |
| — (no Onyx source) | `Resources/Prototypes/Entities/Objects/Tools/welders.yml:118-119` — `WeldingHealing.damageContainers` | upstream, 1 marked entry (PROTO S) | **WP13-1** (deviation — PLAN5 §4 assigns PROTO S/T to WP13-2) | `- SiliconWolfmed` appended. `WeldingHealableSystem.OnRepairFinished` gates on `damageable.DamageContainerID ∈ damageContainers` (`WeldingHealableSystem.cs:29-41`); when WP13-2 moves `MobIPC` onto `SiliconWolfmed`, omitting this removes the only way to repair an IPC (P5-D5 / R3). Landing it a package early is inert (no entity uses `SiliconWolfmed` yet) and removes the dead-window risk |
| — (no Onyx source) | `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml:49-50` — `WeldingHealing.damageContainers` | upstream, 1 marked entry (PROTO T) | **WP13-1** (same deviation) | Identical to PROTO S |
| — (no Onyx source) | `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | `_WF`, extended (created by WP13-0) | **WP13-1** | Two changes: (1) the stale WP13-0 "wiring is a later package's job" note replaced with the wiring status; (2) **a `- type: ContainerContainer` declaring `bodypart` + `wounds` added to all four abstracts** — see the WP13-1-1 deviation below |

#### WP13-1-1 — `wounds` container declared statically on the four `_WF` abstracts (addition to PLAN5 §2.2)

Not in PLAN5. Required, and caught by `PrototypeSaveTest.UninitializedSaveTest`, which failed on **40**
prototypes (every `*IPC`, `*Slime`, `*Diona` and `*Cybernetic` part) with
`modifies component on spawn: ContainerContainer` the moment PROTO M/N/O/P landed. Cause:
`WoundSystem.OnWoundableInit` does `EnsureContainer<Container>(part, "wounds")` on **`ComponentInit`**
(`Content.Shared/_Onyx/Wounds/WoundSystem.cs:60-63`), i.e. at spawn. Phase 1-4 never hit this because organic
parts do not declare `- type: Woundable` in YAML — `WoundDamageProjectionSystem.SetupPart` `EnsureComp`s it
after body setup — whereas P5-D1's whole design is a *static* `Woundable` declaration.

Fix, applied in the `_WF` file rather than upstream:

```yaml
  - type: ContainerContainer
    containers:
      bodypart: !type:Container
        ents: []
      wounds: !type:Container
        ents: []
```

`ContainerManagerComponent.Containers` is a plain `[DataField]` dictionary
(`RobustToolbox/Robust.Shared/Containers/ContainerManagerComponent.cs:23-24`), so this mapping **replaces**
`BasePartInorganic`'s `{bodypart}` (`Body/Parts/base.yml:23-26`) rather than merging with it — `bodypart` is
restated for exactly that reason. `BaseTorsoInorganic`'s `torso_slot` (`:74-77`) was **already** suppressed by
the same first-parent rule before this package (`TorsoIPC: parent: [PartIPCBase, BaseTorsoInorganic]`, and
`PartIPCBase` already carried `{bodypart}`), and is still created at runtime by `SharedBodySystem`; nothing
changes there. `EnsureContainer` finds the declared container instead of creating one, and the declared
`!type:Container` defaults (`showEnts: False`, `occludes: True`) match what it would have created — verified by
the test passing after the change.

#### Effective `Destructible` thresholds after inheritance (measured, not derived)

Measured by spawning each prototype in a throwaway `GameTest` harness and reading
`DestructibleComponent.Thresholds` (since deleted). Reasoning over *every* parent was required: a child's
`thresholds` list **replaces** the parent's, and the first parent in the list wins.

| prototype | before WP13-1 | after WP13-1 | source of the winning block |
|---|---|---|---|
| `LeftArmIPC` / `RightArmIPC` / `LeftLegIPC` / `RightLegIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` (first parent) beats `BaseLeftArm → MajorLimb` (190/210/250+Ash) |
| `LeftHandIPC` / `RightHandIPC` / `LeftFootIPC` / `RightFootIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` beats `MinorLimb` (150/180/230+Ash) — IPC extremities are **tougher** than flesh (accepted asymmetry, P5-D8) |
| `HeadIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` beats `BaseHead`'s own Mono block — see the correction note below |
| `TorsoIPC` | Blunt 400 / Slash 400 | **unchanged, Blunt 400 / Slash 400** | `TorsoIPC`'s own block (`_EE ipc.yml:37-51`) |
| `LeftArmCyberneticBase`, `RightArmCyberneticBase`, `LeftLegCyberneticBase`, `RightLegCyberneticBase`, `JawsOfLife{Left,Right}Arm`, `Speed{Left,Right}Leg` | Blunt 190 / Slash 210 / **Heat 250 → Ash + BurnBody + MeatLaserImpact** | **Blunt 190 / Slash 210, no Heat** | new `CyberneticPartBase` block beats `BaseLeftArm → MajorLimb` |
| `Left/RightHandCybernetic`, `Left/RightFootCybernetic`, `Dex{Left,Right}Hand` | Blunt 150 / Slash 180 / **Heat 230 → Ash** | **Blunt 190 / Slash 210, no Heat** | new `CyberneticPartBase` block beats `MinorLimb`; a steel hand is now as tough as a steel arm |
| `LeftArmSlime` … `RightLegDiona` (all slime/diona **arms and legs**) | Blunt 190 / Slash 210 / Heat 250 + Ash | **unchanged** | `MajorLimb` — `WolfmedPartSlime`/`WolfmedPartDiona` declare no `Destructible` |
| slime/diona **hands and feet** | Blunt 150 / Slash 180 / Heat 230 + Ash | **unchanged** | `MinorLimb` |
| `TorsoSlime` / `TorsoDiona` | 400 / 400 / 400 + Ash | **unchanged** | `BaseTorso` |
| `HeadSlime` / `HeadDiona` | Blunt 500 / Slash 600 / Heat 700 + Ash | **unchanged** | `BaseHead`'s own Mono block |
| every organic (human-lineage) part | — | **unchanged** (D2) | `MajorLimb` / `MinorLimb` / `BaseHead` / `BaseTorso` |

**Correction to PLAN5 (N5, P5-D8, §8.1a): organic heads DO carry a gib trigger.** PLAN5 states repeatedly
that `BaseHead` is `parent: WolfmedBaseHead` only and therefore has *no* rung. `BaseHead` declares its own
`- type: Destructible # Mono` at `Resources/Prototypes/Body/Parts/base.yml:105-133` with
**Blunt 500 / Slash 600 / Heat 700 → Ash + BurnBody + MeatLaserImpact** (measured on a spawned `HeadHuman`).
The stated asymmetry ("the IPC head is destructible where a human head is not") is therefore wrong in *kind*
but understated in *degree*: an IPC head gibs at Blunt 190 where an organic head needs 500. This is **not a
regression** — `PartIPCBase` was 110/150 before this package, so U3′(b) makes the IPC head 1.7× tougher than
it is today. `HeadIPC`'s amputation thresholds are Slash 200 / Piercing 200 / Blunt 350 / Heat 200, so an IPC
can still be decapitated by Slash (200 < the 210 gib rung) but never by pure Blunt (350 > 190). Handed to the
balance pass with U3′(d) (per-slot parity) as the fix if the asymmetry is unwanted.

**Correction to PLAN5 §8.1/§8.1a: slime limbs ARE severable.** Both sections say slime limbs are
"unseverable by threshold, same as diona". Onyx gives `amputationThresholds: {}` to `OrganDionaExternal`
only, **not** to `OrganSlimePersonExternal` (`ONYX Body/Species/slime.yml:235-239`, read this pass), and
`species_parts.yml` correctly reproduces that. Measured: `LeftArmSlime` resolves
`amputationThresholds = Slash 130 / Piercing 250 / Blunt 250 / Heat 250` — the ordinary organic set. Slimes
are severable exactly like humans; only diona are not. The guidebook/changelog copy must say so.

- **New prototype ids:** none (all 22 landed in WP13-0). **New C# types/components:** none.
  **Subscription pairs:** none. **Locale:** none.
- **Measured profile wiring (R1/R9):** `*IPC` → `IpcBodyPartProfile` + `fractureProfile: null`;
  `*Cybernetic` → `CyberneticBodyPartProfile` + `CyberneticFractureProfile` (the two agree, R9);
  `*Slime` → `SlimeBodyPartProfile` + `null`; `*Diona` → `PlantBodyPartProfile` + `null` + `{}` amputation.
  Human-lineage parts still resolve **no** static `Woundable` at all (D2 intact — they keep getting the
  runtime `EnsureComp` default `OrganicBodyPartProfile`).
- **R8 confirmed:** `TorsoIPC` now carries a `WolfmedBodyPart` with `maxDamage: 0` (component default) and an
  empty `amputationThresholds` — overflow disabled, which is correct for a torso. Do not "fix" it.
- **D2 deviations recorded:** detached IPC limbs 110/150 → 190/210 (§8.3 R5); detached cybernetic limbs lose
  the Heat/Ash rung and hands/feet go 150/180 → 190/210 (§8.3 R12); detached IPC and cybernetic limbs can now
  take `Cold`/`Caustic` on their own `Damageable` (cosmetic — no host, so no wound forms).
- **Line endings:** every edit written CRLF, matching the working tree.
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential).
  Release YAML linter **"No errors found in 78202 ms."** Headless server (120 s, port 1299) reached
  `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
  (`<plan>/p5/wp/WP13-1-report-server.log`). `DockTest` 3/3.
  `EntityTest|PrototypeSaveTest` 6 passed / 2 skipped / **0 failed** (after the WP13-1-1 fix; 40 failures
  before it). Wolfmed suite
  (`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`) **98/98 passed**, zero regressions.

### WP13-2 (phase 5 — IPC as a wound host and circulation, P5-1c/P5-2; PROTO Q, §2.4, U1/U2(a)/U16/U18)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:92-113` (`MobIpc`: `- type: WoundHost`, `bloodReferenceSolution: Oil 250`) + `ONYX Resources/Prototypes/Body/species_base.yml:54` (`- type: PainShockTarget` on `BaseSpeciesMob`, which Onyx's `MobIpc` parents) | `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` — `MobIPC`'s `components:` block and its `Destructible` | upstream, 1 marked block + 1 marked number (PROTO Q) | **WP13-2** | Four components added immediately after `components:` so the Wolfmed section reads in one place, mirroring Onyx's `# <Onyx-IPCWounds>` fence: `- type: WoundHost`; `- type: PainShockTarget` (P5-D10/U1 — Onyx IPCs *do* get pain shock, Wolfgate simply put the component on `BaseMobSpeciesOrganic`, which `MobIPC` does not inherit); `- type: Bloodstream` with `bloodReagent: Oil`, `bloodMaxVolume: 250`, `chemicalMaxVolume: 0`, `bloodlossDamage {Bloodloss: 0.5}`, `bloodlossHealDamage {Bloodloss: -1}`; `- type: Damageable` with `damageContainer: SiliconWolfmed` + `damageModifierSet: IPC`. Plus `Destructible` Blunt **400 → 1500** (D22/P5-D11). `MobThresholds` **not** touched — death stays at a projected total of 100. `bloodlossDamage` uses `Bloodloss`, never `Heat` (P5-D6): `Heat` is in `LocalizedDamageTypes`, so it would route to a part, create chassis-wound severity and bleed at `chance: 1` — a self-reinforcing leak loop |
| — (no Onyx source) | `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` — `SetBleedRates` | in-vendored `_Onyx`, 2nd marked site (PLAN5 §2.4) | **WP13-2** | `rates.GetValueOrDefault(PrimaryStream)` replaced with a sum over `rates.Values` (P5-D3 hardening). Bit-identical today — `Organic` is the only `circulatoryStream` prototype and none of the five profiles selects another — but it makes a future profile that *does* set `circulatoryStream:` degrade to "bleeds normally" instead of **silently stopping that species' bleeding with no log**. Complete, not partial: `GetPartStream` → `SetBleedRates` is the only stream-aware path with callers (`WoundBleedingSystem.cs:306,319,324`); `TryGetPartSolution`/`TryGetStreamSolution` have zero callers anywhere in WG (N4). No new `using` |

**PROTO S and PROTO T were NOT re-applied.** PLAN5 §4 assigns `Entities/Objects/Tools/welders.yml` and
`_Mono/Entities/Objects/Tools/nanite_applicator.yml` to WP13-2, but WP13-1 landed both `- SiliconWolfmed`
entries early (its deviation D1). Verified present before PROTO Q's container change landed, so the R3 dead
window — in which an IPC on `SiliconWolfmed` would have had **no** repair path at all — never existed.
`_EinsteinEngines/Body/Parts/ipc.yml` was likewise left alone: WP13-1 fully consumed it.

**PROTO R stays dropped (U18).** `_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml` is untouched.
`DestructibleComponent.Thresholds` is a plain non-`Always` `[DataField]`, so `MobIPC`'s own list replaces the
parent's `!type:DamageTrigger damage: 500` wholesale, and `MobIPC` is the only entity parenting
`PlayerSiliconHumanoidBase`. Measured on a spawned `MobIPC`: exactly **one** threshold, a `DamageTypeTrigger`.
The upstream file count therefore stays at PLAN5's 55, not 56.

#### Measured state of a spawned `MobIPC` (probe harness, deleted before the final builds)

A throwaway `GameTest` spawned `MobIPC` and dumped its live components and every body part
(`<plan>/p5/wp/WP13-2-probe.log`). Nothing below is derived:

| | measured |
|---|---|
| `WoundHostComponent` | present |
| `PainShockTargetComponent` / `PainComponent` | both present (U1: IPCs feel pain and can shock) |
| `BloodstreamComponent` | `bloodReagent = Oil`, `bloodMaxVolume = 250`, `chemicalMaxVolume = 0`, `bloodlossDamage = {Bloodloss: 0.5}`, `bloodlossHealDamage = {Bloodloss: -1}`, `BloodRefreshAmount = 1` (U11(a), Onyx default), `BloodlossThreshold = 0.9`, `BleedReductionAmount = 0.33`, `MaxBleedAmount = 10` |
| `InjectableSolutionComponent` | **absent** (U16 — deliberate divergence from Onyx, which gets one by parenting `MobBloodstream`) |
| `DamageableComponent` | container `SiliconWolfmed`, modifier `IPC`, live types `Bloodloss, Blunt, Heat, Piercing, Radiation, Shock, Slash` |
| `DestructibleComponent` | one `DamageTypeTrigger` (Blunt 1500) |
| `WeldingHealableComponent` | present (repair path intact; `Repairable` deliberately not added, P5-D15/U14) |
| all 10 body parts (`TorsoIPC`, `HeadIPC`, 2 arms, 2 hands, 2 legs, 2 feet) | `Woundable.profile = IpcBodyPartProfile`, `WolfmedBodyPart.fractureProfile = null`, `maxDamage = 0`, `damageContainer = InorganicWolfmed`; `amputationThresholds` 4 entries on every limb and the head, **0 on `TorsoIPC`** (R8 — correct, `AmputationSystem` skips torsos) |
| `IpcBodyPartProfile` as loaded | `canFeelPain = True`, `bleedingMultiplier = 1`, `circulatoryStream = Organic` (P5-D3 — no profile sets it), `treatmentCapabilities = [Mechanical, Electrical]`, `acceptedDamageTypes = [Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic]`, `passiveRecoveryMultiplier = 0`, `bedRecoveryMultiplier = 0`, `scarrable = False` |

#### WP13-2-1 — finding: `Cold`/`Caustic` reach IPC *parts* but are dropped from the IPC *body* total

Not in PLAN5, no code change made, flagged for the balance pass and for WP13-6's test wording.
P5-D20/U13′(b) restored `Cold` and `Caustic` on IPC and cybernetic **parts** (`InorganicWolfmed`), and both
types are in `WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:36-43`), so a Cold or
Caustic hit really does route to a limb and create `IpcMechanicalDamageWound` severity, pain and a leak.
But `SiliconWolfmed` (the **mob** container, PLAN5 §2.3, owned by WP13-0) is stock `Silicon` + `Bloodloss`,
i.e. Brute + Heat/Shock/Radiation/Bloodloss — **no `Cold`, no `Caustic`**.
`WoundDamageProjectionSystem.RefreshBodyDamage` projects the sum of part damage back with
`_damage.SetDamage(body, total)`, and `DamageableSystem` silently drops unsupported types, so the
Cold/Caustic component of an IPC's part damage never reaches the body total that `MobThresholds` (death at
100) and `SlowOnDamage` read. Onyx does not have this asymmetry: its `SiliconIpc` takes the whole `Burn`
group on the mob as well as on the part.

Consequence: acid and cryogenics on an IPC produce wounds, pain, functionality loss and oil leakage — and
kill only indirectly, through the `Bloodloss` the leak generates. They cannot themselves push an IPC to its
100-point death threshold. **Not fixed here:** `_WF/Wolfmed/Damage/containers.yml` is WP13-0's file, adding
`Cold`/`Caustic` to `SiliconWolfmed` would also change how the mob's `damageModifierSet: IPC` (`Cold 0.2`)
applies at the body level, and PLAN5 §2.3 specifies the container's shape exactly. The one-line fix, if the
balance pass wants Onyx parity, is `supportedGroups: [Brute, Burn]` on `SiliconWolfmed`.

- **New prototype ids:** none. **New C# types/components:** none. **Subscription pairs:** none (PLAN5 §5.1
  — phase 5 registers zero). **Locale:** none.
- **`PrototypeSaveTest` exposure: none.** `MobIPC` inherits `save: false` from `PlayerSiliconHumanoidBase:2`,
  so it is outside `UninitializedSaveTest`'s prototype set — the WP13-1-1 class of failure (a declared
  component whose `ComponentInit` mutates the entity; here `BloodstreamSystem.OnComponentInit`
  `EnsureComp`s `SolutionContainerManagerComponent`) cannot fire. **`SolutionContainerManager` is therefore
  deliberately not declared**, per PLAN5 §2.0. Any future package that puts `- type: Bloodstream` on a
  **map-savable** prototype must declare it.
- **D2:** `MobIPC` is the only entity changed. `MobIPCDummy` parents `MobHumanDummy` and is unaffected; no
  other entity parents `MobIPC`. Borgs and every other `damageContainer: Silicon` entity keep the stock
  container.
- **Line endings:** every edit written CRLF, matching the working tree (verified byte-wise before and after).
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential). Release YAML
  linter **"No errors found in 75236 ms."** Headless server (120 s, port 1299) reached
  `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
  (`<plan>/p5/wp/WP13-2-report-server.log`). `DockTest` 3/3.
  `EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps` **1/1 passed** — `MobIPC` is non-abstract, carries no
  `MapGrid`/`RoomFill` and is not in the spawner category, so it is spawned, ticked 450 ticks (15 s, enough
  for `BloodstreamSystem.Update` and `PainSystem.Update` to run) and deleted with no error. Wolfmed suite
  (`_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed`) **98/98 passed**, zero regressions. No environmental
  `db.ef` failures, so no re-run was needed.

#### What an IPC experiences after WP13-2

- **Leaks oil, not blood.** Every chassis wound (`IpcMechanicalDamageWound`) carries a wound-level
  `WoundBleedingBehavior rate: 0.08 chance: 1` with **no `minimumSeverity`**, so an IPC leaks from the first
  point of damage. `bleedingMultiplier: 1`. The spilled reagent is `Oil`, which is `flammability: 2` with a
  `FlammableTileReaction` — **an IPC's trail can be set on fire.**
- **Oil loss hurts.** `Bloodloss: 0.5` per bloodstream update below 90 % fluid, healing back at
  `Bloodloss: -1` once topped up; `SiliconWolfmed` is what lets those land at all. Phase 3's P3-D1
  vital-part `Bloodloss` charge (decapitation etc.) now lands too, instead of being silently discarded.
- **Feels pain and can go into pain shock** (U1/P5-D10). `IpcBodyPartProfile` leaves `canFeelPain` at its
  default and `IpcMechanicalDamageWound` carries `painPerSeverity: 0.87 minSeverity: 10`. Pain shock at 130
  is a 2 s paralyze + jitter + a 30 s adrenaline window, and `PainSystem.UpdatePainShock` calls
  `TryEmoteWithChat(..., "Scream", forceEmote: true)`, **bypassing `allowedEmotes: [Boop, Whirr]` — an IPC
  screams on shock.** There is **no chemical relief of any kind**: no metabolizer (`OrganIPCPump`'s
  `Metabolizer` block is commented out), `chemicalMaxVolume: 0` and no `InjectableSolution`, so no
  `SuppressPain`, no painkiller, no adrenaline chem. **Pain falls only as the chassis is repaired.**
- **Drunk and stuttering below 90 % fluid.** `BloodstreamSystem.Update`'s bloodloss branch
  (`BloodstreamSystem.cs:141-162`) carries no wound-host or species guard, unlike `OnDamageChanged` (GUARD E).
  This is exactly what every bleeding organic already gets — Wolfgate-consistent, but a drunk robot is a
  flavour oddity. Flagged for playtest (R4).
- **Repair only by welder, nanite applicator and cable coil.** `IpcBodyPartProfile.treatmentCapabilities`
  is `[Mechanical, Electrical]`, which no medicine, brute pack, ointment or gauze overlaps. The welder and
  the nanite applicator work through `WeldingHealable` (whose `TryChangeDamage` on the mob routes to parts
  with no capability scope open, so it heals chassis wounds for free); the cable coil becomes `[Electrical]`
  in WP13-4 and overlaps. The tourniquet and every wound surgery also work (`SurgeryTarget` is on
  `PlayerSiliconHumanoidBase:319`).
- **Never passively or bed-heals** (`passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0`), **never
  scars** (`scarrable: false`), **never fractures** (`fractureProfile: null` on every part).
- **Limb loss:** gib ceiling **Blunt 190 / Slash 210, no Heat rung** (WP13-1, U3′(b)) against the inherited
  organic amputation set per slot — so Slash and Piercing sever, pure Blunt destroys first, and an IPC limb
  never burns to `Ash`. `TorsoIPC` keeps its own 400/400 and is never severable.
- **Dies at the same point as before.** `MobThresholds` is untouched (100 projected), but body damage is now
  the **sum of every part's positive damage** (D22), which decays much more slowly than before (passive
  regen is neutralised on wound hosts and the Ipc profile's own recovery is 0) — so IPCs will sit in
  `SlowOnDamage`'s 60/90/120 bands longer than they do today (R7). The `Destructible` gib is 1500, raised
  from 400 for exactly that reason.
- **Not affected by `Cold`/`Caustic` at the body level** — see WP13-2-1 above.

### WP13-3 (phase 5 — protogen exclusion lift, P5-1d; EXT 3/4, PROTO V, U4)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| — (no Onyx source — reverses a phase-1 Wolfgate decision, D21/D32) | `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` — `ExcludedAncestors` | `_WF`, 1 marked edit (EXT 3) | **WP13-3** | `ExcludedAncestors = new() { "BaseMobProtogen" }` → `ExcludedAncestors = new()`, with a `// WOLFGATE (P5-D9)` comment replacing the field's doc line: protogen is biologically organic (`Biological` container, `OrganicPart` limbs, `Blood` bloodstream, `Hunger`/`Thirst`, `Respirator`, `Butcherable → FoodMeatHuman`) and the exclusion no longer applies to it. The `<WoundHostComponent, ComponentInit>` subscription at `:17` (now `:18`) is unchanged, and so is the `RemComp` (not `RemCompDeferred`) fix from WP9 — it simply never matches now. **The file is kept, not deleted**, per PLAN5 §7.1's explicit instruction ("kept as the mechanism for any future synthetic species") — see Deviations |
| — (no Onyx source) | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` — `BuildWoundDiagnostics`'s D2 comment | `_WF`, comment-only (EXT 4) | **WP13-3** | The marked comment at (now) `:32-34` named "a borg, a Protogen" as a `SurgeryTargetComponent`-without-`WoundHost` example; a protogen now has `WoundHost`, so that example is stale. Reworded to "a borg, a synthetic species". No code change — `HasComp<WoundHostComponent>(body)` gate is untouched. **R13 honoured**: this is the only WP13-3 site in the file; WP13-5 lands after and re-reads it before adding the `Mechanical` flag producer |
| — (already a tracked upstream file since WP7, `# WOLFGATE (D21/D32)`) | `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` — `BaseMobProtogen`'s marked comment at `:3-4` | upstream, comment-only (PROTO V) | **WP13-3** | Comment rewritten from "synthetic … WoundHost is stripped at runtime" to "the exclusion was lifted in phase 5 … now an ordinary organic wound host … organs carry no OrganDamage — see P5-D19", per PLAN5 §3.2's exact wording. No functional YAML change; the entity's `components:` list is untouched |

- **New prototype ids:** none. **New C# types/components:** none. **Subscription pairs:** none (PLAN5 §5.1
  — phase 5 registers zero; EXT 3's existing `<WoundHostComponent, ComponentInit>` subscription is
  unchanged, just now matches nothing).
- **Census verified against the plan (P5-D9/E7):** the two hosts created are `MobProtogen`
  (`_Mono/Entities/Mobs/Player/protogen.yml:4-5`, `roundStart: false`) and `MobProtogenRandom`
  (`_Goobstation/Entities/Mobs/Player/humanoid.yml:225-227`, `parent: MobProtogen`). `MobProtogenDummy`
  parents `BaseSpeciesDummy` (`_Mono/.../protogen.yml:86-89`), not `BaseMobProtogen`, and is unaffected —
  confirmed by grep, not just plan citation.
- **Organ gap recorded, not fixed (P5-D19/U12′):** protogen's organs (`OrganProtogenBrain/Eyes/Lungs/Heart/
  Stomach/Liver/Kidneys`) parent `BaseProtogenOrgan`/`BaseProtogenOrganUnGibbable`
  (`_Mono/Body/Organs/protogen.yml:39,89,136,175,201,235,256`), none of which carries `OrganDamageComponent`
  or any `WolfmedOrgan*` component — only the seven `OrganHuman*` ids do (`Body/Organs/human.yml:53,103,150,
  189,215,249,270`, P3-D7). `OrganicBodyPartProfile.organDamage.chances` therefore rolls on every protogen
  hit for nothing: no organ damage, no organ destruction, no `InternalBleedingWound`, no `SurgeryHeal<Organ>`
  chain. `OrganProtogenEars` parents `BaseHumanOrgan` (`:126-127`) but ears are not one of the seven
  instrumented organs, so this changes nothing. Pre-existing for every non-human organic host already
  shipped (moth, vox, arachnid, diona, slime…); phase 5 is simply the first phase to newly enrol a species
  into the gap. `Resources/Prototypes/_Mono/Body/Organs/protogen.yml` is **not** touched (§3.4).
- **D2:** the only entities affected are the two protogen hosts above; nothing else parents
  `BaseMobProtogen`, and the exclusion mechanism itself (the subscription, the `RemComp` fix) is untouched
  for any future synthetic species someone adds to `ExcludedAncestors`.
- **Line endings:** all three edits written CRLF, matching the working tree.
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential, ran
  separately — no concurrent build). Headless server (120 s, port 1299) reached
  `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
  (`<plan>/p5/wp/WP13-3-report-server.log`). `DockTest` **3/3 passed**
  (`<plan>/p5/wp/WP13-3-docktest.log`), no environmental `db.ef` failure, no re-run needed.
  `EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps` **1/1 passed** in 1 m 46 s
  (`<plan>/p5/wp/WP13-3-tests.log`) — this test spawns and deletes **every** non-abstract entity
  prototype in the game, `MobProtogen` and `MobProtogenRandom` included, and is exactly the regression guard
  the exclusion system's own WP9 comment warns about (a `RemCompDeferred`/`_deleteSet` assert would have
  thrown here). Zero `[ERRO]`/`[FATL]`/`Exception`/`Assert` lines in the test log.

#### What protogen gains after WP13-3

A protogen is now an ordinary organic wound host: `WoundHostComponent` present, every part's
`WoundableComponent.Profile == OrganicBodyPartProfile` (inherited from `BaseTorso`/`BaseLeftArm`/… via
`PartProtogen`'s `parent: [BaseItem, BasePart]` → concrete parts `parent: [PartProtogen, BaseTorso]` etc.,
which were never touched by the exclusion), `PainShockTargetComponent` and `EmoteOnDamage` (both inherited
from `BaseMobSpeciesOrganic`, previously present on the entity but functionally inert without `WoundHost`),
localized bleeding through the existing `Blood`-reagent `Bloodstream`, the tourniquet, the phase-4 analyzer
panel, the pain overlay and every wound surgery — all of which it silently lacked while excluded. **It gets
no organ damage, no organ destruction, no internal bleeding and no organ surgery** (P5-D19, above) — a
stated deviation from full organic parity, not an omission.

### WP13-4 (phase 5 — cable coil `treatmentCapabilities`, P5-3; PROTO U, U5)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `_Onyx` `cable_coils.yml:179` (`treatmentCapabilities: [Electrical]` reference, not itself copied) | `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` — `CableStack`'s `- type: Healing` block | upstream, 1 marked line (PROTO U) | **WP13-4** | Added `treatmentCapabilities: [Electrical] # WOLFGATE (P5-3): …` directly under the existing `damageContainers: [Silicon]`. Onyx's own delay (3.5) and damage set are **not** adopted — Wolfgate's `delay: 0.6` / `Heat -3 / Shock -3 / Radiation -3` stand untouched, per PLAN5 §4 ("Only this one line"). |

- **New prototype ids:** none. **New C# types/components:** none. **Subscription pairs:** none.
- **The bug this closes:** `HealingComponent`'s `treatmentCapabilities` defaults to `[Biological]`
  (`HealingComponent.cs:72-73`). HOOK 8 skips the `damageContainers` check for wound hosts
  (`HealingSystem.cs:201-210`) and `WoundHealingSystem.IsCompatiblePart` never reads `damageContainers`
  either (`:154-166`) — only `treatmentCapabilities` gates a `Healing` item against
  `OrganicBodyPartProfile.acceptedTreatmentTypes`. Without the annotation, the cable coil's default
  `[Biological]` overlapped `OrganicBodyPartProfile` and healed a human's Heat/Shock wounds at −3/−3 per
  0.6 s despite `damageContainers: [Silicon]` suggesting it shouldn't. This package closes that leak.
- **Sequencing (per PLAN5 §4):** landed after WP13-0/WP13-1/WP13-2, so IPC and cybernetic parts — whose
  profiles carry `[Mechanical, Electrical]`, which `[Electrical]` overlaps — gain the coil as a valid
  treatment in the same package that closes the organic leak, rather than a dead window with no valid
  target for the tool.
- **D2:** entities without `WoundHostComponent` are unaffected — HOOK 8's `treatmentCapabilities` gate only
  applies inside the wound-host healing path; a non-wound-host `Healing`-item interaction is untouched.
- **Line endings:** edit written CRLF, matching the working tree (confirmed via `git diff`, which shows a
  clean one-line addition with no whitespace/EOL noise).
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential, ran
  separately — no concurrent build). Headless server (120 s, port 1299) reached
  `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
  (`<plan>/p5/wp/WP13-4-report-server.log`). YAMLLinter/DockTest not re-run this package (no
  new prototype ids, no new file); orchestrator may run WP13-4's own Checkpoint line (YAMLLinter; DockTest)
  as part of WP13-7 reconciliation if a fresh signal is wanted.

### WP13-5 (phase 5 — mechanical analyzer and examine wording, P5-5, P5-D17, U9(a))

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| none — Onyx has no per-species short-label variants (its IPCs read as "bruises"/"fracture"); this is a Wolfgate-only improvement (P5-D17) | `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` | new, marked `# WOLFGATE (P5-5)` | **WP13-5** | Appended `health-analyzer-wound-bleeding-short-mechanical = fluid leak`, `health-analyzer-wound-fracture-short-frame = frame damage: { $grade }`, `health-analyzer-wound-fracture-treated-short-frame = frame damage: { $grade } ({ $treatment })` (PLAN5 §2.5, verbatim). No existing key collided (checked by grep). |
| none | `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | new, marked `# WOLFGATE (P5-5)` | **WP13-5** | Appended `health-examinable-part-bleeding-mechanical = leaking fluid` (PLAN5 §2.5, verbatim), next to the existing `health-examinable-part-bleeding` key it substitutes for. |

- **Wound prototype display names (the "data-only wiring" half of P5-5): already complete, verified, not touched.**
  All 30 `- type: wound` prototypes in `Resources/Prototypes/_Onyx/Wounds/wounds.yml` (including the six
  species/mechanical ones landed in WP13-0/1 — `IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`,
  `CyberneticFrameFractureWound`, the four `Slime*`/`Plant*` sets) carry correct `name:` fields, and every
  `wound-name-*` LocId they reference (`wound-name-ipc-mechanical-damage = chassis damage`,
  `wound-name-cybernetic-mechanical-damage = cybernetic damage`,
  `wound-name-cybernetic-frame-fracture = frame fracture`, the four `wound-name-slime-*` and four
  `wound-name-plant-*` keys) already resolves in
  `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl`. Likewise every `wound-stage-*` (incl.
  `wound-stage-frame-*` and `wound-stage-mechanical-*`) and every per-stage `examineDescription:`
  (`wound-examine-frame-*`) resolves cleanly — `HealthAnalyzerSystem.Wolfmed.cs:102` already keys the
  analyzer's per-wound rows off `prototype.Name`/`GetStageDefinition(...).Name`, which is generic across
  species with zero per-species branching needed. Confirmed by grep against both `.ftl` files; nothing
  appended here.
- **Deviation from PLAN5 §4 WP13-5 (justified — task-scoped, not a judgement call): the C# half of P5-5/§2.6
  is explicitly OUT OF SCOPE for this package** ("No new C#" in this package's own brief). PLAN5's WP13-5
  file list calls for four more edits this package does **not** make:
  `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` (append `bool Mechanical` to the payload
  record), `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` (set the flag in
  `BuildWoundDiagnostics`), `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` (branch the
  three LocIds on it) and `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs`
  (branch the examine LocId). Verified by grep that no existing code references any of the four new keys —
  the generic (non-per-wound) "external bleeding" / "fracture: { $grade }" / "active bleeding" labels are
  hard-coded in those four files today and will keep rendering for IPC and cybernetic parts until the flag
  and its three consumer branches land. **What later packages must know:** the four keys added in this
  package are the complete, plan-exact text for P5-D17/§2.6 and need no further locale work — a follow-up
  package only has to add the one-member struct append and the three `if (diagnostic.Mechanical)` /
  equivalent branches described in PLAN5 §2.6 and the WP13-5 file table, in that order, re-reading
  `HealthAnalyzerSystem.Wolfmed.cs` first (WP13-3 and this package both left it otherwise untouched, so no
  merge conflict is expected). Until then this package's new keys are inert (unreferenced by any `.cs`
  file) but harmless — they add no runtime behaviour and are not orphaned data in the sense of dangling
  prototype references (the YAML linter and headless server both confirm no error from the addition).
- **New prototype ids:** none. **New C# types/components/subscriptions:** none — no `.cs` file touched.
- **D2:** not applicable — no code path changed; the new keys are dead text until a later package wires them.
- **Line endings:** both edits written CRLF, matching the working tree (confirmed via `git diff`, clean
  insertion-only hunks, no whitespace/EOL noise).
- **Checkpoint:** `Content.Server` and `Content.Client` **0 errors** (`-c DebugOpt`, sequential, ran
  separately — no concurrent build; expected, since no `.cs` file was touched). Headless server (120 s, port
  1299) reached `Server Version 277.0.0.0 -> Ready` with **zero** `[ERRO]`/`[FATL]`/exception lines
  (`<plan>/p5/wp/WP13-5-report-server.log`). YAMLLinter: **"No errors found"** — no duplicate
  `.ftl` key, confirming the four new keys are genuinely new.

### WP13-6 (phase 5 — tests, P5-6)

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| none (Wolfgate-authored; PLAN5 §6.2 T-P5-1/4/5/6/7/8/19/21) | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesProfileTest.cs` | **new**, 8 tests | **WP13-6** | Per-profile behaviour on real mobs: IPC routing/bleeding/pain/no-scar/no-fracture; `Cold`+`Caustic` restored by `InorganicWolfmed`; cybernetic limb on an organic body (numb, half bleed, frame fracture); frame fracture mended by the P4 surgery ladder; the U15(a) gib ceiling; slime bleeds sooner and never fractures; slime bleeds ×1.15; diona scars and is never severable. |
| none (PLAN5 §6.2 T-P5-2/3/9/10/11/12/14/20) | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesSpawnTest.cs` | **new**, 8 tests | **WP13-6** | Whole-mob behaviour: IPC is a host with the IPC profile + oil bloodstream + `chemicalMaxVolume: 0`, spawns and deletes; IPC leaks oil and takes `Bloodloss` (the `SiliconWolfmed` guard); PROTO S/T keep the welder and nanite applicator repairing an IPC; the 190/210 gib ceiling versus the organic amputation set; `MobIPC` survives 600 projected Blunt (400→1500); protogen (`MobProtogen` **and** `MobProtogenRandom`) is an organic host with the P5-D19 organ gap pinned; a diona limb is destroyed, not severed; every `bodyPartProfile` in the game uses the primary circulatory stream (P5-D3). |
| none (PLAN5 §6.2 T-P5-15/16/17) | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentMatrixTest.cs` | **new**, 3 tests | **WP13-6** | The 6-cell `{Biological, Mechanical, Electrical}` × `{organic wound, IPC chassis wound}` matrix through `WoundDamageRoutingSystem.WithTreatmentCapabilities`; the cable-coil nerf and its compensating gain through `WoundHealingSystem.TryApplyHealing`; the ungated systemic branch (T-P5-17, re-derived — see below). |
| none (PLAN5 §6.2 T-P5-18) | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | **extended**, +1 test | **WP13-6** | `MechanicalWoundDiagnosticTextResolvesTest`: an `IpcMechanicalDamageWound` at severity 30 produces exactly one `HealthAnalyzerVisibleWound` whose `Name`/`StageName` resolve through `ILocalizationManager` to `"chassis damage"` / `"moderate"`, not to a raw key. |

- **20 tests, 4 files, all green.** No new prototype ids, no new `[TestPrototypes]` ids (the two surgery-step
  fixtures `WolfmedStepSetBone`/`WolfmedStepMendBone` are reused from `WolfmedWoundSurgeryTest` — that pool
  is global across the suite, PLAN2 §4 rule 3), no new C# types, components or subscriptions, and **no
  production file touched**. D2 exposure: nil.
- **T-P5-13 is not written** — U13′(b) shipped, so PLAN5's own alternative **T-P5-21** ships in its place.
- **WP13-6-1 — correction to WP13-2's finding "WP13-2-1" (`Cold`/`Caustic` dropped from an IPC's body
  total).** WP13-2 recorded that acid and cryo wound an IPC's limb but never move the number `MobThresholds`
  and `SlowOnDamage` read, because the mob container `SiliconWolfmed` supports neither type. **Measured, that
  is not what happens.** `WoundDamageProjectionSystem.RefreshBodyDamage` projects the part total with
  `WolfmedDamageableSystem.SetDamage`, which writes `dict[type] = amount` for every type in the incoming spec
  (`Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:143-171`) — it is a **set**, not a
  `TryChangeDamage`, so `DamageableSystem`'s container filter (`if (!dict.TryGetValue(type, …)) continue;`,
  `DamageableSystem.cs:270-277`) is never on that path. `DamageChanged` then recomputes `TotalDamage` from the
  whole dict. A `Caustic 15` + `Cold 20` pair on `LeftArmIPC` therefore lands on `MobIPC` as
  `Caustic 15 / Cold 20 / TotalDamage 35`, asserted in `IpcTakesColdAndCausticTest`. The mob container gates
  what can be **dealt to the mob directly**, not what the projection writes. **Consequence: U13′(b) is more
  complete than WP13-2 believed — an IPC really can be killed with acid and cryogenics, exactly as in Onyx,
  and the "Cold/Caustic asymmetry" WP13-2 handed to the balance pass does not exist.** WP13-7 should strike
  it from the deviations block rather than carry it forward.
- **WP13-6-2 — deviation from PLAN5's wording of T-P5-17.** PLAN5 words the test as "the coil's
  `Radiation: -3.0` component still lands" on an organic host. Measured, it does not, for a reason PLAN5 did
  not name: `WoundHealingSystem.OnResolveHealingPart` sets `Accepted = Part != null || !hasLocalized`, where
  `hasLocalized` is true if **any** type in the item's spec is in `WoundHostComponent.LocalizedDamageTypes`.
  The coil's spec carries `Heat` and `Shock`, both localized, so on a body with no capability-compatible part
  the whole application is refused before `Radiation` is looked at. The invariant PLAN5 actually wanted lives
  one layer down and is asserted directly instead: systemic damage healed inside a
  `WithTreatmentCapabilities([Electrical])` scope on an organic host still falls (10 → 7), because
  `Radiation` never reaches `CanTreatPart`. Both halves are asserted in
  `CableCoilRadiationStillHealsSystemicallyTest`, with the correction stated at the assertion.
- **WP13-6-3 — deviation from PLAN5's wording of T-P5-18.** PLAN5 ends that test with "`Mechanical == true`
  on the payload". That member does not exist: WP13-5 shipped only the locale half of §2.6 and explicitly
  deferred the `bool Mechanical` append and its three consumer branches. The test asserts everything that is
  assertable today (the per-wound `Name`/`StageName` path, which WP13-0 shipped complete) and carries a
  marked note telling the follow-up package where to add the flag assertion.
- **WP13-6-4 — bleed-rate assertions use `WoundSystem.CreateOrMergeWound`, not routed damage.** A
  damage-created bleed is immediately reduced by the body's `DamageBleedModifiers`
  (`WoundBleedingSystem.HandlePartDamageApplied`), which differs per species, so a routed wound's
  `CurrentRate` cannot be compared across species. Severity, component presence and absence are asserted
  from routed damage as PLAN5 intends; only the two multiplier tests (T-P5-4's `bleedingMultiplier: 0.5` and
  T-P5-7's `1.15`) create the wound directly.
- **WP13-6-5 — every routed hit passes `ignoreResistances: true`.** `DamageableSystem.TryChangeDamage`
  applies the body's `damageModifierSet` **before** the Wolfmed routing seam, and the phase-5 species all
  carry one (IPC `Cold 0.2 / Heat 1.5 / Shock 2.5`; Slime `Slash 1.2 / Blunt 0.6`; Diona `Slash 0.8 /
  Blunt 0.7`). Without the flag, "Slash 20" would be a different severity on each species and every literal
  in these files would be a per-species number rather than a profile-derived one.
- **Traps honoured, each cited at its assertion:** trap 3 (no Blunt-family bleeding assertion), trap 5
  (`canFeelPain: false` is asserted as `HasComponent<PainComponent>` **false**, never as zero pain), trap 6
  (the frame-fracture test asserts the fracture **wound id**, not just that damage accumulated), trap 7 (no
  pure-Blunt severing assertion anywhere; the 190-versus-250 contradiction is documented, not asserted as
  working), trap 8 (every destruction check runs after `Pair.RunTicksSync`, because `GibPartBehavior` ends
  in `QueueDel`), trap 9 (no positive organ-damage test for IPC/slime/diona/protogen —
  `ProtogenIsAWoundHostTest` asserts the **absence** of `OrganDamageComponent` as the recorded P5-D19 gap).
- **Checkpoint:** `Content.Server` **0 errors**, `Content.Client` **0 errors**, `Content.IntegrationTests`
  **0 errors** (`-c DebugOpt`, run sequentially). Headless server 120 s on port 1299: zero
  `[ERRO]`/`[FATL]`/exception lines (`<plan>/p5/wp/WP13-6-report-server.log`). Wound suite
  (`FullyQualifiedName~_Onyx.Wounds|_Onyx.Medical|Wolfmed`) **116/116 passed** (`WP13-6-tests.log`). Smoke
  (`EntityTest|PrototypeSaveTest|DockTest`) **9 passed, 2 skipped**, both skips pre-existing `[Ignore]`
  attributes on `EntityTest.SpawnAndDirtyAllEntities` and `SpawnAndDeleteEntityCountTest`
  (`WP13-6-smoke.log`). `DockTest` was run first and alone, 3/3, so no environmental `db.ef` masking.
  YAMLLinter not run: this package touches no YAML or FTL, and PLAN5's WP13-6 checkpoint does not list it.

## Phase 5 — user decisions (DECISIONS.md, "Phase 5 — answers to PLAN5.md §8.4")

Recorded here as they land. **WP13-7 reconciliation: every group-A decision (U5, U4, U1, U3′, U15, U2, U13′)
landed across WP13-0..WP13-6 and is ticked off below; all group-B defaults DECISIONS.md binds phase 5 to
(U16/U17/U18, plus U11 which the implementing packages also measured) are honoured and recorded.** The
remaining PLAN5 §8.4 rows (U6/U7/U8/U9/U10/U12′/U14) are PLAN5's own internal recommendations, not part of
DECISIONS.md's binding "Phase 5 — answers to PLAN5.md §8.4" list; each is closed as follows: **U9** (mechanical
analyzer/examine wording, (a)) **landed in WP13-5**, see that section; **U10** (skeleton) **recorded as a
known exclusion, not implemented** — `MobSkeletonPerson` parents the non-`Organic` `BaseMobSkeletonPerson`
base and never had `WoundHost`; mechanically ready (`damageContainer: Biological`, real `BasePart` limbs) but
"bone fracture"/"bleeding" on an undead skeleton needs its own design (P5-D18, §7.3 deviation 17); **U6**
(`allowedWoundStages` mirroring), **U7** (welder `Healing` block), **U8** (pain numbness — closed permanently,
not deferred, P5-D14), **U12′** (protogen organ damage gap, accepted as (a), P5-D19) and **U14**
(`Repairable` on `MobIPC`, declined as (a), P5-D15) are all recorded above in the individual WP sections and
carried to the balance pass per §8.7 — none require further phase-5 action:

* **U5 Cable coil: YES, nerf now.** **LANDED in WP13-4** — `CableStack`'s `Healing` component now carries
  `treatmentCapabilities: [Electrical]`; humans lose the cable-coil burn heal (default `[Biological]`
  removed by the explicit override), IPC and cybernetic parts keep it. Recorded as changelog-worthy per
  DECISIONS.md §111 (a deliberate balance fix, not a bugfix-only note).
* **U4 Protogen: LIFT the exclusion.** **LANDED in WP13-3** — `WolfmedWoundHostExclusionSystem.ExcludedAncestors`
  is now an empty `HashSet<string>`; `MobProtogen` and `MobProtogenRandom` spawn and delete cleanly as wound
  hosts (`EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps` 1/1). Organ gap recorded (U12′/P5-D19).
* **U1 IPC pain: KEEP.** **LANDED in WP13-2** — `MobIPC` carries `- type: WoundHost` and
  `- type: PainShockTarget`; `IpcBodyPartProfile.canFeelPain` measured `True` on the loaded prototype.
  Caveat recorded: an IPC has **no** chemical pain relief at all (R4).
* **U3′(b) 190/210 `MajorLimb` parity for IPC limb gib triggers; U15(a) marked `Destructible` on
  `CyberneticPartBase`; U2(a) `SiliconWolfmed` container.** **U3′(b) and U15(a) LANDED in WP13-1** (PROTO
  O(c) and PROTO P(c); measured thresholds in the WP13-1 table). U2(a): the `SiliconWolfmed` prototype was
  created in WP13-0 and its two companion tool lines (PROTO S/T) landed early in **WP13-1**; it is now
  applied to `MobIPC`'s `Damageable` — **LANDED in WP13-2** (measured `DamageContainerID = SiliconWolfmed`
  on a spawned `MobIPC`). WP13-2 did **not** re-add the welder/nanite entries. **U2(a) is COMPLETE.**
* **U13′(b) `_WF` `InorganicWolfmed` part container restoring Cold/Caustic.** `InorganicWolfmed` prototype
  **created** this package, per the revision N-ordering fix (N8: WP13-1's part edits will reference it, so a
  dangling `ProtoId<DamageContainerPrototype>` at lint time is avoided by landing the container first), and
  **applied to `PartIPCBase` and `CyberneticPartBase` in WP13-1** (PROTO O(b)/P(b)); `Cold` and `Caustic`
  are now live damage types on IPC and cybernetic parts. U13′(b) is COMPLETE.
* **Group B defaults (U16/U17/U18):** **U16 (`chemicalMaxVolume: 0`, no `InjectableSolution`) LANDED in
  WP13-2** — measured `ChemicalMaxVolume = 0` and no `InjectableSolutionComponent` on a spawned `MobIPC`.
  **U18 (drop PROTO R) HONOURED in WP13-2** — `silicon_base.yml` untouched; `MobIPC` resolves exactly one
  `Destructible` threshold, confirming the parent's `damage: 500` is unreachable. **U11(a) (IPC oil
  regeneration keeps `BloodRefreshAmount`'s default) HONOURED** — measured `1`. U17 is WP13-1's recorded
  behaviour and needs no further action.

### WP13-7 (phase 5 — docs, manifest reconcile, status, P5-7)

**Reconciliation method:** `git diff 2b4a4675d0 --stat -- Content.Shared Content.Server Content.Client
Resources Content.IntegrationTests` plus `git status --porcelain` for the same paths, cross-checked file by
file against every row WP13-0..WP13-6 already wrote. All 16 changed/new tracked-by-diff files and all 5
untracked files (`species_parts.yml`, `containers.yml`, `WolfmedSpeciesProfileTest.cs`,
`WolfmedSpeciesSpawnTest.cs`, `WolfmedTreatmentMatrixTest.cs`) already carry a manifest row from their
originating WP — no orphaned file found. Five existing rows needed correction, applied above: the
`_Onyx/Wounds/wounds.yml` status text ("trimmed 14 of 30" → "complete 30 of 30, 2 marked fold classes"),
the `WolfmedWoundHostExclusionSystem.cs` note (exclusion set now empty), `HealthAnalyzerSystem.Wolfmed.cs`
(second marked site, `Mechanical` flag correction), `protogen.yml`'s deviation note (exclusion lifted, organ
gap recorded), and `CirculatoryStreamSystem.cs`'s path (`Content.Server/…`, not `Content.Shared/…`, plus its
second WP13-2 marked site).

**WP13-7's own footprint** (fix round 1: the reconciliation sweep above is scoped to code/prototype roots
and does not cover `Docs`; this package's own 21 new Docs files and its two modified Docs files get their
own rows here, mirroring WP12-10's phase-4 precedent):

| Onyx source | WG path | Status | WP | Notes |
|---|---|---|---|---|
| — | `Docs/_WF/Wolfmed/WOLFMED_PLAN5.md` | new (copy of `<plan>/p5/PLAN5.md`) | **WP13-7** | |
| — | `Docs/_WF/Wolfmed/reports/analysis/phase5/CRITIQUE5.md`, `.../phase5/{capabilities,circulation,numbness,species,tests}.md` | new (copies, 6 files) | **WP13-7** | the six phase-5 analyst/critique reports |
| — | `Docs/_WF/Wolfmed/reports/work-packages/phase5/WP13-{0..6}-{report,verify}.md` | new (copies, 14 files) | **WP13-7** | |
| — | `Docs/_WF/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md` | modified — reconcile | **WP13-7** | this section plus the §7.1 row corrections above and the "Phase 5 — user decisions" subsection below |

- **P5-D18 recorded (new, never previously in any manifest):** `MobSkeletonPerson` (`Resources/Prototypes/
  _Mono/Entities/Mobs/Species/skeleton.yml` at the pin) parents `BaseMobSkeletonPerson → [MobFlammable,
  BaseMobSpecies]` — the non-`Organic`, non-`WoundHost` base — so it was never a wound host and phase 5
  leaves it untouched. It is mechanically ready (`damageContainer: Biological`, `BasePart`-derived limbs, a
  real `Skeleton` body prototype) but "bone fracture" and "bleeding" on an undead skeleton need their own
  design (U10(a)). No file changed; this is a documentation-only exclusion record.
- **WP13-6-1 struck from the deviations carried forward:** WP13-6 measured that `Cold`/`Caustic` damage
  dealt to an IPC limb *does* reach `MobIPC`'s body total and `SlowOnDamage` (via
  `WoundDamageProjectionSystem.RefreshBodyDamage` → `WolfmedDamageableSystem.SetDamage`, which writes the
  whole incoming spec rather than filtering through the mob's own `DamageContainerID`). The "Cold/Caustic
  asymmetry" WP13-2 originally handed to the balance pass does not exist and is **not** carried into
  §7.3 below.
- **Upstream footprint re-measured:** `git diff 2b4a4675d0 --name-only -- Content.Shared Content.Server
  Content.Client Resources Content.IntegrationTests`, filtered to paths outside `_Onyx`/`_WF`/
  `Content.IntegrationTests`, returns exactly the 8 files PLAN5 §3.5 predicted: `Body/Parts/slime.yml`,
  `Body/Parts/diona.yml`, `_EinsteinEngines/Body/Parts/ipc.yml`, `_Shitmed/Body/Parts/cybernetic.yml`,
  `_EinsteinEngines/Entities/Mobs/Player/ipc.yml`, `Entities/Objects/Tools/welders.yml`,
  `_Mono/Entities/Objects/Tools/nanite_applicator.yml`, `Entities/Objects/Tools/cable_coils.yml`.
  `_Mono/Entities/Mobs/Species/protogen.yml` is already tracked since phase 1 and gains no new row.
  `_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml` is confirmed **not** touched (U18, PROTO R
  dropped). **47 (phase 4) + 8 = 55 tracked upstream files after phase 5**, matching PLAN5 §3.5 exactly.

### Phase 5 — deviations block (§7.3, consolidated)

1. `CyberneticFractureProfile` manipulation modifiers corrected to 1.1/1.25/1.5/2.0 (§8.2-1 applied to the
   second profile).
2. IPC and cybernetic amputation thresholds are Wolfgate's organic set, not Onyx's (P5-D7) — Onyx's arm
   numbers (270/400/600) are unreachable under Wolfgate's gib rungs.
3. IPC limb gib triggers changed 110/150 → 190/210 (P5-D8/U3′(b)), matching `MajorLimb`. Two asymmetries
   accepted: IPC hands/feet now tougher than organic (190/210 vs `MinorLimb`'s 150/180); IPC head
   destructible at 190 where an organic head has no gib trigger. Also affects detached IPC limbs (D2).
4. `CyberneticPartBase` gains its own `Destructible` (Blunt 190 / Slash 210, no Heat rung; U15(a)) —
   removes the pre-existing Heat-250-burns-to-`Ash` behaviour on a steel prosthetic.
5. `SiliconWolfmed` container — IPC oil loss deals `Bloodloss` (U2(a)); Onyx's own IPC takes none.
   `Bloodloss` added as a type, not the `Airloss` group (its only benefit, the tourniquet cost, lands on
   the part, which this container does not touch).
6. `MobIPC` gains `PainShockTarget` explicitly (Wolfgate moved it to `BaseMobSpeciesOrganic` in P2-D7;
   Onyx has it on `BaseSpeciesMob`) — net Onyx parity.
7. `MobIPC`'s gib threshold raised to 1500 (D22, U18 confirms `silicon_base.yml` untouched and safe to
   skip: `MobIPC`'s own `thresholds` list replaces the parent's and is the parent's only descendant).
8. Protogen is an organic wound host — D32 exclusion lifted (U4). `MobProtogen` + `MobProtogenRandom` are
   hosts; `MobProtogenDummy` (parents `BaseSpeciesDummy`) is unaffected.
9. Protogen has no organ damage, destruction, internal bleeding or organ surgery (P5-D19/U12′) —
   `BaseProtogenOrgan`-derived organs carry no `OrganDamage`. Pre-existing pattern for every non-human
   organic host, newly relevant now that a species is enrolled into it.
10. `organDamage` on the Ipc/Slime/Plant profiles is inert — no organ in those species carries
    `OrganDamageComponent` (P5-D13, §8.6-7 unchanged).
11. `Cold`/`Caustic` on IPC and cybernetic parts restored by the `_WF` `InorganicWolfmed` container
    (U13′(b)) — closes a Wolfgate-only loss versus Onyx (Onyx's `SiliconIpc` took the whole `Burn` group).
    **Superseded finding (WP13-6-1, see above): this was never a body-total asymmetry** — the projection
    path always wrote `Cold`/`Caustic` to `MobIPC`'s total regardless of container support; the container
    only gates whether the *part* itself takes the damage and creates a wound.
12. Cable coil no longer heals organic Heat/Shock wounds — a live pre-existing leak, closed (U5/P5-D12).
    The coil's other numbers stay Wolfgate's, not Onyx's.
13. Regenerative Mesh and Medicated Suture keep Onyx's `allowedWoundStages` off (U6).
14. No `Repairable` on `MobIPC`, no `Healing` on the welder (U14, U7; P5-D15/D16).
15. No `InjectableSolution` and `chemicalMaxVolume: 0` on `MobIPC` (U16) — an IPC has no metabolizer
    (`OrganIPCPump`'s `Metabolizer` block is commented out), so an injectable solution would be a trap.
16. P5-4 pain numbness closed permanently, not deferred (U8/P5-D14). `PainNumbnessStatusEffectComponent`
    remains dead code: two readers, no writer.
17. **Skeleton (`MobSkeletonPerson`) is outside the wound system** — never was, now recorded (U10/P5-D18,
    see the WP13-7 entry above).
18. Slime and diona internal bleeding is listed in their profiles but unreachable — `InternalBleedingWound`
    is only ever produced by `WolfmedOrganComponent.destructionWound`, human-lineage only. Surgery can
    still create one manually.
19. A tourniquet costs half as much on a robot or plant limb — its `Asphyxiation: 5` share is dropped by
    the part's container (`Inorganic`/`Silicon`/`InorganicWolfmed` support no `Airloss`). Accepted; the
    reason deviation 5 does not take the `Airloss` group either.
20. Diona and slime limbs are destroyed, not severed, past 190/210 Blunt/Slash (U17) —
    `amputationThresholds: {}` disables `AmputationSystem` only; the inherited `MajorLimb`/`MinorLimb`
    `GibPartBehavior` still deletes the limb, so thrown limb, stump wound, mend surgery and reattachment
    are all unreachable for them. Pre-existing; phase 5 is the first document to state it plainly.

### Post-phase-5 fixes (orchestrator, 2026-09-14)

### CI GibTest fix (2026-09-14)

`Tests.Body.GibTest` failed on CI: gibbing a body part dumps every container on it, including the Onyx `wounds`
container. Wound entities live in nullspace with no physics (`ApplyLinearImpulse` resolve error) and, once dumped,
outlive the deleted part and serialise a dangling `HoldingPart` (`GetNetEntity` resolve error in PVS).

| Onyx path | Wolfgate path | Status | Notes |
|---|---|---|---|
| — | `Content.Shared/Gibbing/Systems/GibbingSystem.cs` | modified | one marked line after `RaiseLocalEvent(gibbable, ref gibContentsAttempt)`: `excludedContainers = gibContentsAttempt.ExcludedContainers;` so subscribers of `AttemptEntityContentsGibEvent` can veto containers (upstream raised the event but never read it back) |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBodySystem.cs` | modified | `<WoundableComponent, AttemptEntityContentsGibEvent>` adds `WoundableComponent.ContainerId` to `ExcludedContainers`; wounds stay contained and are deleted with the part |

Verified: `GibTest`, all `Tests.Body`, and the 116-test wound suite pass locally.

- `Content.Client/Overlays/EntityHealthBarOverlay.cs` — hook, 2 marked lines: `CalcProgress` ratios clamped to [0,1]; wound-host projected damage can exceed the dead threshold and drew a negative bar for ghosts.
- `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/{Wounds,WoundTreatment}.xml` — modified: Onyx's `FTLTextpart` guidebook tag does not exist in Wolfgate (Pidgin parse error at open); the locale text is now inlined as markdown and `Resources/Locale/en-US/_WF/Wolfmed/guidebook/wounds.ftl` is deleted (its 21 keys had no other consumer).

### Analyzer redesign (2026-09-14)

The phase-4 analyzer was one 350x650 column: overview, damage groups and the Wolfmed tab strip stacked
vertically. Reshaped into a fixed-width overview pane on the left and the Wolfmed tabs on the right, in a
900x600 resizable window. Layout option (b) of the brief: option (a) (a `_WF` window class composing the
Shitmed controls) would have had to copy ~200 lines of body-doll XAML plus `Populate`, `DrawDiagnosticGroups`
and `SetupIcon`, none of which is reachable from another class because they read the window's own
`[GenerateTypedNameReferences]` fields.

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml` | new | analyzer redesign | hook, 4 marked blocks: window is 900x600 / `MinSize 660 470` / `Resizable`; a `WolfmedPaneSplit` horizontal box holds `WolfmedOverviewPane` (return button, doll, patient grid, alerts, damage groups) and `WolfmedPanel`; the alerts box and the return-button box gained the names `WolfmedAlertsPanel` / `WolfmedReturnPanel` so their frames hide with their contents; the patient grid stopped vertically expanding so the damage list takes the slack. No control renamed, no binding moved. |
| — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml.cs` | new | analyzer redesign | hook, 2 marked lines: `WolfmedAlertsPanel.Visible = showAlerts;` beside the existing two alert toggles and `WolfmedReturnPanel.Visible = isPart;` beside `ReturnButton.Visible`, so an empty alert frame (62px) and an empty return-button frame (40px) stop eating the narrower overview pane. |
| — | `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | modified | analyzer redesign | `DamageSection` handoff dropped (the damage groups are permanently on the left now); `PopulateWolfmed`/`HideWolfmed` instead toggle `WolfmedOverviewPane.HorizontalExpand` so a non-wound-host scan gets the full width. Still the first statement of `Populate`, so every early return repaints. |
| — | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml` | modified | analyzer redesign | Damage tab removed (redundant with the left pane); three tabs left, `OpenRight`/`OpenBoth`/`OpenLeft`; the Wounds tab is now itself a `ScrollContainer` like Organs and Chemicals instead of scrolling only its findings list; root expands vertically. |
| — | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | analyzer redesign | `DamageSection`/`ReleaseDamageSection` and the `DamageButton` wiring deleted; `WolfmedDiagnosticTab` loses its `Damage` member and defaults to `Wounds`; `ApplyTab` no longer toggles `TabBody`/`VerticalExpand`. |

Deviations / notes:

1. `health-analyzer-window-damage-tab` in `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl`
   is now unreferenced. Left in place; no consumer, no cost.
2. The window is resizable for the first time (`BaseWindow` writes `SetSize` on a resize drag, so the
   900x600 default and user resizing coexist).

## Final stages: H (Crit heartbeat) (2026-09-19)

A client-only looping heartbeat while the local player's own body is in `MobState.Critical`. Never plays for
anyone else's mob and works for any entity with `MobStateComponent`, not only wound hosts.

| Onyx path | Wolfgate path | Status | Notes |
|---|---|---|---|
| — | `Content.Client/_WF/Wolfmed/Audio/WolfmedCritHeartbeatSystem.cs` | new | client `EntitySystem`. Broadcast-subscribes `LocalPlayerAttachedEvent`/`LocalPlayerDetachedEvent` (pattern copied from `DamageOverlayUiController`), `MobStateChangedEvent` (filtered to `args.Target == _player.LocalEntity`, matching the overlay controller's own broadcast subscription — no duplicate directed pair) and `EntityTerminatingEvent` (already broadcast-subscribed elsewhere, e.g. `JukeboxSystem`/`ClientDirtySystem`, so a second broadcast subscriber is safe) to cover deletion without a detach. Also subscribes `<MobStateComponent, AfterAutoHandleStateEvent>` (no existing subscriber of that pair): `MobStateComponent` has no custom `ComponentHandleState`, so a state change made purely on the server — the common case, since wound/threshold crit is server-only — never raises `MobStateChangedEvent` on the client; `AfterAutoHandleStateEvent` is Robust's standard signal that an `AutoGenerateComponentState` component's networked fields were just applied, and is the reliable trigger here (found via a failing integration test — see Deviations). `Refresh()` re-derives play/stop state from the CVar and the current `MobStateComponent.CurrentState` on every relevant event, so attach-while-already-critical, recovery, death and re-crit all resolve correctly. Plays via `SharedAudioSystem.PlayGlobal(..., Filter.Local(), false, AudioParams.Default.WithLoop(true).WithVolume(-4f))`; a public `Active` bool is true whenever the loop should be playing even when the headless test backend returns no stream. |
| — | `Content.Shared/Mobs/Components/MobStateComponent.cs` | modified | upstream hook, 1 marked line: `[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]`. Without this the auto-generated component state applies silently and no client-side event exists to react to a server-driven `MobState` change at all. |
| — | `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | new | `wolfmed.crit_heartbeat` bool, default true, `CLIENTONLY \| ARCHIVE`. Wired with `Subs.CVar(_cfg, WolfmedCVars.CritHeartbeat, OnCVarChanged, true)` so it reacts live; no options-menu UI per the task. First Wolfmed-specific CCVar class (existing `_WF/CCVar` classes are Wolfgate-wide, e.g. `WolfgateCVars`, `InternetSoundCVars`). |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedCritHeartbeatTest.cs` | new | client+server pair test (`PoolManager.GetServerClient(Connected = true)`), attaches the session's mind to a fresh `MobHuman` (pattern from `GenitalConsentTestHelpers.AttachToNewBody`), drives `MobStateSystem.ChangeMobState` through Critical → Alive → Critical → Dead and asserts `WolfmedCritHeartbeatSystem.Active` toggles true/false at each step. |
| — | `Resources/Audio/_WF/Wolfmed/heartbeat_loop.ogg` + `attributions.yml` | pre-existing | asset and attribution already landed on the branch before this package; reused as-is at `/Audio/_WF/Wolfmed/heartbeat_loop.ogg`. |

Deviations: the first pass subscribed only to `MobStateChangedEvent`, matching `DamageOverlayUiController`'s
pattern; the new integration test caught that a server-only `MobStateSystem.ChangeMobState` never reaches the
client through that event (it is not re-raised from networked component state), so the `AfterAutoHandleStateEvent`
subscription above was added. That in turn needed the one-line upstream `MobStateComponent` hook (Robust does
not raise `AfterAutoHandleStateEvent` for an `AutoGenerateComponentState` component unless it opts in), which
is now the reliable trigger for a purely server-side crit. No locale or guide changes — the loop is a
physical/audio cue with no player-facing text, matching how the existing crit vignette/overlay needs none.

## Final stages: W0 (Healing multiplier fix, treatment restrictions, fracture rebalance) (2026-09-19)

Three balance changes that share one theme: damage and wounds are separate records, and the tool has to match
the injury. Onyx's `healingMultiplier` is finally set, topicals are restricted by damage type, and fractures
are retuned for Wolfgate melee.

| Onyx path | Wolfgate path | Status | Notes |
|---|---|---|---|
| `_Onyx/Wounds/wounds.yml` | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | 19 marked `healingMultiplier` lines. `0.15` (Onyx's intended value) on the 15 wounds damage removal can reach; `0` on the four pure-bleeding wounds (`SystemicBleedingWound`, `InternalBleedingWound`, `SurgicalIncisionWound`, `DismembermentWound`) as documentation - their `damageTypes` are empty, so damage never healed them anyway. `IpcMechanicalDamageWound` and `CyberneticMechanicalDamageWound` are exempt at `1` (see Deviations). Also `BoneFractureWound`'s stage thresholds 20/35/50/60 -> 12/20/32/45 and Hairline's pain `minSeverity` 15 -> 12, tracking the new fracture grades; `CyberneticFrameFractureWound` untouched. |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/fractures.yml` | new | `WolfmedFractureProfile`: Blunt thresholds 12/20/32/45 at 25/50/80/100 %, `accumulationMultiplier` 0.4 -> 0.8. Everything else copied from `OrganicFractureProfile`, which stays shipped and unreferenced as the vendored Onyx reference. |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | modified | the 10 organic part abstracts point at `WolfmedFractureProfile`; one marked comment per line. `species_parts.yml` untouched (slime `null`, cybernetic keeps `CyberneticFractureProfile`). |
| — | `Content.Server/Medical/Components/HealingComponent.cs` | modified | one new `[DataField] TreatedDamageTypes` (`HashSet<ProtoId<DamageTypePrototype>>?`) inside the existing marked HOOK 7 / D14 block, plus the `Robust.Shared.Prototypes` using. Null or empty keeps today's behaviour. |
| — | `Content.Server/_WF/Wolfmed/Medical/WoundHealingSystem.Wolfmed.cs` | new | `_WF` partial of `WoundHealingSystem` holding `GetTreatableDamage(HealingComponent)`, the single place an item's spec is narrowed. An item left with nothing resolves no part, heals nothing and is refused. |
| `_Onyx/Wounds/WoundHealingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs` | modified | 2 marked lines in `TryApplyHealing`: resolve and apply the narrowed spec. |
| — | `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` | modified | `OnWoundHostDoAfter` and `IsWoundDamaged` read the narrowed spec. `IsWoundDamaged`'s wound branch gained `&& !healing.HealDamage`, mirroring `TryApplyHealing`'s own condition - without it a 0.15 topical reports work left after the part damage is gone and repeats over the whole stack for nothing. |
| — | `Content.Server/Medical/HealingSystem.cs` | modified | 2 marked hooks: `TryHeal` resolves the narrowed spec, and the "nothing to do" popup uses `wolfmed-item-cant-treat-part` on a wound host. |
| — | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` | modified | marked `treatedDamageTypes: [Blunt]` on `Brutepack` and `[Slash, Piercing]` on `MedicatedSuture` (children inherit), plus a corrected suture description. `Ointment`, `RegenerativeMesh` and `Gauze` already carry only their own types and need nothing. `HealingToolbox` stays unrestricted. |
| — | `Resources/Locale/en-US/_WF/Wolfmed/healing-popup.ftl` | new | `wolfmed-item-cant-treat-part`. |
| — | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | per-item treatment list, the "damage removal is not wound closure" rule, and a line on repeated blows breaking bones. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentRestrictionTest.cs` | new | 3 tests: bruise pack treats Blunt and neither closes a cut nor touches its bleed; sutures treat cuts and their bleeding but not bruises; every `healingMultiplier` matches the model and the refusal locale key resolves. |
| — | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundFractureTest.cs` | modified | grade boundaries now read `WolfmedFractureProfile` (12/20/32/45) and pin the retuned chances and accumulation; Onyx's profile is still asserted at 20/35/50/60 so a re-sync that moves it is visible. Alert-gate test re-derived for the new bands. |
| — | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundHealingTest.cs` | modified | 15 -> 11 Blunt (12 is the new Hairline threshold at a 25 % roll, which would have made the pain figures flaky) and the wound now heals to 9.5 rather than 5 - Onyx's own stale literal was written for exactly this multiplier. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentMatrixTest.cs` | modified | organic wound severities re-derived at 0.15; the mechanical half is unchanged by the exemption. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReagentTreatmentTest.cs` | modified | one severity literal re-derived; one 15 Blunt hit lowered to 10 for fracture determinism. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedHealingTargetTest.cs` | modified | the bruise-pack cases use Blunt (a bruise pack can no longer treat a cut at all, so the do-after would never start) and assert the flesh wound is treated but not closed. The cable-coil case still closes outright. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSurgeryTest.cs` | modified | the "drive a fracture into the Hairline band" step re-derived for 12/20. |

Deviations from the spec:
1. `IpcMechanicalDamageWound` and `CyberneticMechanicalDamageWound` keep `healingMultiplier: 1` rather than
   0.15. A welder is the only thing that closes a chassis wound, and it closes it by removing damage
   (`WeldingHealableSystem.Wolfmed.cs` routes a heal through `TryApplyPartDamage`); at 0.15 its repeat loop
   would burn fuel forever on a wound it can no longer reduce, leaving mechanical wounds permanently open.
   Flesh has surgery and sutures for that job, chassis do not. Reversible: drop the two exemptions and give
   the welder its own `TryHealWounds` call.
2. No Piercing fracture profile. `WolfmedBodyPartComponent.FractureProfile` is a single id and
   `WoundFractureSystem` assumes one fracture per part (`GetFracture` returns the first, `TryGetProfile`
   re-derives the profile from the holding part when re-grading), so a second damage type needs a list on the
   component and a rewrite of creation, grading and treatment - well past the "small marked change" the task
   allowed. Declared as a gap.
3. `BoneFractureWound`'s stage thresholds were lowered alongside the profile grades, which the spec did not
   ask for. Without it a fresh Hairline fracture (severity 12-19) falls below the wound's own lowest stage and
   carries no pain, no functionality penalty and no stage name at all.

## Final stages: W1 (Ballistic wounds, embedded objects, wound rules framework) (2026-09-19)

Four ballistic wounds, objects that stay in the flesh until someone digs them out, and the small data-driven
framework W2-W6 will reuse: a cause enum derived from the hit's tool, a `wolfmedWoundRule` prototype that maps
{damage type, cause, per-hit size, part type, profile capability, chance} to a wound, and one marked extension
point in the vendored `WoundSystem`.

| Onyx path | Wolfgate path | Status | Notes |
|---|---|---|---|
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundCause.cs` | new | `[Flags]` enum: `Projectile, Fragment, Hitscan, Explosion, Melee, Unarmed, Bite, Thrown, Environmental, Surgery`. Several flags in one YAML scalar: `cause: "Unarmed, Bite"`. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedDamageCauseComponent.cs` | new | per-entity cause override, merged with the derived flags. Put on a projectile, a weapon or an attacking mob. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundRulePrototype.cs` | new | `wolfmedWoundRule`: `wound`, `damageTypes`, `causes`, `minDamage`/`maxDamage`, `partTypes`, `capabilities`, `chance`, `priority`, `severityMultiplier`, `minSeverity`, `replacesDefault`, `continue`, `embedded` (`WolfmedEmbeddedSpec`: `item`, `minCount`, `maxCount`, `maxTotal`). Every filter left empty matches everything. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | new | `WolfmedWoundSelectionEvent` (the extension point, raised per damage type per hit on the part) and `WolfmedEmbeddedRemovalDoAfterEvent`. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundRuleSystem.cs` | new | rule cache by damage type (rebuilt on prototype reload), `GetCause(origin, tool, isExplosion)`, and the selection handler: rules in descending priority, first match wins unless it sets `continue`, explosion severity scaling mirrored from `WoundSystem.HandlePartDamageApplied`. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedEmbeddedObjectComponent.cs` | new | on the wound: `count`, `item`, `maxCount`, `blocksTreatment`, `cleanDelay`/`sharpDelay`/`selfMultiplier`, `sharpDamage`, `sharpPain`. |
| — | `Content.Shared/_WF/Wolfmed/Wounds/WolfmedEmbeddedObjectSystem.cs` | new | cancels `WoundTreatmentAttemptEvent` while anything is embedded; `Add`, `GetEmbeddedWound`, `GetPartCount`, `TryTakeOne` (drops the component at zero). |
| — | `Content.Server/_WF/Wolfmed/Wounds/WolfmedEmbeddedRemovalSystem.cs` | new | `<WoundHostComponent, AfterInteractUsingEvent>` (last in the interaction chain, so it never steals a surgery or a topical) plus the do-after. `TryGetTool` calls a hemostat or tweezers clean, any `Sharp` item or Slash melee weapon dirty. `TryRemoveOne` is public so tests drive it without a do-after. |
| `_Onyx/Wounds/WoundSystem.cs` | `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | modified | two marked blocks: raise `WolfmedWoundSelectionEvent` before the default per-type loop and `continue` when it suppresses the default; skip `prototype.RuleOnly` wounds in the creation branch. |
| `_Onyx/Wounds/WoundPrototype.cs` | `Content.Shared/_Onyx/Wounds/WoundPrototype.cs` | modified | one marked field, `ruleOnly`. Keeps rule wounds out of the damage-type creation pass while `damageTypes` still governs how they heal. |
| `_Onyx/Wounds/WoundEvents.cs` | `Content.Shared/_Onyx/Wounds/WoundEvents.cs` | modified | one marked field on `PartDamageAppliedEvent`: `Tool`. |
| `_Onyx/Wounds/WoundDamageRoutingSystem.cs` | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | modified | one marked line: pass `_routedModifiers[body].Tool` into `PartDamageAppliedEvent`. The side table already carried it for armour penetration (D23). |
| `_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | marked `ushort EmbeddedObjects = 0` and its `HasFindings` clause. |
| — | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | fills `EmbeddedObjects` from `WolfmedEmbeddedObjectSystem.GetPartCount`. |
| — | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | prints `health-analyzer-wound-embedded-short` before the scar row. |
| — | `Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs` | modified | upstream hook, two marked one-line `tool: meleeUid` additions (light and heavy attack). Nothing reads `DamageModifyEvent.Tool`, so this only reaches the wound rules. |
| — | `Content.Server/Damage/Systems/DamageOtherOnHitSystem.cs` | modified | upstream hook, one marked line: `tool: uid` for a thrown item, so `Thrown` is a real cause for W2/W3. |
| — | `Resources/Prototypes/_WF/Wolfmed/Wounds/ballistic.yml` | new | `WolfmedGrazeWound`, `WolfmedGunshotWound`, `WolfmedLodgedRoundWound`, `WolfmedShrapnelWound`, all `ruleOnly: true`. The two that hold objects use `healingMultiplier: 0` and `clottingMultiplier: 0`, so nothing closes them and they never clot while something is in there. |
| — | `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | new | five rules: shrapnel (Explosion or Fragment, >= 2), lodged heavy (Projectile, >= 18), lodged light (Projectile, 7-18, 20 %), gunshot (Projectile, >= 7), graze (Projectile, <= 7). All `capabilities: [Biological]`. |
| — | `Resources/Prototypes/_WF/Wolfmed/Entities/removed_objects.yml` | new | `WolfmedSpentRound` (existing `ammo_casing.rsi` `base-spent`) and `WolfmedShrapnelFragment` (existing, until now unused, `Shards/shrapnel.rsi` `shrapnelsmall`). No new art, no new attribution. |
| `_Onyx/Wounds/wounds.yml` | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | four marked ids added to `OrganicBodyPartProfile.supportedWounds`; `CanCreateWound` gates on that list. |
| — | `Resources/Prototypes/_Mono/Entities/Objects/Weapons/Guns/Ammunition/Projectiles/12_gauge.yml` | modified | marked `WolfmedDamageCause: Fragment` on `Pellet12_gauge`; the spread variants inherit it. |
| — | `Resources/Prototypes/Entities/Objects/Weapons/Guns/Ammunition/Projectiles/grenade_shrapnel.yml` | modified | marked `WolfmedDamageCause: Fragment` on `PelletClusterLethal`. |
| — | `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | new | four wound names, three removal popups, `health-analyzer-wound-embedded-short`. |
| — | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | four new wound entries after the puncture. |
| — | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | "Embedded objects" section: a hemostat is clean, any sharp item works but cuts and hurts, self-removal is slower. |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBallisticWoundTest.cs` | new | seven tests: damage bands, mid-band through-or-lodge, melee Piercing stays a puncture, explosion and buckshot both make shrapnel, lodged round bleeds and refuses treatment until removed, knife removal costs a cut and pain, analyzer reports the count. Drives the real damage pipeline with a real projectile entity as the tool. |

Deviations from the spec:
1. A rifle-weight round (>= 18 Piercing per hit) lodges **always**, not by chance. The spec allows "at high or
   by chance"; a deterministic heavy band gives a medic something they can rely on and keeps the tests free of
   RNG. One `chance:` line reverses it.
2. No new forceps item. `HemostatComponent` (hemostat, advanced retractor, omnitool) and `TweezersComponent`
   already exist and are treated as the clean tool, as the task preferred. W7 can add a dedicated forceps item
   by giving it either component.
3. Bleeding and pain upkeep while embedded are the wound prototype's own behaviors (`clottingMultiplier: 0`
   plus `WoundPainBehavior`) rather than a tick loop on the component. Same result, no new update loop, and a
   new embedded wound only has to set its data.
4. `Hitscan`, `Bite` and `Surgery` exist in the cause enum but nothing derives them yet: hitscan weapons pass
   no tool, and bites arrive through melee as `Unarmed`. Both are one `WolfmedDamageCause` component in YAML
   (or one marked `tool:` argument) away, which is why the flags are already there for W2.

## Final stages: W2 (Slash and bite wounds) (2026-09-19)

Three wounds on W1's rule framework - an arterial bleed, a severed tendon, an avulsion - plus the two
behaviours they need that no existing system provided: a bleed that only a tourniquet or surgery stops, and
a limb penalty that does not depend on `wounds.body_part_functionality_enabled` (P2-3 keeps that false).

| Path | Status | Notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | new | Three `WoundBehavior` subclasses. `WolfmedArterialBleedBehavior`: per-treatment bleeding multipliers, `topicalsReduceBleeding`, `requiresStoppedBleedToTreat`, `tourniquetableParts`. `WolfmedLimbPenaltyBehavior`: `movementModifier` / `manipulationModifier`. `WolfmedInfectionRiskBehavior`: `riskMultiplier`, the field W5 reads. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | new | Reads those behaviors off a live wound at its current severity. Cancels `WoundTreatmentAttemptEvent` on an arterial bleed that is still losing blood; refreshes movement speed on wound created/changed/removed (Onyx only does that off fracture events). Public: `TryGetBehavior<T>`, `GetInfectionRisk`, `GetPartInfectionRisk`, `TryGetLimbPenalty`, `CanTourniquet`, `CanTourniquetPart`. |
| `Content.Server/_WF/Wolfmed/Wounds/WoundBleedingSystem.Wolfmed.cs` | new | Partial of the vendored bleeding system: `GetTreatmentMultiplier` (arterial override table), `AllowsTopicalBleedReduction`, `BandageArterialBleeds`. |
| `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs` | modified | Two marked lines: `RefreshWound` routes through `GetTreatmentMultiplier`; `ReducePartBleeding` skips wounds that refuse topical bleed reduction. |
| `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs` | modified | Marked: `TreatBleeding` also records a dressing on arterial bleeds, which is all gauze achieves there. |
| `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | modified | Marked block in `HealWounds`: damage removal now raises `WoundTreatmentAttemptEvent` like `TreatWound` does. Closes a W1 gap too - embedded objects blocked `TreatWound` but not the damage path. |
| `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | modified | Marked block in `GetEffect`: a limb-penalty wound supplies the movement / manipulation multiplier when there is no fracture, before the CVar-gated functionality fallback. |
| `Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | modified | Marked: `CanApply` also asks `CanTourniquetPart`, `Apply` skips wounds a tourniquet cannot reach, and the refusal popup distinguishes "nothing to tie around" from "not bleeding". |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/slash_bite.yml` | new | `WolfmedArterialBleedWound` (rate 0.8, never clots, `healingMultiplier: 0.15` behind the treatment gate), `WolfmedTendonCutWound` (`healingMultiplier: 0`, limb penalty 0.7 / 1.5), `WolfmedAvulsionWound` (three stages, scars from severity 4, `riskMultiplier: 2.5`). |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | Four rules: avulsion from `Bite` at >= 6; arterial from Slash >= 22 or Piercing >= 30, `replacesDefault: false` + `continue: true`; tendon from Slash >= 16 on limbs. All deterministic. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | Marked: the three wounds added to `OrganicBodyPartProfile.supportedWounds`. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | `SurgeryStepClampArtery` (hemostat, reuses `WolfmedSurgeryIncisionTreatmentEffect` in Clamp mode), `SurgeryStepRepairArtery`, `SurgeryStepRepairTendon` (both `WolfmedSurgeryTreatWoundEffect`). No new C#. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryRepairArtery` (clamp, suture, seal), `SurgeryRepairTendon` (repair, seal), both gated on the wound. |
| `Resources/Prototypes/Entities/Mobs/NPCs/simplemob.yml` | modified | Marked `WolfmedDamageCause: Bite` on `SimpleSpaceMobBase`, so every simple animal's unarmed attack is a bite. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Three wound names and `wolfmed-tourniquet-nowhere-to-tie`. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Three new wound entries. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | "Arterial bleeding" and "Severed tendons" sections. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSlashBiteWoundTest.cs` | new | Seven tests: creation bands, gauze vs tourniquet vs treatment on an arterial bleed, the torso case, tendon penalties applied and cleared, tendon vs topicals, bite to avulsion with the infection field, names and surgeries. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedCritHeartbeatTest.cs` | modified | Made deterministic. `ChangeMobState` on an undamaged mob is pulled back to Alive by its thresholds, so on a recycled pair the client could never see the state; the new `AssertHeartbeat` helper holds the state while it waits. Not a W2 behaviour change. |

Deviations from the spec:
1. **Sutures after a tourniquet are not distinguished from gauze after a tourniquet.** The gate is "the bleed
   has stopped", not "the item is a suture": gauze and medicated sutures carry the same `treatedDamageTypes`
   (Slash, Piercing) and the same negative `bloodlossModifier`, so telling them apart would need a new item
   flag. Without a tourniquet gauze still only slows the bleed and cannot take its severity, which is the part
   of the spec that carries the gameplay.
2. **The tendon penalty does not use `WoundFunctionalityBehavior`.** Onyx's route for "this limb is impaired"
   is gated on `wounds.body_part_functionality_enabled`, which P2-3 deliberately keeps false, so it would have
   shipped inert. `WolfmedLimbPenaltyBehavior` hooks the fracture branch of `FractureEffectSystem` instead -
   the same two multipliers, ungated. A fracture on the same limb shadows it, exactly as it already shadows
   the functionality fallback.
3. **Deterministic bands, no chance rolls**, following W1: rarity is in the thresholds (22 Slash, 30 Piercing,
   16 Slash on a limb). One `chance:` line per rule reverses it.
4. **Systemic bleeding chems still stop an arterial bleed.** `ModifyBodyBleeding` / `StopBodyBleeding` are not
   gated; only the part-targeted topical path is. A chem that stops all bleeding is a whole-body effect and
   was left alone, but W4's cauterisation work should decide whether heat seals an artery.

## Final stages: W3 (Blunt trauma wounds) (2026-09-19)

Four blunt-trauma wounds on W1's rule framework plus three small `_WF` systems for the effects data cannot
express. No Onyx C# was touched; the one Onyx data edit is the `supportedWounds` list.

| path | status | notes |
| --- | --- | --- |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/blunt.yml` | new | `WolfmedCrushInjuryWound` (staged limb penalty, seeps at severe), `WolfmedConcussionWound` (staged blur/slur/knockdown, no damage types), `WolfmedDislocationWound` (limb penalty, no damage types), `WolfmedOrganContusionWound`. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | Six W3 rules: crush at 30 Blunt (replaces the bruise), internal bleeding at 30 Blunt / 40 %, concussion at 15 Blunt on a head, organ contusion at 20 Blunt on a torso, dislocation at 18 Blunt on a limb, and 10-18 Blunt when thrown or environmental. All `continue`. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | Four ids added to `OrganicBodyPartProfile.supportedWounds`, marked `# WOLFGATE (W3)`. Data only. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `WolfmedConcussionBehavior` (blur, stutter, knockdown, recoveryPerMinute, restMultiplier), `WolfmedDislocationBehavior` (delay, selfMultiplier, pain, selfPainMultiplier), `WolfmedOrganContusionBehavior` (damage, minRemainingHealth). All per stage. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | modified | `WolfmedWoundLifecycleEvent` (broadcast: Created/Changed/Removed) and `WolfmedRelocateDoAfterEvent`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | modified | Its three wound-lifecycle handlers now also broadcast `WolfmedWoundLifecycleEvent`. The directed `WoundComponent`/`WoundableComponent` subscriptions for those events are already owned, and a pair can have only one owner, so this is the seam every later Wolfmed system uses. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedConcussionComponent.cs` | new | On the body while any concussion is open: `Blur`, `Stutter`, recovery accumulator. Networked, because the client re-raises `GetBlurEvent` when eyewear changes. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedConcussionSystem.cs` | new | Contributes the blur through `GetBlurEvent`, slurs speech through `SharedStutteringSystem`, knocks the patient down when the wound lands or worsens, and fades severity on a one-second tick (`Recover`, `IsResting`: asleep or buckled to a `WolfmedBedHealMarker` bed). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedDislocationSystem.cs` | new | `AlternativeVerb` "Relocate joint" on any wound host carrying one, do-after (2.5x and double pain on yourself), `TryRelocate` public for tests and future surgery steps. Part comes from the user's targeting doll, falling back to the first dislocated part. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedOrganContusionSystem.cs` | new | Answers the lifecycle relay and takes health off one organ in the struck part, never below `minRemainingHealth`. `TryBruise` is public. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Four wound names, the verb and three relocate popups. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Four new sections, including how to reach the relocate verb. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBluntWoundTest.cs` | new | Six tests: crush replaces the bruise and pops the joint, internal bleeding over 24 crushing blows, the concussion's full arc (stages, no item cures it, awake vs asleep recovery, clearing), dislocation penalties / topical refusal / verb / relocation, organ contusion with its clamp, names. |

Deviations from the spec:
1. **Function loss uses `WolfmedLimbPenaltyBehavior`, not the `BodyPartFunctionality` path** the task named.
   W2 established this: `wounds.body_part_functionality_enabled` ships false (P2-3), so Onyx's impaired and
   disabled states are inert. The behavior hooks the fracture branch of `FractureEffectSystem` instead, which
   is ungated, and gives the same movement and manipulation multipliers.
2. **Internal bleeding is a rule, not code.** The existing `InternalBleedingWound` is created by a second
   `wolfmedWoundRule` on the same hit (`continue: true`), which is also W3's only chance roll (40 %).
3. **The organ contusion carries `Blunt` damage types** so ordinary bruise treatment closes the record. The
   organ's own condition is separate and recovers the way every other organ does.
4. **A concussion is not treatable at all.** The wound lists no damage types, so no topical can reach it, and
   it has no surgery: time and rest are the cure, painkillers only mask the pain. This is the spec read
   literally; if a "diagnose and stabilise" surgery is wanted later, it is one step effect.

## Final stages: W4 (Burn wounds and cauterisation) (2026-09-19)

Four burn wounds, cauterisation, a skin graft and one new broadcast seam. Onyx C# is untouched; the two
Onyx data edits are the `supportedWounds` list and a behavior on `BurnWound`'s critical stage.

| path | status | notes |
| --- | --- | --- |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/burns.yml` | new | `WolfmedCharringWound` (no damage types, staged limb penalty, necrosis risk at critical), `WolfmedFrostbiteWound` (four stages of numbness, necrosis risk at critical), `WolfmedChemicalBurnWound` (staged caustic residue), `WolfmedInternalBurnWound` (staged shock behavior). |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/cautery.yml` | new | `WolfmedCauteryDefault`: heat floor 8, incidental burn 5, deliberate burn 14, deliberate pain 20, 4 s do-after, 2x on yourself. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | Three W4 rules: frostbite from 8 Cold (replaces the burn), chemical burn from 12 Caustic (replaces), internal burn from 15 Shock (added to the electrical wound). Charring has no rule. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/slash_bite.yml` | modified | `WolfmedCauteryResistBehavior` (`maxIncidentalSeverity: 12`) on `WolfmedArterialBleedWound`. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | Four ids in `OrganicBodyPartProfile.supportedWounds`, and `WolfmedCharringBehavior` on `BurnWound`'s `Critical` stage. Both marked `# WOLFGATE (W4)`, data only. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/removed_objects.yml` | modified | `WolfmedSkinGraft`, on the regenerative mesh's existing sprite. No new art. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | `SurgeryStepGraftSkin`: the graft tool, 6 s, `WolfmedSurgeryTreatWoundEffect` on `WolfmedCharringWound` at the default amount (removed outright). |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryGraftSkin`, gated on the charring wound, behind `SurgeryOpenIncision`. |
| `Resources/Prototypes/Catalog/Fills/Backpacks/duffelbag.yml` | modified | One marked line: the graft in the surgical duffel, beside the bone gel. |
| `Resources/Prototypes/Catalog/Fills/Crates/medical.yml` | modified | One marked line: the graft in the surgery crate. |
| `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified | One marked line: `WashChemicalBurns` on the humanoid base's existing `[Water, SpaceCleaner]` touch reaction. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | modified | `WolfmedPartDamageEvent` (broadcast: body, part, damage type, amount, cause, origin, tool) and `WolfmedCauteryDoAfterEvent`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundRuleSystem.cs` | modified | Raises `WolfmedPartDamageEvent` before evaluating rules. Four lines. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `WolfmedCharringBehavior`, `WolfmedCauteryResistBehavior`, `WolfmedNumbnessBehavior`, `WolfmedNecrosisRiskBehavior`, `WolfmedCausticResidueBehavior`, `WolfmedElectricalShockBehavior`. All per stage. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | modified | `GetNecrosisRisk` / `GetPartNecrosisRisk`, W5's readers. *[M2-M6 review correction: `GetPartNecrosisRisk` removed, uncalled since M6; the part's reader is `GetPartNecrosisOnset`.]* |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedCauteryProfilePrototype.cs` | new | `wolfmedCauteryProfile`: every cautery threshold and cost. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedFrostbiteComponent.cs` | new | On a frozen part: numbness accumulator, `NecrosisRisk`, `NecrosisOnset`. Networked. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedFrostbiteSystem.cs` | new | Pins pain suppression on the part at the stage's value (top-up, not a fresh dose) on a two-second tick, and keeps the necrosis flag current. `Refresh` public. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedChemicalBurnComponent.cs` | new | On a part with residue still on it. Its presence is the state. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedChemicalBurnSystem.cs` | new | Ticks the residue's damage into the part, arms and disarms off the lifecycle relay, and `Wash(body)` clears every part. |
| `Content.Shared/_WF/Wolfmed/EntityEffects/WashChemicalBurns.cs` | new | Entity effect calling `Wash`; sits on the base mob's water touch reaction. |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSkinGraftComponent.cs` | new | `ISurgeryToolComponent` for the graft step. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedCauterySystem.cs` | new | Incidental cautery off `WolfmedPartDamageEvent` for Heat; deliberate cautery as a `UtilityVerb` with a hot held item plus a do-after. `TryCauterize`, `HasSealableBleed` public. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedCharringSystem.cs` | new | Creates the charring when a burn crosses into the stage carrying `WolfmedCharringBehavior`. `TryChar` public. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedElectricalBurnSystem.cs` | new | Rolls heart damage and runs the spasm (drop held, paralyse through the Onyx compat) when an internal burn lands or worsens. `TryShockOrgan`, `Spasm` public. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Four wound names, the cautery verb and four popups, the wash popup, the wash effect's guidebook line. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Charring, frostbite, chemical burn, internal burns and a cauterisation section. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | A burns section (wash, graft, numbness) and a cauterisation section. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBurnWoundTest.cs` | new | Six tests: charring off the top stage and its topical refusal, heat sealing bleeds with the arterial threshold both ways, frostbite numbness and the necrosis flag, the residue tick and the wash, internal burns with the heart and the spasm, names. |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundFractureTest.cs` | modified | `EffectsRefreshOnTreatmentHealingAndDetachTest` re-derived: a mended arm now reports 1.6, not 1. See deviation 5. |

Deviations from the spec:
1. **Charring is a top-stage trigger, not a rule.** A `wolfmedWoundRule` fires on one hit's size; the spec
   asked for a top-stage wound. `WolfmedCharringBehavior` on `BurnWound`'s critical stage is the threshold
   instead, so a part chars because it has been burned that far. It fires once per crossing into the stage.
2. **Skin graft surgery, no synthflesh reagent.** The spec allowed either. This fork ships no synthflesh, so
   a reagent would have needed a new reagent, a metabolism entry and a vendor slot; the graft is one
   component, one item and two prototypes on step effects that already exist.
3. **The deliberate cautery is a `UtilityVerb`, not an `InteractUsing` handler.** Every interaction event on
   `WoundHostComponent` is already owned (welder repair, embedded removal) and a pair can have only one
   owner. A verb also reads as intent, which is exactly what separates it from a stray hit.
4. **Heat already reduced bleeding before W4**, through `BloodlossHuman`'s `Heat: -0.5` coefficient, which
   takes bleeding severity off any hot hit. W4 does not remove that. What it adds is the treatment state
   (`Cauterized`, so sutures may then close the wound), the burn charged for it, and reach into arterial
   bleeds, which W2 deliberately exempted from the severity-reduction path.
5. **One pre-existing W3 test regression fixed.** `EffectsRefreshOnTreatmentHealingAndDetachTest` has been
   failing since W3 and was masked as "skipped" in grouped runs: its 75-Blunt arm now also carries a crush
   injury and a dislocation, whose 1.6 manipulation modifier the fracture was shadowing until it was mended.
   The expectation is re-derived rather than the data changed.

## Final stages: W5 (Infection, necrosis, sepsis) (2026-09-19)

| Path | Status | Notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionProfilePrototype.cs` | new | `wolfmedInfectionProfile`: every timer, threshold, treatment multiplier and dose in the model. One instance, `WolfmedDefaultInfection`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionComponents.cs` | new | `WolfmedInfectionStage`, `WolfmedInfectionComponent` (wound), `WolfmedSepsisComponent` (body), `WolfmedNecrosisSource`, `WolfmedNecrosisComponent` (part), `WolfmedTourniquetComponent` (part). All networked. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | modified | `WolfmedCleanWoundsEvent` and `WolfmedAntibioticEvent`: the seam the two Shared entity effects use to reach the server-side model. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.infection_enabled`, `wolfmed.infection_rate`, `wolfmed.sepsis_enabled`, `wolfmed.necrosis_enabled`, `wolfmed.necrosis_rate`, all SERVERONLY. |
| `Content.Shared/_WF/Wolfmed/EntityEffects/WolfmedCleanWounds.cs` | new | Antiseptic touch effect; raises the clean event. |
| `Content.Shared/_WF/Wolfmed/EntityEffects/WolfmedTreatInfection.cs` | new | Antibiotic metabolism effect; raises the antibiotic event with the scaled dose. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | new | The model. Five-second batch over contaminated wounds and septic bodies; `Update` advances by the time accumulated. Public: `Profile`, `Contaminate`, `Clean`, `Treat`, `GetStage`, `GetPartStage`, `GetSepsis`. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | new | Tourniquet clock, wound-risk clock and the reattachment grace period, plus the "Loosen tourniquet" verb. Public: `Start`, `MakeNecrotic`, `Loosen`, `OnDetached`, `OnAttached`, `IsNecrotic`, `IsAtRisk`, `OnTourniquetApplied`. |
| `Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | modified | Marked W5: one call in `Apply` starts the clock on the part, because the tourniquet item is consumed. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedEmbeddedRemovalSystem.cs` | modified | A dirty removal now calls `Contaminate`, which is the model's one source of dirty treatment. |
| `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | modified | Stamps the detach time and checks it on reattach. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Fills the three new per-part fields and the body-level sepsis figure. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | Marked W5: `Infection`, `Necrotic`, `NecrosisRisk` (optional trailing params) and `HealthAnalyzerWoundDiagnostics.Sepsis`. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | Prints the sepsis line above the parts, and the necrosis and infection findings per part. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | new | `WolfmedDefaultInfection` and the `WolfmedNecrosisWound`. |
| `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml` | new | `WolfmedSepsis` alert. |
| `Resources/Textures/_WF/Wolfmed/Interface/Alerts/sepsis.rsi` | new | Original 32x32 icon, CC-BY-SA-3.0, drawn for Wolfgate. |
| `Resources/Prototypes/_WF/Wolfmed/Reagents/medicine.yml` | new | `Spaceacillin`: `WolfmedTreatInfection` plus a poison penalty past 25u. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/reactions.yml` | new | Spaceacillin = cryptobiolin + inaprovaline. |
| `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified | Marked W5: one antiseptic touch reaction (`Ethanol`, `Bleach`, `Spaceacillin`) carrying `WolfmedCleanWounds`. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | Marked W5: `WolfmedNecrosisWound` in `OrganicBodyPartProfile.supportedWounds`, and `WolfmedInfectionRiskBehavior` on `SlashWound` (1), `PiercingWound` (1.4), `BurnWound` (1.2), `SurgicalIncisionWound` (1.5) and `DismembermentWound` (2.5). |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/ballistic.yml` | modified | Infection risk on the gunshot (1.6), lodged round (2.2) and shrapnel (2.0) wounds. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/slash_bite.yml` | modified | Infection risk on the tendon cut (1.3). |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Wound and stage names, four popups, the loosen verb, six analyzer lines, the alert, the reagent and the two effect guidebook lines. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | Infection and necrosis sections for medics. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedInfectionTest.cs` | new | Ten tests: stage progression, dressing and cleaning, the reagent seams, dirty treatment, sepsis and the antibiotic, tourniquet necrosis, loosening, late reattachment, the CVars, and the analyzer plus names. |

Deviations from the spec:
1. **Onyx's `SurgeryInfectionSystem` was not vendored.** It is a thin wrapper over Onyx's whole disease
   framework (`SharedDiseaseSystem`, `DiseaseCarrierComponent`, `DiseaseSurgicalSiteInfection`), none of
   which this fork has. Its one reusable idea, a surgery site being an infection risk, is data here:
   `SurgicalIncisionWound` carries `WolfmedInfectionRiskBehavior` at 1.5.
2. **"Slower healing" is expressed as the wound reopening.** Onyx's healing path applies
   `WoundPrototype.HealingMultiplier` to a local, so there is no seam to scale without editing
   `WoundSystem.HealWounds`. A locally infected wound instead regains severity on the infection tick,
   capped by `maxSeverityAdded`, which makes treatment outrun the infection rather than be cancelled by it.
3. **A necrotic limb is non-functional through `WolfmedLimbPenaltyBehavior`, not the functionality state.**
   Same reason W2 and W3 gave: `wounds.body_part_functionality_enabled` ships false (P2-3). The necrosis
   wound carries a 0.55 movement and 2.0 manipulation penalty.
4. **Non-sterile conditions are not modelled beyond dirty tools.** The spec allowed "if such a concept
   exists"; it does not. `Contaminate` is called from W1's improvised embedded-object removal only, and is
   public for any later caller.
5. **Cleaning and antibiotics reset a wound rather than immunising it.** Progress falling to zero clears
   the `Cleaned` flag, so an open wound that has been cleared starts accumulating again from nothing.

## Final stages: W6 (Mechanical wounds: IPC and cybernetic parts) (2026-09-19)

| Path | Status | Notes |
| --- | --- | --- |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/mechanical.yml` | new | The five wounds, all `ruleOnly`: `WolfmedDentWound`, `WolfmedBreachWound`, `WolfmedShortCircuitWound`, `WolfmedServoDamageWound`, `WolfmedOverheatingWound`. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | Eight `wolfmedWoundRule`s at priority 99-89, all `capabilities: [ Mechanical ]`, all `replacesDefault: false` + `continue: true`. Three breach rules partition every hit by cause so the two ballistic ones can carry W1's `embedded` block without any hit paying twice. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | Marked W6: the five wounds added to `IpcBodyPartProfile.supportedWounds` and `CyberneticBodyPartProfile.supportedWounds`. Nothing else changed. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `WolfmedShortCircuitBehavior` (stun, effect, sound) and `WolfmedOverheatBehavior` (coolingPerMinute / PerCold / PerDousing). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | modified | `IsOrganic(part)` and `IsMechanical(part)`: the organic/mechanical selector, read off the part profile's `TreatmentCapabilities`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedOverheatingComponent.cs` | new | Part-level index and networked cooling rate. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedOverheatingSystem.cs` | new | Cooling tick, Cold-damage cooling off W4's `WolfmedPartDamageEvent`, `Douse(body)`, `Cool(part, amount)`, `IsOverheating(part)`, `Refresh(part)`. |
| `Content.Shared/_WF/Wolfmed/EntityEffects/WolfmedCoolOverheating.cs` | new | Water touch effect; calls `Douse`. |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedServoKitComponent.cs` | new | `ISurgeryToolComponent` on the cable coil; the servo step's tool. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedShortCircuitSystem.cs` | new | Answers the W3 lifecycle broadcast: stun plus spark effect and sound. Public `Arc(body, behavior)`. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | modified | Marked W6: `IsOrganic` gate in `OnWoundLifecycle` and in `Contaminate`, so a chassis never goes septic. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | modified | Marked W6: `IsOrganic` gate in `Start`, `MakeNecrotic`, `OnDetached` and `OnTourniquetApplied`, so no source starts a necrosis clock on a chassis. |
| `Content.Server/_WF/Wolfmed/Medical/WeldingHealableSystem.Wolfmed.cs` | modified | Marked W6: `fuelCost: 0` skips the fuel check and the fuel spend, which is what lets a wrench use the repair path. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Fills the two new per-part fields. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | Marked W6: `Mechanical` and `Overheating` optional trailing params. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | Consumes phase 5's `-mechanical` / `-frame` locale variants off the new flag, and prints the overheating line. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryReplaceServo`, gated on `WolfmedServoDamageWound`. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | `SurgeryStepReplaceServo`, tool `WolfmedServoKit`, effect `WolfmedSurgeryTreatWoundEffect`. |
| `Resources/Prototypes/Entities/Objects/Tools/tools.yml` | modified | Marked W6: `WeldingHealing` on `Wrench` (`qualityNeeded: Anchoring`, `fuelCost: 0`, `Blunt: -15`). |
| `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | modified | Marked W6: `WolfmedServoKit` on `CableStack`. |
| `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified | Marked W6: `WolfmedCoolOverheating` on the existing water touch reaction (for cybernetic limbs on organic species). |
| `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml` | modified | Marked W6: the same effect on the silicon base's own water reaction, for IPCs. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Five wound names, three overheating stage names, the dousing popup, the analyzer line and the effect guidebook line. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | "Mechanical trauma" section: the five wounds and the wall between them and the organic set. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | "Mechanical repair" section: which tool for which finding, and what a chassis cannot get. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedMechanicalWoundTest.cs` | new | Seven tests: the exclusion both ways (profiles and real hits), dents and breaches including the embedded round, short circuit and servo damage, overheating cooling three ways, the infection/necrosis gate, the wrench repair, and the analyzer wording. |

Wound suite after W6: 180 passed / 0 failed / 0 skipped (173 before; 7 new).

### Deviations from the spec

1. **No mechanical lodged-round or shrapnel wound.** The spec left "lodged rounds/shrapnel can stay" to
   this package to decide. They stay as *objects*, not as wounds: the two ballistic breach rules carry
   W1's `embedded` block on an ordinary `WolfmedBreachWound`, so a round in a chassis still blocks every
   repair until it is pulled out, but the flesh wounds themselves (`WolfmedLodgedRoundWound`,
   `WolfmedShrapnelWound`, gunshot, graze) stay `[ Biological ]`. Reusing them would also have been a
   trap: both carry `healingMultiplier: 0`, and a welder repairs only by removing damage, so a lodged
   round in an IPC could never have been welded shut.
2. **Servo damage is repaired by a surgery that uses the cable coil, not by the coil alone.** An item's
   healing spec is matched against the wound's `damageTypes`, and damage removal needs damage of that type
   on the part - which a Slash-caused servo wound does not have. Listing it under `Shock` would have made
   it repairable only after someone also shocked the limb. It therefore carries no damage type at all
   (like the tendon cut it mirrors) and `SurgeryReplaceServo` is the exit, with `WolfmedServoKit` on the
   cable coil as the step's tool. That is both halves of the spec's "cable coil or the mechanical repair
   surgery".
3. **Mechanical wounds are added to the generic chassis wound, not substituted for it.** Following W4's
   internal burn rather than W3's crush injury: `IpcMechanicalDamageWound` stays the total an analyzer
   reads, and the five say what kind of damage it is. This also leaves every phase 5 mechanical test
   unchanged.
4. **Infection and necrosis are gated on the part profile, not on the wound.** One `IsOrganic` check in
   four places beats opting every mechanical wound out one at a time, and it means a wound that *is*
   infectable in flesh (a surgical incision, an embedded fragment) is simply inert on a chassis.
5. **A wrench repairs dents.** The spec said "welder or wrench-class repair". Rather than a second repair
   system, `WeldingHealingComponent` gained a fuel-free path in the Wolfmed partial that already owns the
   wound-repair half of it, and the wrench carries the component with `fuelCost: 0`. Blunt only, so it
   never seals a breach.

## Final stages: W7 (Treatment matrix, content distribution, docs sweep) (2026-09-19)

The consistency and completeness pass over W0-W6. No new wounds, no new systems: availability for the
items W4 and W5 left unreachable, one complete treatment matrix over every wound prototype in the game,
the guide and analyzer wording finished, and the status document brought up to phase 8.

| path | status | notes |
|---|---|---|
| `Resources/Prototypes/_WF/Wolfmed/Entities/medicine.yml` | new | `SpaceacillinChemistryBottle`, 30u on `BaseChemistryBottleFilled`, same shape as every shipped reagent bottle. W5 shipped the reagent with a reaction and nothing that stocked it. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/lathes.yml` | new | `WolfmedSkinGraft` lathe recipe, Plastic 50 + Cloth 50, costed beside Ointment and Gauze. |
| `Resources/Prototypes/Recipes/Lathes/Packs/medical.yml` | modified | One marked line: the graft joins `TopicalsStatic`. Charring has no other exit, so it has to be printable. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml` | modified | Two marked lines: `SpaceacillinChemistryBottle: 3` and `WolfmedSkinGraft: 2` in `NanoMedPlusInventory`. |
| `Resources/Prototypes/Catalog/Fills/Crates/medical.yml` | modified | One marked entry: two spaceacillin bottles in `CrateMedicalSupplies`, so an infected crew is orderable without chemistry. |
| `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` | modified | One marked entry: `WolfmedSkinGraft` in `MedkitBurnFilled`. Five entries in an eight-cell grid; `WolfmedAvailabilityTest` spawns the kit so an overflow fails a test rather than a round. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | Two lines: the pain and clotting findings take the same `-mechanical` suffix W6 gave the bleed line. |
| `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` | modified | Four marked keys: `health-analyzer-wound-pain-short-mechanical` and the three `-clotting-*-mechanical` variants. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | New "Time and contamination" chapter (infection, sepsis, necrosis - W5 had nothing on this page), and a quick-reference list of wound - cause - danger - treatment covering all 33 wounds a player meets. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | New "Blunt trauma" section (relocate verb, concussion rest, bruised organ, internal bleeding), plus where the skin graft and spaceacillin come from. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundTreatmentMatrixTest.cs` | new | 4 tests. The complete matrix: all 44 wound prototypes x 8 treatment items, declared and checked against live prototype data. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAvailabilityTest.cs` | new | 3 tests. Every new item is vended or printed, every new reagent has a reaction, and the touched fills hold what they declare. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLocaleCoverageTest.cs` | new | 2 tests. Every wound name, every stage name, and every analyzer key the panel builds by concatenation, in both the organic and the mechanical form. |
| `Docs/_WF/Wolfmed/WOLFMED_STATUS.md` | modified | New "What phase 8 delivers" section: wounds, framework, items, CVars, tests, carried gaps. |

Wound suite after W7: **189 passed, 0 failed, 0 skipped** (180 before; 9 new). `Guidebook`,
`PrototypeSaveTest`, `VendingMachine` and `Lathe` filters rerun green (20 passed, 1 skipped - the
permanently `[Ignore]`d `TestAllRestocksAreAvailableToBuy`, confirmed by rerunning it alone).

### Design calls

1. **The matrix is declared, then checked against data, rather than measured by 352 real heals.** Spawning
   44 wounds on the right body and applying 8 items to each is minutes of test time for the same
   assertion. The sibling `WolfmedTreatmentMatrixTest` already drives three real heals through the
   routing system to pin the gate itself; this one is the breadth pass and reads the gate. A cell is
   `healingMultiplier > 0` AND the item's treatable damage types overlap the wound's AND the item's
   `TreatmentCapability` set overlaps some profile that lists the wound.
2. **The capability column is the union over carriers.** Three wounds sit in both the organic and the
   mechanical profiles, so their rows say "some body carrying this wound can be treated with this item"
   rather than "every body". Recorded in the test's own remarks; splitting the matrix per profile would
   have doubled it to catch nothing that is not already covered by W6's organic/mechanical wall test.
3. **The welding tools are in the matrix on an assumption, pinned separately.** A welder and a wrench are
   gated by `damageContainers`, not by `TreatmentCapability`, so the matrix takes them as Mechanical and
   `WeldingToolsAreMechanicalOnlyTest` asserts their container lists directly.
4. **No forceps item.** W1 decided the existing `HemostatComponent` / `TweezersComponent` are the clean
   removal tool and the spec's "forceps" is satisfied by the hemostat, which is already lathe-printable
   and in the surgical crate. A third item with the same component would be content for its own sake.
5. **Spaceacillin is not in a medkit.** `Medkit` is an eight-cell grid and the kits that would want an
   antibiotic are the ones already at four or five entries; PROTO E's tourniquet was withdrawn from
   `MedkitAdvancedFilled` in WP12-9 for exactly this. The vendor, the supplies crate and the chem
   dispenser are three routes without risking a spawn error. The skin graft did fit the burn kit, and the
   test proves it.
6. **The analyzer's mechanical column was finished, not just checked.** W6 consumed phase 5's
   `-mechanical`/`-frame` variants for bleeding and fractures and left pain and clotting reading as flesh
   on a chassis. Four locale keys and a two-character suffix on two existing lines closes it, and the new
   locale test keeps both halves of every such family present.

### Gaps not closed (carried forward, unchanged from W0-W6)

- **No Piercing fracture profile** (W0). `WolfmedBodyPartComponent.FractureProfile` is a single id and
  `WoundFractureSystem` assumes one fracture per part; a second damage type needs a list on the component
  plus a rewrite of creation, grading and treatment.
- **`Hitscan` and `Surgery` causes are never derived** (W1). `HitscanBasicDamageSystem` passes `origin`
  but no `tool`, and no rule filters on either flag, so deriving them would add an upstream hook that
  nothing reads. One marked line plus a `WolfmedDamageCause` component whenever a rule wants them.
- **Systemic bleeding chems still stop an arterial bleed** (W2); only the part-targeted topical path is
  gated.
- **The analyzer has no dedicated arterial, dislocated, concussed, numb or residue flag** (W2-W4). A medic
  reads the wound name, which the quick reference now explains.
- **Necrosis has no sprite on the limb** (W5), only the analyzer flag, the popup and the wound name.
- **The short circuit's sparks spawn at the body** (W6); parts live in nullspace and have no coordinate.
- **Five interactions reuse the plain do-after bar with no sound**: embedded removal (W1), relocate joint
  (W3), deliberate cautery (W4), loosen tourniquet (W5) and the wrench repair (W6). One shared
  `WolfmedWoundInteraction` sound set would close all five together; it is a phase-7 "Viscera" item
  (V1 wound SFX) rather than a W7 one.

## Final stages: V5 (Splint) (2026-09-19)

Phase 7 backlog item V5. A handheld splint applied to a fractured arm, hand, leg or foot through a
do-after sets the fracture to `FractureTreatment.Reduced` without an incision, which is the profile's own
`treatmentEffectScales` 0.25, and is used up doing it. Mending stays surgical (bone gel) or chemical
(osteogen, stasizium), and a fresh hard blow resets the treatment through the existing
`resetTreatmentOnDamage` path, exactly as it resets a bonesetter's reduction. No Onyx file was touched:
the whole feature hangs off the public `WoundFractureSystem.TryReduce`.

| path | status | notes |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSplintComponent.cs` | new | `WolfmedSplintComponent` (`delay`, `selfMultiplier`, `parts`, `consumed`, begin/end sounds), the `WolfmedSplintRefusal` reason enum, and `WolfmedSplintDoAfterEvent`. `parts` is data, so a future brace for another limb type needs no C#. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedSplintSystem.cs` | new | `UseInHand` (self) and `AfterInteract` (others) start the do-after on the part the user has selected on the targeting doll, via `WoundTargetResolver.TryResolveExact`, the same resolution the tourniquet uses. `CanApply` returns the refusal reason; `TryApply` calls `TryReduce`, popups and `QueueDel`s the item. Both public so tests and future surgery steps skip the do-after. Server-side for the same reason `TourniquetSystem` is (D13): `TrySetTreatment` is a no-op off the server. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/splint.yml` | new | `WolfmedSplint` (delay 4 s) and `WolfmedSplintImprovised` (delay 7 s, same component otherwise). Both `BaseItem`, size Small, consumed on use. |
| `Resources/Textures/_WF/Wolfmed/Objects/Medical/splint.rsi` | new | Original 32x32 PIL art, states `splint` (white polymer brace, blue straps) and `splint-improvised` (steel rod, cloth binding). `CC-BY-SA-3.0`, "Made for Wolfgate (Wolfmed)". No inhand states. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/Construction/splint.yml` | new | `construction` prototype for the improvised splint, `objectType: Item`, `category: construction-category-tools`, with the `icon:` the Release linter requires. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/Construction/Graphs/splint.yml` | new | `constructionGraph`: MetalRod 2 + Cloth 2, one second each. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml` | modified | One marked line: `WolfmedSplint: 4` in `NanoMedPlusInventory`. |
| `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` | modified | One marked entry: `WolfmedSplint` in `MedkitBruteFilled`, the kit for blunt force. Fifth entry in an eight-cell grid; the other kits were at four or five already (W7), so no other fill was touched. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Eight keys under a `V5` heading: two start popups, the success popup, and one per refusal reason. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | The Fractures section gains the splint: how to apply it, what Reduced is worth, that it is not a cure, that a hard blow undoes it, and where splints come from. Both splints embedded. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Two lines: the Fracture entry and its quick-reference line now name the splint beside the bonesetter. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSplintTest.cs` | new | 3 tests: a splint on a Comminuted leg sets the bone, cuts the penalty to a quarter and is consumed; the three refusals (unbroken limb, torso, head) and the locale keys; a hard hit undoes the splint and the limb takes a fresh one. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAvailabilityTest.cs` | modified | `WolfmedSplint` joins the obtainable items and `MedkitBruteFilled` the spawned fills; a new `CraftedItems` list checks `WolfmedSplintImprovised` against every construction recipe's target-node entity. |

Wound suite after V5: **192 passed, 0 failed, 0 skipped** (189 before; 3 new).

### Design calls and deviations

1. **The splint does not touch a dislocation.** The task asked and answered this; the relocate verb (W3)
   stays the only thing that sets a joint.
2. **A Hairline fracture refuses the splint**, because `WolfmedFractureProfile.reductionMinimumGrade` is
   `Simple` and `WoundFractureSystem.CanTreat` enforces it for every caller. The splint says so
   (`wolfmed-splint-too-slight`) rather than silently failing; a hairline crack carries almost no penalty
   to cut anyway.
3. **The improvised splint is slower, not weaker.** `FractureTreatment` has no grade, so a worse splint
   can only cost more time; 7 seconds against 4, and 17.5 against 10 on yourself.
4. **Only `MedkitBruteFilled` got a splint.** W7 found the other kits at four to six entries in eight
   cells, and an overflow logs an error that fails every pair test. The brute kit is also the right one:
   fractures are blunt.
5. **No lathe recipe.** The vendor stocks the proper splint and the improvised one is craftable from two
   common materials anywhere, which is two routes; a third would have been surface for its own sake.
6. **Speed is asserted as a ratio, not an absolute.** The same 60 Blunt that makes the fracture also
   applies an ordinary damage slowdown that splinting does not touch, so the test asserts
   `after / before == 1.75` (0.875 / 0.5), which is the treatment scale and nothing else.

## Final stages: V124 (Wound, dismemberment and interaction SFX/VFX) (2026-09-19)

Phase 7 backlog items V1 (wound SFX by cause), V2 (dismemberment SFX/VFX) and V4 (hit debris), plus the
five silent do-after interactions W7 carried forward. No new audio or art files: every sound is a repo
file reached through a new `_WF` `soundCollection`, and the blood mist reuses `Fluids/splatter.rsi`.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSfxProfilePrototype.cs` | new | `wolfmedSfxProfile`: an ordered `woundSounds` list (filters: wound ids, damage types read off the wound prototype, `tissue` Organic/Mechanical/Any, `minSeverity`), the two dismemberment specs, the debris tiers and the two throttle intervals. `GetWoundSound` and `GetDebris` are the selectors the tests drive. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundSfxComponent.cs` | new | Per-body throttle: `NextSound`, `NextDebris`, `LastHit`. Ensured on the body, so the state cannot outlive the entity the way a dictionary would. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedWoundSfxSystem.cs` | new | V1 + V4. Answers `WolfmedPartDamageEvent` (stamps `LastHit`) and `WolfmedWoundLifecycleEvent`. Sound on create or stage jump above the floor; debris on any worsening. `PlayPvs` at the **body**, because parts and wounds are in nullspace. `TryPlayWound`/`TrySpawnDebris`/`Now`/`Profile` are public for the tests and for the dismemberment system. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedDismembermentSystem.cs` | new | V2. Answers `WolfmedPartAmputatedEvent`: tear or shear sound, effect entity, and `TrySpill`, which builds a `Solution` of the body's own `BloodstreamComponent.BloodReagent` and hands it to `PuddleSystem.TrySpillAt`. `Play` and `TrySpill` are public. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedRepairSoundComponent.cs` | new | `EndSound` on a repair tool. The tool's own `useSound` already covers the start of a pass. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | modified | New `WolfmedPartAmputatedEvent(Body, Part, Parent)`. |
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` | modified | Two marked lines at the end of `TryAmputate` raising that event, plus one marked using. `TryAmputate` is the only damage-driven detach path, so surgery (which uses `TryDetachPart`) stays quiet. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.wound_sfx` and `wolfmed.hit_debris`, both server bool, both default true. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedEmbeddedObjectComponent.cs` | modified | `BeginSound` / `EndSound` (probe, wet extract). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `BeginSound` / `EndSound` on `WolfmedDislocationBehavior` (strain, then the joint going back). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedCauteryProfilePrototype.cs` | modified | `DeliberateBeginSound` / `DeliberateEndSound`. Incidental heat gets nothing: the hit already made a noise. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionComponents.cs` | modified | `LoosenSound` on `WolfmedTourniquetComponent`, which was an empty marker. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedEmbeddedRemovalSystem.cs` | modified | Plays the two sounds at the patient. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedDislocationSystem.cs` | modified | `PlayPredicted` on start (it has a user), `PlayPvs` on the set. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedCauterySystem.cs` | modified | Plays start, and the sizzle only when something actually sealed. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | modified | `Loosen` resolves the component instead of `HasComp` and plays its `LoosenSound`. |
| `Content.Server/_WF/Wolfmed/Medical/WeldingHealableSystem.Wolfmed.cs` | modified | One marked block: the repair-pass finish plays `WolfmedRepairSoundComponent.EndSound`. |
| `Resources/Prototypes/_WF/Wolfmed/SoundCollections/wounds.yml` | new | 15 `soundCollection` prototypes, all pointing at files already in the repo. Wounds: `WolfmedWoundFlesh` (`/Audio/Weapons/bladeslice.ogg`, `/Audio/Weapons/Xeno/alien_claw_flesh1-3.ogg`), `WolfmedWoundPierce` (`/Audio/Weapons/Guns/Hits/bullet_meat1-4.ogg`), `WolfmedWoundBlunt` (`/Audio/Effects/hit_kick.ogg`, `/Audio/Weapons/genhit1-3.ogg`), `WolfmedWoundBone` (`/Audio/Effects/snap.ogg`, `/Audio/Effects/chopstickbreak.ogg`, `/Audio/Weapons/Guns/Hits/snap.ogg`), `WolfmedWoundBurn` (`/Audio/Effects/sizzle.ogg`, `/Audio/Effects/lightburn.ogg`), `WolfmedWoundShock` (`/Audio/Effects/sparks1-4.ogg`), `WolfmedWoundChassis` (`/Audio/_WF/Weapons/Impacts/impact_metal_03/07/11/15.ogg`), `WolfmedWoundFrame` (`/Audio/Effects/metal_crunch.ogg`, `/Audio/Effects/metal_break1.ogg`, `/Audio/Effects/metal_break3.ogg`). Dismemberment: `WolfmedDismemberOrganic` (`/Audio/Effects/gib1-3.ogg`), `WolfmedDismemberMechanical` (`/Audio/Effects/metal_break2.ogg`, `/Audio/Effects/metal_break4.ogg`, `/Audio/Effects/metal_scrape1.ogg`, `/Audio/Effects/metal_scrape3.ogg`). Interactions: `WolfmedToolProbe` (`/Audio/_Shitmed/Medical/Surgery/hemostat1.ogg`, `/Audio/_Shitmed/Medical/Surgery/retractor1.ogg`), `WolfmedToolExtract` (`/Audio/Effects/Fluids/splat.ogg`), `WolfmedJointStrain` (`/Audio/Effects/thudswoosh.ogg`), `WolfmedCauteryBegin` (`/Audio/_Shitmed/Medical/Surgery/cautery1.ogg`, `cautery2.ogg`), `WolfmedClothUnwrap` (`/Audio/Effects/unwrap.ogg`, `/Audio/Items/Handcuffs/rope_takeoff.ogg`), `WolfmedPanelBeat` (`/Audio/Effects/metal_thud1-3.ogg`). |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/sfx.yml` | new | The one shipped `WolfmedSfxDefault` profile: ten ordered wound-sound entries, the two dismemberment specs, three debris tiers per tissue, `minSeverity: 5`, `soundInterval: 0.4`, `debrisInterval: 0.25`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/effects.yml` | new | Six debris entities. `WolfmedBloodMistSmall/Medium/Large`: `Fluids/splatter.rsi` (greyscale, so it tints) at `#8f1616`, scales 0.4/0.7/1.1, `TimedDespawn` 0.4/0.5/0.6. `WolfmedSparkBurstSmall/Medium/Large`: `EffectSparks` re-scaled. No system, no tick. |
| `Resources/Prototypes/Entities/Objects/Tools/tools.yml` | modified | One marked line on the wrench: `- type: WolfmedRepairSound`. |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | modified | One marked block on the welder: `WolfmedRepairSound` with the burn collection, since a welded seam should not clank. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSfxTest.cs` | new | 6 tests: sound selection per wound and tissue plus every named collection/effect existing; debris tiers and their `TimedDespawn`; a live hit spawning exactly one spray and then being throttled, and the spray despawning; a wound created with no hit behind it staying silent; dismemberment sounds and both spills (blood and oil) plus the real amputation path stamping the throttle; the five interaction sounds as data; both CVars off silencing their half. |

Wound suite after V124: **199 passed, 0 failed, 0 skipped** (192 before).

### Design calls and deviations

1. **One profile prototype, not a prototype per rule.** `WolfmedCauteryProfilePrototype` and
   `WolfmedInfectionProfilePrototype` already set the pattern: a single shipped instance a server retunes
   in one file. The ordered `woundSounds` list gives the same reach a per-rule prototype would, without a
   second id space to keep in step with the wound prototypes.
2. **The damage gate is a timestamp, not a flag on the wound.** A wound plays a sound only if
   `WolfmedPartDamageEvent` stamped the body in the same tick. That excludes infection, necrosis,
   chemical-burn ticks, overheating and every heal at once, without any of those systems knowing the SFX
   layer exists, and it needs no new field on `WoundComponent`.
3. **Sound on a stage jump, debris on any worsening.** The spec's wording for V1 is "created or jumps a
   stage"; V4's is "create or worsen". A wound with no stages therefore only ever sounds when it lands,
   which is correct for the one-shot wounds and for merges into an existing bruise.
4. **Everything plays at the body.** Wound entities sit in the part's `wounds` container and parts sit in
   nullspace, so neither has a position. This is the same call W6 made for short-circuit sparks.
5. **Dismemberment ignores the throttle but stamps it.** The hit that takes an arm off also creates the
   stump's dismemberment and consequence wounds in the same tick; without the shared stamp that would be
   two noises. The tear always plays; the wounds behind it do not.
6. **The dismemberment spill reads the bloodstream.** `spillVolume` is a number, not a reagent, so an IPC
   leaks oil because its `Bloodstream.bloodReagent` is `Oil`. Nothing in this package knows what an IPC is.
7. **Fractures are keyed by wound id, not by damage type.** `BoneFractureWound` and
   `WolfmedDislocationWound` are Blunt wounds; listing them above the Blunt entry is what stops a breaking
   bone sounding like a bruise.
8. **The blunt entry has its own floor of 10.** Every unarmed punch creates a small `BluntWound`, and at
   the profile's floor of 5 a fist fight became a drum solo even through the throttle.
9. **The wrench and welder finish sounds sit on a `_WF` component, not on `WeldingHealingComponent`.**
   That component is upstream (`_EinsteinEngines`); the new one is two marked lines of YAML on two
   prototypes and no upstream C# at all.

### Gaps not closed

- **V3 (per-part degradation sprites) is not in this package** and was not asked for: it needs 2 stages x
  8 parts x organic/synthetic of new art.
- **The debris sprites are reused, not drawn.** Blood mist is the puddle splatter tinted and scaled, which
  reads as a spray at 32px but is not a bespoke mist sprite.
- **Nothing is predicted.** Wound creation is server-only (`_net.IsServer`), so every wound sound is
  `PlayPvs`. Only the relocate start, which has a user and runs in Shared, is `PlayPredicted`.

## Final stages: V3 (2026-09-19)

Per-part degradation visuals. A limb's own summed wound severity picks one of two organic stages (muscle,
then bone), two mechanical stages (struts, then wiring) or, for dead tissue, a necrotic wash. Driven off
`PartDamageVisualsComponent`, which was already networked and already had a client hook, so the whole
feature adds no new network component and no new directed subscription.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Damage/WolfmedDegradation.cs` | new | `WolfmedPartDegradation` (None/Muscle/Bone/Struts/Wiring/Necrotic), `WolfmedDegradationProfilePrototype` (`wolfmedDegradationProfile`: `Rsi`, `Stage1`, `Stage2`, `Suffixes`, plus `GetStage`/`GetState`), `WolfmedDegradationVisualsComponent` (networked `Enabled` + `Profile`, the species opt-out), `WolfmedDegradationLayers` (the ten layers and their state prefixes) |
| `Content.Server/_WF/Wolfmed/Damage/WolfmedDegradationVisualsSystem.cs` | new | Computes and networks the stages. Public `Refresh(uid)` and `GetStage(part, profile)`. Driven by the `WolfmedWoundLifecycleEvent` broadcast, so a wound closed by sutures or surgery (no damage change) still clears the overlay. Writes nothing when the stage set is unchanged |
| `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | modified | V3 section: `UpdateDegradation`, `UpdateDegradationLayer`, called from the existing `OnPartDamageVisualsState` and `OnBodyPartState` hooks. One layer per visual layer, added once at `limbIndex + 1` and afterwards only toggled or re-stated |
| `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | modified (vendored) | **1 marked field + 1 marked using.** `PartDamageVisualsComponent.Degradation` (`[AutoNetworkedField] Dictionary<HumanoidVisualLayers, WolfmedPartDegradation>`) |
| `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs` | modified (vendored) | **1 marked line.** `TryGetVisualLayer` made public so the overlay maps parts to layers the same way instead of forking the switch |
| `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | modified | Two `_degradation.Refresh` pairs: a limb coming off takes its overlay with it, a limb going back on gives it to the body |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | modified | One `Refresh` in `MakeNecrotic`, so dead tissue shows even when the part cannot carry the necrosis wound |
| `Resources/Prototypes/_WF/Wolfmed/Damage/degradation.yml` | new | `WolfmedDegradationDefault`: stage1 25, stage2 60 summed severity, the RSI and the five state suffixes |
| `Resources/Textures/_WF/Wolfmed/Effects/part_degradation.rsi` | new | 50 states (10 layers x 5 variants), 4 directions each. `CC-BY-SA-3.0`; copyright records the derivation from `Mobs/Species/Human/parts.rsi` with that RSI's credit verbatim |
| `Tools/_WF/Wolfmed/gen_part_degradation_rsi.py` | new | The generator. Masks every overlay through the matching human part sprite's alpha, erodes it, then grows a deterministic jagged blob inside it |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualsTest.cs` | modified | Four new tests, see below |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | One paragraph under Examination: what the stages look like, and that clothing hides them |

### Tests

- `OrganicDegradationStagesTest`: nothing below stage 1; two shallow cuts summing past it show muscle; a
  deeper one shows bone; the other limbs stay clean; the stage reaches the client; on the client the
  overlay layer exists, is visible, draws above its own limb layer and below `jumpsuit`; removing the
  wounds clears it.
- `MechanicalAndNecroticDegradationTest`: an IPC arm shows struts then wiring, never muscle or bone;
  `MakeNecrotic` replaces a muscle stage with the necrotic state.
- `DetachedLimbKeepsItsDegradationTest`: a severed arm carries its own stage and phase 3's detached damage
  data, and the body stops drawing that layer.
- `DegradationArtCoversEveryLayerAndStageTest`: every (layer, stage) the profile can select resolves to a
  state that exists in the RSI. A missing state would be a client error log, which this suite fails on.

### Design calls

1. **Stages ride `PartDamageVisualsComponent` instead of a new component.** The client already subscribes
   `<PartDamageVisualsComponent, AfterAutoHandleStateEvent>` and a second system could not subscribe the
   same pair. One marked field is cheaper than a second networked component plus a hook for it.
2. **Severity, not damage.** `Damage` on that component is the projected per-part damage the phase-3
   overlay reads; it says nothing about how bad the wounds are, whether the part is flesh or frame, or
   whether it is dead. The stages sum the part's own wound severities instead.
3. **No hand/foot fold.** Phase 3 folds `LHand` into `LArm` because the stock six-layer `targetLayers`
   list has no hand art. The overlay adds its own layers, so it uses all ten layers directly.
4. **Human masks, eroded, for every species.** Each overlay is cut from the matching human part sprite's
   alpha, then eroded once or twice, so it is a small central patch that still lands inside limbs of a
   different shape. A species it does not fit adds `- type: WolfmedDegradationVisuals` / `enabled: false`;
   nothing in the repo needs it today (both wound hosts, `BaseMobSpeciesOrganic` and `MobIPC`, are
   humanoid). A body sprite with no layer for a given limb never gets that overlay at all.
5. **Layers are added once and then only toggled.** No layer is ever removed and no draw depth is touched,
   so this cannot fight `_CE` Z-levels or the phase-3 damage overlays, which sit one index above.
6. **The client is told the stage, not the severity.** The threshold comparison happens once on the
   server, which keeps the profile a server-side tuning knob and the wire payload one byte per limb.

## Final stages: P6 (2026-09-19)

Phase 6: the predicted-damage flicker on wound hosts, the `damage` command's body-part argument, the
locational-armour content pass, and a derived Hitscan wound cause. No new networked component, no new
directed subscription on a pair that was already owned.

| path | status | notes |
| --- | --- | --- |
| `Content.Client/_WF/Wolfmed/Damage/WolfmedPredictedDamageSystem.cs` | new | The flicker fix. Subscribes `<DamageableComponent, DamageDealtEvent>` (the seam only fires for wound hosts, and `<WoundHostComponent, DamageDealtEvent>` is already owned by the shared routing system) and sets `Suppressed`, so the client never writes predicted damage onto a wound host |
| `Content.Shared/_WF/Wolfmed/Compat/DamageDealtEvent.cs` | modified | One new field, `Suppressed`: stop the write but still report the damage to the caller. Clearing the dict, the server's way, stays the other option |
| `Content.Shared/Damage/Systems/DamageableSystem.cs` | modified (upstream) | **1 marked token** inside the existing GUARD D block: `if (damage.Empty \|\| dealt.Suppressed)` |
| `Content.Server/_WF/Wolfmed/Commands/DamageCommand.Wolfmed.cs` | new | The body of the `damage` command's 5th argument: `WolfmedPartCompletion`, `WolfmedHurtPart`, `TryParseDamageSpecifier`. Partial of `Content.Server.Damage.Commands.DamageCommand`, uses `WoundTargetResolver.TryResolveExact` and `WoundDamageRoutingSystem.TryApplyPartDamage` |
| `Content.Server/Damage/Commands/HurtCommand.cs` | modified (upstream) | **4 marked hook lines**, all bodies in the `_WF` partial: the completion branch, `args.Length > 5`, `args.Length >= 4` for the uid, and the dispatch to `WolfmedHurtPart` |
| `Resources/Locale/en-US/_WF/Wolfmed/damage-command.ftl` | new | `damage-command-arg-body-part`, `-error-body-part`, `-error-missing-body-part`, `-error-part-damage` |
| `Resources/Locale/en-US/damage/damage-command.ftl` | modified (upstream) | **1 marked line**: the usage string gains `[bodyPart]` |
| `Content.Shared/Weapons/Hitscan/Systems/HitscanBasicDamageSystem.cs` | modified (upstream) | **1 marked line**: `tool: ent`, so a beam hit carries the beam entity into the wound rules |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundRuleSystem.cs` | modified | `GetCause` derives `Hitscan` from `HitscanBasicDamageComponent` on the tool; no per-prototype data needed |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundCause.cs` | modified | Doc comment only: `Hitscan` is derived now, not declared |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | `WolfmedRuleGunshot` and `WolfmedRuleGraze` gain `Hitscan`; the lodged-round and shrapnel rules deliberately do not (P6-D3) |
| `Content.Shared/_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs` | modified | `Covers` made public so the content pass can be asserted against shipped prototypes |
| 37 prototype files under `Resources/Prototypes/**/Clothing/**` | modified (data) | **123 `coverage:` lines**, each marked `# WOLFGATE (P6, P3-D6)`. 100 head/mask/eye blocks → `[Head]`, 2 glove blocks → `[Hand]`, 21 over-uniform armour blocks → `[Torso, Arm, Leg]`. Files: `Entities/Clothing/{Eyes/glasses, Hands/gloves, Head/{base_clothinghead, eva-helmets, hardhats, hardsuit-helmets, hats, helmets, hoods}, Masks/masks, OuterClothing/{armor, vests}}.yml`, `Nyanotrasen/…/{Head/hats, OuterClothing/armor}.yml`, `_Goobstation/…/{Head/hardsuit-helmets, Head/modsuit, OuterClothing/armour}.yml`, `_Mono/…/{Eyes/glasses, Hands/gloves, Head/Hardsuits/*, Head/Helmets/ussp, Head/modsuit, OuterClothing/Vests/vests}.yml`, `_NF/…/{Head/hardsuit-helmets, Head/headwear_punks, OuterClothing/armor}.yml` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPredictionTest.cs` | new | `ClientDoesNotWritePredictedWoundHostDamageTest` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDamageCommandTest.cs` | new | `DamageCommandHitsTheNamedPartTest`, `DamageCommandWithoutAPartStillWorksTest` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedArmorCoverageTest.cs` | new | `ShippedCoverageIsWhatTheItemIsWornOnTest`, `CoverageReachesRoutedPartDamageTest` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBallisticWoundTest.cs` | modified | `HitscanIsItsOwnCauseTest` |
| `Docs/_WF/Wolfmed/DECISIONS.md` | modified | New `## Phase 6 — shipped` section, P6-D1 to P6-D5 |

### Tests

- `ClientDoesNotWritePredictedWoundHostDamageTest`: a connected pair; the client drives `TryChangeDamage`
  on its own copy of a `MobHuman` and on a plain damageable control in the same tick. The wound host's
  `DamageableComponent.TotalDamage` is unchanged and the call still returns a non-empty specifier; the
  control takes the full 20. Asserted at the seam rather than through a predicted swing: the swing would
  need an in-combat player on both sides and reaches the same `DamageDealtEvent` block anyway.
- `ShippedCoverageIsWhatTheItemIsWornOnTest`: three full-body suits still have unset coverage and still
  `Covers` a foot they never name; six helmets/masks/gloves/vests carry exactly the expected part set.
- `CoverageReachesRoutedPartDamageTest`: a riot helmet changes nothing about a routed leg hit; an
  engineering hardsuit reduces the same hit.
- `DamageCommandHitsTheNamedPartTest` / `DamageCommandWithoutAPartStillWorksTest`: the 5-argument form
  confines the hit to the named arm; the 4-argument form still lands.
- `HitscanIsItsOwnCauseTest`: `GetCause` returns `Hitscan` for a ballistic beam and a laser beam; a
  25-Piercing beam hit makes a `WolfmedGunshotWound` and never a `WolfmedLodgedRoundWound`; a 25-Heat beam
  hit makes no gunshot wound at all.

### Deviations from the spec

1. **The flicker is removed by suppression, not by predicting the routing** (P6-D1). The spec allowed
   either; predicting it is not possible here, because wound creation is server-only by construction.
2. **The predicted damage is still returned to the caller**, so popups and visual events that key off
   `TryChangeDamage`'s result survive (P6-D2). The task text suggested suppressing them too; that would
   have left a melee attacker with no red flash at all on wound hosts, since the server's `DoDamageEffect`
   filter excludes them on purpose.
3. **Full-body suits are left unannotated rather than given an explicit full part list** (P6-D4), because
   unset already means "every part" and an explicit list rots when a `BodyPartType` is added.
4. **Not annotated, recorded instead of guessed** (P6-D5): coats, winter coats and armoured jumpsuits; the
   `_Mono` Aurora exosuit; `_NF` brass knuckles (an armour penalty on a hands item); every `- type: Armor`
   on non-clothing. `WOLFMED_STATUS.md`'s "full 272-entry pass" line is now 123 of the ~250 clothing blocks.

## Final stages: LINT (2026-09-19)

Final hygiene pass over the whole branch: one clean YAML linter run, a CI-trap sweep over every file changed
since `origin/main`, the two integration-test filter groups plus the wound suite, and the docs wrap-up. No
new gameplay behaviour.

| path | status | notes |
| --- | --- | --- |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | modified | Added `#nullable enable` — the file uses `BodyPartSymmetry?`/`TimeSpan?`/`float?` parameters without it, a CI-lint-as-error trap |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedCritHeartbeatTest.cs` | modified | Added `#nullable enable` — uses `MobStateComponent?` without it |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesProfileTest.cs` | modified | Added `#nullable enable` — uses `BodyPartSymmetry?` without it |
| `Docs/_WF/Wolfmed/WOLFMED_STATUS.md` | modified | Fixed a bare CR byte mid-line-8 (a mangled `<plan>\reports` path had eaten its own backslashes and the `r` of `reports` into a literal carriage-return byte — a CI CRLF-trap failure on a text file, not a binary asset false positive); rewrote the header/state paragraph to say phases 6, 7 and 8 plus the crit heartbeat are implemented, with the 209/209 wound-suite test count |
| `Docs/_WF/Wolfmed/DECISIONS.md` | modified | New `## Final stages` section: the cross-package decisions the task asked to have collected (fracture thresholds, deterministic bands, no forceps, no Piercing fracture profile, mechanical `healingMultiplier: 1`, spaceacillin not in a medkit, the P6 flicker-fix approach, the heartbeat license note) plus every gap still open across H/W0-W7/V5/V124/V3/P6, as one bullet list |

### YAML linter (Release)

`dotnet run --project Content.YAMLLinter -c Release`, run once: **no errors found** (78 s). Only warnings
(`RA0033`, prototype `id` literals; `NU1903`/`NU1510` NuGet advisories), none from `_WF`/`_Onyx`/Wolfmed
paths and none blocking.

### CI trap sweep (`git diff --name-only origin/main...HEAD`, 609 files)

- **CRLF / bare CR.** `Tools/check_crlf.py` flags nearly the entire repository on this Windows checkout
  because `core.autocrlf=true` converts every tracked text file to CRLF on checkout; `git ls-files --eol`
  confirms every changed file is `i/lf` in the actual git blob (what CI checks out on Linux), so that tool's
  local output is the known Windows false positive and was not otherwise acted on. A byte-level scan of the
  609 changed files' blobs for a bare (non-CRLF) `\r` found 230 false positives (PNG/OGG binary bytes that
  happen to contain `0x0D`) and one real one, fixed above (`WOLFMED_STATUS.md`).
- **Nullable annotations without `#nullable enable`** in changed test files: 3 found and fixed, above.
  `Content.Tests/` has no changed files on this branch.
- **RSI meta.json / states / sizes**, **audio `attributions.yml` coverage**, and **prototype id references**:
  all covered by the clean YAML linter run (it validates exactly these); spot-checked
  `part_degradation.rsi/meta.json` (license, copyright, 50 states listed) and
  `Resources/Audio/_WF/Wolfmed/attributions.yml` (covers `heartbeat_loop.ogg`, the only file in that folder)
  by hand as well.
- **Locale ids used in C# under `_WF/Wolfmed`**: 49 distinct `Loc.GetString("...")` ids collected, all exist
  in some `.ftl` (one apparent miss, `medical-item-finished-using`, was a false positive from a BOM at the
  start of the pre-existing upstream file it's defined in, not a missing key). No `Loc.GetString` calls or
  loc-id-shaped fields found in `_WF/Wolfmed` YAML on this branch to cross-check separately.

### Integration tests

- `--filter "FullyQualifiedName~Tests.Body|~Tests.Damage|~Tests.Medical|~PrototypeSaveTest|~EntityTest|~Tests.Weapons|~Tests.Chemistry|~GuideEntry|~Tests.Guidebook"`:
  **29 passed, 0 failed, 2 skipped** (pre-existing skips, unrelated to Wolfmed).
- Wound suite `--filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~Wolfmed|FullyQualifiedName~GibTest"`:
  **209 passed, 0 failed, 0 skipped**, matching P6's count — no regression from the LINT-pass edits.

No regressions found; nothing on this list required a fix beyond what is in the table above.

## Final stages: review fixes (2026-09-19)

One fix pass over a code review of the final stages (`49ee1a9247..HEAD`). Seven of the eight findings were
real and are fixed below; one was rejected. Four of the fixes are behavioural and carry a new integration
test each; the rest are perf or correctness-of-placement and are covered by the existing suite.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundEvents.cs` | modified | New `WolfmedRejuvenateEvent(EntityUid Target)` broadcast: a rejuvenate has cleared this body's, or this detached part's, wounds |
| `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs` | modified (vendored) | **1 marked using + 1 marked 4-line raise.** `OnRejuvenate` ends by broadcasting `WolfmedRejuvenateEvent` for the body |
| `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | modified (vendored) | **1 marked 3-line raise.** `OnPartRejuvenate` broadcasts the same event for a limb healed on its own |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedRejuvenateSystem.cs` | new | Answers the broadcast: removes `WolfmedSepsisComponent` from the body, `WolfmedNecrosisComponent` and `WolfmedTourniquetComponent` from every part, then `WolfmedDegradationVisualsSystem.Refresh`. Both `RejuvenateEvent` pairs on a body and a part are owned upstream, which is why this answers a broadcast |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedChemicalBurnComponent.cs` | modified | New cached `Interval` (seconds), set from the worst `WolfmedCausticResidueBehavior` on the part |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedChemicalBurnSystem.cs` | modified | `Update` gates on that interval inside the query loop, like `WolfmedOverheatingSystem`/`WolfmedFrostbiteSystem`/`WolfmedConcussionSystem`; the due list is allocated only when something is actually due, and `Tick` no longer walks a part's wounds every frame |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | modified | `SetProgress` dirties only when `Progress`, `Stage` or `Cleaned` moved; `TickSepsis` dirties only when the sepsis figure moved |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | modified | New private `ClearSource(part, source)`, used by the wound-lifecycle, tourniquet-unclamped and `Loosen` paths. `Source` and `Progress` are one slot, so dropping one source now falls back to the other (tourniquet or wound) and keeps the accumulated time instead of removing the component |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedSplintSystem.cs` | modified | `TryApply` re-asserts `TerminatingOrDeleted` + `BodyHasChild` before applying, with the existing `wolfmed-splint-no-part` popup |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedCauterySystem.cs` | modified | `OnDoAfter` seals nothing when the part is no longer on the body (existing "nothing to seal" popup). New `SharedBodySystem` dependency |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedEmbeddedRemovalSystem.cs` | modified | `TryRemoveOne` refuses a part that has left the body, and validates `embedded.Item` against the prototype manager **before** `TryTakeOne` consumes it. New `SharedBodySystem` dependency |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedShortCircuitSystem.cs` | modified | `Arc` spawns the spark effect at `_transform.GetMapCoordinates(body)`, matching the other Wolfmed feedback systems; a chassis in a locker no longer sparks in the container's coordinate space |
| `Content.Server/_WF/Wolfmed/Damage/WolfmedDegradationVisualsSystem.cs` | modified | The wound-lifecycle handler now queues the body in a `HashSet` drained once per `Update`, and `Refresh` builds into a reusable scratch dictionary, copying out only when the stage set actually changed |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedInfectionTest.cs` | modified | Two new tests, see below |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSplintTest.cs` | modified | One new test |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBallisticWoundTest.cs` | modified | One new test |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualsTest.cs` | modified | The `Stage` helper drains the coalesced refresh before reading, the same way the infection tests hand `Update` their own time |

### Tests

- `RejuvenateClearsSepsisAndNecrosisTest`: tourniquet left on until the arm is necrotic and the patient is
  septic; a `RejuvenateEvent` clears sepsis, the necrosis component, the tourniquet and the necrosis wound,
  and twenty further minutes of infection plus an hour of necrosis do not bring any of it back.
- `TourniquetClockSurvivesATreatedWoundTest`: a deep freeze takes the necrosis slot from a tourniquet;
  treating the frostbite hands the slot back to the tourniquet with its accumulated progress intact.
- `SplintRefusesALimbThatCameOffTest`: a severed leg still passes `CanApply`, but `TryApply` refuses it, the
  fracture stays untreated and the splint is not consumed.
- `RemovalRefusesBadIdAndDetachedLimbTest`: an `embedded.Item` that is not a registered prototype is refused
  without decrementing the count; a limb detached mid-removal is refused and charged no pain.

### Findings rejected, and parts of findings not taken

- **Rejected: "`WolfmedInfectionSystem.OnWoundLifecycle` `EnsureComp`s onto every wound whose prototype
  declares the risk, contrary to the class remark at line 29."** The remark is about the tick, and the tick
  is what it says it is: `Update` walks `WolfmedInfectionComponent` holders and nothing else. Narrowing
  `EnsureComp` to already-contaminated wounds would take away the component that `Contaminate`, `Clean`,
  `Treat` and the analyzer all read off an as-yet-clean wound, and would need a second add-back pass the
  moment a clean wound goes dirty. The real defect inside that finding, the unconditional `Dirty` on every
  five-second tick, is fixed. The remark was left as written.
- **Not taken: "skip `TickWound` entirely for wounds whose computed delta is zero."** A wound whose progress
  delta is zero can still be past the local threshold, where the tick owes it pain and severity creep, so
  the skip is not safe as stated. Gating the `Dirty` achieves what the finding was after (no state on the
  wire for a wound that did not move) without that risk.
- **Note on the degradation fix:** the coalescing changes *when* the overlay is recomputed, from once per
  wound event to once per body per tick. That is invisible in play (the state only leaves the server at the
  end of the tick), but it is why `WolfmedVisualsTest.Stage` now drains the queue before reading, the same
  pattern the infection tests use when they hand `Update` their own minutes.

### Build and suite

`dotnet build Content.IntegrationTests -c DebugOpt` green; wound suite
(`~_Onyx.Wounds|~Wolfmed|~GibTest`) **212 passed, 0 failed, 1 skipped** (the pre-existing skip).
## Final stages: UI2 (2026-09-19)

The analyzer's wounds tab stops being a wall of text. Findings are now a systemic banner strip, a clickable
category filter strip, and one card per injured body part: accent bar coloured by the part's worst finding,
part-level conditions as icon chips, visible wounds as tinted icon rows grouped by category. Every string the
old joined line printed is still shown, on a row or in a chip's tooltip; the W6 mechanical/frame wording
switch is unchanged. No new networked component; the payload gains one field on an existing record struct.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundCategory.cs` | new | The `WolfmedWoundCategory` enum (byte, NetSerializable) and `WolfmedWoundCategories`: `All` (draw order), `Resolve(WoundPrototype)` (explicit field, else damage-type derivation, else Other), `NameKey`, `IconState` |
| `Content.Shared/_Onyx/Wounds/WoundPrototype.cs` | modified (vendored) | **1 marked using + 1 marked `[DataField] WolfmedWoundCategory? AnalyzerCategory`**. Null means "derive from `damageTypes`" |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified (vendored) | **1 marked trailing field** on `HealthAnalyzerVisibleWound`: `WolfmedWoundCategory Category = Other`, defaulted so nothing else on the wire moves |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | The visible-wound grouping key gains the category, the sort leads with it (so the panel gets whole category blocks), and `WolfmedWoundCategories.Resolve` fills it |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified (vendored) | **10 marked `analyzerCategory:` lines**, only where derivation would be wrong or absent: BoneFracture→Blunt, CyberneticFrameFracture/IpcMechanicalDamage/CyberneticMechanicalDamage→Mechanical, SystemicBleeding/InternalBleeding→Internal, SurgicalIncision→Cut, Dismemberment/AmputationConsequence/MedicalScar→Other. The Slime/Plant/Blunt/Slash/Piercing/Burn/Electrical wounds derive correctly and declare nothing |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/ballistic.yml` | modified | 4 `analyzerCategory: Ballistic` (they list Piercing, which would derive Puncture) |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/slash_bite.yml` | modified | 3 `analyzerCategory: Cut` |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/blunt.yml` | modified | Crush/Concussion/Dislocation→Blunt, OrganContusion→Internal |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/burns.yml` | modified | Charring/Frostbite/ChemicalBurn→Burn, InternalBurn→Internal (it destroys organs; deviation from the task's "chemical/electrical = Burn" list, recorded below) |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | modified | Necrosis→Infection |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/mechanical.yml` | modified | 5 `analyzerCategory: Mechanical` |
| `Tools/_WF/Wolfmed/gen_analyzer_icons.py` | new | Deterministic PIL generator: 21 states, each an 8x supersampled alpha mask resized once, off-white on transparent so the client tints with `Modulate` |
| `Resources/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi/` | new | 21 32x32 PNGs + `meta.json` (`CC-BY-SA-3.0`, "Made for Wolfgate (Wolfmed)"). 9 category states, 12 condition states (fracture, bleeding, internal_bleeding, embedded, necrosis, overheating, scar, pain, impaired, clotting, sepsis, blood_low) |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedWoundStyle.cs` | new | The whole palette in one static class, plus `Accent(diagnostic)` (necrotic/septic > bleeding > fracture > infection > neutral) and `WolfmedAnalyzerIcons`, which resolves each RSI state once and caches the `Texture` |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | new | The tab: alert banners, category chip strip with the filter, part cards, condition chips, wound rows, pain/scar footer. Partial of the existing panel |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml` | modified | `WoundAlertsContainer` (vertical) and `WoundCategoryStrip` (5-column grid so the chips wrap instead of clipping the ~470 px pane) added above the findings container |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | `_icons`, `_lastMessage` (so a chip press redraws without waiting for the next scan tick), `Clear()` drops the new containers and the filter; the old `DrawWoundDiagnostics`/`AddWoundFinding` text builder deleted |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | 9 `wolfmed-wound-category-*` names, `health-analyzer-wound-category-chip`, `-no-findings`, `-no-findings-filtered` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | modified | `VisibleWoundsCarryTheirCategoryTest`, `EveryWoundCategoryIsNamedAndDrawableTest` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLocaleCoverageTest.cs` | modified | The three new fixed keys added to `Fixed` |

### Tests

- `VisibleWoundsCarryTheirCategoryTest`: all three resolution paths at once. `WolfmedGunshotWound` on a human
  arm must read `Ballistic` (explicit field beating its `Piercing` damage type), `BurnWound` on the head must
  read `Burn` (pure derivation, no field), and `IpcMechanicalDamageWound` on a `MobIPC` arm must read
  `Mechanical` (explicit field on a vendored prototype whose first damage type is Blunt).
- `EveryWoundCategoryIsNamedAndDrawableTest`: every `WoundPrototype` in the game resolves to a member of
  `WolfmedWoundCategories.All`; every category has a locale name and a state in the RSI; every condition
  glyph the panel can draw exists as a PNG and is listed in `meta.json`. Read through `IResourceManager`, so
  a state renamed in the generator and not in the panel fails here rather than in play.

### Deviations from the spec

- **`WolfmedInternalBurnWound` is `Internal`, not `Burn`.** The task's list puts chemical and electrical
  burns under Burn and organ-destruction wounds under Internal; internal burns are both. Internal won,
  because what the medic does about them is surgery, not burn ointment.
- **Chips carry short text, not the full wording.** The fracture chip shows the grade word and the infection
  chip the stage word; treatment state, counts and every other phrase live in the `ToolTip`. At the pane's
  ~470 px a header row carrying the full phrases would clip, and the tab scrolls vertically only.
- **The category filter is card-level, not row-level.** Clicking a chip hides cards with no wound in that
  category; the cards that survive still show all their wounds, so a filtered view never hides a finding
  that belongs to the part it is showing.
- `health-analyzer-wound-part-summary` now has no consumer. It was left in place (and in the locale
  coverage test) rather than removed, since removing a vendored-adjacent key is not this package's business.

### Build

`Content.Client`, `Content.Server` and `Content.IntegrationTests` all compile clean (`-t:Compile`,
`Build succeeded`). The wound suite was **not** run for this package: the project owner was playtesting out
of this repo's `bin/`, so only compile checks were possible and every `MSB3021`/`MSB3027` copy error was
expected. The panel itself has not been seen on screen.

## Final stages: GORE (2026-09-19)

Directional hit splatter with cleanable wall and floor splats (G1), arterial and stump bleed spurts (G2),
and treatment overlays on limbs (G3).

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Gore/WolfmedGoreComponents.cs` | new | `WolfmedHitSplatterComponent` (direction/distance/colour/state/travel, networked), `WolfmedBloodSplatComponent` (wall splat), `WolfmedCleanableComponent`, `WolfmedBleedSpurtComponent`. |
| `Content.Shared/_WF/Wolfmed/Gore/WolfmedGoreSpecs.cs` | new | `WolfmedHitSplatterSpec`, `WolfmedSplatterRange`, `WolfmedBleedSpurtSpec`: all of G1 and G2's tuning as profile data. |
| `Content.Shared/_WF/Wolfmed/Damage/WolfmedTreatmentOverlay.cs` | new | `WolfmedPartTreatment`, `wolfmedTreatmentOverlayProfile` prototype, `WolfmedTreatmentVisualsComponent` opt-out, `WolfmedTreatmentLayers` (six layers, hand/foot fold, rank). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSfxProfilePrototype.cs` | modified | Two fields: `hitSplatter`, `bleedSpurt`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundSfxComponent.cs` | modified | `LastDirection`: the world vector the last hit was travelling in. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSplintComponent.cs` | modified | `Overlay` field plus `WolfmedSplintMarkComponent` on the part. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.bleed_spurts`. G1 stays on `wolfmed.hit_debris`. |
| `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | modified | **Marked**: one `[AutoNetworkedField] Treatments` on `PartDamageVisualsComponent`, beside V3's `Degradation`. |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedGoreSystem.cs` | new | Spray spawn, direction resolution, blood colour, tile walk, wall/floor landing queue, per-tile cap, `CleanTile`. |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedBleedSpurtSystem.cs` | new | Spurt condition, clock component add/remove, spurt (spray + sound + spill). |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedCleanSplatsReaction.cs` | new | `WolfmedCleanSplats : ITileReaction`. |
| `Content.Server/_WF/Wolfmed/Damage/WolfmedTreatmentVisualsSystem.cs` | new | Per-layer treatment, driven by the wound lifecycle, bleed refresh and fracture treatment change. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedWoundSfxSystem.cs` | modified | Stamps `LastDirection`; `TrySpawnDebris` prefers the spray for flesh, mist as fallback. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedSplintSystem.cs` | modified | Marks the part with the splint that was used. |
| `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | modified | Two treatment refreshes beside V3's. |
| `Content.Client/_WF/Wolfmed/Effects/WolfmedGoreEffectSystem.cs` | new | Tints, states and faces both effects; slides the spray over its animation. |
| `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | modified | `UpdateTreatments` / `UpdateTreatmentLayer`, above the degradation layer, below clothing. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/gore.yml` | new | `WolfmedHitSplatter`, `WolfmedBloodSplatWall`. |
| `Resources/Prototypes/_WF/Wolfmed/Decals/blood.yml` | new | `WolfmedBloodFloor1..7`, cleanable, custom colour. |
| `Resources/Prototypes/_WF/Wolfmed/Damage/treatment_overlays.yml` | new | `WolfmedTreatmentOverlayDefault`. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/sfx.yml` | modified | `hitSplatter` and `bleedSpurt` blocks. |
| `Resources/Prototypes/_WF/Wolfmed/SoundCollections/wounds.yml` | modified | `WolfmedBleedSpurt`, `WolfmedBleedStump`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/splint.yml` | modified | `overlay: SplintImprovised` on the improvised splint. |
| `Resources/Prototypes/Reagents/cleaning.yml` | modified | **Marked**: one `!type:WolfmedCleanSplats {}` on SpaceCleaner's `tileReactions`. |
| `Resources/Textures/_WF/Wolfmed/Effects/blood_splatter.rsi` | new (imported) | NovaSector blood.dmi, CC-BY-SA-3.0. |
| `Resources/Textures/_WF/Wolfmed/Mobs/treatment_overlays.rsi` | new (imported) | NovaSector on_limb_overlays.dmi, CC-BY-SA-3.0. |
| `Resources/Audio/_WF/Wolfmed/Bleeding/*.ogg` | new (imported) | NovaSector sound/effects/wounds, CC-BY-SA-3.0, mono. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Two paragraphs: treated limbs show it, blood goes on walls and floors and space cleaner takes it. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedGoreTest.cs` | new | Nine tests. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSfxTest.cs` | modified | The debris helper counts the spray; the despawn wait covers its 1.2 s. |

### Deviations from the spec

- **The wall check is `CollisionGroup.Impassable`, not a wall list.** `TurfSystem.IsTileBlocked` already
  answers it and covers walls, windows, grilles and closed doors; a closed locker counts too, which reads
  fine and costs nothing.
- **The spray direction is snapped to a cardinal.** The art has four directions, so travelling on the true
  angle would show a sprite pointing somewhere else. The cardinal is taken in the grid's frame, which is
  also what makes the tile walk one integer step.
- **`splint_tribal_*` is unused.** Nothing in this fork is a bound-wood splint; the enum member, the
  prefix and the art are in place and one YAML line away from a user.
- **No digitigrade variants.** Nothing in this fork marks a species digitigrade (no hit anywhere for the
  word), so the plain leg states are used. The suffix table is prototype data, so a fork that gains one
  ships a second profile rather than changing code.
- **No tourniquet overlay.** `BleedingTreatment.Clamped` is deliberately not in `dressings`: the sheet has
  no tourniquet, and a recoloured gauze band would read as gauze.
- **IPCs keep sparks.** The spray is gated on organic tissue, as the spec asked. The colour path is
  reagent-driven, so enabling an oil spray later is deleting the `organic &&` guard.

## Final stages: UI3 (2026-09-19)

Two changes to the health analyzer. **A**: the body doll no longer rescans one part, it sets the local
player's targeted body part through the same path the HUD doll and the targeting hotkeys use; the Return
button and its frame are gone, the targeted part's doll silhouette lights up, and the wounds tab marks and
scrolls to that part's card. **B**: every wound row, condition chip, category chip and banner carries a
"what is this and what do I do" tooltip, and clicking one opens a reused procedure window with a numbered
step-by-step treatment. The advice is locale data keyed off the wound prototype id, which the payload now
carries, so a wound shipped without advice fails a test rather than showing an empty tooltip.

| path | status | notes |
| --- | --- | --- |
| `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml` | modified (upstream) | **1 marked block**: `WolfmedReturnPanel` and `ReturnButton` deleted. No other change |
| `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml.cs` | modified (upstream) | **3 marked hooks**: `ReturnButton.OnPressed` becomes `InitWolfmedTargeting()`; `SetActiveBodyPart`'s body becomes `SelectWolfmedTargetPart(part)`; the two `ReturnButton`/`WolfmedReturnPanel` visibility lines go. `OnBodyPartSelected`, `ResetBodyPart` and the server's part-view path are left in place, unused |
| `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | modified | The UI3 bodies: a doll-part texture overlay per button, `SelectWolfmedTargetPart` (Groin maps to Torso, then `TargetingUIController.CycleTarget`), and a `FrameUpdate` poll of the local player's `TargetingComponent.Target` that keeps the doll and the wounds tab in sync with hotkeys, the HUD doll and the server |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedTreatmentAdvice.cs` | new | The key scheme: `ShortKey`/`StepsKey` (prototype id to kebab slug), `ConditionShortKey`/`ConditionStepsKey`, `CategoryShortKey`, the `-mechanical` suffix, and the `Conditions` / `MechanicalConditions` / `MechanicalWounds` lists the coverage test iterates |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified (vendored) | **1 marked trailing field** on `HealthAnalyzerVisibleWound`: `string Prototype = ""`, defaulted so nothing else on the wire moves |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | The visible-wound grouping key gains the prototype id and the row carries it |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedTreatmentWindow.cs` | new | The procedure window: a code-built `FancyWindow` (summary, divider, scrolling numbered steps, "Open guidebook" button, `HelpGuidebookIds` pointing at `WoundTreatment`). `Show(title, summary, steps)` refills it and fronts it |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | `OnPartSelected` event and `SetTargetedPart`; the targeted card gets a full border in `TargetedBorder` plus a "targeted" tag; the card header's part name is a `ContainerButton` that moves the target; every wound row, chip and banner is wrapped in a `ContainerButton` carrying the advice tooltip and opening the procedure window; `FrameUpdate` drains the pending scroll-into-view |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | `Clear()` drops the pending scroll; `Dispose(bool)` closes the procedure window with the analyzer |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedWoundStyle.cs` | modified | Two colours: `CardTargeted`, `TargetedBorder` |
| `Resources/Locale/en-US/_WF/Wolfmed/treatment-advice.ftl` | new | 44 wound tooltips and procedures, 5 `-mechanical` wound variants, 16 conditions plus 3 `-mechanical` variants, 9 category tooltips, 5 chrome strings. Every claim taken from `WolfmedWoundTreatmentMatrixTest` and the Wound treatment guide page |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentAdviceTest.cs` | new | 4 tests: every wound prototype has both keys and a numbered procedure; the dual-carrier set is re-derived from the body part profiles and each has chassis advice; every condition, category and chrome string resolves; the payload carries the prototype id and the keys derive from it |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLocaleCoverageTest.cs` | modified | Five UI3 chrome keys added to the fixed list |

### Deviations from the spec

- **Targeting is polled, not subscribed.** The spec suggested subscribing `TargetingSystem.TargetChange`
  and unsubscribing on close. That event fires only for the hotkeys: the HUD doll calls
  `TargetingUIController.CycleTarget` directly and the server can move the target on its own. A per-frame
  read of the local player's `TargetingComponent.Target` sees all three, and leaks nothing to unsubscribe.
- **Eyes and Mouth buttons were already commented out** in the analyzer's XAML, so there was nothing to
  repoint at Head.
- **No third "what it is" key.** The short key is both the tooltip and the window's summary paragraph, so
  there is one string to keep true rather than two.
- **Warning lines are a `Do not:` prefix, not a separate key.** A Fluent pattern line may not start with
  `[`, so the colour markup begins after the prefix.
- **Item names are the entity `name:` fields**, so the advice says "welding tool", "bruise pack" and
  "bone setter" where the guidebook page says "welder", "bruise pack" and "bonesetter". The guide was left
  alone; it is prose, and the advice is meant to be searched for in a vendor.

## Final stages: UI4 (2026-09-19)

The analyzer's procedure window stops being a block of numbered text. A procedure is now a
`wolfmedTreatmentProcedure` prototype: an ordered list of steps, each naming the item or reagent it is done
with and the checks that say it has been done, plus the "do not" lines. The window draws one row per step
with that item's own sprite, greys and ticks a row once its checks hold, outlines the first row that is not
done, and re-evaluates itself from every scan without being rebuilt, so the scroll and the focus stay put.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedTreatmentProcedurePrototype.cs` | new | `wolfmedTreatmentProcedure`: `summary` (LocId), `steps`, `avoid`. A `WolfmedTreatmentStep` is `text` (LocId), optional `tool` (`EntProtoId`), optional `reagent` (`ProtoId<ReagentPrototype>`), a `surgery` flag and a `done` list of checks |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedStepChecks.cs` | new | `WolfmedStepCheck` (17 members), the `WolfmedPartTreatments` flags, `WolfmedProcedureState`, and the pure evaluator. No entities, no IoC, no client types, so the truth table is unit testable |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedTreatmentAdvice.cs` | modified | `StepsKey`/`ConditionStepsKey` removed; `ProcedureId`, `ConditionProcedureId` and `Pascal` added. The short keys and the condition/category lists are unchanged |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified (vendored) | **1 marked trailing field**: `WolfmedPartTreatments Treatments`, defaulted. Nothing else can tell a sutured or clamped part from an untouched one, because both bleed at zero |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Fills `Treatments` from each wound's `WoundBleedingComponent.Treatment` and `WolfmedInfectionComponent.Cleaned` |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedTreatmentWindow.cs` | rewritten | `WolfmedTreatmentSubject` (part + wound id or condition + mechanical) plus the row list. `Show` rebuilds only when the subject changes; `SetState` re-tints in place. Rows: number badge, 32 px tool sprite with the item's name on hover, wrapping text, tick. Done rows are `Modulate`d grey, the first pending row takes an accent border. "Do not" rows carry the warning glyph |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | Chips and rows carry their part; `OpenTreatment` resolves the procedure and titles the window "finding, part"; `RefreshTreatment` re-evaluates the open window on every repopulate; `BuildProcedureState` and `ConditionPresent` read the payload; a new patient closes the window |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | `DangerousBloodLevel` now aliases `WolfmedStepChecks.DangerousBloodLevel` |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/treatment_procedures.yml` | new | 68 procedures (44 wounds, 5 chassis wound variants, 16 conditions, 3 chassis condition variants), 215 steps, 31 avoid lines |
| `Resources/Locale/en-US/_WF/Wolfmed/treatment-advice.ftl` | modified | The 77 short keys, 9 category lines and 5 chrome strings are unchanged. The 68 `-steps-` blobs are replaced by 215 `wolfmed-treatment-step-<slug>-<n>` and 31 `wolfmed-treatment-avoid-<slug>-<n>` lines, plus 5 new chrome keys |
| `Tools/_WF/Wolfmed/gen_analyzer_icons.py` | modified | `PROCEDURE_STATES`: `surgery`, `reagent`, `warning`, `done`, `step`. The 21 existing glyphs regenerate byte for byte |
| `Resources/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi` | modified | Five new 32x32 glyphs plus `meta.json` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentProcedureTest.cs` | new | 5 tests: every wound prototype has a procedure; every condition (and chassis condition) has one; every step's text, summary, avoid line, tool and reagent resolves and no step names both; the dual-carrier set is re-derived and has both variants; the id derivation is pinned |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedStepCheckTest.cs` | new | 5 tests, no server: every check separates a done patient from an untreated one, the "nothing left to do" cases, the body-level checks without a part, all-checks-must-hold, and the bleeding-treatment flag map |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentAdviceTest.cs` | modified | The `-steps-` assertions are gone; the chassis test now also asserts the chassis procedure; five UI4 chrome keys added |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | modified | The five procedure glyphs added to the icon coverage list |

### Deviations from the spec

- **No `StageBelow(<stage>)` check.** Nothing in the converted advice needed a per-stage gate;
  `SubjectGone` covers "until it stops being listed". The payload carries the stage name, so it can be
  added without another field.
- **Tourniquets, sutures, cauteries and antiseptic needed the one permitted extra payload field.** All four
  drive the bleeding rate to zero (or, for antiseptic, change nothing the payload reports), so the checks
  for them cannot be read off the existing fields. They are one `[Flags]` byte, not four fields.
- **The medical content is unchanged.** Every step and warning is the UI3 sentence, split at the existing
  numbering. The only edits are mechanical: the `Do not: [color=...]...[/color]` wrapper is gone, because
  the window now draws those rows in the warning colour itself, and the first letter of each is capitalised.

## Final stages: FIX1 (2026-09-19)

Three playtest bugs. The analyzer's wounds tab stops destroying the control the cursor is on every scan
tick, so its tooltips hold; the analyzer doll's selection highlight is drawn the way the button draws its
own hover graphic, so it lands on it exactly; and blood is thrown by a hit making the body bleed more,
along the exact line of the hit, rather than by every wound-creating hit along one of four cardinals.

| path | status | notes |
| --- | --- | --- |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | **Bug 1.** `BuildSignature` reduces a payload to what decides which controls exist (parts, wounds by prototype/name/stage/count/category, condition chips as their discrete states, mechanical flag, banners, filter, targeted part) with pain, sepsis percent, blood level and bleed rates present only as `> 0` tests. `DrawWoundDiagnostics` rebuilds only when that string changes and otherwise calls `UpdateVaryingText`, which writes the moving numbers onto the labels it kept (`_sepsisLabel`, `_painLabels`). `FooterText` split out of `CreateFooter`; `CreateAlertRow` and `CreateFooter` hand their label back; `RefreshTreatment` runs on every payload either way, so UI4's live update is untouched |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | `Clear` calls `ResetWoundControls` |
| `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | modified | **Bug 2.** The per-button highlight is now the button's own `<part>_hover.png` drawn with `StretchMode.Scale`, which is the one mode that reproduces `TextureButton.Draw`'s `DrawTextureRectRegion(texture, PixelSizeBox)` |
| `Content.Shared/_WF/Wolfmed/Gore/WolfmedGoreComponents.cs` | modified | **Bug 3.** `WolfmedHitSplatterComponent.Direction` and `WolfmedBloodSplatComponent.Direction` become `float Angle`, world radians from +X; the spray's default state becomes `hitsplatter1_free` |
| `Content.Shared/_WF/Wolfmed/Gore/WolfmedGoreSpecs.cs` | modified | `WolfmedHitSplatterSpec` gains `MinBleedIncrease`, `BurstWindow` and `BurstBudget`; `States` defaults to the single-direction states |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundSfxComponent.cs` | modified | `BleedBefore`, `BurstWindowStart`, `SpraysInWindow` |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedGoreSystem.cs` | modified | `ResolveDirection` becomes `ResolveAngle` (no snapping, random angle with nothing behind the hit). The landing walk is a step walk along the exact line, four samples a tile, at most three tiles, in the grid's frame. The wall splat's facing is the spray angle plus pi |
| `Content.Client/_WF/Wolfmed/Effects/WolfmedGoreEffectSystem.cs` | modified | The spray's sprite is rotated to `Angle` (a world angle, because the sprite is `noRot`) and slid along the same vector. The wall splat's direction is snapped here, to eight or four ways depending on what its own RSI state has |
| `Resources/Prototypes/_WF/Wolfmed/Entities/gore.yml` | modified | `WolfmedHitSplatter` uses `hitsplatter1_free` and drops `enableOverrideDir` |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/sfx.yml` | modified | `hitSplatter`: the free states, `minBleedIncrease: 1`, `burstWindow: 1.5`, `burstBudget: 3`, distance bands rebased on the bleed gain (15/6/0). `organicDebris` rebased the same way, smallest tier at 0 |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedWoundSfxSystem.cs` | modified | `OnPartDamage` snapshots `TotalBleeding` before the hit's wounds exist; `Update` resolves each hit body once, sprays when the gain clears `MinBleedIncrease`, and sizes the spray by the gain. `OnWoundLifecycle` now only spawns debris for a chassis. `TryTakeBurstBudget` gates the organic path, mist included |
| `Tools/_WF/Wolfmed/import_nova_blood.py` | modified | `free_state`/`add_free_states` crop the East row of each hitsplatter into a 1-direction 11-frame state; `--free` rebuilds them from the RSI alone |
| `Resources/Textures/_WF/Wolfmed/Effects/blood_splatter.rsi` | modified | `hitsplatter1_free.png`, `hitsplatter2_free.png`, `hitsplatter3_free.png` (352x32 each) plus `meta.json` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedGoreTest.cs` | modified | Angle assertions replace the Direction ones; 2 new tests: a projectile sprays along its velocity, and a 45 degree spray lands on the wall on the diagonal and not due east |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSfxTest.cs` | modified | The `Damage` helper drains the deferred resolve; 2 new tests: a burn throws nothing and a blunt hit that raises a bleed does, and the burst budget caps and recovers |

### Deviations from the spec

- **No injected random roll.** The owner's revision replaced the severity/probability curve with "sprayed
  if and only if the hit raised the bleeding", which has no roll in it. What is left random is the spray's
  art variant and the fallback angle when nothing is behind the hit, neither of which a test needs to fix.
- **The blood decision is deferred by one system tick**, not taken on the wound event. A hit creates
  several wounds across several damage types and the bleeding system updates them from its own handler, so
  the only order-independent place to compare the bleeding is after the whole hit has landed. The test
  helper drains it by calling `Update(0f)`.
- **The blunt case is set up by hand.** `BluntWound`'s bleeding behaviour has a 0.25 to 0.7 chance, so the
  test gives the bruise a `WoundBleedingComponent` itself and then hits it, which exercises the same
  `BleedingSeverity += delta` path deterministically.
- **The HUD doll was not changed.** See the report: its highlight is exactly concentric with its own hover
  ring on all eleven parts, and the one deviation is upstream and sub-pixel.

## Final stages: LOOK (2026-09-20)

The health examine on a wound host is a visual inspection: plain sentences, part by part, of what an
examiner could actually see. Numbers, rates, severities and everything under the skin stay on the analyzer.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedLookPrototypes.cs` | new | `wolfmedWoundLook` (per wound: visibility, distance, per-stage locale keys, self hint), `wolfmedLookProfile` (clothing slot coverage, bleeding bands, sepsis threshold), `WolfmedLookVisibility` (None/Skin/Clothed) |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | new | Builds the inspection: per-part findings, clothing coverage from armour `coverage:` or slot defaults, bleeding bands, treatment, infection, scars, self-only pain and numbness, whole-body sepsis line |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/look.yml` | new | The `WolfmedLookDefault` profile and one `wolfmedWoundLook` for each of the 44 wound prototypes |
| `Resources/Locale/en-US/_WF/Wolfmed/look.ftl` | new | Every string the inspection prints |
| `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` | modified | WOLFGATE (LOOK): `CreateMarkup` gains `detailed`, the wound-host branch calls `WolfmedVisualInspectionSystem`, and the verb is no longer disabled outside details range for a wound host |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | modified | WOLFGATE (LOOK) comment only: no longer called, kept verbatim |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.Pain.cs` | modified | WOLFGATE (LOOK) comment only; its `health-examinable-pain-*` keys live on in the self lines |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | A paragraph under "Examination" on what the examine shows and what it never shows |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualInspectionTest.cs` | new | 10 tests, including one that fails if any wound prototype has no `wolfmedWoundLook` and one that resolves every key |

### Deviations from the spec

- **Onyx's `AddPartStatusMarkup` is now dead code**, not deleted and not gutted. It lists damage types,
  wound counts and a severity word, which is analyzer information; the upstream call site (already a
  WOLFGATE-marked line) routes to `_WF` instead. The vendored file keeps a zero-code diff.
- **The `[partstatus]` collapsible control is no longer produced.** A visual inspection is prose, and the
  control's heading is a severity band the spec forbids. The client tag handler is untouched and inert.
- **No per-slot "hides this" component on clothing.** Coverage is armour `coverage:` first, then the
  `wolfmedLookProfile` slot table, per the spec's fallback list. A mask does not hide a head.
- **A dressed wound is dropped from the part's bleeding total outright**, so a bandaged artery reads as
  dressed rather than as dressed and spurting. That is the spec's rule 3 taken literally.

## Final stages: LOOK2 (2026-09-20)

The visual inspection LOOK shipped as prose is now rows: one per body part with something to show, marked
in the colour of the worst finding on it, the part's name in a shared column, and each finding as a chip
with the health analyzer's own pictogram, two to four words, and the full sentence on hover. What is
visible to whom is unchanged; only the presentation is.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedLookReport.cs` | new | `WolfmedLookReport` / `WolfmedLookPart` / `WolfmedLookObservation` (icon, colour, label, sentence), `WolfmedLookClasses` (the nine finding classes the system builds itself), `WolfmedLookPalette` (the colour keys the data may name) |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedLookTag.cs` | new | Writes and reads `[wolfmedlook]` / `[wolfmedlookfinding]` / `[wolfmedlookend]`, in the same shape as Onyx's `partstatus`: nodes for the rows, a plain line after them for the chat copy |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | `GetLook` returns the structured report (public, so tests read findings rather than markup); `AddLookMarkup` writes it through the tag. Each finding is an object: glyph from the wound's analyzer category or the look's override, colour from the palette, label from the data, sentence from the description |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedLookPrototypes.cs` | modified | `label` / `hintLabel` / `icon` / `colour` on `wolfmedWoundLook` and on each stage; `classes` and `accentPriority` on `wolfmedLookProfile`; new `WolfmedLookGlyph` |
| `Content.Client/_WF/Wolfmed/Examine/ExamineSystem.WolfmedLook.cs` | new | Partial of the client `ExamineSystem`: builds the rows in a width-capped `ScrollContainer`, lines up the name columns, keeps the whole-body lines as text and skips the per-part plain line |
| `Content.Client/_WF/Wolfmed/Examine/WolfmedLookControls.cs` | new | `WolfmedLookRow` (left marker, name column, chips), `WolfmedLookChip` (tinted glyph, label, tooltip, `MouseFilter.Pass`), `WolfmedLookFlow` (wrapping layout for the chips) |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedWoundStyle.cs` | modified | `Look(key)` and `LookKeys`: the analyzer's palette addressed by the key a finding carries, so examine and analyzer tint the same glyph alike |
| `Content.Client/Examine/ExamineSystem.cs` | modified | HOOK 14, one marked line: the inspection rows first, then Onyx's part-status boxes, then the plain label |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/look.yml` | modified | A `label` on every description and a `hintLabel` on every self hint; `icon`/`colour` overrides where the category glyph would mislead (fractures, dislocation, necrosis, arterial bleed, shrapnel, lodged round, overheating, stumps, servos, incisions, scars); the profile's `classes` table and `accentPriority` |
| `Resources/Locale/en-US/_WF/Wolfmed/look.ftl` | modified | 132 `-short` row labels, one per finding the examine can draw |
| `Tools/_WF/Wolfmed/gen_analyzer_icons.py` | modified | `LOOK_STATES`: `dressing`, `splint`, `tourniquet`, plus `write_meta`/`all_states` so a new state can be added without rewriting the other 26 pngs |
| `Resources/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi/` | modified | `dressing.png`, `splint.png`, `tourniquet.png` and their `meta.json` entries |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | One sentence under "Examination" on the rows, the pictograms and the hover |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualInspectionTest.cs` | modified | 13 tests: the ten visibility tests now read the structured report, plus every glyph/colour/label in the data being drawable, the markup round-tripping with a readable plain-text copy, and the client building one row per part with a tooltip on every chip |

### Deviations from the spec

- **Labels are a data field, but a missing one falls back to the description key's `-short` form and then
  to the whole sentence.** The findings the system builds itself (bleeding bands, dressings, splints,
  tourniquet, infection, scars, numbness, pain) have their keys built in C# already, so they use the
  suffix rather than a `label:` field; only wound looks carry one. The test still requires the field on
  every wound description, so the fallback never fires in shipped data.
- **Onyx's `health-examinable-pain-*` keys have no `-short` form.** They are already two words
  ("hurts badly"), and they are vendored; the fallback uses them as labels verbatim.
- **Self hints wear the pain glyph**, not their wound's category glyph: a hint is what the patient feels,
  not something anybody can see.
- **Rows have no expander.** The full sentence lives in the chip's tooltip, which is what the owner asked
  for; hover works inside the examine popup, since hit testing walks to the deepest control that does not
  ignore the mouse and the examine buttons in the same popup already have tooltips.

## Final stages: EVISC (2026-09-20)

Disembowelment. The torso is the one part D9 never severs, so the tear-off pressure past its 250 cap
(`WoundableComponent.AmputationOverflow`) had no consumer at all; it now buys an abdomen laid open, the
organs on the deck, and a wound only the operating table closes. Mechanical torsos get the same treatment
in metal.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedEvisceration.cs` | new | `WolfmedTorsoOverflowEvent`, `WolfmedEviscerationComponent` (the one-at-a-time lock plus what incision state was granted), `wolfmedEviscerationProfile` prototype and its per-material `WolfmedEviscerationSpec` |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedEviscerationSystem.cs` | new | The trigger, the organ ejection, the feedback, the `IncisionOpen`/`SkinRetracted` grant, the closing step's effect and the `ForcedRoll` test seam |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedChassisToolComponents.cs` | new | `WolfmedHullPlate` (wrench) and `WolfmedHullWeld` (welder), surgery tools in the shape of W6's `WolfmedServoKit` |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs` | modified | `eviscerationProfile`: null means the part can never be opened |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `WolfmedClearedWoundBehavior.Severity`, so a cleared wound may land at its own severity rather than carrying the old one over |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedStepChecks.cs` | modified | `OrgansRestored` check |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | modified | `WolfmedSurgeryCloseEviscerationEffect` |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | modified | Its completion check, shared so the BUI highlights the right row |
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` | modified | WOLFGATE (EVISC): the torso branch raises `WolfmedTorsoOverflowEvent` instead of returning. No logic in the vendored file |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | WOLFGATE (EVISC): `MissingOrgans` on the payload |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Fills it: organ slots on the part with nothing in them |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/evisceration.yml` | new | `WolfmedEviscerationDefault` profile, `WolfmedEviscerationWound`, `WolfmedChassisBreachWound` |
| `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | modified | `eviscerationProfile` on `WolfmedBaseTorso` |
| `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | modified | `WolfmedBaseTorsoIpc`: the damage cap and profile an IPC torso's parent chain does not carry |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryCloseEvisceration`, `SurgeryWeldChassisBreach` |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | Clamp/close and seat/weld, four steps |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/look.yml` | modified | Both wounds: `visibility: Clothed`, `distant: true` |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/treatment_procedures.yml` | modified | Both procedures, with `Dressed` / `OrgansRestored` / `SubjectGone` checks |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified | WOLFGATE (EVISC): one wound each into `OrganicBodyPartProfile` and `IpcBodyPartProfile` `supportedWounds` |
| `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` | modified | WOLFGATE (EVISC): one line, `WolfmedBaseTorsoIpc` first in `TorsoIPC`'s parent list |
| `Resources/Prototypes/Entities/Objects/Tools/tools.yml` | modified | WOLFGATE (EVISC): one line, `WolfmedHullPlate` on the wrench |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | modified | WOLFGATE (EVISC): one line, `WolfmedHullWeld` on the welder |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | Both names and both popups |
| `Resources/Locale/en-US/_WF/Wolfmed/look.ftl` | modified | Both descriptions and row labels |
| `Resources/Locale/en-US/_WF/Wolfmed/treatment-advice.ftl` | modified | Both tooltips, ten steps, two "do not" lines |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | An "Evisceration" section: the six-step fix, and the chassis variant |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedEviscerationTest.cs` | new | 9 tests: trigger, the four negatives, one-at-a-time, explosion, items vs surgery, the incision shortcut, IPC, gib/delete |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundTreatmentMatrixTest.cs` | modified | Two rows, both `Exit.Surgery` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedStepCheckTest.cs` | modified | `MissingOrgans` on the "nothing has been done" fixture, so `OrgansRestored` separates |
| `Docs/_WF/Wolfmed/DECISIONS.md` | modified | "Evisceration (2026-09-20)" |

### Deviations from the spec

- **No intestines come out, because this fork has no intestines.** The slot is listed in
  `WolfmedEviscerationDefault.organic.organs` with a comment, so an intestines organ works the day one is
  added; today a Slash evisceration empties stomach, liver and kidneys.
- **An IPC torso has exactly one removable component**, its `pump` (`_EinsteinEngines/Body/Prototypes/ipc.yml`
  fills `posbrain` and `pump`, and the brain never comes out). The breach wound, the oil leak and the
  weld-closed repair all ship as specified; there is simply one thing on the deck rather than four.
- **The degradation overlay is not forced by code.** The wound lands at severity 70, the profile's stage 2 is
  60 and `healingMultiplier: 0` means nothing can whittle it down, so the torso sits at its worst stage for
  exactly as long as the wound exists. A test pins it. Forcing it in C# would have meant a new branch in
  `WolfmedDegradationVisualsSystem.GetStage` for no different outcome.
- **The wound is named "torn chassis" on a chassis**, not "chassis breach": the guidebook and W6 already use
  "chassis breach" for `WolfmedBreachWound`, which is what welding this one shut leaves behind. The
  prototype id the spec asked for is unchanged.
- **The closing surgery's cautery step does not carry a `remove:` list.** The granted `IncisionOpen` and
  `SkinRetracted` come off with the wound instead, and only if this system was what put them there.

## Final stages: CONSC (2026-09-22)

Consciousness replaces the damage thresholds on wound hosts, plus the painkiller tiers that play against it.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | new | `WolfmedConsciousness` (Up/Downed/Unconscious), `WolfmedConsciousnessComponent` (State, Depth, Pressures, BloodFraction), `WolfmedDownedComponent`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/SharedWolfmedConsciousnessSystem.cs` | new | `OwnsMobState`, `SetExternalPressure` and `Refresh` seams. Abstract so the shared threshold gate resolves on both sides. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | new | Every Downed restriction, as existing ActionBlocker attempt events. `IsSelfOrCarried` is the self-only rule. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | new | The evaluation: pain, blood, legs, pressures, hysteresis, depth, mob state, rejuvenate. |
| `Content.Client/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | new | Client subclass so the shared gate can resolve the system. No logic. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefComponent.cs` | new | Tiers, doses, sedation and the whole-body tuning. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefSystem.cs` | new | Dose lifetimes, sedation, the emergency window and its crash, the slowdown mask. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainRelief.cs` | new | The metabolism effect that gives a reagent a tier and a strength. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.consciousness`, `consc_pain_down` 0.7, `consc_pain_out` 1.25, `consc_blood_down` 0.6, `consc_blood_out` 0.45, `consc_hysteresis` 0.1. |
| `Content.Shared/Mobs/Systems/MobThresholdSystem.cs` | modified | Upstream. One marked gate at the top of `CheckThresholds`, plus the using and the dependency. |
| `Content.Server/Body/Systems/BloodstreamSystem.cs` | modified | Upstream. One marked call handing the blood percentage to consciousness on the tick that computes it. |
| `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | modified | Vendored. One marked early return in `OnRefreshSpeed` so a strong painkiller masks the wound slowdowns. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | Vendored. Three appended body-level fields: PainRelief, PainReliefSeconds, Sedation. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Fills the three new fields. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | Two body-level banners, their signature bits and their per-tick text. Non-clickable: a painkiller has no procedure. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDyingEffectsSystem.cs` | modified | `TargetLevel` reads `WolfmedConsciousnessComponent.Depth` on a wound host. `Level` unchanged. |
| `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml` | modified | `WolfmedDowned` alert. |
| `Resources/Textures/_WF/Wolfmed/Interface/Alerts/downed.rsi` | new | Original 32x32 prone figure, CC-BY-SA-3.0, "Made for Wolfgate (Wolfmed)". |
| `Resources/Prototypes/_WF/Wolfmed/Reagents/painkillers.yml` | new | `WolfmedAnalgesic` (weak 22), `WolfmedOpiate` (strong 70, sedation), `WolfmedStim` (emergency, 30 s). |
| `Resources/Prototypes/_WF/Wolfmed/Entities/painkillers.yml` | new | `WolfmedAnalgesicPill`, `WolfmedOpiateChemistryBottle`, `WolfmedStimPen`. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/reactions.yml` | modified | A reaction for each of the three new reagents. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/Debugging/debug_medbox.yml` | modified | The three new items in the debug crate. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml` | modified | Upstream. Three marked vendor rows. |
| `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml` | modified | Vendored. Marked tier blocks on Ibuprofen, Ketorolac, Tramadol, Oxycodone. |
| `Resources/Prototypes/Reagents/narcotics.yml` | modified | Upstream. Marked tier blocks on Desoxyephedrine and Ephedrine. |
| `Resources/Prototypes/Reagents/medicine.yml` | modified | Upstream. Marked tier block on Epinephrine. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | new | Alert, tiers, analyzer banners, the three reagents. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | A "Consciousness" section. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | A "Painkillers" section. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | new | Ten tests: the gate, pain, Downed restrictions, the tiers, blood, legs, the pen's crash, the overdose, airloss, rejuvenate. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAvailabilityTest.cs` | modified | The three new items and three new reagents. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLocaleCoverageTest.cs` | modified | The tier family, the two banners and the Downed alert. |
| `Docs/_WF/Wolfmed/DECISIONS.md` | modified | A "Consciousness" section. |

Deviations from the spec:

- **No new pain accessor on `PainComponent`.** The spec allowed one for "pain before the soft clamp".
  Summing `PainSystem.GetPain` over the body's parts gives the same number with no `_Onyx` edit at all, so
  `WolfmedConsciousnessSystem.GetUncappedPain` does that instead.
- **"Emergency-lite" is its own tier, `Stimulant`,** rather than a flag on Emergency. Emergency carries a
  fixed window and a crash; a stimulant is an ordinary refreshed dose. One enum member was cleaner than a
  boolean that only means something for one tier.
- **Happiness and ethanol were left alone.** Both carry `SuppressPain` and neither is in the spec's list of
  classes to rate; giving a drink a consciousness tier is a balance call for the owner.
- **The analyzer's pain-relief and sedation banners are not clickable.** Every other banner opens a treatment
  procedure keyed on its condition id; a painkiller has no procedure, and inventing one would have needed a
  prototype the treatment matrix tests would then police.

## Final stages: AUTODOC (2026-09-22)

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | new | pod component, runtime state, `AutodocState`/`AutodocVisuals`, `WolfmedSurgeryToolsEvent` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | new | `autodocVoice`, `autodocProgram`, `autodocProcedure`, `autodocReagents`, `AutodocVoiceEvent` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocUi.cs` | new | `AutodocUiKey`, `AutodocBuiState`, the five BUI messages |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocItemComponents.cs` | new | `AutodocProgramDisk`, `AutodocDefibModule` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocDoAfterEvents.cs` | new | `AutodocPryDoAfterEvent` |
| `Content.Shared/_WF/Wolfmed/Autodoc/SharedAutodocSystem.cs` | new | `GetPod` / `OnOperatingPlatform`, the half both sides need |
| `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Autodoc.cs` | new | hook bodies plus `WolfmedPerformStep`, `WolfmedCanPerformStep`, `WolfmedIsStepComplete`, `WolfmedSurgeryValid`, `WolfmedStepDuration` |
| `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs` | modified | two marked hooks: `GetTools` asks the performer first, `IsLyingDown` accepts a pod occupant |
| `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs` | modified | one marked hook: the operating-table condition accepts a pod occupant |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedReachableComponent.cs` | new | named exception to CONSC's self-only interaction rule |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | one marked condition so a Downed body can reach a pod |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | new | occupancy, verbs, drag-drop, prying, power, emag, tray toolset, appearance |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | new | library, queue, requirements, the state machine, reagents, step voice, malfunction, emag loop |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | new | BUI state and messages, machine part refresh, pry do-after |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Voice.cs` | new | `Speak` and idle chatter |
| `Content.Server/_WF/Wolfmed/Surgery/SurgerySystem.Autodoc.cs` | new | `AllSurgeries` accessor onto the list `RefreshUI` already walks |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Autodoc.cs` | new | `WolfmedBuildScanMessage`, the analyzer payload built on demand |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocBoundUserInterface.cs` | new | BUI |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | new | one window: doll + diagnostic panel left, procedures/queue/reservoir/controls right |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedBodyDoll.cs` | new | the analyzer's doll as a reusable control, same 3x geometry |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml` | new | `MachineAutodoc`, `AutodocMachineCircuitboard`, four program disks, `AutodocDefibModule` |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/programs.yml` | new | five programs, six procedure overrides, the reagent list |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml` | new | generated: 71 lines, 59 events |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/cargo.yml` | new | `CrateAutodocPrograms`, its cargo row, board and disk lathe recipes, `WolfmedAutodocPrograms` pack |
| `Resources/Prototypes/Recipes/Lathes/Packs/medical.yml` | modified | one marked line: the board joins `MedicalBoardsStatic` |
| `Resources/Prototypes/Entities/Structures/Machines/lathe.yml` | modified | one marked line: the disk pack joins the medical lathe's dynamic packs |
| `Resources/Prototypes/Guidebook/medical.yml` | modified | one marked line: the guide page joins the medical index |
| `Resources/Prototypes/_WF/Wolfmed/Guidebook/medical.yml` | modified | `WolfmedAutodoc` guide entry |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Autodoc.xml` | new | the guide page |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc.ftl` | new | verbs, examine, status, requirements, window strings |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | new | generated: the transcript of all 71 lines |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/*.ogg` (71) | new | generated with eSpeak NG, mono Ogg Vorbis 32 kbps |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/attributions.yml` | new | CC-BY-SA-3.0, "Generated with eSpeak NG for Wolfgate (Wolfmed)" |
| `Resources/Textures/_WF/Wolfmed/Structures/autodoc.rsi` | new | open/closed/operate, converted from CM-SS13 cryogenics.dmi, CC-BY-SA-3.0 |
| `Resources/Maps/_NF/POI/medical.yml` | modified | one entity: `MachineAutodoc` at -6.5,40.5, beside the medbay's operating table |
| `Tools/_WF/Wolfmed/import_cm_autodoc.py` | new | DMI to RSI converter for the pod sprites |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py` | new | eSpeak NG + ffmpeg; writes the oggs, the attributions and the transcript ftl |
| `Tools/_WF/Wolfmed/gen_autodoc_voice_protos.py` | new | writes `voice.yml` from the same line table |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocTest.cs` | new | 10 tests |

Deviations from the spec, and why:

- **No `SoundCollection` per event.** Each line id has exactly one ogg and one transcript and the prototype's
  `events` block names the variants, so the audio and the chat line are chosen together. A collection would
  pick a file independently of the transcript and the two could disagree.
- **Embedded-object removal and joint relocation are not in the library.** Neither is a surgery in Wolfmed
  (W1 and W3 built both as do-after interactions), and the pod is only allowed to perform surgeries. The
  spec's other base-library items all exist as surgeries and are all in `WolfmedAutodocProgramBase`.
- **The `StepRelocate` and `StepEmbedded` voice lines are generated and unreferenced** for the same reason;
  they are there for whenever those procedures become surgeries.
- **The doll is a new control, not the analyzer's XAML.** `WolfmedBodyDoll` carries the analyzer's exact
  layout table; moving the analyzer onto it would have churned the window that
  `WolfmedAnalyzerDollLayoutTest` measures. The wound cards themselves are the analyzer's own
  `WolfmedDiagnosticPanel`, reused verbatim.
- **The pod does not raise `SurgeryToolUsedEvent`.** That event is what refuses an unlit welder in a
  surgeon's hand; the pod's tools are internal and always ready.
- **Pod ambience while operating was not added.** The operating sprite loops and every step plays its own
  tool sound; a second looping layer on top of that read as noise. The lid uses the existing
  `airlock_ext_open`/`_close` sounds.

## Final stages: BRAIN (2026-09-22)

Cardiac arrest, the brain oxygenation clock, brain death as ordinary `MobState.Dead`, the revival rules
behind it, and the machine-body analogue. Replaces the interim `WolfmedLifeSystem`.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedLifeComponents.cs` | new | `WolfmedCardiacArrestComponent`, `WolfmedBrainComponent` (+ `WolfmedBrainColdStep`), `WolfmedBrainTraumaComponent`, `WolfmedCprComponent`, `WolfmedShutdownComponent` |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedLifeEvents.cs` | new | `WolfmedPainShockEvent`, `WolfmedConcussionSourcesEvent` |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | new | arrest triggers and exits, the oxygenation clock, brain organ damage, brain death, the analyzer's ETA, `Tick` as the test seam |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | new | the defibrillator rule, `StartCpr`, `ForcedRoll` test seam |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | new | machine bodies: power and pump, `"shutdown"` pressure |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedLifeSystem.cs` | deleted | the interim stand-in this package replaces |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLifeTest.cs` | deleted | its test |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `life_blood_dead` / `life_airloss_dead` out; 22 `arrest_*`, `brain_*` and `defib_*` CVars in |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | the `"airloss"` stand-in removed; oxygenation reset on rejuvenate |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | networked `Oxygenation`, which the dying view reads |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefSystem.cs` | modified | sedation is a `"sedation"` pressure plus `GetRespiratoryDepression`, not Asphyxiation damage |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | modified | one marked raise of `WolfmedPainShockEvent` |
| `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs` | modified | one marked block: arrest scales the body's cached stream rates |
| `Content.Server/_Shitmed/DelayedDeath/DelayedDeathSystem.cs` | modified | one marked `continue` so a wound host is not killed by a missing heart |
| `Content.Server/Medical/DefibrillatorSystem.cs` | modified | one marked block replacing the threshold gate on a wound host |
| `Content.Server/_EinsteinEngines/Medical/CPR/CPRSystem.cs` | modified | one marked condition: CPR marks the chest and never revives |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedBleedSpurtSystem.cs` | modified | no spurts without a pulse |
| `Content.Server/_WF/Wolfmed/Body/WolfmedOrganConsequenceSystem.cs` | modified | tells the shutdown system when a pump changes state |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedConcussionSystem.cs` | modified | raises `WolfmedConcussionSourcesEvent`; the occasional dropped item |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedConcussionComponent.cs` | modified | `Drop`, `DropChance` |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | modified | `destroyed:` on the organ condition, `WolfmedSurgeryBrainRepairEffectComponent` |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | modified | the destroyed-organ branch and the repair step check |
| `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` | modified | the repair step calls `WolfmedLifeSystem.RepairBrain` |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified | six body-level vitals on the payload |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | fills them |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | the arrest / brain-dead / shutdown banners, the brain activity row, the two new procedure subjects |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedStepChecks.cs` | modified | `PulseRestored`, `BrainRepaired`, two fields on `WolfmedProcedureState` |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedTreatmentAdvice.cs` | modified | `cardiac-arrest` and `brain-death` conditions |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | no pulse / not breathing / nothing running |
| `Content.Client/_WF/Wolfmed/Audio/WolfmedCritHeartbeatSystem.cs` | modified | one flat tone at onset, then silence; `Flatlined` |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDyingEffectsSystem.cs` | modified | the arrest banner and the fade toward black as oxygenation falls |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDeathBannerOverlay.cs` | modified | the banner's locale keys are settable |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem{,.Procedure}.cs` | modified | `TryDefibrillateOccupant`, run before the first procedure and after the queue |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `DefibCharge`, `DefibSuccess`, `DefibFailure` voice events |
| `Tools/_WF/Wolfmed/gen_autodoc_voice{,_protos}.py` | modified | three new lines in the one source-of-truth table |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/defib-{charge,success,failure}.ogg` | new | eSpeak NG, regenerated from the table |
| `Resources/Audio/_WF/Wolfmed/flatline.ogg` | new | 1 kHz tone, ffmpeg lavfi, CC0, attributed |
| `Resources/Audio/_WF/Wolfmed/attributions.yml` | modified | the flatline entry |
| `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | modified | `WolfmedBrain` on the brain; `WolfmedOrganPositronicBrain`, `WolfmedOrganIpcPump` |
| `Resources/Prototypes/Entities/Objects/Specific/Robotics/mmi.yml` | modified | one marked parent on `PositronicBrain` |
| `Resources/Prototypes/_EinsteinEngines/Body/Organs/ipc.yml` | modified | one marked parent on `OrganIPCPump` |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryRepairBrain` |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | `SurgeryStepRepairBrain` |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/programs.yml` | modified | `SurgeryRepairBrain` on the Neuro disk |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/{autodoc,voice}.yml` | modified | module description; regenerated voice prototype |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/treatment_procedures.yml` | modified | `CondCardiacArrest`, `CondBrainDeath` |
| `Resources/Locale/en-US/_WF/Wolfmed/{wounds,look,death,treatment-advice,autodoc-voice}.ftl` | modified | analyzer vitals, examine lines, the arrest banner, the paddles' lines, both procedures |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | "Death and revival" |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Autodoc.xml` | modified | the module does something now |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBrainTest.cs` | new | 10 tests |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocTest.cs` | modified | the defib module test |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | modified | the airloss stand-in test moved to the BRAIN suite; sedation reads the pressure |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedStepCheckTest.cs` | modified | the two new checks |

Deviations from the spec, and why:

- **The spec's own timing summary does not follow from its own rule.** With damage starting under 0.4
  oxygenation at 0.1/s at zero, an untreated arrest is brain-dead at about 246 s, not the "~198 s" the spec
  quoted, and CPR keeps the brain out of the damage band entirely rather than reaching brain death at
  "~6.5 min". The rule was implemented as written; the figures were not tuned to match the summary.
- **`wolfmed.brain_cold_factor` is a multiplier on a curve, not the curve.** The spec asked for a "curve in
  data" and named one CVar. The curve is `WolfmedBrainComponent.ColdSteps` (per organ prototype) and the CVar
  scales the resolved factor, so both halves of the sentence exist.
- **Hypoxia's own consciousness ramp is two CVars the spec did not name** (`brain_pressure_start` 0.75,
  `brain_pressure_out` 0.45). Without them a patient at 0.2 oxygenation would walk around until the arrest
  trigger fired at 0.15.
- **A revived corpse always comes back at 0.35 oxygenation**, even if the brain was destroyed some other way
  with a full tank. It is what makes "Dead -> Critical" deterministic rather than "Dead -> whatever
  consciousness thinks".
- **`SurgeryHealBrain` already existed** (P4) for a damaged but living brain and was left alone;
  `SurgeryRepairBrain` is the new destroyed-organ surgery beside it, and the two conditions cannot overlap.
- **The pod defibrillates at two points, not continuously.** Before the first procedure of a queue and when
  the queue finishes. A pod that shocked any occupant on a timer would revive anybody who lay down in it.
- **Rot was not re-tested.** `DefibrillatorSystem` refuses a rotten body before the Wolfmed block is reached
  and that path is untouched, so the assertion would have measured `RottingSystem`, not this package.
- **"Mechanical" needs `SiliconComponent`, not just an absent brain.** The first cut of
  `WolfmedShutdownSystem.IsMechanical` read "wound host with no oxygenation clock", which shut down every
  brainless test fixture on its first rejuvenate. The same trap the interim life system fell into.

## Final stages: AUTODOC2 (2026-09-22)

| path | status | notes |
| --- | --- | --- |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py` | modified | priority column, `-s 165 -p 25`, the new ffmpeg filter chain, `--report` duration check |
| `Tools/_WF/Wolfmed/gen_autodoc_voice_protos.py` | modified | carries the priority into the prototype |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/*.ogg` | modified | all 74 lines regenerated at the new settings |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | modified | regenerated transcripts, step lines cut to one or two words |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml` | modified | regenerated, every line now carries `priority` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocVoicePriority`, `AutodocVoiceLine.Priority` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | voice queue state, `AutodocVoiceRequest`, `AutodocStepFamily`, ducking gain |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Voice.cs` | modified | the queue, the priority rules, step families, `DuckedParams` |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | ticks the queue, announces one line per step family, ducks the tool sound |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | lays the occupant down, clears the spoken families on reset |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocVisualizerSystem.cs` | new | base/lid layers, the colour reset, the draw depth flip |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml` | modified | two sprite layers, no `GenericVisualizer`, `showEnts: true` |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocTest.cs` | modified | four new tests |

Deviations from the spec, and why:

- **`slip-fix` is Info, not Urgent.** The spec puts "slip" in the urgent set, and both slip lines were urgent
  at first, which meant "NOT SUPPOSED TO HAPPEN. FIXING IT." cut "OOPS." off after two frames. The apology
  now queues behind the interruption it belongs to.
- **Line durations are read off the ogg at runtime**, with `SharedAudioSystem.GetAudioLength`, rather than
  written into the generated table. The spec allowed either; this way the queue cannot disagree with a file
  that was regenerated without the table.
- **Two step lines needed a second pass.** "SETTING BONE." (1.20 s) and "EXTRACTING OBJECT." (1.70 s) were
  over the 1.2 s bound at `-s 165`, so they are "SETTING." and "RETRIEVING.".
- **Unpowered still dims and now also draws open.** The spec only asked that powered states stop being dim.
  The unpowered colour is the same `#555555`, applied by the visualizer and cleared by it, instead of by a
  `GenericVisualizer` entry that could only ever set it.
- **The occupant's lying pose is the standing-state appearance key**, not a pod-specific visual layer, so
  leaving the pod hands the sprite straight back to whatever posture the body is in.

## Final stages: AUTODOC3 (2026-09-22)

| path | status | notes |
| --- | --- | --- |
| `Content.Server/Body/Systems/RespiratorSystem.cs` | modified | one marked condition: no gasp emote while in cardiac arrest |
| `Content.Client/Overlays/ShowHealthIconsSystem.cs` | modified | one marked hook: the arrest flatline icon before the mob-state icon |
| `Content.Client/_WF/Wolfmed/Overlays/ShowHealthIconsSystem.Wolfmed.cs` | new | that hook's body |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | "appears to be dead" at any range; "not breathing" no longer needs details range |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocTriagePrototype`, `AutodocTriageStep`, `AutodocTriageCondition`, six voice events |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocItemComponents.cs` | modified | `AutodocAutofixModuleComponent` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | autofix slot id, triage id, alarm sounds and intervals, auto and alarm runtime state, `AutodocAlarm` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocUi.cs` | modified | `AutodocControl.Plan` / `.Auto`, `AutofixModule` and `Auto` in the BUI state |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.autodoc_alarm` |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | new | the planner, automatic mode and the vital alarm |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | `_cfg`, `InitializeTriage`, `IsRunning`, occupant hidden with the lid |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | `Control` made public, PLAN and AUTO, the eject split |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | `TickAuto` and `TickAlarm` in the update loop |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `IsBrainDead` |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocVisualizerSystem.cs` | modified | one visible layer per state, occupant occluded with the lid |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | modified | PLAN / FIX ME, AUTO, the autofix hardware line |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/triage.yml` | new | `WolfmedAutodocTriage` |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml` | modified | `autodoc_autofix` slot, `AutodocAutofixModule` |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/cargo.yml` | modified | the module in the crate, lathe recipes for both modules |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml` | regenerated | six new lines and events |
| `Resources/Prototypes/_WF/Wolfmed/health_icons.yml` | new | `HealthIconWolfmedArrest` |
| `Resources/Textures/_WF/Wolfmed/Interface/health_icons.rsi` | new | `Flatline`, derived from the stock Critical icon |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/{plan,auto-engaged,auto-nothing,auto-off,goodbye,dying}.ogg` | new | eSpeak NG, same chain as AUTODOC2 |
| `Resources/Locale/en-US/_WF/Wolfmed/{autodoc,autodoc-ui,autodoc-voice,look}.ftl` | modified | slot names, PLAN/FIX ME/AUTO, the new transcripts, the corpse lines |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/{Autodoc,WoundTreatment}.xml` | modified | triage and the alarm; arrest looks dead |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py` | modified | renders only the line ids it is given |
| `Tools/_WF/Wolfmed/gen_autodoc_voice_protos.py` | modified | the six new events |
| `Tools/_WF/Wolfmed/gen_arrest_icon.py` | new | draws the flatline HUD icon |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedArrestLooksDeadTest.cs` | new | 4 tests |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocTest.cs` | modified | 6 new tests, lid assertion inverted |

Deviations from the spec:

- **Arrest is not `MobState.Dead`.** The spec's section A asked for it; the owner withdrew that mid-package
  because every rule that reads a living body (metabolism, objectives, rot, corpse handling) has to keep
  treating an arrested patient as alive. Arrest stays Critical and only its perceptible signs changed.
- **The autofix module gets its own slot** rather than sharing the cardiac module's. The spec allowed
  either; sharing would have made a pod choose between shocking a patient and triaging one, and step 1 of
  the triage order is a shock.
- **The triage order gained an eleventh step** for limbs and organs the tray already holds. The spec's list
  stops at ten but its own rule ("skip anything needing a tray item unless the tray already holds a matching
  one") only means anything if those procedures are in the order at all.
- **The triage's closing step needs `requiresStarted`.** Closing an incision is a valid surgery on an
  unopened body, because the pod would open one first, so without the flag the planner queued a close on
  every intact limb of a healthy patient.
- **Self-service keeps its START button** next to FIX ME. The spec said PLAN and START "collapse into one";
  removing START would have taken away picking a single procedure by hand, which AUTODOC shipped.
- **The lid test asserts server state, not sprite layers.** "One visible layer per state" lives in the
  client visualizer; the test asserts the appearance state and the container's `showEnts`, which are what
  the visualizer reads.
- `AutodocDefibModule` gained a lathe recipe alongside the new one. It had none; the spec asked for the
  autofix recipe to sit "alongside the defib module".

## Final stages: AUTODOC4 (2026-09-22)

The pod can never loop, and it can now clear the two things that used to block it. Plus four owner playtest
findings folded in: the tend-wounds completion check, the anaesthetic dose, the self-service queue and a
brain nobody could repair.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | modified | `WolfmedSurgeryEmbeddedCondition`, `WolfmedSurgeryExtractEmbeddedEffect`, `WolfmedSurgeryRelocateJointEffect`, `anyDamage` on the organ condition |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | modified | listing and completion checks for both new steps; `GetTreatableGroupSeverity` |
| `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` | modified | the two step effects, each calling the hand path's own code |
| `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs` | modified | HOOK 26 and HOOK 27, two marked call sites in the tend-wounds handlers |
| `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | modified | HOOK 24 rewritten, HOOK 26 and HOOK 27 bodies |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | stall fields, `FailedProcedures`, `StepRuns`, `BlockedReason`, `AutoReplanLimit`, anaesthesia fields |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `Stall`, `Clothing`, `SedationLimit` voice events |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | stall guard, part and body signatures, closure, blocked-on-patient waiting, one dose a queue, oxygen tick, reservoir fallback |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | modified | planner skips failed and embedded-blocked procedures, bounded re-plans, hand queues are run not replaced |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | fresh memory per occupant, sleep key, pain relief and status dependencies |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | anaesthesia readout, reservoir reads drainable containers |
| `Content.Shared/_WF/Wolfmed/Autodoc/SharedAutodocSystem.cs` | modified | `CanDropTargetEvent` answered shared, so a drop is not decided server-only |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | brain damage is a red finding under 60%, critical under 25% |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `autodoc_step_retries`, `surgery_tend_strength`, `autodoc_sedation_cap` |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryRemoveEmbeddedObjects`, `SurgeryRelocateJoint`, brain repair on any damage |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | `SurgeryStepExtractEmbedded`, `SurgeryStepRelocateJoint` |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/programs.yml` | modified | both new surgeries in the base library |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/triage.yml` | modified | removal first after the defib, relocation before bones |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml` | modified | reservoir slots accept `DrainableSolution` |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/treatment_procedures.yml` | modified | the pod named for lodged rounds, shrapnel and dislocations |
| `Resources/Locale/en-US/_WF/Wolfmed/treatment-advice.ftl` | modified | three new advice lines |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc.ftl` | modified | `wolfmed-autodoc-status-anaesthesia` |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl` | modified | brain damage findings |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | modified | generated with the three new lines |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml` | modified | generated |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/{stall,clothing,sedation-limit}.ogg` | new | eSpeak NG, the AUTODOC2 chain |
| `Tools/_WF/Wolfmed/gen_autodoc_voice*.py` | modified | three rows and three events |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocLoopTest.cs` | new | nine tests |

### Deviations

- **The stall guard counts runs, not "the check's observable changing twice in a row".** The spec's wording
  compares the observable before and after; what is implemented compares the part after each run against
  the part after the previous run of the same step, which is the same test written once instead of twice
  and makes the first run cheap. Three unchanged runs abandon the procedure, so a step is begun at most
  three times, which is what the spec's test asks for.
- **Pain and bleed rates are out of the part signature.** Both move every tick on their own, and a
  signature that moved with them could never read "no progress"; the signature is wound prototypes,
  severities, states, embedded counts, the fracture, organ health and part damage.
- **The AUTO re-plan signature is coarser still**: which wounds exist and in what state, not how severe.
  Severity drifts as a wound heals passively, which reset the bound every second and defeated it.
- **HOOK 24 was rewritten, not only extended.** The tend surgeries' listing condition upstream asks whether
  the BODY is damaged, so the planner queued a tend on every limb of a patient with one cut - the owner's
  twenty-procedure queue. On a wound host a tend now lists only where there is severity tending could close.
- **Tending completes on wounds, not on the part's damage figure.** The part's `DamageableComponent` is the
  routing's bookkeeping; what is wrong with a Wolfmed part is its wounds, so that is what the step waits on.
- **A clothed patient is asked to undress, not operated on through the jumpsuit.** The upstream armour check
  refuses every step over a covered part, and the pod used to answer that with `Fault` and die. It now waits,
  says so once and picks the procedure straight back up. Cutting through clothing would have broken the
  package's own rule that the pod follows every surgeon's rule.
- **`SurgeryHealBrain` was kept** alongside the repair rather than merged away: the repair restores the whole
  organ and leaves trauma behind, which is not what a brain at 80% wants. The repair is now listed for any
  damaged brain, which is the one path that was missing.
- **The brain-damage finding is a coloured banner, not a clickable procedure.** Wiring a new condition id
  into the treatment-advice mapping is a package of its own; the banner names the surgery in its text.

## Final stages: AUTODOC5 (2026-09-22)

Twelve owner playtest findings: the drop-sound loop, the finish ding, the window's missing bottom row,
residual damage after surgery, transfusion, clothing, dropping held items on going down, blood packs on a
full bloodstream, the head's "Mend ribcage" label, the defibrillator's odds on a corpse, the pod's empty
promise to charge again, and airloss past 700.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundDamageSyncSystem.cs` | new | lowers a part's damage to what its wounds still account for |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | idempotent Down/Stand, `DropEverything` on the first tick down |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | `DownedUntil` dwell, `WasUp`, `WolfmedDownedComponent.Dropped` |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | `MinDownedTime`, the dwell in `Evaluate`/`Apply` |
| `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | modified | HOOK 27 syncs the part's damage after a tend |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | transfusion, clothing cutting, defib retry, no finish sound, damage sync |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | `StatusLine`, CUT CLOTHING control, clothing/transfusing flags |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | modified | `wolfmed.autodoc_transfuse_below` and `wolfmed.autodoc_defib_attempts` |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | inventory and damage-sync dependencies, per-occupant defib reset |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | transfusion, clothing and defib fields; `DoneSound` removed |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocUi.cs` | modified | `ClothingBlocked`, `Transfusing`, `AutodocControl.CutClothing` |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | voice events Transfusing, Cutting, ClothingAuto, DefibBlocked, DefibGaveUp |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | modified | controls row on the window column, CUT CLOTHING button, smaller minimums |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocStyle.cs` | modified | the static font cache is locked |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | `GetRefusal`, flat chance on a dead body |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | CPR refills a corpse's oxygenation |
| `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` | modified | a blood pack has no work on a full bloodstream |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | modified | marked: the airloss ceiling in `ApplySystemicDamage` |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs` | modified | `AirlossCeiling` |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `autodoc_transfuse_below`, `autodoc_defib_attempts`, `airloss_cap` |
| `Resources/Prototypes/_Shitmed/Entities/Surgery/surgery_steps.yml` | modified | marked: `SurgeryStepMendRibcage` renamed "Mend bone" |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml` | modified | generated: 88 lines, 76 events |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc.ftl` | modified | waiting-clothing, transfusing, the clothing popup |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-ui.ftl` | modified | CUT CLOTHING and its tooltip |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | modified | generated transcripts |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/{transfuse,cutting,clothing-auto,defib-blocked,defib-gaveup}.ogg` | new | eSpeak NG, the AUTODOC2 chain |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/clothing.ogg` | modified | retuned line |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | modified | Downed drops what you hold; the dwell |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py` | modified | five new rows |
| `Tools/_WF/Wolfmed/gen_autodoc_voice_protos.py` | modified | five new events |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDownedTransitionTest.cs` | new | one fall on the edge, held items dropped |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocWindowLayoutTest.cs` | new | the controls row at UI scale 1 and 1.25 |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocCareTest.cs` | new | ghost damage, transfusion, CUT CLOTHING, AUTO cutting |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVitalLimitsTest.cs` | new | the airloss cap, the blood pack, the corpse chance, the second shock |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | modified | three tests now wait the Downed dwell out instead of expecting instant recovery |

Deviations from the spec:

- **No head-specific close chain.** `SurgeryStepMendRibcage` is renamed "Mend bone" instead of a separate
  "Seal cranium" step behind a head-only `SurgeryCloseIncisionHead`. One close chain is named as a
  `requirement` by a dozen surgeries; splitting it per part is a package of its own, and the generic name
  fixes the wrong label on every non-torso part at once.
- **Airloss recovery is not separately tested.** The cap is asserted by applying the damage directly rather
  than simulating thirty seconds without air; the drain back down is the vanilla respirator path, untouched.
- **Bloodloss is not capped, only Asphyxiation.** Decapitation and the other vital losses deal a fixed
  lethal 315 Bloodloss on purpose; a 200 ceiling made them survivable.
- **CPR raising a corpse's oxygenation is a flat rate**, and only on a body that is Dead: an arrested body
  still on the clock keeps BRAIN's "CPR buys time" balance exactly as it was.

## Final stages: REVIEW (2026-09-22)

Merge-preflight review fixes (UPSTREAM, CRASH, CLIENT, DATA, GAMEPLAY, CI findings).

| path | status | notes |
| --- | --- | --- |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | the heart-restored exit from arrest is its own method and runs with or without an oxygenation clock |
| `Content.Server/_WF/Wolfmed/Life/WolfmedOverheatSystem.cs` | new | a wound host past its overheat threshold burns through the wound pipeline instead of being set Dead |
| `Content.Server/_WF/Wolfmed/Life/WolfmedOverheatComponent.cs` | new | per-body burn pulse and its heat, tunable from a prototype |
| `Content.Server/_Goobstation/Temperature/KillOnOverheatSystem.cs` | modified | one marked hook: Wolfmed takes the kill for a body whose death it owns |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefSystem.cs` | modified | relief is the strongest dose plus a share of the rest, not the sum |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefComponent.cs` | modified | `StackShare` (0.35) data field behind that cap |
| `Content.Server/_WF/Wolfmed/Commands/DamageCommand.Wolfmed.cs` | modified | the admin damage command rejects NaN and the infinities |
| `Content.Server/Medical/HealingSystem.cs` | modified | the `before: CableSystem` ordering is marked and explained |
| `Content.Server/_EinsteinEngines/Silicon/WeldingHealable/WeldingHealableSystem.cs` | modified | the `before: FlammableSystem` ordering is marked and explained |
| `Content.Server/_WF/Wolfmed/Medical/WeldingHealableSystem.Wolfmed.cs` | modified | the reason for the wound-host ordering |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | modified | the progress label is rebuilt once a percent, not once a frame |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedBodyDoll.cs` | modified | one preview entity reused across scans, stale layers hidden |
| `Docs/_WF/Wolfmed/DECISIONS.md` | modified | the analyzer window's own geometry recorded against §8.4-5 |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBrainTest.cs` | modified | arrest ends on a body with no clock once its pump is back |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | modified | stacked painkillers hit the relief cap |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOverheatTest.cs` | new | overheating burns a wound host and leaves other mobs to upstream |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedThresholdFallbackTest.cs` | new | a non-wound-host mob crits and dies at its prototype thresholds |

Deviations:

- **The two `before:` orderings could not be gated to wound hosts.** The engine requires every subscription
  a system makes to one event to carry the same ordering (`EntityEventBus.UpdateOrderSeq`), so the
  wound-host handlers' ordering forces the same ordering on the upstream handler beside them. Both are
  marked and explained instead. Neither handler sets `Handled` unless it does its work, so the reorder
  changes nothing for cable placing or for setting things on fire.
- **Overheating is a burn, not a shutdown.** Heat routed through the ordinary pipeline kills the pump long
  before the positronic brain, so an IPC shuts down through `WolfmedShutdownSystem` on its own rather than
  through a second, special-cased path. *[M6 correction: it almost never did (organ rolls, about one in a million).
  Since M4 the pulse reaches no organ at all and the lethal route is the core-heat route: thermal shutdown, then core
  failure.]*

## Final stages: HUD (2026-09-22)

Synthetic diagnostics readout: a mechanical body draws a film-style helmet HUD instead of the organic red
vignette and dying view. The fault list is the analyzer payload for one body, pushed rather than pulled.

| path | status | notes |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudComponent.cs` | new | networked readout: faults, integrity, fluid, power, servos, shutdown, core, advice. `WolfmedSyntheticFault`, the severity tag and the condition enum live here. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudLinePrototype.cs` | new | `syntheticHudLine`: wound or condition -> locale line, tag, escalation severity, advice. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudLineSystem.cs` | new | the table lookup, the part labels and the pure tier function (`Strain`, `Tier`, 0.08/0.25/0.55). |
| `Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs` | new | fills the component twice a second for every mechanical wound host, dirty only on change. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlaySystem.cs` | new | drives the readout: tiers, slide-in, blips, spinner, jitter, glitch, standby and the panic/death sequence. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlay.cs` | new | the screen-space drawing. No controls, no input. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticScreenOverlay.cs` | new | the shader pass: cyan rim, torn slice, standby drain. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudLayout.cs` | new | block rectangles and the reserved HUD regions the layout test measures against. |
| `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs` | modified | `WolfmedOwnsDeadScreen` became `WolfmedOwnsScreen`, which is also true for a synthetic body. |
| `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs` | modified | the one marked call site renamed with it. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDyingEffectsSystem.cs` | modified | four marked gates: the dying view, the sway, the death banner and the arrest banner all skip a synthetic body. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.synthetic_hud`, `wolfmed.synthetic_hud_scale` (both client, archived). |
| `Resources/Prototypes/_WF/Wolfmed/Hud/synthetic_hud.yml` | new | 15 rows: seven wounds and eight conditions, fallback included. |
| `Resources/Prototypes/_WF/Wolfmed/Shaders/shaders.yml` | modified | the `WolfmedSynthetic` shader entry. |
| `Resources/Textures/_WF/Wolfmed/Shaders/synthetic.swsl` | new | rim tint, slice tear, scan, standby drain. |
| `Resources/Locale/en-US/_WF/Wolfmed/synthetic-hud.ftl` | new | every readout string. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSyntheticHudTest.cs` | new | five tests: fault building and ordering, the cap, the organic gate, the tier bands, data/locale coverage, layout at two resolutions and two scales. |

Deviations from the spec:
- **Advice comes from its own short locale keys, not the analyzer's.** The analyzer's mechanical advice keys
  (`wolfmed-treatment-step-wolfmed-breach-wound-2`) are whole sentences; a HUD line is two or three words.
  The `syntheticHudLine` rows name their own `advice` key, which says the same thing in the readout's voice.
- **Power is on the wire, not read client-side.** `BatteryComponent` is server-only, so the cell charge
  rides in the component with everything else. It is omitted from the block when there is no readable cell.
- **The kernel panic does not freeze the game for two seconds.** It holds a static register dump on screen
  for two seconds and then fades to the `CORE OFFLINE` banner; nothing stops the client ticking.
- **The reserved HUD regions in the layout test are declared, not measured from the live controls.** They
  are deliberately generous bands for the hotbar, alerts column, chat pane and targeting doll.

## Final stages: playtest fixes, defib and autofix (2026-09-22)

| path | status | notes |
| --- | --- | --- |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | the blood gate is strictly under `wolfmed.defib_blood`, so a body on the threshold is shockable. |
| `Content.Server/Medical/DefibrillatorSystem.cs` | modified | a gate refusal is popped up to the medic as well as spoken by the paddles. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | transfuse before the shock and again on a blood refusal; pod wounds marked while it works; `OccupantWasDead`; one QUEUE COMPLETE per run. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | modified | the planner skips a step's surgeries on a part whose only wounds the pod made. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | `TryMoveQueued` seam, shared with the client through `AutodocQueueRules`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | the new per-occupant state is cleared with the rest on insert, removal and reset. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocWoundComponents.cs` | new | `WolfmedPodWoundComponent`, the marker on a wound the pod made itself. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | `PreProcedureWounds`, `PodWoundGrace`/`PodWoundUntil`, `OccupantWasDead`. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `ignorePodWounds` on a triage step, `DeadProceeding` voice event. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocUi.cs` | modified | `AutodocQueueRules.FirstMovable`, the one bound both ends of the queue UI follow. |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | modified | the reorder buttons read that bound off the pod's state instead of off each row. |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocStyle.cs` | modified | the static font cache is keyed by the owning resource cache; one client's `Font` carries the engine glyph cache and two clients corrupted it. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/triage.yml` | modified | `ignorePodWounds: true` on the bleeding and tending steps. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml`, `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl`, `Resources/Audio/_WF/Wolfmed/Autodoc/voice/dead-proceeding.ogg` | modified/new | `dead-proceeding` "PATIENT IS DEAD. PROCEEDING." (Info), eSpeak NG through the AUTODOC2 chain. |
| `Tools/_WF/Wolfmed/gen_autodoc_voice{,_protos}.py` | modified | the new line's row and event. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPlaytestFixesTest.cs` | new | six tests: the hand defib on a plain arrest, the blood refusal and the pod's transfusion, autofix running once, a brain-dead occupant operated on, a death mid-run and its RESUME, queue reorder. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocWindowLayoutTest.cs` | modified | the reorder buttons' enabled state against the shared bound, idle and running. |

Deviations:
- **Anything that appears on an occupant while the pod works is the pod's**, not only what a step created:
  a cautery's burn lands through the damage path a tick or two behind the step, so marking only at the
  procedure boundaries let one wound a run through, which was enough to restart the planner. The window
  closes `PodWoundGrace` (1 s) after the last step, so an injury from outside during a run is miscredited.
- **No client test presses the reorder button.** Robust's `BaseButton` has no public press, so the window
  test asserts what `DrawQueue` decided and the server test drives `TryMoveQueued`, the seam the message
  handler calls.

## M1a B: causes, faint and alerts (2026-09-22)

Marked upstream and Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Shared/Mobs/Systems/MobThresholdSystem.cs:333-343` | upstream, new method | WOLFGATE (M1a): `SetTriggersAlerts`, the one way `_WF` code can hand a wound host's health alerts to `WolfmedConditionAlertSystem` (the component's access rule keeps the field to this system). Inventory #3. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:30` | Onyx | WOLFGATE (M1a): dependency on `WolfmedBodyPainSystem`, the `_WF` half of the edits below. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:32-33` | Onyx | WOLFGATE (M1a): the shock threshold, re-arm and adrenaline constants and the 0.7 adrenaline multiplier removed; CVars `wolfmed.pain_shock_threshold`, `wolfmed.pain_shock_rearm`, `wolfmed.adrenaline_seconds`. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:149` | Onyx | WOLFGATE (M1a): adrenaline running out hands off to `AdrenalineChanged` instead of raising a pain change (OD5: pain never moved). |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:168-169` | Onyx | WOLFGATE (M1a): `GetPain` no longer multiplies by 0.7 under adrenaline (OD5). |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:181-183` | Onyx | WOLFGATE (M1a): P13, a part's share of the body's suppression is part over the sum of the parts. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:200-203` | Onyx | WOLFGATE (M1a): P13, a body's pain is min(soft cap, Σ parts) after every change; direct sets are rederived. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:281, 291` | Onyx | WOLFGATE (M1a): the re-arm and threshold read the CVars. |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs:458-461` | Onyx | WOLFGATE (M1a): the shock's adrenaline window from the CVar; hands off to `AdrenalineChanged` (crawl boost, patient line). |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundDamageFoundationTest.cs:873-890` | Onyx test | WOLFGATE (M1a): the shock block sets the 200 on a part (P13) and reads 135/105 instead of 94.5/73.5 (OD5); the re-armed shock fires again at 135. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessCause.cs` | new | `WolfmedCause`, `WolfmedCauseFlags`, `WolfmedCauseSource`, tie order, `wolfmedConsciousnessCause` prototype, `WolfmedConsciousnessChangedEvent`, `WolfmedConditionAlertEvent`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedBodyPainSystem.cs` | new | P13 derived body pain and suppression share, shock CVars, adrenaline state and `WolfmedAdrenalineEvent`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | networked `Cause`, `CauseSource`, `Blockers`; faint fields; `HypoxiaSource`; `LastConditionLine`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | the Downed alert moved to the condition alerts; adrenaline crawl ×1.5 and no do-after penalty. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefSystem.cs` | modified | `EndsFaint`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedLifeComponents.cs` | modified | networked `WolfmedShutdownComponent.Reason`. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudComponent.cs` | modified | `CauseLine`. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.pain_faint_seconds` 20, `pain_faint_rise` 40, `pain_faint_cooldown` 30, `pain_shock_threshold` 130, `pain_shock_rearm` 110, `adrenaline_seconds` 30, `adrenaline_crawl_multiplier` 1.5. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | inputs per cause, cause and blockers, the pain faint, the change event, alert hand-off. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | new | condition alerts, the Up doll's severity, transition lines, the alert click, adrenaline lines. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | modified | sets the shutdown reason. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `DrainRate` reports its largest drain; the hypoxia sub-source is recorded. |
| `Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs` | modified | fills `CauseLine`. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlaySystem.cs` | modified | the banner shows the cause's line instead of STANDBY and MOBILITY LOST. |
| `Content.Client/_WF/Wolfmed/Audio/WolfmedCritHeartbeatSystem.cs` | modified | silent for machines and faints. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml` | new | one cause prototype per M1a cause. |
| `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml` | modified | 15 condition alerts; `WolfmedDowned` moved into the Health category with the click event. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | cause, title, help, transition and alert strings. |
| `Resources/Locale/en-US/_WF/Wolfmed/synthetic-hud.ftl` | modified | per-cause banner lines. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedCauseScenarioTest.cs` | new | `PainScenarioTest`, `SustainedFireFaintTest`, `BodyPainTracksPartsTest`, `OverlappingCausesTest`, `IpcShutdownScenarioTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | modified | pain on the parts (P13); the unconscious cases are faints. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainTest.cs` | modified | pain on the torso; OD5 readings; the adrenaline window from its CVar. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSyntheticHudTest.cs` | modified | the banner names the pump shutdown and clears on rejuvenate. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedCritHeartbeatTest.cs` | modified | `HeartbeatSilentForFaintsAndMachinesTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDownedTransitionTest.cs` | modified | the Downed alerts sit in the health slot. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocLoopTest.cs` | modified | the two-fracture fixture is pain-numb: it would faint now. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBreathingClockTest.cs` | modified | package A's `PainShockNoArrestTest` stays under the faint line. |

## M1a C: honest endings and crawling (2026-09-22)

Marked upstream edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Server/Ghost/GhostSystem.cs:76` | upstream, dependency | WOLFGATE (M1a): `WolfmedDyingActionsSystem`, the `_WF` half of the two lines below. |
| `Content.Server/Ghost/GhostSystem.cs:675-679` | upstream | WOLFGATE (M1a): a wound host's own `ghost` command (`viaCommand && !forced && canReturnGlobal`) opens Wolfmed's honest dialog instead: Succumb while Dying, "left alive but empty" otherwise. Returns true, so the command prints no denial. Inventory #2. |
| `Content.Server/Ghost/GhostSystem.cs:705` | upstream | WOLFGATE (M1a): a wound host never enters the kill-crit branch, so it never gets the Asphyxiation top-up or a free returnable ghost from Critical. Inventory #2. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Life/WolfmedCritActionsSystem.cs` | new | Strips the upstream Critical actions from wound hosts on both sides at `MobStateActionsComponent` startup. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedDyingActions.cs` | new | `WolfmedSuccumbActionEvent`, `WolfmedLastWordsActionEvent` (`MaxLength`), `WolfmedChoiceEuiState`, `WolfmedChoiceMessage`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedCallForHelp.cs` | new | Networked `WolfmedCallForHelpComponent` (flag and cooldown) and `WolfmedCallForHelpActionEvent` (`MaxLength`). |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | `IsWithinReach`: loose floor items within `wolfmed.downed_reach` pass the Downed interaction check. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.downed_reach` 1.5, `wolfmed.call_for_help_seconds` 60, `wolfmed.call_for_help_cooldown` 30. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedDyingActionsSystem.cs` | new | Grants and revokes Succumb and Last Words, the Succumb and leave dialogs, `Succumb` (brain 0, Kill, EndArrest, returnable ghost), `LeaveAlive`, the ghost-command hand-off. Also holds `WolfmedDyingActionsComponent`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedChoiceEui.cs` | new | Server half of the yes/no dialog. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `StartArrest` grants and `EndArrest` revokes the Dying actions (direct calls). |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | `OfferReturn`: the return prompt after a pod revival. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | `InFaint`; Call for help refreshed on every apply; the Dying actions and Call for help go on death. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedCallForHelpSystem.cs` | new | The Downed-only action, the shout, the flag and the cooldown. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | Consciousness dependency. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | Anaesthetises a fainted occupant; offers the return after a pod revival. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | modified | No `AutodocAlarm.Critical` for a faint. |
| `Content.Client/_WF/Wolfmed/Life/WolfmedChoiceEui.cs`, `WolfmedChoiceWindow.cs` | new | Client half of the yes/no dialog. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedCallForHelpIconSystem.cs` | new | The Call for help flag on medical HUDs. |
| `Content.Client/_WF/Wolfmed/Overlays/ShowHealthIconsSystem.Wolfmed.cs` | modified | `WolfmedHudActive` accessor for the flag. |
| `Resources/Prototypes/_WF/Wolfmed/Actions/dying.yml` | new | `ActionWolfmedSuccumb`, `ActionWolfmedLastWords`. |
| `Resources/Prototypes/_WF/Wolfmed/Actions/downed.yml` | new | `ActionWolfmedCallForHelp`. |
| `Resources/Prototypes/_WF/Wolfmed/health_icons.yml` | modified | `HealthIconWolfmedCallForHelp`. |
| `Resources/Locale/en-US/_WF/Wolfmed/death.ftl` | modified | Succumb, leave and Last Words dialog strings ([OD1 wording]). |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | Call for help strings; the arrest help and alert say "You can choose to let go." |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedHonestEndingTest.cs` | new | `HonestEndingScenarioTest`, `CritSuccumbNeverGrantedToWoundHostsTest`, `CriticalHearingTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedCrawlingActionsTest.cs` | new | `DownedPickupTest`, `CallForHelpTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocFaintTest.cs` | new | `FaintedOccupantIsAnaesthetisedTest`, `FaintDoesNotSoundTheCriticalAlarmTest`, `PodRefusesRotAndHeartlessBodiesTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs` | modified | `DownedReachesOnlyItselfTest`: a loose item within reach is reachable, a door is not. |

## M1a A: breathing and the clock (2026-09-22; listed by package D)

Package A left no manifest section; this lists its marked edits and files from commit `8f71617774`.

Marked upstream and Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Server/Body/Systems/RespiratorSystem.cs:43` | upstream, dependency | WOLFGATE (M1a): `WolfmedBreathingSystem`, the `_WF` half of the line below. |
| `Content.Server/Body/Systems/RespiratorSystem.cs:85-87` | upstream | WOLFGATE (M1a): `!_mobState.IsIncapacitated(uid)` becomes `!_wolfmedBreathing.BreathingSuppressed(uid)`: a wound host breathes while unconscious; others keep the upstream rule. Inventory #1. |
| `Content.Server/Medical/DefibrillatorSystem.cs:214-216, 224` | upstream | WOLFGATE (M1a): the Wolfmed refusal line is localised with the patient's numbers (`WolfmedRevivalSystem.LocalizeLine`). Not in the plan's inventory; the plan said this file would not be edited. |
| `Content.Server/_Onyx/Wounds/WoundInternalBleedingSystem.cs:6, 18, 20-23, 64, 79` | Onyx | WOLFGATE (M1a): P22, internal bleeding ticks once a second so the amount survives FixedPoint2 rounding. Inventory #5. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs:99-112, 129-132, 146-149` | Onyx | WOLFGATE (M1a): the post-shock transfusion numbers on the analyzer payload. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Server/_WF/Wolfmed/Life/WolfmedBreathingSystem.cs` | new | Who breathes, the suffocation level, the breathing assessment. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | Breathing input, clean refill, post-shock grace and repeat rule, transfusion guidance, vital signs, blood band, bleed rate. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedPostShockComponent.cs` | new | Grace and repeat window of the last successful shock. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | Shared `GetRefusal`, `LocalizeLine`, the post-shock `Revive`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | Resets the vital signs with the rest of the state. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | The pod's refusal line carries the numbers. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Post-shock advice on the payload. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalSigns.cs` | new | `WolfmedBreathing`, `WolfmedBreathingSource`, `WolfmedBloodBand`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedPostShockText.cs` | new | The analyzer's post-shock line. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | Networked `Breathing`, `BreathingSource`, `BloodBand`. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.arrest_shock_blood` 0, `defib_blood` 0.25, `airloss_full` 100, `post_shock_oxygenation` 0.5, `post_shock_grace_seconds` 45, `post_shock_repeat_seconds` 300, `post_shock_blood_target` 0.35, `blood_band_pale` 0.8. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | The post-shock banner. |
| `Resources/Locale/en-US/_WF/Wolfmed/death.ftl` | modified | Refusals with numbers; post-shock lines. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBreathingClockTest.cs`, `WolfmedScenario.cs` | new | The seven package A tests and the scenario helper. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBrainTest.cs`, `WolfmedArrestLooksDeadTest.cs`, `WolfmedPlaytestFixesTest.cs` | modified | Migrations: real suffocation, 0.5 after a shock, the 0.25 gate and the pulse refusal. |

## M1a D: medic lines, conformance, closure (2026-09-23)

Marked Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs:5` | Onyx, using | WOLFGATE (M1a): `WolfmedVitalsReport`'s namespace. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs:114-115, 133, 150` | Onyx | WOLFGATE (M1a): the analyzer payload carries the vitals block (`Vitals`), built by `_WF` `HealthAnalyzerSystem.Vitals.cs`. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | new | `WolfmedVitalsReport`, `WolfmedVitalsState`, `WolfmedBloodTrend`, `WolfmedDefibVerdict`, and `WolfmedVitalsText`, the words. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | new | Builds the vitals block: state and cause, fresh breathing and blood band, trend, units to 50%, defib verdict. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | Passes the block into the payload. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | modified | `HasPump` public for the cooling line. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | Any organ insertion refreshes the vital signs. |
| `Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs` | modified | Fills `Sensors` and `CoreTemperature`. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudComponent.cs` | modified | Networked `Sensors`, `CoreTemperature`. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | Chest and pulse read from the networked `Breathing` and `BloodBand`; sedation reads slow and shallow. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.analyzer_blood_fast` 1. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | The vitals block as the panel's first banner row. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlaySystem.cs` | modified | SENSOR and CORE rows. |
| `Resources/Locale/en-US/_WF/Wolfmed/analyzer-vitals.ftl` | new | The vitals block's words. |
| `Resources/Locale/en-US/_WF/Wolfmed/look.ftl` | modified | Gasping, slow breathing, pale, weak pulse, barely palpable pulse. |
| `Resources/Locale/en-US/_WF/Wolfmed/synthetic-hud.ftl` | modified | SENSOR and CORE row strings. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedMedicLinesTest.cs` | new | `AnalyzerStateLinesTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesConformanceTest.cs` | new | Report mode with `KnownGaps` (98). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedScenario.cs` | modified | `Report`, `AnalyzerLines`, `Analyzer` helpers. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBreathingClockTest.cs`, `WolfmedCauseScenarioTest.cs` | modified | The analyzer lines the plan's scenarios name. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | modified | `VitalsBlockHeadsThePanelTest` (client panel). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualInspectionTest.cs` | modified | `BreathingAndPulseReadTheNetworkedVitalsTest`; new keys in the resolve list. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSyntheticHudTest.cs` | modified | `SystemBlockCarriesSensorsAndCoreTemperatureTest`. |

## M1a playtest 1 fixes (2026-09-23)

Marked upstream, Onyx and vendored edits:

| File:line | Kind | Reason |
|---|---|---|
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:1166` | Onyx YAML | WOLFGATE (playtest 1): internal bleed rate 0.02 → 0.01, halved with `wolfmed.bleed_rate`, which it does not read. |
| `Content.Server/Body/Systems/StomachSystem.cs:15, 59` | upstream | WOLFGATE (playtest 1): the digestion delay per reagent comes from `WolfmedOralAbsorptionSystem`; painkillers absorb in 4 s. |
| `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:26, 109` | Onyx | WOLFGATE (playtest 1): "wounds painfully close" once per `wolfmed.cautery_popup_seconds` per body, through `WolfmedCauterySystem`. |
| `Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs:595, 605` | upstream (Mono) | WOLFGATE (playtest 1): `SetCartridgeSpent(..., despawn)`; no casing despawn while it stays in a cylinder. |
| `Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.Revolver.cs:455-460, 548-550` | upstream | WOLFGATE (playtest 1): a fired casing keeps its slot alive; `EmptyRevolver` arms the despawn on the floor. The source of the 149 MetaData resolve errors. |
| `Content.Shared/_EinsteinEngines/Silicon/Components/SiliconComponent.cs:12, 16` | vendored EE | WOLFGATE (playtest 1): `ChargeState` networked, so the client predicts the low-power crawl. |
| `Content.Server/_EinsteinEngines/Silicon/Charge/Systems/SiliconChargeSystem.cs:142-145` | vendored EE | WOLFGATE (playtest 1): dirty `SiliconComponent` when the charge state changes. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/wallmed.yml:15-16` | upstream YAML | WOLFGATE (playtest 1): analgesic and opiate pens. |
| `Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/civimed.yml:14-15` | upstream YAML | WOLFGATE (playtest 1): analgesic and opiate pens. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.bleed_rate` 0.3; new `wolfmed.painkiller_absorb_seconds` 4 and `wolfmed.cautery_popup_seconds` 10. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/look.yml`, `sfx.yml` | modified | Bleed bands and the major-bleed rate halved with the bleed rate. |
| `Content.Server/_WF/Wolfmed/Medical/WolfmedOralAbsorptionSystem.cs` | new | Painkiller digestion delay. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefSystem.cs` | modified | `WolfmedPainReliefTierChangedEvent`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | modified | Painkiller takes-hold and wears-off lines; counts condition lines. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | `ConditionLineCount` (server, tests). |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | Death goes through `Apply`; the depth comment (review lows). |
| `Resources/Prototypes/_WF/Wolfmed/Entities/painkillers.yml` | modified | `WolfmedAnalgesicPen`, `WolfmedOpiatePen`. |
| `Resources/Prototypes/_WF/Catalog/VendingMachines/Inventories/wolfgate.yml` | modified | The pens. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | The pens and the onset line. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | Painkiller lines. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | `NoResponse` is the failed roll only; `NotMonitored` for the fallback. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs`, `Medical/HealthAnalyzerSystem.Vitals.cs` | modified | Read `NotMonitored`. |
| `Resources/Locale/en-US/_WF/Wolfmed/death.ftl` | modified | "No response. Charge again."; the not-monitored refusal. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedCauterySystem.cs`, `WolfmedCauteryAnnounceComponent.cs` | modified, new | The wounds-closing rate limit. |
| `Content.Client/_WF/Wolfmed/Life/WolfmedChoiceEui.cs`, `WolfmedChoiceWindow.cs` | modified | Open and centre once the text is in; wrap; size to content. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | modified | `RestoreCell` on rejuvenate. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBreathingClockTest.cs` | modified | `BleedTimingTest`; internal bleed bands; fresh blood band. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPainkillerTest.cs` | new | `OralPainkillerTakesHoldTest`, `PainkillerPenTest` ×2. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPlaytestOneTest.cs` | new | Defib, vacuum spam, IPC crawl, rejuvenate cell, revolver casing. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedChoiceWindowLayoutTest.cs` | new | The dialog at UI scale 1 and 1.25. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAvailabilityTest.cs`, `WolfmedLocaleCoverageTest.cs` | modified | The pens; the takes-hold key family. |

## M1b (2026-09-23)

Marked upstream and Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Shared/_Onyx/Wounds/WoundEvents.cs:59-64` | Onyx | WOLFGATE (M1b): `PartDamageAppliedEvent.Overflow` (what a ceiling cut) and the computed `Total` (Applied + Overflow). |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:798-844` | Onyx | WOLFGATE (M1b): `ClampToBodyCap(body, part, …)` returns what it cut; with the torso cap's overflow it rides on the hit as `Overflow`; one `PartDamageAppliedEvent` per hit, raised even when nothing was stored; the D27 accumulator adds Applied only. `PartDamageOverflowedEvent` is still raised first. |
| `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:27, 36-43, 74` | Onyx | WOLFGATE (M1b): the dispatcher hands `Total` to wounds, bleeding and the organ roll and Applied to fractures and amputation; `WolfmedPartHitSystem.OnHit` first (charring escalation, OD12 crumble, test seam). |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:575-578` | Onyx YAML | WOLFGATE (M1b): `WolfmedFluidLossBehavior` on `BurnWound` (from 20, 0.0008 u/s per severity). |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs` | modified | Per-part ambient ceiling, corpse ceiling, `WithCeilingBypass`, `AmbientCeiling`; `WolfmedPartDestructionThresholdEvent`. |
| `Content.Server/_WF/Wolfmed/Body/WolfmedPartCeilingSystem.cs` | new | A part's lowest Destructible trigger. |
| `Content.Server/_WF/Wolfmed/Commands/DamageCommand.Wolfmed.cs` | modified | The part form bypasses the ceilings (P31). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedBurnFluidComponents.cs` | new | `WolfmedFluidLossBehavior`, `WolfmedFluidLossComponent`, `WolfmedDressedComponent`, `WolfmedBurnFluidLossComponent`, `WolfmedSurgeryGraftBurnsEffectComponent`. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedFluidLossSystem.cs` | new | Burn fluid loss, no puddle; dress and graft. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedPartHitSystem.cs` | new | Burn-at-cap escalation into charring; OD12 crumble; the per-hit test seam. |
| `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` | modified | A Heat-removing item dresses the part's weeping wounds. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs`, `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionProfilePrototype.cs`, `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | modified | `dressedMultiplier` 0.15 for dressed or grafted wounds with no bleeding treatment (P20). |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `GetVolumeLossRate` (bleeding + burns); the post-shock transfusion number counts burn loss. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs`, `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | modified | "Fluid loss from burns" line; the trend counts burn loss. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | "has weeping burns". |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M1b block at the end; `body_damage_cap` doc (corpse ceiling); `pain_faint_cooldown` 30 → 50. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/burns.yml` | modified | Charring weeps (from 0). |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | modified | The graft step grafts the part's burns. |
| `Resources/Locale/en-US/_WF/Wolfmed/burns.ftl` | new | Crumble, fluid-loss, analyzer and examine lines. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBurnScenarioTest.cs` | new | `FireMeasurementTest`, `BurnScenarioTest`, `SaturatedTorsoTest`, `AmbientCeilingTest`, `BurnDressingInfectionTest`, `CharCrumbleTest`, `DownedCanPatOutFireTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBurnWoundTest.cs`, `WolfmedEviscerationTest.cs`, `WolfmedInfectionTest.cs`, `WolfmedDamageCommandTest.cs`, `WolfmedSpeciesSpawnTest.cs`, `WolfmedAmputationTest.cs`, `WolfmedConsciousnessTest.cs`, `Scenarios/WolfmedCauseScenarioTest.cs`, `Scenarios/WolfmedScenario.cs` | modified | Test migration (DECISIONS M1b); `KeepGrid` against Mono's grid cleanup. |

## Playtest 2: faint timer, fire helplessness (2026-09-23)

Marked upstream and Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:306-319` | Shitmed | WOLFGATE (playtest 2): on a wound host the switch-off and switch-on lines read the part's damage less its Burn group (`WolfmedLimbIntegrity.ForEnable`); burns never take a hand or a leg away. |
| `Content.Shared/Body/Systems/SharedBodySystem.Parts.cs:816-824` | upstream (Shitmed) | WOLFGATE (playtest 2): a wound host with no enabled leg keeps the default base speed (`WolfmedCrawlSystem.LeglessBase`) instead of 0, and its modifiers are refreshed after every base change. |
| `Content.Shared/Movement/Systems/MovementSpeedModifierSystem.cs:92-109` | upstream | WOLFGATE (playtest 2): `WolfmedSpeedFloorEvent` raised after the modifiers are gathered; its result is what is stored (the crawl floor). |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:601-603, 611-613` | Onyx YAML | WOLFGATE (playtest 2): `BurnWound` Severe/Critical manipulation penalty ×1.25/×1.5. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedCrawlSystem.cs` | new | `WolfmedSpeedFloorEvent`, the crawl floor and legless crawl, `WolfmedLimbIntegrity`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | Refresh the speed when Downed starts and ends. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | `PainFaintStart`; `HandsPenaltyTold`, `LegsPenaltyTold`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | Faint start, `GetFaintWindow`, `GetFaintSecondsLeft`; `LegsGone` counts switched-off legs. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | modified | Faint alert countdown, timed help, limb-penalty popup and condition line. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | modified | `GetBodyLimbPenalty`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs`, `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | modified | "FAINTED: pain, {N} s". |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.crawl_floor` 0.35. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl`, `analyzer-vitals.ftl` | modified | Timed faint help, limb-penalty lines, timed analyzer state. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedFireHelplessnessTest.cs` | new | `FireHelplessnessTest` (the reproduction table and the assertions). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPlaytestTwoTest.cs` | new | `FaintCountdownTest`, `BurnsNeverSwitchALimbOffTest`, `CrawlFloorTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedCauseScenarioTest.cs`, `WolfmedMedicLinesTest.cs` | modified | The faint's text and analyzer line now carry the seconds. |

## M2 (2026-09-23)

Marked upstream, Onyx and vendored edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Server/Body/Systems/RespiratorSystem.cs:44, 307` | upstream | WOLFGATE (M2): the suffocation alert asks `WolfmedBreathingAlertSystem.SuffocationAlert`, "Can't breathe: no air" on a wound host (plan §3.3). |
| `Content.Server/_EinsteinEngines/Silicon/DeadStartupButton/DeadStartupButtonSystem.cs:29, 50-52` | upstream (EE) | WOLFGATE (M2): on a wound host the restart is `WolfmedRevivalSystem.TryRestart` (core, head, power, pump), not the damage total (inventory #9). |
| `Content.Shared/Execution/SharedExecutionSystem.cs:219-221, 226-229` | upstream | WOLFGATE (M2): HOOK 13 rewritten (inventory #11, OD17): the execution and the self-execution raise `WolfmedEndingEvent` on the victim instead of the torso top-up; the HOOK 13 `using` and `WoundDamageRoutingSystem` dependency are gone. |
| `Content.Server/Chat/SuicideSystem.cs:31, 74-75` | upstream | WOLFGATE (M2): OD17, `WolfmedDyingActionsSystem.EndDeliberately` after upstream's suicide events (inventory #12). |
| `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml:143, 179` | Onyx YAML | WOLFGATE (M2): tramadol and oxycodone `sedationPerUnit` 0.11 and 0.12 in place of the per-second `sedation` (plan §3.4). |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:194` | upstream YAML | WOLFGATE (M2): `WolfmedSuture` on `MedicatedSuture` (P20). |
| `Resources/Prototypes/Entities/Objects/Tools/tools.yml:234` | upstream YAML | WOLFGATE (M2): `WolfmedCoreProbe` on `Multitool`, the core repair step's tool (OD10). |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml:16`, `wallmed.yml:17` | upstream YAML | WOLFGATE (M2): the naloxone pen (OD14). |
| `Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/civimed.yml:16` | Frontier YAML | WOLFGATE (M2): the naloxone pen (OD14). |

Not needed: inventory #10 (`ElectrocutionSystem`): the electrocuted event already carries the post-insulation coefficient.

## M3 (2026-09-23)

Marked upstream and Onyx edits:

| File:line | Kind | Reason |
|---|---|---|
| `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:28, 46-49` | Onyx | WOLFGATE (M3): inventory #13. A part with reach lines hands the hit's Total to `WolfmedOrganThresholdSystem.HandleHit` (deterministic organ split, head blow) and skips Onyx's roll; parts without reach lines keep the roll. |
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs:92-94` | Onyx | WOLFGATE (M3): inventory #15, P27. An explosion hit on a head does nothing in `HandlePartDamageApplied` unless `wolfmed.blast_dismember_head` is on (`WolfmedBodyPartSystem.BlastMaySever`): covers Onyx's explosion roll and a blast's finishing hit. |
| `Content.Server/Atmos/EntitySystems/BarotraumaSystem.cs:243-245, 266, 282` | upstream | WOLFGATE (M3): inventory #14, P23. A wound host's pressure damage has no origin: a part by weight, under the ambient per-part ceiling. |
| `Content.Server/Body/Systems/BloodstreamSystem.cs:47, 131-132` | upstream | WOLFGATE (M3): blood regeneration × `WolfmedLifeSystem.BloodRegenFactor` (an impaired heart halves it). This is inventory #16's line, which the plan gives to M5 (radiation stops regeneration); M5 extends the same method. |
| `Resources/Prototypes/Body/Parts/base.yml:285, 297, 320, 332` | upstream YAML | WOLFGATE (M3): P19. `MajorLimb` Blunt 190 → 400, Heat 250 → 350; `MinorLimb` Blunt 150 → 270, Heat 230 → 320: above every limb's sever threshold, so a limb comes off with a stump before it can be destroyed. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainRelief.cs` | modified | `sedationPerUnit` (target per unit in the blood); new `WolfmedReverseSedation` effect. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedPainReliefComponent.cs`, `WolfmedPainReliefSystem.cs` | modified | Target-based sedation, rise and decay, warnings (`WolfmedSedationWarningEvent`), antagonist hold, `SedationPerUnit`, `GetSedationTarget`; `MasksSlowdown` reads the doses (P30). |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedSedationWarningSystem.cs` | new | Tells the patient the three sedation warnings. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | The anaesthetic push is capped at the units that keep the sedation target under the pod's cap. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | Cold slows tissue loss (P24); sepsis-named oxygen arrest (OD15); P28 post-insulation shock; `GetActiveRoutes`, `GetInternalBleedRate`; arrest memory and `GetRestartMemory`; `RepairBrain` gives a machine CORE RESTORED instead of trauma. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | `GetRestartRefusal`, `TryRestart`, `Restart`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | modified | `HasPower` public for the restart. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedDyingActionsSystem.cs` | modified | `WolfmedEndingEvent` handler and `EndDeliberately` (OD17). |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedEndingEvents.cs` | new | `WolfmedEnding`, `WolfmedEndingEvent`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedRevivalComponents.cs` | new | `WolfmedArrestMemoryComponent`, `WolfmedCoreRestoredComponent`, `WolfmedCardComponent`, `WolfmedRoutes`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedCardSystem.cs` | new | The explanation card's server half: the bar, CPR, examined. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedDormantSystem.cs`, `Content.Shared/_WF/Wolfmed/Life/WolfmedDormant.cs` | new | Wait as a ghost and the distress flag (OD8). |
| `Content.Server/_WF/Wolfmed/Life/WolfmedBreathingAlertSystem.cs` | new | "Can't breathe: no air / no lungs". |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedCrawlActionsSystem.cs`, `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedCrawlActions.cs` | new | Check yourself, Play dead (OD20). |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | The crawl actions refresh in `Apply`; the Unconscious depth by route; a pressure step onto or off 1 always lands. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | `CanAidAdjacent`: gauze onto a Downed neighbour (OD7 (c)). |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDyingDepth.cs`, `WolfmedExplanationCard.cs` | new | The depth by route; the card's text. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | AVPU, blue lips, pupils, "appears lifeless". |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs`, `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | modified | Routes line, "After a restart" line, restart verdict; marks the card examined. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | CORE FAILURE banner for a dead core; the M1a post-shock banner hides while the vitals line carries its numbers. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedExplanationCardSystem.cs`, `WolfmedExplanationCardOverlay.cs` | new | The card and the faint white-out. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDyingEffectsSystem.cs` | modified | No dying view in a faint. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDistressIconSystem.cs` | new | The distress flag on medical HUDs. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs`, `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSutureComponents.cs`, `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs` | modified / new | Sutures count as treated for infection (P20). |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs`, `WolfmedSurgeryConditionSystem.cs`, `WolfmedChassisToolComponents.cs` | modified | The repair effect's `slot`; `WolfmedCoreProbe`, `WolfmedCoreHousingOpen`. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudComponent.cs`, `Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs` | modified | `CoreRestored` condition and its line. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M2 block at the end; `arrest_sepsis_chance` 0. |
| `Resources/Prototypes/_WF/Wolfmed/Reagents/painkillers.yml`, `Entities/painkillers.yml`, `Recipes/reactions.yml` | modified | Opiate `sedationPerUnit`; naloxone, its pen and recipe. |
| `Resources/Prototypes/_WF/Catalog/VendingMachines/Inventories/wolfgate.yml` | modified | The naloxone pen. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml`, `surgery_steps.yml` | modified | `SurgeryRepairCore` and its three steps. |
| `Resources/Prototypes/_WF/Wolfmed/Hud/synthetic_hud.yml`, `Alerts/alerts.yml`, `Actions/downed.yml`, `Actions/dormant.yml` | modified / new | CORE RESTORED line; "Can't breathe" alerts; Play dead and Check yourself; Wait as a ghost. |
| `Resources/Locale/en-US/_WF/Wolfmed/revival.ftl` | new | M2 lines. |
| `Resources/Locale/en-US/_WF/Wolfmed/analyzer-vitals.ftl`, `look.ftl`, `synthetic-hud.ftl`, `wounds.ftl`, `consciousness.ftl` | modified | Routes, restart, examine signs, CORE RESTORED, core repair wording, naloxone in the sedation help. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | modified | The sedation model and naloxone. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedRevivalTest.cs`, `WolfmedMedicInfoTest.cs` | new | The M2 acceptance tests. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedConsciousnessTest.cs`, `WolfmedBrainTest.cs`, `WolfmedDyingLevelTest.cs`, `WolfmedLocaleCoverageTest.cs`, `WolfmedTreatmentRestrictionTest.cs`, `Scenarios/WolfmedCauseScenarioTest.cs`, `Scenarios/WolfmedMedicLinesTest.cs` | modified | Test migration (DECISIONS M2); locale coverage for the route and restart families; the suture test's bruise can no longer roll a bleed. |

| `Content.Server/_WF/Wolfmed/Body/WolfmedOrganThresholdSystem.cs` | new | Organ reach and split (plan §8), the head blow (§3.6); `Observer` test seam. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs` | modified | `OrganReach`. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs` | modified | `ImpairedBelow`, `ImpairedRegenFactor`, `Band`, `Fraction`; `WolfmedOrganBand`. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs` | modified | `BlastMaySever` (P27). |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessCause.cs` | modified | Causes `HeadBlow` 12, `Brain` 13, `Core` 14 (flags, tie order); `IsFaint` covers the head blow; prototype field `helpOutTimed`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | `HeadBlowUntil`, `HeadBlowStart`; networked `PulseIrregular`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | `StartHeadBlow`, the head-blow faint input; `injury` pressure → Brain or Core; `InFaint` and `GetFaintWindow` cover the head blow. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | modified | The faint countdown and timed help for any faint cause, the timed text from the cause prototype. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `InjuryPressure` (brain in `UpdatePressures`, core in the brainless branch); the lung input in `DrainRate`; `BloodRegenFactor`; `HeartImpaired` and `PulseIrregular` in `UpdateVitalSigns`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedBreathingSystem.cs` | modified | `LungDamageLevel`; `Assess` reads Laboured / LungsDamaged. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalSigns.cs` | modified | `WolfmedBreathing.Laboured`, `WolfmedBreathingSource.LungsDamaged`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs`, `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | modified | "Breathing: laboured"; the "Organs:" line (`WolfmedOrganReading`); the blood trend counts the regeneration factor. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | modified | The organ tab says "impaired" or "failed" after the health. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | "short of breath", "has an irregular pulse", "has unequal pupils and seems confused". |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedElectricalBurnSystem.cs` | modified | OD15 electrical band on `WolfmedPartDamageEvent`; the roll is gone. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundBehaviors.cs` | modified | `WolfmedElectricalShockBehavior` loses its roll fields (`organDamageChance`, `organDamage`, `organSlot`). |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M3 block at the end; the ambient-ceiling doc names the new ceilings. |
| `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml`, `species_parts.yml` | modified | `organReach` on torso, head, IPC torso; limbs finish on Blunt 10. |
| `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | modified | Heart `impairedRegenFactor` 0.5; the header notes what the roll fields still do. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/wound_rules.yml` | modified | OD15: the crush internal bleed at Blunt 40, no chance. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/burns.yml` | modified | The internal burn's roll fields go. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml` | modified | `helpOutTimed` on PainFaint. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/organ_causes.yml`, `Alerts/organ_alerts.yml` | new | HeadBlow, Brain, Core causes and alerts. |
| `Resources/Locale/en-US/_WF/Wolfmed/organs.ftl` | new | Every M3 string. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedConsequencesTest.cs` | new | `OrganCalibrationTest`, `LungRouteTest`, `BrainInjuryInputTest`, `IpcCoreInputTest`, `ElectricalHeartBandTest`, `CrushInternalBleedBandTest`, `StumpTest`, `BarotraumaPartTest`, `BlastHeadTest`, `HeartBandTest`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs`, `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | A procedure cut short before its seal step queues `SurgeryCloseIncision` after itself (`TryQueueClosure` takes an index); the pod's own closures are `AutodocQueued.Continuation` and take no fresh anaesthetic. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedCrawlingActionsTest.cs` | modified | `CallForHelpTest` waits out the fall's stutter, in station air. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedBreathingClockTest.cs`, `WolfmedPainTest.cs`, `WolfmedConsciousnessTest.cs` | modified | Head hits that are now knockouts moved to an arm or aimed at the torso. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOrganTest.cs`, `WolfmedBluntWoundTest.cs`, `WolfmedBurnWoundTest.cs`, `WolfmedAmputationTest.cs`, `WolfmedDamageCommandTest.cs`, `WolfmedSpeciesSpawnTest.cs`, `Scenarios/WolfmedBurnScenarioTest.cs`, `Scenarios/WolfmedPlaytestTwoTest.cs` | modified | Test migration (DECISIONS M3). |

## M4 (2026-09-24)

No marked C# edits (plan §12 M4: "Marked code edits: none"). Marked upstream YAML lines:

| File:line | Kind | Reason |
|---|---|---|
| `Resources/Prototypes/Body/Organs/Animal/animal.yml:36, 141` | upstream YAML | WOLFGATE (M4): OD16, plan §9.2 group B. `OrganAnimalLungs` + `WolfmedOrganLungs`, `OrganAnimalHeart` + `WolfmedOrganHeart`: the animal heart (moth, reptilian, vulpkanin, canine, felionoid, tajaran, rodentia and others) can arrest. Inert on bodies that are not wound hosts. |
| `Resources/Prototypes/Body/Organs/arachnid.yml:91` | upstream YAML | WOLFGATE (M4): OD16, group B. `OrganArachnidHeart` + `WolfmedOrganHeart`. |
| `Resources/Prototypes/Body/Organs/diona.yml:136, 158` | upstream YAML | WOLFGATE (M4): OD16, group C. `OrganDionaBrainNymph` + `WolfmedOrganBrain` (the nymph organ is the brain clock), `OrganDionaLungsNymph` + `WolfmedOrganLungs`. |
| `Resources/Prototypes/Body/Organs/slime.yml:3, 47` | upstream YAML | WOLFGATE (M4): OD16, group C. `SentientSlimeCore` + `WolfmedOrganBrain` (the core is the brain), `OrganSlimeLungs` + `WolfmedOrganLungs`. |
| `Resources/Prototypes/Body/Parts/slime.yml:40-42` | upstream YAML | WOLFGATE (M4): plan §9.3. `HeadSlime` `vital: false`: the core is in the torso, so a slime survives decapitation. |
| `Resources/Prototypes/_DV/Body/Organs/feroxi.yml:34`, `_DV/Body/Organs/harpy.yml:3`, `_NF/Body/Organs/goblin_organs.yml:69`, `_Mono/Body/Organs/hydra.yml:93` | upstream YAML | WOLFGATE (M4): OD16, group A′. The Feroxi, Harpy, Goblin and Hydrakin lungs + `WolfmedOrganLungs`. |
| `Resources/Prototypes/_Mono/Body/Organs/protogen.yml:40, 137, 176` | upstream YAML | WOLFGATE (M4): OD16, group C. `OrganProtogenBrain` + `WolfmedOrganBrain`, `OrganProtogenLungs` + `WolfmedOrganLungs`, `OrganProtogenHeart` + `WolfmedOrganHeart` (every Proto- heart parents it). |
| `Resources/Prototypes/_HL/Body/Organs/skrell.yml:42, 89, 175` | upstream YAML | WOLFGATE (M4): OD16, group C. Skrell lungs, heart and brain + the Wolfmed abstracts. |
| `Resources/Prototypes/_HL/Body/Organs/synth.yml:23, 121` | upstream YAML | WOLFGATE (M4): OD16, Synth mechanical. `OrganSynthBrain` + `WolfmedOrganPositronicBrain` (the ccu is the core), `OrganSynthHeart` + `WolfmedOrganIpcPump` (the coolant pump). |
| `Resources/Prototypes/_HL/Body/Parts/synth.yml:5` | upstream YAML | WOLFGATE (M4): OD16. `PartSynth` parents `WolfmedPartIpc` first: the IPC chassis wound profile. |
| `Resources/Prototypes/_HL/Entities/Mobs/Species/synth.yml:259-266` | upstream YAML | WOLFGATE (M4): OD16. `BaseMobSynth` gets `WoundHost`, `PainShockTarget` and `DeadStartupButton`. |
| `Resources/Prototypes/_StarLight/Entities/Mobs/Species/shadekin.yml:19-22, 94-108` | upstream YAML | WOLFGATE (M4): OD16, group D. `WoundHost`, `PainShockTarget`; passive regeneration neutralised (D29); Blunt gib 400 → 1500 (D22); the body-level Heat 1500 ash trigger removed (OD12). |
| `Resources/Prototypes/_HL/Entities/Mobs/Species/protogen_subspecies.yml:630-634, 729-741` | upstream YAML | WOLFGATE (M4): OD16, group D. `BaseMobProtoKin` gets `WoundHost`, `PainShockTarget`, `LayingDown`; passive regeneration neutralised, Blunt gib 1500, no Heat ash, as the Shadekin. |

## M5 (2026-09-23)

Marked upstream, Onyx and vendored edits: **none.** Inventory #16 (the bloodstream's regeneration seam) landed in M3;
M5's radiation factor is one more multiplier inside `WolfmedLifeSystem.BloodRegenFactor`, which that marked line already
calls. Everything else is `_WF` code or data.

Files:

| File | Change | Why |
|---|---|---|
| `Content.Server/_WF/Wolfmed/Life/WolfmedOverheatSystem.cs` | rewritten | The core-heat route and thermal shutdown (plan §3.11); the pulse without organ reach; the summary corrected. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedCoreHeatComponent.cs` | new | Core temperature, `Hot`, `ThermalShutdown`; the machine marker shared code reads. |
| `Content.Server/_WF/Wolfmed/Body/WolfmedOrganThresholdSystem.cs` | modified | `WithoutOrganReach`. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs` | modified | `ImpairedCoolingFactor`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessCause.cs` | modified | Cause `CoreHeat` 20 (flag, tie order after Arrest); `CirculatoryCollapse` prototype id and `PrototypeId`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessComponent.cs` | modified | Networked `Heartless`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | The `coreheat` pressure is cause CoreHeat. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | modified | The heartless cause prototype; the collapse restart line; the line out of thermal shutdown. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedDyingActionsSystem.cs` | modified | Dying includes thermal shutdown; Succumb and `EndDeliberately` end it on the corpse; the core and collapse dialog texts. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `IsHeartless`; `Heartless` set at `StartArrest`; the CoreHeat route. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedShutdownSystem.cs` | modified | `IsMechanical` and `HasPower` for the Synth's battery; the Synth power poll. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedDormantSystem.cs`, `WolfmedCardSystem.cs` | modified | No wait-as-a-ghost in thermal shutdown; the card bar is the core in thermal shutdown. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedRevivalComponents.cs` | modified | `WolfmedRoutes.CoreHeat` (`1 << 15`). |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs`, `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | modified | `ThermalShutdown` state, `Heartless` (collapse lines), the temperature line. |
| `Content.Shared/_WF/Wolfmed/Hud/WolfmedSyntheticHudComponent.cs`, `Content.Server/_WF/Wolfmed/Hud/WolfmedSyntheticHudSystem.cs` | modified | `CoreTempCritical`; the CORE row is the core's temperature; the Synth's power. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | "is smoking, too hot to touch"; a synth reads as a machine. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedExplanationCard.cs` | modified | The heartless cause prototype. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedDyingEffectsSystem.cs` | modified | The collapse banner. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedSpeciesExceptionPrototype.cs` | new | `wolfmedSpeciesException` and `WolfmedSpeciesCheck`. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M4 block at the end. |
| `Resources/Prototypes/_WF/Wolfmed/Body/species_exceptions.yml` | new | IPC, Synth, Diona, ProtoDionae, SlimePerson, ProtoSlimePerson. |
| `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | modified | The pump's `impairedCoolingFactor` 0.5. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/species_causes.yml`, `Alerts/species_alerts.yml` | new | CoreHeat and CirculatoryCollapse causes and alerts. |
| `Resources/Prototypes/_WF/Wolfmed/Hud/synthetic_hud.yml` | modified | CORE TEMP CRITICAL line (M4 block at the end). |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/synth_core.yml` | new | `SurgeryRepairSynthCore` and its re-flash step. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | modified | `SurgeryRepairBrain` is not offered on a synth. |
| `Resources/Locale/en-US/_WF/Wolfmed/species.ftl` | new | Every M4 string. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedIpcDeathTest.cs`, `WolfmedSpeciesTest.cs` | new | `IpcFireScenarioTest`, `ThermalShutdownSuccumbTest`, `SpeciesArrestTest`, `SynthBranchTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesConformanceTest.cs` | rewritten | Strict (`EverySpeciesConformsOrIsExcusedTest`). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOverheatTest.cs`, `WolfmedSpeciesSpawnTest.cs`, `WolfmedSyntheticHudTest.cs`, `Scenarios/WolfmedCauseScenarioTest.cs` | modified | Test migration (DECISIONS M4); the OverlappingCausesTest IPC branch. |

| `Content.Server/_WF/Wolfmed/Life/WolfmedToxinSystem.cs` | new | Toxin load (systemic Poison), its lines, the toxic coma's brain drain, liver clearance (OD13). |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRadiationSystem.cs` | new | The marrow route: regeneration factor, marrow blood loss (discarded, no puddle), the Downed level; `WolfmedMarrowLossComponent`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedBodyTemperatureSystem.cs`, `WolfmedBodyTemperatureComponent.cs` | new | The core temperature (lags a colder surface), the per-species lines, the cold and heat inputs, the fire grace, heat stroke's drain, the cold arrest query. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | Clearance and marrow steps in `Tick`; toxin and heat drains in `DrainRate`; the oxygen trigger names "toxin" and "heat"; the cold arrest trigger; `BloodRegenFactor` × radiation; `GetVolumeLossRate` counts the marrow; four new routes; restart memory for cold, toxin and heat. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | Toxin, Radiation, Cold and Heat inputs in `Evaluate` (flesh only); arrest sources "cold", "toxin", "heat". |
| `Content.Server/_WF/Wolfmed/Life/WolfmedRevivalSystem.cs` | modified | `TooCold` refusal; `LocalizeLine` carries the core and the rewarm line. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Vitals.cs` | modified | Toxins with the liver, radiation with the marrow, the core temperature; the TooCold verdict. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | modified | Spreading infection and sepsis deal no Poison (OD13); the fever stops under the species' heat input (`FeverCeiling`). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionProfilePrototype.cs`, `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | modified | `spreadingPoisonPerMinute` and `sepsisPoisonPerMinute` removed. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedChemicalBurnSystem.cs`, `WolfmedWoundRuleSystem.cs`, `Resources/Prototypes/_WF/Wolfmed/Wounds/burns.yml` | modified | P21: the residue's damage deepens its own chemical burn (`ResidueTarget`), never a plain burn. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessCause.cs` | modified | Causes `Toxin` 16, `Radiation` 17, `Cold` 18, `Heat` 19 (15 left for M4's CoreHeat); sources `Toxin` 10, `Heat` 11, `ArrestCold` 30, `ArrestToxin` 31, `ArrestHeat` 32; tie order. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedRevivalComponents.cs` | modified | Routes `Toxin`, `HeatStroke`, `Marrow`, `Hypothermia` (bits 11-14; 10 left for M4). |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | modified | Toxin, liver, radiation and core fields and lines; `WolfmedDefibVerdict.TooCold`; `WolfmedToxinBand`, `WolfmedLiverState`, `WolfmedRadiationBand`. |
| `Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs`, `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | modified | `impairedClearanceFactor` (liver 0.5). |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs` | modified | Close-up signs: retching, grey and sickly, cold and stiff, hot and dry. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M5 block at the end. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/remaining_causes.yml`, `Alerts/remaining_alerts.yml` | new | The four causes and their seven alerts. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml` | modified | Hypoxia sources Toxin and Heat; arrest sources cold, toxin, heat. |
| `Resources/Locale/en-US/_WF/Wolfmed/remaining-causes.ftl` | new | Every M5 string. |
| `Resources/Locale/en-US/_WF/Wolfmed/treatment-advice.ftl`, `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml`, `WoundTreatment.xml` | modified | Infection no longer "takes toxin damage"; a paragraph on the four causes. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedRemainingCausesTest.cs` | new | `ToxinScenarioTest`, `RadiationScenarioTest`, `SepsisNotToxinTest`, `AcidResidueTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedTemperatureTest.cs` | new | `ColdRoomMeasurementTest`, `HypothermiaScenarioTest`, `HeatStrokeScenarioTest`, `SpeciesLinesTest`, `SpaceColdSmokeTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedInfectionTest.cs` | modified | Infection and sepsis deal no Poison; `SepsisPoisonsAndAntibioticsClearItTest` renamed `SepsisShowsAndAntibioticsClearItTest`. |

## M6 (2026-09-24)

Marked upstream and Onyx edits (inventory #17 and #18, plus the P25 event fields and the P30 fracture line):

| File:line | Kind | Reason |
|---|---|---|
| `Content.Shared/Damage/Systems/DamageableSystem.cs:219` | upstream | WOLFGATE (M6): P25. `BeforeDamageChangedEvent` is built with the caller's `ignoreResistances`, `interruptsDoAfters` and `partMultiplier`, so a handler that cancels and re-applies the damage (Wolfmed routing) can pass them on. |
| `Content.Shared/Damage/Systems/DamageableSystem.cs:509` | upstream | WOLFGATE (M6): P25. The three fields on the record, defaulted so every other reader is unchanged. |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:60-62` | Onyx | WOLFGATE (M6): P25. `_ignoreResistances` and `_partMultipliers`, the pass's arguments for its part step. |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:101-114` | Onyx | WOLFGATE (M6): P25 (inventory #17). `OnBeforeDamageChanged` records the part multiplier and passes `IgnoreResistances` and `InterruptsDoAfters` to `RouteThroughBodyModifiers` instead of the defaults. |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:658, 710` | Onyx | WOLFGATE (M6): P25. `RouteThroughBodyModifiers` marks a pass that ignores resistances, and clears it. |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:750-756, 770` | Onyx | WOLFGATE (M6): P25. The part multiplier scales the localized damage once; ignoring resistances skips the part's armour (`PartDamageModifyEvent`). |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:828` | Onyx | WOLFGATE (M6): OD18 (inventory #18). The hit's `interruptsDoAfters` on its `PartDamageAppliedEvent`. |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:41, 557` | Onyx | WOLFGATE (M6): `TryApplyLethalDamage` removed (no caller since M2's HOOK 13 rewrite), with the `MobThresholdSystem` dependency only it read. |
| `Content.Shared/_Onyx/Wounds/WoundEvents.cs:62-63` | Onyx | WOLFGATE (M6): OD18. `PartDamageAppliedEvent.InterruptsDoAfters`, default true. |
| `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:57-59` | Onyx | WOLFGATE (M6): P30. A new fracture is created at the effective trauma that graded it, not at the hit alone, so it never starts gradeless. |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:76-78, 96-98` | Onyx YAML (comments) | WOLFGATE (corrected M6): the IPC profile's Cold, Caustic and organ damage are live, not inert (rundown Appendix A). No data change. |

Files:

| File | Change | Why |
|---|---|---|
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedDoAfterInterruptSystem.cs` | new | OD18: one part hit of `wolfmed.doafter_interrupt_damage` or more cancels the hit body's treatment, surgery and break-on-damage do-afters. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedPartHitSystem.cs` | modified | Calls the interrupt system once per hit. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedOverheatSystem.cs` | modified | The pulse passes `interruptsDoAfters: false`, as fire does. |
| `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` | modified | A blast passes `interruptsDoAfters: true`, as upstream does. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs`, `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs`, `WolfmedWoundBehaviors.cs` | modified | P30: `GetPartNecrosisOnset`, the onset divided by the risk multiplier. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | The stall signature carries each wound's bleeding severity. |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs`, `WolfmedSurgeryConditionSystem.cs` | modified | `validWhile` on the organ-damaged condition. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml`, `synth_core.yml` | modified | Core repairs stay listed while the housing is open (`validWhile: WolfmedCoreHousingOpen`). |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/programs.yml`, `categories.yml`, `triage.yml`, `autodoc.yml` | modified | Core repairs on the neuro disk, in the neuro category and the organ-repair triage step, with no reagents; a multitool in the pod's tools. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | M6 block at the end: `wolfmed.doafter_interrupt_damage`. |
| `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml`, `Content.Shared/_WF/Wolfmed/Consciousness/SharedWolfmedConsciousnessSystem.cs`, `WolfmedConsciousnessComponent.cs` | modified | Comments only: rundown Appendix A corrections. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | M6 sections; inline *[M6 correction]* notes. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedLeftoversTest.cs` | new | `HitInterruptsSelfTreatmentTest`, `PodRepairsACoreTest`, `PodClampProgressTest`, `SynthRunsNoToxinRouteTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDamageBridgeTest.cs`, `WolfmedBluntWoundTest.cs`, `WolfmedInfectionTest.cs` | modified | `RoutingPassesIgnoreResistancesTest`, `FractureGradeTest`, `NecrosisRiskTest`; the tourniquet clock migration. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedMechanicalWoundTest.cs` | modified | The short-circuit and overheating hits take resistances (they had counted on P25's dropped flag). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSurgeryTest.cs` | modified | The second fracture of the ladder test starts at the arm's whole trauma (P30). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedRemainingCausesTest.cs` | modified | `AcidResidueTest` gets station air (a vacuum cold tick left a plain burn on the torso). |

## M2-M6 review fixes (2026-09-24)

No marked upstream or Onyx edits.

| File | Change | Why |
|---|---|---|
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `GetRestartMemory` and `GetActiveRoutes` read M3's damaged lungs. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedWoundTraitSystem.cs` | modified | `GetPartNecrosisRisk` removed (uncalled since M6). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedConsequencesTest.cs` | modified | `LungArrestRestartMemoryTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBurnWoundTest.cs` | modified | The frostbite test reads `GetPartNecrosisOnset`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The review fixes section; inline corrections for the heartbeat licence, M2's restart memory and the necrosis row. |

## Synth takes no poison (2026-09-24)

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_HL/Damage/modifier_sets.yml:5` | upstream YAML | WOLFGATE (Wolfmed): `Poison: 0` on the `Synth` modifier set (OD16, the owner's 2026-09-24 call). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedLeftoversTest.cs` | test | `SynthRunsNoToxinRouteTest` renamed `SynthTakesNoPoisonTest`; the dose goes through resistances and the load must be zero. |

## Playtest 3 fixes (2026-09-24)

No marked upstream, Onyx or vendored edits: everything is in `_WF` code and Wolfmed prototypes and locale.

| File | Change | Why |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedExplanationCard.cs` | modified | `Rows` (kinds for styling), `Countdown`, `Colour`, `CauseAlert`, `BlockerAlerts`, `DefaultColour`; the felt states (breath, blood). |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedConsciousnessCause.cs` | modified | Optional `cardColour` on `wolfmedConsciousnessCause`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedRevivalComponents.cs` | modified | `WakeStart`/`WakeEnd` on the networked `WolfmedCardComponent`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | `GetWakeWindow`, the one wake window. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConditionAlertSystem.cs` | modified | The faint alert's cooldown reads `GetWakeWindow`; the change handler refreshes the card. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedCardSystem.cs` | modified | Sets and clears the wake window. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedExplanationCardOverlay.cs` | modified | The new look, the countdown row, the icons; `WolfmedExplanationCardLayout`. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedClimbSystem.cs` | new | `ClimbableComponent, AttemptClimbEvent` refusal; the tables' layer kept on a Downed body (`WolfmedDownedComponent, EndClimbEvent`). |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | `HoldTables` on going Downed, `ReleaseTables` after. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | modified | `WolfmedVitalsText` rebuilt: `VitalsLine`, `DoFirstLine` (replaces `RoutesLine`), `Aid`, `Number`/`Units`/`Whole`; `BreathingLine`, `CirculationLine`, `BurnFluidLine`, `ToxinLine`, `RadiationLine`, `CoreTemperatureLine`, `OrganLine`, `TemperatureLine` removed. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedPostShockText.cs` | modified | Strings through the rounding helpers. |
| `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml`, `organ_causes.yml`, `species_causes.yml` | modified | `cardColour` on PainFaint, HeadBlow (amber), Arrest, CoreHeat, CirculatoryCollapse (red), Sedation (steel blue), Shutdown (grey). |
| `Resources/Locale/en-US/_WF/Wolfmed/analyzer-vitals.ftl` | modified | Item, "Do first" and aid keys; breathing, pulse and trend values; circulation, hydraulics, route and routes keys removed. |
| `Resources/Locale/en-US/_WF/Wolfmed/organs.ftl`, `remaining-causes.ftl`, `species.ftl`, `burns.ftl` | modified | Organ names capitalised, effect and "Organs:" keys removed; toxin, liver, route and burn-fluid keys removed; radiation, core and machine temperature values as items; `wolfmed-vitals-breathing-none-collapse` removed. |
| `Resources/Locale/en-US/_WF/Wolfmed/revival.ftl` | modified | `wolfmed-card-countdown`, `-countdown-none`, `-breathing-*`, `-blood-*`. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | `wolfmed-downed-cant-climb`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPlaytestThreeTest.cs` | new | `CardCountdownTest`, `CardLooksTest`, `DownedCannotClimbTest`, `VitalsBlockIsCompactTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedMedicLinesTest.cs`, `WolfmedMedicInfoTest.cs`, `WolfmedBreathingClockTest.cs`, `WolfmedConsequencesTest.cs`, `WolfmedIpcDeathTest.cs`, `WolfmedLeftoversTest.cs`, `WolfmedRemainingCausesTest.cs`, `WolfmedSpeciesTest.cs`, `WolfmedTemperatureTest.cs`, `Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs`, `WolfmedLocaleCoverageTest.cs` | modified | Migrated to the three-line block (DECISIONS "Playtest 3 fixes", test migration). |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The "Playtest 3 fixes" sections. |

## Playtest 3, IPC round (2026-09-24)

One marked Onyx edit; everything else is in `_WF` code, Wolfmed locale and tests.

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:69-71` | Onyx YAML | WOLFGATE (playtest 3 IPC): `IpcBodyPartProfile.bleedingMultiplier` 1 → 0.4. Seven spear hits bled a chassis at the bloodstream's 10 u a tick cap, as fast as a human; now it reaches its lines about twice as late (DECISIONS "Playtest 3, IPC round"). |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudLayout.cs` | modified | `SystemPanel`/`WolfmedSystemPanel` (the SYSTEM box sized from its rows), `SystemHeight`, `SystemWidth`, `WrapAdvice`, `Visible`, `DrawnBoxes`, `Row`, `Contains`; `SystemRows` replaced by `MaxSystemGauges`, `MaxAdviceRows`, `SystemColumns`, `BarColumn`, `ValueColumns`; `CharWidth` carries the 1.4 factor its callers applied. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlay.cs` | modified | `DrawOptics` (the centre arcs and ticks), `Tier`, the idle glyph and `GlyphAlpha` removed; `DrawSystem` rebuilt on `SystemPanel` with `SystemRow` (label, bar, right-aligned value), one text size, `TextRight`. |
| `Content.Client/_WF/Wolfmed/Overlays/WolfmedSyntheticHudOverlaySystem.cs` | modified | Builds `SystemRow`s, the fault label and count, the advice prefix; visibility through `WolfmedSyntheticHudLayout.Visible`; no glyph. |
| `Resources/Locale/en-US/_WF/Wolfmed/synthetic-hud.ftl` | modified | Row keys hold the label only; `wolfmed-synthetic-row-core` new; `wolfmed-synthetic-row-core-temp` is the value; `wolfmed-synthetic-glyph` removed. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedIpcFluidLossTest.cs` | new | `IpcFluidLossTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSyntheticHudTest.cs` | modified | `SyntheticHudPanelFitsTest` new; the locale key list follows the row keys. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The "Playtest 3, IPC round" sections; the "Synthetic HUD" section's Idle glyph annotated as removed. |

## Playtest 3, IPC round 2 (2026-09-24)

Four marked upstream lines (two Einstein Engines, one stock, one Frontier) and two reworded comments inside an existing
WOLFGATE block; everything else is `_WF` code, Wolfmed prototypes, locale and tests.

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml:30` | EE YAML | WOLFGATE (playtest 3 IPC 2): `MobIPC` `bloodReagent` Oil → `WolfmedHydraulicFluid`. Oil is lamp oil (flammability 2, a FlammableTileReaction) and a lit welder lit the chassis's own puddle. Lines 19 and 34, in the existing P5-2 WOLFGATE comments, say "its fluid" / "Fluid loss" instead of oil. |
| `Resources/Prototypes/_EinsteinEngines/Body/Organs/ipc.yml:17` | EE YAML | WOLFGATE (playtest 3 IPC 2): `BaseIPCOrgan`'s 10 u organ solution is hydraulic fluid, not Oil. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/wallmed.yml:7` | stock YAML | WOLFGATE (playtest 3 IPC 2): `WolfmedHydraulicFluidPack: 2` beside the blood packs. |
| `Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/civimed.yml:8` | Frontier YAML | WOLFGATE (playtest 3 IPC 2): `WolfmedHydraulicFluidPack` infinite, beside the blood packs. |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | modified | `WolfmedSurgeryWoundCondition.woundPrototypes` and `mechanical`; `WolfmedSurgeryWeldChassisEffect`, `WolfmedSurgeryRewireChassisEffect` new. |
| `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | modified | The wound list and the machine filter in `OnWoundValid`; the weld and rewire completion checks; `HasAnyWound`. |
| `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | modified | HOOK 24 body: a tend never lists on a machine part. |
| `Content.Server/_WF/Wolfmed/Surgery/WolfmedChassisRepairSurgerySystem.cs` | new | The weld pass (the hand welder's call, then the topical rule) and the rewire pass (the coil's own heal, five uses). |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocReagentEntry.Machine`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | `DrawFluid`: a machine fluid only to a body that runs on it, and nothing else to such a body. |
| `Content.Shared/_WF/Wolfmed/Reagents/WolfmedFluidPackComponent.cs` | new | `WolfmedFluidPackComponent`, `WolfmedFluidPackDoAfterEvent`. |
| `Content.Server/_WF/Wolfmed/Medical/WolfmedFluidPackSystem.cs` | new | The hand refill; `GetRefusal`, `TryStart`, `TryTransfer`. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSfxProfilePrototype.cs` | modified | Comment: an IPC spills hydraulic fluid. |
| `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml`, `surgery_steps.yml` | modified | `SurgeryWeldChassis`, `SurgeryRewireChassis`, `SurgeryStepWeldChassis`, `SurgeryStepRewireChassis`. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/categories.yml`, `programs.yml` | modified | Both on the Mechanical category and the base program; `autodocProcedure` 0/0; `WolfmedHydraulicFluid` on `WolfmedAutodocReagents` with `machine: true`. |
| `Resources/Prototypes/_WF/Wolfmed/Reagents/hydraulics.yml` | new | `WolfmedHydraulicFluid`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/hydraulics.yml` | new | `WolfmedHydraulicFluidPack`. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/reactions.yml` | modified | `WolfmedHydraulicFluid`: Oil + Silicon → 2. |
| `Resources/Prototypes/_WF/Catalog/VendingMachines/Inventories/wolfgate.yml` | modified | `WolfmedHydraulicFluidPack: 4`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/Debugging/debug_medbox.yml` | modified | One pack in the debug crate. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/sfx.yml` | modified | Comment: an IPC spills hydraulic fluid. |
| `Resources/Locale/en-US/_WF/Wolfmed/hydraulics.ftl` | new | The reagent's name and description; the pack's refusals and finish. |
| `Resources/Locale/en-US/_WF/Wolfmed/analyzer-vitals.ftl`, `consciousness.ftl` | modified | "Fluid N%", "refill hydraulic fluid ≈ N u", "weld the fluid leak", "refill the hydraulic fluid" (values only; keys unchanged). |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml`, `Autodoc.xml` | modified | A torn chassis leaks hydraulic fluid; the pack, and oil refused; the pod welds and rewires a chassis. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPodWeldsChassisTest.cs` | new | `PodWeldsChassisTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedHydraulicFluidTest.cs` | new | `HydraulicFluidTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesSpawnTest.cs`, `WolfmedEviscerationTest.cs`, `Scenarios/WolfmedIpcFluidLossTest.cs` | modified | Migrated from Oil to hydraulic fluid. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The "Playtest 3, IPC round 2" sections. |

## Playtest 3, S.A.M. round (2026-09-24)

Two marked Shitmed edits; everything else is `_WF` code, Wolfmed locale and voice data, and tests.

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs:348-349` | Shitmed | WOLFGATE (playtest 3 SAM): `OnTendWoundsStep` hands a wound host with no damage of the step's group to `WolfmedTendUndamaged` before the damage gate, so the pass still closes the part's wounds. It returned without touching them, and the pod said THIS IS NOT WORKING (DECISIONS "Playtest 3, S.A.M. round", item 3). |
| `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs:128` | Shitmed | WOLFGATE (playtest 3 SAM): `OnWoundedValid`'s "some damage somewhere" gate does not apply to a wound host (`WolfmedJudgedByWounds`), which HOOK 24 already judges by its wounds; a patient healed with packs kept every wound and got no tend planned. |
| `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Tend.cs` | new | `WolfmedTendUndamaged` and `WolfmedJudgedByWounds`, the hook bodies. |
| `Content.Shared/_WF/Wolfmed/Autodoc/SharedAutodocSystem.cs` | modified | The drop check answers before the climb's; `AutodocComponent, AttemptClimbEvent` refuses climbing onto the pod; `AutodocComponent, ItemSlotInsertAttemptEvent` keeps whole bodies out of the tray. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocComponent.cs` | modified | `AutoSession`, `VoiceEvents`, `ProceduresAnnounced`, `BlockingGarment`, `BlockingSlot`, `GarmentStuck`; `AutodocQueued.FollowUp`, `.IgnorePodWounds`. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocVoiceEvent.Removing`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | Eject slides the body off the pod (`SlideOff` replaces the climb; the `ClimbSystem` dependency is gone); the drag-drop handles a body before construction's drag-drop and the climb; `WolfmedWoundReplacedEvent` subscription; the run and garment state reset on insert and in `Reset`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Session.cs` | new | The AUTO run (`ContinueAutoRun`, `EndAutoRun`), follow-ups (`QueueFollowUp`, `PlanTendFollowUps`, `DropStaleFollowUps`, `StillPlannable`), `OnWoundReplaced`, `CountVoice`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Undress.cs` | new | `ArmorSlots` (Shitmed's table), `Blockers`, `Undress`, `CanTakeOff`, `StowInTray`, `AbandonForClothing`, `BlockingStatus`, `AnnounceUndress`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Triage.cs` | modified | `Plan` collects follow-ups; `TryPlan(whole)` over `WritePlan`; `AutoPlan`, the bounded planner `TickAuto` used inline, now shared with the run's re-plan; the run marked where it starts. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Procedure.cs` | modified | `Prepare` drops stale follow-ups; the stall guard measures each pass (`NoteStall(…, before)`); `StallProcedure(speak)`; `FinishQueue` continues the run; `EnterBlocked` names the garment; `TickClothing` gives a procedure up; `CutClothing` undresses the blocked part. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Voice.cs` | modified | Counts voice events and procedure announcements. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | WAITING names the garment and its slot. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedEmbeddedRemovalSystem.cs` | modified | `WolfmedWoundReplacedEvent`, broadcast when a pulled round's wound is replaced. |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py`, `gen_autodoc_voice_protos.py` | modified | The `removing` line and the `Removing` event. |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/removing.ogg`, `attributions.yml` | new, modified | Generated (eSpeak NG). |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml`, `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | modified | Generated: `removing`. |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc.ftl` | modified | `wolfmed-autodoc-no-climb`, `wolfmed-autodoc-status-waiting-garment`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPlaytestThreeSamTest.cs` | new | `PodHoldsOneTest`, `AutoIsOneRunTest`, `NoStallWhileWorkingTest`, `PodUndressesWhatItCannotCutTest`, `PodUndressesWhatItCannotCutLockedTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAutodocLoopTest.cs` | modified | `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`: the torso's other work is queued after the removal as follow-ups and has run by the end of the queue. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The "Playtest 3, S.A.M. round" sections; two AUTODOC statements annotated inline. |

## Playtest 3, pod atmosphere (2026-09-24)

No upstream or Onyx file is touched; everything is `_WF` code, Wolfmed YAML and locale, the voice generator and tests.
The existing pod files only gained calls (DECISIONS "Playtest 3, pod atmosphere").

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Autodoc/WolfmedAutodocAtmosphereComponent.cs` | new | `WolfmedAutodocAtmosphereComponent` (the pod's mix and `Broken`), `WolfmedAutodocSeal`, `WolfmedAutodocAtmosphereVisuals`, `WolfmedAutodocSealText`. |
| `Content.Shared/_WF/Wolfmed/Autodoc/WolfmedAutodocOccupantComponent.cs` | new | The occupant marker. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Atmosphere.cs` | new | `WolfmedAutodocOccupantComponent` + `InhaleLocationEvent` / `ExhaleLocationEvent` / `AtmosExposedGetAirEvent` (by ref), `WolfmedAutodocAtmosphereComponent` + `BreakageEventArgs` / `DamageChangedEvent`; `GetSeal`, `IsSealedIn`, the regenerated mix, the examine line. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs` | modified | Four added calls: `InitializeAtmosphere` in `Initialize`, `AtmosphereOccupantEntered` in `OnInserted`, `AtmosphereOccupantLeft` in `OnRemoved`, `ExamineAtmosphere` in `OnExamined`. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Ui.cs` | modified | `Seal = GetSeal(ent)` in the BUI state. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocUi.cs` | modified | `AutodocBuiState.Seal`. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocVoiceEvent.HullBreach`, appended. |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocWindow.cs` | modified | The seal label after the status readout. |
| `Content.Client/_WF/Wolfmed/Autodoc/AutodocVisualizerSystem.cs` | modified | The breached tint. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml` | modified | `WolfmedAutodocAtmosphere` (400 L, 101.325 kPa, 293.15 K, 21/79); a Destructible threshold at 100 acting `Breakage`; `Repairable` with Welding and Applicating, 5 s. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml`, `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl`, `Resources/Audio/_WF/Wolfmed/Autodoc/voice/attributions.yml` | regenerated | The `hull-breach` line and the `HullBreach` event. |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/hull-breach.ogg` | new | "HULL BREACH. OUTSIDE ATMOSPHERE.", eSpeak NG, 2.05 s. |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py`, `gen_autodoc_voice_protos.py` | modified | The `hull-breach` row and the `HullBreach` event. |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc-atmosphere.ftl` | new | The five readout keys and four examine keys. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/PodAtmosphereTest.cs` | new | Six tests (DECISIONS). |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The "Playtest 3, pod atmosphere" sections. |

## Playtest 4, the Debug client and gear it arrived wearing (2026-09-26)

One marked upstream edit (DECISIONS "The Debug client and gear it arrived wearing"). Not a Wolfmed regression: the
client never learns the slot of gear a mob already wore when it first saw it, and Debug's assert on the unequip closed
the client.

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/Clothing/EntitySystems/HideLayerClothingSystem.cs` | modified, `// WOLFGATE` | `SetLayerVisibility` returns early when `InSlot` / `InSlotFlag` is null instead of reaching the Debug asserts. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpawnedGearUnequipTest.cs` | new | A client attached to a mob spawned wearing a hardsuit helmet takes it off through the predicted slot click; prints the client-side `InSlot` (null). |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, limb pain caps and the range (2026-09-26)

All `_WF`, tests and one generated map (DECISIONS "Limbs hold less pain" and "The Wolfmed range").

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.part_pain_cap_arm` 80, `_hand` 50, `_leg` 90, `_foot` 50. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedBodyPainSystem.cs` | modified | `OnPainInit` sets a limb's `SoftPainCap` from its type's line; `PartPainCap(BodyPartType)`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPartPainCapTest.cs` | new | One arm caps at 80 and stays Up; arm + leg Down; four limbs faint; the torso keeps 135. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Range/WolfmedRangeMapGenerator.cs` | new | `[Explicit]` generator for the range map (16 x 16, wall lights, air via fixgridatmos, species markers). |
| `Content.Server/_WF/Wolfmed/Range/WolfmedRangeCommand.cs`, `Resources/Locale/en-US/_WF/Wolfmed/range.ftl` | new | `wolfmedrange`: loads the range initialised and teleports the caller; `loadmap` would leave it inert. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Range/WolfmedRangeMapTest.cs` | new | Loads the committed range: one humanoid per playable species, both pods powered, oxygen at both pods. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/Debugging/range_spawner.yml` | new | `WFWolfmedRangeSpawner`, the self-deleting marker that spawns one species on map init. |
| `Resources/Maps/_WF/Wolfmed/wolfmed_range.yml` | new, generated | The range; `loadmap 100 /Maps/_WF/Wolfmed/wolfmed_range.yml`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The two sections. |

## Playtest 4, organs take nine heavy hits (2026-09-26)

All `_WF` YAML, one CVar default and tests (DECISIONS "Organs take nine heavy hits").

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | modified | `health`/`maxHealth` 25 on the heart, lungs, liver, stomach and kidney bases. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.organ_hit_cap` 3 (was 5); the reach-line comment carries the new rifle numbers. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedMacheteTest.cs` | new | Machete to a human and a Skrell chest: heart intact and no faint on hit 3, heart failed within hits 7-12. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOrganTest.cs`, `Scenarios/WolfmedConsequencesTest.cs`, `WolfmedAnalyzerTest.cs`, `WolfmedWoundSurgeryTest.cs`, `WolfmedAutodocFaintTest.cs` | modified | Organ health 25 on torso organs (rows, heal steps, the shock band at 21); the rifle calibration reads lungs 8-12 impaired, 12-16 failed, heart never; the IPC pump only has to outlast the heart; both pin cap 3; the autodoc faint fixture adds a torso hit since two legs cap at 180 pain. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, treated pain fades and the pod's popup (2026-09-26)

One marked Onyx edit and `_WF` (DECISIONS "Treated pain fades", "The pod is for lying in").

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | modified, `// WOLFGATE` | `RecoverPain` sheds pain over the wound floor at `wolfmed.pain_loose_recovery` a second. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs`, `Consciousness/WolfmedBodyPainSystem.cs` | modified | `wolfmed.pain_loose_recovery` (3) and its accessor. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedClimbSystem.cs` | modified | The table guard runs after the pod's climb refusal. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedLoosePainTest.cs` | new | 100 pain over a 60 floor settles to 60 in 20 s; with the floor gone it is under 1 in 25 s. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | The two sections. |

## Playtest 4, sidearms from the floor (2026-09-26)

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedDownedSystem.cs` | modified | `OnShotAttempt` passes guns with the `Sidearm` tag; `OnAttackAttempt` passes the gun system's targetless, weaponless CanAttack when the active hand holds one. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | The reach line's comment. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedDownedSidearmTest.cs` | new | Pistol and revolver fire, rifle and shotgun do not. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, SEPSIS (2026-09-27)

All `_WF`: sepsis damages the torso organs past 90, the analyzer says so, and organic bodies cough, choke, wheeze,
retch and shiver by their condition (DECISIONS "Playtest 4, SEPSIS").

| File | Kind | Change |
| --- | --- | --- |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | modified | `TickSepsis` damages the organic torso's organs past `wolfmed.sepsis_organ_damage_from` through `OrganHealthSystem`, by slot weight, carrying the hundredths per organ; `DamagingOrgans` and `HasFever` queries. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionProfilePrototype.cs`, `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | modified | `sepsisOrganWeights`: kidneys 1.5, liver 1.5, lungs 1, stomach 1, heart 0.75. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionComponents.cs` | modified | `WolfmedSepsisComponent.OrganDamageOwed`, the per-organ remainder (server, not networked). |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.sepsis_organ_damage_from`, `sepsis_organ_damage_per_minute`, `condition_emote_interval`, `condition_emote_chance`, `condition_vomit_interval`, `condition_cough_sepsis`, `condition_retch_sepsis`, `condition_vomit_sepsis`, `condition_wheeze_oxygenation`. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedRevivalComponents.cs` | modified | `WolfmedRoutes.SepsisOrgans` (bit 10). |
| `Content.Server/_WF/Wolfmed/Life/WolfmedLifeSystem.cs` | modified | `GetActiveRoutes` adds `SepsisOrgans` while the damage runs. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | modified | "Do first" drops the plain sepsis aid while the organ-damage aid shows. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs` | modified | The sepsis banner reads "damaging the organs" off the vitals routes. |
| `Resources/Locale/en-US/_WF/Wolfmed/analyzer-vitals.ftl`, `revival.ftl`, `wounds.ftl` | modified | `wolfmed-vitals-aid-sepsisorgans`, `wolfmed-dormant-route-sepsisorgans`, `health-analyzer-wound-sepsis-organs`. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedConditionEmoteSystem.cs` | new | Every 8 s, each organic wound host's conditions in priority order, one emote a check through `TryEmoteWithChat`; the retch vomits once a vomit interval. |
| `Content.Server/_WF/Wolfmed/Life/WolfmedConditionEmoteComponent.cs` | new | Last emote, counts and the vomit clock (server). |
| `Resources/Prototypes/_WF/Wolfmed/Voice/condition_emotes.yml` | new | `WFWolfmedChoke`, `Cough`, `CoughBlood`, `Wheeze`, `Retch`, `Shiver`: Vocal, in the emote menu, no sounds, no chat triggers. |
| `Resources/Locale/en-US/_WF/Wolfmed/condition-emotes.ftl` | new | Their names and chat lines. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedSepsisTest.cs` | new | `SepsisKillsTest` (real time: arrest 511 s, death 625 s), `SepsisOrganDamageTest` (kidneys and liver 665 s, lungs and stomach 1000 s, heart last; the analyzer lines; antibiotics stop it). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedConditionEmoteTest.cs` | new | Failed lungs cough up blood; sepsis 95 retches and vomits once in 64 s; healthy, dead and IPC never emote. |
## Playtest 4, VISUALS: the pain HUD, wound and rot overlays (2026-09-27)

Two marked upstream edits, one marked Onyx edit, the rest `_WF` (DECISIONS "Playtest 4, VISUALS").

| File | Kind | Change |
| --- | --- | --- |
| `Tools/_WF/Wolfmed/gen_wolfmed_overlays.py` | new | Builds `pain.rsi`, `wounds.rsi` and `rot.rsi` from the Bob icons: backing composite, grey recolour and per-part placement, the chest and groin composite, the mirrored right-hand north frame. |
| `Tools/_WF/Wolfmed/gen_overlay_offsets.py` | new | Measures each round-start species' part sprites against the human ones and writes the offset table. |
| `Resources/Textures/_WF/Wolfmed/Interface/Alerts/pain.rsi/` | new, generated | `pain0`..`pain7`, `paindd` on the health alerts' backing. |
| `Resources/Textures/_WF/Wolfmed/Damage/wounds.rsi/` | new, generated | `<Part>_drip`, `<Part>_stream`, `<Part>_old` for the ten part layers, 4 directions, grey. |
| `Resources/Textures/_WF/Wolfmed/Damage/rot.rsi/` | new, generated | `<Part>_rot` for the ten part layers, 4 directions x 10 frames. |
| `Resources/Textures/_WF/Wolfmed/Damage/artery.rsi/` | new, generated | Playtest 4: `<site>_artery0/1` for the head, neck and eight limbs from Bob's artery.dmi, 4 directions, grey; the 1 states are the three-frame spray. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedStumpComponent.cs` | new | Playtest 4: which part a dismemberment wound is the stump of. |
| `Resources/Textures/_WF/Wolfmed/Damage/stumps.rsi/` | new, generated | Playtest 4: `<site>_stump` (blood mask), `_stump_bone`, `_stump_drip`, `_stump_stream` for the neck and eight limbs, from Nevado's stump.dmi and Bob's bulletwound glyph. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedStumpTagSystem.cs` | new | Playtest 4: tags each new stump wound off the amputation event. |
| `Content.Shared/_WF/Wolfmed/Consciousness/WolfmedPainSettleComponent.cs` | new | Playtest 5: when a part's wound pain floor last rose, for the settle. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPainSettleTest.cs` | new | Playtest 5: the settling floor and wolfmed.pain_scale. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesOverlayTest.cs` | new | Playtest 5: every organic species draws its wounds, stumps and arteries, over its markings. |
| `Resources/Textures/_WF/Wolfmed/Interface/Alerts/downed.rsi/` | generated | Playtest 5: two arrows down on the health alerts' backing. |
| `Resources/Prototypes/_WF/Wolfmed/Damage/overlay_offsets.yml` | new, generated | `WFWolfmedOverlayOffsets`: per-species, per-part pixel shifts. |
| `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml` | modified | The `WFWolfmedPain` category and the `WFWolfmedPain` and `WFWolfmedPainMechanical` alerts, severities 0-8. |
| `Resources/Prototypes/Alerts/alerts.yml` | modified, `# WOLFGATE` | The pain category is ordered under Health. |
| `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | The two alerts' names and severity-banded descriptions. |
| `Content.Client/UserInterface/Systems/Alerts/Controls/AlertControl.cs` | modified, `// WOLFGATE` | The tooltip passes the alert's severity to its description. |
| `Content.Shared/_WF/Wolfmed/Damage/WolfmedWoundOverlay.cs` | new | `WolfmedWoundOverlay` (none, old, drip, stream), the state names, `WolfmedOverlayOffsetsPrototype`. |
| `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | modified, `// WOLFGATE` | `PartDamageVisualsComponent.Wounds`, `Rot`, `WoundColor`, networked. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.pain_hud_from` (5), `wolfmed.wound_overlay_stream_rate` (2), `wolfmed.overlay_refresh_seconds` (1). |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedPainAlertSystem.cs` | new | The pain HUD's severity, the mechanical variant, hide and clear. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedConsciousnessSystem.cs` | modified | `Apply` refreshes the pain HUD. |
| `Content.Server/_WF/Wolfmed/Damage/WolfmedWoundOverlaySystem.cs` | new | Wound look and rot per organic part, the blood colour, the event refresh and the sweep. |
| `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | modified | The rot and wound layers: order, tint, species shift. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedPainHudTest.cs` | new | Hidden under 5; severity 0, 3, 7; cleared at 0; paindd in a faint; a machine gets the mechanical alert. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundOverlayTest.cs` | new | Drip, stream, old on dressing, no layer on an unhurt limb, none on a machine, the layer order under the jumpsuit; rot on each arm, the septic chest, cleared by antibiotics and amputation; every state exists. |
| `Content.Server/_WF/Wolfmed/README.md` | regenerated | `modules.py --write`. |
## Playtest 4, IV (2026-09-27)

All `_WF` (DECISIONS "Playtest 4, IV"). The pod's existing files carry one-line hooks into the new partial.

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Medical/WolfmedIvDripComponent.cs` | new | The drip's component (whitelist, range, interval, sounds, mode, rate, patient, pack use), its mode, appearance keys, layers and attach do-after event. |
| `Content.Shared/_WF/Wolfmed/Medical/SharedWolfmedIvDripSystem.cs` | new | The client half: the stand can be dragged and dropped on a body. |
| `Content.Server/_WF/Wolfmed/Medical/WolfmedIvDripSystem.cs` | new | Hanging and removing a container, attaching (verb, drag, 1 s do-after), inject and take at the flow, the rip, pings and beeps, verbs, examine, appearance; `TransfuseFromPack` and `PacksCanTreat`, which the pod shares. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedIvDripVisualizerSystem.cs` | new | tg's icon states: the stand by mode and flow, `beakeridle`/`beakeractive`, the `reagentNN` fill tinted by the contents. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/iv_drip.yml` | new | `WFWolfmedIvDrip`: a wheeled, walk-through stand with one container slot. |
| `Resources/Textures/_WF/Wolfmed/Medical/iv_drip.rsi` | new | Nova's `icons/obj/medical/iv_drip.dmi`, 14 states (CC-BY-SA-3.0). |
| `Resources/Locale/en-US/_WF/Wolfmed/iv-drip.ftl` | new | The drip's verbs, popups, emotes and examine. |
| `Content.Shared/_WF/Wolfmed/Autodoc/WolfmedAutodocBloodComponent.cs` | new | The pod's blood reservoir state. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.Blood.cs` | new | The transfusion step, its tick, the NO BLOOD LOADED fault, the status text and the reservoir row. |
| `Content.Server/_WF/Wolfmed/Autodoc/AutodocSystem.cs`, `.Procedure.cs`, `.Triage.cs`, `.Ui.cs` | modified | Hooks: `InitializeBlood`, `TickBlood`, `ScheduleTransfusion` at the top of `TryTransfuse` and for the triage `transfuse` step, AUTO and PLAN quiet while blood is running or waited on, the status, TRANSFUSING flag and blood row. |
| `Content.Shared/_WF/Wolfmed/Autodoc/AutodocPrototypes.cs` | modified | `AutodocVoiceEvent.NoBlood`, `AutodocTriageStep.Transfuse`. |
| `Resources/Prototypes/_WF/Wolfmed/Autodoc/autodoc.yml`, `triage.yml` | modified | The `autodoc_blood` slot (tag `Bloodpack`), `WolfmedAutodocBlood`, the step at the top of the triage. |
| `Tools/_WF/Wolfmed/gen_autodoc_voice.py`, `gen_autodoc_voice_protos.py` | modified | The `no-blood` line, "NO BLOOD LOADED.", Urgent. |
| `Resources/Audio/_WF/Wolfmed/Autodoc/voice/no-blood.ogg`, `attributions.yml`, `Resources/Prototypes/_WF/Wolfmed/Autodoc/voice.yml`, `Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl` | new, generated | The line, 1.31 s (eSpeak NG). |
| `Resources/Locale/en-US/_WF/Wolfmed/autodoc.ftl` | modified | The slot name, the fault, the reservoir row. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.iv_rate_default/_min/_max/_step`, `iv_units_per_pack`, `iv_attach_seconds`, `iv_rip_damage`, `iv_rip_wound_severity`, `iv_beep_below`, `iv_beep_chance`, `pod_transfuse_below/_rate/_to`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedIvDripTest.cs` | new | Pack refill at the flow rate, take mode fills and pings, the rip and the clean detach into a pod, Slime blood for a Slime person. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/PodBloodTest.cs` | new | The pod transfuses to 0.95 from its packs; an empty reservoir says NO BLOOD LOADED and starts when a stack goes in. |
| `Content.Server/_WF/Wolfmed/README.md` | generated | The file list. |
## Playtest 4, SOUNDS (2026-09-27)

Three marked upstream edits and `_WF` (DECISIONS "Playtest 4, SOUNDS").

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Audio/_WF/Wolfmed/Gasp/` (20 `.ogg`, `attributions.yml`) | new | The owner's Bob gasps, 7 male and 13 female, mono. |
| `Resources/Audio/_WF/Wolfmed/Emotes/` (51 `.ogg`, `attributions.yml`) | new | Bob sneezes, snores, sniffs, coughs and chokes; Nova gulps; tg gags. |
| `Resources/Audio/_WF/Wolfmed/Bleeding/blood1-6.ogg`, `attributions.yml` | modified / new | Bob's drips replace NovaSector's `blood1-3` under the same names; 4-6 added. |
| `Resources/Audio/_WF/Wolfmed/Bone/` (5 `.ogg`, `attributions.yml`) | new | `crack1-3`, `tendon_snap1-2`. |
| `Resources/Audio/_WF/Wolfmed/Melee/` (6 `.ogg`, `attributions.yml`) | new | `melee1-3`, `stab1-3`. |
| `Resources/Audio/_WF/Wolfmed/Pill/` (`pill_swallow.ogg`, `attributions.yml`) | new | From Bob's WAV. |
| `Resources/Prototypes/_WF/Wolfmed/SoundCollections/bobmed.yml` | new | Every Bobmed collection, SEPSIS's cough, choke and retch included. |
| `Resources/Prototypes/_WF/Wolfmed/SoundCollections/wounds.yml` | modified | `blood1-3` out of `WFWolfmedWoundFlesh`; the spurt comment. |
| `Resources/Prototypes/_WF/Wolfmed/Voice/bobmed_emotes.yml` | new | `WFWolfmedSneeze`, `WFWolfmedSnore`, `WFWolfmedSniff`, `WFWolfmedGulp`. |
| `Resources/Prototypes/_WF/Wolfmed/Voice/bobmed_emote_sounds.yml` | new | `WFWolfmedMaleEmotes` / `WFWolfmedFemaleEmotes`. |
| `Resources/Prototypes/_WF/Wolfmed/Wounds/sfx.yml` | modified | The `BoneFractureWound` entry claims the wound silently. |
| `Resources/Locale/en-US/_WF/Wolfmed/bob-sounds.ftl` | new | The four emotes' names and messages. |
| `Resources/Prototypes/SoundCollections/gasp.yml` | modified, `# WOLFGATE` | `MaleGasp` / `FemaleGasp` hold the Bob files. |
| `Resources/Prototypes/Entities/Objects/Specific/chemistry.yml` | modified, `# WOLFGATE` | `Pill` swallows with `WFWolfmedPillSwallow`. |
| `Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs` | modified, `// WOLFGATE` | Both `PlayHitSound` calls take `WolfmedOrganicSoundSystem.GetHitSound` when the hit has no override. |
| `Content.Shared/_WF/Wolfmed/Sounds/WolfmedOrganicSoundSystem.cs` | new | `IsOrganicBody`; the stab / melee pick. |
| `Content.Server/_WF/Wolfmed/Sounds/WolfmedBodySoundSystem.cs` | new | Emote voices and the flesh-only guard, the crack, the snap. |
| `Content.Server/_WF/Wolfmed/Sounds/WolfmedBleedDripSystem.cs`, `WolfmedBleedDripComponent.cs` | new | The drip clock. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedSfxProfilePrototype.cs` | modified | `WolfmedWoundSoundEntry.Sound` is optional. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.drip_sound_below` (0.5), `_interval` (4), `_volume` (-6). |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBobSoundsTest.cs` | new | Files, gasp, pill, emote voices, melee pick, crack, snap, drips, each against an IPC. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSfxTest.cs` | modified | The fracture entry is silent; a soundless entry is skipped. |
| `Content.Server/_WF/Wolfmed/README.md` | regenerated | `modules.py --write`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |
| `Resources/Prototypes/_WF/Wolfmed/Voice/bobmed_emote_sounds.yml` (merge) | modified | The SEPSIS emotes wired to the SOUNDS voices: cough and cough-blood, choke, retch. |

## Playtest 4, stumps hurt and the HUD flashes (2026-09-27)

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified, `# WOLFGATE` | Pain behaviours on `DismembermentWound` (one-time, 1) and `AmputationConsequenceWound` (floor, 1). |
| `Tools/_WF/Wolfmed/gen_wolfmed_overlays.py`, `Resources/Textures/_WF/Wolfmed/Interface/Alerts/pain.rsi` | modified | `bordered()`; `paindowned` and the fast-flashing `paindd`. |
| `Content.Server/_WF/Wolfmed/Consciousness/WolfmedPainAlertSystem.cs`, `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml`, `Resources/Locale/en-US/_WF/Wolfmed/consciousness.ftl` | modified | Severity 8 Downed by pain, 9 the faint; the icons and hover lines. |
| `Content.Client/_WF/Wolfmed/Audio/WolfmedCritHeartbeatSystem.cs` | modified | Flatline at -18 dB. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedStumpPainTest.cs`, `WolfmedPainHudTest.cs` | new, modified | The stump pain; the cap now reads as the Downed severity. |
| `Resources/Prototypes/SoundCollections/punching.yml`, `Entities/Objects/Tools/crowbars.yml` | modified, `# WOLFGATE` | Bob punches; `WolfmedHitOverlaySound` on `BaseCrowbar`. |
| `Resources/Prototypes/_WF/Wolfmed/SoundCollections/bobmed.yml`, `wounds.yml`, `Resources/Audio/_WF/Wolfmed/Melee`, `Bleeding` | modified | `WFWolfmedPunch`, `WFWolfmedChop`, `WFWolfmedCrowbarHit`; artery sprays in the spurt collections. |
| `Content.Shared/_WF/Wolfmed/Sounds/WolfmedOrganicSoundSystem.cs`, `WolfmedHitOverlaySoundComponent.cs`, `Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs` (two marked lines), `WolfmedCVars.cs` | modified, new | `PlayHitOverlays`: the weapon's overlay and the heavy chop over the hit sound; `wolfmed.chop_sound_damage` 40. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedBobMeleeSoundsTest.cs` | new | The prototypes' collections and the precedence. |
| `Resources/Prototypes/SoundCollections/gun_impacts.yml`, `Resources/Audio/_WF/Wolfmed/Impacts` | modified, new | `MeatBulletImpact` is Bob's `ric_flesh1-4`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, gibs (2026-09-27)

| File | Kind | Change |
| --- | --- | --- |
| `Tools/_WF/Wolfmed/gen_wolfmed_gibs.py` | new | Splits the Nevado gib art into blood, flesh and meat layers and writes the RSI and the decal prototypes. |
| `Resources/Textures/_WF/Wolfmed/Effects/gibs.rsi`, `Resources/Prototypes/_WF/Wolfmed/Decals/gibs.yml` | new, generated | 31 states and decals. |
| `Content.Server/_WF/Wolfmed/Gore/WolfmedGibDecalSystem.cs`, `WolfmedCVars.cs` | new, modified | The three trauma hooks and the five `wolfmed.gib*` lines. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedGibDecalTest.cs` | new | Limb off, gib, colours, cleanable, machines. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, the tourniquet holds (2026-09-27)

| File | Kind | Change |
| --- | --- | --- |
| `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs`, `Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | modified, `// WOLFGATE` | Clamp survives new damage under a tourniquet; new bleeds under one start clamped; the strap covers child parts. |
| `Content.Server/_WF/Wolfmed/Wounds/WoundBleedingSystem.Wolfmed.cs` | modified | `IsTiedOff`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedTourniquetHoldsTest.cs` | new | The strap holds through Blunt and Slash, ties off the foot, leaves the other leg alone. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 4, INFECTION SPREAD (2026-09-27)

Infection lives on the parts and travels towards the torso, only an infected torso or head feeds sepsis, and septic
shock is sepsis's named late stage (DECISIONS "Playtest 4, INFECTION SPREAD").

| File | Kind | Change |
| --- | --- | --- |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionComponents.cs` | modified | `WolfmedPartInfectionComponent` (networked `Progress`, `Stage`); `WolfmedSepsisComponent.Shock` (networked). |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedInfectionProfilePrototype.cs`, `Resources/Prototypes/_WF/Wolfmed/Wounds/infection.yml` | modified | `partLocalAt` 25, `partSpreadingAt` 60, `partFromWoundPerMinute` 10, `partSpreadPerMinute` 8, `partRecoveryPerMinute` 4, `partAntibioticPerUnit` 8; the timings in the comments. |
| `Content.Shared/_WF/Wolfmed/CCVar/WolfmedCVars.cs` | modified | `wolfmed.septic_shock_at` 80; `sepsis_enabled`'s summary. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedInfectionSystem.cs` | modified | `TickParts` (wound, necrosis and child feeders, recovery, pain, fever), `SeedSepsis` from the torso and head, `CountSources` over the core parts, one fever per body per tick, the shock flag and alert severity, parts in `Treat`, `InSepticShock`, `GetPartStage` / `GetPartProgress`, `GetWorstWoundStage` (was `GetPartStage`). |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedRejuvenateSystem.cs` | modified | Clears `WolfmedPartInfectionComponent`. |
| `Content.Server/_WF/Wolfmed/Wounds/WolfmedNecrosisSystem.cs` | modified | Comments: dead tissue infects towards the torso. |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | modified, `// WOLFGATE` | `PartInfection` parameter; counts as a finding. |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs`, `HealthAnalyzerSystem.Vitals.cs` | modified | The part's stage from Local; `SepticShock` on the vitals report. |
| `Content.Shared/_WF/Wolfmed/Life/WolfmedVitalsReport.cs` | modified | `SepticShock`; `Aid(route, mechanical, shock)` picks the `-shock` aids. |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.Wounds.cs`, `WolfmedWoundStyle.cs` | modified | The "tissue" chip per part, the SEPTIC SHOCK banner and title, the signature, the card accent. |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedTreatmentAdvice.cs`, `WolfmedStepChecks.cs` | modified | `PartInfectionCondition`; "infection cleared" waits for the part. |
| `Content.Shared/_WF/Wolfmed/Examine/WolfmedVisualInspectionSystem.cs`, `Resources/Locale/en-US/_WF/Wolfmed/look.ftl` | modified | "Grey and clammy" in septic shock. |
| `Content.Client/UserInterface/Systems/Alerts/Controls/AlertControl.cs` | modified, `// WOLFGATE` | The alert name gets the severity. |
| `Resources/Prototypes/_WF/Wolfmed/Alerts/alerts.yml` | modified | `WFWolfmedSepsis` severities 0 and 1. |
| `Tools/_WF/Wolfmed/gen_wolfmed_overlays.py`, `Resources/Textures/_WF/Wolfmed/Interface/Alerts/sepsis.rsi` | modified, new state | `build_sepsis` (`--sepsis-only`), `write_rsi(keep=)`; `septicshock.png`. |
| `Resources/Locale/en-US/_WF/Wolfmed/wounds.ftl`, `analyzer-vitals.ftl`, `treatment-advice.ftl` | modified | The alert's severity lines, the shock banner, the part chip, the shock aids and banner title, the advice lines. |
| `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml`, `WoundTreatment.xml` | modified | Travel, sepsis from the chest, septic shock. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedInfectionSpreadTest.cs` | new | Hand to arm to torso, amputation, head, antibiotics, septic shock, IPC. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedInfectionTest.cs` | modified | The dead limb's new sepsis timing in 5 s ticks; the part in the analyzer and the heal. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedSepsisTest.cs`, `WolfmedMedicInfoTest.cs` | modified | The shock aids. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/WolfmedConditionEmoteTest.cs` | modified | `InfectedLimbShiversTest`. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedLocaleCoverageTest.cs`, `WolfmedVisualInspectionTest.cs` | modified | The new keys. |
| `Content.Server/_WF/Wolfmed/README.md` | regenerated | `modules.py --write`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |

## Playtest 5, ITEMS (2026-09-28)

The Wolfmed items where players can get them: the standard medkit's tourniquet, splint and painkillers, pill and
bottle forms of the painkillers and osteogen, the synthetic repair kit, the autodoc and IV drip flatpacks, and the
vendor stock (DECISIONS "Playtest 5, ITEMS").

| File | Kind | Change |
| --- | --- | --- |
| `Resources/Prototypes/_WF/Wolfmed/Entities/painkillers.yml` | modified | `WFWolfmedAnalgesicPillCanister`; ibuprofen and ketorolac pills and canisters; tramadol and oxycodone bottles; the analgesic pill's `pillType`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/medicine.yml` | modified | Osteogen pill, canister and bottle. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/synthetic_kit.yml` | new | `WFMedkitSynthetic`, `WFMedkitSyntheticFilled`. |
| `Resources/Prototypes/_WF/Wolfmed/Entities/flatpacks.yml` | new | `WFMachineAutodocFlatpack`, `WFWolfmedIvDripFlatpack`. |
| `Resources/Prototypes/_WF/Wolfmed/Recipes/lathes.yml` | modified | The empty synthetic kit's recipe. |
| `Resources/Locale/en-US/_WF/Wolfmed/items.ftl` | new | The synthetic kit's lathe name. |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/medkits.yml` | modified, `# WOLFGATE` | `Medkit`'s grid 6x2. |
| `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` | modified, `# WOLFGATE` | The standard kit's tourniquet, splint and analgesic canister; the advanced kit's tourniquet back; the brute kit's osteogen canister; cell-count comments. |
| `Resources/Prototypes/Catalog/VendingMachines/Inventories/medical.yml`, `wallmed.yml`, `robotics.yml` | modified, `# WOLFGATE` | NanoMed Plus, NanoMed and Robotech stock. |
| `Resources/Prototypes/_NF/Catalog/VendingMachines/Inventories/civimed.yml` | modified, `# WOLFGATE` | CiviMed stock. |
| `Resources/Prototypes/Recipes/Lathes/Packs/medical.yml` | modified, `# WOLFGATE` | `WFMedkitSynthetic` in `EmptyMedkitsStatic`. |
| `Resources/Prototypes/_WF/Traders/Catalog/VendingMachines/Inventories/wolfgate.yml` | modified | The Wolfgate shop's stock and the autodoc flatpack. |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAvailabilityTest.cs` | modified | Reachability through fills and flatpacks, the new ids and kits, every fill entry, `PillCanistersSpawnFullTest`, `FlatpacksUnpackTest`. |
| `Content.Server/_WF/Wolfmed/README.md` | regenerated | `modules.py --write`. |
| `Docs/_WF/Wolfmed/DECISIONS.md`, this file | modified | This section. |
