using Content.Shared._WF.Drydock;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Ship refits: the tiers a ship owns, what falls short of them, and what it costs to bring it up. The work on the
/// ship itself is each refit kind's, through its <see cref="WFShipRefitEffect"/>.
/// </summary>
public sealed class WFShipRefitSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <summary>The tier and coverage a ship owns of a refit line, or zeros.</summary>
    public (int Tier, int Coverage) GetRefit(EntityUid ship, string refit)
    {
        return Find(ship, refit) is { } owned ? (owned.Tier, owned.Coverage) : (0, 0);
    }

    /// <summary>Every refit line a ship owns, with its prototype.</summary>
    public List<(WFShipRefitPrototype Proto, WFShipRefit Owned)> Owned(EntityUid ship)
    {
        var result = new List<(WFShipRefitPrototype, WFShipRefit)>();
        if (!TryComp<WFShipRefitsComponent>(ship, out var refits))
            return result;

        foreach (var owned in refits.Refits)
        {
            if (owned.Tier > 0 && _prototype.TryIndex(owned.Refit, out var proto))
                result.Add((proto, owned));
        }

        return result;
    }

    /// <summary>Records the tier a ship owns of a refit line and the units it paid for there.</summary>
    public void SetRefit(EntityUid ship, string refit, int tier, int coverage)
    {
        var refits = EnsureComp<WFShipRefitsComponent>(ship);
        var owned = Find(ship, refit);
        if (owned == null)
        {
            owned = new WFShipRefit { Refit = refit };
            refits.Refits.Add(owned);
        }

        owned.Tier = tier;
        owned.Coverage = coverage;
    }

    /// <summary>Surveys a ship's units of a refit line against a tier.</summary>
    public WFRefitSurvey Survey(EntityUid ship, WFShipRefitPrototype refit, int tier)
    {
        var survey = new WFRefitSurvey(ship, tier);
        refit.Effect.Survey(EntityManager, survey);
        return survey;
    }

    /// <summary>The units below a tier a stock rebuild of a prototype would have, all zero if the line doesn't cover it.</summary>
    public int[] Prospect(WFShipRefitPrototype refit, EntProtoId prototype, int tier)
    {
        var prospect = new WFRefitProspect(prototype, tier);
        refit.Effect.Prospect(EntityManager, prospect);
        return prospect.Lacking;
    }

    /// <summary>Brings a target up to a tier of a refit line, if the line covers it.</summary>
    public void Apply(WFShipRefitPrototype refit, EntityUid target, int tier)
    {
        refit.Effect.Apply(EntityManager, target, tier);
    }

    /// <summary>
    /// The tier the next refit of a line buys: the first past the ship's own that some unit falls short of, or 0. Gives
    /// the survey against it.
    /// </summary>
    public int NextTier(EntityUid ship, WFShipRefitPrototype refit, out WFRefitSurvey? survey)
    {
        survey = null;
        for (var tier = GetRefit(ship, refit.ID).Tier + 1; tier <= refit.Tiers.Count; tier++)
        {
            survey = Survey(ship, refit, tier);
            if (survey.Below.Count > 0)
                return tier;
        }

        return 0;
    }

    /// <summary>Units in a survey that reach a tier at or below the surveyed one.</summary>
    private static int UnitsReaching(WFRefitSurvey survey, int tier)
    {
        var units = survey.UnitsAtTier;
        foreach (var target in survey.Below)
        {
            for (var reached = tier; reached < target.Lacking.Length; reached++)
            {
                units += target.Lacking[reached];
            }
        }

        return units;
    }

    /// <summary>All units in a survey, at the tier or below it.</summary>
    public static int Units(WFRefitSurvey survey)
    {
        return UnitsReaching(survey, 0);
    }

    /// <summary>
    /// Units the ship paid for at its tier of a line that no longer reach it, so they are restored at the repair share.
    /// Takes a survey against the ship's tier or above.
    /// </summary>
    public int CoverageLeft(EntityUid ship, WFShipRefitPrototype refit, WFRefitSurvey survey)
    {
        var (owned, coverage) = GetRefit(ship, refit.ID);
        return owned == 0 ? 0 : Math.Max(0, coverage - UnitsReaching(survey, owned));
    }

    /// <summary>
    /// Prices bringing units up to a tier, by the tier each reaches (<paramref name="lacking"/>). A unit within
    /// <paramref name="coverageLeft"/> pays <paramref name="repairShare"/> for the tiers the ship owns; every other tier
    /// is paid in full. Adds the material bill to <paramref name="materials"/>, and the units paid in full, by the tier each
    /// reaches, to <paramref name="full"/>.
    /// </summary>
    public int Price(WFShipRefitPrototype refit, int owned, int[] lacking, ref int coverageLeft, float repairShare,
        Dictionary<string, int> materials, int[] full)
    {
        var price = 0d;
        var bill = new Dictionary<string, double>();
        for (var reached = 0; reached < lacking.Length; reached++)
        {
            var count = lacking[reached];
            var covered = reached < owned ? Math.Min(count, coverageLeft) : 0;
            coverageLeft -= covered;
            full[reached] = count - covered;

            for (var tier = reached; tier < lacking.Length && tier < refit.Tiers.Count; tier++)
            {
                var units = tier < owned ? covered * repairShare + (count - covered) : count;
                price += refit.Tiers[tier].Price * units;
                foreach (var (material, amount) in refit.Tiers[tier].Materials)
                {
                    bill[material.Id] = bill.GetValueOrDefault(material.Id) + amount * units;
                }
            }
        }

        foreach (var (material, amount) in bill)
        {
            materials[material] = materials.GetValueOrDefault(material) + (int) Math.Ceiling(amount);
        }

        return (int) Math.Ceiling(price);
    }

    /// <summary>Material units to bring one unit from a tier it reaches to a tier, in full.</summary>
    public static Dictionary<string, int> UnitMaterials(WFShipRefitPrototype refit, int reached, int tier)
    {
        var bill = new Dictionary<string, int>();
        for (var t = reached; t < tier && t < refit.Tiers.Count; t++)
        {
            foreach (var (material, amount) in refit.Tiers[t].Materials)
            {
                bill[material.Id] = bill.GetValueOrDefault(material.Id) + amount;
            }
        }

        return bill;
    }

    private WFShipRefit? Find(EntityUid ship, string refit)
    {
        if (!TryComp<WFShipRefitsComponent>(ship, out var refits))
            return null;

        foreach (var owned in refits.Refits)
        {
            if (owned.Refit.Id == refit)
                return owned;
        }

        return null;
    }
}
