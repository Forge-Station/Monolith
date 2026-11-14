using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

public static class MappingMaps
{
    /// <summary>
    /// Folder in the server's data directory that the Maps window saves into.
    /// </summary>
    public const string SaveFolder = "/Mapping";
}

/// <summary>
/// Where a map file in the Maps window comes from.
/// </summary>
[Serializable, NetSerializable]
public enum MappingMapSource : byte
{
    /// <summary>The server's data folder, where saves go.</summary>
    Saved,

    /// <summary>Ship grids shipped with the game, under <c>/SharedMaps</c>.</summary>
    Ships,

    /// <summary>Stations and other maps shipped with the game, under <c>/Maps</c>.</summary>
    Maps,
}

[Serializable, NetSerializable]
public readonly record struct MappingMapFile(string Path, MappingMapSource Source);

/// <summary>
/// Asks the server for the map files a mapper can open.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsListRequestEvent : EntityEventArgs;

/// <summary>
/// Server to client: the map files, and the folder new saves go into.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsListEvent(List<MappingMapFile> files, string saveFolder) : EntityEventArgs
{
    public readonly List<MappingMapFile> Files = files;
    public readonly string SaveFolder = saveFolder;
}

/// <summary>
/// Asks the server for a map file's text, to preview it.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsPreviewRequestEvent(string path) : EntityEventArgs
{
    public readonly string Path = path;
}

/// <summary>
/// Server to client: a map file's text, or null with a Fluent id saying why there is none.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsPreviewEvent(string path, string? yaml, string? error) : EntityEventArgs
{
    public readonly string Path = path;
    public readonly string? Yaml = yaml;
    public readonly string? Error = error;
}

/// <summary>
/// Saves the grid the mapper stands on, or with <see cref="WholeMap"/> their whole map, under a name.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsSaveEvent(string name, bool wholeMap) : EntityEventArgs
{
    public readonly string Name = name;
    public readonly bool WholeMap = wholeMap;
}

/// <summary>
/// Opens a map file on a new mapping map, or with <see cref="Here"/> loads its grid where the mapper stands.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingMapsLoadEvent(string path, bool here) : EntityEventArgs
{
    public readonly string Path = path;
    public readonly bool Here = here;
}
