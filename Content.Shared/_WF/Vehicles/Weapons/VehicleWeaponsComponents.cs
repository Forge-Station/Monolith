using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>A vehicle's weapon crew: who is the primary operator, which weapon each operator has picked.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(VehicleWeaponsSystem))]
public sealed partial class VehicleWeaponsComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;

    [DataField, AutoNetworkedField]
    public EntityUid? SelectedWeapon;

    [NonSerialized]
    public Dictionary<EntityUid, EntityUid> HardpointOperators = new();

    [NonSerialized]
    public Dictionary<EntityUid, EntityUid> OperatorSelections = new();
}

/// <summary>A crewman working a vehicle's guns, set while they are strapped into a weapons seat.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleWeaponsOperatorComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;

    [DataField, AutoNetworkedField]
    public EntityUid? SelectedWeapon;

    [NonSerialized]
    public Dictionary<EntityUid, EntityUid> HardpointActions = new();

    [NonSerialized]
    public TimeSpan NextCooldownFeedbackAt = TimeSpan.Zero;
}

/// <summary>A seat that works the vehicle's turrets; strapping in picks the user's hardpoint and opens the weapons window.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(VehicleWeaponsSystem))]
public sealed partial class VehicleWeaponsSeatComponent : Component
{
    [DataField]
    public bool IsPrimaryOperatorSeat = true;

    [DataField]
    public bool AllowUiSelection = true;

    [DataField]
    public bool AllowHotbarSelection = true;

    [DataField]
    public List<EntProtoId> AllowedHardpointTypes = new();

    [DataField]
    public float BaseViewPvsScale;

    [DataField]
    public float BaseViewCursorMaxOffset;

    [DataField]
    public float BaseViewCursorOffsetSpeed = 0.5f;

    [DataField]
    public float BaseViewCursorPvsIncrease;

    [AutoNetworkedField]
    public VehicleWeaponsUiState Ui = new(default, new List<VehicleWeaponsUiEntry>(), false, false);
}
