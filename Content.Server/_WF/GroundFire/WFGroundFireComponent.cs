namespace Content.Server._WF.GroundFire;

/// <summary>A tile fire on planet ground, and how many more fires its line may still start.</summary>
[RegisterComponent]
public sealed partial class WFGroundFireComponent : Component
{
    /// <summary>Fires this one and the ones it starts may still spread to between them.</summary>
    [DataField]
    public int Budget;
}
