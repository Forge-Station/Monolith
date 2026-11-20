using Content.Shared.Body.Part;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// On a dismemberment wound: which part it is the stump of. The stump sits on the parent part, and a torso can hold
/// several, so the wound itself remembers. Set by <c>WolfmedStumpTagSystem</c> when a limb is torn off.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedStumpComponent : Component
{
    [DataField]
    public BodyPartType PartType = BodyPartType.Other;

    [DataField]
    public BodyPartSymmetry Symmetry = BodyPartSymmetry.None;
}
