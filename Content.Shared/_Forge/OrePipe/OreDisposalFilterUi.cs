using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.OrePipe;

[Serializable, NetSerializable]
public enum OreDisposalFilterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class OreDisposalFilterBoundUserInterfaceState : BoundUserInterfaceState
{
    public List<OreDisposalFilterOption> Options;

    public OreDisposalFilterBoundUserInterfaceState(List<OreDisposalFilterOption> options)
    {
        Options = options;
    }
}

[Serializable, NetSerializable]
public sealed class OreDisposalFilterOption
{
    public string StackId;
    public string Name;
    /// <summary>True when this ore is diverted to the side.</summary>
    public bool Filtered;

    public OreDisposalFilterOption(string stackId, string name, bool filtered)
    {
        StackId = stackId;
        Name = name;
        Filtered = filtered;
    }
}

[Serializable, NetSerializable]
public sealed class OreDisposalFilterSetMessage : BoundUserInterfaceMessage
{
    public List<string> Filtered;

    public OreDisposalFilterSetMessage(List<string> filtered)
    {
        Filtered = filtered;
    }
}

/// <summary>
/// Stack types shown in the ore disposal filter UI (ship-drill ores).
/// </summary>
public static class OreDisposalFilterCatalog
{
    public static readonly ProtoId<StackPrototype>[] StackTypes =
    [
        "SteelOre",
        "SpaceQuartz",
        "Coal",
        "GoldOre",
        "SilverOre",
        "SaltOre",
        "PlasmaOre",
        "UraniumOre",
        "BananiumOre",
        "DiamondOre",
        "BluespaceOre",
        "CopperOre",
        "LithiumOre",
        "ScrapOre",
        "ArtifactFragment",
    ];
}
