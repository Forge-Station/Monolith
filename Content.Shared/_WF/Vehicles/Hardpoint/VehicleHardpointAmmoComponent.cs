using System.Collections.Generic;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>A hardpoint gun's ammo: the ready magazine is the gun's own, the rest are counted in the reserve.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleHardpointAmmoComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MagazineSize = 1;

    [DataField, AutoNetworkedField]
    public int MaxStoredMagazines = 0;

    [DataField, AutoNetworkedField]
    public int StoredMagazines = 0;

    [DataField, AutoNetworkedField]
    public int StoredRounds = 0;

    [DataField, AutoNetworkedField]
    public List<int> StoredRoundSlots = new();
}
