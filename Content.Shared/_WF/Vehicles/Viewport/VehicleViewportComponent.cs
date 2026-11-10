using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Viewport;

/// <summary>A window in an interior that shows the outside of the vehicle when used.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleViewportComponent : Component;

/// <summary>On someone looking through a viewport or into a doorway: where their eye was and what they are looking at.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleViewportUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? PreviousTarget;

    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField, AutoNetworkedField]
    public EntityUid? PeekTarget;
}
