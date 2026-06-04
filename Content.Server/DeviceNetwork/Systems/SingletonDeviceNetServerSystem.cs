using System.Diagnostics.CodeAnalysis;
using Content.Server.DeviceNetwork.Components;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.Station.Systems;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Power;
using Robust.Shared.Map;
using System.Collections.Generic; // Forge-change

namespace Content.Server.DeviceNetwork.Systems;

/// <summary>
/// Keeps one active server entity per station. Activates another available one if the currently active server becomes unavailable
/// Server in this context means an entity that manages the devicenet packets like the <see cref="Content.Server.Medical.CrewMonitoring.CrewMonitoringServerSystem"/>
/// </summary>
public sealed partial class SingletonDeviceNetServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!; // Forge-change

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, PowerChangedEvent>(OnPowerChanged);
        // Forge-Change-start
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MapInitEvent>(OnServerMapInit);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, ComponentRemove>(OnServerRemove);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MetaFlagRemoveAttemptEvent>(OnMetaFlagRemoveAttempt);
        SubscribeLocalEvent<SingletonDeviceNetServerComponent, MapUidChangedEvent>(OnServerMapChanged);
        // Forge-Change-end
    }

    /// <summary>
    /// Returns whether the given entity is an active server or not
    /// </summary>
    public bool IsActiveServer(EntityUid serverId, SingletonDeviceNetServerComponent? serverComponent = default)
    {
        return Resolve(serverId, ref serverComponent) && serverComponent.Active;
    }

    /// <summary>
    /// Returns the address of the currently active server for the given map (instead of station id) if there is one.<br/>
    /// What kind of server you're trying to get the active instance of is determined by the component type parameter TComp.<br/>
    /// <br/>
    /// Setting TComp to <see cref="CrewMonitoringServerComponent"/>, for example, gives you the address of an entity containing the crew monitoring server component.<br/>
    /// </summary>
    /// <param name="stationId">The entityUid of the station</param>
    /// <param name="address">The address of the active server if it exists</param>
    /// <typeparam name="TComp">The component type that determines what type of server you're getting the address of</typeparam>
    /// <returns>True if there is an active serve. False otherwise</returns>
    public bool TryGetActiveServerAddress<TComp>(MapId map, [NotNullWhen(true)] out string? address) where TComp : IComponent
    {
        // Forge-Change-start
        return TryGetActiveServerAddress<TComp>(map, null, out address);
    }
    /// <summary>
    /// Returns the address of the currently active server for the given map and receive frequency if there is one.<br/>
    /// What kind of server you're trying to get the active instance of is determined by the component type parameter TComp.<br/>
    /// </summary>
    /// <param name="map">The map id to search on</param>
    /// <param name="receiveFrequency">The receive frequency group to search in. Null means all frequencies.</param>
    /// <param name="address">The address of the active server if it exists</param>
    /// <typeparam name="TComp">The component type that determines what type of server you're getting the address of</typeparam>
    /// <returns>True if there is an active server. False otherwise</returns>
    public bool TryGetActiveServerAddress<TComp>(MapId map, uint? receiveFrequency, [NotNullWhen(true)] out string? address) where TComp : IComponent
    {
        // Forge-Change-end
        var servers = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TComp,
            TransformComponent // Frontier
        >();

        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? last = default;
        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? active = default; // Forge-Change
        HashSet<(uint? receive, uint? transmit)> activeFrequencyPairs = new(); // Forge-change

        while (servers.MoveNext(out var uid, out var server, out var device, out _, out var xform))
        {
            if (xform.MapID != map) // Forge-Change
                continue;

            if (receiveFrequency != null && device.ReceiveFrequency != receiveFrequency) // Forge-change
                continue; // Forge-change

            if (!server.Available)
            {
                DisconnectServer(uid, server, device); // Forge-Change
                continue;
            }

            last = (uid, server, device);

            if (!server.Active) // Forge-Change
                continue;

            //if (!active.HasValue) // Forge-Change
            //    active = (uid, server, device); // Forge-Change

            // Forge-Change-start
            var frequencyPair = (device.ReceiveFrequency, device.TransmitFrequency);
            if (!activeFrequencyPairs.Add(frequencyPair))
            {
                DisconnectServer(uid, server, device);
                continue;
            }

            if (!active.HasValue)
                active = (uid, server, device);
            // Forge-Change-end
        }

        if (active.HasValue) // Forge-Change
        {
            var (id, server, device) = active.Value; // Forge-Change
            if (string.IsNullOrEmpty(device.Address)) // Forge-Change
                ConnectServer(id, server, device); // Forge-Change

            address = device.Address;
            return !string.IsNullOrEmpty(address); // Forge-Change
        }

        //If there was no active server for the station make the last available inactive one active
        if (last.HasValue)
        {
            ConnectServer(last.Value.id, last.Value.server, last.Value.device);
            address = last.Value.device.Address;
            return !string.IsNullOrEmpty(address); // Forge-Change
        }

        address = null; // Forge-Change
        return false; // Forge-Change
    }

    // Forge-Change-start
    public bool TryGetActiveServerAddressGlobal<TComp>([NotNullWhen(true)] out string? address)
        where TComp : IComponent
    {
        var servers = EntityQueryEnumerator<
            SingletonDeviceNetServerComponent,
            DeviceNetworkComponent,
            TComp
        >();

        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? lastAvailable = null;
        (EntityUid id, SingletonDeviceNetServerComponent server, DeviceNetworkComponent device)? active = null;
        HashSet<(uint? receive, uint? transmit)> activeFrequencyPairs = new(); // Forge-Change

        while (servers.MoveNext(out var uid, out var server, out var device, out _))
        {
            if (!server.Available)
            {
                DisconnectServer(uid, server, device);
                continue;
            }

            lastAvailable = (uid, server, device);

            if (!server.Active)
                continue;

            // if (active.HasValue)
            var frequencyPair = (device.ReceiveFrequency, device.TransmitFrequency); // Forge-Change
            if (!activeFrequencyPairs.Add(frequencyPair)) // Forge-Change
            {
                DisconnectServer(uid, server, device);
                continue;
            }

            if (!active.HasValue) // Forge-Change
                active = (uid, server, device); // Forge-Change
        }

        if (active.HasValue)
        {
            var (id, server, device) = active.Value;
            if (string.IsNullOrEmpty(device.Address))
                ConnectServer(id, server, device);

            address = device.Address;
            return !string.IsNullOrEmpty(address);
        }

        if (lastAvailable.HasValue)
        {
            var (id, serv, dev) = lastAvailable.Value;
            ConnectServer(id, serv, dev);
            address = dev.Address;
            return !string.IsNullOrEmpty(address);
        }

        address = null;
        return false;
    }
    // Forge-Change-end
    /// <summary>
    /// Disconnects the server losing power
    /// </summary>
    private void OnPowerChanged(EntityUid uid, SingletonDeviceNetServerComponent component, ref PowerChangedEvent args)
    {
        component.Available = args.Powered;

        if (!args.Powered && component.Active)
            DisconnectServer(uid, component);
    }

    private void ConnectServer(EntityUid uid, SingletonDeviceNetServerComponent? server = null, DeviceNetworkComponent? device = null)
    {
        if (!Resolve(uid, ref server, ref device))
            return;

        server.Active = true;

        var connectedEvent = new DeviceNetServerConnectedEvent();
        RaiseLocalEvent(uid, ref connectedEvent);

        if (_deviceNetworkSystem.IsDeviceConnected(uid, device))
            return;

        _deviceNetworkSystem.ConnectDevice(uid, device);
    }

    /// <summary>
    /// Disconnects a server from the device network and clears the currently active server
    /// </summary>
    private void DisconnectServer(EntityUid uid, SingletonDeviceNetServerComponent? server = null, DeviceNetworkComponent? device = null)
    {
        // if (!Resolve(uid, ref server, ref device))
        // Forge-Change-start
        if (!Resolve(uid, ref server))
            return;

        if (!server.Active)
        // Forge-Change-end
            return;

        server.Active = false;

        var disconnectedEvent = new DeviceNetServerDisconnectedEvent();
        RaiseLocalEvent(uid, ref disconnectedEvent);

        // _deviceNetworkSystem.DisconnectDevice(uid, device, false);
        if (device != null) // Forge-Change
            _deviceNetworkSystem.DisconnectDevice(uid, device, false); // Forge-Change
    }

    // Forge-Change-start
    /// <summary>
    /// Sets the ExtraTransformEvents flag so the server receives MapUidChangedEvent
    /// when its grid moves between maps (e.g. via FTL/BSS jump).
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
    /// that already has an active server on the same (Receive, Transmit) frequency pair.
    /// The arriving (moved) server is disconnected; the existing server on the destination map keeps running.
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

            DisconnectServer(ent.Owner, ent.Comp, device);
            return;
        }
    }
    // Forge-Change-end
}

/// <summary>
/// Raised when a server gets activated and connected to the device net
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerConnectedEvent;

/// <summary>
/// Raised when a server gets disconnected
/// </summary>
[ByRefEvent]
public record struct DeviceNetServerDisconnectedEvent;
