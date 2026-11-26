using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._WF.JigglePhysics;

/// <summary>
/// Makes sprite layers wobble with the entity's movement, simulated on the client as a spring through displacement
/// maps.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class JigglePhysicsComponent : Component
{
    /// <summary>
    /// Spring constant pulling the wobble back to rest.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float Springiness;

    /// <summary>
    /// Damping on the wobble's velocity.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float Damping;

    /// <summary>
    /// How strongly the entity's acceleration pushes the wobble.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float InertiaScale;

    /// <summary>
    /// Largest wobble offset.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float JiggleLimit;

    /// <summary>
    /// Largest wobble velocity.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float SlapLimit;

    [DataField(required: true), AutoNetworkedField]
    public ResPath DisplacementsRsi;

    [DataField, AutoNetworkedField]
    public string DisplacementPrefix = "jiggle_";

    /// <summary>
    /// Displacement states each way from rest: the RSI has states from <c>-count</c> to <c>count</c>.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public int DisplacementCount;

    /// <summary>
    /// Sprite layer keys to wobble; enum keys use the <c>enum.</c> form.
    /// </summary>
    [DataField(required: true)]
    public List<string> Layers = new();
}
