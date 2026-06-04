// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// Placed on a person who has had emergency-limb implants installed in their torso. Stores charges
/// (one per implant, up to <see cref="MaxCharges"/>). A granted action regrows a missing arm/leg of
/// the player's choice, spending one charge.
/// </summary>
[RegisterComponent]
public sealed partial class EmergencyLimbRegrowerComponent : Component
{
    [DataField]
    public int Charges;

    [DataField]
    public int MaxCharges = 4;

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
