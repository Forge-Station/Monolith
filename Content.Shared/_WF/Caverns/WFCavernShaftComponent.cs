using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>A shade over a hole in the ground: what lies below it, for its examine.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFCavernShaftComponent : Component
{
    /// <summary>The cavern the shaft drops into.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<WFCavernPrototype>? Cavern;

    /// <summary>The cavern's air, classified when the shade spawned.</summary>
    [DataField, AutoNetworkedField]
    public WFCavernAir Air;

    /// <summary>The fall damage multiplier of the cavern tile under the hole.</summary>
    [DataField, AutoNetworkedField]
    public float LandingMultiplier = 1f;
}
