namespace Content.Shared.Guidebook;

public partial class GuideEntry
{
    /// <summary>
    /// This entry is a chapter of the lore book, not the guidebook. Its children belong to the lore book too.
    /// </summary>
    [DataField]
    public bool Lore;

    /// <summary>How much reading this lore page unsettles the open book, 0 to 1. The book corrupts as it builds up.</summary>
    [DataField]
    public float Unease;
}
