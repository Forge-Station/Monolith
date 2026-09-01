using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ProjectileBatteryAmmoProviderComponent : BatteryAmmoProviderComponent
{
    [ViewVariables(VVAccess.ReadWrite), DataField("proto", required: true, customTypeSerializer: typeof(ProtoId<EntityPrototype>))]
    public string Prototype = default!;
}
