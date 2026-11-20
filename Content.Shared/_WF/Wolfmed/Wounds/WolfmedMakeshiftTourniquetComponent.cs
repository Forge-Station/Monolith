namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// A tourniquet torn from cloth. It clamps like the real one, but a hard hit on the limb knocks it loose.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedMakeshiftTourniquetComponent : Component
{
    /// <summary>Damage from one hit on the strapped part that makes it slip.</summary>
    [DataField]
    public float SlipDamage = 15f;
}
