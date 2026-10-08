using Content.Shared.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.Power.Components;
using Content.Shared.SensorMonitoring;

namespace Content.Server.SensorMonitoring;

public sealed partial class BatterySensorSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BatterySensorComponent, DeviceNetworkPacketEvent<BatterySensorSyncPayload>>(PacketReceived);
    }

    private void PacketReceived(EntityUid uid, BatterySensorComponent component, ref DeviceNetworkPacketEvent<BatterySensorSyncPayload> args)
    {
        var battery = Comp<BatteryComponent>(uid);
        var netBattery = Comp<PowerNetworkBatteryComponent>(uid);

        var payload = new BatterySensorDataPayload
        {
            Data = new BatterySensorData(
                battery.CurrentCharge,
                battery.MaxCharge,
                netBattery.CurrentReceiving,
                netBattery.MaxChargeRate,
                netBattery.CurrentSupply,
                netBattery.MaxSupply),
        };

        _deviceNetwork.SendPacket(uid, args.SenderAddress, ref payload);
    }
}
