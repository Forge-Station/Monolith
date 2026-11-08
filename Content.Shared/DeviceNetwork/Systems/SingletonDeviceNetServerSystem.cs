using System.Diagnostics.CodeAnalysis;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Power;
using Robust.Shared.Map;

namespace Content.Shared.DeviceNetwork.Systems;

/// <summary>
/// Keeps one active server entity per station. Activates another available one if the currently active server becomes unavailable.
/// Server in this context means an entity that manages the devicenet packets like the crew monitoring server.
/// </summary>
public sealed partial class SingletonDeviceNetServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MapInitEvent>(OnServerMapInit);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, ComponentRemove>(OnServerRemove);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MetaFlagRemoveAttemptEvent>(OnMetaFlagRemoveAttempt);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MapUidChangedEvent>(OnServerMapChanged);
    }

    /// <summary>
    /// Returns whether the given entity is an active server or not.
    /// </summary>
    public bool IsActiveServer(EntityUid serverId, SingletonDeviceNetServerComponent? serverComponent = default)
    {
        return Resolve(serverId, ref serverComponent) && serverComponent.Active;
    }

    /// <summary>
    /// Returns the address of the currently active server for the given map if there is one.
    /// </summary>
    public bool TryGetActiveServerAddress<TComp>(MapId map, [NotNullWhen(true)] out DeviceAddress? address) where TComp : IComponent
    {
        return TryGetActiveServerAddress<TComp>(map, null, out address);
    }

    /// <summary>
    /// Returns the address of the currently active server for the given map and receive frequency if there is one.
    /// A null frequency searches every frequency group on that map.
    /// One active server is kept per receive/transmit frequency pair.
    /// </summary>
    public bool TryGetActiveServerAddress<TComp>(MapId map, DeviceFrequency? receiveFrequency, [NotNullWhen(true)] out DeviceAddress? address) where TComp : IComponent
    {
        var servers = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TComp,
            TransformComponent>();

        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? last = default;
        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? active = default;
        var activeFrequencyPairs = new HashSet<(DeviceFrequency? Receive, DeviceFrequency? Transmit)>();

        while (servers.MoveNext(out var uid, out var server, out var device, out _, out var xform))
        {
            if (xform.MapID != map)
                continue;

            if (receiveFrequency != null && device.ReceiveFrequency != receiveFrequency)
                continue;

            if (!server.Available)
            {
                DisconnectServer((uid, server, device));
                continue;
            }

            last = (uid, server, device);

            if (!server.Active)
                continue;

            var frequencyPair = (device.ReceiveFrequency, device.TransmitFrequency);
            if (!activeFrequencyPairs.Add(frequencyPair))
            {
                DisconnectServer((uid, server, device));
                continue;
            }

            if (!active.HasValue)
                active = (uid, server, device);
        }

        if (active.HasValue)
        {
            var (id, server, device) = active.Value;
            if (device.Address == DeviceAddress.Invalid)
                ConnectServer((id, server, device));

            address = device.Address;
            return device.Address != DeviceAddress.Invalid;
        }

        if (last.HasValue)
        {
            ConnectServer((last.Value.id, last.Value.server, last.Value.device));
            address = last.Value.device.Address;
            return last.Value.device.Address != DeviceAddress.Invalid;
        }

        address = null;
        return false;
    }

    public bool TryGetActiveServerAddressGlobal<TComp>([NotNullWhen(true)] out DeviceAddress? address)
        where TComp : IComponent
    {
        var servers = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TComp>();

        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? lastAvailable = null;
        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? active = null;
        var activeFrequencyPairs = new HashSet<(DeviceFrequency? Receive, DeviceFrequency? Transmit)>();

        while (servers.MoveNext(out var uid, out var server, out var device, out _))
        {
            if (!server.Available)
            {
                DisconnectServer((uid, server, device));
                continue;
            }

            lastAvailable = (uid, server, device);

            if (!server.Active)
                continue;

            var frequencyPair = (device.ReceiveFrequency, device.TransmitFrequency);
            if (!activeFrequencyPairs.Add(frequencyPair))
            {
                DisconnectServer((uid, server, device));
                continue;
            }

            if (!active.HasValue)
                active = (uid, server, device);
        }

        if (active.HasValue)
        {
            var (id, server, device) = active.Value;
            if (device.Address == DeviceAddress.Invalid)
                ConnectServer((id, server, device));

            address = device.Address;
            return device.Address != DeviceAddress.Invalid;
        }

        if (lastAvailable.HasValue)
        {
            var (id, serv, dev) = lastAvailable.Value;
            ConnectServer((id, serv, dev));
            address = dev.Address;
            return dev.Address != DeviceAddress.Invalid;
        }

        address = null;
        return false;
    }

    private void OnPowerChanged(Entity<SingletonDeviceNetServerComponent> ent, ref PowerChangedEvent args)
    {
        ent.Comp.Available = args.Powered;

        if (!args.Powered && ent.Comp.Active)
            DisconnectServer((ent.Owner, ent.Comp, (DeviceNetworkComponent?)null));
    }

    private void ConnectServer(Entity<SingletonDeviceNetServerComponent?, DeviceNetworkComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp1, ref ent.Comp2))
            return;

        ent.Comp1.Active = true;

        var connectedEvent = new DeviceNetServerConnectedEvent();
        RaiseLocalEvent(ent, ref connectedEvent);

        if (_deviceNetworkSystem.IsDeviceConnected((ent.Owner, ent.Comp2)))
            return;

        _deviceNetworkSystem.ConnectDevice((ent.Owner, ent.Comp2));
    }

    /// <summary>
    /// Disconnects a server from the device network and clears the currently active server.
    /// </summary>
    private void DisconnectServer(Entity<SingletonDeviceNetServerComponent?, DeviceNetworkComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp1))
            return;

        if (!ent.Comp1.Active)
            return;

        ent.Comp1.Active = false;

        var disconnectedEvent = new DeviceNetServerDisconnectedEvent();
        RaiseLocalEvent(ent, ref disconnectedEvent);

        if (Resolve(ent.Owner, ref ent.Comp2, false))
            _deviceNetworkSystem.DisconnectDevice((ent.Owner, ent.Comp2), false);
    }

    /// <summary>
    /// Sets the ExtraTransformEvents flag so the server receives MapUidChangedEvent
    /// when its grid moves between maps (for example via FTL).
    /// </summary>
    private void OnServerMapInit(Entity<SingletonDeviceNetServerComponent> ent, ref MapInitEvent args)
    {
        _metaData.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnServerRemove(Entity<SingletonDeviceNetServerComponent> ent, ref ComponentRemove args)
    {
        _metaData.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    /// <summary>
    /// Prevents other systems from removing the ExtraTransformEvents flag while this server is alive.
    /// </summary>
    private void OnMetaFlagRemoveAttempt(Entity<SingletonDeviceNetServerComponent> ent, ref MetaFlagRemoveAttemptEvent args)
    {
        if ((args.ToRemove & MetaDataFlags.ExtraTransformEvents) != 0
            && ent.Comp.LifeStage <= ComponentLifeStage.Running)
        {
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
        }
    }

    /// <summary>
    /// Resolves singleton conflicts caused by a grid carrying an active server arriving on a map
    /// that already has an active server on the same receive/transmit frequency pair.
    /// The arriving server is disconnected; the existing server on the destination map keeps running.
    /// </summary>
    private void OnServerMapChanged(Entity<SingletonDeviceNetServerComponent> ent, ref MapUidChangedEvent args)
    {
        if (args.OldMapId == args.NewMapId)
            return;
        if (!ent.Comp.Active || !ent.Comp.Available)
            return;
        if (!TryComp<DeviceNetworkComponent>(ent, out var device))
            return;

        var newMap = args.NewMapId;
        if (newMap is null || newMap == MapId.Nullspace)
            return;

        var query = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TransformComponent>();

        while (query.MoveNext(out var otherUid, out var otherServer, out var otherDevice, out var otherXform))
        {
            if (otherUid == ent.Owner)
                continue;
            if (otherXform.MapID != newMap)
                continue;
            if (!otherServer.Active || !otherServer.Available)
                continue;
            if (otherDevice.ReceiveFrequency != device.ReceiveFrequency
                || otherDevice.TransmitFrequency != device.TransmitFrequency)
                continue;

            DisconnectServer((ent.Owner, ent.Comp, device));
            return;
        }
    }
}

/// <summary>
/// Raised when a server gets activated and connected to the device net.
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerConnectedEvent;

/// <summary>
/// Raised when a server gets disconnected.
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerDisconnectedEvent;
