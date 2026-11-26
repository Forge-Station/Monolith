using System.Collections.Generic;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Content.Server._WF.Wolfmed.Life;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// Playtest 5, "radiation medicine causes lots of body damage that the new medical system takes to overdrive": what a
/// metabolising reagent deals to a wound host is toxin load, not wounds. A wound is an injury with a site and a chemical
/// in the blood has none, so every unit of brute, burn, cold, shock or caustic such a reagent would deal becomes
/// wolfmed.reagent_toxin_factor units of systemic Poison for the liver to clear. So does Asphyxiation and Bloodloss, the
/// bookkeeping types the model reads only during real suffocation, or never, so an overdose written as airloss still
/// harms. Healing, Poison and the other systemic types pass through unchanged, and a reaction on the skin is not a
/// metabolism.
/// </summary>
public sealed class WolfmedReagentDamageSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    public static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    /// <summary>Systemic types that are bookkeeping on a wound host (capped at wolfmed.airloss_cap, read by no route).</summary>
    private static readonly HashSet<string> Bookkeeping = new() { "Asphyxiation", "Bloodloss" };

    /// <summary>The change a metabolising reagent's effect applies to a wound host: the change itself when nothing converts.</summary>
    public DamageSpecifier ForWoundHost(EntityUid body, DamageSpecifier change)
    {
        // Pre-merge review: a chassis has no liver to clear a toxin load, so its reagent damage stays what it was.
        if (!TryComp(body, out WoundHostComponent? host) || _shutdown.IsMechanical(body))
            return change;

        DamageSpecifier? converted = null;
        var toxin = FixedPoint2.Zero;
        var factor = _cfg.GetCVar(WolfmedCVars.ReagentToxinFactor);
        foreach (var (type, amount) in change.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !host.LocalizedDamageTypes.Contains(type) && !Bookkeeping.Contains(type))
                continue;

            converted ??= new DamageSpecifier(change);
            converted.DamageDict.Remove(type);
            toxin += amount * factor;
        }

        if (converted == null)
            return change;

        if (toxin > FixedPoint2.Zero)
            converted.DamageDict[Poison] = converted.DamageDict.GetValueOrDefault(Poison) + toxin;
        return converted;
    }
}
