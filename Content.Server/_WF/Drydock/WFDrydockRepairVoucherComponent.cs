using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// A voucher that pays for one full repair at a drydock that takes credits, used up when the work starts. One issued
/// to a ship repairs only that ship.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockRepairVoucherComponent : Component
{
    /// <summary>What is handed back if the job aborts before finishing.</summary>
    [DataField]
    public EntProtoId? Returned;

    /// <summary>The only ship it repairs, or null for any ship.</summary>
    [ViewVariables]
    public EntityUid? Ship;

    /// <summary>The ship's name when it was issued, for examining.</summary>
    [ViewVariables]
    public string? ShipName;
}
