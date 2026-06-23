using Robust.Shared.GameStates;

namespace Content.Shared._Forge.ShipWeapons.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ShipWeaponBoardComponent : Component
{
    [DataField]
    public TimeSpan FabricationTime = TimeSpan.FromSeconds(10);

    [DataField]
    public int RequiredPartRating = 1;
}
