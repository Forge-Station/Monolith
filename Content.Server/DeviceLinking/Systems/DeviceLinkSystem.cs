using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Components;
// #Forge-change START (KIAS): Источник и приёмник передают адаптеру фактический порт; доставка остаётся штатной.
using Content.Shared._Forge.KIAS;
using Content.Server._Forge.KIAS;
// #Forge-change END (KIAS)

namespace Content.Server.DeviceLinking.Systems;

public sealed partial class DeviceLinkSystem : SharedDeviceLinkSystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetworkSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeviceLinkSinkComponent, DeviceNetworkPacketEvent>(OnPacketReceived);
        SubscribeLocalEvent<DeviceLinkSourceComponent, NewLinkEvent>(OnNewLink);
    }

    #region Sending & Receiving
    // #Forge-change START (KIAS): Источник и приёмник передают адаптеру фактический порт; доставка остаётся штатной.
    public bool InvokeSink(EntityUid source, EntityUid target, string port, NetworkPayload? data = null)
    {
        if (!TryComp<DeviceLinkSourceComponent>(source, out var sourceComp)
            || !TryComp<DeviceLinkSinkComponent>(target, out var sink) || !sink.Ports.Contains(port)) return false;
        InvokeDirect((source, sourceComp), (target, sink), "Controller", port, data);
        return true;
    }

    // #Forge-change END (KIAS)
    public override void InvokePort(EntityUid uid, string port, NetworkPayload? data = null, DeviceLinkSourceComponent? sourceComponent = null)
    {
        // #Forge-change START (KIAS): Источник и приёмник передают адаптеру фактический порт; доставка остаётся штатной.
        if (!Resolve(uid, ref sourceComponent) || !sourceComponent.Ports.Contains(port))
            return;

        var invoked = new DeviceLinkPortInvokedEvent(port, data);
        RaiseLocalEvent(uid, ref invoked);
        if (!sourceComponent.Outputs.TryGetValue(port, out var sinks))
        // #Forge-change END (KIAS)
            return;

        foreach (var sinkUid in sinks)
        {
            if (!sourceComponent.LinkedPorts.TryGetValue(sinkUid, out var links))
                continue;

            if (!TryComp<DeviceLinkSinkComponent>(sinkUid, out var sinkComponent))
                continue;

            foreach (var (source, sink) in links)
            {
                if (source == port)
                    InvokeDirect((uid, sourceComponent), (sinkUid, sinkComponent), source, sink, data);
            }
        }
    }

    /// <summary>
    /// Raises an event on or sends a network packet directly to a sink from a source.
    /// </summary>
    private void InvokeDirect(Entity<DeviceLinkSourceComponent> source, Entity<DeviceLinkSinkComponent?> sink, string sourcePort, string sinkPort, NetworkPayload? data)
    {
        if (!Resolve(sink, ref sink.Comp))
            return;

        // #Forge-change START (KIAS): Источник и приёмник передают адаптеру фактический порт; доставка остаётся штатной.
        if (HasComp<KiasDeviceComponent>(source)
            && Transform(source).GridUid is { } grid
            && TryComp<KiasGridComponent>(grid, out var runtime) && runtime.Testing
            && !HasComp<KiasSpeakerComponent>(sink))
            return;

        // #Forge-change END (KIAS)
        var invokeCounter = GetEffectiveInvokeCounter(sink.Comp);
        if (invokeCounter > sink.Comp.InvokeLimit)
        {
            SetInvokeCounter(sink.Comp, 0);
            var args = new DeviceLinkOverloadedEvent();
            RaiseLocalEvent(sink, ref args);
            RemoveAllFromSink(sink, sink.Comp);
            return;
        }

        SetInvokeCounter(sink.Comp, invokeCounter + 1);

        //Just skip using device networking if the source or the sink doesn't support it
        if (!HasComp<DeviceNetworkComponent>(source) || !TryComp<DeviceNetworkComponent>(sink, out var sinkNetwork))
        {
            // #Forge-change START (KIAS): Источник и приёмник передают адаптеру фактический порт; доставка остаётся штатной.
            var eventArgs = new SignalReceivedEvent(sinkPort, source, data, sourcePort);
            // #Forge-change END (KIAS)
            RaiseLocalEvent(sink, ref eventArgs);
            return;
        }

        var payload = new NetworkPayload()
        {
            [InvokedPort] = sinkPort
        };

        if (data != null)
        {
            //Prevent overriding the invoked port
            data.Remove(InvokedPort);
            foreach (var (key, value) in data)
            {
                payload.Add(key, value);
            }
        }

        // force using wireless network so things like atmos devices are able to send signals
        var network = (int) DeviceNetworkComponent.DeviceNetIdDefaults.Wireless;
        _deviceNetworkSystem.QueuePacket(source, sinkNetwork.Address, payload, sinkNetwork.ReceiveFrequency, network);
    }

    /// <summary>
    /// Helper function that invokes a port with a high/low binary logic signal.
    /// </summary>
    public void SendSignal(EntityUid uid, string port, bool signal, DeviceLinkSourceComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        var data = new NetworkPayload
        {
            [DeviceNetworkConstants.LogicState] = signal ? SignalState.High : SignalState.Low
        };
        InvokePort(uid, port, data, comp);

        comp.LastSignals[port] = signal;
    }

    /// <summary>
    /// Clears the last signals state for linking.
    /// This is not to be confused with sending a low signal, this is the complete absence of anything.
    /// Use if the device is in an invalid state and has no reasonable output signal.
    /// </summary>
    public void ClearSignal(Entity<DeviceLinkSourceComponent?> ent, string port)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.LastSignals.Remove(port);
    }

    /// <summary>
    /// Checks if the payload has a port defined and if the port is present on the sink.
    /// Raises a <see cref="SignalReceivedEvent"/> containing the payload when the check passes
    /// </summary>
    private void OnPacketReceived(EntityUid uid, DeviceLinkSinkComponent component, DeviceNetworkPacketEvent args)
    {
        if (!args.Data.TryGetValue(InvokedPort, out string? port) || !(component.Ports?.Contains(port) ?? false))
            return;

        var eventArgs = new SignalReceivedEvent(port, args.Sender, args.Data);
        RaiseLocalEvent(uid,  ref eventArgs);
    }

    /// <summary>
    /// When linking from a port that currently has a signal being sent, invoke the new link with that signal.
    /// </summary>
    private void OnNewLink(Entity<DeviceLinkSourceComponent> ent, ref NewLinkEvent args)
    {
        if (args.Source != ent.Owner)
            return;

        // Don't automatically send signals if the source has NoSignalOnLinkComponent
        if (HasComp<NoSignalOnLinkComponent>(ent))
            return;

        // only do anything if a signal is being sent from a port
        if (!ent.Comp.LastSignals.TryGetValue(args.SourcePort, out var signal))
            return;

        var payload = new NetworkPayload()
        {
            [DeviceNetworkConstants.LogicState] = signal ? SignalState.High : SignalState.Low
        };
        InvokeDirect(ent, args.Sink, args.SourcePort, args.SinkPort, payload);
    }
    #endregion
}
