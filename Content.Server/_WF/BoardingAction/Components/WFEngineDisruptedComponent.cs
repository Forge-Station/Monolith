namespace Content.Server._WF.BoardingAction.Components;

/// <summary>A hull whose engines a disruptor has knocked out.</summary>
[RegisterComponent]
public sealed partial class WFEngineDisruptedComponent : Component
{
    /// <summary>When the engines come back.</summary>
    [ViewVariables]
    public TimeSpan Until;
}
