using Robust.Shared.GameStates;

namespace Content.Shared._WF.Caverns;

/// <summary>A climb point in a cavern, under the lip of a hole, that leads back up to the ground.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFCavernClimbComponent : Component
{
    /// <summary>Seconds to climb up, with the surface's gravity already applied.</summary>
    [DataField, AutoNetworkedField]
    public float Delay = 4f;
}
