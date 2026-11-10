using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Base;

/// <summary>On an operator: the vehicle whose view they are looking through.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleWatchingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Watching;
}
