using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>A makeshift tourniquet slips off its limb when that limb takes a hard hit.</summary>
// A real strap holds through anything (playtest 4); a strip of cloth does not. Only a hit on the strapped part itself
// counts, and only one that can interrupt a do-after: fire, bleeding and other ticks never knock it loose.
public sealed class WolfmedTourniquetSlipSystem : EntitySystem
{
    [Dependency] private WolfmedNecrosisSystem _necrosis = default!;

    /// <summary>Records on the tied part how hard a hit its strap takes. Called by TourniquetSystem once it is on.</summary>
    public void OnApplied(EntityUid part, EntityUid strap)
    {
        if (TryComp(part, out WolfmedTourniquetComponent? tied))
            tied.SlipDamage = CompOrNull<WolfmedMakeshiftTourniquetComponent>(strap)?.SlipDamage;
    }

    /// <summary>Called once per hit from <see cref="WolfmedPartHitSystem.OnHit"/>. True when the strap slipped.</summary>
    public bool OnHit(EntityUid part, PartDamageAppliedEvent hit)
    {
        if (!hit.InterruptsDoAfters || !TryComp(part, out WolfmedTourniquetComponent? tied) ||
            tied.SlipDamage is not { } line ||
            DamageSpecifier.GetPositive(hit.Total).GetTotal() < FixedPoint2.New(line))
            return false;

        return _necrosis.Slip(hit.Body, part);
    }
}
