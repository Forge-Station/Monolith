using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Armor;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMArmorSystem))]
public sealed partial class CMArmorPiercingComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Amount;

    [DataField, AutoNetworkedField]
    public bool IgnoreXenoArmor;
}
