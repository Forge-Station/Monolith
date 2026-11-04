using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WildSlimes;

/// <summary>
/// A wild breed's home climate: outside it the slime lives but won't grow or split. Given through the breed's
/// components so its offspring keep it.
/// </summary>
[RegisterComponent]
public sealed partial class WFSlimeClimateComponent : Component
{
    /// <summary>Coldest air it breeds in, in kelvin. Vacuum counts as 2.7 K.</summary>
    [DataField]
    public float MinTemperature;

    /// <summary>Hottest air it breeds in, in kelvin.</summary>
    [DataField]
    public float MaxTemperature = float.MaxValue;

    /// <summary>If set, it only breeds while touching a pool or puddle holding one of these.</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Underfoot = new();

    /// <summary>Fluent id for the examine line while it is out of its climate.</summary>
    [DataField]
    public LocId Unsettled = "wf-wild-slime-unsettled";
}
