using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;

namespace Content.Server.DeviceNetwork.Systems;

// WOLFGATE
public sealed partial class DeviceNetworkSystem
{
    /// <summary>
    /// Redoes what a device's map init does, for a grid that was loaded from a saved copy and so never
    /// raised MapInitEvent. Safe to repeat: a device already on its network keeps the address it has.
    /// </summary>
    public void ReinitLoadedDevice(EntityUid uid, DeviceNetworkComponent? device = null)
    {
        if (!Resolve(uid, ref device, false))
            return;

        if (device.ReceiveFrequency == null
            && device.ReceiveFrequencyId != null
            && _protoMan.TryIndex<DeviceFrequencyPrototype>(device.ReceiveFrequencyId, out var receive))
        {
            device.ReceiveFrequency = receive.Frequency;
        }

        if (device.TransmitFrequency == null
            && device.TransmitFrequencyId != null
            && _protoMan.TryIndex<DeviceFrequencyPrototype>(device.TransmitFrequencyId, out var transmit))
        {
            device.TransmitFrequency = transmit.Frequency;
        }

        // A second connect would hand the device a fresh address and leave the old key pointing at it.
        if (device.AutoConnect && !IsDeviceConnected(uid, device))
            ConnectDevice(uid, device);
    }
}
