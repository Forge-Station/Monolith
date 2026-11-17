namespace Content.Shared._Forge.Company;

/// <summary>
/// A company bonus that stays across shifts once the research archive reaches its threshold.
/// </summary>
public readonly record struct CompanyResearchTier(
    string Name,
    string Purpose,
    long Threshold,
    CompanyProjectEffect Effect,
    int Amount);

public static class CompanyResearch
{
    /// <summary>Percent of a technology's cost that is kept in the company archive.</summary>
    public const int ArchivePercent = 10;

    /// <summary>Purchased discounts and research discounts together cannot exceed this.</summary>
    public const int MaxDiscountPercent = 35;

    public const int MinRelationCooldown = 25;

    public static readonly CompanyResearchTier[] Tiers =
    [
        new("company-research-supply", "company-research-supply-purpose", 250_000, CompanyProjectEffect.SupplyDiscount, 5),
        new("company-research-ships", "company-research-ships-purpose", 1_000_000, CompanyProjectEffect.ShipDiscount, 5),
        new("company-research-diplomacy", "company-research-diplomacy-purpose", 3_000_000, CompanyProjectEffect.RelationCooldown, 20),
        new("company-research-roster", "company-research-roster-purpose", 8_000_000, CompanyProjectEffect.MemberSlots, 8),
        new("company-research-archive", "company-research-archive-purpose", 20_000_000, CompanyProjectEffect.DiplomacyDiscount, 10),
    ];
}
