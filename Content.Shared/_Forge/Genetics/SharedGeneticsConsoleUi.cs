using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Genetics;

[Serializable, NetSerializable]
public enum GeneticsConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum GeneticsConsoleAction : byte
{
    Irradiate,
    Identify,
    Activate,
    Deactivate,
    Isolate,
    Clean,
    Eject,
    PulseBlock,
}

[Serializable, NetSerializable]
public enum GeneBlockHint : byte
{
    None = 0,
    OffTrack = 1,
    OnTrack = 2,
}

[Serializable, NetSerializable]
public sealed class GeneticsConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool Powered;
    public bool ScannerConnected;
    public bool ScannerInRange;
    public bool OccupantPresent;
    public bool OccupantCritical;
    public string OccupantName = string.Empty;
    public string UniqueDna = string.Empty;
    public int Instability;
    public int InstabilityThreshold;
    public float Mutagen;
    public float MutagenMax;
    public float IrradiateCost;
    public float PulseCost;
    public List<GeneticsConsoleBranchEntry> Branches = new();
    public List<GeneticsConsoleDiscoveryEntry> Discoveries = new();
}

[Serializable, NetSerializable]
public sealed class GeneticsConsoleBranchEntry
{
    public string Id = string.Empty;
    public GeneBranch Branch;
    public List<string> Sequence = new();
    public bool Expressed;
    public bool Discovered;
    public bool CanIsolate;
    public bool CanClean;
    public bool CanDeactivate;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public List<byte> BlockHints = new();
}

[Serializable, NetSerializable]
public sealed class GeneticsConsoleDiscoveryEntry
{
    public string GeneId = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public GeneQuality Quality;
    public GeneBranch Branch;
    public string Sequence = string.Empty;
}

[Serializable, NetSerializable]
public sealed class GeneticsConsoleActionMessage : BoundUserInterfaceMessage
{
    public GeneticsConsoleAction Action;
    public string? GeneId;
    public int? BlockIndex;

    public GeneticsConsoleActionMessage(GeneticsConsoleAction action, string? geneId = null, int? blockIndex = null)
    {
        Action = action;
        GeneId = geneId;
        BlockIndex = blockIndex;
    }
}
