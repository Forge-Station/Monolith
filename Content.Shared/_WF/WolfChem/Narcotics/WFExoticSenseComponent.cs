using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// On a body whose high senses exotic matter (<see cref="WFDrugTrait.ExoticSense"/>): where the nearest rock that
/// holds some is, for the light on its owner's screen.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFExoticSenseComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>True while a rock is in reach.</summary>
    [DataField, AutoNetworkedField]
    public bool HasTarget;

    /// <summary>World position of the rock, on the owner's map.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Target;

    /// <summary>How bright the light is, 0-1: the rock's share of the richest a rock gets.</summary>
    [DataField, AutoNetworkedField]
    public float Richness;
}
