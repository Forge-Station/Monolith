using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Synthetics;

/// <summary>On the torso of a chassis whose bay is open: the organ reach lines it had shut, put back on close.</summary>
[RegisterComponent]
public sealed partial class WFChassisBayExposedComponent : Component
{
    /// <summary>The torso's <c>WolfmedBodyPartComponent.OrganReach</c> before the bay opened.</summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> OriginalReach = new();
}
