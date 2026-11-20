namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>
    /// Client: entities off the ground, or just landed, that the per-frame sprite passes walk. Walking every entity with
    /// Z physics, tens of thousands of them, took over a third of a live client's game thread.
    /// </summary>
    public readonly HashSet<EntityUid> WFRaised = new();

    /// <summary>Adds an entity to <see cref="WFRaised"/> when it has a height. The sprite pass removes it once landed.</summary>
    protected void WFNoteHeight(EntityUid uid, float height)
    {
        if (height != 0f && _net.IsClient)
            WFRaised.Add(uid);
    }
}
