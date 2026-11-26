namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// On a body lying in an autodoc. Answers the body's breathing and exposure events with the pod's own air while the
/// pod is sealed; otherwise they fall through to the tile as for anybody else. Added on insertion, removed on eject.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedAutodocOccupantComponent : Component
{
    /// <summary>The pod the body is lying in.</summary>
    [ViewVariables]
    public EntityUid Pod;
}
