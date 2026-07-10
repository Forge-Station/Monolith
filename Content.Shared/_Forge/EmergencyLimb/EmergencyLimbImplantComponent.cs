// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Damage;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.EmergencyLimb;

/// <summary>
/// An emergency cybernetic-limb implant item. While activated (red cauterizer lit, via ItemToggle),
/// using it on a humanoid that's missing an arm or leg welds a crude replacement limb into the empty
/// slot, dealing burn damage in the process. The replacement works but has worse stats than a real limb.
///
/// Also implements <see cref="ISurgeryToolComponent"/> so the implant can be used as the tool for the
/// "implant into torso" surgery step (Shitmed surgery only accepts entities whose tool component
/// implements this interface).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class EmergencyLimbImplantComponent : Component, ISurgeryToolComponent
{
    /// <summary>
    /// Damage dealt to the patient when the limb is installed (the cauterization burn).
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public EntProtoId LeftArm = "EmergencyLeftArm";

    [DataField]
    public EntProtoId RightArm = "EmergencyRightArm";

    [DataField]
    public EntProtoId LeftLeg = "EmergencyLeftLeg";

    [DataField]
    public EntProtoId RightLeg = "EmergencyRightLeg";

    /// <summary>
    /// Whether the implant is consumed (deleted) after a successful install. One-shot emergency tool.
    /// </summary>
    [DataField]
    public bool ConsumeOnUse = true;

    // --- ISurgeryToolComponent (lets the implant act as the surgery tool) ---

    public string ToolName => "аварийный имплант-конечность";

    public bool? Used { get; set; } = null;

    [DataField]
    public float Speed { get; set; } = 1f;
}
