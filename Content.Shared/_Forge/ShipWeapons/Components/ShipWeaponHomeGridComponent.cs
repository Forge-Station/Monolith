namespace Content.Shared._Forge.ShipWeapons.Components;

/// <summary>
/// Marks a mapped ship weapon as bound to the grid it was originally placed on.
/// </summary>
[RegisterComponent]
public sealed partial class ShipWeaponHomeGridComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? HomeGrid;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool LockToHomeGrid = true;
}
