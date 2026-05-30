using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles;

/// <summary>
/// Active tab on the shuttle helm BUI, replicated from client for server-side state packing.
/// </summary>
[Serializable, NetSerializable]
public enum ShuttleConsoleUiTab : byte
{
    Nav = 0,
    Map = 1,
    Dock = 2,
}
