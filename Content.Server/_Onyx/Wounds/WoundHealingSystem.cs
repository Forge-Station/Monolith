using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Server.Medical.Components; // WOLFGATE(Wolfmed): D14, Wolfgate keeps HealingComponent server-side.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12, Onyx-shaped damage API facade.
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundHealingSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damage = default!; // WOLFGATE(Wolfmed): Onyx-shaped damage API; see _WF/Wolfmed/Compat
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundTargetResolver _targets = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, ResolveHealingPartEvent>(OnResolveHealingPart);
    }

    private void OnResolveHealingPart(Entity<WoundHostComponent> body, ref ResolveHealingPartEvent args)
    {
        args.Part = ResolveHealingPart(body, args.RequestedPart, args.Healing, args.DamageContainers,
            args.TreatmentCapabilities, args.AllowedWoundStages, args.BloodlossModifier, args.HealWounds);

        var hasLocalized = args.Healing.DamageDict.Any(entry => body.Comp.LocalizedDamageTypes.Contains(entry.Key));
        args.Accepted = args.Part != null || !hasLocalized;
    }

    /// <summary>Resolve an explicitly selected treatment site without falling back to another limb.</summary>
    public bool TryGetTargetedPart(EntityUid body, EntityUid user, out EntityUid? part)
    {
        part = null;
        if (!TryComp(user, out TargetingComponent? targeting))
            return true;

        if (!_targets.TryResolveExact(body, targeting.Target, out var selected))
            return false;

        part = selected;
        return true;
    }

    public EntityUid? ResolveHealingPart(
        EntityUid body,
        EntityUid? requestedPart,
        DamageSpecifier healing,
        IReadOnlyList<ProtoId<DamageContainerPrototype>>? damageContainers,
        IReadOnlySet<TreatmentCapability> treatmentCapabilities,
        IReadOnlySet<string>? allowedWoundStages,
        float bloodlossModifier,
        bool healWounds = false)
    {
        if (!TryComp(body, out WoundHostComponent? host))
            return null;

        if (requestedPart is { } requested)
            return IsCompatiblePart(body, requested, damageContainers, treatmentCapabilities) ? requested : null;

        EntityUid? selected = null;
        var best = FixedPoint2.Zero;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!IsCompatiblePart(body, part, damageContainers, treatmentCapabilities) ||
                !TryComp(part, out DamageableComponent? damageable))
                continue;

            var score = FixedPoint2.Zero;
            var partDamage = _damage.GetPositiveDamage((part, damageable)).DamageDict;
            foreach (var (type, amount) in healing.DamageDict)
            {
                if (amount >= FixedPoint2.Zero || !host.LocalizedDamageTypes.Contains(type))
                    continue;

                score += FixedPoint2.Min(-amount, partDamage.GetValueOrDefault(type));
            }
            if (healWounds)
                score += _wounds.GetHealingPotential(part, healing, allowedWoundStages);

            if (bloodlossModifier < 0)
                score += FixedPoint2.New(_bleeding.GetPartRate(part));

            if (score <= best)
                continue;

            best = score;
            selected = part;
        }

        return selected;
    }

    public bool CanTreatBleeding(EntityUid part) => _bleeding.CanDressBleeding(part); // WOLFGATE(Wolfmed): a dressed arterial bleed still has a rate, but nothing a topical can do about it

    public bool HasTreatableWounds(EntityUid part, DamageSpecifier healing, IReadOnlySet<string>? allowedStages) =>
        _wounds.GetHealingPotential(part, healing, allowedStages) > FixedPoint2.Zero;

    public bool TryApplyHealing(
        EntityUid body,
        EntityUid? requestedPart,
        Entity<HealingComponent> healing,
        EntityUid? origin,
        out DamageSpecifier healed,
        out bool stoppedBleeding)
    {
        healed = new DamageSpecifier();
        stoppedBleeding = false;
        if (!_net.IsServer || (!healing.Comp.HealDamage && !healing.Comp.HealWounds &&
                               healing.Comp.BloodlossModifier >= 0f && healing.Comp.ModifyBloodLevel == 0f) ||
            !TryComp(body, out WoundHostComponent? host) ||
            !TryComp(body, out DamageableComponent? bodyDamageable))
            return false;

        var treatable = GetTreatableDamage(healing.Comp); // WOLFGATE(Wolfmed): W0, TreatedDamageTypes narrows the spec.
        var resolve = new ResolveHealingPartEvent(body, treatable, // WOLFGATE(Wolfmed): W0
            // WOLFGATE(Wolfmed): D31, Wolfgate's HealingComponent.DamageContainers is List<string>.
            healing.Comp.DamageContainers?.Select(x => new ProtoId<DamageContainerPrototype>(x)).ToList(),
            healing.Comp.TreatmentCapabilities, healing.Comp.AllowedWoundStages,
            healing.Comp.BloodlossModifier, requestedPart, healing.Comp.HealWounds);
        RaiseLocalEvent(body, ref resolve);
        if (!resolve.Accepted)
            return false;

        var change = treatable * _damage.UniversalTopicalsHealModifier; // WOLFGATE(Wolfmed): W0
        var before = _damage.GetPositiveDamage((body, bodyDamageable));
        var applied = false;
        if (resolve.Part is { } part)
        {
            if (healing.Comp.HealDamage)
                applied = _routing.TryApplyPartDamage(body, part, change, origin, healWounds: healing.Comp.HealWounds);
            if (healing.Comp.HealWounds && !healing.Comp.HealDamage)
                applied |= _wounds.TryHealWounds(part, change, healing.Comp.AllowedWoundStages);
            // WOLFGATE(Wolfmed): a topical that removes damage only shrinks the wound by healingMultiplier of it, so the
            // damage runs out first and the wound was left standing with nothing able to touch it. Once there is
            // no damage left for the item to remove, it works on the wound itself.
            else if (healing.Comp.HealWounds && !applied)
                applied |= _wounds.TryHealWounds(part, change, healing.Comp.AllowedWoundStages);
        }
        else if (healing.Comp.HealDamage)
            applied = _routing.TryApplyDamage(body, change, origin, healWounds: healing.Comp.HealWounds);

        if (healing.Comp.BloodlossModifier < 0 && resolve.Part is { } bleedingPart)
        {
            stoppedBleeding = TreatBleeding(bleedingPart, -healing.Comp.BloodlossModifier);
        }

        // WOLFGATE(Wolfmed) START: a makeshift suture leaves its dirt in the wound it treated.
        if ((applied || stoppedBleeding) && resolve.Part is { } treatedPart)
            ContaminateIfDirty(healing, treatedPart);
        // WOLFGATE END

        var after = _damage.GetPositiveDamage((body, bodyDamageable));
        foreach (var (type, amount) in before.DamageDict)
        {
            var delta = after.DamageDict.GetValueOrDefault(type) - amount;
            if (delta != FixedPoint2.Zero)
                healed.DamageDict[type] = delta;
        }

        return applied || stoppedBleeding || healing.Comp.ModifyBloodLevel != 0f;
    }

    private bool TreatBleeding(EntityUid part, float amount)
    {
        var reduced = _bleeding.ReducePartBleeding(part, FixedPoint2.New(amount), dressing: true); // WOLFGATE(Wolfmed): gauze stays on
        // WOLFGATE(Wolfmed): W2: a dressing cannot take an arterial bleed's severity, only slow its rate.
        return _bleeding.BandageArterialBleeds(part) | reduced;
    }

    public bool IsCompatiblePart(
        EntityUid body,
        EntityUid part,
        IReadOnlyList<ProtoId<DamageContainerPrototype>>? damageContainers,
        IReadOnlySet<TreatmentCapability> treatmentCapabilities)
    {
        if (!_body.BodyHasChild(body, part) || !TryComp(part, out WoundableComponent? woundable) ||
            !_prototypes.TryIndex(woundable.Profile, out var profile) ||
            !profile.TreatmentCapabilities.Overlaps(treatmentCapabilities))
            return false;

        return true;
    }

}
