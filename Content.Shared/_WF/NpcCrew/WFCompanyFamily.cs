namespace Content.Shared._WF.NpcCrew;

/// <summary>
/// The faction a company belongs to. The Federation and the Dynasty each have a navy, a civilian arm and a high
/// command as separate companies; crews treat all of one faction's companies as their own.
/// </summary>
public static class WFCompanyFamily
{
    /// <summary>The faction of a company id, the id itself for a company with no family, or empty for none.</summary>
    public static string Of(string? company)
    {
        if (string.IsNullOrEmpty(company) || company == "None")
            return string.Empty;
        if (company.StartsWith("TSF", StringComparison.Ordinal))
            return "TSF";
        return company.StartsWith("PDV", StringComparison.Ordinal) ? "PDV" : company;
    }

    /// <summary>Whether two company ids are of one faction. Two ships with no company are not.</summary>
    public static bool Same(string? first, string? second)
    {
        var family = Of(first);
        return family.Length > 0 && family == Of(second);
    }
}
