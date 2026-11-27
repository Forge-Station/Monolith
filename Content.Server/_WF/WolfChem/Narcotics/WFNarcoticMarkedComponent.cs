namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>On an item <see cref="WFNarcoticGoodsSystem"/> marked as a narcotic in play, so only those are unmarked again.</summary>
[RegisterComponent]
public sealed partial class WFNarcoticMarkedComponent : Component
{
    /// <summary>The mark components the item lacked and was given.</summary>
    [ViewVariables]
    public List<string> Added = new();
}
