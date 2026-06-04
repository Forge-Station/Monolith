using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BsEnergy;

[Serializable, NetSerializable]
public enum BsEnergyUiKey : byte
{
    ReceiverKey,
    TransmitterKey,
}

[Serializable, NetSerializable]
public enum BsEnergyVisuals : byte
{
    Enabled,
}

[Serializable, NetSerializable]
public sealed class UpdateTransmitterStateData
{
    public int Price;
    public int CurrentConnected;
    public int MaxConnected;
    public float TransmitterAvailablePower;
    public string GridTransmitterName = string.Empty;
}

[Serializable, NetSerializable]
public sealed class ChoiceTransmitterMessage : BoundUserInterfaceMessage
{
    public NetEntity NetUid;
}

[Serializable, NetSerializable]
public sealed class ChangePowerMessage : BoundUserInterfaceMessage
{
    public int Power;
}

[Serializable, NetSerializable]
public sealed class EnableToggleMessage : BoundUserInterfaceMessage
{
    public bool Enabled;
}

[Serializable, NetSerializable]
public sealed class WithdrawMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PriceMessage : BoundUserInterfaceMessage
{
    public int Price;
}

[Serializable, NetSerializable]
public sealed class BsReceiverInterfaceStateMessage : BoundUserInterfaceState
{
    public bool Enabled;
    public int StepSize;
    public int MaxValue;
    public int RequestedPower;
    public int Money;
    public int ReceivedPower;
    public (float Load, float Supply)? NetworkStats;
    public NetEntity ConnectedTransmitter;
    public Dictionary<NetEntity, UpdateTransmitterStateData> TransmittersData = new();
}

[Serializable, NetSerializable]
public sealed class BsTransmitterInterfaceStateMessage : BoundUserInterfaceState
{
    public bool Enabled;
    public float Income;
    public int StepSize;
    public int MaxConnected;
    public int MaxValue;
    public int ConnectedCount;
    public int TargetPower;
    public int Price;
    public int Money;
    public int PowerConsumer;
    public int AvailablePower;
    public (float Load, float Supply)? NetworkStats;
}
