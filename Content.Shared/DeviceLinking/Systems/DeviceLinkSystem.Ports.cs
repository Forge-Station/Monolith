using System.Linq;
using Content.Shared.DeviceLinking.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeviceLinking.Systems;

public sealed partial class DeviceLinkSystem
{
    /// <summary>
    /// Convenience function to add a source port to an entity.
    /// </summary>
    [PublicAPI]
    public void EnsureSourcePort(EntityUid uid, ProtoId<SourcePortPrototype> port)
    {
        var comp = EnsureComp<DeviceLinkSourceComponent>(uid);
        comp.Ports.Add(port);
        Dirty(uid, comp);
    }

    /// <summary>
    /// Convenience function to a sink port to an entity.
    /// </summary>
    [PublicAPI]
    public void EnsureSinkPort(EntityUid uid, ProtoId<SinkPortPrototype> port)
    {
        var comp = EnsureComp<DeviceLinkSinkComponent>(uid);
        comp.Ports.Add(port);
        DirtyField(uid, comp, nameof(DeviceLinkSinkComponent.Ports));
    }

    /// <summary>
    /// Convenience function to add several ports to an entity.
    /// </summary>
    [PublicAPI]
    public void EnsureSourcePorts(EntityUid uid, params ProtoId<SourcePortPrototype>[] ports)
    {
        if (ports.Length == 0)
            return;

        var comp = EnsureComp<DeviceLinkSourceComponent>(uid);
        foreach (var port in ports)
        {
            if (!ProtoMan.HasIndex(port))
                Log.Error($"Attempted to add invalid port {port} to {ToPrettyString(uid)}");
            else
                comp.Ports.Add(port);
        }
        Dirty(uid, comp);
    }

    /// <summary>
    /// Convenience function to add several ports to an entity.
    /// </summary>
    [PublicAPI]
    public void EnsureSinkPorts(EntityUid uid, params ProtoId<SinkPortPrototype>[] ports)
    {
        if (ports.Length == 0)
            return;

        var comp = EnsureComp<DeviceLinkSinkComponent>(uid);
        foreach (var port in ports)
        {
            if (!ProtoMan.HasIndex(port))
                Log.Error($"Attempted to add invalid port {port} to {ToPrettyString(uid)}");
            else
                comp.Ports.Add(port);
        }
        Dirty(uid, comp);
    }

    [PublicAPI]
    public ProtoId<SourcePortPrototype>[] GetSourcePortIds(Entity<DeviceLinkSourceComponent> source)
    {
        return [.. source.Comp.Ports];
    }

    /// <summary>
    /// Retrieves the available ports from a source
    /// </summary>
    /// <returns>A list of source port prototypes</returns>
    [PublicAPI]
    public HashSet<ProtoId<SourcePortPrototype>> GetSourcePorts(Entity<DeviceLinkSourceComponent?> source)
    {
        if (!_deviceLinkSourceQuery.Resolve(source.Owner, ref source.Comp))
            return [];

        return source.Comp.Ports;
    }

    [PublicAPI]
    public ProtoId<SinkPortPrototype>[] GetSinkPortIds(Entity<DeviceLinkSinkComponent> source)
    {
        return [.. source.Comp.Ports];
    }

    /// <summary>
    /// Retrieves the available ports from a sink.
    /// </summary>
    /// <returns>A list of sink port prototypes.</returns>
    [PublicAPI]
    public List<SinkPortPrototype> GetSinkPorts(Entity<DeviceLinkSinkComponent?> sink)
    {
        if (!_deviceLinkSinkQuery.Resolve(sink.Owner, ref sink.Comp))
            return [];

        var sinkPorts = new List<SinkPortPrototype>();
        foreach (var port in sink.Comp.Ports)
        {
            sinkPorts.Add(ProtoMan.Index(port));
        }

        return sinkPorts;
    }

    /// <summary>
    /// Removes a sink port and any links that used it.
    /// </summary>
    [PublicAPI]
    public void RemoveSinkPort(EntityUid uid, ProtoId<SinkPortPrototype> port)
    {
        if (!TryComp<DeviceLinkSinkComponent>(uid, out var sink))
            return;

        foreach (var sourceUid in sink.LinkedSources.ToArray())
        {
            if (!TryComp<DeviceLinkSourceComponent>(sourceUid, out var source)
                || !source.LinkedPorts.TryGetValue(uid, out var links))
                continue;

            foreach (var link in links.ToArray())
            {
                if (link.Sink == port)
                    ToggleLink(null, (sourceUid, source), (uid, sink), link.Source, link.Sink);
            }
        }

        sink.Ports.Remove(port);
        Dirty(uid, sink);
    }

    /// <summary>
    /// Removes a source port and any links that used it.
    /// </summary>
    [PublicAPI]
    public void RemoveSourcePort(EntityUid uid, ProtoId<SourcePortPrototype> port)
    {
        if (!TryComp<DeviceLinkSourceComponent>(uid, out var source))
            return;

        foreach (var (sinkUid, links) in source.LinkedPorts.ToArray())
        {
            if (!TryComp<DeviceLinkSinkComponent>(sinkUid, out var sink))
                continue;

            foreach (var link in links.ToArray())
            {
                if (link.Source == port)
                    ToggleLink(null, (uid, source), (sinkUid, sink), link.Source, link.Sink);
            }
        }

        source.Ports.Remove(port);
        source.Outputs.Remove(port);
        source.LastSignals.Remove(port);
        Dirty(uid, source);
    }

    /// <summary>
    /// Convenience function to retrieve the name of a <see cref="DevicePortPrototype"/>.
    /// </summary>
    [PublicAPI]
    public string PortName<TPort>(string port) where TPort : DevicePortPrototype, IPrototype
    {
        if (!ProtoMan.TryIndex<TPort>(port, out var proto))
            return port;

        return Loc.GetString(proto.Name);
    }
}
