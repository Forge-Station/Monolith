using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Compat;

/// <summary>Lets clicks pass through the entity while the local player stands inside its sprite, so a cabin roof or wall does not swallow them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InteractionTransparencyComponent : Component
{
    /// <summary>False on a piece that inherits the component but has to stay clickable, such as a door or a viewport.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
