using Content.Shared.Guidebook;

namespace Content.Shared._Forge.ShipShieldPower;

/// <summary>
/// Dedicated shield power bank (ИП). Supplies only <see cref="ShieldPowerConsumerComponent"/> loads on the same grid.
/// Does not participate in Pow3r / APC networks.
/// </summary>
[RegisterComponent]
public sealed partial class ShieldPowerSourceComponent : Component
{
    /// <summary>
    /// Maximum continuous discharge to shield emitters, in watts.
    /// </summary>
    [DataField, GuidebookData]
    public float MaxDischarge = 200_000f;

    /// <summary>
    /// Passive joules/second restored when the bank is not empty and enabled.
    /// Represents dock / capacitor recovery without touching the ship APC bus.
    /// </summary>
    [DataField, GuidebookData]
    public float IdleRechargeRate = 25_000f;

    /// <summary>
    /// Fraction of desired draw that must be met for consumers to count as powered.
    /// </summary>
    [DataField]
    public float PoweredThreshold = 0.95f;

    /// <summary>
    /// When false, the bank does not supply or idle-recharge.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Last tick aggregate shield draw (watts). Examine / UI.
    /// </summary>
    [ViewVariables]
    public float LastTotalDraw;

    /// <summary>
    /// Last tick supply ratio (0–1).
    /// </summary>
    [ViewVariables]
    public float LastSupplyRatio = 1f;

    /// <summary>
    /// Cached powered appearance state to avoid spamming <c>PowerChangedEvent</c>.
    /// </summary>
    [ViewVariables]
    public bool LastPowered;
}
