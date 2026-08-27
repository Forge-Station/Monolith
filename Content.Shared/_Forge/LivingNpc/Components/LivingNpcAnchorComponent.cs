namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Optional home marker. Nearby living NPCs adopt these coordinates as home on spawn.
/// </summary>
[RegisterComponent]
public sealed partial class LivingNpcAnchorComponent : Component
{
    [DataField]
    public float Radius = 16f;

    [DataField]
    public string? Group;
}
