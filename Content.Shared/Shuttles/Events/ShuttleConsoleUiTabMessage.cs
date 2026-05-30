using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

/// <summary>
/// Client tells the server which shuttle console tab is visible so map beacon data can be omitted on Nav/Dock.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleUiTabMessage : BoundUserInterfaceMessage
{
    public ShuttleConsoleUiTab Tab;
}
