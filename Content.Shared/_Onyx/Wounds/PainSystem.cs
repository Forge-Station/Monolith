using System.Linq;
using Content.Shared.Body.Part;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Rejuvenate;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): Onyx's TryUpdateParalyzeDuration is a compat extension here.
using Content.Shared.Traits.Assorted;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class PainSystem : EntitySystem
{
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Content.Shared._WF.Wolfmed.Consciousness.WolfmedBodyPainSystem _wolfmedPain = default!; // WOLFGATE(Wolfmed): M1a

    // WOLFGATE(Wolfmed): M1a: the shock's threshold, re-arm and adrenaline length are CVars now
    // (WolfmedBodyPainSystem), and adrenaline no longer multiplies pain by 0.7 (OD5).
    private static readonly TimeSpan PainShockStunTime = TimeSpan.FromSeconds(2f);
    private float _recoveryAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PainShockTargetComponent, ComponentStartup>(OnPainShockTargetStartup);
        SubscribeLocalEvent<PainComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnPainShockTargetStartup(Entity<PainShockTargetComponent> entity, ref ComponentStartup args)
    {
        if (_net.IsServer &&
            TryComp(entity, out PainComponent? pain) &&
            TryComp(entity, out MobStateComponent? mobState))
            UpdatePainShock((entity.Owner, pain), mobState, entity.Comp);
    }

    public void ApplyOneTimePain(EntityUid part, EntityUid woundUid, FixedPoint2? delta = null)
    {
        if (!_net.IsServer || !CanFeelPain(part) || !TryComp(woundUid, out WoundComponent? wound))
            return;

        if (!_prototypes.TryIndex(wound.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(wound.Severity, out WoundPainBehavior behavior) || !behavior.OneTime)
            return;

        if (behavior.MinSeverity is { } minimum && wound.Severity < minimum)
            return;

        var amount = delta ?? wound.Severity;
        if (amount <= FixedPoint2.Zero)
            return;

        ChangePain((part, EnsureComp<PainComponent>(part)), amount * behavior.PainPerSeverity);
    }

    /// <summary>
    /// Recomputes the pain floor produced by the part's wounds. The floor is the minimum
    /// pain the part settles to while a wound is present; it does not add pain instantly
    /// (damage already does). When a wound is removed the floor drops and normal recovery
    /// decays the remaining pain down to it (accelerated decay of the wound source).
    /// </summary>
    public void RefreshWoundPain(Entity<WoundableComponent?> part)
    {
        if (!_net.IsServer || !Resolve(part, ref part.Comp, false) ||
            !TryComp(part, out PainComponent? pain))
            return;

        if (!CanFeelPain(part))
        {
            pain.WoundPain = FixedPoint2.Zero;
            SetPain((part.Owner, pain), FixedPoint2.Zero);
            Dirty(part.Owner, pain);
            return;
        }

        var floor = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                !prototype.TryGetBehavior(wound.Comp.Severity, out WoundPainBehavior behavior))
                continue;

            if (behavior.MinSeverity is { } minimum && wound.Comp.Severity < minimum)
                continue;

            if (behavior.OneTime)
                continue;

            floor += wound.Comp.Severity * behavior.PainPerSeverity;
        }

        if (TryComp(part.Owner, out BodyPartComponent? bodyPart) && bodyPart.Body is { } body)
        {
            // WOLFGATE(Wolfmed): `new ModifyPainGainEvent()` binds to the record struct's implicit parameterless
            // constructor, which zeroes Multiplier instead of taking the primary constructor's `= 1f`
            // default - every pain gain was being multiplied by zero. Pass the default explicitly.
            var ev = new ModifyPainGainEvent(1f);
            RaiseLocalEvent(body, ref ev);
            floor *= ev.Multiplier;
        }

        if (pain.WoundPain == floor)
            return;

        _wolfmedPain.FloorChanged(part.Owner, pain.WoundPain, floor); // WOLFGATE(Wolfmed): playtest 5, the floor settles
        pain.WoundPain = floor;
        Dirty(part, pain);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        _recoveryAccumulator += frameTime;
        if (_recoveryAccumulator < 1f)
            return;

        var elapsed = _recoveryAccumulator;
        _recoveryAccumulator = 0f;

        var suppressionQuery = EntityQueryEnumerator<PainComponent>();
        while (suppressionQuery.MoveNext(out var suppressionUid, out var suppressionPain))
            DecayPainSuppression((suppressionUid, suppressionPain), elapsed);

        var bodyQuery = EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>();
        while (bodyQuery.MoveNext(out var bodyUid, out var bodyPain, out var mobState, out var shockTarget))
        {
            if (shockTarget.AdrenalineEnds is { } adrenalineEnds && _timing.CurTime >= adrenalineEnds)
            {
                shockTarget.AdrenalineEnds = null;
                Dirty(bodyUid, shockTarget);
                _wolfmedPain.AdrenalineChanged(bodyUid, false); // WOLFGATE(Wolfmed): M1a: OD5, pain never moved
            }

            UpdatePainShock((bodyUid, bodyPain), mobState, shockTarget);
        }

        var recoveryQuery = EntityQueryEnumerator<PainComponent>();
        while (recoveryQuery.MoveNext(out var partUid, out var partPain))
        {
            if (TryComp(partUid, out BodyPartComponent? part))
                RecoverPain((partUid, partPain), elapsed, part);
        }
    }

    public FixedPoint2 GetPain(Entity<PainComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return FixedPoint2.Zero;

        // WOLFGATE(Wolfmed): M1a: OD5, adrenaline no longer takes 30% off every reading; it no longer stands anyone up.
        return GetPainBeforeAdrenaline((entity.Owner, entity.Comp));
    }

    private FixedPoint2 GetPainBeforeAdrenaline(Entity<PainComponent> entity)
    {
        if (IsPainNumb(entity.Owner))
            return FixedPoint2.Zero;

        var suppression = entity.Comp.Suppression;
        if (TryComp(entity, out BodyPartComponent? part) && part.Body is { } body &&
            TryComp(body, out PainComponent? bodyPain) && bodyPain.Value > FixedPoint2.Zero)
        {
            // WOLFGATE(Wolfmed): M1a: P13, the share is of the sum of the parts, not of the capped body value, so the
            // body's suppression is taken off once in total rather than once per part.
            var share = _wolfmedPain.SuppressionShare((entity.Owner, entity.Comp), body);
            suppression += bodyPain.Suppression * share;
        }

        return FixedPoint2.Max(FixedPoint2.Zero, entity.Comp.Value - suppression);
    }

    public FixedPoint2 GetRawPain(Entity<PainComponent?> entity)
    {
        return Resolve(entity, ref entity.Comp, false) ? entity.Comp.Value : FixedPoint2.Zero;
    }

    public bool SetPain(Entity<PainComponent?> entity, FixedPoint2 value)
    {
        if (!_net.IsServer || !Resolve(entity, ref entity.Comp, false))
            return false;

        // WOLFGATE(Wolfmed): M1a: P13, a body's pain is min(soft cap, sum of its parts) after every change, direct
        // sets included. The part branch below still calls this for the body; the value is rederived.
        if (_wolfmedPain.TryGetDerivedPain(entity.Owner, out var derived))
            value = derived;

        value = FixedPoint2.Clamp(value, FixedPoint2.Zero, entity.Comp.SoftPainCap);
        var old = entity.Comp.Value;
        if (old == value)
            return false;

        var oldPain = GetPain(entity);
        entity.Comp.Value = value;
        Dirty(entity);

        if (TryComp(entity, out BodyPartComponent? part) && part.Body is { } body &&
            TryComp(body, out PainComponent? bodyPain))
            SetPain((body, bodyPain), bodyPain.Value + value - old);

        RaisePainChanged(entity.Owner, entity.Comp, oldPain);
        return true;
    }

    public bool ChangePain(Entity<PainComponent?> entity, FixedPoint2 delta)
    {
        if (!Resolve(entity, ref entity.Comp, false) || delta == FixedPoint2.Zero)
            return false;

        if (delta > FixedPoint2.Zero)
        {
            var target = entity.Owner;
            if (TryComp(entity, out BodyPartComponent? part) && part.Body is { } body)
                target = body;

            // WOLFGATE(Wolfmed): `new ModifyPainGainEvent()` binds to the record struct's implicit parameterless
            // constructor, which zeroes Multiplier instead of taking the primary constructor's `= 1f`
            // default - every pain gain was being multiplied by zero. Pass the default explicitly.
            var ev = new ModifyPainGainEvent(1f);
            RaiseLocalEvent(target, ref ev);
            delta *= ev.Multiplier;
        }

        return SetPain(entity, entity.Comp.Value + delta);
    }

    public bool RecoverPain(Entity<PainComponent?> entity, float seconds, BodyPartComponent? part = null)
    {
        if (!Resolve(entity, ref entity.Comp, false) || seconds <= 0f ||
            entity.Comp.Value <= FixedPoint2.Zero || entity.Comp.RecoveryPerSecond <= FixedPoint2.Zero)
            return false;

        // WOLFGATE(Wolfmed): playtest 5, a floor that has not risen for a while settles toward wolfmed.pain_floor_rest
        var minimum = _wolfmedPain.SettledFloor(entity.Owner, FixedPoint2.Max(entity.Comp.WoundPain, FixedPoint2.Zero));

        var recovery = entity.Comp.RecoveryPerSecond;
        if (Resolve(entity, ref part, false) && part.Body is { } body &&
            TryComp(body, out PainComponent? bodyPain) && bodyPain.Value > FixedPoint2.Zero)
        {
            var suppressedPain = FixedPoint2.Min(bodyPain.Suppression, bodyPain.Value);
            var suppressionRatio = suppressedPain.Float() / bodyPain.Value.Float();
            recovery *= 1f + (GetRecoveryMultiplier(bodyPain) - 1f) * suppressionRatio;
        }

        // WOLFGATE(Wolfmed): playtest 4, the share of the pain no open wound backs (Value over WoundPain) fades at
        // wolfmed.pain_loose_recovery a second, so a treated wound stops hurting within a minute; the floor holds.
        var loose = _wolfmedPain.LooseRecovery;
        if (loose > FixedPoint2.Zero && entity.Comp.Value > minimum)
            recovery = FixedPoint2.Max(recovery, loose);

        var recovered = FixedPoint2.Max(minimum, entity.Comp.Value - recovery * seconds);
        return SetPain(entity, recovered);
    }

    private void UpdatePainShock(Entity<PainComponent> entity, MobStateComponent mobState,
        PainShockTargetComponent shockTarget)
    {
        if (mobState.CurrentState == MobState.Dead)
        {
            if (shockTarget.Armed)
            {
                shockTarget.Armed = false;
                Dirty(entity.Owner, shockTarget);
            }
            return;
        }

        var pain = GetPain((entity.Owner, entity.Comp));
        var rearmPain = GetPainBeforeAdrenaline(entity);

        if (rearmPain < _wolfmedPain.ShockRearm) // WOLFGATE(Wolfmed): M1a: CVar
        {
            if (!shockTarget.Armed)
            {
                shockTarget.Armed = true;
                Dirty(entity.Owner, shockTarget);
            }
            return;
        }

        if (!shockTarget.Armed || pain < _wolfmedPain.ShockThreshold) // WOLFGATE(Wolfmed): M1a: CVar
            return;

        if (!_stun.TryUpdateParalyzeDuration(entity, PainShockStunTime))
            return;

        shockTarget.Armed = false;
        Dirty(entity.Owner, shockTarget);

        _chat.TryEmoteWithChat(entity, "Scream", ChatTransmitRange.HideChat,
            ignoreActionBlocker: true, forceEmote: true);
        _jitter.DoJitter(entity, PainShockStunTime, true, 20f, 7f);
        ApplyPainShockAdrenaline(entity);
        // WOLFGATE(Wolfmed): BRAIN: a shock on a body that has already bled out stops the heart.
        var shocked = new Content.Shared._WF.Wolfmed.Life.WolfmedPainShockEvent(entity.Owner);
        RaiseLocalEvent(ref shocked);
    }

    private void OnRejuvenate(Entity<PainComponent> entity, ref RejuvenateEvent args)
    {
        if (_net.IsServer)
        {
            SetPain(entity.AsNullable(), FixedPoint2.Zero);
            ClearPainSuppression(entity.AsNullable());
            if (TryComp(entity, out PainShockTargetComponent? shockTarget))
            {
                shockTarget.Armed = true;
                shockTarget.AdrenalineEnds = null;
                Dirty(entity.Owner, shockTarget);
            }
        }
    }

    private bool IsPainNumb(EntityUid entity)
    {
        if (TryComp(entity, out BodyPartComponent? part) && part.Body is { } body)
            entity = body;

        // WOLFGATE(Wolfmed): P2-D8: Wolfgate's PainNumbness trait grants the legacy PainNumbnessComponent
        // (Content.Shared/Traits/Assorted/PainNumbnessComponent.cs); Onyx's status-effect form
        // (StatusEffectPainNumbness) has no applier here — TraitPrototype has no `specials:`, and the
        // narcotics that apply it are phase 4. Honour both.
        if (HasComp<PainNumbnessComponent>(entity))
            return true;

        return _statusEffects.EnumerateStatusEffects<PainNumbnessStatusEffectComponent>(entity)
            .Any(effect => effect.Comp1.Applied);
    }

    public bool SuppressPain(Entity<PainComponent?> entity, string identifier, FixedPoint2 amount,
        TimeSpan decayDuration, float recoveryMultiplier = 1f)
    {
        if (!_net.IsServer || string.IsNullOrWhiteSpace(identifier) || amount <= FixedPoint2.Zero ||
            decayDuration <= TimeSpan.Zero || !float.IsFinite(recoveryMultiplier) || recoveryMultiplier < 1f ||
            !Resolve(entity, ref entity.Comp, false))
            return false;

        var accumulated = amount;
        if (entity.Comp.SuppressionModifiers.TryGetValue(identifier, out var current))
        {
            accumulated += current.Amount;
            recoveryMultiplier = Math.Max(recoveryMultiplier, current.RecoveryMultiplier);
        }

        entity.Comp.SuppressionModifiers[identifier] = new PainSuppressionModifier(
            accumulated,
            accumulated / (float) decayDuration.TotalSeconds,
            recoveryMultiplier);
        RefreshSuppression((entity.Owner, entity.Comp));
        return true;
    }

    public bool ClearPainSuppression(Entity<PainComponent?> entity)
    {
        if (!_net.IsServer || !Resolve(entity, ref entity.Comp, false) ||
            entity.Comp.SuppressionModifiers.Count == 0 && entity.Comp.Suppression == FixedPoint2.Zero)
            return false;

        var oldPain = GetPain(entity);
        entity.Comp.SuppressionModifiers.Clear();
        entity.Comp.Suppression = FixedPoint2.Zero;
        Dirty(entity);
        RaisePainChanged(entity.Owner, entity.Comp, oldPain);
        return true;
    }

    public bool DecayPainSuppression(Entity<PainComponent?> entity, float seconds)
    {
        if (!_net.IsServer || seconds <= 0f || !Resolve(entity, ref entity.Comp, false) ||
            entity.Comp.SuppressionModifiers.Count == 0)
            return false;

        foreach (var (identifier, modifier) in entity.Comp.SuppressionModifiers.ToArray())
        {
            var amount = FixedPoint2.Max(FixedPoint2.Zero, modifier.Amount - modifier.DecayPerSecond * seconds);
            if (amount == FixedPoint2.Zero)
                entity.Comp.SuppressionModifiers.Remove(identifier);
            else
                entity.Comp.SuppressionModifiers[identifier] = modifier with { Amount = amount };
        }

        RefreshSuppression((entity.Owner, entity.Comp));
        return true;
    }

    public bool ApplyDamage(EntityUid part, DamageSpecifier delta, BodyPartComponent? bodyPart = null,
        PainComponent? pain = null)
    {
        if (!CanFeelPain(part) || !Resolve(part, ref bodyPart, ref pain, false))
            return false;

        var change = CalculatePain(delta, pain.DamageMultipliers);
        return change != FixedPoint2.Zero && ChangePain((part, pain), change);
    }

    public bool CanFeelPain(EntityUid part)
    {
        return !TryComp(part, out WoundableComponent? woundable) ||
               !_prototypes.TryIndex(woundable.Profile, out var profile) ||
               profile.CanFeelPain;
    }

    public FixedPoint2 CalculatePain(DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, float> painMultipliers)
    {
        var painfulDamage = FixedPoint2.Zero;
        foreach (var (type, amount) in damage.DamageDict)
            if (amount > FixedPoint2.Zero && painMultipliers.TryGetValue(type, out var multiplier))
                painfulDamage += amount * multiplier;

        return FixedPoint2.Max(FixedPoint2.Zero, painfulDamage);
    }

    private void RefreshSuppression(Entity<PainComponent> entity)
    {
        var oldPain = GetPain((entity.Owner, entity.Comp));
        var value = FixedPoint2.Zero;
        foreach (var modifier in entity.Comp.SuppressionModifiers.Values)
            value += modifier.Amount;

        if (entity.Comp.Suppression == value)
            return;

        entity.Comp.Suppression = value;
        Dirty(entity);
        RaisePainChanged(entity.Owner, entity.Comp, oldPain);
    }

    private void RaisePainChanged(EntityUid uid, PainComponent component, FixedPoint2 oldPain)
    {
        var pain = GetPain((uid, component));
        if (pain == oldPain)
            return;

        var changed = new PainChangedEvent(uid, oldPain, pain);
        RaiseLocalEvent(uid, ref changed);

        if (TryComp(uid, out MobStateComponent? mobState) &&
            TryComp(uid, out PainShockTargetComponent? shockTarget))
            UpdatePainShock((uid, component), mobState, shockTarget);
    }

    private void ApplyPainShockAdrenaline(Entity<PainComponent> entity)
    {
        if (!TryComp(entity, out PainShockTargetComponent? shockTarget))
            return;

        // WOLFGATE(Wolfmed): M1a: OD5, adrenaline leaves pain alone; it speeds the crawl instead (WolfmedBodyPainSystem).
        shockTarget.AdrenalineEnds = _timing.CurTime + _wolfmedPain.AdrenalineTime;
        Dirty(entity.Owner, shockTarget);
        _wolfmedPain.AdrenalineChanged(entity.Owner, true);
    }

    private static float GetRecoveryMultiplier(PainComponent pain)
    {
        if (pain.Suppression <= FixedPoint2.Zero)
            return 1f;

        var weightedMultiplier = 0f;
        foreach (var modifier in pain.SuppressionModifiers.Values)
            weightedMultiplier += modifier.Amount.Float() * modifier.RecoveryMultiplier;

        return Math.Max(1f, weightedMultiplier / pain.Suppression.Float());
    }
}
