using System;
using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>A loader prop in a vehicle's interior. Its window lists the hardpoint guns that take rounds from ammo boxes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class VehicleAmmoLoaderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? HardpointType;

    [DataField, AutoNetworkedField]
    public EntProtoId? BulletType;

    [DataField, AutoNetworkedField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public float InteractionRange = 2.5f;

    [AutoNetworkedField]
    public VehicleAmmoLoaderUiState Ui = new(new List<VehicleAmmoLoaderUiEntry>(), 0, 0, null);
}

/// <summary>On a user while a loader window is open, so the loader hears about their hands changing.</summary>
[RegisterComponent]
[Access(typeof(VehicleAmmoLoaderSystem))]
public sealed partial class VehicleAmmoLoaderUserComponent : Component;
