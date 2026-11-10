using System.Text;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode.Export;

/// <summary>
/// What an export packages: a shipyard vessel (one level, one grid), a map of any level count, or just the one grid
/// file with no prototype and no zip, for POIs and other grids that aren't ships.
/// </summary>
[Serializable, NetSerializable]
public enum WFMappingExportKind : byte
{
    Ship,
    Map,
    Grid,
}

/// <summary>
/// The vessel fields a mapper fills in for a ship. Empty text, a price of 0, <see cref="VesselSize.All"/> and
/// <see cref="AutoEngines"/> leave that field to the generator's guess from the grid.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingShipDetails
{
    /// <summary>The vessel's name; empty uses the map name.</summary>
    public string Name = string.Empty;

    public string Description = string.Empty;

    /// <summary>The price in spesos; 0 takes the generator's price over the appraisal.</summary>
    public int Price;

    /// <summary>The size category; <see cref="VesselSize.All"/> takes the size from the tile count.</summary>
    public VesselSize Category = VesselSize.All;

    /// <summary>The purposes; empty takes the classes the scan found.</summary>
    public List<VesselClass> Classes = new();

    public ShipyardConsoleUiKey Group = ShipyardConsoleUiKey.Shipyard;

    /// <summary>Whether the engines come from the scan rather than <see cref="Engines"/>.</summary>
    public bool AutoEngines = true;

    public List<VesselEngine> Engines = new();

    /// <summary>The access level needed to buy it; empty is none.</summary>
    public string Access = string.Empty;

    public bool RequireCrew;

    public WFMappingShipDetails Clone()
    {
        return new WFMappingShipDetails
        {
            Name = Name,
            Description = Description,
            Price = Price,
            Category = Category,
            Classes = new List<VesselClass>(Classes),
            Group = Group,
            AutoEngines = AutoEngines,
            Engines = new List<VesselEngine>(Engines),
            Access = Access,
            RequireCrew = RequireCrew,
        };
    }
}

/// <summary>Bounds on the details a mapper sends; <see cref="Sanitize"/> brings any details inside them.</summary>
public static class WFMappingExportLimits
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxAccessLength = 64;

    /// <summary>Most classes or engines one vessel lists.</summary>
    public const int MaxListEntries = 8;

    /// <summary>The highest price a mapper may set; 0 means the generated one.</summary>
    public const int MaxPrice = 100_000_000;

    /// <summary>
    /// A copy with every field inside the bounds: an invalid name or access goes empty, text is cut, the price is
    /// clamped, unknown or placeholder enum values are dropped and lists are deduplicated and cut.
    /// </summary>
    public static WFMappingShipDetails Sanitize(WFMappingShipDetails details)
    {
        var clean = new WFMappingShipDetails
        {
            Name = details.Name.Length <= WFMappingModeLimits.MaxNameLength * 2 &&
                   WFMappingLibraryNames.TryNormalizeMapName(details.Name, out var name)
                ? name
                : string.Empty,
            Description = CleanText(details.Description, MaxDescriptionLength),
            Price = details.Price <= 0 ? 0 : Math.Clamp(details.Price, 1, MaxPrice),
            Category = Enum.IsDefined(details.Category) ? details.Category : VesselSize.All,
            Group = Enum.IsDefined(details.Group) ? details.Group : ShipyardConsoleUiKey.Shipyard,
            AutoEngines = details.AutoEngines,
            Access = IsValidAccess(details.Access) ? details.Access : string.Empty,
            RequireCrew = details.RequireCrew,
        };

        foreach (var vesselClass in details.Classes)
        {
            if (clean.Classes.Count >= MaxListEntries)
                break;

            if (Enum.IsDefined(vesselClass) && vesselClass != VesselClass.All && !clean.Classes.Contains(vesselClass))
                clean.Classes.Add(vesselClass);
        }

        foreach (var engine in details.Engines)
        {
            if (clean.Engines.Count >= MaxListEntries)
                break;

            if (Enum.IsDefined(engine) && engine != VesselEngine.All && !clean.Engines.Contains(engine))
                clean.Engines.Add(engine);
        }

        return clean;
    }

    /// <summary>Whether an access id is 1 to 64 letters, digits or underscores, as access level prototype ids are.</summary>
    public static bool IsValidAccess(string access)
    {
        if (access.Length is 0 or > MaxAccessLength)
            return access.Length == 0;

        foreach (var c in access)
        {
            if (c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                return false;
        }

        return true;
    }

    /// <summary>Trims text, drops control characters other than line breaks and cuts it to a length.</summary>
    public static string CleanText(string text, int maxLength)
    {
        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (var c in text)
        {
            if (builder.Length >= maxLength)
                break;

            if (c == '\r')
                continue;

            if (c == '\n' || !char.IsControl(c))
                builder.Append(c);
        }

        return builder.ToString().Trim();
    }
}

/// <summary>
/// Packages a library version as one zip, or one grid file for <see cref="WFMappingExportKind.Grid"/>, and downloads
/// it: the newest version when <see cref="Version"/> is null. A ship export stores <see cref="Details"/> as the map's
/// ship details first.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingExportRequest(string mapName, string? version, WFMappingExportKind kind,
    WFMappingShipDetails? details) : EntityEventArgs
{
    public readonly string MapName = mapName;
    public readonly string? Version = version;
    public readonly WFMappingExportKind Kind = kind;
    public readonly WFMappingShipDetails? Details = details;
}

/// <summary>Moves the mapper's observer to an issue's entity, or else to its coordinates, on one of their session's maps.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingGoToIssueRequest(NetEntity? entity, NetCoordinates? where) : EntityEventArgs
{
    public readonly NetEntity? Entity = entity;
    public readonly NetCoordinates? Where = where;
}
