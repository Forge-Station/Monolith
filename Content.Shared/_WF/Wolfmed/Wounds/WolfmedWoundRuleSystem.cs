using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Chooses the wound a hit makes from <see cref="WolfmedWoundRulePrototype"/> data instead of the one
/// default wound Onyx keeps per damage type. Answers <see cref="WolfmedWoundSelectionEvent"/>, which the
/// vendored WoundSystem raises once per damage type per hit.
/// </summary>
/// <remarks>
/// To add a wound: write the wound prototype with <c>ruleOnly: true</c>, list it in the part profile's
/// <c>supportedWounds</c>, and add a <c>wolfmedWoundRule</c> naming it. No C# is needed.
/// </remarks>
public sealed class WolfmedWoundRuleSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedChemicalBurnSystem _chemicalBurns = default!; // M5 (P21)

    /// <summary>Damage types explosions scale the severity of, mirroring WoundSystem.HandlePartDamageApplied.</summary>
    private static readonly HashSet<ProtoId<DamageTypePrototype>> ExplosionScaledTypes =
        ["Blunt", "Slash", "Piercing", "Heat", "Cold"];

    private readonly Dictionary<ProtoId<DamageTypePrototype>, List<WolfmedWoundRulePrototype>> _rulesByDamageType = new();
    private readonly List<WolfmedWoundRulePrototype> _anyTypeRules = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        RebuildRuleCache();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<WoundableComponent, WolfmedWoundSelectionEvent>(OnWoundSelection);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WolfmedWoundRulePrototype>())
            RebuildRuleCache();
    }

    private void RebuildRuleCache()
    {
        _rulesByDamageType.Clear();
        _anyTypeRules.Clear();
        foreach (var rule in _prototypes.EnumeratePrototypes<WolfmedWoundRulePrototype>())
        {
            if (rule.DamageTypes.Count == 0)
            {
                _anyTypeRules.Add(rule);
                continue;
            }

            foreach (var type in rule.DamageTypes)
            {
                if (!_rulesByDamageType.TryGetValue(type, out var rules))
                    _rulesByDamageType[type] = rules = new();

                rules.Add(rule);
            }
        }

        _anyTypeRules.Sort(ByPriority);
        foreach (var rules in _rulesByDamageType.Values)
            rules.Sort(ByPriority);
    }

    private static int ByPriority(WolfmedWoundRulePrototype a, WolfmedWoundRulePrototype b)
    {
        var priority = b.Priority.CompareTo(a.Priority);
        return priority != 0 ? priority : string.CompareOrdinal(a.ID, b.ID);
    }

    /// <summary>
    /// What dealt this hit. The tool is the projectile for gunfire and the swung weapon for melee, so a
    /// weapon that is the attacker itself is an unarmed attack. Anything with neither is environmental.
    /// </summary>
    public WolfmedWoundCause GetCause(EntityUid? origin, EntityUid? tool, bool isExplosion)
    {
        var cause = WolfmedWoundCause.None;
        if (isExplosion)
            cause |= WolfmedWoundCause.Explosion;

        if (tool is { } used && !TerminatingOrDeleted(used))
        {
            if (TryComp(used, out WolfmedDamageCauseComponent? declared))
                cause |= declared.Cause;

            if (HasComp<ProjectileComponent>(used))
                cause |= WolfmedWoundCause.Projectile;
            else if (HasComp<HitscanBasicDamageComponent>(used)) // P6: the tool of a beam hit is the beam entity itself.
                cause |= WolfmedWoundCause.Hitscan;
            else if (HasComp<ThrownItemComponent>(used))
                cause |= WolfmedWoundCause.Thrown;
            else if (used == origin)
                cause |= WolfmedWoundCause.Unarmed;
            else if (HasComp<MeleeWeaponComponent>(used))
                cause |= WolfmedWoundCause.Melee;
        }

        return cause == WolfmedWoundCause.None ? WolfmedWoundCause.Environmental : cause;
    }

    private void OnWoundSelection(Entity<WoundableComponent> part, ref WolfmedWoundSelectionEvent args)
    {
        var cause = GetCause(args.Origin, args.Tool, args.IsExplosion);

        // W4's seam: the directed selection event has one owner, so systems that answer a hit rather than a
        // wound (cautery, W5/W6) listen to this broadcast instead.
        var hit = new WolfmedPartDamageEvent(args.Body, part, args.DamageType, args.Amount, cause,
            args.Origin, args.Tool);
        RaiseLocalEvent(ref hit);

        // M5 (P21): acid residue still on the skin deepens the chemical burn it came from, never a plain burn.
        if (_chemicalBurns.ResidueTarget is { } residue && residue.Part == part.Owner)
        {
            _wounds.CreateOrMergeWound(part.AsNullable(), residue.Wound, args.Amount);
            args.SuppressDefault = true;
            return;
        }

        if (!HasRules(args.DamageType))
            return;

        var multiplier = args.IsExplosion && ExplosionScaledTypes.Contains(args.DamageType)
            ? args.SeverityMultiplier
            : 1f;

        foreach (var rule in EnumerateRules(args.DamageType))
        {
            if (!Matches(rule, part, args.Amount, cause) ||
                rule.Chance < 1f && !Roll(Math.Clamp(rule.Chance, 0f, 1f)))
                continue;

            Apply(rule, part, args.Amount * rule.SeverityMultiplier * multiplier);
            if (rule.ReplacesDefault)
                args.SuppressDefault = true;

            if (!rule.Continue)
                return;
        }
    }

    private bool HasRules(ProtoId<DamageTypePrototype> type) =>
        _anyTypeRules.Count > 0 || _rulesByDamageType.ContainsKey(type);

    private IEnumerable<WolfmedWoundRulePrototype> EnumerateRules(ProtoId<DamageTypePrototype> type)
    {
        if (!_rulesByDamageType.TryGetValue(type, out var typed))
            return _anyTypeRules;
        if (_anyTypeRules.Count == 0)
            return typed;

        var merged = new List<WolfmedWoundRulePrototype>(typed.Count + _anyTypeRules.Count);
        merged.AddRange(typed);
        merged.AddRange(_anyTypeRules);
        merged.Sort(ByPriority);
        return merged;
    }

    private bool Matches(
        WolfmedWoundRulePrototype rule,
        Entity<WoundableComponent> part,
        FixedPoint2 amount,
        WolfmedWoundCause cause)
    {
        if (amount < rule.MinDamage || rule.MaxDamage is { } max && amount > max)
            return false;

        if (rule.Causes.Count > 0 && !rule.Causes.Exists(required => (cause & required) != WolfmedWoundCause.None))
            return false;

        if (rule.PartTypes.Count > 0 &&
            (!TryComp(part, out BodyPartComponent? bodyPart) || !rule.PartTypes.Contains(bodyPart.PartType)))
            return false;

        if (rule.Capabilities.Count > 0 &&
            (!_prototypes.TryIndex(part.Comp.Profile, out var profile) ||
             !profile.TreatmentCapabilities.Overlaps(rule.Capabilities)))
            return false;

        return _wounds.CanCreateWound(part.AsNullable(), rule.Wound);
    }

    private void Apply(WolfmedWoundRulePrototype rule, Entity<WoundableComponent> part, FixedPoint2 severity)
    {
        severity = FixedPoint2.Max(severity, rule.MinSeverity);
        if (_wounds.CreateOrMergeWound(part.AsNullable(), rule.Wound, severity) is not { } wound ||
            rule.Embedded is not { } embedded)
            return;

        var count = embedded.MaxCount > embedded.MinCount
            ? _random.Next(embedded.MinCount, embedded.MaxCount + 1)
            : embedded.MinCount;
        _embedded.Add(wound, embedded.Item, count, embedded.MaxTotal);
    }

    /// <summary>Test seam: when set, a rule's chance passes exactly when this value is below it.</summary>
    public float? ForcedRoll;

    private bool Roll(float chance) => ForcedRoll is { } forced ? forced < chance : _random.Prob(chance);
}
