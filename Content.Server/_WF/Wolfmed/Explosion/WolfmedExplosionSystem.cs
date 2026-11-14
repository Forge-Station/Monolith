using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Explosion;

/// <summary>Routes explosion damage on wound hosts through the distributed per-part split instead of one flat body hit.</summary>
public sealed class WolfmedExplosionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private AmputationSystem _amputation = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Test seam: replaces every blast dismemberment roll with this value.</summary>
    public float? ForcedRoll;

    private float _variation;
    private float _multiplier;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Both CVars have shipped unconsumed since phase 1; this is their first and only reader.
        Subs.CVar(_cfg, CCVars.ExplosionLimbDamageVariation, value => _variation = value, true);
        Subs.CVar(_cfg, CCVars.ExplosionWoundMultiplier, value => _multiplier = value, true);
    }

    /// <summary>True when the blast was spread across a wound host's parts; false means the caller should fall through.</summary>
    public bool TryApplyExplosionDamage(EntityUid entity, DamageSpecifier damage)
    {
        if (!HasComp<WoundHostComponent>(entity))
            return false;

        var applied = _routing.TryRouteDistributedDamage(entity,
            damage,
            TargetBodyPart.All,
            DamageDistribution.SplitWithVariation,
            ignoreResistances: true,
            interruptsDoAfters: true, // M6 (OD18): a blast is a hit, as it is upstream.
            variation: _variation,
            isExplosion: true,
            woundSeverityMultiplier: _multiplier,
            originFlag: DamageableSystem.DamageOriginFlag.Explosion);

        if (applied)
            TryBlastDismember(entity, damage);

        return applied;
    }

    /// <summary>Rolls limbs off a body from the size of the blast it took, armour already counted in.</summary>
    /// <remarks>
    /// Onyx's own explosion severing reads each limb's share against limb totals, which a blast split over ten
    /// parts almost never reaches. This reads the whole blast instead.
    /// </remarks>
    public int TryBlastDismember(EntityUid body, DamageSpecifier damage)
    {
        if (!_cfg.GetCVar(WolfmedCVars.BlastDismember) || TerminatingOrDeleted(body))
            return 0;

        var min = _cfg.GetCVar(WolfmedCVars.BlastDismemberMin);
        var full = Math.Max(min + 1f, _cfg.GetCVar(WolfmedCVars.BlastDismemberFull));
        var total = damage.GetTotal().Float();
        if (total <= min)
            return 0;

        var chance = Math.Clamp((total - min) / (full - min), 0f, 1f) * _cfg.GetCVar(WolfmedCVars.BlastDismemberChance);
        var heads = _cfg.GetCVar(WolfmedCVars.BlastDismemberHead);

        var limbs = new List<EntityUid>();
        foreach (var (id, part) in _body.GetBodyChildren(body))
        {
            if (part.PartType == BodyPartType.Torso || part.PartType == BodyPartType.Head && !heads ||
                !HasComp<WoundableComponent>(id))
                continue;

            limbs.Add(id);
        }

        var severed = 0;
        for (var rolls = 1 + (int) (total / full); rolls > 0 && limbs.Count > 0; rolls--)
        {
            var limb = _random.PickAndTake(limbs);
            if ((ForcedRoll ?? _random.NextFloat()) < chance && _amputation.TryAmputate(body, limb))
                severed++;
        }

        return severed;
    }
}
