namespace Content.Server._WF.Remade.Counterplay;

/// <summary>
/// A Remade whose Monolith has fallen. It is cut loose from the Director, hurt, slower and weaker, and wastes away if nobody finishes
/// it first.
/// </summary>
[RegisterComponent]
public sealed partial class WFFeralComponent : Component
{
    /// <summary>Share of its health it loses on being cut loose.</summary>
    [DataField]
    public float HurtFraction = 0.3f;

    [DataField]
    public float SpeedFactor = 0.7f;

    [DataField]
    public float MeleeFactor = 0.6f;

    /// <summary>How long it lasts before it starts to waste away.</summary>
    [DataField]
    public TimeSpan Lasts = TimeSpan.FromMinutes(10);

    /// <summary>Damage a second once it is wasting.</summary>
    [DataField]
    public float WitherPerSecond = 1.5f;

    [ViewVariables]
    public TimeSpan WithersAt;

    [ViewVariables]
    public TimeSpan NextWither;
}
