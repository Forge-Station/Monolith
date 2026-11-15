using Robust.Shared.GameObjects;

namespace Content.Shared.Containers;

/// <summary>
/// Raised on an entity to find a container that should be used in its place.
/// Nothing in this fork subscribes yet; backpack-linked nozzles can fill <see cref="ContainerEntity"/> later.
/// </summary>
[ByRefEvent]
public struct GetConnectedContainerEvent
{
    /// <summary>
    /// Container entity, if one exists.
    /// </summary>
    public EntityUid? ContainerEntity;
}
