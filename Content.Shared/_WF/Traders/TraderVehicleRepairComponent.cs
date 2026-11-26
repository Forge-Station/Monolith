namespace Content.Shared._WF.Traders;

/// <summary>
/// Lets a trader repair the customer's vehicle parked near him, priced by how badly it is hurt and what it is worth.
/// </summary>
[RegisterComponent]
public sealed partial class TraderVehicleRepairComponent : Component
{
    /// <summary>How far from the trader, in metres, a vehicle may be parked.</summary>
    [DataField]
    public float Range = 16f;

    /// <summary>The share of the vehicle's appraised value a full rebuild costs; less damage costs that much less.</summary>
    [DataField]
    public float ValueShare = 0.3f;

    /// <summary>The least any repair costs.</summary>
    [DataField]
    public int MinimumPrice = 100;

    /// <summary>The vehicle the customer is being asked to accept a quote for.</summary>
    [ViewVariables]
    public EntityUid? QuotedVehicle;
}
