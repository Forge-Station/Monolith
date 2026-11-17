using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Warehouse;

[Serializable, NetSerializable]
public enum WarehouseSiloUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class WarehouseSiloBuiState : BoundUserInterfaceState
{
    public readonly List<WarehouseSiloEntry> Entries;

    public WarehouseSiloBuiState(List<WarehouseSiloEntry> entries)
    {
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public readonly record struct WarehouseSiloEntry(string StackType, string Name, int Count);

[Serializable, NetSerializable]
public sealed class WarehouseSiloWithdrawMessage : BoundUserInterfaceMessage
{
    public readonly string StackType;

    public WarehouseSiloWithdrawMessage(string stackType)
    {
        StackType = stackType;
    }
}
