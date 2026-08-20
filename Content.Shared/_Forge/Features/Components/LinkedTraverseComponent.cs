namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Lets a player interact with a linked hatch/ladder to teleport to the paired entity.
/// </summary>
[RegisterComponent]
public sealed partial class LinkedTraverseComponent : Component
{
    [DataField]
    public string VerbText = "remnant-hatch-traverse-verb";

    [DataField]
    public TimeSpan UseDelay = TimeSpan.FromSeconds(0.8);
}
