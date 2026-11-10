using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>Marks a select-hardpoint action and the mounted weapon it picks.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(VehicleWeaponsSystem))]
public sealed partial class VehicleHardpointActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? MountedWeapon;

    [DataField, AutoNetworkedField]
    public int SortOrder;
}

/// <summary>The hotbar action that picks a hardpoint.</summary>
public sealed partial class VehicleHardpointSelectActionEvent : InstantActionEvent;
