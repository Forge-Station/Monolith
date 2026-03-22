using System.Text.RegularExpressions;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Localization;

namespace Content.Client._NF.Shipyard;

// Forge-Change-full: add locale for vessel names

/// <summary>
///     Resolves vessel-{id}-name / vessel-{id}-desc Fluent keys (ss14-ru prototypes) for shipyard UI.
/// </summary>
public static class VesselDisplayLocale
{
    private static readonly Regex KebabRegex = new("([A-Z])", RegexOptions.Compiled);

    public static string VesselIdToKebab(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;

        return KebabRegex.Replace(id, "-$1").TrimStart('-').ToLowerInvariant();
    }

    public static string GetLocalizedName(ILocalizationManager loc, VesselPrototype proto)
    {
        var key = $"vessel-{VesselIdToKebab(proto.ID)}-name";
        if (loc.TryGetString(key, out var localized))
            return localized;
        return proto.Name;
    }

    public static string GetLocalizedDescription(ILocalizationManager loc, VesselPrototype proto)
    {
        var key = $"vessel-{VesselIdToKebab(proto.ID)}-desc";
        if (loc.TryGetString(key, out var localized))
            return localized;
        return proto.Description;
    }
}
