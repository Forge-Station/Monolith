using System.Globalization;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// Names of z-stack saves: one map file per level, <c>name_z0.yml</c> for the ground level, <c>name_z-1.yml</c>
/// below it, <c>name_z1.yml</c> above it.
/// </summary>
public static class MappingZLevels
{
    private const string Marker = "_z";

    /// <summary>
    /// The file a level of a z-stack is saved to.
    /// </summary>
    public static string StackPath(string name, int depth)
    {
        return $"{MappingMaps.SaveFolder}/{name}/{LevelFile(name, depth)}";
    }

    /// <summary>
    /// The file name of one level of a z-stack.
    /// </summary>
    public static string LevelFile(string prefix, int depth)
    {
        return $"{prefix}{Marker}{depth.ToString(CultureInfo.InvariantCulture)}.yml";
    }

    /// <summary>
    /// Splits a level's file name into the stack's prefix and the level's depth.
    /// </summary>
    public static bool TryParseLevelFile(string fileName, out string prefix, out int depth)
    {
        prefix = string.Empty;
        depth = 0;

        if (!fileName.EndsWith(".yml", StringComparison.Ordinal))
            return false;

        var stem = fileName[..^4];
        var marker = stem.LastIndexOf(Marker, StringComparison.Ordinal);
        if (marker <= 0 ||
            !int.TryParse(stem.AsSpan(marker + Marker.Length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out depth))
        {
            return false;
        }

        prefix = stem[..marker];
        return true;
    }
}

/// <summary>
/// Moves the mapper up or down this many levels of their z-stack.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingZLevelsMoveEvent(int offset) : EntityEventArgs
{
    public readonly int Offset = offset;
}

/// <summary>
/// Adds an empty level directly above or below the mapper's level, making a z-stack if the map has none.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingZLevelsAddEvent(bool above) : EntityEventArgs
{
    public readonly bool Above = above;
}

/// <summary>
/// Deletes the mapper's level and everything on it; the levels around it close the gap.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingZLevelsRemoveEvent : EntityEventArgs;

/// <summary>
/// Saves every level of the mapper's z-stack under a name, one map file per level.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingZLevelsSaveEvent(string name) : EntityEventArgs
{
    public readonly string Name = name;
}

/// <summary>
/// Opens the z-stack a level file belongs to on new maps, every level of it.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingZLevelsOpenEvent(string path) : EntityEventArgs
{
    public readonly string Path = path;
}
