using Content.Shared.Access;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Company;

/// <summary>
/// Door / ID access tier within a company.
/// </summary>
public enum CompanyAccessTier : byte
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public static class CompanyAccessHelper
{
    public static string GetAccessLevelId(ProtoId<CompanyPrototype> company, CompanyAccessTier tier)
    {
        return tier switch
        {
            CompanyAccessTier.Low => $"{company.Id}TierLow",
            CompanyAccessTier.Medium => $"{company.Id}TierMedium",
            CompanyAccessTier.High => $"{company.Id}TierHigh",
            _ => string.Empty,
        };
    }

    public static string GetAccessGroupId(ProtoId<CompanyPrototype> company, CompanyAccessTier tier)
    {
        return GetAccessLevelId(company, tier);
    }

    public static ProtoId<AccessLevelPrototype>? TryGetAccessLevel(ProtoId<CompanyPrototype> company, CompanyAccessTier tier)
    {
        if (tier == CompanyAccessTier.None)
            return null;

        return GetAccessLevelId(company, tier);
    }

    /// <summary>
    /// Tags granted for a tier (nested: High includes Medium and Low).
    /// </summary>
    public static List<ProtoId<AccessLevelPrototype>> GetTagsForTier(ProtoId<CompanyPrototype> company, CompanyAccessTier tier)
    {
        var tags = new List<ProtoId<AccessLevelPrototype>>();
        if (tier >= CompanyAccessTier.Low)
            tags.Add(GetAccessLevelId(company, CompanyAccessTier.Low));
        if (tier >= CompanyAccessTier.Medium)
            tags.Add(GetAccessLevelId(company, CompanyAccessTier.Medium));
        if (tier >= CompanyAccessTier.High)
            tags.Add(GetAccessLevelId(company, CompanyAccessTier.High));
        return tags;
    }

    /// <summary>
    /// Whether this user may change a door from <paramref name="current"/> to <paramref name="target"/>.
    /// Cannot touch or set tiers above their own access.
    /// </summary>
    public static bool CanConfigureDoorMode(CompanyAccessTier userTier, CompanyDoorAccessMode current, CompanyDoorAccessMode target)
    {
        if ((CompanyAccessTier)current > userTier)
            return false;
        if ((CompanyAccessTier)target > userTier)
            return false;
        return true;
    }
}
