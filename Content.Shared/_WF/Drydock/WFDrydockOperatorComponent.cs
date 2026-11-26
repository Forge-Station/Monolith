namespace Content.Shared._WF.Drydock;

/// <summary>
/// Drydock operator: an NPC or terminal that books berths for callers. Work starts at the berth's console once the ship arrives.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockOperatorComponent : Component
{
    /// <summary>Seconds a booking holds before it lapses.</summary>
    [DataField]
    public float BookingTime = 300f;

    /// <summary>Seconds the ship must hold still inside its berth before work starts.</summary>
    [DataField]
    public float SettleTime = 2f;

    /// <summary>Tiles added around the ship for the parking frame.</summary>
    [DataField]
    public float FrameMargin = 1f;

    /// <summary>Actors viewing the operator through a helm, mapped to that helm.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, EntityUid> Viewers = new();
}
