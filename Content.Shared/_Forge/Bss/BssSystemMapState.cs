using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Bss;

[Serializable, NetSerializable]
public sealed class BssSystemMapState
{
    public string? Network;
    public string? RegionName;
    public string? CurrentSector;
    public List<BssSystemMapNode> Nodes;
    public List<BssSystemMapEdge> Edges;
    public List<BssSystemMapGroup> Groups;
    public string? Error;

    public BssSystemMapState(
        string? network,
        string? regionName,
        string? currentSector,
        List<BssSystemMapNode> nodes,
        List<BssSystemMapEdge> edges,
        List<BssSystemMapGroup> groups,
        string? error = null)
    {
        Network = network;
        RegionName = regionName;
        CurrentSector = currentSector;
        Nodes = nodes;
        Edges = edges;
        Groups = groups;
        Error = error;
    }

    public static BssSystemMapState Empty(string? error = null)
        => new(null, null, null, [], [], [], error);
}

[Serializable, NetSerializable]
public sealed record BssSystemMapNode(
    string Id,
    string Name,
    Vector2 Position,
    bool Current,
    bool Reachable,
    bool Online,
    Color Color,
    string Group,
    string GroupName);

[Serializable, NetSerializable]
public sealed record BssSystemMapEdge(
    string From,
    string To,
    bool Bidirectional,
    bool Online);

[Serializable, NetSerializable]
public sealed record BssSystemMapGroup(
    string Id,
    string Name,
    Color Color);

[Serializable, NetSerializable]
public sealed record BssGateRadarState(
    Vector2 WorldPosition,
    float Range,
    string Sector);
