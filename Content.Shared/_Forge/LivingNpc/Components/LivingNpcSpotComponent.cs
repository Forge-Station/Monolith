namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Marker the living NPC treats as a place to work, rest, socialize, or patrol.
/// </summary>
[RegisterComponent]
public sealed partial class LivingNpcSpotComponent : Component
{
    [DataField]
    public LivingNpcSpotKind Kind = LivingNpcSpotKind.Work;

    /// <summary>
    /// If set, only NPCs with this group use the spot.
    /// </summary>
    [DataField]
    public string? Group;
}
