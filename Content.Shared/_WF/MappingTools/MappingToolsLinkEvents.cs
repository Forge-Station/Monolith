using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>Sets the links between a source and a sink to exactly <see cref="Links"/>; empty unlinks them.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsLinkEvent(NetEntity source, NetEntity sink, List<(string Source, string Sink)> links)
    : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly NetEntity Sink = sink;
    public readonly List<(string Source, string Sink)> Links = links;
}

/// <summary>Anchors or unanchors an entity, as a wrench would.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsWrenchEvent(NetEntity target) : EntityEventArgs
{
    public readonly NetEntity Target = target;
}

/// <summary>Asks for the ports of a source and a sink and the links between them.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsLinkPortsRequestEvent(NetEntity source, NetEntity sink) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly NetEntity Sink = sink;
}

/// <summary>Server to client: a source's and a sink's ports, their links and the game's default links.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsLinkPortsEvent(
    NetEntity source,
    NetEntity sink,
    List<string> sourcePorts,
    List<string> sinkPorts,
    List<(string Source, string Sink)> links,
    List<(string Source, string Sink)> defaults)
    : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly NetEntity Sink = sink;
    public readonly List<string> SourcePorts = sourcePorts;
    public readonly List<string> SinkPorts = sinkPorts;
    public readonly List<(string Source, string Sink)> Links = links;
    public readonly List<(string Source, string Sink)> Defaults = defaults;
}

/// <summary>Asks for every signal link on a map; signal links aren't networked.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsLinksRequestEvent(NetEntity map) : EntityEventArgs
{
    public readonly NetEntity Map = map;
}

/// <summary>One linked source and sink pair, with the number of port pairs between them.</summary>
[Serializable, NetSerializable]
public readonly record struct MappingSignalLinkState(NetEntity Source, NetEntity Sink, int Ports);

/// <summary>Server to client: every signal link whose source is on <see cref="Map"/>.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsLinksSnapshotEvent(NetEntity map, List<MappingSignalLinkState> links) : EntityEventArgs
{
    public readonly NetEntity Map = map;
    public readonly List<MappingSignalLinkState> Links = links;
}

/// <summary>What a <see cref="MappingToolsDeviceListEvent"/> does with its devices.</summary>
[Serializable, NetSerializable]
public enum MappingDeviceListOp : byte
{
    Add,
    Remove,
}

/// <summary>Adds devices to, or removes them from, a device list (an air alarm's, a fire alarm's, a console's).</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsDeviceListEvent(NetEntity holder, List<NetEntity> devices, MappingDeviceListOp op)
    : EntityEventArgs
{
    public readonly NetEntity Holder = holder;
    public readonly List<NetEntity> Devices = devices;
    public readonly MappingDeviceListOp Op = op;
}

/// <summary>Makes every target's reader accept any one of <see cref="AccessLevels"/>; empty opens to all.</summary>
[Serializable, NetSerializable]
public sealed class MappingToolsAccessEvent(List<NetEntity> targets, List<string> accessLevels) : EntityEventArgs
{
    public readonly List<NetEntity> Targets = targets;
    public readonly List<string> AccessLevels = accessLevels;
}
