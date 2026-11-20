namespace Content.Server._WF.BoardingAction.Components;

/// <summary>A ship round that knocks out every thruster and gyroscope on the hull it hits.</summary>
[RegisterComponent]
public sealed partial class WFEngineDisruptorComponent : Component
{
    /// <summary>How long the engines stay dead.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);
}
