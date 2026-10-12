using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.OrePipe;

[Serializable, NetSerializable]
public enum OreHoldUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OreHoldBoundUserInterfaceState : BoundUserInterfaceState
{
    public List<OreHoldEntry> Entries;
    public int TotalCount;
    public int Capacity;

    public OreHoldBoundUserInterfaceState(List<OreHoldEntry> entries, int totalCount, int capacity)
    {
        Entries = entries;
        TotalCount = totalCount;
        Capacity = capacity;
    }
}

[Serializable, NetSerializable]
public sealed class OreHoldEntry
{
    public string PrototypeId;
    public string Name;
    public int Count;

    public OreHoldEntry(string prototypeId, string name, int count)
    {
        PrototypeId = prototypeId;
        Name = name;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class OreHoldEjectMessage : BoundUserInterfaceMessage
{
    public string PrototypeId;
    public int Amount;

    public OreHoldEjectMessage(string prototypeId, int amount)
    {
        PrototypeId = prototypeId;
        Amount = amount;
    }
}
