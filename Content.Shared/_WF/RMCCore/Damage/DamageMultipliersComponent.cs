using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Damage;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCDamageableSystem))]
public sealed partial class DamageMultipliersComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public Dictionary<DamageMultiplierFlag, float> Multipliers;
}
