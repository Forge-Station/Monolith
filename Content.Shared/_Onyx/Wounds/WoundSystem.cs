using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W1: the wound rule extension point.
using Content.Shared.FixedPoint;
using Content.Shared.Rejuvenate;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<ProtoId<DamageTypePrototype>, List<(WoundPrototype Wound, WoundDamageTypeSettings Settings)>>
        _woundsByDamageType = new();

    public override void Initialize()
    {
        base.Initialize();
        RebuildDamageTypeCache();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<WoundableComponent, ComponentInit>(OnWoundableInit);
        // WOLFGATE(Wolfmed): D17 - <BodyComponent, RejuvenateEvent> is owned by _Mono/Body/Systems/BodyRejuvenateSystem.cs:28.
        // The handler body below is exposed as ClearBodyWounds and called from WoundDamageProjectionSystem.OnRejuvenate.
        SubscribeLocalEvent<WoundableComponent, RejuvenateEvent>(OnPartRejuvenate);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WoundPrototype>())
            RebuildDamageTypeCache();
    }

    private void RebuildDamageTypeCache()
    {
        _woundsByDamageType.Clear();
        foreach (var prototype in _prototypes.EnumeratePrototypes<WoundPrototype>())
        {
            foreach (var (type, settings) in prototype.DamageTypes)
            {
                if (!_woundsByDamageType.TryGetValue(type, out var wounds))
                    _woundsByDamageType[type] = wounds = new();

                wounds.Add((prototype, settings));
            }
        }
    }

    private void OnWoundableInit(Entity<WoundableComponent> part, ref ComponentInit args)
    {
        part.Comp.WoundsContainer = _containers.EnsureContainer<Container>(part, WoundableComponent.ContainerId);
    }

    // WOLFGATE(Wolfmed): D13 puts OrganDamageSystem (the sole caller) in Content.Server, so internal no longer reaches it.
    public void HandlePartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        if (!_net.IsServer)
            return;

        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            if (amount == FixedPoint2.Zero || !_woundsByDamageType.TryGetValue(type, out var wounds))
                continue;

            // WOLFGATE(Wolfmed): W1: Wolfmed's wound rules answer here and may create a cause-specific wound
            // (gunshot, lodged round, shrapnel) in place of the default one for this damage type.
            if (amount > FixedPoint2.Zero)
            {
                var selection = new WolfmedWoundSelectionEvent(args.Body, part.Owner, type, amount,
                    args.Origin, args.Tool, args.IsExplosion, args.WoundSeverityMultiplier);
                RaiseLocalEvent(part.Owner, ref selection);
                if (selection.SuppressDefault)
                    continue;
            }

            foreach (var (prototype, settings) in wounds)
            {
                if (settings.SeverityMultiplier <= 0f)
                    continue;

                var explosionMultiplier = args.IsExplosion &&
                    (type == "Blunt" || type == "Slash" || type == "Piercing" || type == "Heat" || type == "Cold")
                    ? args.WoundSeverityMultiplier
                    : 1f;
                var severity = amount * settings.SeverityMultiplier * explosionMultiplier;

                if (amount > FixedPoint2.Zero)
                {
                    // WOLFGATE(Wolfmed): W1: rule-only wounds are created by WolfmedWoundRuleSystem, not by damage type.
                    if (prototype.RuleOnly || !CanCreateWound(part.AsNullable(), prototype.ID, type))
                        continue;

                    CreateOrMergeWoundInternal(part.Owner, prototype, severity,
                        amount >= FixedPoint2.Max(FixedPoint2.Zero, settings.ReopenMinimumDamage));
                }
                else if (args.HealWounds)
                    HealWounds(part, prototype, -severity);
            }
        }
    }

    public bool CanBleed(Entity<WoundableComponent?> part)
    {
        if (!Resolve(part, ref part.Comp, false) ||
            !_prototypes.TryIndex(part.Comp.Profile, out var profile))
            return true;

        return profile.BleedingMultiplier > 0f;
    }

    public bool CanCreateWound(Entity<WoundableComponent?> part, ProtoId<WoundPrototype> prototype,
        ProtoId<DamageTypePrototype>? damageType = null)
    {
        if (!Resolve(part, ref part.Comp, false) || !_prototypes.TryIndex(part.Comp.Profile, out var profile))
            return false;

        if (damageType is { } type &&
            profile.AcceptedDamageTypes.Count != 0 &&
            !profile.AcceptedDamageTypes.Contains(type))
            return false;

        return profile.SupportedWounds.Count == 0 || profile.SupportedWounds.Contains(prototype);
    }

    // WOLFGATE(Wolfmed): D17 - was OnRejuvenate(Entity<BodyComponent>, ref RejuvenateEvent); now a public entry point.
    /// <summary>Clears every wound and severable flag on a wound host's parts.</summary>
    public void ClearBodyWounds(EntityUid body)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            ClearWounds(part);
            if (TryComp(part, out WoundableComponent? woundable) &&
                (woundable.Severable || woundable.AmputationOverflow != FixedPoint2.Zero))
            {
                woundable.Severable = false;
                woundable.AmputationOverflow = FixedPoint2.Zero;
                Dirty(part, woundable);
            }
        }
    }

    private void OnPartRejuvenate(Entity<WoundableComponent> part, ref RejuvenateEvent args)
    {
        if (_net.IsServer)
        {
            ClearWounds(part.Owner);
            part.Comp.Severable = false;
            part.Comp.AmputationOverflow = FixedPoint2.Zero;
            Dirty(part);

            // WOLFGATE(Wolfmed): W5: a part healed on its own keeps its dead tissue and its tourniquet otherwise.
            var rejuvenated = new WolfmedRejuvenateEvent(part.Owner);
            RaiseLocalEvent(ref rejuvenated);
        }
    }

    public IEnumerable<Entity<WoundComponent>> GetWounds(Entity<WoundableComponent?> part)
    {
        if (!Resolve(part, ref part.Comp, false) ||
            !_containers.TryGetContainer(part.Owner, WoundableComponent.ContainerId, out var container))
            yield break;

        foreach (var wound in container.ContainedEntities)
            if (TryComp(wound, out WoundComponent? component))
                yield return (wound, component);
    }

    public EntityUid? CreateOrMergeWound(
        Entity<WoundableComponent?> part,
        ProtoId<WoundPrototype> prototypeId,
        FixedPoint2 severity)
    {
        return CreateOrMergeWoundInternal(part, prototypeId, severity, true);
    }

    private EntityUid? CreateOrMergeWoundInternal(
        Entity<WoundableComponent?> part,
        ProtoId<WoundPrototype> prototypeId,
        FixedPoint2 severity,
        bool reopen)
    {
        if (!_net.IsServer || severity <= FixedPoint2.Zero ||
            !Resolve(part, ref part.Comp, false) || !CanCreateWound(part, prototypeId) ||
            !_prototypes.TryIndex(prototypeId, out var prototype))
            return null;

        if (prototype.MergeMode == WoundMergeMode.MergeByPrototype)
        {
            foreach (var wound in GetWounds(part))
            {
                if (wound.Comp.Prototype == prototypeId && wound.Comp.State is not WoundState.Healed and not WoundState.Scarred)
                {
                    if (reopen && wound.Comp.State != WoundState.Open)
                        SetWoundState(wound.Owner, WoundState.Open);
                    ChangeSeverity(wound.Owner, severity);
                    return wound;
                }
            }
        }

        var woundId = Spawn(null, MapCoordinates.Nullspace);
        var component = AddComp<WoundComponent>(woundId);
        component.HoldingPart = part;
        component.Prototype = prototypeId;
        component.Severity = FixedPoint2.Min(severity, prototype.MaximumSeverity);
        component.PeakSeverity = component.Severity;
        Dirty(woundId, component);

        SyncRuntimeComponents((woundId, component), prototype);

        var woundsContainer = _containers.EnsureContainer<Container>(part.Owner, WoundableComponent.ContainerId);
        part.Comp.WoundsContainer = woundsContainer;
        if (!_containers.Insert(woundId, woundsContainer))
        {
            QueueDel(woundId);
            return null;
        }

        var created = new WoundCreatedEvent(part, woundId, prototypeId);
        RaiseLocalEvent(part, ref created);
        RaiseLocalEvent(woundId, ref created);
        return woundId;
    }

    private void SyncRuntimeComponents(Entity<WoundComponent> wound, WoundPrototype prototype)
    {
        var previousSeverity = wound.Comp.LastSyncSeverity; // WOLFGATE(Wolfmed)
        wound.Comp.LastSyncSeverity = wound.Comp.Severity; // WOLFGATE(Wolfmed)

        if (CanBleed(wound.Comp.HoldingPart) &&
            prototype.TryGetBehavior(wound.Comp.Severity, out WoundBleedingBehavior bleedingBehavior) &&
            bleedingBehavior.Rate > 0f && wound.Comp.Severity >= bleedingBehavior.MinimumSeverity)
        {
            if (!TryComp(wound, out WoundBleedingComponent? bleeding) &&
                wound.Comp.Severity > previousSeverity) // WOLFGATE(Wolfmed): only a wound that just grew rolls, see LastSyncSeverity
            {
                var chance = Math.Clamp(bleedingBehavior.Chance, 0f, 1f);
                if (chance > 0f && _random.Prob(chance))
                {
                    bleeding = AddComp<WoundBleedingComponent>(wound);
                    // WOLFGATE(Wolfmed): a grown wound gets its WoundChangedEvent next, and the bleeding system adds the growth
                    // there. Starting from the old severity keeps that from being counted twice.
                    bleeding.BleedingSeverity = previousSeverity > FixedPoint2.Zero ? previousSeverity : wound.Comp.Severity;
                    Dirty(wound, bleeding);
                }
            }
        }
        else
            RemComp<WoundBleedingComponent>(wound);

        if (prototype.TryGetBehavior(wound.Comp.Severity, out WoundInternalBleedingBehavior internalBehavior) &&
            internalBehavior.Rate > 0f)
        {
            if (!TryComp(wound, out WoundInternalBleedingComponent? internalBleeding))
            {
                var chance = Math.Clamp(internalBehavior.Chance, 0f, 1f);
                if (chance > 0f && _random.Prob(chance))
                    internalBleeding = AddComp<WoundInternalBleedingComponent>(wound);
            }

            if (internalBleeding != null)
            {
                internalBleeding.Rate = internalBehavior.Rate;
                internalBleeding.Severity = wound.Comp.State == WoundState.Open
                    ? wound.Comp.Severity
                    : FixedPoint2.Zero;
                Dirty(wound, internalBleeding);
            }
        }
        else
            RemComp<WoundInternalBleedingComponent>(wound);

        if (prototype.TryGetBehavior(wound.Comp.Severity, out WoundFunctionalityBehavior functionalityBehavior))
        {
            var functionality = EnsureComp<WoundFunctionalityComponent>(wound);
            functionality.State = functionalityBehavior.State;
            Dirty(wound, functionality);
        }
        else
            RemComp<WoundFunctionalityComponent>(wound);
    }

    public void RefreshRuntimeComponents(Entity<WoundComponent?> wound)
    {
        if (!Resolve(wound, ref wound.Comp, false) ||
            !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
            return;

        SyncRuntimeComponents((wound.Owner, wound.Comp), prototype);
    }

    public bool ChangeSeverity(Entity<WoundComponent?> wound, FixedPoint2 delta)
    {
        if (!_net.IsServer || !Resolve(wound, ref wound.Comp, false) ||
            HasComp<WoundScarComponent>(wound) || !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
            return false;

        var old = wound.Comp.Severity;
        var severity = FixedPoint2.Min(prototype.MaximumSeverity, FixedPoint2.Max(FixedPoint2.Zero, old + delta));
        if (severity == old)
            return false;

        if (severity == FixedPoint2.Zero)
        {
            wound.Comp.Severity = FixedPoint2.Zero;
            Dirty(wound);
            var healed = new WoundChangedEvent(wound.Comp.HoldingPart, wound, old, FixedPoint2.Zero);
            RaiseLocalEvent(wound.Comp.HoldingPart, ref healed);
            RaiseLocalEvent(wound, ref healed);
            SetWoundState(wound, WoundState.Healed);
            return RemoveWound(wound);
        }

        wound.Comp.Severity = severity;
        wound.Comp.PeakSeverity = FixedPoint2.Max(wound.Comp.PeakSeverity, severity);
        Dirty(wound);
        SyncRuntimeComponents((wound.Owner, wound.Comp), prototype);
        var changed = new WoundChangedEvent(wound.Comp.HoldingPart, wound, old, severity);
        RaiseLocalEvent(wound.Comp.HoldingPart, ref changed);
        RaiseLocalEvent(wound, ref changed);
        return true;
    }

    public bool TreatWound(Entity<WoundComponent?> wound, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !Resolve(wound, ref wound.Comp, false))
            return false;

        var attempt = new WoundTreatmentAttemptEvent(wound.Comp.HoldingPart, wound, amount);
        RaiseLocalEvent(wound.Comp.HoldingPart, ref attempt);
        RaiseLocalEvent(wound, ref attempt);
        if (attempt.Cancelled)
            return false;

        if (amount < wound.Comp.Severity)
        {
            var changed = ChangeSeverity(wound, -amount);
            if (changed && TryComp(wound, out WoundComponent? remaining))
                SetWoundState((wound.Owner, remaining), WoundState.Stabilized);
            return changed;
        }

        return ChangeSeverity(wound, -amount);
    }

    public bool SetWoundState(Entity<WoundComponent?> wound, WoundState state)
    {
        if (!_net.IsServer || !Resolve(wound, ref wound.Comp, false) || wound.Comp.State == state)
            return false;

        var old = wound.Comp.State;
        wound.Comp.State = state;
        Dirty(wound);
        var changed = new WoundStateChangedEvent(wound.Comp.HoldingPart, wound, old, state);
        RaiseLocalEvent(wound.Comp.HoldingPart, ref changed);
        RaiseLocalEvent(wound, ref changed);
        if (_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
            SyncRuntimeComponents((wound.Owner, wound.Comp), prototype);
        return true;
    }

    public bool CloseWound(Entity<WoundComponent?> wound) => SetWoundState(wound, WoundState.Closed);

    public bool RemoveWound(Entity<WoundComponent?> wound)
    {
        if (!_net.IsServer || !Resolve(wound, ref wound.Comp, false) || HasComp<WoundScarComponent>(wound))
            return false;

        return RemoveWoundInternal((wound.Owner, wound.Comp));
    }

    private bool RemoveWoundInternal(Entity<WoundComponent> wound)
    {
        if (_containers.TryGetContainer(wound.Comp.HoldingPart, WoundableComponent.ContainerId, out var container))
            _containers.Remove(wound.Owner, container);
        var removed = new WoundRemovedEvent(wound.Comp.HoldingPart, wound, wound.Comp.Prototype);
        RaiseLocalEvent(wound.Comp.HoldingPart, ref removed);
        RaiseLocalEvent(wound, ref removed);
        QueueDel(wound);
        return true;
    }

    public void ClearWounds(Entity<WoundableComponent?> part)
    {
        if (!_net.IsServer || !Resolve(part, ref part.Comp, false) ||
            !_containers.TryGetContainer(part.Owner, WoundableComponent.ContainerId, out var container))
            return;

        foreach (var wound in container.ContainedEntities.ToArray())
            if (TryComp(wound, out WoundComponent? component))
                RemoveWoundInternal((wound, component));
    }

    private void HealWounds(Entity<WoundableComponent> part, WoundPrototype prototype, FixedPoint2 amount)
    {
        var remaining = amount * prototype.HealingMultiplier;
        foreach (var wound in GetWounds(part.Owner).ToArray())
        {
            if (remaining <= FixedPoint2.Zero || wound.Comp.Prototype != prototype.ID)
                continue;

            var healed = FixedPoint2.Min(remaining, wound.Comp.Severity);
            // WOLFGATE(Wolfmed): W2: damage removal is a treatment too, so a wound that refuses treatment (an
            // embedded object, an arterial bleed that is still pumping) refuses this path as well.
            // Without this the attempt event only covered TreatWound and the damage side walked past it.
            var attempt = new WoundTreatmentAttemptEvent(part.Owner, wound.Owner, healed);
            RaiseLocalEvent(part.Owner, ref attempt);
            RaiseLocalEvent(wound.Owner, ref attempt);
            if (attempt.Cancelled)
                continue;

            ChangeSeverity(wound.Owner, -healed);
            remaining -= healed;
        }
    }

    public bool TryHealWounds(Entity<WoundableComponent?> part, DamageSpecifier healing,
        IReadOnlySet<string>? allowedStages = null)
    {
        if (!_net.IsServer || !Resolve(part, ref part.Comp, false))
            return false;

        var changed = false;
        foreach (var (type, amount) in healing.DamageDict)
        {
            if (amount >= FixedPoint2.Zero || !_woundsByDamageType.TryGetValue(type, out var wounds))
                continue;

            foreach (var (prototype, settings) in wounds)
            {
                if (settings.SeverityMultiplier <= 0f)
                    continue;

                // WOLFGATE(Wolfmed): an item working on the wound itself closes it at full strength. HealingMultiplier is how much
                // of REMOVED DAMAGE comes off a wound (HandlePartDamageApplied); applied here as well it left a suture
                // closing three severity a use, and a critical gunshot wound outlasting twenty of them. Zero still means
                // "no topical closes this" (charring, a cut tendon, a lodged round).
                var remaining = prototype.HealingMultiplier > 0f
                    ? -amount * settings.SeverityMultiplier
                    : FixedPoint2.Zero;
                foreach (var wound in GetWounds(part).ToArray())
                {
                    if (remaining <= FixedPoint2.Zero || wound.Comp.Prototype != prototype.ID ||
                        !CanTreatStage(wound, prototype, allowedStages))
                        continue;

                    var healed = FixedPoint2.Min(remaining, wound.Comp.Severity);
                    changed |= TreatWound(wound.Owner, healed);
                    remaining -= healed;
                }
            }
        }

        return changed;
    }

    public FixedPoint2 GetHealingPotential(Entity<WoundableComponent?> part, DamageSpecifier healing,
        IReadOnlySet<string>? allowedStages = null)
    {
        if (!Resolve(part, ref part.Comp, false))
            return FixedPoint2.Zero;

        var result = FixedPoint2.Zero;
        foreach (var (type, amount) in healing.DamageDict)
        {
            if (amount >= FixedPoint2.Zero || !_woundsByDamageType.TryGetValue(type, out var wounds))
                continue;

            foreach (var (prototype, settings) in wounds)
            {
                if (settings.SeverityMultiplier <= 0f)
                    continue;

                var available = FixedPoint2.Zero;
                foreach (var wound in GetWounds(part))
                    if (wound.Comp.Prototype == prototype.ID && CanTreatStage(wound, prototype, allowedStages) &&
                        !RefusesTreatment(part.Owner, wound)) // WOLFGATE(Wolfmed): playtest 5, a wound that refuses treatment (a lodged round) is no potential to heal
                        available += wound.Comp.Severity;

                result += FixedPoint2.Min(-amount * settings.SeverityMultiplier * prototype.HealingMultiplier, available);
            }
        }

        return result;
    }

    // WOLFGATE(Wolfmed) START: playtest 5, the refusal HealWounds raises, asked without healing anything.
    // A lodged round, a scar or a pumping artery refuses, and a tool that counted such a wound as work repeated
    // its pass on it until its fuel or its stack was gone.
    private bool RefusesTreatment(EntityUid part, Entity<WoundComponent> wound)
    {
        var attempt = new WoundTreatmentAttemptEvent(part, wound.Owner, wound.Comp.Severity);
        RaiseLocalEvent(part, ref attempt);
        RaiseLocalEvent(wound.Owner, ref attempt);
        return attempt.Cancelled;
    }
    // WOLFGATE END

    private static bool CanTreatStage(Entity<WoundComponent> wound, WoundPrototype prototype,
        IReadOnlySet<string>? allowedStages)
    {
        return allowedStages == null ||
               prototype.GetStage(wound.Comp.Severity) is { } stage && allowedStages.Contains(stage);
    }

}
