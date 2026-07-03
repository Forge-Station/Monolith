// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// Placed on a person who has had an emergency-limb implant surgically installed in their torso.
/// Grants a single-use action that regrows a missing arm/leg of the player's choice. Once used
/// (or if the implant surgery fails because one is already installed), the component is removed.
/// </summary>
[RegisterComponent]
public sealed partial class EmergencyLimbRegrowerComponent : Component
{
    [DataField]
    public EntProtoId LeftArm = "EmergencyLeftArm";

    [DataField]
    public EntProtoId RightArm = "EmergencyRightArm";

    [DataField]
    public EntProtoId LeftLeg = "EmergencyLeftLeg";

    [DataField]
    public EntProtoId RightLeg = "EmergencyRightLeg";

    /// <summary>
    /// Burn damage dealt when a limb is regrown (cauterization).
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public EntProtoId Action = "ActionRegrowEmergencyLimb";

    [DataField]
    public EntityUid? ActionEntity;
}
