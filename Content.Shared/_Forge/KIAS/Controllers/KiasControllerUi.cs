using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS.Controllers;

[Serializable, NetSerializable]
public enum KiasControllerUiKey : byte { Programmer, Rack }

[Serializable, NetSerializable]
public enum KiasGraphEdit : byte
{
    Refresh, Add, Move, Configure, Remove, Connect, Disconnect, Rename, Preset, Discard, Write, Eject, Enabled, ImportLegacy, Catalog
}

[Serializable, NetSerializable]
public sealed class KiasGraphNodeView
{
    public int Id;
    public KiasNodeKind Kind;
    public float X, Y;
    public string Profile = string.Empty, DeviceName = string.Empty, Room = string.Empty, Group = string.Empty;
    public NetEntity? Binding;
    public KiasNodeConfig Config = new();
    public List<KiasGraphPort> Ports = new();
    public int Matched;
}

[Serializable, NetSerializable]
public sealed class KiasGraphDeviceView
{
    public NetEntity Entity;
    public string Name = string.Empty, Profile = string.Empty;
    public string Identifier = string.Empty, Room = string.Empty;
    public bool NamedRoom;
    public int RoomOrder;
}

[Serializable, NetSerializable]
public sealed class KiasGraphProfileView
{
    public string Id = string.Empty;
    public List<KiasGraphPort> Ports = new();
}

[Serializable, NetSerializable]
public sealed class KiasControllerEditorState : BoundUserInterfaceState
{
    public string Name = string.Empty;
    public uint Revision;
    public bool HasCard, Online, Dirty, Editing;
    public bool Mapping;
    public bool Enabled;
    public string CatalogQuery = string.Empty;
    public int CatalogPage, CatalogTotal;
    public List<KiasGraphNodeView> Nodes = new();
    public List<KiasControllerWire> Wires = new();
    public List<KiasGraphDeviceView> Devices = new();
    public List<string> Identifiers = new();
    public List<KiasGraphProfileView> Profiles = new();
    public List<string> Presets = new(), Errors = new();
    public List<string> Legacy = new();
}

[Serializable, NetSerializable]
public sealed class KiasControllerEditMessage : BoundUserInterfaceMessage
{
    public uint Revision;
    public KiasGraphEdit Edit;
    public int Node;
    public KiasNodeKind Kind;
    public float X, Y;
    public string Text = string.Empty, Profile = string.Empty, Room = string.Empty, Group = string.Empty;
    public NetEntity? Binding;
    public KiasNodeConfig Config = new();
    public KiasControllerWire Wire = new();
    public bool Enabled;
}

[Serializable, NetSerializable]
public sealed class KiasControllerRackSlotView
{
    public string Name = string.Empty, Status = string.Empty, Fault = string.Empty;
    public bool Inserted, Enabled;
}

[Serializable, NetSerializable]
public sealed class KiasControllerRackState : BoundUserInterfaceState
{
    public bool Online;
    public float Load;
    public int Running;
    public List<KiasControllerRackSlotView> Slots = new();
}

[Serializable, NetSerializable]
public sealed class KiasControllerRackMessage : BoundUserInterfaceMessage
{
    public int Slot;
    public bool Eject, Enabled, Refresh;
}
