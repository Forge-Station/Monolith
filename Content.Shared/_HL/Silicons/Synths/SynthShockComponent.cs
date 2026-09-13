using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._HL.Silicons.Synths;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(Other = AccessPermissions.Read)]
public sealed partial class SynthShockComponent : Component
{
    /// <summary>
    /// Damage type that grants battery charge when taken.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<DamageTypePrototype> ShockDamageType = "Shock";

    /// <summary>
    /// Battery charge gained per point of matching shock damage taken.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BatteryChargeMultiplier = 2f;

    /// <summary>
    /// Damage type applied as a side effect of matching shock damage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<DamageTypePrototype> CellularDamageType = "Cellular";

    /// <summary>
    /// Cellular damage dealt per point of matching shock damage taken.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CellularDamageMultiplier = 0.2f;
}
