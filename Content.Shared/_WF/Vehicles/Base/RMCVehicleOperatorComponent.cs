using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Base;

/// <summary>
/// Tracking component for handling the operator of a given <see cref="RMCVehicleComponent"/>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCVehicleSystem))]
public sealed partial class RMCVehicleOperatorComponent : Component
{
    /// <summary>
    /// The vehicle we are currently operating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Vehicle;
}
