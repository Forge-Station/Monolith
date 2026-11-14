namespace Content.Server._WF.Remade.Environment;

/// <summary>A piece of ambient decor a Monolith grew over its corruption. It has no logic; it is parented to its tile and goes with it.</summary>
[RegisterComponent]
public sealed partial class WFEnvDecorComponent : Component
{
    /// <summary>The Monolith whose budget it counts against.</summary>
    [ViewVariables]
    public EntityUid? Stone;

    /// <summary>Whether it counts against the Monolith's decor budget. Mist doesn't.</summary>
    [ViewVariables]
    public bool Counted = true;

    /// <summary>A console face that now and then throws sparks.</summary>
    [DataField]
    public bool Sparks;
}

/// <summary>On a corruption tile that has been decorated up to a stage, so each tile rolls once per stage.</summary>
[RegisterComponent]
public sealed partial class WFEnvDecoratedComponent : Component
{
    /// <summary>The highest stage this tile has rolled decor for.</summary>
    [ViewVariables]
    public int Level;

    /// <summary>It already has veins or a screen over what stands on it.</summary>
    [ViewVariables]
    public bool Covered;
}

/// <summary>The decor and ambient-noise bookkeeping of one Monolith.</summary>
[RegisterComponent]
public sealed partial class WFEnvStateComponent : Component
{
    /// <summary>Counted decor entities alive for this Monolith, which the budget limits. Mist isn't counted.</summary>
    [ViewVariables]
    public int Decor;

    [ViewVariables]
    public TimeSpan NextAmbient;

    [ViewVariables]
    public TimeSpan NextSpark;

    /// <summary>Sparks thrown so far. Tests read it.</summary>
    [ViewVariables]
    public int Sparked;

    /// <summary>Noises played so far. Tests read it.</summary>
    [ViewVariables]
    public int Noises;
}
