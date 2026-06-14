using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences.Loadouts;

public sealed partial class LoadoutPrototype
{
    /// <summary>
    /// Groups loadout variants (e.g. color options) into a single expandable row in the character editor.
    /// </summary>
    [DataField]
    public string? GroupBy;
}
