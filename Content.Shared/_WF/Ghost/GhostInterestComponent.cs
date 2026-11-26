namespace Content.Shared._WF.Ghost;

/// <summary>
/// Lists the entity under Points of Interest in the ghost orbit menu.
/// Singularities, tesla balls, nukes, the disk, rifts and anomalies are picked up without it.
/// </summary>
[RegisterComponent]
public sealed partial class GhostInterestComponent : Component
{
    /// <summary>Shown under the name; defaults to the prototype name.</summary>
    [DataField]
    public LocId? Label;
}
