namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>Marks a narcotic so <see cref="WFNarcoticStockSystem"/> counts it toward the stock aboard a ship. Part of <c>WFNarcoticMarks</c>.</summary>
[RegisterComponent]
public sealed partial class WFNarcoticStockComponent : Component
{
}

/// <summary>On the narcotic that shows a ship's stock on radar.</summary>
[RegisterComponent]
public sealed partial class WFNarcoticSignatureComponent : Component
{
    /// <summary>Whether the radar blip was put on with the signature, and so comes off with it.</summary>
    [DataField]
    public bool OwnBlip;
}
