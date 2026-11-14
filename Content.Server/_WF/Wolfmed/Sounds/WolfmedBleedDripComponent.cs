namespace Content.Server._WF.Wolfmed.Sounds;

/// <summary>
/// Sits on a body that is bleeding lightly and holds its drip clock. Added and removed by
/// <see cref="WolfmedBleedDripSystem"/> as the bleed comes and goes, so the tick only walks bodies that drip.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedBleedDripComponent : Component
{
    /// <summary>Earliest time the next drip may play.</summary>
    [DataField]
    public TimeSpan NextDrip;

    /// <summary>When the last drip played.</summary>
    [DataField]
    public TimeSpan LastDrip;
}
