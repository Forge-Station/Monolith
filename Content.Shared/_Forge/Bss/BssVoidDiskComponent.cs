namespace Content.Shared._Forge.Bss;

/// <summary>
/// Coordinates disk that opens a warp route into a hidden (black) system.
/// </summary>
[RegisterComponent]
public sealed partial class BssVoidDiskComponent : Component
{
    /// <summary>
    /// If set, unlocks this hidden system. Otherwise a random still-locked one is chosen.
    /// </summary>
    [DataField]
    public string? TargetSystem;
}
