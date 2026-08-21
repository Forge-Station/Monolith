namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Invokes a device-link source port whenever this entity is triggered.
/// </summary>
[RegisterComponent]
public sealed partial class DeviceLinkOnTriggerComponent : Component
{
    [DataField]
    public string Port = "Pressed";
}
