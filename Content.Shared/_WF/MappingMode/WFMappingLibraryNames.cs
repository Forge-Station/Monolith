using System.Globalization;
using System.Text;
using Content.Shared._WF.MappingMode.Export;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.MappingMode;

/// <summary>How a library version was written.</summary>
public enum WFMappingVersionKind : byte
{
    Manual,
    Auto,

    /// <summary>A map file the player uploaded, as the server saved it again after its checks.</summary>
    Upload,
}

/// <summary>A library version as its folder name spells it: number, UTC time of the save and kind.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingVersionName(int Number, DateTime Stamp, WFMappingVersionKind Kind)
{
    /// <summary>The folder name, <c>v0001_20261018-142501_manual</c>.</summary>
    public string Folder => WFMappingLibraryNames.FormatVersion(Number, Stamp, Kind);
}

/// <summary>
/// Names and paths of the per-player map library,
/// <c>/Mapping/Library/&lt;NetUserId&gt;/&lt;MapName&gt;/v&lt;NNNN&gt;_&lt;yyyyMMdd-HHmmss&gt;_&lt;kind&gt;/z&lt;D&gt;.yml</c> with
/// <c>z&lt;D&gt;.grid.yml</c> and <c>z&lt;D&gt;.vessel.yml</c> beside a level holding one grid, and of downloads.
/// </summary>
public static class WFMappingLibraryNames
{
    /// <summary>The folder in the server's data directory holding every player's library.</summary>
    public static readonly ResPath Root = new("/Mapping/Library");

    /// <summary>The folder in the client's data directory downloads are written to.</summary>
    public static readonly ResPath DownloadRoot = new("/Mapping");

    private const string StampFormat = "yyyyMMdd-HHmmss";
    private const string ManualSuffix = "manual";
    private const string AutoSuffix = "auto";
    private const string UploadSuffix = "upload";

    /// <summary>The map name an upload gets when its file name has nothing usable.</summary>
    public const string UploadFallbackName = "Upload";

    /// <summary>Names Windows refuses as file or folder names, whatever the case.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Trims a map name and checks it: 1 to 48 letters, digits, spaces, dashes or underscores, and not a name the
    /// file system reserves.
    /// </summary>
    public static bool TryNormalizeMapName(string raw, out string name)
    {
        name = raw.Length > WFMappingModeLimits.MaxNameLength * 2 ? string.Empty : raw.Trim();
        return IsValidMapName(name);
    }

    /// <summary>Whether an already trimmed name is a valid map name.</summary>
    public static bool IsValidMapName(string name)
    {
        return WFMappingModeLimits.IsValidName(name) && !Reserved.Contains(name);
    }

    public static string FormatVersion(int number, DateTime stamp, WFMappingVersionKind kind)
    {
        var suffix = kind switch
        {
            WFMappingVersionKind.Auto => AutoSuffix,
            WFMappingVersionKind.Upload => UploadSuffix,
            _ => ManualSuffix,
        };
        return $"v{number.ToString("D4", CultureInfo.InvariantCulture)}_{stamp.ToString(StampFormat, CultureInfo.InvariantCulture)}_{suffix}";
    }

    /// <summary>Reads a version folder name in the form <see cref="FormatVersion"/> writes.</summary>
    public static bool TryParseVersion(string folder, out WFMappingVersionName version)
    {
        version = default;
        var parts = folder.Split('_');
        if (parts.Length != 3 || parts[0].Length < 5 || parts[0].Length > 10 || parts[0][0] != 'v')
            return false;

        var digits = parts[0].AsSpan(1);
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
                return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            return false;

        if (!DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var stamp))
        {
            return false;
        }

        WFMappingVersionKind kind;
        switch (parts[2])
        {
            case ManualSuffix:
                kind = WFMappingVersionKind.Manual;
                break;
            case AutoSuffix:
                kind = WFMappingVersionKind.Auto;
                break;
            case UploadSuffix:
                kind = WFMappingVersionKind.Upload;
                break;
            default:
                return false;
        }

        version = new WFMappingVersionName(number, stamp, kind);
        return version.Folder == folder;
    }

    /// <summary>
    /// A map name made from an uploaded file's name: the part after the last slash, without <c>.yml</c>, kept to the
    /// name alphabet and length; <see cref="UploadFallbackName"/> when nothing valid is left.
    /// </summary>
    public static string UploadNameFromFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return UploadFallbackName;

        var start = fileName.LastIndexOfAny(new[] { '/', '\\' }) + 1;
        var end = fileName.Length;
        if (fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            end -= 4;

        var builder = new StringBuilder(WFMappingModeLimits.MaxNameLength);
        for (var i = start; i < end && builder.Length < WFMappingModeLimits.MaxNameLength; i++)
        {
            var c = fileName[i];
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '_' or '-')
                builder.Append(c);
        }

        var name = builder.ToString().Trim();
        return IsValidMapName(name) ? name : UploadFallbackName;
    }

    private const string LevelSuffix = ".yml";
    private const string GridSuffix = ".grid.yml";
    private const string VesselSuffix = ".vessel.yml";
    private const string ScanSuffix = ".scan.yml";
    private const string ZipSuffix = ".zip";

    /// <summary>The mapper's ship details, kept in a version folder and carried into the next version.</summary>
    public const string ShipDetailsFile = "ship.yml";

    /// <summary>The file a level is saved to inside a version folder: <c>z0.yml</c> for the ground.</summary>
    public static string LevelFile(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}{LevelSuffix}";
    }

    /// <summary>The grid file saved beside a level that holds exactly one grid: <c>z0.grid.yml</c>.</summary>
    public static string GridFile(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}{GridSuffix}";
    }

    /// <summary>The generated vessel prototype saved beside a grid file: <c>z0.vessel.yml</c>.</summary>
    public static string VesselFile(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}{VesselSuffix}";
    }

    /// <summary>What the vessel generator read off a grid, saved beside its grid file: <c>z0.scan.yml</c>.</summary>
    public static string ScanFile(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}{ScanSuffix}";
    }

    /// <summary>Reads a scan file name back to its depth.</summary>
    public static bool TryParseScanFile(string fileName, out int depth)
    {
        return TryParseDepthFile(fileName, ScanSuffix, out depth) && ScanFile(depth) == fileName;
    }

    /// <summary>Reads a level file name back to its depth.</summary>
    public static bool TryParseLevelFile(string fileName, out int depth)
    {
        return TryParseDepthFile(fileName, LevelSuffix, out depth) && LevelFile(depth) == fileName;
    }

    /// <summary>Reads a grid file name back to its depth.</summary>
    public static bool TryParseGridFile(string fileName, out int depth)
    {
        return TryParseDepthFile(fileName, GridSuffix, out depth) && GridFile(depth) == fileName;
    }

    /// <summary>Reads a vessel file name back to its depth.</summary>
    public static bool TryParseVesselFile(string fileName, out int depth)
    {
        return TryParseDepthFile(fileName, VesselSuffix, out depth) && VesselFile(depth) == fileName;
    }

    /// <summary>Reads <c>z&lt;D&gt;</c> followed by exactly <paramref name="suffix"/>.</summary>
    private static bool TryParseDepthFile(string fileName, string suffix, out int depth)
    {
        depth = 0;
        if (fileName.Length < suffix.Length + 2 || fileName.Length > suffix.Length + 12 || fileName[0] != 'z' ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = fileName.AsSpan(1, fileName.Length - suffix.Length - 1);
        var start = digits[0] == '-' ? 1 : 0;
        if (digits.Length == start)
            return false;

        for (var i = start; i < digits.Length; i++)
        {
            if (digits[i] is < '0' or > '9')
                return false;
        }

        return int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out depth);
    }

    /// <summary>A single-level download's file: <c>&lt;MapName&gt;.yml</c>, also the grid of a ship download.</summary>
    public static string DownloadMapFile(string mapName)
    {
        return mapName + LevelSuffix;
    }

    /// <summary>One level of a multi-level download: <c>&lt;MapName&gt;_z&lt;D&gt;.yml</c>.</summary>
    public static string DownloadLevelFile(string mapName, int depth)
    {
        return $"{mapName}_{LevelFile(depth)}";
    }

    /// <summary>The vessel prototype of a ship download: <c>&lt;MapName&gt;.vessel.yml</c>.</summary>
    public static string DownloadVesselFile(string mapName)
    {
        return mapName + VesselSuffix;
    }

    /// <summary>The export package: <c>&lt;MapName&gt;.zip</c>.</summary>
    public static string DownloadZipFile(string mapName)
    {
        return mapName + ZipSuffix;
    }

    /// <summary>Whether a downloaded file name is the export package.</summary>
    public static bool IsZipFile(string fileName)
    {
        return fileName.EndsWith(ZipSuffix, StringComparison.Ordinal);
    }

    /// <summary>The grid export: the ship grid's file name, <c>&lt;file&gt;.yml</c> (<see cref="WFMappingShipNaming.FileName"/>).</summary>
    public static string DownloadGridFile(string mapName)
    {
        return WFMappingShipNaming.FileName(mapName) + LevelSuffix;
    }

    /// <summary>The file an export of a kind arrives as: the zip, or the grid file for <see cref="WFMappingExportKind.Grid"/>.</summary>
    public static string ExportFile(string mapName, WFMappingExportKind kind)
    {
        return kind == WFMappingExportKind.Grid ? DownloadGridFile(mapName) : DownloadZipFile(mapName);
    }

    /// <summary>Whether a downloaded file name is one the server may send for <paramref name="mapName"/>.</summary>
    public static bool IsDownloadFile(string mapName, string fileName)
    {
        if (!IsValidMapName(mapName))
            return false;

        if (fileName == DownloadGridFile(mapName))
            return true;

        if (!fileName.StartsWith(mapName, StringComparison.Ordinal))
            return false;

        if (fileName == DownloadMapFile(mapName) || fileName == DownloadVesselFile(mapName) ||
            fileName == DownloadZipFile(mapName))
        {
            return true;
        }

        var rest = fileName.Substring(mapName.Length);
        return rest.Length > 1 && rest[0] == '_' && TryParseLevelFile(rest.Substring(1), out _);
    }

    public static ResPath UserFolder(NetUserId user)
    {
        return Root / user.ToString();
    }

    public static ResPath MapFolder(NetUserId user, string mapName)
    {
        return UserFolder(user) / mapName;
    }

    public static ResPath VersionFolder(NetUserId user, string mapName, string version)
    {
        return MapFolder(user, mapName) / version;
    }

    public static ResPath LevelPath(NetUserId user, string mapName, string version, int depth)
    {
        return VersionFolder(user, mapName, version) / LevelFile(depth);
    }

    public static ResPath GridPath(NetUserId user, string mapName, string version, int depth)
    {
        return VersionFolder(user, mapName, version) / GridFile(depth);
    }

    public static ResPath VesselPath(NetUserId user, string mapName, string version, int depth)
    {
        return VersionFolder(user, mapName, version) / VesselFile(depth);
    }

    public static ResPath ScanPath(NetUserId user, string mapName, string version, int depth)
    {
        return VersionFolder(user, mapName, version) / ScanFile(depth);
    }

    public static ResPath ShipDetailsPath(NetUserId user, string mapName, string version)
    {
        return VersionFolder(user, mapName, version) / ShipDetailsFile;
    }

    /// <summary>Where the client writes a downloaded file: <c>/Mapping/&lt;MapName&gt;/&lt;version&gt;/&lt;file&gt;</c>.</summary>
    public static ResPath DownloadPath(string mapName, string version, string fileName)
    {
        return DownloadRoot / mapName / version / fileName;
    }
}
