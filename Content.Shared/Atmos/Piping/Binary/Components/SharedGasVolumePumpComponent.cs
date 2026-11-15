using Content.Shared.DeviceNetwork;
using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.Piping.Binary.Components;

/// <summary>
/// Contains data about a <see cref="GasVolumePumpComponent"/>.
/// </summary>
public partial record struct GasVolumePumpDataPayload : INetworkPayload
{
    [DataField]
    public float LastMolesTransferred;
}

[Serializable, NetSerializable]
public enum GasVolumePumpUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class GasVolumePumpBoundUserInterfaceState : BoundUserInterfaceState
{
    public string PumpLabel { get; }
    public float TransferRate { get; }
    public bool Enabled { get; }

    public GasVolumePumpBoundUserInterfaceState(string pumpLabel, float transferRate, bool enabled)
    {
        PumpLabel = pumpLabel;
        TransferRate = transferRate;
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class GasVolumePumpToggleStatusMessage : BoundUserInterfaceMessage
{
    public bool Enabled { get; }

    public GasVolumePumpToggleStatusMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class GasVolumePumpChangeTransferRateMessage : BoundUserInterfaceMessage
{
    public float TransferRate { get; }

    public GasVolumePumpChangeTransferRateMessage(float transferRate)
    {
        TransferRate = transferRate;
    }
}
