using System.Numerics;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Throwing;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE(Wolfmed): D8, Onyx's amputation part fields live on WolfmedBodyPartComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 facade + the TryDetachPart shim.
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): V124, the dismemberment feedback event.

namespace Content.Shared._Onyx.Wounds;

public sealed partial class AmputationSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE(Wolfmed): D12, GetAllDamage lives on the compat facade.
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE(Wolfmed): D8
    [Dependency] private WolfmedBodySystem _wfBody = default!; // WOLFGATE(Wolfmed): Onyx's SharedBodySystem.TryDetachPart

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundableComponent, PartDamageOverflowedEvent>(OnPartDamageOverflowed);
    }

    private void OnPartDamageOverflowed(Entity<WoundableComponent> part, ref PartDamageOverflowedEvent args)
    {
        if (!_net.IsServer ||
            !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            _wfPart.Get(part).MaxDamage <= FixedPoint2.Zero) // WOLFGATE(Wolfmed): D8
            return;

        // WOLFGATE(Wolfmed): EVISC: D9 never severs a torso, so its overflow is offered to evisceration instead.
        if (bodyPart.PartType == BodyPartType.Torso)
        {
            var torso = new WolfmedTorsoOverflowEvent(args.Body, part.Owner, args.Damage, args.IsExplosion);
            RaiseLocalEvent(ref torso);
            return;
        }

        if (args.ExplosionAmputationCandidate && TryExplosionAmputate(args.Body, part, args.Damage)) // WOLFGATE(Wolfmed): P3-D15
            return;

        if (!part.Comp.Severable)
        {
            if (part.Comp.AmputationOverflow >= _wfPart.Get(part).MaxDamage) // WOLFGATE(Wolfmed): D8
            {
                SetSeverable(part, true);
            }
            return;
        }

        if (IsFinishingHit(args.Body, part.Owner, args.Damage)) // WOLFGATE(Wolfmed): P3-D15
            TryAmputate(args.Body, part.Owner);
    }

    public void ApplyAmputationConsequences(EntityUid body, EntityUid parent)
    {
        if (!_net.IsServer || !HasComp<WoundableComponent>(parent) ||
            !TryComp(parent, out BodyPartComponent? parentPart))
            return;

        if (!TryComp(body, out WoundHostComponent? host))
            return;

        // WOLFGATE(Wolfmed): D8. The severity comes off the PARENT the stump is left on, not off the severed part.
        _wounds.CreateOrMergeWound(parent, host.AmputationConsequenceWound, _wfPart.Get(parent).AmputationConsequenceSeverity);
    }

    public void HandlePartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        if (!_net.IsServer)
            return;

        if (!TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            bodyPart.PartType is BodyPartType.Torso || // WOLFGATE(Wolfmed): D9
            _body.GetParentPartOrNull(part) is null || // WOLFGATE(Wolfmed): Shitmed's BodyPartComponent has no Parent field.
            _wfPart.Get(part).AmputationThresholds.Count == 0 || // WOLFGATE(Wolfmed): D8
            !TryComp(part, out DamageableComponent? damageable))
            return;

        // WOLFGATE(Wolfmed): M3: P27, a blast never takes the head unless wolfmed.blast_dismember_head is on.
        if (args.IsExplosion && !_wfPart.BlastMaySever(part))
            return;

        var damage = _damageable.GetAllDamage((part.Owner, damageable));
        if (args.ExplosionAmputationCandidate && TryExplosionAmputate(args.Body, part, args.Damage, damage)) // WOLFGATE(Wolfmed): P3-D15
            return;

        if (!part.Comp.Severable)
        {
            if (ReachedThreshold(damage, _wfPart.Get(part).AmputationThresholds)) // WOLFGATE(Wolfmed): D8
            {
                SetSeverable(part, true);
            }
            return;
        }

        if (GetThresholdProgress(damage, _wfPart.Get(part).AmputationThresholds) < GetResetRatio(args.Body)) // WOLFGATE(Wolfmed): D8
        {
            SetSeverable(part, false);
            return;
        }

        var damageBeforeHit = damage.Clone();
        foreach (var (type, amount) in args.Damage.DamageDict)
            if (amount > FixedPoint2.Zero)
                damageBeforeHit.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero,
                    damageBeforeHit.DamageDict.GetValueOrDefault(type) - amount);

        if (ReachedThreshold(damageBeforeHit, _wfPart.Get(part).AmputationThresholds) && // WOLFGATE(Wolfmed): D8
            IsFinishingHit(args.Body, part.Owner, args.Damage)) // WOLFGATE(Wolfmed): P3-D15
            TryAmputate(args.Body, part.Owner);
    }

    /// <summary>
    /// Detaches the part deterministically and applies amputation consequences.
    /// </summary>
    public bool TryAmputate(EntityUid body, EntityUid part)
    {
        if (!_net.IsServer || !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            bodyPart.PartType == BodyPartType.Torso) // WOLFGATE(Wolfmed): D9
            return false;

        var parent = _body.GetParentPartOrNull(part) ?? part; // WOLFGATE(Wolfmed): Shitmed has no BodyPartComponent.Parent.
        if (!_wfBody.TryDetachPart(part)) // WOLFGATE(Wolfmed): compat shim
            return false;

        if (TryComp(body, out WoundHostComponent? host))
            _wounds.CreateOrMergeWound(parent, host.DismembermentWound,
                _wfPart.Get(part).DismembermentSeverity ?? GetDismembermentSeverity(host, bodyPart.PartType)); // WOLFGATE(Wolfmed): D8
        ApplyAmputationConsequences(body, parent);
        _throwing.TryThrow(part, Vector2.UnitY, baseThrowSpeed: 3f,
            pushbackRatio: 0f, doSpin: true);
        // WOLFGATE(Wolfmed): V124: the one damage-driven detach path, so the dismemberment feedback hangs here
        // rather than on TryDetachPart, which surgery also uses.
        var amputated = new WolfmedPartAmputatedEvent(body, part, parent);
        RaiseLocalEvent(ref amputated);
        return true;
    }

    private static bool ReachedThreshold(
        DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, FixedPoint2> thresholds)
    {
        return GetThresholdProgress(damage, thresholds) >= 1f;
    }

    private static float GetThresholdProgress(
        DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, FixedPoint2> thresholds)
    {
        var progress = 0f;
        foreach (var (type, threshold) in thresholds)
        {
            if (threshold <= FixedPoint2.Zero)
                continue;
            progress += (float) damage.DamageDict.GetValueOrDefault(type) / (float) threshold;
        }
        return progress;
    }

    private bool IsFinishingHit(EntityUid body, EntityUid part, DamageSpecifier damage) // WOLFGATE(Wolfmed): D8/P3-D15
    {
        var wf = _wfPart.Get(part); // WOLFGATE(Wolfmed): D8
        if (!TryComp(body, out WoundHostComponent? host))
            return false;

        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !wf.AmputationThresholds.ContainsKey(type))
                continue;

            var minimum = wf.DismembermentFinishingDamage.GetValueOrDefault(type,
                host.DefaultDismembermentFinishingDamage.GetValueOrDefault(type));
            if (minimum > FixedPoint2.Zero && amount >= minimum)
                return true;
        }

        return false;
    }

    private bool TryExplosionAmputate(
        EntityUid body,
        Entity<WoundableComponent> part,
        // WOLFGATE(Wolfmed): P3-D15 drops Onyx's BodyPartComponent parameter; the data it read is on WolfmedBodyPartComponent.
        DamageSpecifier hit,
        DamageSpecifier? totalDamage = null)
    {
        if (!IsFinishingHit(body, part.Owner, hit)) // WOLFGATE(Wolfmed): P3-D15
            return false;

        if (totalDamage == null)
        {
            totalDamage = _damageable.GetAllDamage(part.Owner).Clone();
            foreach (var (type, amount) in hit.DamageDict)
                if (amount > FixedPoint2.Zero)
                    totalDamage.DamageDict[type] = totalDamage.DamageDict.GetValueOrDefault(type) + amount;
        }

        var chance = Math.Clamp(GetThresholdProgress(totalDamage, _wfPart.Get(part.Owner).AmputationThresholds) * 0.5f, 0f, 1f); // WOLFGATE(Wolfmed): D8
        return chance > 0f && _random.Prob(chance) && TryAmputate(body, part.Owner);
    }

    private float GetResetRatio(EntityUid body)
    {
        return TryComp(body, out WoundHostComponent? host)
            ? Math.Clamp(host.SeverableResetRatio, 0f, 1f)
            : 0.8f;
    }

    private void SetSeverable(Entity<WoundableComponent> part, bool value)
    {
        if (part.Comp.Severable == value)
            return;

        part.Comp.Severable = value;
        Dirty(part);
    }

    private static FixedPoint2 GetDismembermentSeverity(WoundHostComponent host, BodyPartType type)
    {
        return host.DismembermentSeverities.GetValueOrDefault(type, host.DefaultDismembermentSeverity);
    }
}
