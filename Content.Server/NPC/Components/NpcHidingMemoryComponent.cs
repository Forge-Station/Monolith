namespace Content.Server.NPC.Components;

/// <summary>
/// What a walking NPC has actually seen someone climb into.
/// A person already shut in a crate is not a target until this NPC watches them enter.
/// </summary>
[RegisterComponent]
public sealed partial class NpcHidingMemoryComponent : Component
{
    /// <summary>
    /// Hostiles who were visible and not inside storage last check.
    /// </summary>
    public HashSet<EntityUid> SeenLoose = new();

    /// <summary>
    /// Scratch set swapped with <see cref="SeenLoose"/> so the check does not allocate.
    /// </summary>
    public HashSet<EntityUid> SeenScratch = new();

    /// <summary>
    /// Crates and lockers this NPC saw a living hostile close themselves in.
    /// </summary>
    public HashSet<EntityUid> WitnessedContainers = new();
}
