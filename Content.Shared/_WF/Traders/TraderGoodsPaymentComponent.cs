using Content.Shared.Whitelist;

namespace Content.Shared._WF.Traders;

/// <summary>
/// Lets a trader take goods on the counter as payment, such as organs, each worth a share of its appraised value.
/// Cash covers the bill first; goods are taken only for what it leaves, and anything over comes back as change.
/// </summary>
[RegisterComponent]
public sealed partial class TraderGoodsPaymentComponent : Component
{
    /// <summary>What he takes.</summary>
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    /// <summary>What he never takes, even when it passes the whitelist.</summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>The share of an item's appraised value it counts for.</summary>
    [DataField]
    public float Rate = 0.5f;
}
