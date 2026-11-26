using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Melee;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCMeleeWeaponSystem))]
public sealed partial class MeleeResetComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan OriginalTime;
}
