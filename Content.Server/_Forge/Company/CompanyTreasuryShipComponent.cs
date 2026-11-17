using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Forge.Company;

/// <summary>
/// Marks a voucher and the shuttle it buys. On sale, part of the price returns to the company treasury.
/// </summary>
[RegisterComponent]
public sealed partial class CompanyTreasuryShipComponent : Component
{
    [DataField]
    public string CompanyId = string.Empty;

    [DataField]
    public float RefundPercent = 0.4f;
}
