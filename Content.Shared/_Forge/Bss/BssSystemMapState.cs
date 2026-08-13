using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Bss;

[Serializable, NetSerializable]
public sealed class BssSystemMapState
{
    public string? Network;
    public string? CurrentSector;
    public List<BssSystemMapNode> Nodes;
    public List<BssSystemMapEdge> Edges;
    public string? Error;

    public BssSystemMapState(
        string? network,
        string? currentSector,
        List<BssSystemMapNode> nodes,
        List<BssSystemMapEdge> edges,
        string? error = null)
    {
        Network = network;
        CurrentSector = currentSector;
        Nodes = nodes;
        Edges = edges;
        Error = error;
    }

    public static BssSystemMapState Empty(string? error = null)
        => new(null, null, [], [], error);
}

[Serializable, NetSerializable]
public sealed record BssSystemMapNode(
    string Id,
    string Name,
    Vector2 Position,
    bool Current,
    bool Reachable,
    bool Online,
    Color Color);

[Serializable, NetSerializable]
public sealed record BssSystemMapEdge(
    string From,
    string To,
    bool Bidirectional,
    bool Online);

[Serializable, NetSerializable]
public sealed record BssGateRadarState(
    Vector2 WorldPosition,
    float Range,
    string Sector);
