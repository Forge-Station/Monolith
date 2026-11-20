namespace Content.Shared._NF.Bank.Components;

/// <summary>
/// This is used for applying a pricing modifier to things like vending machines.
/// It's used to ensure that a purchased product costs more than it is actually worth.
/// </summary>
[RegisterComponent]
public sealed partial class MarketModifierComponent : Component
{
    /// <summary>
    /// The amount to multiply an item's price by
    /// </summary>
    [DataField(required: true)]
    public float Mod { get; set; } = 1.0f;

    /// <summary>
    /// True if the modifier is for purchase (e.g. on a vendor)
    /// Currently used for examine strings.
    /// </summary>
    [DataField]
    public bool Buy { get; set; } = true;

    // WOLFGATE(Mining) START: a console can state its rate in its description instead
    /// <summary>Whether examining leaves out the rate line.</summary>
    [DataField]
    public bool HideExamine { get; set; }
    // WOLFGATE END
}
