using Robust.Shared.Serialization;

namespace Content.Shared._Forge.LivingNpc;

/// <summary>
/// What this NPC remembers about another entity.
/// </summary>
[DataDefinition, Serializable]
public sealed partial class LivingNpcMemoryEntry
{
    [ViewVariables]
    public EntityUid Entity;

    /// <summary>-1 hated, 0 unknown, 1 trusted friend.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float Reputation;

    [ViewVariables]
    public TimeSpan LastSeen;

    [ViewVariables]
    public TimeSpan LastTalked;

    [ViewVariables]
    public int TimesTalked;

    [ViewVariables]
    public bool HurtMe;

    [ViewVariables]
    public bool HelpedMe;

    [ViewVariables]
    public string? RememberedName;
}
