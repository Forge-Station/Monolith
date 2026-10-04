using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BsEnergy;

[Serializable, NetSerializable]
public enum BsEnergyUiKey : byte
{
    ReceiverKey,
    TransmitterKey,
    AuthenticationKey,
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
    public int LimitConnecting;
    public float TransmitterAvailablePower;
    public string GridTransmitterName = string.Empty;
    public bool HasPassword;
}

[Serializable, NetSerializable]
public sealed class UpdateReceiversData
{
    public string GridReceiverName = string.Empty;
    public float RequestedPower;
    public int ReceivedPower;
    public int Priority;
}

[Serializable, NetSerializable]
public sealed class UpdateHistoryData
{
    [ViewVariables]
    public string GridReceiverName = string.Empty;

    [ViewVariables]
    public ulong TotalEnergyReceived;

    [ViewVariables]
    public ulong TotalMoneyTransferred;
}

[Serializable, NetSerializable]
public sealed class ChoiceTransmitterMessage : BoundUserInterfaceMessage
{
    public NetEntity Transmitter;
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
public sealed class OpenAuthenticationMessage : BoundUserInterfaceMessage
{
    public NetEntity Transmitter;
}

[Serializable, NetSerializable]
public sealed class WithdrawMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PriceMessage : BoundUserInterfaceMessage
{
    public int Price;
}

[Serializable, NetSerializable]
public sealed class ConnectingLimitMessage : BoundUserInterfaceMessage
{
    public int LimitConnecting;
}

[Serializable, NetSerializable]
public sealed class KickMessage : BoundUserInterfaceMessage
{
    public NetEntity KickedReceiver;
}

[Serializable, NetSerializable]
public sealed class PasswordMessage : BoundUserInterfaceMessage
{
    public string Password = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SendPasswordToConnectMessage : BoundUserInterfaceMessage
{
    public NetEntity Transmitter;
    public string Password = string.Empty;
    public int RequestId;
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
public sealed class BsReceiverAuthenticationInterfaceStateMessage : BoundUserInterfaceState
{
    public NetEntity Transmitter;
    public bool ConnectingResult;
    public bool IsResponse;
    public string TransmitterGridName = string.Empty;
    public int RequestId;
}

[Serializable, NetSerializable]
public sealed class BsTransmitterInterfaceStateMessage : BoundUserInterfaceState
{
    public bool Enabled;
    public float Income;
    public int StepSize;
    public int MaxConnected;
    public int ConnectingLimit;
    public int MaxValue;
    public int ConnectedCount;
    public int TargetPower;
    public int Price;
    public int Money;
    public int PowerConsumer;
    public int AvailablePower;
    public string Password = string.Empty;
    public (float Load, float Supply)? NetworkStats;
    public Dictionary<NetEntity, UpdateReceiversData> ReceiversData = new();
    public Dictionary<NetEntity, UpdateHistoryData> HistoryData = new();
}
