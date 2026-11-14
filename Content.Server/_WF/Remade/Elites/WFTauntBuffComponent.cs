namespace Content.Server._WF.Remade.Elites;

/// <summary>A hunter's taunt is on this mob: it takes less damage until the buff runs out.</summary>
[RegisterComponent]
public sealed partial class WFTauntBuffComponent : Component
{
    /// <summary>The share of damage it takes. 0.7 is 30% off.</summary>
    [DataField]
    public float Multiplier = 0.7f;

    /// <summary>When the buff ends, unless the taunt renews it.</summary>
    [ViewVariables]
    public TimeSpan Until;
}
