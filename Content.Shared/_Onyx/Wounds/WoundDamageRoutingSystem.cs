using System.Linq;
using Content.Shared.Body.Systems;
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE(Wolfmed): D8, Onyx's extra part fields live on WolfmedBodyPartComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade, DamageDealtEvent + shared bed-heal marker (HealOnBuckleComponent is server-only here).
using Content.Shared._WF.Wolfmed.Targeting; // WOLFGATE(Wolfmed): D10, Onyx's TargetResolverSystem is replaced by WoundTargetResolver.
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Medical;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10, TargetBodyPart comes from Shitmed.
using Content.Shared._Mono.ArmorPlate; // WOLFGATE(Wolfmed): subscription ordering, PLAN 8.3 trap 4.
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundDamageRoutingSystem : EntitySystem
{
    [Dependency] private WolfmedDamageableSystem _damage = default!; // WOLFGATE(Wolfmed): D12, Onyx-shaped damage API; see _WF/Wolfmed/Compat
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private WoundTargetResolver _targetResolver = default!; // WOLFGATE(Wolfmed): D10, Shitmed-backed replacement.
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE(Wolfmed): D8, Onyx's extra part fields.
    [Dependency] private WolfmedAimScatterSystem _wfAim = default!; // WOLFGATE(Wolfmed): bullets can stray off the aimed part.
    [Dependency] private PainSystem _pain = default!;
    // WOLFGATE(Wolfmed): M6: the MobThresholdSystem dependency went with TryApplyLethalDamage, its only reader.
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly HashSet<EntityUid> _routing = new();
    private readonly Dictionary<EntityUid, EntityUid> _requestedParts = new();
    private readonly HashSet<EntityUid> _applied = new();
    private readonly HashSet<EntityUid> _skipWoundHealing = new();
    private readonly HashSet<EntityUid> _explosionDamage = new();
    private readonly Dictionary<EntityUid, EntityUid> _explosionAmputationCandidates = new();
    private readonly Dictionary<EntityUid, float> _woundSeverityMultipliers = new();
    private readonly Dictionary<EntityUid, IReadOnlySet<TreatmentCapability>> _treatmentCapabilities = new();

    // WOLFGATE(Wolfmed): D23 - routing cancels BeforeDamageChangedEvent before DamageableSystem's resistance block, so armour
    // penetration, the tool and Mono's origin flag only reach the re-entrant routed pass through this side table.
    private readonly Dictionary<EntityUid, (float ArmorPenetration, EntityUid? Tool, DamageableSystem.DamageOriginFlag? OriginFlag)> _routedModifiers = new();

    // WOLFGATE(Wolfmed): D27 - what the routed pass actually applied, so TryChangeDamage can report it instead of null.
    private readonly Dictionary<EntityUid, DamageSpecifier> _appliedDelta = new();

    // WOLFGATE(Wolfmed): M6: P25 - the caller's ignoreResistances and Shitmed part multiplier, for the part step of the pass.
    private readonly HashSet<EntityUid> _ignoreResistances = new();
    private readonly Dictionary<EntityUid, float> _partMultipliers = new();

    public override void Initialize()
    {
        base.Initialize();
        // WOLFGATE(Wolfmed): PLAN 8.3 trap 4 - SharedArmorPlateSystem.OnBeforeDamageChanged mutates args.Damage in place and
        // would absorb twice per hit. RT demands identical before/after sets when one system takes the same event twice.
        SubscribeLocalEvent<WoundHostComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged, before: [typeof(SharedArmorPlateSystem)]);
        SubscribeLocalEvent<WoundHostComponent, DamageDealtEvent>(OnDamageDealt, before: [typeof(DamageableSystem)]);
        SubscribeLocalEvent<WoundableComponent, BeforeDamageChangedEvent>(OnBeforePartDamageChanged, before: [typeof(SharedArmorPlateSystem)]);
    }

    private void OnBeforeDamageChanged(Entity<WoundHostComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // WOLFGATE(Wolfmed): godmode and stasis already refused the hit, and ordering against them is impossible from
        // shared code (both concrete systems are abstract), so test the flag instead.
        if (!_net.IsServer || args.Cancelled || _routing.Contains(ent))
            return;

        args.Cancelled = true;

        // WOLFGATE(Wolfmed): D23 - stash the caller's armour penetration, tool and origin flag for the routed pass.
        _routedModifiers[ent.Owner] = (args.ArmorPenetration, args.Tool, args.OriginFlag);

        // WOLFGATE(Wolfmed): D27 - accumulate whatever the routed pass lands so TryChangeDamage can return it.
        var applied = new DamageSpecifier();
        _appliedDelta[ent.Owner] = applied;

        // WOLFGATE(Wolfmed): Wolfgate's TryChangeDamage carries an explicit Shitmed targetPart; honour it when it names one
        // part. Composite masks (TargetBodyPart.All, Torso | <random>) deliberately leave the request unset.
        var requested = false;
        if (args.TargetPart is { } requestedTarget && SharedTargetingSystem.IsSelectable(requestedTarget) &&
            _targetResolver.TryResolveAvailable(ent, requestedTarget, out var targetedPart) &&
            IsAttachedWoundablePart(ent, targetedPart))
        {
            _requestedParts[ent.Owner] = targetedPart;
            requested = true;
        }

        if (args.PartMultiplier != 1f) // WOLFGATE(Wolfmed): M6: P25
            _partMultipliers[ent.Owner] = args.PartMultiplier;

        try
        {
            RouteThroughBodyModifiers(ent, new DamageSpecifier(args.Damage), args.Origin, args.IgnoreResistances, args.InterruptsDoAfters); // WOLFGATE(Wolfmed): M6: P25; a copy, so the inner pass never scales the caller's specifier
        }
        finally
        {
            if (requested)
                _requestedParts.Remove(ent.Owner);
            _appliedDelta.Remove(ent.Owner);
            _routedModifiers.Remove(ent.Owner);
            _partMultipliers.Remove(ent.Owner); // WOLFGATE(Wolfmed): M6
        }

        // WOLFGATE(Wolfmed): D27 - a non-null result keeps every caller that reads TryChangeDamage's return working
        // (pierce-through, blunt stamina, hit logs, damage popups) now that routing cancels the original pass.
        applied.TrimZeros();
        args.Applied = applied;
    }

    /// <summary>
    /// WOLFGATE(Wolfmed): D27: folds one applied delta into the routed pass's running total, if a pass is open.
    /// </summary>
    private void AccumulateApplied(EntityUid body, DamageSpecifier applied)
    {
        if (applied.Empty || !_appliedDelta.TryGetValue(body, out var total))
            return;

        foreach (var (type, amount) in applied.DamageDict)
            total.DamageDict[type] = total.DamageDict.GetValueOrDefault(type) + amount;
    }

    private void OnDamageDealt(Entity<WoundHostComponent> ent, ref DamageDealtEvent args)
    {
        if (!_net.IsServer || !_routing.Contains(ent))
            return;

        var damage = args.Damage.Clone();
        args.Damage.DamageDict.Clear();
        RouteAppliedDamage(ent, damage, args.Origin, args.InterruptsDoAfters);
    }

    public void WithTreatmentCapabilities(EntityUid body, IReadOnlySet<TreatmentCapability> capabilities, Action action)
    {
        _treatmentCapabilities[body] = capabilities;
        try
        {
            action();
        }
        finally
        {
            _treatmentCapabilities.Remove(body);
        }
    }

    private void OnBeforePartDamageChanged(Entity<WoundableComponent> part, ref BeforeDamageChangedEvent args)
    {
        var filtered = FilterPartDamage(part, args.Damage, args.Origin, args.OriginFlag);
        if (ReferenceEquals(filtered, args.Damage))
            return;

        args.Damage.DamageDict.Clear();
        foreach (var (type, amount) in filtered.DamageDict)
            args.Damage.DamageDict[type] = amount;
    }

    public bool TryApplyDamage(
        EntityUid body,
        DamageSpecifier damage,
        EntityUid? origin = null,
        EntityUid? requestedPart = null,
        bool ignoreResistances = false,
        bool healWounds = true)
    {
        if (!TryComp(body, out WoundHostComponent? host) || !_net.IsServer || _routing.Contains(body))
            return false;

        if (requestedPart is { } part)
        {
            if (!IsAttachedWoundablePart(body, part))
                return false;
            _requestedParts[body] = part;
        }

        try
        {
            if (!healWounds)
                _skipWoundHealing.Add(body);
            return RouteThroughBodyModifiers((body, host), damage, origin, ignoreResistances);
        }
        finally
        {
            _skipWoundHealing.Remove(body);
            _requestedParts.Remove(body);
        }
    }

    public bool TryApplyOriginDamage(
        EntityUid body,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true)
    {
        return TryRouteOriginDamage(body,
                   damage,
                   origin,
                   out damageDealt,
                   ignoreResistances,
                   interruptsDoAfters) &&
               !damageDealt.Empty;
    }

    public bool TryRouteOriginDamage(
        EntityUid body,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true)
    {
        damageDealt = new DamageSpecifier();
        if (!TryComp(body, out WoundHostComponent? host) || !_net.IsServer || _routing.Contains(body))
            return false;

        var before = _damage.GetAllDamage(body).Clone();
        RouteThroughBodyModifiers((body, host), damage, origin, ignoreResistances, interruptsDoAfters);

        var after = _damage.GetAllDamage(body);
        foreach (var (type, newAmount) in after.DamageDict)
        {
            var delta = newAmount - before.DamageDict.GetValueOrDefault(type);
            if (delta != FixedPoint2.Zero)
                damageDealt.DamageDict[type] = delta;
        }

        foreach (var (type, oldAmount) in before.DamageDict)
        {
            if (!after.DamageDict.ContainsKey(type) && oldAmount != FixedPoint2.Zero)
                damageDealt.DamageDict[type] = FixedPoint2.Zero - oldAmount;
        }

        return true;
    }

    public bool TryApplyPartDamage(
        EntityUid body,
        EntityUid part,
        DamageSpecifier damage,
        EntityUid? origin = null,
        bool ignoreResistances = false,
        bool healWounds = true)
    {
        return TryApplyDamage(body, damage, origin, part, ignoreResistances, healWounds);
    }

    // WOLFGATE(Wolfmed): P4-D14: the distributed entry points bypass OnBeforeDamageChanged, the only other writer of
    // _routedModifiers, so without this the re-entrant routed pass reaches SharedArmorPlateSystem with
    // OriginFlag == null and its gate (Origin == null && OriginFlag != Explosion) refuses plate protection
    // against explosions outright. The entry is saved and restored rather than removed because the dictionary has
    // a second writer and a bare remove would be the non-reentrancy bug of PLAN4 trap T3.
    public bool TryApplyDistributedDamage(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart mask,
        DamageDistribution mode,
        EntityUid? origin = null,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        float variation = 0f,
        bool isExplosion = false,
        float woundSeverityMultiplier = 1f,
        DamageableSystem.DamageOriginFlag? originFlag = null) // WOLFGATE(Wolfmed): P4-D14: optional, so existing callers are unchanged.
    {
        var hadModifiers = _routedModifiers.TryGetValue(body, out var previousModifiers); // WOLFGATE(Wolfmed): P4-D14
        _routedModifiers[body] = (0f, null, originFlag); // WOLFGATE(Wolfmed): P4-D14
        try
        {
            return ApplyDistributedDamageCore(body, damage, mask, mode, origin, ignoreResistances,
                interruptsDoAfters, variation, isExplosion, woundSeverityMultiplier);
        }
        finally
        {
            if (hadModifiers) // WOLFGATE(Wolfmed): P4-D14
                _routedModifiers[body] = previousModifiers;
            else
                _routedModifiers.Remove(body);
        }
    }

    // WOLFGATE(Wolfmed): P4-D14: Onyx's body verbatim, made private so the public entry point can wrap it in the
    // _routedModifiers scope above without re-indenting the whole method.
    private bool ApplyDistributedDamageCore(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart mask,
        DamageDistribution mode,
        EntityUid? origin = null,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        float variation = 0f,
        bool isExplosion = false,
        float woundSeverityMultiplier = 1f)
    {
        if (!TryComp(body, out WoundHostComponent? host) || !_net.IsServer || _routing.Contains(body) ||
            mode is not DamageDistribution.SplitEvenly and not DamageDistribution.SplitByPartWeight and not DamageDistribution.SplitWithVariation)
            return false;

        var systemic = new DamageSpecifier();
        var localized = new DamageSpecifier();
        foreach (var (type, amount) in damage.DamageDict)
            (host.LocalizedDamageTypes.Contains(type) ? localized : systemic).DamageDict[type] = amount;

        var parts = _targetResolver.GetMatchingParts(body, mask);
        parts.RemoveAll(part => !IsAttachedWoundablePart(body, part));
        var applied = false;

        if (!systemic.Empty)
            applied |= RouteThroughBodyModifiers((body, host), systemic, origin, ignoreResistances, interruptsDoAfters);

        if (localized.Empty || parts.Count == 0)
            return applied;

        var weights = new float[parts.Count];
        var totalWeight = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            var weight = 1f;
            if (mode != DamageDistribution.SplitEvenly && TryComp(parts[i], out BodyPartComponent? part))
                weight = host.TargetWeights.GetValueOrDefault(part.PartType, 1f);
            if (!float.IsFinite(weight) || weight <= 0f)
                weight = 1f;
            if (mode == DamageDistribution.SplitWithVariation && variation > 0f)
                weight *= _random.NextFloat() * variation + 1f;
            weights[i] = weight;
            totalWeight += weight;
        }

        var shares = new DamageSpecifier[parts.Count];
        for (var i = 0; i < shares.Length; i++)
            shares[i] = new DamageSpecifier();

        foreach (var (type, amount) in localized.DamageDict)
        {
            var remaining = amount.Value;
            for (var i = 0; i < parts.Count; i++)
            {
                var value = i == parts.Count - 1
                    ? remaining
                    : (int) ((long) amount.Value * weights[i] / totalWeight);
                shares[i].DamageDict[type] = FixedPoint2.FromHundredths(value);
                remaining -= value;
            }
        }

        if (isExplosion)
        {
            _explosionDamage.Add(body);
            if (PickExplosionAmputationCandidate(parts, shares) is { } candidate)
                _explosionAmputationCandidates[body] = candidate;
        }
        if (woundSeverityMultiplier != 1f)
            _woundSeverityMultipliers[body] = Math.Max(0f, woundSeverityMultiplier);
        try
        {
            for (var i = 0; i < parts.Count; i++)
            {
                _requestedParts[body] = parts[i];
                try
                {
                    applied |= RouteThroughBodyModifiers((body, host), shares[i], origin, ignoreResistances, interruptsDoAfters);
                }
                finally
                {
                    _requestedParts.Remove(body);
                }
            }

            return applied;
        }
        finally
        {
            _explosionDamage.Remove(body);
            _explosionAmputationCandidates.Remove(body);
            _woundSeverityMultipliers.Remove(body);
        }
    }

    public bool TryApplyTargetedDamage(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart requested,
        EntityUid? shooter,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false)
    {
        return TryRouteTargetedDamage(body,
                   damage,
                   requested,
                   shooter,
                   out damageDealt,
                   ignoreResistances,
                   shooter) &&
               !damageDealt.Empty;
    }

    public bool TryApplyCarrierDamage(
        EntityUid body,
        EntityUid carrier,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false)
    {
        return TryRouteCarrierDamage(body,
                   carrier,
                   damage,
                   origin,
                   out damageDealt,
                   ignoreResistances) &&
               !damageDealt.Empty;
    }

    public bool TryRouteCarrierDamage(
        EntityUid body,
        EntityUid carrier,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false)
    {
        damageDealt = new DamageSpecifier();
        return TryComp(carrier, out TargetingSnapshotComponent? snapshot) &&
               TryRouteTargetedDamage(body,
                   damage,
                   snapshot.RequestedTarget,
                   snapshot.Shooter,
                   out damageDealt,
                   ignoreResistances,
                   origin);
    }

    public bool TryRouteTargetedDamage(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart requested,
        EntityUid? shooter,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false)
    {
        return TryRouteTargetedDamage(body,
            damage,
            requested,
            shooter,
            out damageDealt,
            ignoreResistances,
            shooter);
    }

    private bool TryRouteTargetedDamage(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart requested,
        EntityUid? shooter,
        out DamageSpecifier damageDealt,
        bool ignoreResistances,
        EntityUid? origin)
    {
        damageDealt = new DamageSpecifier();
        if (!TryComp(body, out WoundHostComponent? host) || !_net.IsServer || _routing.Contains(body) ||
            !_targetResolver.TryResolve(body, requested, shooter, out var part))
            return false;

        if (!TryComp(part, out DamageableComponent? partDamageable))
            return false;

        var before = _damage.GetPositiveDamage((part, partDamageable));
        var systemicBefore = CompOrNull<SystemicDamageComponent>(body)?.Damage.Clone() ?? new DamageSpecifier();
        _requestedParts[body] = part;
        try
        {
            RouteThroughBodyModifiers((body, host), damage, origin, ignoreResistances);

            var after = _damage.GetPositiveDamage((part, partDamageable));
            foreach (var (type, oldAmount) in before.DamageDict)
            {
                var delta = after.DamageDict.GetValueOrDefault(type) - oldAmount;
                if (delta != FixedPoint2.Zero)
                    damageDealt.DamageDict[type] = delta;
            }

            foreach (var (type, newAmount) in after.DamageDict)
            {
                if (!before.DamageDict.ContainsKey(type) && newAmount != FixedPoint2.Zero)
                    damageDealt.DamageDict[type] = newAmount;
            }

            var systemicAfter = CompOrNull<SystemicDamageComponent>(body)?.Damage;
            if (systemicAfter != null)
            {
                foreach (var type in systemicBefore.DamageDict.Keys.Concat(systemicAfter.DamageDict.Keys).Distinct())
                {
                    var delta = systemicAfter.DamageDict.GetValueOrDefault(type) -
                                systemicBefore.DamageDict.GetValueOrDefault(type);
                    if (delta != FixedPoint2.Zero)
                        damageDealt.DamageDict[type] = damageDealt.DamageDict.GetValueOrDefault(type) + delta;
                }
            }
            return true;
        }
        finally
        {
            _requestedParts.Remove(body);
        }
    }

    public EntityUid? ResolveDamagePart(EntityUid body, EntityUid? requestedPart)
    {
        if (requestedPart is { } requested)
            return IsAttachedWoundablePart(body, requested) ? requested : null;

        if (!TryComp(body, out WoundHostComponent? host))
            return null;

        var candidates = new List<(EntityUid Part, float Weight)>();
        var totalWeight = 0f;
        foreach (var (part, component) in _body.GetBodyChildren(body))
        {
            if (!HasComp<WoundableComponent>(part))
                continue;

            var weight = host.TargetWeights.GetValueOrDefault(component.PartType, 1f);
            if (weight <= 0f)
                continue;

            candidates.Add((part, weight));
            totalWeight += weight;
        }

        if (candidates.Count == 0)
            return null;

        var roll = _random.NextFloat() * totalWeight;
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll <= 0f)
                return candidate.Part;
        }

        return candidates[^1].Part;
    }

    // WOLFGATE(Wolfmed): M6: Onyx's TryApplyLethalDamage is removed. Its one caller, the execution hook (HOOK 13), went in M2
    // (an execution is catastrophic brain injury now, OD17), and a torso top-up no longer kills a wound host.

    public bool TryApplyPartDamage(
        EntityUid body,
        EntityUid part,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false,
        bool healWounds = true)
    {
        return TryRoutePartDamage(body,
                   part,
                   damage,
                   origin,
                   out damageDealt,
                   ignoreResistances,
                   healWounds) &&
               !damageDealt.Empty;
    }

    public bool TryRoutePartDamage(
        EntityUid body,
        EntityUid part,
        DamageSpecifier damage,
        EntityUid? origin,
        out DamageSpecifier damageDealt,
        bool ignoreResistances = false,
        bool healWounds = true)
    {
        damageDealt = new DamageSpecifier();
        if (!TryComp(body, out WoundHostComponent? host) || !_net.IsServer || _routing.Contains(body) ||
            !IsAttachedWoundablePart(body, part))
            return false;

        var before = _damage.GetAllDamage(body).Clone();
        _requestedParts[body] = part;
        try
        {
            if (!healWounds)
                _skipWoundHealing.Add(body);
            RouteThroughBodyModifiers((body, host), damage, origin, ignoreResistances);
        }
        finally
        {
            _skipWoundHealing.Remove(body);
            _requestedParts.Remove(body);
        }

        var after = _damage.GetAllDamage(body);
        foreach (var (type, newAmount) in after.DamageDict)
        {
            var delta = newAmount - before.DamageDict.GetValueOrDefault(type);
            if (delta != FixedPoint2.Zero)
                damageDealt.DamageDict[type] = delta;
        }

        return true;
    }

    public bool TryGetActiveHandPart(EntityUid body, out EntityUid handPart)
    {
        handPart = EntityUid.Invalid;
        // WOLFGATE(Wolfmed): Wolfgate's GetActiveHand returns the Hand itself (a class), and HandLocation has no
        // Functional* members.
        if (!TryComp(body, out HandsComponent? hands) ||
            _hands.GetActiveHand((body, hands)) is not { } hand)
            return false;

        var symmetry = hand.Location switch
        {
            HandLocation.Left => BodyPartSymmetry.Left,
            HandLocation.Right => BodyPartSymmetry.Right,
            _ => BodyPartSymmetry.None,
        };
        if (symmetry == BodyPartSymmetry.None)
            return false;

        foreach (var candidate in _body.GetBodyChildrenOfType(body, BodyPartType.Hand))
        {
            if (candidate.Component.Symmetry != symmetry || !HasComp<WoundableComponent>(candidate.Id))
                continue;

            handPart = candidate.Id;
            return true;
        }

        return false;
    }

    private bool RouteThroughBodyModifiers(
        Entity<WoundHostComponent> body,
        DamageSpecifier damage,
        EntityUid? origin,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true)
    {
        if (!_routing.Add(body))
            return false;

        if (ignoreResistances) // WOLFGATE(Wolfmed): M6: P25, the part's armour is a resistance too
            _ignoreResistances.Add(body);

        var hadRequestedPart = _requestedParts.ContainsKey(body);
        try
        {
            if (!hadRequestedPart)
            {
                var localized = new DamageSpecifier();
                var localizedDamage = false;
                foreach (var (type, amount) in damage.DamageDict)
                {
                    if (body.Comp.LocalizedDamageTypes.Contains(type))
                    {
                        localized.DamageDict[type] = amount;
                        if (amount > FixedPoint2.Zero)
                            localizedDamage = true;
                    }
                }

                if (!localized.Empty && localizedDamage)
                {
                    if (origin is { } source &&
                        TryComp(source, out TargetingSnapshotComponent? snapshot) &&
                        _targetResolver.TryResolve(body, snapshot.RequestedTarget, snapshot.Shooter, out var snapshotPart))
                        _requestedParts[body] = snapshotPart;
                    else if (origin is { } light && HasComp<PoweredLightComponent>(light) &&
                             TryGetActiveHandPart(body, out var handPart))
                        _requestedParts[body] = handPart;
                    else if (origin is { } targetingSource && _targetResolver.TryResolve(body, targetingSource, out var targetedPart))
                        _requestedParts[body] = _wfAim.Scatter(body, targetingSource, _routedModifiers.GetValueOrDefault(body.Owner).Tool, targetedPart); // WOLFGATE(Wolfmed)
                    else if (origin is { } defibrillator && HasComp<DefibrillatorComponent>(defibrillator) &&
                             _targetResolver.TryResolveAvailable(body, TargetBodyPart.Torso, out var chestPart)) // WOLFGATE(Wolfmed): D9
                        _requestedParts[body] = chestPart;
                    else if (ResolveDamagePart(body, null) is { } randomPart)
                        _requestedParts[body] = randomPart;
                }
            }

            _applied.Remove(body);
            // WOLFGATE(Wolfmed): D23 - the routed pass is the only one that reaches DamageableSystem's resistance block, so it
            // has to carry the original call's armour penetration, tool and origin flag or every AP weapon loses its AP.
            var modifiers = _routedModifiers.GetValueOrDefault(body.Owner);
            _damage.ChangeDamage(body.Owner, damage, ignoreResistances, interruptsDoAfters, origin,
                armorPenetration: modifiers.ArmorPenetration, tool: modifiers.Tool, originFlag: modifiers.OriginFlag);
            return _applied.Remove(body);
        }
        finally
        {
            if (!hadRequestedPart)
                _requestedParts.Remove(body);
            _routing.Remove(body);
            _ignoreResistances.Remove(body); // WOLFGATE(Wolfmed): M6
        }
    }

    private void RouteAppliedDamage(Entity<WoundHostComponent> body, DamageSpecifier damage, EntityUid? origin, bool interruptsDoAfters)
    {
        var systemic = new DamageSpecifier();
        var localized = new DamageSpecifier();
        foreach (var (type, amount) in damage.DamageDict)
            (body.Comp.LocalizedDamageTypes.Contains(type) ? localized : systemic).DamageDict[type] = amount;

        if (!systemic.Empty)
        {
            if (ApplySystemicDamage(body, systemic))
                _applied.Add(body);
        }

        var healing = new DamageSpecifier();
        foreach (var (type, amount) in localized.DamageDict.ToArray())
        {
            if (amount >= FixedPoint2.Zero)
                continue;

            healing.DamageDict[type] = amount;
            localized.DamageDict.Remove(type);
        }

        if (!healing.Empty && ApplyLocalizedHealing(body, healing, origin, interruptsDoAfters))
            _applied.Add(body);

        EntityUid? part = _requestedParts.TryGetValue(body, out var requestedPart) ? requestedPart : null;
        if (part is null && !localized.Empty)
            part = ResolveDamagePart(body, null);

        if (!localized.Empty && part is { } target)
        {
            if (!TryComp(target, out BodyPartComponent? partComponent))
                return;

            localized = FilterPartDamage(target, localized);
            // WOLFGATE(Wolfmed): M6: P25 - Shitmed's part multiplier (a heavy swing's 0.5) scales what reaches the part, once.
            if (_partMultipliers.TryGetValue(body, out var partMultiplier))
            {
                localized = localized * partMultiplier;
                localized.TrimZeros();
            }

            if (localized.Empty)
            {
                _projection.RefreshBodyDamage(body);
                return;
            }

            var modify = new PartDamageModifyEvent(
                body,
                target,
                partComponent.PartType,
                partComponent.Symmetry,
                localized,
                _routedModifiers.GetValueOrDefault(body).ArmorPenetration); // WOLFGATE(Wolfmed): D23, HOOK 10 armours the part here.
            if (!_ignoreResistances.Contains(body) && // WOLFGATE(Wolfmed): M6: P25, ignoreResistances skips the part's armour too
                TryComp(body, out InventoryComponent? inventory))
                _inventory.RelayEvent((body, inventory), modify);

            localized = modify.Damage;
            if (localized.Empty)
            {
                _projection.RefreshBodyDamage(body);
                return;
            }

            // WOLFGATE(Wolfmed): body-wide damage ceiling, for damage nobody dealt (fire, cold, atmosphere, an EMP). A corpse
            // drifts up to the ceiling on its own, and a ceiling that also stopped attacks meant a dead body could
            // no longer be wounded or dismembered at all.
            // WOLFGATE(Wolfmed): M1b: per-part ceiling for damage nobody dealt (fire, cold, atmosphere, an EMP), plus the
            // corpse ceiling. What it trims is not lost: it rides on the hit's event as Overflow.
            var discarded = origin == null && !_explosionDamage.Contains(body)
                ? _wfPart.ClampToBodyCap(body, target, localized)
                : new DamageSpecifier();
            var overflow = AccumulateAmputationOverflow(target, ref localized);
            if (!overflow.Empty)
            {
                var overflowed = new PartDamageOverflowedEvent(body, target, overflow,
                    _explosionDamage.Contains(body), _explosionAmputationCandidates.GetValueOrDefault(body) == target);
                RaiseLocalEvent(target, ref overflowed);
            }

            discarded += overflow; // WOLFGATE(Wolfmed): M1b
            if ((localized.Empty && discarded.Empty) || !_body.BodyHasChild(body, target)) // WOLFGATE(Wolfmed): M1b
            {
                _projection.RefreshBodyDamage(body);
                return;
            }

            // WOLFGATE(Wolfmed): M1b: one event per hit, raised even when the ceiling stored nothing.
            var stored = !localized.Empty && _damage.TryChangeDamage(target,
                    localized,
                    out var appliedDamage,
                    ignoreResistances: true,
                    interruptsDoAfters: interruptsDoAfters,
                    origin: origin,
                    ignoreGlobalModifiers: true)
                ? appliedDamage
                : null;
            if (stored != null || !discarded.Empty) // WOLFGATE(Wolfmed): M1b
            {
                if (stored != null) // WOLFGATE(Wolfmed): M1b
                {
                    _applied.Add(body);
                    AccumulateApplied(body, stored); // WOLFGATE(Wolfmed): D27, Applied only (M1b)
                }

                var applied = new PartDamageAppliedEvent(body, target, stored ?? new DamageSpecifier(), // WOLFGATE(Wolfmed): M1b
                    !_skipWoundHealing.Contains(body), origin, _explosionDamage.Contains(body),
                    overflow.Empty && _explosionAmputationCandidates.GetValueOrDefault(body) == target,
                    _woundSeverityMultipliers.GetValueOrDefault(body, 1f),
                    _routedModifiers.GetValueOrDefault(body).Tool, // WOLFGATE(Wolfmed): W1: carry the weapon to the wound rules.
                    discarded.Empty ? null : discarded, // WOLFGATE(Wolfmed): M1b
                    interruptsDoAfters); // WOLFGATE(Wolfmed): M6: OD18
                RaiseLocalEvent(target, ref applied);
                if (stored == null) // WOLFGATE(Wolfmed): M1b: nothing stored, so nothing re-projected the body.
                    _projection.RefreshBodyDamage(body);
                return;
            }
        }

        _projection.RefreshBodyDamage(body);
    }

    private DamageSpecifier AccumulateAmputationOverflow(EntityUid part, ref DamageSpecifier damage)
    {
        var overflow = new DamageSpecifier();
        if (!TryComp(part, out WoundableComponent? woundable) ||
            !TryComp(part, out BodyPartComponent? bodyPart) ||
            _wfPart.Get(part).MaxDamage <= FixedPoint2.Zero || // WOLFGATE(Wolfmed): D8
            !TryComp(part, out DamageableComponent? damageable))
            return overflow;

        var current = _damage.GetPositiveDamage((part, damageable)).GetTotal();
        var remaining = _wfPart.Get(part).MaxDamage - current; // WOLFGATE(Wolfmed): D8

        if (remaining > FixedPoint2.Zero && woundable.AmputationOverflow != FixedPoint2.Zero)
        {
            woundable.AmputationOverflow = FixedPoint2.Zero;
            Dirty(part, woundable);
        }

        if (remaining <= FixedPoint2.Zero)
        {
            foreach (var (type, amount) in damage.DamageDict.ToArray())
            {
                if (amount <= FixedPoint2.Zero)
                    continue;

                overflow.DamageDict[type] = amount;
                damage.DamageDict.Remove(type);
            }
        }
        else
        {
            foreach (var (type, amount) in damage.DamageDict.ToArray())
            {
                if (amount <= FixedPoint2.Zero)
                    continue;

                var fits = FixedPoint2.Min(amount, remaining);
                if (fits == amount)
                {
                    remaining -= fits;
                    continue;
                }

                damage.DamageDict[type] = fits;
                overflow.DamageDict[type] = amount - fits;
                remaining = FixedPoint2.Zero;
            }
        }

        if (overflow.Empty)
            return overflow;

        woundable.AmputationOverflow += overflow.GetTotal();
        Dirty(part, woundable);
        return overflow;
    }

    private bool ApplyLocalizedHealing(
        Entity<WoundHostComponent> body,
        DamageSpecifier healing,
        EntityUid? origin,
        bool interruptsDoAfters)
    {
        if (_requestedParts.TryGetValue(body, out var requestedPart))
            return CanTreatPart(body, requestedPart) &&
                   ApplyPartChange(body, requestedPart, healing, origin, interruptsDoAfters);

        var parts = new List<(EntityUid Part, DamageableComponent Damageable)>();
        foreach (var (part, _) in _body.GetBodyChildren(body))
            if (HasComp<WoundableComponent>(part) && CanTreatPart(body, part) &&
                TryComp(part, out DamageableComponent? damageable))
                parts.Add((part, damageable));

        var changes = parts.ToDictionary(part => part.Part, _ => new DamageSpecifier());
        foreach (var (type, amount) in healing.DamageDict)
        {
            var totalDamage = FixedPoint2.Zero;
            foreach (var part in parts)
            {
                if (CanPartReceiveDamage(part.Part, type))
                    totalDamage += _damage.GetPositiveDamage((part.Part, part.Damageable)).DamageDict.GetValueOrDefault(type);
            }

            if (totalDamage <= FixedPoint2.Zero)
                continue;

            var remaining = amount.Value;
            var damaged = parts.Where(part =>
                    CanPartReceiveDamage(part.Part, type) &&
                    _damage.GetPositiveDamage((part.Part, part.Damageable)).DamageDict.GetValueOrDefault(type) > FixedPoint2.Zero)
                .ToArray();
            for (var i = 0; i < damaged.Length; i++)
            {
                var partDamage = _damage.GetPositiveDamage((damaged[i].Part, damaged[i].Damageable))
                    .DamageDict.GetValueOrDefault(type);
                var value = i == damaged.Length - 1
                    ? remaining
                    : (int) ((long) amount.Value * partDamage.Value / totalDamage.Value);
                changes[damaged[i].Part].DamageDict[type] = FixedPoint2.FromHundredths(value);
                remaining -= value;
            }
        }

        var applied = false;
        foreach (var (part, change) in changes)
            if (!change.Empty)
                applied |= ApplyPartChange(body, part, change, origin, interruptsDoAfters);
        return applied;
    }

    public bool TryRouteDistributedDamage(
        EntityUid body,
        DamageSpecifier damage,
        TargetBodyPart mask,
        DamageDistribution mode,
        EntityUid? origin = null,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        float variation = 0f,
        bool isExplosion = false,
        float woundSeverityMultiplier = 1f,
        DamageableSystem.DamageOriginFlag? originFlag = null) // WOLFGATE(Wolfmed): P4-D14: optional, so existing callers are unchanged.
    {
        if (!TryComp<WoundHostComponent>(body, out _) || !_net.IsServer || _routing.Contains(body) ||
            mode is not DamageDistribution.SplitEvenly and not DamageDistribution.SplitByPartWeight and not DamageDistribution.SplitWithVariation)
            return false;

        TryApplyDistributedDamage(body,
            damage,
            mask,
            mode,
            origin,
            ignoreResistances,
            interruptsDoAfters,
            variation,
            isExplosion,
            woundSeverityMultiplier,
            originFlag); // WOLFGATE(Wolfmed): P4-D14: carry Mono's plate-protection flag into the routed pass.
        return true;
    }

    private bool CanTreatPart(EntityUid body, EntityUid part)
    {
        if (!_treatmentCapabilities.TryGetValue(body, out var capabilities))
            return true;

        return TryComp(part, out WoundableComponent? woundable) &&
               _prototypes.TryIndex(woundable.Profile, out var profile) &&
               profile.TreatmentCapabilities.Overlaps(capabilities);
    }

    private EntityUid? PickExplosionAmputationCandidate(
        IReadOnlyList<EntityUid> parts,
        IReadOnlyList<DamageSpecifier> shares)
    {
        var candidates = new List<(EntityUid Part, float Weight)>();
        var totalWeight = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            if (!TryComp(parts[i], out BodyPartComponent? part) ||
                part.PartType == BodyPartType.Torso || // WOLFGATE(Wolfmed): D9
                _body.GetParentPartOrNull(parts[i]) is null || // WOLFGATE(Wolfmed): Shitmed's BodyPartComponent has no Parent field.
                _wfPart.Get(parts[i]).AmputationThresholds.Count == 0) // WOLFGATE(Wolfmed): D8
                continue;

            var weight = Math.Max(0f, shares[i].GetTotal().Float());
            if (weight <= 0f)
                continue;

            candidates.Add((parts[i], weight));
            totalWeight += weight;
        }

        if (candidates.Count == 0)
            return null;

        var roll = _random.NextFloat() * totalWeight;
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll <= 0f)
                return candidate.Part;
        }

        return candidates[^1].Part;
    }

    private bool ApplyPartChange(
        Entity<WoundHostComponent> body,
        EntityUid part,
        DamageSpecifier change,
        EntityUid? origin,
        bool interruptsDoAfters)
    {
        change = FilterPartDamage(part, change);
        if (change.Empty)
            return false;

        if (!_damage.TryChangeDamage(part,
                change,
                out var appliedDamage,
                ignoreResistances: true,
                interruptsDoAfters: interruptsDoAfters,
                origin: origin,
                ignoreGlobalModifiers: true,
                originFlag: _routedModifiers.GetValueOrDefault(body.Owner).OriginFlag))
            return false;

        AccumulateApplied(body, appliedDamage); // WOLFGATE(Wolfmed): D27
        var applied = new PartDamageAppliedEvent(body, part, appliedDamage,
            !_skipWoundHealing.Contains(body), origin);
        RaiseLocalEvent(part, ref applied);
        return true;
    }

    private DamageSpecifier FilterPartDamage(EntityUid part, DamageSpecifier damage, EntityUid? origin = null,
        DamageableSystem.DamageOriginFlag? originFlag = null)
    {
        if (!TryComp(part, out WoundableComponent? woundable) ||
            !_prototypes.TryIndex(woundable.Profile, out var profile))
            return damage;

        var filtered = damage.Clone();
        foreach (var type in filtered.DamageDict.Keys.ToArray())
        {
            if (profile.AcceptedDamageTypes.Count != 0 && !profile.AcceptedDamageTypes.Contains(type))
                filtered.DamageDict.Remove(type);
        }

        var recoveryMultiplier = 1f;
        // A medic also has PassiveDamageComponent. Its presence on the healer does not make a tool
        // treatment passive regeneration (which mechanical profiles deliberately disable).
        if (originFlag == DamageableSystem.DamageOriginFlag.PassiveRecovery)
            recoveryMultiplier = profile.PassiveRecoveryMultiplier;
        else if (origin is { } bed && HasComp<WolfmedBedHealMarkerComponent>(bed)) // WOLFGATE(Wolfmed): HealOnBuckleComponent is server-only here.
            recoveryMultiplier = profile.BedRecoveryMultiplier;

        if (!float.IsFinite(recoveryMultiplier))
            recoveryMultiplier = 1f;
        recoveryMultiplier = Math.Max(0f, recoveryMultiplier);
        if (recoveryMultiplier != 1f)
        {
            foreach (var (type, amount) in filtered.DamageDict.ToArray())
            {
                if (amount < FixedPoint2.Zero)
                    filtered.DamageDict[type] = amount * recoveryMultiplier;
            }
        }

        return filtered;
    }

    private bool CanPartReceiveDamage(EntityUid part, ProtoId<DamageTypePrototype> type)
    {
        return !TryComp(part, out WoundableComponent? woundable) ||
               !_prototypes.TryIndex(woundable.Profile, out var profile) ||
               profile.AcceptedDamageTypes.Count == 0 ||
               profile.AcceptedDamageTypes.Contains(type);
    }

    private bool ApplySystemicDamage(EntityUid body, DamageSpecifier change)
    {
        var systemic = EnsureComp<SystemicDamageComponent>(body);
        var applied = new DamageSpecifier();
        var changed = false;
        foreach (var (type, amount) in change.DamageDict)
        {
            if (!_damage.CanBeDamagedBy(body, type))
                continue;

            var oldValue = systemic.Damage.DamageDict.GetValueOrDefault(type);
            var value = FixedPoint2.Max(FixedPoint2.Zero, systemic.Damage.DamageDict.GetValueOrDefault(type) + amount);

            // WOLFGATE(Wolfmed): AUTODOC5: the body damage cap only ever saw localized (part) damage, so airloss
            // counted without limit on a body that cannot die of the number. BRAIN's hypoxia clock carries
            // the lethality; the reading stops at the old death line.
            if (_wfPart.AirlossCeiling(type) is { } ceiling && value > ceiling)
                value = FixedPoint2.Max(oldValue, ceiling);
            if (value == oldValue)
                continue;

            changed = true;
            applied.DamageDict[type] = value - oldValue;
            if (value == FixedPoint2.Zero)
                systemic.Damage.DamageDict.Remove(type);
            else
                systemic.Damage.DamageDict[type] = value;
        }

        if (!changed)
            return false;

        AccumulateApplied(body, applied); // WOLFGATE(Wolfmed): D27
        Dirty(body, systemic);
        if (TryComp(body, out WoundHostComponent? host) &&
            _targetResolver.TryResolveExact(body, host.SystemicPainTarget, out var painTarget))
            _pain.ApplyDamage(painTarget, applied);
        return true;
    }

    private bool IsAttachedWoundablePart(EntityUid body, EntityUid part)
    {
        return HasComp<WoundableComponent>(part) && _body.BodyHasChild(body, part);
    }
}
