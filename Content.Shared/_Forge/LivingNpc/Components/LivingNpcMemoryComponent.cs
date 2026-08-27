namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Per-entity social memory for a living NPC.
/// </summary>
[RegisterComponent]
public sealed partial class LivingNpcMemoryComponent : Component
{
    public const int MaxEntries = 24;

    [ViewVariables]
    public Dictionary<EntityUid, LivingNpcMemoryEntry> Entries { get; } = new();

    [ViewVariables]
    public EntityUid? LastSpeaker;

    [ViewVariables]
    public string? LastHeardPhrase;
}
