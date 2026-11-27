using Robust.Shared.GameStates;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// Marks a non-organic brain organ. A body whose brain has this ignores chemical doses and runs neuroware instead.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFNeurowareHostComponent : Component;
