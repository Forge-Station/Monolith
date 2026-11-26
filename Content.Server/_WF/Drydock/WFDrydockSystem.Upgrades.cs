using Content.Server.Construction;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    private void InitializeUpgrades()
    {
        SubscribeLocalEvent<WFDrydockCraneUpgradesComponent, RefreshPartsEvent>(OnCraneRefreshParts);
        SubscribeLocalEvent<WFDrydockCraneUpgradesComponent, UpgradeExamineEvent>(OnCraneUpgradeExamine);
    }

    /// <summary>Sets a crane's speed, reach and working draw from the tiers of its installed parts.</summary>
    private void OnCraneRefreshParts(Entity<WFDrydockCraneUpgradesComponent> ent, ref RefreshPartsEvent args)
    {
        if (!TryComp<WFDrydockCraneComponent>(ent, out var crane))
            return;

        var upgrades = ent.Comp;
        upgrades.SpeedTier = PartTier(upgrades, args, upgrades.SpeedPart);
        upgrades.ReachTier = PartTier(upgrades, args, upgrades.ReachPart);
        upgrades.EfficiencyTier = PartTier(upgrades, args, upgrades.EfficiencyPart);

        crane.SpeedMultiplier = AtTier(upgrades.Speed, upgrades.SpeedTier);
        if (upgrades.Reach.Count > 0)
        {
            var reach = upgrades.Reach[Math.Min(upgrades.ReachTier, upgrades.Reach.Count) - 1];
            crane.MaxArm = reach.MaxArm;
            crane.Sections = reach.Sections;
            crane.SectionLength = reach.SectionLength;
        }

        crane.WorkingLoad = upgrades.WorkingLoad
            * AtTier(upgrades.SpeedLoad, upgrades.SpeedTier)
            * AtTier(upgrades.ReachLoad, upgrades.ReachTier)
            * AtTier(upgrades.Efficiency, upgrades.EfficiencyTier);

        if (crane.Active && TryComp<PowerConsumerComponent>(ent, out var consumer))
            consumer.DrawRate = crane.WorkingLoad;

        // The boom path cache is sized to the old reach.
        _boomBlock.Remove(ent.Owner);
        Dirty(ent, crane);
    }

    private void OnCraneUpgradeExamine(Entity<WFDrydockCraneUpgradesComponent> ent, ref UpgradeExamineEvent args)
    {
        if (!TryComp<WFDrydockCraneComponent>(ent, out var crane))
            return;

        var upgrades = ent.Comp;
        var stock = upgrades.Reach.Count > 0 ? upgrades.Reach[0] : null;
        args.AddPercentageUpgrade("drydock-crane-upgrade-speed", crane.SpeedMultiplier);
        if (stock != null)
        {
            var stockWidth = stock.SectionLength + (stock.Sections - 1) * (stock.SectionLength - 1);
            args.AddNumberUpgrade("drydock-crane-upgrade-length", (int) MathF.Round(crane.MaxArm - stock.MaxArm));
            args.AddNumberUpgrade("drydock-crane-upgrade-width", (int) MathF.Round(crane.MaxExtension - stockWidth));
        }

        args.AddPercentageUpgrade("drydock-crane-upgrade-load", crane.WorkingLoad / upgrades.WorkingLoad);
    }

    /// <summary>The tier an average part rating reaches: one plus how many of the tier ratings it meets.</summary>
    private static int PartTier(WFDrydockCraneUpgradesComponent upgrades, RefreshPartsEvent args, string part)
    {
        var rating = args.PartRatings.GetValueOrDefault(part, 1f);
        var tier = 1;
        foreach (var threshold in upgrades.Ratings)
        {
            if (rating < threshold)
                break;

            tier++;
        }

        return tier;
    }

    /// <summary>A per-tier value, holding the last entry for tiers past the list.</summary>
    private static float AtTier(List<float> values, int tier)
    {
        return values.Count == 0 ? 1f : values[Math.Clamp(tier, 1, values.Count) - 1];
    }
}
