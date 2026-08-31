using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Features;

[Serializable, NetSerializable]
public enum CombinationLockUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CombinationLockUiState : BoundUserInterfaceState
{
    public int EnteredLength;
    public int MaxLength;
    public CombinationLockUiStatus Status;

    public CombinationLockUiState(int enteredLength, int maxLength, CombinationLockUiStatus status)
    {
        EnteredLength = enteredLength;
        MaxLength = maxLength;
        Status = status;
    }
}

[Serializable, NetSerializable]
public enum CombinationLockUiStatus : byte
{
    Idle,
    Success,
    Failure
}

[Serializable, NetSerializable]
public sealed class CombinationLockKeypadMessage : BoundUserInterfaceMessage
{
    public int Value;

    public CombinationLockKeypadMessage(int value)
    {
        Value = value;
    }
}

[Serializable, NetSerializable]
public sealed class CombinationLockEnterMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CombinationLockClearMessage : BoundUserInterfaceMessage
{
}
