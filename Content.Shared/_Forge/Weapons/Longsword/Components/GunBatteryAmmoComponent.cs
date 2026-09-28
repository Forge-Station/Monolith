using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.Longsword.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunBatteryAmmoComponent : Component
{
    
    [DataField, AutoNetworkedField]
    public float FireCost = 100f;

    
    [DataField]
    public LocId NoChargePopup = "gun-battery-no-charge";
}