using Content.Shared.Tag;
using Robust.Shared.Containers;

namespace Content.Shared._Forge.Monitoring;

public static class ForgeHandheldMonitoringHelper
{
    public static EntityUid? GetMonitoringGrid(
        EntityUid uid,
        IEntityManager entityManager,
        SharedTransformSystem transformSystem,
        SharedContainerSystem containerSystem,
        TagSystem tagSystem)
    {
        var gridUid = transformSystem.GetGrid(uid);
        if (gridUid != null)
            return gridUid;

        if (!tagSystem.HasTag(uid, "ForgeHandheldMonitoringConsole"))
            return null;

        if (!containerSystem.TryGetContainingContainer((uid, null, null), out var container))
            return null;

        var holder = container.Owner;
        if (holder == uid)
            return null;

        return transformSystem.GetGrid(holder);
    }
}
