using Robust.Shared.Prototypes;
using GagsColors = Content.Shared._WF.Gags.WFGagsColors;

namespace Content.Shared.Preferences.Loadouts;

public sealed partial class Loadout
{
    /// <summary>
    /// Colours the player picked for the loadout's recolourable item, or null for its own. Not part of
    /// <see cref="Equals(Loadout?)"/>, so a loadout is found by its prototype whatever its colours.
    /// </summary>
    [DataField]
    public List<Color>? WFGagsColors;
}

public sealed partial class RoleLoadout
{
    /// <summary>Whether two lists of the same loadouts also carry the same colours.</summary>
    private static bool WFGagsColorsEqual(List<Loadout> a, List<Loadout> b)
    {
        for (var i = 0; i < a.Count && i < b.Count; i++)
        {
            if (!GagsColors.Same(a[i].WFGagsColors, b[i].WFGagsColors))
                return false;
        }

        return true;
    }

    /// <summary>Caps and makes opaque every picked colour, as a client may send anything.</summary>
    private void SanitizeWFGagsColors()
    {
        foreach (var loadouts in SelectedLoadouts.Values)
        {
            foreach (var loadout in loadouts)
            {
                loadout.WFGagsColors = GagsColors.Sanitize(loadout.WFGagsColors);
            }
        }
    }

    /// <summary>Sets the colours of a selected loadout, replacing it so copies of this loadout keep theirs.</summary>
    public void SetWFGagsColors(
        ProtoId<LoadoutGroupPrototype> group,
        ProtoId<LoadoutPrototype> loadout,
        List<Color>? colors)
    {
        if (!SelectedLoadouts.TryGetValue(group, out var loadouts))
            return;

        var index = loadouts.FindIndex(l => l.Prototype == loadout);
        if (index < 0)
            return;

        loadouts[index] = new Loadout
        {
            Prototype = loadouts[index].Prototype,
            WFGagsColors = GagsColors.Sanitize(colors),
        };
    }
}
