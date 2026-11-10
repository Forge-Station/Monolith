using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>A gun on a firing port. It lives in a vehicle's item slot and is worked from inside by one operator at a time.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;
}

/// <summary>The firing port prop inside the vehicle. Using it takes or leaves the gun in <see cref="GunSlotId"/>.</summary>
[RegisterComponent]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunControllerComponent : Component
{
    [DataField]
    public string GunSlotId = "port-gun";
}

/// <summary>On the user who works a port gun. Answers the gun lookup with the gun, so their shots come from it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunOperatorComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Gun;

    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;

    [DataField, AutoNetworkedField]
    public EntityUid? Controller;
}

/// <summary>A seat a user must be strapped into to work a port gun.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(VehiclePortGunSystem))]
public sealed partial class VehiclePortGunSeatComponent : Component;
