namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>The sprite states a part shows on the vehicle, whole and damaged.</summary>
[RegisterComponent]
public sealed partial class HardpointVisualComponent : Component
{
    [DataField(required: true)]
    public string VehicleState = string.Empty;

    [DataField]
    public string DamagedVehicleState = string.Empty;
}
