using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Melee;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCMeleeWeaponSystem))]
public sealed partial class ImmuneToUnarmedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool ApplyToXenos;
}
