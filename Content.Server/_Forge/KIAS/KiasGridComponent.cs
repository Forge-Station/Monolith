using Content.Shared._Forge.KIAS;

namespace Content.Server._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasGridComponent : Component
{
    public EntityUid? Core;
    public bool Active;
    public uint Revision;
    public bool Testing;
    public int Entities;
    public int Crew;
    public string CrewDetails = string.Empty;
    public string PowerDetails = string.Empty;
    public readonly HashSet<KiasDeviceRole> Roles = new();
    public readonly Queue<string> Log = new();
    public readonly HashSet<EntityUid> Devices = new();
    public readonly HashSet<EntityUid> Cables = new();
    public readonly HashSet<EntityUid> Online = new();
    public readonly List<EntityUid> ProtocolTargets = new();
    public readonly KiasTopology Topology = new();
}
