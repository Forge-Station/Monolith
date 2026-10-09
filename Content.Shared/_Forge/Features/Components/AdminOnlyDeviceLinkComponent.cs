namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Blocks in-round device-link changes unless the user is an admin.
/// Map loading and other null-user linkers still work.
/// </summary>
[RegisterComponent]
public sealed partial class AdminOnlyDeviceLinkComponent : Component;
