using System.Linq;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.Power;
using Content.Shared.SurveillanceCamera;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.DeviceNetwork.Components;

namespace Content.Server.SurveillanceCamera;

public sealed partial class SurveillanceCameraRouterSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private UserInterfaceSystem _userInterface = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, ComponentInit>(OnInitialize);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, DeviceNetworkPacketEvent<SurveillanceCameraSubnetConnectPayload>>(OnSubnetConnect);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, DeviceNetworkPacketEvent<SurveillanceCameraSubnetDisconnectPayload>>(OnSubnetDisconnect);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, DeviceNetworkPacketEvent<SurveillanceCameraPingSubnetPayload>>(OnSubnetPing);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, DeviceNetworkPacketEvent<SurveillanceCameraPingPayload>>(OnCameraPing);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, SurveillanceCameraSetupSetNetwork>(OnSetNetwork);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, GetVerbsEvent<AlternativeVerb>>(AddVerbs);
        SubscribeLocalEvent<SurveillanceCameraRouterComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnInitialize(EntityUid uid, SurveillanceCameraRouterComponent router, ComponentInit args)
    {
        if (router.SubnetFrequencyId == null ||
            !_prototypeManager.TryIndex(router.SubnetFrequencyId, out DeviceFrequencyPrototype? subnetFrequency))
        {
            return;
        }

        router.SubnetFrequency = subnetFrequency.Frequency;
        router.Active = true;
    }

    private void OnSubnetConnect(Entity<SurveillanceCameraRouterComponent> ent, ref DeviceNetworkPacketEvent<SurveillanceCameraSubnetConnectPayload> args)
    {
        if (!ent.Comp.Active || string.IsNullOrEmpty(args.SenderAddress))
            return;

        AddMonitorToRoute(ent.Owner, args.SenderAddress, ent.Comp);
        PingSubnet(ent.Owner, ent.Comp);
    }

    private void OnSubnetDisconnect(Entity<SurveillanceCameraRouterComponent> ent, ref DeviceNetworkPacketEvent<SurveillanceCameraSubnetDisconnectPayload> args)
    {
        if (string.IsNullOrEmpty(args.SenderAddress))
            return;

        RemoveMonitorFromRoute(ent.Owner, args.SenderAddress, ent.Comp);
    }

    private void OnCameraPing(Entity<SurveillanceCameraRouterComponent> ent, ref DeviceNetworkPacketEvent<SurveillanceCameraPingPayload> args)
    {
        if (!ent.Comp.Active)
            return;

        PingSubnet(ent.Owner, ent.Comp);
    }

    private void OnSubnetPing(Entity<SurveillanceCameraRouterComponent> ent, ref DeviceNetworkPacketEvent<SurveillanceCameraPingSubnetPayload> args)
    {
        if (!ent.Comp.Active || ent.Comp.SubnetFrequencyId == null || string.IsNullOrEmpty(args.SenderAddress))
            return;

        var response = new SurveillanceCameraSubnetDataPayload
        {
            Subnet = ent.Comp.SubnetName,
            TransmitFrequency = ent.Comp.SubnetFrequencyId.Value,
        };
        _deviceNetworkSystem.SendPacket(ent.Owner, args.SenderAddress, ref response);
    }

    private void OnPowerChanged(EntityUid uid, SurveillanceCameraRouterComponent component, ref PowerChangedEvent args)
    {
        component.MonitorRoutes.Clear();
        component.Active = args.Powered;
    }

    private void AddVerbs(EntityUid uid, SurveillanceCameraRouterComponent component, GetVerbsEvent<AlternativeVerb> verbs)
    {
        if (!_actionBlocker.CanInteract(verbs.User, uid))
        {
            return;
        }

        if (component.SubnetFrequencyId != null)
        {
            return;
        }

        AlternativeVerb verb = new();
        verb.Text = Loc.GetString("surveillance-camera-setup");
        verb.Act = () => OpenSetupInterface(uid, verbs.User, component);
        verbs.Verbs.Add(verb);
    }

    private void OnSetNetwork(EntityUid uid, SurveillanceCameraRouterComponent component,
            SurveillanceCameraSetupSetNetwork args)
    {
        if (args.UiKey is not SurveillanceCameraSetupUiKey key
            || key != SurveillanceCameraSetupUiKey.Router)
        {
            return;
        }
        if (args.Network < 0 || args.Network >= component.AvailableNetworks.Count)
        {
            return;
        }

        if (!_prototypeManager.TryIndex<DeviceFrequencyPrototype>(component.AvailableNetworks[args.Network],
                out var frequency))
        {
            return;
        }

        component.SubnetFrequencyId = component.AvailableNetworks[args.Network];
        component.SubnetFrequency = frequency.Frequency;
        component.Active = true;
        UpdateSetupInterface(uid, component);
    }

    private void OpenSetupInterface(EntityUid uid, EntityUid player, SurveillanceCameraRouterComponent? camera = null)
    {
        if (!Resolve(uid, ref camera))
            return;

        if (!_userInterface.TryOpenUi(uid, SurveillanceCameraSetupUiKey.Router, player))
            return;

        UpdateSetupInterface(uid, camera);
    }

    private void UpdateSetupInterface(EntityUid uid, SurveillanceCameraRouterComponent? router = null, DeviceNetworkComponent? deviceNet = null)
    {
        if (!Resolve(uid, ref router, ref deviceNet))
        {
            return;
        }

        if (router.AvailableNetworks.Count == 0 || router.SubnetFrequencyId != null)
        {
            _userInterface.CloseUi(uid, SurveillanceCameraSetupUiKey.Router);
            return;
        }

        var state = new SurveillanceCameraSetupBoundUiState(router.SubnetName, deviceNet.ReceiveFrequency ?? 0,
            router.AvailableNetworks.Select(id => id.Id).ToList(), true, router.SubnetFrequencyId != null);
        _userInterface.SetUiState(uid, SurveillanceCameraSetupUiKey.Router, state);
    }

    // Adds a monitor to the set of routes.
    private void AddMonitorToRoute(EntityUid uid, string address, SurveillanceCameraRouterComponent? router = null)
    {
        if (!Resolve(uid, ref router))
        {
            return;
        }

        router.MonitorRoutes.Add(address);
    }

    private void RemoveMonitorFromRoute(EntityUid uid, string address, SurveillanceCameraRouterComponent? router = null)
    {
        if (!Resolve(uid, ref router))
        {
            return;
        }

        router.MonitorRoutes.Remove(address);
    }

    // Pings a subnet to get all camera information.
    private void PingSubnet(EntityUid uid, SurveillanceCameraRouterComponent? router = null)
    {
        if (!Resolve(uid, ref router))
        {
            return;
        }

        var payload = new SurveillanceCameraPingPayload
        {
            Subnet = router.SubnetName,
        };

        _deviceNetworkSystem.SendPacket(uid, null, ref payload, router.SubnetFrequency);
    }
}
