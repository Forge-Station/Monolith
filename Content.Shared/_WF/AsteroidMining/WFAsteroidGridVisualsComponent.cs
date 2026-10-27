using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// An asteroid grid's copy of its rock's look. Grids reach every client while the rock only reaches those in view range,
/// so clients draw the rock from this when it is too far to have been sent.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAsteroidGridVisualsComponent : Component
{
    [DataField, AutoNetworkedField] public EntProtoId? Rock;
    [DataField, AutoNetworkedField] public int Variant;
    [DataField, AutoNetworkedField] public string? Look;
    [DataField, AutoNetworkedField] public Color RockColor = Color.White;
    [DataField, AutoNetworkedField] public Color VeinColor = Color.White;
}
