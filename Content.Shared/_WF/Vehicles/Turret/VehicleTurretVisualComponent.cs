using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;

namespace Content.Shared._WF.Vehicles.Turret;

/// <summary>The drawn overlay of a turret: which turret it follows.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class VehicleTurretVisualComponent : Component
{
    [AutoNetworkedField]
    public NetEntity Turret;

    [NonSerialized]
    public bool SpriteInitialized;
}
