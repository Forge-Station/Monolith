namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Marks an entity as a candidate for <see cref="SpaceCleanupSystem"/>. Maintained
///     automatically by <see cref="SpaceCleanupTargetSystem"/>: added when an entity's
///     parent becomes a map (entity dropped/thrown into open space) and removed when it
///     returns onto a grid or into a container.
/// </summary>
/// <remarks>
///     Forge-Change: narrows the periodic cleanup scan from "every PhysicsComponent in
///     the world" to "entities actually drifting in space", which is a tiny fraction.
///     A periodic safety scan in <see cref="SpaceCleanupTargetSystem"/> retroactively
///     tags any entities the event-based path missed (e.g. unusual spawn flows).
/// </remarks>
[RegisterComponent]
public sealed partial class SpaceCleanupTargetComponent : Component
{
}
